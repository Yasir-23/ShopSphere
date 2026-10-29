namespace ShopSphere.Services.Basket.Configuration;

public class CatalogServiceOptions
{
    public const string SectionName = "CatalogService";

    public string GrpcUrl { get; set; } = "http://localhost:5011";
}
