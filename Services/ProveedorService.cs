using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class ProveedorService
{
    private readonly IMongoCollection<Proveedor> _proveedores;
    private readonly IMongoCollection<Producto> _productos;

    public ProveedorService(MongoService mongoService)
    {
        _proveedores = mongoService.GetCollection<Proveedor>("proveedores");
        _productos = mongoService.GetCollection<Producto>("productos");
    }
    // Consulta Select
    public async Task<Result<List<Proveedor>>> GetAllAsync()
    {
        try
        {
            var proveedores = await _proveedores.Find(proveedor => proveedor.Activo).ToListAsync();
            return Result<List<Proveedor>>.Ok(proveedores);
        }
        catch (Exception exception)
        {
            return Result<List<Proveedor>>.Fail("No se pudieron obtener los proveedores.", exception.Message);
        }
    }

    public async Task<Result<Proveedor>> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Proveedor>.Fail("El identificador del proveedor no es válido.");
        }

        try
        {
            var proveedor = await _proveedores
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return proveedor is null
                ? Result<Proveedor>.Fail("El proveedor no existe o está inactivo.")
                : Result<Proveedor>.Ok(proveedor);
        }
        catch (Exception exception)
        {
            return Result<Proveedor>.Fail("No se pudo obtener el proveedor.", exception.Message);
        }
    }

    // Insertar datos en la base de datos
    public async Task<Result<Proveedor>> CreateAsync(Proveedor proveedor)
    {
        var validation = await ValidateAsync(proveedor);
        if (validation is not null)
        {
            return Result<Proveedor>.Fail(validation);
        }

        try
        {
            var nitUpper = proveedor.Nit.ToUpperInvariant();
            var emailLower = proveedor.Email.ToLowerInvariant();

            var duplicate = await _proveedores
                .Find(item => (item.Nit.ToUpper() == nitUpper || (emailLower != "" && item.Email.ToLower() == emailLower)) && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Proveedor>.Fail("Ya existe un proveedor activo con ese NIT o email.");
            }

            proveedor.Activo = true;
            proveedor.Version = 1;
            proveedor.ProductosIds = (proveedor.ProductosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            await _proveedores.InsertOneAsync(proveedor);

            foreach (var productoId in proveedor.ProductosIds)
            {
                var update = await _productos.UpdateOneAsync(
                    item => item.Id == productoId && item.Activo,
                    Builders<Producto>.Update.AddToSet(item => item.ProveedoresIds, proveedor.Id));

                if (!update.IsAcknowledged || update.MatchedCount != 1)
                {
                    await _productos.UpdateManyAsync(
                        item => item.ProveedoresIds.Contains(proveedor.Id),
                        Builders<Producto>.Update.Pull(item => item.ProveedoresIds, proveedor.Id));
                    await _proveedores.DeleteOneAsync(item => item.Id == proveedor.Id);
                    return Result<Proveedor>.Fail("No se pudo sincronizar un producto con el proveedor.");
                }
            }

            return Result<Proveedor>.Ok(proveedor, "Proveedor creado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Proveedor>.Fail("No se pudo crear el proveedor.", exception.Message);
        }
    }
    // Actualizar datos
    public async Task<Result<Proveedor>> UpdateAsync(string id, Proveedor proveedor)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Proveedor>.Fail("El identificador del proveedor no es válido.");
        }

        var validation = await ValidateAsync(proveedor, id);
        if (validation is not null)
        {
            return Result<Proveedor>.Fail(validation);
        }

        try
        {
            var current = await _proveedores.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Proveedor>.Fail("El proveedor no existe o está inactivo.");
            }

            var nitUpper = proveedor.Nit.ToUpperInvariant();
            var emailLower = proveedor.Email.ToLowerInvariant();

            var duplicate = await _proveedores
                .Find(item => item.Id != id && (item.Nit.ToUpper() == nitUpper || (emailLower != "" && item.Email.ToLower() == emailLower)) && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Proveedor>.Fail("Ya existe otro proveedor activo con ese NIT o email.");
            }

            if (proveedor.Version != 0 && proveedor.Version != current.Version)
            {
                return Result<Proveedor>.Fail("No se pudo actualizar el proveedor debido a un conflicto de concurrencia.");
            }

            proveedor.Id = id;
            proveedor.Activo = current.Activo;
            proveedor.Version = current.Version + 1;
            proveedor.ProductosIds = (proveedor.ProductosIds ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x) && ObjectId.TryParse(x, out _))
                .Distinct()
                .ToList();
            var result = await _proveedores.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, proveedor);

            if (!result.IsAcknowledged || result.MatchedCount != 1)
            {
                return Result<Proveedor>.Fail("No se pudo actualizar el proveedor debido a un conflicto de concurrencia o modificación previa.");
            }

            foreach (var productoId in current.ProductosIds.Except(proveedor.ProductosIds))
            {
                await _productos.UpdateOneAsync(
                    item => item.Id == productoId,
                    Builders<Producto>.Update.Pull(item => item.ProveedoresIds, id));
            }

            foreach (var productoId in proveedor.ProductosIds.Except(current.ProductosIds))
            {
                await _productos.UpdateOneAsync(
                    item => item.Id == productoId && item.Activo,
                    Builders<Producto>.Update.AddToSet(item => item.ProveedoresIds, id));
            }

            return Result<Proveedor>.Ok(proveedor, "Proveedor actualizado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Proveedor>.Fail("No se pudo actualizar el proveedor.", exception.Message);
        }
    }
    // Eliminar datos
    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result.Fail("El identificador del proveedor no es válido.");
        }

        try
        {
            var result = await _proveedores.UpdateOneAsync(
                item => item.Id == id && item.Activo,
                Builders<Proveedor>.Update.Set(item => item.Activo, false));

            if (!result.IsAcknowledged || result.ModifiedCount != 1)
            {
                return Result.Fail("El proveedor no existe o ya está inactivo.");
            }

            await _productos.UpdateManyAsync(
                item => item.ProveedoresIds.Contains(id),
                Builders<Producto>.Update.Pull(item => item.ProveedoresIds, id));

            return Result.Ok("Proveedor eliminado lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente el proveedor.", exception.Message);
        }
    }

    public async Task<Result<List<Producto>>> GetProductsAsync(string proveedorId)
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

    private async Task<string?> ValidateAsync(Proveedor proveedor, string? currentId = null)
    {
        if (proveedor is null)
        {
            return "El proveedor es obligatorio.";
        }

        proveedor.Nombre = InputValidation.Sanitize(proveedor.Nombre);
        proveedor.Nit = InputValidation.Sanitize(proveedor.Nit);
        proveedor.Contacto = InputValidation.Sanitize(proveedor.Contacto);
        proveedor.Telefono = InputValidation.Sanitize(proveedor.Telefono);
        proveedor.Email = InputValidation.Sanitize(proveedor.Email).ToLowerInvariant();
        proveedor.Direccion = InputValidation.Sanitize(proveedor.Direccion);
        proveedor.Pais = InputValidation.Sanitize(proveedor.Pais);

        if (string.IsNullOrWhiteSpace(proveedor.Nombre) || string.IsNullOrWhiteSpace(proveedor.Nit))
        {
            return "El nombre y el NIT son obligatorios.";
        }

        if (!InputValidation.IsValidNit(proveedor.Nit))
        {
            return "El formato del NIT no es válido.";
        }

        if (!string.IsNullOrWhiteSpace(proveedor.Email) && !InputValidation.IsValidEmail(proveedor.Email))
        {
            return "El formato del correo electrónico no es válido.";
        }

        if (!string.IsNullOrWhiteSpace(proveedor.Telefono) && !InputValidation.IsValidPhone(proveedor.Telefono))
        {
            return "El formato del teléfono no es válido.";
        }

        if (InputValidation.Exceeds(proveedor.Nombre, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(proveedor.Nit, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(proveedor.Contacto, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(proveedor.Telefono, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(proveedor.Direccion, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(proveedor.Pais, InputValidation.ShortTextMaxLength))
        {
            return "Los datos cortos del proveedor no pueden superar 200 caracteres.";
        }

        if (InputValidation.Exceeds(proveedor.Email, InputValidation.EmailMaxLength))
        {
            return "El email no puede superar 254 caracteres.";
        }

        proveedor.ProductosIds = (proveedor.ProductosIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id) && ObjectId.TryParse(id, out _))
            .Distinct()
            .ToList();

        if (proveedor.ProductosIds.Count > 0)
        {
            var productCount = await _productos
                .Find(item => proveedor.ProductosIds.Contains(item.Id) && item.Activo)
                .CountDocumentsAsync();

            if (productCount != proveedor.ProductosIds.Count)
            {
                return "Uno o más productos no existen o están inactivos.";
            }
        }

        return null;
    }
}
