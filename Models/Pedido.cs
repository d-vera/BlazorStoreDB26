using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Pedido
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("clienteId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ClienteId { get; set; } = string.Empty;

    [BsonElement("fechaPedido")]
    public DateTime FechaPedido { get; set; }

    [BsonElement("estado")]
    public string Estado { get; set; } = string.Empty;

    [BsonElement("total")]
    public decimal Total { get; set; }

    [BsonElement("metodoPago")]
    public string MetodoPago { get; set; } = string.Empty;

    [BsonElement("direccionEnvio")]
    public string DireccionEnvio { get; set; } = string.Empty;

    [BsonElement("productosIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> ProductosIds { get; set; } = [];

    [BsonElement("detalles")]
    public List<DetallePedido> Detalles { get; set; } = [];

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
