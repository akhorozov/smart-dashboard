using RedisSmartDemo.ServiceDefaults;

namespace RedisSmartDemo.Web;

public class MetricsApiClient(HttpClient httpClient)
{
    public async Task<MetricPoint[]> GetMetricAsync(string name, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<MetricPoint[]>($"/metrics/{name}", cancellationToken) ?? [];
    }
}
