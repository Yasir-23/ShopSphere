namespace ShopSphere.Services.Catalog.Api.Configuration;

public class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; set; } = "mongodb://localhost:27018";
    public string DatabaseName { get; set; } = "ShopSphere_Catalog";
    public string ProductsCollectionName { get; set; } = "Products";
    public string CategoriesCollectionName { get; set; } = "Categories";
}
