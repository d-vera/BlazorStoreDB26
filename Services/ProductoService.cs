using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class ProductoService
{
    private readonly IMongoCollection<Producto> _productos;
    private readonly IMongoCollection<Categoria> _categorias;
    private readonly IMongoCollection<Proveedor> _proveedores;
    private readonly IMongoCollection<Pedido> _pedidos;

    public ProductoService(MongoService mongoService)
    {
        _productos = mongoService.GetCollection<Producto>("productos");
        _categorias = mongoService.GetCollection<Categoria>("categorias");
        _proveedores = mongoService.GetCollection<Proveedor>("proveedores");
        _pedidos = mongoService.GetCollection<Pedido>("pedidos");
    }
    // Consulta Select
    public async Task<Result<List<Producto>>> GetAllAsync()
    {
        try
        {
            var productos = await _productos.Find(producto => producto.Activo).ToListAsync();
            return Result<List<Producto>>.Ok(productos);
        }
        catch (Exception exception)
        {
            return Result<List<Producto>>.Fail("No se pudieron obtener los productos.", exception.Message);
        }
    }

    public async Task<Result<Producto>> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Producto>.Fail("El identificador del producto no es válido.");
        }

        try
        {
            var producto = await _productos
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return producto is null
                ? Result<Producto>.Fail("El producto no existe o está inactivo.")
                : Result<Producto>.Ok(producto);
        }
        catch (Exception exception)
        {
            return Result<Producto>.Fail("No se pudo obtener el producto.", exception.Message);
        }
    }
    // Insertar datos en la base de datos
    public async Task<Result<Producto>> CreateAsync(Producto producto)
    {
        var validation = await ValidateAsync(producto);
        if (validation is not null)
        {
            return Result<Producto>.Fail(validation);
        }

        try
        {
            producto.Activo = true;
            producto.Version = 1;
            producto.ProveedoresIds = (producto.ProveedoresIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            producto.PedidosIds = (producto.PedidosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            await _productos.InsertOneAsync(producto);

            var categoryUpdate = await _categorias.UpdateOneAsync(
                item => item.Id == producto.CategoriaId && item.Activo,
                Builders<Categoria>.Update.AddToSet(item => item.ProductosIds, producto.Id));

            if (!categoryUpdate.IsAcknowledged || categoryUpdate.MatchedCount != 1)
            {
                await _productos.DeleteOneAsync(item => item.Id == producto.Id);
                return Result<Producto>.Fail("No se pudo sincronizar la categoría del producto.");
            }

            foreach (var proveedorId in producto.ProveedoresIds)
            {
                var providerUpdate = await _proveedores.UpdateOneAsync(
                    item => item.Id == proveedorId && item.Activo,
                    Builders<Proveedor>.Update.AddToSet(item => item.ProductosIds, producto.Id));

                if (!providerUpdate.IsAcknowledged || providerUpdate.MatchedCount != 1)
                {
                    await _categorias.UpdateOneAsync(
                        item => item.Id == producto.CategoriaId,
                        Builders<Categoria>.Update.Pull(item => item.ProductosIds, producto.Id));
                    await _proveedores.UpdateManyAsync(
                        item => item.ProductosIds.Contains(producto.Id),
                        Builders<Proveedor>.Update.Pull(item => item.ProductosIds, producto.Id));
                    await _productos.DeleteOneAsync(item => item.Id == producto.Id);
                    return Result<Producto>.Fail("No se pudo sincronizar los proveedores del producto.");
                }
            }

            return Result<Producto>.Ok(producto, "Producto creado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Producto>.Fail("No se pudo crear el producto.", exception.Message);
        }
    }

    // Actualizar datos
    public async Task<Result<Producto>> UpdateAsync(string id, Producto producto)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Producto>.Fail("El identificador del producto no es válido.");
        }

        var validation = await ValidateAsync(producto, id);
        if (validation is not null)
        {
            return Result<Producto>.Fail(validation);
        }

        try
        {
            var current = await _productos.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Producto>.Fail("El producto no existe o está inactivo.");
            }

            if (producto.Version != 0 && producto.Version != current.Version)
            {
                return Result<Producto>.Fail("No se pudo actualizar el producto debido a un conflicto de concurrencia.");
            }

            producto.Id = id;
            producto.Activo = current.Activo;
            producto.Version = current.Version + 1;
            producto.PedidosIds = (current.PedidosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            producto.ProveedoresIds = (producto.ProveedoresIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();

            var result = await _productos.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, producto);

            if (!result.IsAcknowledged || result.MatchedCount != 1)
            {
                return Result<Producto>.Fail("No se pudo actualizar el producto debido a un conflicto de concurrencia o modificación previa.");
            }

            if (current.CategoriaId != producto.CategoriaId)
            {
                await _categorias.UpdateOneAsync(
                    item => item.Id == current.CategoriaId,
                    Builders<Categoria>.Update.Pull(item => item.ProductosIds, id));
                await _categorias.UpdateOneAsync(
                    item => item.Id == producto.CategoriaId && item.Activo,
                    Builders<Categoria>.Update.AddToSet(item => item.ProductosIds, id));
            }

            var removedProviders = current.ProveedoresIds.Except(producto.ProveedoresIds);
            foreach (var proveedorId in removedProviders)
            {
                await _proveedores.UpdateOneAsync(
                    item => item.Id == proveedorId,
                    Builders<Proveedor>.Update.Pull(item => item.ProductosIds, id));
            }

            var addedProviders = producto.ProveedoresIds.Except(current.ProveedoresIds);
            foreach (var proveedorId in addedProviders)
            {
                await _proveedores.UpdateOneAsync(
                    item => item.Id == proveedorId && item.Activo,
                    Builders<Proveedor>.Update.AddToSet(item => item.ProductosIds, id));
            }

            return Result<Producto>.Ok(producto, "Producto actualizado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Producto>.Fail("No se pudo actualizar el producto.", exception.Message);
        }
    }
    
    // Eliminar datos
    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result.Fail("El identificador del producto no es válido.");
        }

        try
        {
            var producto = await _productos.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (producto is null)
            {
                return Result.Fail("El producto no existe o ya está inactivo.");
            }

            var hasActiveOrders = await _pedidos
                .Find(item => item.ProductosIds.Contains(id) && item.Activo)
                .AnyAsync();

            if (hasActiveOrders)
            {
                return Result.Fail("No se puede eliminar el producto porque pertenece a uno o más pedidos activos.");
            }

            await _productos.UpdateOneAsync(
                item => item.Id == id,
                Builders<Producto>.Update.Set(item => item.Activo, false));
            await _categorias.UpdateOneAsync(
                item => item.Id == producto.CategoriaId,
                Builders<Categoria>.Update.Pull(item => item.ProductosIds, id));
            await _proveedores.UpdateManyAsync(
                item => item.ProductosIds.Contains(id),
                Builders<Proveedor>.Update.Pull(item => item.ProductosIds, id));

            return Result.Ok("Producto eliminado lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente el producto.", exception.Message);
        }
    }

    // Query Select con Filtro
    public async Task<Result<List<Producto>>> GetByCategoriaAsync(string categoriaId)
    {
        if (!ObjectId.TryParse(categoriaId, out _))
        {
            return Result<List<Producto>>.Fail("El identificador de la categoría no es válido.");
        }

        try
        {
            var productos = await _productos
                .Find(item => item.CategoriaId == categoriaId && item.Activo)
                .ToListAsync();
            return Result<List<Producto>>.Ok(productos);
        }
        catch (Exception exception)
        {
            return Result<List<Producto>>.Fail("No se pudieron obtener los productos de la categoría.", exception.Message);
        }
    }
    // Query Select con Filtro
    public async Task<Result<List<Producto>>> GetByProveedorAsync(string proveedorId)
    {
        if (!ObjectId.TryParse(proveedorId, out _))
        {
            return Result<List<Producto>>.Fail("El identificador del proveedor no es válido.");
        }

        try
        {
            var productos = await _productos
                .Find(item => item.ProveedoresIds.Contains(proveedorId) && item.Activo)
                .ToListAsync();
            return Result<List<Producto>>.Ok(productos);
        }
        catch (Exception exception)
        {
            return Result<List<Producto>>.Fail("No se pudieron obtener los productos del proveedor.", exception.Message);
        }
    }
    // Validaciones
    private async Task<string?> ValidateAsync(Producto producto, string? currentId = null)
    {
        if (producto is null)
        {
            return "El producto es obligatorio.";
        }

        producto.Nombre = InputValidation.Sanitize(producto.Nombre);
        producto.Descripcion = InputValidation.Sanitize(producto.Descripcion);
        producto.Marca = InputValidation.Sanitize(producto.Marca);

        if (string.IsNullOrWhiteSpace(producto.Nombre))
        {
            return "El nombre del producto es obligatorio.";
        }

        if (InputValidation.Exceeds(producto.Nombre, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(producto.Marca, InputValidation.ShortTextMaxLength))
        {
            return "El nombre y la marca no pueden superar 200 caracteres.";
        }

        if (InputValidation.Exceeds(producto.Descripcion, InputValidation.DescriptionMaxLength))
        {
            return "La descripción no puede superar 2000 caracteres.";
        }

        if (producto.Precio < 0 || producto.Stock < 0)
        {
            return "El precio y el stock no pueden ser negativos.";
        }

        if (!ObjectId.TryParse(producto.CategoriaId, out _))
        {
            return "El identificador de la categoría no es válido.";
        }

        var categoria = await _categorias
            .Find(item => item.Id == producto.CategoriaId && item.Activo)
            .FirstOrDefaultAsync();
        if (categoria is null)
        {
            return "La categoría no existe o está inactiva.";
        }

        producto.ProveedoresIds = (producto.ProveedoresIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && ObjectId.TryParse(id, out _))
            .Distinct()
            .ToList();

        if (producto.ProveedoresIds.Count == 0)
        {
            return "El producto debe tener al menos un proveedor asociado.";
        }

        var providerCount = await _proveedores
            .Find(item => producto.ProveedoresIds.Contains(item.Id) && item.Activo)
            .CountDocumentsAsync();

        if (providerCount != producto.ProveedoresIds.Count)
        {
            return "Uno o más proveedores no existen o están inactivos.";
        }

        return null;
    }
}
