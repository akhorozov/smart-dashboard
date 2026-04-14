using System.Net;
using System.Net.Http.Json;

namespace RedisSmartDemo.Web;

public class DashboardApiClient(HttpClient httpClient)
{
    public async Task<User[]> GetUsersAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<User[]>("/users", cancellationToken) ?? [];

    public async Task<Product[]> GetProductsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<Product[]>("/products", cancellationToken) ?? [];

    public async Task<CachedItemResponse<User>> GetUserAsync(string id, CancellationToken cancellationToken = default) =>
        await GetItemAsync<User>($"/users/{id}", cancellationToken);

    public async Task<CachedItemResponse<Product>> GetProductAsync(string id, CancellationToken cancellationToken = default) =>
        await GetItemAsync<Product>($"/products/{id}", cancellationToken);

    public async Task ClearCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/cache/{Uri.EscapeDataString(key)}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    private async Task<CachedItemResponse<T>> GetItemAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new CachedItemResponse<T>(null, null, false);

        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        var cacheStatus = response.Headers.TryGetValues("X-Cache", out var values)
            ? values.FirstOrDefault()
            : null;

        return new CachedItemResponse<T>(item, cacheStatus, true);
    }
}

public record CachedItemResponse<T>(T? Item, string? CacheStatus, bool Found);

public class User
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Product
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
