using System.Net.Http.Json;

namespace RedisSmartDemo.Web;

public class RecommendationsApiClient(HttpClient httpClient)
{
    public async Task<UserSummary[]> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<UserSummary[]>("/users", cancellationToken) ?? [];
    }

    public async Task<ProductRecommendation[]> GetRecommendationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ProductRecommendation[]>($"/recommendations/{Uri.EscapeDataString(userId)}", cancellationToken) ?? [];
    }

    public async Task<Product[]> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<Product[]>("/products", cancellationToken) ?? [];
    }

    public async Task EmbedProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/recommendations/embed", new EmbedProductRequest(productId), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public record UserSummary(string Id, string Name);

public record ProductRecommendation(string ProductId, string ProductName, double SimilarityScore);

public record Product(string Id, string Name);

public record EmbedProductRequest(string ProductId);
