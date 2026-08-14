using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace RunningPerformance.Infrastructure.Storage;

public sealed class SupabaseObjectStore(HttpClient httpClient, IConfiguration configuration)
{
    public const string Bucket = "athlete-files";

    private readonly Uri _baseUri = ResolveBaseUri(configuration);
    private readonly string? _apiKey = ResolveApiKey(configuration);
    private readonly string? _bearerToken = ResolveBearerToken(configuration);

    public async Task UploadAsync(
        string objectPath,
        Stream source,
        long sizeBytes,
        string mimeType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Post, objectPath);
        request.Headers.TryAddWithoutValidation("x-upsert", "false");
        request.Content = new StreamContent(source);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        request.Content.Headers.ContentLength = sizeBytes;

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ObjectStoreException(
                "storage_upload_failed",
                $"Private object upload failed with status {(int)response.StatusCode}.");
        }
    }

    public async Task DownloadToAsync(
        string objectPath,
        Stream destination,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Get, objectPath);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ObjectStoreException(
                "storage_download_failed",
                $"Private object download failed with status {(int)response.StatusCode}.");
        }

        await response.Content.CopyToAsync(destination, cancellationToken);
    }

    public async Task RemoveAsync(string objectPath, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(HttpMethod.Delete, objectPath);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode is not System.Net.HttpStatusCode.NotFound)
        {
            throw new ObjectStoreException(
                "storage_cleanup_failed",
                $"Private object cleanup failed with status {(int)response.StatusCode}.");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string objectPath)
    {
        var key = _apiKey!;
        var escapedPath = string.Join(
            '/',
            objectPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        var request = new HttpRequestMessage(
            method,
            new Uri(_baseUri, $"storage/v1/object/{Bucket}/{escapedPath}"));
        if (_bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        request.Headers.TryAddWithoutValidation("apikey", key);
        return request;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new ObjectStoreException(
                "storage_not_configured",
                "SUPABASE_SECRET_KEY is required for private source ingestion.");
        }
    }

    private static Uri ResolveBaseUri(IConfiguration configuration)
    {
        var value = configuration["Supabase:Url"]
            ?? configuration["SUPABASE_URL"]
            ?? "http://127.0.0.1:54321";
        return new Uri($"{value.TrimEnd('/')}/", UriKind.Absolute);
    }

    private static string? ResolveApiKey(IConfiguration configuration) =>
        configuration["Supabase:SecretKey"]
        ?? configuration["SUPABASE_SECRET_KEY"]
        ?? configuration["SUPABASE_SERVICE_ROLE_KEY"];

    private static string? ResolveBearerToken(IConfiguration configuration)
    {
        var serviceRole = configuration["SUPABASE_SERVICE_ROLE_KEY"];
        if (!string.IsNullOrWhiteSpace(serviceRole))
        {
            return serviceRole;
        }

        var key = configuration["Supabase:SecretKey"] ?? configuration["SUPABASE_SECRET_KEY"];
        return key?.StartsWith("eyJ", StringComparison.Ordinal) == true ? key : null;
    }
}

public sealed class ObjectStoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
