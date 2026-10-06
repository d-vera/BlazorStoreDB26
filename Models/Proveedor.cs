using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Proveedor
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("nit")]
    public string Nit { get; set; } = string.Empty;

    [BsonElement("contacto")]
    public string Contacto { get; set; } = string.Empty;

    [BsonElement("telefono")]
    public string Telefono { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("direccion")]
    public string Direccion { get; set; } = string.Empty;

    [BsonElement("pais")]
    public string Pais { get; set; } = string.Empty;

    [BsonElement("productosIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> ProductosIds { get; set; } = [];

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
