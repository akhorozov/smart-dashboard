namespace RedisSmartDemo.Web;

public class ApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecast[]> GetWeatherAsync(int maxItems = 10, CancellationToken cancellationToken = default)
    {
        List<WeatherForecast>? forecasts = null;

        await foreach (var forecast in httpClient.GetFromJsonAsAsyncEnumerable<WeatherForecast>("/weatherforecast", cancellationToken))
        {
            if (forecasts?.Count >= maxItems)
            {
                break;
            }

            if (forecast is not null)
            {
                forecasts ??= [];
                forecasts.Add(forecast);
            }
        }

        return forecasts?.ToArray() ?? [];
    }

    public async Task<User[]> GetUsersAsync(CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<User[]>("/users", cancellationToken) ?? [];

    public async Task<User?> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/users/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<User>(response.Content, cancellationToken);
    }

    public async Task<User> CreateUserAsync(User user, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/users", user, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<User>(response.Content, cancellationToken);
    }

    public async Task<User?> UpdateUserAsync(string id, User user, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/users/{id}", user, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<User>(response.Content, cancellationToken);
    }

    public async Task<bool> DeleteUserAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/users/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
        => await content.ReadFromJsonAsync<T>(cancellationToken)
           ?? throw new InvalidOperationException($"Response body did not contain a valid '{typeof(T).Name}'.");
}

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserPreferences Preferences { get; set; } = new();
}

public class UserPreferences
{
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "en";
}
