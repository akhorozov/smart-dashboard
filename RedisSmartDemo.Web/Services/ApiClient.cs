using RedisSmartDemo.Web.Models;
using System.Net.Http.Json;

namespace RedisSmartDemo.Web.Services;

public class ApiClient(HttpClient http)
{
    // ── Users ─────────────────────────────────────────────────────────────
    public Task<List<User>?> GetUsersAsync() =>
        http.GetFromJsonAsync<List<User>>("users");

    public Task<User?> GetUserAsync(string id) =>
        http.GetFromJsonAsync<User>($"users/{id}");

    public async Task<User?> CreateUserAsync(User user)
    {
        var r = await http.PostAsJsonAsync("users", user);
        return await r.Content.ReadFromJsonAsync<User>();
    }

    public async Task<User?> UpdateUserAsync(string id, User user)
    {
        var r = await http.PutAsJsonAsync($"users/{id}", user);
        return await r.Content.ReadFromJsonAsync<User>();
    }

    public Task DeleteUserAsync(string id) => http.DeleteAsync($"users/{id}");

    // ── Products ──────────────────────────────────────────────────────────
    public Task<List<Product>?> SearchProductsAsync(string? q = null, string? category = null, decimal? minPrice = null, decimal? maxPrice = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(q))        qs.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrEmpty(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (minPrice.HasValue)               qs.Add($"minPrice={minPrice}");
        if (maxPrice.HasValue)               qs.Add($"maxPrice={maxPrice}");
        var url = qs.Count > 0 ? "products?" + string.Join("&", qs) : "products";
        return http.GetFromJsonAsync<List<Product>>(url);
    }

    public Task<Product?> GetProductAsync(string id) =>
        http.GetFromJsonAsync<Product>($"products/{id}");

    public async Task<Product?> CreateProductAsync(Product product)
    {
        var r = await http.PostAsJsonAsync("products", product);
        return await r.Content.ReadFromJsonAsync<Product>();
    }

    public async Task<Product?> UpdateProductAsync(string id, Product product)
    {
        var r = await http.PutAsJsonAsync($"products/{id}", product);
        return await r.Content.ReadFromJsonAsync<Product>();
    }

    public Task DeleteProductAsync(string id) => http.DeleteAsync($"products/{id}");

    public Task EmbedProductAsync(string productId) =>
        http.PostAsync($"recommendations/embed/{productId}", null);

    // ── Metrics ───────────────────────────────────────────────────────────
    public Task<Dictionary<string, List<MetricDataPoint>>?> GetAllMetricsAsync() =>
        http.GetFromJsonAsync<Dictionary<string, List<MetricDataPoint>>>("metrics");

    // ── Activity ──────────────────────────────────────────────────────────
    public Task<List<ActivityEvent>?> GetRecentActivityAsync(int count = 30) =>
        http.GetFromJsonAsync<List<ActivityEvent>>($"activity/recent?count={count}");

    // ── Recommendations ───────────────────────────────────────────────────
    public Task<List<Recommendation>?> GetRecommendationsAsync(string userId, int count = 5) =>
        http.GetFromJsonAsync<List<Recommendation>>($"recommendations/{userId}?count={count}");

    // ── Bloom filter ──────────────────────────────────────────────────────
    public async Task<BloomCheckResult?> CheckEmailAsync(string email)
    {
        var r = await http.PostAsJsonAsync("bloom/check-email", new { email });
        return await r.Content.ReadFromJsonAsync<BloomCheckResult>();
    }

    public async Task RegisterEmailAsync(string email) =>
        await http.PostAsJsonAsync("bloom/register-email", new { email });

    public async Task<ProductViewResult?> CheckProductViewAsync(string userId, string productId)
    {
        var r = await http.PostAsJsonAsync("bloom/check-product-view", new { userId, productId });
        return await r.Content.ReadFromJsonAsync<ProductViewResult>();
    }

    public Task RecordProductViewAsync(string userId, string productId) =>
        http.PostAsJsonAsync("bloom/record-product-view", new { userId, productId });

    // ── Rate limiting ─────────────────────────────────────────────────────
    public Task<RateLimitResult?> CheckRateLimitAsync(string userId) =>
        http.GetFromJsonAsync<RateLimitResult>($"ratelimit/check/{userId}");

    // ── Cache ─────────────────────────────────────────────────────────────
    public Task<CacheStats?> GetCacheStatsAsync() =>
        http.GetFromJsonAsync<CacheStats>("cache/stats");

    public Task ClearCacheKeyAsync(string key) => http.DeleteAsync($"cache/{key}");

    public Task ClearAllCacheAsync() => http.DeleteAsync("cache");
}
