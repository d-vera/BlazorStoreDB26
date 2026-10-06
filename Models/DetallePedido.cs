using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class DetallePedido
{
    [BsonElement("productoId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductoId { get; set; } = string.Empty;

    [BsonElement("cantidad")]
    public int Cantidad { get; set; }

    [BsonElement("precioUnitario")]
    public decimal PrecioUnitario { get; set; }
}
