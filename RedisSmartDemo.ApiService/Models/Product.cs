namespace RedisSmartDemo.Api.Models;

public class Product
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
