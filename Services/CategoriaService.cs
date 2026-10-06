using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class CategoriaService
{
    private readonly IMongoCollection<Categoria> _categorias;
    private readonly IMongoCollection<Producto> _productos;

    public CategoriaService(MongoService mongoService)
    {
        _categorias = mongoService.GetCollection<Categoria>("categorias");
        _productos = mongoService.GetCollection<Producto>("productos");
    }

    public async Task<Result<List<Categoria>>> GetAllAsync()
    {
        try
        {
            var categorias = await _categorias.Find(categoria => categoria.Activo).ToListAsync();
            return Result<List<Categoria>>.Ok(categorias);
        }
        catch (Exception exception)
        {
            return Result<List<Categoria>>.Fail("No se pudieron obtener las categorías.", exception.Message);
        }
    }

    public async Task<Result<Categoria>> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Categoria>.Fail("El identificador de la categoría no es válido.");
        }

        try
        {
            var categoria = await _categorias
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return categoria is null
                ? Result<Categoria>.Fail("La categoría no existe o está inactiva.")
                : Result<Categoria>.Ok(categoria);
        }
        catch (Exception exception)
        {
            return Result<Categoria>.Fail("No se pudo obtener la categoría.", exception.Message);
        }
    }

    public async Task<Result<Categoria>> CreateAsync(Categoria categoria)
    {
        var validation = Validate(categoria);
        if (validation is not null)
        {
            return Result<Categoria>.Fail(validation);
        }

        try
        {
            var nombreUpper = categoria.Nombre.ToUpperInvariant();
            var codigoUpper = categoria.Codigo.ToUpperInvariant();

            var duplicate = await _categorias
                .Find(item => (item.Nombre.ToUpper() == nombreUpper || item.Codigo.ToUpper() == codigoUpper) && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Categoria>.Fail("Ya existe una categoría activa con ese nombre o código.");
            }

            categoria.Activo = true;
            categoria.Version = 1;
            categoria.ProductosIds = (categoria.ProductosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            await _categorias.InsertOneAsync(categoria);
            return Result<Categoria>.Ok(categoria, "Categoría creada correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Categoria>.Fail("No se pudo crear la categoría.", exception.Message);
        }
    }

    public async Task<Result<Categoria>> UpdateAsync(string id, Categoria categoria)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result<Categoria>.Fail("El identificador de la categoría no es válido.");
        }

        var validation = Validate(categoria);
        if (validation is not null)
        {
            return Result<Categoria>.Fail(validation);
        }

        try
        {
            var current = await _categorias.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Categoria>.Fail("La categoría no existe o está inactiva.");
            }

            var nombreUpper = categoria.Nombre.ToUpperInvariant();
            var codigoUpper = categoria.Codigo.ToUpperInvariant();

            var duplicate = await _categorias
                .Find(item => item.Id != id && (item.Nombre.ToUpper() == nombreUpper || item.Codigo.ToUpper() == codigoUpper) && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Categoria>.Fail("Ya existe otra categoría activa con ese nombre o código.");
            }

            if (categoria.Version != 0 && categoria.Version != current.Version)
            {
                return Result<Categoria>.Fail("No se pudo actualizar la categoría debido a un conflicto de concurrencia.");
            }

            categoria.Id = id;
            categoria.Activo = current.Activo;
            categoria.Version = current.Version + 1;
            categoria.ProductosIds = (current.ProductosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();

            var result = await _categorias.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, categoria);

            return !result.IsAcknowledged || result.MatchedCount != 1
                ? Result<Categoria>.Fail("No se pudo actualizar la categoría debido a un conflicto de concurrencia o modificación previa.")
                : Result<Categoria>.Ok(categoria, "Categoría actualizada correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Categoria>.Fail("No se pudo actualizar la categoría.", exception.Message);
        }
    }

    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return Result.Fail("El identificador de la categoría no es válido.");
        }

        try
        {
            var hasActiveProducts = await _productos
                .Find(item => item.CategoriaId == id && item.Activo)
                .AnyAsync();

            if (hasActiveProducts)
            {
                return Result.Fail("No se puede eliminar la categoría porque contiene productos activos.");
            }

            var result = await _categorias.UpdateOneAsync(
                item => item.Id == id && item.Activo,
                Builders<Categoria>.Update.Set(item => item.Activo, false));

            if (!result.IsAcknowledged || result.ModifiedCount != 1)
            {
                return Result.Fail("La categoría no existe o ya está inactiva.");
            }

            await _productos.UpdateManyAsync(
                item => item.CategoriaId == id,
                Builders<Producto>.Update.Unset("categoriaId"));
            await _categorias.UpdateOneAsync(
                item => item.Id == id,
                Builders<Categoria>.Update.Set(item => item.ProductosIds, new List<string>()));

            return Result.Ok("Categoría eliminada lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente la categoría.", exception.Message);
        }
    }

    private static string? Validate(Categoria categoria)
    {
        if (categoria is null)
        {
            return "La categoría es obligatoria.";
        }

        categoria.Nombre = InputValidation.Sanitize(categoria.Nombre);
        categoria.Descripcion = InputValidation.Sanitize(categoria.Descripcion);
        categoria.Codigo = InputValidation.Sanitize(categoria.Codigo).ToUpperInvariant();
        categoria.Responsable = InputValidation.Sanitize(categoria.Responsable);

        if (string.IsNullOrWhiteSpace(categoria.Nombre))
        {
            return "El nombre de la categoría es obligatorio.";
        }

        if (string.IsNullOrWhiteSpace(categoria.Codigo))
        {
            return "El código de la categoría es obligatorio.";
        }

        if (InputValidation.Exceeds(categoria.Nombre, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(categoria.Codigo, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(categoria.Responsable, InputValidation.ShortTextMaxLength))
        {
            return "El nombre, código y responsable no pueden superar 200 caracteres.";
        }

        if (InputValidation.Exceeds(categoria.Descripcion, InputValidation.DescriptionMaxLength))
        {
            return "La descripción no puede superar 2000 caracteres.";
        }

        if (categoria.PorcentajeImpuesto < 0 || categoria.PorcentajeImpuesto > 100)
        {
            return "El porcentaje de impuesto debe estar entre 0 y 100.";
        }

        if (categoria.FechaCreacion.Date > DateTime.Today)
        {
            return "La fecha de creación no puede ser futura.";
        }

        return null;
    }
}
