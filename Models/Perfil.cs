using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Perfil
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("clienteId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ClienteId { get; set; }

    [BsonElement("direccion")]
    public string Direccion { get; set; } = string.Empty;

    [BsonElement("ciudad")]
    public string Ciudad { get; set; } = string.Empty;

    [BsonElement("pais")]
    public string Pais { get; set; } = string.Empty;

    [BsonElement("fechaNacimiento")]
    public DateTime FechaNacimiento { get; set; }

    [BsonElement("preferencias")]
    public List<string> Preferencias { get; set; } = [];

    [BsonElement("puntosFidelidad")]
    public int PuntosFidelidad { get; set; }

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
