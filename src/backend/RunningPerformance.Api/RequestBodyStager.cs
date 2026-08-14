using System.Security.Cryptography;

namespace RunningPerformance.Api.Features;

internal static class RequestBodyStager
{
    public static async Task<StagedRequestBody> StageAsync(
        Stream source,
        long? declaredLength,
        int maxBytes,
        string kind,
        CancellationToken cancellationToken)
    {
        var normalizedKind = kind.Trim().ToLowerInvariant();
        var label = normalizedKind.ToUpperInvariant();
        if (declaredLength is > 0 && declaredLength > maxBytes)
        {
            throw new RequestBodyRejectedException(
                $"{normalizedKind}_too_large",
                $"The {label} exceeds the {maxBytes}-byte limit.");
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"rp-{normalizedKind}-upload-{Guid.NewGuid():N}.tmp");
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                    {
                        throw new RequestBodyRejectedException(
                            $"{normalizedKind}_too_large",
                            $"The {label} exceeds the {maxBytes}-byte limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (size == 0)
            {
                throw new RequestBodyRejectedException(
                    $"{normalizedKind}_empty",
                    $"The {label} body is empty.");
            }

            return new(path, size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }
}

internal sealed class StagedRequestBody(string path, long sizeBytes, string sha256) : IAsyncDisposable
{
    public string Path { get; } = path;

    public long SizeBytes { get; } = sizeBytes;

    public string Sha256 { get; } = sha256;

    public FileStream OpenRead() => new(
        Path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    public ValueTask DisposeAsync()
    {
        File.Delete(Path);
        return ValueTask.CompletedTask;
    }
}

internal sealed class RequestBodyRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
