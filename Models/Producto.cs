using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace tienda.Models;

public class Producto
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonElement("_id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [BsonElement("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [BsonElement("precio")]
    public decimal Precio { get; set; }

    [BsonElement("stock")]
    public int Stock { get; set; }

    [BsonElement("marca")]
    public string Marca { get; set; } = string.Empty;

    [BsonElement("categoriaId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string CategoriaId { get; set; } = string.Empty;

    [BsonElement("proveedoresIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> ProveedoresIds { get; set; } = [];

    [BsonElement("pedidosIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> PedidosIds { get; set; } = [];

    [BsonElement("activo")]
    public bool Activo { get; set; } = true;

    [BsonElement("version")]
    public int Version { get; set; } = 1;
}
