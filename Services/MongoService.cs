using MongoDB.Driver;

namespace tienda.Services;

public sealed class MongoService
{
    private readonly IMongoDatabase _database;

    public MongoService(IConfiguration configuration)
    {
        var connectionString = configuration["MongoDB:ConnectionString"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("MongoDB:ConnectionString no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("MongoDB:DatabaseName no está configurado.");
        }

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public IMongoDatabase Database => _database;

    public IMongoCollection<T> GetCollection<T>(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre de la colección es obligatorio.", nameof(name));
        }

        return _database.GetCollection<T>(name);
    }
}
