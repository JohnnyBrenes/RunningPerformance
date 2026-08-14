using Npgsql;
using RunningPerformance.Api.Authentication;
using RunningPerformance.Api.Features;
using RunningPerformance.Api.Http;
using RunningPerformance.Application.FreeTier;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Jobs;
using RunningPerformance.Infrastructure.Observability;
using RunningPerformance.Infrastructure.Storage;
using RunningPerformance.Infrastructure.Sync;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Reflection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var freeTierOptions = builder.Configuration
    .GetSection(FreeTierQuotaOptions.SectionName)
    .Get<FreeTierQuotaOptions>() ?? new();
freeTierOptions.Validate();
var historicalImportOptions = builder.Configuration
    .GetSection(HistoricalImportOptions.SectionName)
    .Get<HistoricalImportOptions>() ?? new();
historicalImportOptions.Validate();
var fitIngestionOptions = builder.Configuration
    .GetSection(FitIngestionOptions.SectionName)
    .Get<FitIngestionOptions>() ?? new();
fitIngestionOptions.Validate();

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = Math.Max(
        historicalImportOptions.MaxCsvBytes,
        fitIngestionOptions.MaxFitBytes);
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
});

builder.Services.AddSingleton(freeTierOptions);
builder.Services.AddSingleton<FreeTierQuotaGuard>();
builder.Services.AddSingleton(historicalImportOptions);
builder.Services.AddSingleton(fitIngestionOptions);
builder.Services.AddSingleton<NormalizedActivityCsvValidator>();
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new NpgsqlDataSourceBuilder(
        DatabaseConfiguration.ResolveConnectionString(configuration)).Build();
});
builder.Services.AddSingleton<OwnerDataSource>();
builder.Services.AddHttpClient<SupabaseObjectStore>();
builder.Services.AddSingleton<CsvIngestionQueue>();
builder.Services.AddSingleton<FitIngestionQueue>();
builder.Services.AddSingleton<SyncCredentialService>();
builder.Services.AddSingleton<OperationalTelemetry>();
if (!isOpenApiGeneration)
{
    builder.Services.AddHostedService<IngestionWorker>();
}
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items.TryGetValue(
                CorrelationIdMiddleware.ItemName,
                out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});
builder.Services.AddSupabaseAuthentication(
    builder.Configuration,
    builder.Environment,
    isOpenApiGeneration);

var configuredOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : ["http://127.0.0.1:5173", "http://localhost:5173"];
if (builder.Environment.IsProduction() && !isOpenApiGeneration && configuredOrigins.Length == 0)
{
    throw new InvalidOperationException("CORS_ALLOWED_ORIGINS is required in production.");
}

foreach (var origin in allowedOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin)
        || !string.Equals(
            parsedOrigin.GetLeftPart(UriPartial.Authority),
            origin.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase)
        || (builder.Environment.IsProduction()
            && !isOpenApiGeneration
            && parsedOrigin.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException($"CORS origin '{origin}' is not a valid allowed origin.");
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy =>
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders(
                "Authorization",
                "Content-Type",
                "Idempotency-Key",
                CorrelationIdMiddleware.HeaderName)
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        var owner = context.User.FindFirst("sub")?.Value;
        var key = owner is not null
            ? $"owner:{owner}"
            : $"anonymous:{context.Connection.RemoteIpAddress}";
        var limit = owner is null ? 30 : 120;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = limit,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        });
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        var correlationId = context.HttpContext.GetCorrelationId();
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Demasiadas solicitudes.",
            detail: "Espera un minuto antes de volver a intentarlo.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "rate_limit_exceeded",
                ["correlationId"] = correlationId
            }).ExecuteAsync(context.HttpContext);
    };
});
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<WorkerHeartbeatHealthCheck>("worker", tags: ["ready"]);
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(OperationalTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(OperationalTelemetry.MeterName));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (app.Environment.IsProduction())
    {
        context.Response.Headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    }

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
    await next();
});
app.UseCors("Spa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/api/v1/status", (FreeTierQuotaOptions limits) => new ServiceStatus(
        "running-performance-api",
        "free-combined-api-hosted-worker",
        false,
        limits))
    .WithName("GetServiceStatus")
    .WithTags("Operations")
    .Produces<ServiceStatus>();

app.MapProfileEndpoints();
app.MapRaceEndpoints();
app.MapExerciseEndpoints();
app.MapTrainingPlanEndpoints();
app.MapHistoricalImportEndpoints();
app.MapFitIngestionEndpoints();
app.MapActivityEndpoints();
app.MapSessionCompletionEndpoints();
app.MapWeeklyEvaluationEndpoints();
app.MapDashboardEndpoints();
app.MapDataGovernanceEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();

public partial class Program;

public sealed record ServiceStatus(
    string Service,
    string Deployment,
    bool BillingEnabled,
    FreeTierQuotaOptions Limits);
