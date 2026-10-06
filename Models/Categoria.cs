using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Categoria
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [BsonElement("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [BsonElement("fechaCreacion")]
    public DateTime FechaCreacion { get; set; }

    [BsonElement("responsable")]
    public string Responsable { get; set; } = string.Empty;

    [BsonElement("porcentajeImpuesto")]
    public decimal PorcentajeImpuesto { get; set; }

    [BsonElement("productosIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> ProductosIds { get; set; } = [];

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
