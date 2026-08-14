namespace RunningPerformance.Api.Http;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "RunningPerformance.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = Guid.TryParse(supplied, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();

        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        await next(context);
    }
}

public static class CorrelationIdHttpContextExtensions
{
    public static Guid GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.ItemName, out var value)
        && value is Guid correlationId
            ? correlationId
            : throw new InvalidOperationException("Correlation ID middleware was not executed.");
}
