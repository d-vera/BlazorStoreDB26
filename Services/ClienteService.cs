using MongoDB.Bson;
using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public sealed class ClienteService
{
    private readonly IMongoCollection<Cliente> _clientes;
    private readonly IMongoCollection<Perfil> _perfiles;
    private readonly IMongoCollection<Pedido> _pedidos;

    public ClienteService(MongoService mongoService)
    {
        _clientes = mongoService.GetCollection<Cliente>("clientes");
        _perfiles = mongoService.GetCollection<Perfil>("perfiles");
        _pedidos = mongoService.GetCollection<Pedido>("pedidos");
    }

    public async Task<Result<List<Cliente>>> GetAllAsync()
    {
        try
        {
            var clientes = await _clientes.Find(cliente => cliente.Activo).ToListAsync();
            return Result<List<Cliente>>.Ok(clientes);
        }
        catch (Exception exception)
        {
            return Result<List<Cliente>>.Fail("No se pudieron obtener los clientes.", exception.Message);
        }
    }

    public async Task<Result<Cliente>> GetByIdAsync(string id)
    {
        if (!TryGetObjectId(id, out _))
        {
            return Result<Cliente>.Fail("El identificador del cliente no es válido.");
        }

        try
        {
            var cliente = await _clientes
                .Find(item => item.Id == id && item.Activo)
                .FirstOrDefaultAsync();

            return cliente is null
                ? Result<Cliente>.Fail("El cliente no existe o está inactivo.")
                : Result<Cliente>.Ok(cliente);
        }
        catch (Exception exception)
        {
            return Result<Cliente>.Fail("No se pudo obtener el cliente.", exception.Message);
        }
    }

    public async Task<Result<Cliente>> CreateAsync(Cliente cliente, Perfil? perfil = null)
    {
        var validation = Validate(cliente);
        if (validation is not null)
        {
            return Result<Cliente>.Fail(validation);
        }

        perfil ??= new Perfil { FechaNacimiento = DateTime.Today.AddYears(-18) };
        if (InputValidation.Exceeds(perfil.Direccion, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(perfil.Ciudad, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(perfil.Pais, InputValidation.ShortTextMaxLength) ||
            perfil.FechaNacimiento.Date > DateTime.Today ||
            perfil.FechaNacimiento.Date < DateTime.Today.AddYears(-120))
        {
            return Result<Cliente>.Fail("Revisa los datos del perfil y la fecha de nacimiento.");
        }

        var insertedProfileId = string.Empty;
        var insertedClientId = string.Empty;

        try
        {
            var docUpper = cliente.Documento.ToUpperInvariant();

            var duplicate = await _clientes
                .Find(item => item.Documento.ToUpper() == docUpper && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Cliente>.Fail("Ya existe un cliente activo con ese documento.");
            }

            if (string.IsNullOrWhiteSpace(perfil.Id))
            {
                perfil.ClienteId = null;
                perfil.Activo = true;
                perfil.Version = 1;
                await _perfiles.InsertOneAsync(perfil);
                insertedProfileId = perfil.Id;
            }
            else
            {
                var existingProfile = await _perfiles
                    .Find(item => item.Id == perfil.Id && item.Activo)
                    .FirstOrDefaultAsync();

                if (existingProfile is null)
                {
                    return Result<Cliente>.Fail("El perfil indicado no existe o está inactivo.");
                }

                if (!string.IsNullOrWhiteSpace(existingProfile.ClienteId))
                {
                    return Result<Cliente>.Fail("El perfil ya está asignado a un cliente.");
                }

                perfil = existingProfile;
            }

            cliente.PerfilId = perfil.Id;
            cliente.Activo = true;
            cliente.Version = 1;
            cliente.PedidosIds = (cliente.PedidosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();
            await _clientes.InsertOneAsync(cliente);
            insertedClientId = cliente.Id;

            var perfilUpdate = Builders<Perfil>.Update.Set(item => item.ClienteId, cliente.Id);
            var perfilResult = await _perfiles.UpdateOneAsync(
                item => item.Id == perfil.Id,
                perfilUpdate);

            if (!perfilResult.IsAcknowledged || perfilResult.ModifiedCount != 1)
            {
                await _clientes.DeleteOneAsync(item => item.Id == cliente.Id);
                if (!string.IsNullOrWhiteSpace(insertedProfileId))
                {
                    await _perfiles.DeleteOneAsync(item => item.Id == insertedProfileId);
                }
                return Result<Cliente>.Fail("No se pudo sincronizar el perfil del cliente.");
            }

            return Result<Cliente>.Ok(cliente, "Cliente creado correctamente.");
        }
        catch (Exception exception)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(insertedClientId))
                {
                    await _clientes.DeleteOneAsync(item => item.Id == insertedClientId);
                }

                if (!string.IsNullOrWhiteSpace(insertedProfileId))
                {
                    await _perfiles.DeleteOneAsync(item => item.Id == insertedProfileId);
                }
            }
            catch
            {
                // Preserve the original operation failure if compensation also fails.
            }

            return Result<Cliente>.Fail("No se pudo crear el cliente.", exception.Message);
        }
    }

    public async Task<Result<Cliente>> UpdateAsync(string id, Cliente cliente)
    {
        var validation = Validate(cliente);
        if (validation is not null)
        {
            return Result<Cliente>.Fail(validation);
        }

        if (!TryGetObjectId(id, out _))
        {
            return Result<Cliente>.Fail("El identificador del cliente no es válido.");
        }

        try
        {
            var current = await _clientes.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (current is null)
            {
                return Result<Cliente>.Fail("El cliente no existe o está inactivo.");
            }

            var docUpper = cliente.Documento.ToUpperInvariant();

            var duplicate = await _clientes
                .Find(item => item.Id != id && item.Documento.ToUpper() == docUpper && item.Activo)
                .AnyAsync();

            if (duplicate)
            {
                return Result<Cliente>.Fail("Ya existe otro cliente activo con ese documento.");
            }

            if (!string.IsNullOrWhiteSpace(cliente.PerfilId))
            {
                var perfil = await _perfiles
                    .Find(item => item.Id == cliente.PerfilId && item.Activo)
                    .FirstOrDefaultAsync();

                if (perfil is null)
                {
                    return Result<Cliente>.Fail("El perfil indicado no existe o está inactivo.");
                }

                if (!string.IsNullOrWhiteSpace(perfil.ClienteId) && perfil.ClienteId != id)
                {
                    return Result<Cliente>.Fail("El perfil ya está asignado a otro cliente.");
                }
            }

            cliente.Id = id;
            cliente.Activo = current.Activo;
            cliente.Version = current.Version + 1;
            cliente.PedidosIds = (current.PedidosIds ?? []).Where(x => ObjectId.TryParse(x, out _)).Distinct().ToList();

            var result = await _clientes.ReplaceOneAsync(item => item.Id == id && item.Version == current.Version, cliente);
            if (!result.IsAcknowledged || result.MatchedCount != 1)
            {
                return Result<Cliente>.Fail("No se pudo actualizar el cliente debido a un conflicto de concurrencia o modificación previa.");
            }

            if (current.PerfilId != cliente.PerfilId)
            {
                if (!string.IsNullOrWhiteSpace(current.PerfilId))
                {
                    await _perfiles.UpdateOneAsync(
                        item => item.Id == current.PerfilId,
                        Builders<Perfil>.Update.Set(item => item.ClienteId, null));
                }

                if (!string.IsNullOrWhiteSpace(cliente.PerfilId))
                {
                    await _perfiles.UpdateOneAsync(
                        item => item.Id == cliente.PerfilId,
                        Builders<Perfil>.Update.Set(item => item.ClienteId, id));
                }
            }

            return Result<Cliente>.Ok(cliente, "Cliente actualizado correctamente.");
        }
        catch (Exception exception)
        {
            return Result<Cliente>.Fail("No se pudo actualizar el cliente.", exception.Message);
        }
    }

    public async Task<Result> DeleteLogicoAsync(string id)
    {
        if (!TryGetObjectId(id, out _))
        {
            return Result.Fail("El identificador del cliente no es válido.");
        }

        try
        {
            var cliente = await _clientes.Find(item => item.Id == id && item.Activo).FirstOrDefaultAsync();
            if (cliente is null)
            {
                return Result.Fail("El cliente no existe o ya está inactivo.");
            }

            var hasActiveOrders = await _pedidos
                .Find(item => item.ClienteId == id && item.Activo)
                .AnyAsync();

            if (hasActiveOrders)
            {
                return Result.Fail("No se puede eliminar el cliente porque posee pedidos activos.");
            }

            await _clientes.UpdateOneAsync(
                item => item.Id == id,
                Builders<Cliente>.Update.Set(item => item.Activo, false));

            if (!string.IsNullOrWhiteSpace(cliente.PerfilId))
            {
                await _perfiles.UpdateOneAsync(
                    item => item.Id == cliente.PerfilId,
                    Builders<Perfil>.Update.Set(item => item.Activo, false));
            }

            return Result.Ok("Cliente y perfil eliminados lógicamente.");
        }
        catch (Exception exception)
        {
            return Result.Fail("No se pudo eliminar lógicamente el cliente.", exception.Message);
        }
    }

    private static string? Validate(Cliente cliente)
    {
        if (cliente is null)
        {
            return "El cliente es obligatorio.";
        }

        cliente.Nombres = InputValidation.Sanitize(cliente.Nombres);
        cliente.Apellidos = InputValidation.Sanitize(cliente.Apellidos);
        cliente.Documento = InputValidation.Sanitize(cliente.Documento);
        cliente.Email = InputValidation.Sanitize(cliente.Email).ToLowerInvariant();
        cliente.Telefono = InputValidation.Sanitize(cliente.Telefono);

        if (string.IsNullOrWhiteSpace(cliente.Nombres) || string.IsNullOrWhiteSpace(cliente.Apellidos))
        {
            return "Los nombres y apellidos son obligatorios.";
        }

        if (string.IsNullOrWhiteSpace(cliente.Documento))
        {
            return "El documento es obligatorio.";
        }

        if (!InputValidation.IsValidDocument(cliente.Documento))
        {
            return "El formato del documento no es válido.";
        }

        if (string.IsNullOrWhiteSpace(cliente.Email))
        {
            return "El email es obligatorio.";
        }

        if (!InputValidation.IsValidEmail(cliente.Email))
        {
            return "El formato del correo electrónico no es válido.";
        }

        if (!string.IsNullOrWhiteSpace(cliente.Telefono) && !InputValidation.IsValidPhone(cliente.Telefono))
        {
            return "El formato del teléfono no es válido.";
        }

        if (InputValidation.Exceeds(cliente.Nombres, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(cliente.Apellidos, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(cliente.Documento, InputValidation.ShortTextMaxLength) ||
            InputValidation.Exceeds(cliente.Telefono, InputValidation.ShortTextMaxLength))
        {
            return "Los nombres, apellidos, documento y teléfono no pueden superar 200 caracteres.";
        }

        if (InputValidation.Exceeds(cliente.Email, InputValidation.EmailMaxLength))
        {
            return "El email no puede superar 254 caracteres.";
        }

        if (cliente.FechaRegistro.Date > DateTime.Today)
        {
            return "La fecha de registro no puede ser futura.";
        }

        return null;
    }

    private static bool TryGetObjectId(string id, out ObjectId objectId)
    {
        return ObjectId.TryParse(id, out objectId);
    }
}
