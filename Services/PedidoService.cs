using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class PedidoService
{
    private readonly IMongoCollection<Pedido> _pedidos;
    private readonly IMongoCollection<Cliente> _clientes;
    private readonly IMongoCollection<Producto> _productos;

    public PedidoService(MongoService mongoService)
    {
        _pedidos = mongoService.GetCollection<Pedido>("pedidos");
        _clientes = mongoService.GetCollection<Cliente>("clientes");
        _productos = mongoService.GetCollection<Producto>("productos");
    }

    public async Task<Result<List<Pedido>>> GetAllAsync()
    {
        try
        {
            var pedidos = await _pedidos.Find(pedido => pedido.Activo).ToListAsync();
            return Result<List<Pedido>>.Ok(pedidos);
        }
        catch (Exception exception)
        {
            return Result<List<Pedido>>.Fail("No se pudieron obtener los pedidos.", exception.Message);
        }
    }

    public async Task<Result<Pedido>> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Pedido>.Fail("El identificador del pedido no es válido.");
        }

        try
        {
            var pedido = await _pedidos
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return pedido is null
                ? Result<Pedido>.Fail("El pedido no existe o está inactivo.")
                : Result<Pedido>.Ok(pedido);
        }
        catch (Exception exception)
        {
            return Result<Pedido>.Fail("No se pudo obtener el pedido.", exception.Message);
        }
    }

    public async Task<Result<Pedido>> CreateAsync(Pedido pedido)
    {
        var validation = await ValidateAsync(pedido);
        if (validation is not null)
        {
            return Result<Pedido>.Fail(validation);
        }

        try
        {
            pedido.Activo = true;
            pedido.Version = 1;
            pedido.ProductosIds = GetProductIds(pedido);
            await _pedidos.InsertOneAsync(pedido);

            var clientUpdate = await _clientes.UpdateOneAsync(
                item => item.Id == pedido.ClienteId && item.Activo,
                Builders<Cliente>.Update.AddToSet(item => item.PedidosIds, pedido.Id));

            if (!clientUpdate.IsAcknowledged || clientUpdate.MatchedCount != 1)
            {
                await _pedidos.DeleteOneAsync(item => item.Id == pedido.Id);
                return Result<Pedido>.Fail("No se pudo sincronizar el cliente con el pedido.");
            }

            foreach (var productoId in pedido.ProductosIds)
            {
                var productUpdate = await _productos.UpdateOneAsync(
                    item => item.Id == productoId && item.Activo,
                    Builders<Producto>.Update.AddToSet(item => item.PedidosIds, pedido.Id));

                if (!productUpdate.IsAcknowledged || productUpdate.MatchedCount != 1)
                {
                    await _clientes.UpdateOneAsync(
                        item => item.Id == pedido.ClienteId,
                        Builders<Cliente>.Update.Pull(item => item.PedidosIds, pedido.Id));
                    await _productos.UpdateManyAsync(
                        item => item.PedidosIds.Contains(pedido.Id),
                        Builders<Producto>.Update.Pull(item => item.PedidosIds, pedido.Id));
                    await _pedidos.DeleteOneAsync(item => item.Id == pedido.Id);
                    return Result<Pedido>.Fail("No se pudo sincronizar un producto con el pedido.");
                }
            }

            // Descontar Stock de los productos comprados
            foreach (var detalle in pedido.Detalles)
            {
                await _productos.UpdateOneAsync(
                    item => item.Id == detalle.ProductoId,
                    Builders<Producto>.Update.Inc(item => item.Stock, -detalle.Cantidad));
            }

            return Result<Pedido>.Ok(pedido, "Pedido creado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Pedido>.Fail("No se pudo crear el pedido.", exception.Message);
        }
    }

    public async Task<Result<Pedido>> UpdateAsync(string id, Pedido pedido)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Pedido>.Fail("El identificador del pedido no es válido.");
        }

        try
        {
            var current = await _pedidos.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Pedido>.Fail("El pedido no existe o está inactivo.");
            }

            if (string.Equals(current.Estado, "Entregado", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Estado, "Cancelado", StringComparison.OrdinalIgnoreCase))
            {
                return Result<Pedido>.Fail("No se puede modificar un pedido que se encuentra en estado Entregado o Cancelado.");
            }

            var validation = await ValidateAsync(pedido, current.Detalles);
            if (validation is not null)
            {
                return Result<Pedido>.Fail(validation);
            }

            pedido.Id = id;
            pedido.Activo = current.Activo;
            pedido.Version = current.Version + 1;
            pedido.ProductosIds = GetProductIds(pedido);
            var result = await _pedidos.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, pedido);

            if (!result.IsAcknowledged || result.MatchedCount != 1)
            {
                return Result<Pedido>.Fail("No se pudo actualizar el pedido debido a un conflicto de concurrencia o modificación previa.");
            }

            if (current.ClienteId != pedido.ClienteId)
            {
                await _clientes.UpdateOneAsync(
                    item => item.Id == current.ClienteId,
                    Builders<Cliente>.Update.Pull(item => item.PedidosIds, id));
                await _clientes.UpdateOneAsync(
                    item => item.Id == pedido.ClienteId && item.Activo,
                    Builders<Cliente>.Update.AddToSet(item => item.PedidosIds, id));
            }

            foreach (var productoId in current.ProductosIds.Except(pedido.ProductosIds))
            {
                await _productos.UpdateOneAsync(
                    item => item.Id == productoId,
                    Builders<Producto>.Update.Pull(item => item.PedidosIds, id));
            }

            foreach (var productoId in pedido.ProductosIds.Except(current.ProductosIds))
            {
                await _productos.UpdateOneAsync(
                    item => item.Id == productoId && item.Activo,
                    Builders<Producto>.Update.AddToSet(item => item.PedidosIds, id));
            }

            return Result<Pedido>.Ok(pedido, "Pedido actualizado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Pedido>.Fail("No se pudo actualizar el pedido.", exception.Message);
        }
    }

    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result.Fail("El identificador del pedido no es válido.");
        }

        try
        {
            var pedido = await _pedidos.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (pedido is null)
            {
                return Result.Fail("El pedido no existe o ya está inactivo.");
            }

            await _pedidos.UpdateOneAsync(
                item => item.Id == id,
                Builders<Pedido>.Update.Set(item => item.Activo, false));
            await _clientes.UpdateOneAsync(
                item => item.Id == pedido.ClienteId,
                Builders<Cliente>.Update.Pull(item => item.PedidosIds, id));
            await _productos.UpdateManyAsync(
                item => item.PedidosIds.Contains(id),
                Builders<Producto>.Update.Pull(item => item.PedidosIds, id));

            return Result.Ok("Pedido eliminado lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente el pedido.", exception.Message);
        }
    }

    private async Task<string?> ValidateAsync(Pedido pedido, IReadOnlyCollection<DetallePedido>? previousDetails = null)
    {
        if (pedido is null)
        {
            return "El pedido es obligatorio.";
        }

        pedido.Estado = InputValidation.Sanitize(pedido.Estado);
        pedido.MetodoPago = InputValidation.Sanitize(pedido.MetodoPago);
        pedido.DireccionEnvio = InputValidation.Sanitize(pedido.DireccionEnvio);

        if (string.IsNullOrWhiteSpace(pedido.Estado) || !InputValidation.AllowedPedidoEstados.Contains(pedido.Estado, StringComparer.OrdinalIgnoreCase))
        {
            return "El estado del pedido no es válido. Valores permitidos: Pendiente, Procesando, Enviado, Entregado, Cancelado.";
        }

        if (string.IsNullOrWhiteSpace(pedido.MetodoPago) || !InputValidation.AllowedMetodosPago.Contains(pedido.MetodoPago, StringComparer.OrdinalIgnoreCase))
        {
            return "El método de pago no es válido. Valores permitidos: PSE, Tarjeta de Crédito, Tarjeta de Débito, Efectivo.";
        }

        if (InputValidation.Exceeds(pedido.Estado, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(pedido.MetodoPago, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(pedido.DireccionEnvio, InputValidation.ShortTextMaxLength))
        {
            return "El estado, método de pago y dirección no pueden superar 200 caracteres.";
        }

        if (pedido.FechaPedido.Date > DateTime.Today)
        {
            return "La fecha del pedido no puede ser futura.";
        }

        if (!ObjectId.TryParse(pedido.ClienteId, out _))
        {
            return "El identificador del cliente no es válido.";
        }

        var cliente = await _clientes
            .Find(item => item.Id == pedido.ClienteId && item.Activo)
            .FirstOrDefaultAsync();
        if (cliente is null)
        {
            return "El cliente no existe o está inactivo.";
        }

        pedido.Detalles ??= [];
        pedido.ProductosIds ??= [];
        if (pedido.Detalles.Count == 0 && pedido.ProductosIds.Count > 0)
        {
            pedido.Detalles = pedido.ProductosIds
                .Distinct()
                .Select(id => new DetallePedido { ProductoId = id, Cantidad = 1 })
                .ToList();
        }

        var productIds = GetProductIds(pedido);
        if (productIds.Count == 0)
        {
            return "El pedido debe tener al menos un producto.";
        }

        if (pedido.Detalles.Any(detalle => !ObjectId.TryParse(detalle.ProductoId, out _)) ||
            productIds.Any(id => !ObjectId.TryParse(id, out _)))
        {
            return "Uno o más identificadores de producto no son válidos.";
        }

        if (pedido.Detalles.Any(detalle => detalle.Cantidad <= 0) ||
            pedido.Detalles.Select(detalle => detalle.ProductoId).Distinct().Count() != pedido.Detalles.Count)
        {
            return "Cada producto debe aparecer una sola vez y tener una cantidad mayor que cero.";
        }

        var products = await _productos
            .Find(item => productIds.Contains(item.Id) && item.Activo)
            .ToListAsync();

        if (products.Count != productIds.Count)
        {
            return "Uno o más productos no existen o están inactivos.";
        }

        // Si es un pedido nuevo, verificar stock disponible
        if (previousDetails is null)
        {
            foreach (var detail in pedido.Detalles)
            {
                var prod = products.First(p => p.Id == detail.ProductoId);
                if (prod.Stock < detail.Cantidad)
                {
                    return $"El producto '{prod.Nombre}' no cuenta con stock suficiente (Disponible: {prod.Stock}, Requerido: {detail.Cantidad}).";
                }
            }
        }

        var previousPrices = previousDetails?
            .GroupBy(detail => detail.ProductoId)
            .ToDictionary(group => group.Key, group => group.First().PrecioUnitario) ?? [];

        foreach (var detail in pedido.Detalles)
        {
            detail.PrecioUnitario = previousPrices.TryGetValue(detail.ProductoId, out var savedPrice)
                ? savedPrice
                : products.First(product => product.Id == detail.ProductoId).Precio;
        }

        pedido.ProductosIds = productIds;
        pedido.Total = pedido.Detalles.Sum(detail => detail.Cantidad * detail.PrecioUnitario);
        return null;
    }

    private static List<string> GetProductIds(Pedido pedido)
    {
        var detailIds = pedido.Detalles?
            .Select(detalle => detalle.ProductoId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList() ?? [];

        return detailIds.Count > 0
            ? detailIds
            : pedido.ProductosIds.Distinct().ToList();
    }
}
