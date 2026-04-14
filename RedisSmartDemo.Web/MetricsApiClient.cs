namespace RedisSmartDemo.Web;

public class MetricsApiClient(HttpClient httpClient)
{
    public async Task<MetricPoint[]> GetMetricAsync(string name, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MetricPoint[]>($"/metrics/{name}", cancellationToken) ?? [];
    }
}

public sealed record MetricPoint(DateTime Timestamp, decimal Value);
