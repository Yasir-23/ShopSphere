using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShopSphere.Services.Catalog.Api.Models;

public class Product
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("slug")]
    public string Slug { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("sku")]
    public string Sku { get; set; } = string.Empty;

    [BsonElement("price")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = "USD";

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("categoryId")]
    public string? CategoryId { get; set; }

    [BsonElement("attributes")]
    public Dictionary<string, string> Attributes { get; set; } = new();

    [BsonElement("images")]
    public List<string> Images { get; set; } = new();

    [BsonElement("isAvailable")]
    public bool IsAvailable { get; set; } = true;

    [BsonIgnore]
    public bool IsActive
    {
        get => IsAvailable;
        set => IsAvailable = value;
    }

    [BsonIgnore]
    public string ImageUrl => Images.Count > 0 ? Images[0] : string.Empty;

    [BsonElement("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
