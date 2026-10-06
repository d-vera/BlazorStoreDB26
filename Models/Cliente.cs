using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Cliente
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nombres")]
    public string Nombres { get; set; } = string.Empty;

    [BsonElement("apellidos")]
    public string Apellidos { get; set; } = string.Empty;

    [BsonElement("documento")]
    public string Documento { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("telefono")]
    public string Telefono { get; set; } = string.Empty;

    [BsonElement("fechaRegistro")]
    public DateTime FechaRegistro { get; set; }

    [BsonElement("perfilId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? PerfilId { get; set; }

    [BsonElement("pedidosIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> PedidosIds { get; set; } = [];

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
