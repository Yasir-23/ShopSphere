namespace ShopSphere.Services.Basket.Configuration;

public class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:16379";
    public int SlidingExpirationDays { get; set; } = 30;
}
