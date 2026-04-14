namespace RedisSmartDemo.Web;

public class ActivityStreamClient(HttpClient httpClient)
{
    public async Task<HttpResponseMessage> GetActivityStreamResponseAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/activity/stream");
        request.Headers.Accept.ParseAdd("text/event-stream");

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }
}
