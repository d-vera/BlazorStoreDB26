using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class PerfilService
{
    private readonly IMongoCollection<Perfil> _perfiles;
    private readonly IMongoCollection<Cliente> _clientes;

    public PerfilService(MongoService mongoService)
    {
        _perfiles = mongoService.GetCollection<Perfil>("perfiles");
        _clientes = mongoService.GetCollection<Cliente>("clientes");
    }

    public async Task<Result<List<Perfil>>> GetAllAsync()
    {
        try
        {
            var perfiles = await _perfiles.Find(perfil => perfil.Activo).ToListAsync();
            return Result<List<Perfil>>.Ok(perfiles);
        }
        catch (Exception exception)
        {
            return Result<List<Perfil>>.Fail("No se pudieron obtener los perfiles.", exception.Message);
        }
    }

    public async Task<Result<Perfil>> GetByIdAsync(string id)
    {
        if (!TryGetObjectId(id, out _))
        {
            return Result<Perfil>.Fail("El identificador del perfil no es válido.");
        }

        try
        {
            var perfil = await _perfiles
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return perfil is null
                ? Result<Perfil>.Fail("El perfil no existe o está inactivo.")
                : Result<Perfil>.Ok(perfil);
        }
        catch (Exception exception)
        {
            return Result<Perfil>.Fail("No se pudo obtener el perfil.", exception.Message);
        }
    }

    public async Task<Result<Perfil>> CreateAsync(Perfil perfil)
    {
        var validation = await ValidateReferenceAsync(perfil);
        if (validation is not null)
        {
            return Result<Perfil>.Fail(validation);
        }

        try
        {
            perfil.Activo = true;
            perfil.Version = 1;
            await _perfiles.InsertOneAsync(perfil);

            if (!string.IsNullOrWhiteSpace(perfil.ClienteId))
            {
                var update = await _clientes.UpdateOneAsync(
                    item => item.Id == perfil.ClienteId && item.Activo,
                    Builders<Cliente>.Update.Set(item => item.PerfilId, perfil.Id));

                if (!update.IsAcknowledged || update.ModifiedCount != 1)
                {
                    await _perfiles.DeleteOneAsync(item => item.Id == perfil.Id);
                    return Result<Perfil>.Fail("No se pudo sincronizar el cliente con el perfil.");
                }
            }

            return Result<Perfil>.Ok(perfil, "Perfil creado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Perfil>.Fail("No se pudo crear el perfil.", exception.Message);
        }
    }

    public async Task<Result<Perfil>> UpdateAsync(string id, Perfil perfil)
    {
        if (!TryGetObjectId(id, out _))
        {
            return Result<Perfil>.Fail("El identificador del perfil no es válido.");
        }

        perfil.Id = id;
        var validation = await ValidateReferenceAsync(perfil);
        if (validation is not null)
        {
            return Result<Perfil>.Fail(validation);
        }

        try
        {
            var current = await _perfiles.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Perfil>.Fail("El perfil no existe o está inactivo.");
            }

            perfil.Id = id;
            perfil.Activo = current.Activo;
            perfil.Version = current.Version + 1;

            var result = await _perfiles.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, perfil);
            if (!result.IsAcknowledged || result.MatchedCount != 1)
            {
                return Result<Perfil>.Fail("No se pudo actualizar el perfil debido a un conflicto de concurrencia o modificación previa.");
            }

            if (current.ClienteId != perfil.ClienteId)
            {
                if (!string.IsNullOrWhiteSpace(current.ClienteId))
                {
                    await _clientes.UpdateOneAsync(
                        item => item.Id == current.ClienteId,
                        Builders<Cliente>.Update.Set(item => item.PerfilId, null));
                }

                if (!string.IsNullOrWhiteSpace(perfil.ClienteId))
                {
                    await _clientes.UpdateOneAsync(
                        item => item.Id == perfil.ClienteId,
                        Builders<Cliente>.Update.Set(item => item.PerfilId, id));
                }
            }

            return Result<Perfil>.Ok(perfil, "Perfil actualizado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Perfil>.Fail("No se pudo actualizar el perfil.", exception.Message);
        }
    }

    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!TryGetObjectId(id, out _))
        {
            return Result.Fail("El identificador del perfil no es válido.");
        }

        try
        {
            var perfil = await _perfiles.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (perfil is null)
            {
                return Result.Fail("El perfil no existe o ya está inactivo.");
            }

            await _perfiles.UpdateOneAsync(
                item => item.Id == id,
                Builders<Perfil>.Update.Set(item => item.Activo, false));

            if (!string.IsNullOrWhiteSpace(perfil.ClienteId))
            {
                await _clientes.UpdateOneAsync(
                    item => item.Id == perfil.ClienteId,
                    Builders<Cliente>.Update.Set(item => item.PerfilId, null));

                await _perfiles.UpdateOneAsync(
                    item => item.Id == id,
                    Builders<Perfil>.Update.Set(item => item.ClienteId, null));
            }

            return Result.Ok("Perfil eliminado lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente el perfil.", exception.Message);
        }
    }

    private async Task<string?> ValidateReferenceAsync(Perfil perfil)
    {
        if (perfil is null)
        {
            return "El perfil es obligatorio.";
        }

        perfil.Direccion = InputValidation.Sanitize(perfil.Direccion);
        perfil.Ciudad = InputValidation.Sanitize(perfil.Ciudad);
        perfil.Pais = InputValidation.Sanitize(perfil.Pais);
        perfil.Preferencias = (perfil.Preferencias ?? [])
            .Select(InputValidation.Sanitize)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Take(20)
            .ToList();

        if (InputValidation.Exceeds(perfil.Direccion, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(perfil.Ciudad, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(perfil.Pais, InputValidation.ShortTextMaxLength))
        {
            return "La dirección, ciudad y país no pueden superar 200 caracteres.";
        }

        if (perfil.FechaNacimiento.Date > DateTime.Today)
        {
            return "La fecha de nacimiento no puede ser futura.";
        }

        if (perfil.FechaNacimiento.Date < DateTime.Today.AddYears(-120))
        {
            return "La fecha de nacimiento no puede tener más de 120 años.";
        }

        if (string.IsNullOrWhiteSpace(perfil.ClienteId))
        {
            return null;
        }

        if (!TryGetObjectId(perfil.ClienteId, out _))
        {
            return "El identificador del cliente no es válido.";
        }

        var cliente = await _clientes
            .Find(item => item.Id == perfil.ClienteId && item.Activo)
            .FirstOrDefaultAsync();

        if (cliente is null)
        {
            return "El cliente indicado no existe o está inactivo.";
        }

        if (!string.IsNullOrWhiteSpace(cliente.PerfilId) && cliente.PerfilId != perfil.Id)
        {
            return "El cliente ya tiene asignado otro perfil.";
        }

        var profileAlreadyAssigned = await _perfiles
            .Find(item => item.ClienteId == perfil.ClienteId && item.Id != perfil.Id && item.Activo)
            .AnyAsync();

        return profileAlreadyAssigned
            ? "El cliente ya tiene otro perfil activo."
            : null;
    }

    private static bool TryGetObjectId(string id, out ObjectId objectId)
    {
        return ObjectId.TryParse(id, out objectId);
    }
}
