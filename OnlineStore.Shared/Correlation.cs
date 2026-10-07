namespace OnlineStore.Shared;

public static class Correlation
{
    public const string Header = "X-Correlation-ID";
    private static readonly AsyncLocal<string?> _current = new();
    public static string? Current { get => _current.Value; set => _current.Value = value; }
}

/// Generates the ID at the edge (or reuses the incoming one), puts it in the log scope and the response.
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Correlation.Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
        Correlation.Current = id;
        ctx.Response.Headers[Correlation.Header] = id;
        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(ctx);
        }
    }
}

/// Attaches the current correlation ID to every outbound HttpClient call.
public class CorrelationIdHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Remove(Correlation.Header);
        request.Headers.TryAddWithoutValidation(Correlation.Header, Correlation.Current ?? Guid.NewGuid().ToString("N"));
        return base.SendAsync(request, ct);
    }
}
