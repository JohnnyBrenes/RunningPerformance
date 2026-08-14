using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Infrastructure.Database;

namespace RunningPerformance.Infrastructure.Sync;

public sealed class SyncCredentialService
{
    private const string PairingPrefix = "rppair_";
    private const string CredentialPrefix = "rpfit_";
    private readonly NpgsqlDataSource dataSource;
    private readonly OwnerDataSource ownerDataSource;
    private readonly FitIngestionOptions options;
    private readonly byte[] pepper;

    public SyncCredentialService(
        NpgsqlDataSource dataSource,
        OwnerDataSource ownerDataSource,
        FitIngestionOptions options,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        this.dataSource = dataSource;
        this.ownerDataSource = ownerDataSource;
        this.options = options;
        var configured = configuration["FIT_SYNC_PEPPER"];
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("FIT_SYNC_PEPPER is required in production.");
        }
        pepper = Encoding.UTF8.GetBytes(
            configured ?? "running-performance-local-fit-sync-pepper");
    }

    public async Task<PairingTokenResult> CreatePairingTokenAsync(
        Guid ownerId,
        string displayName,
        CancellationToken cancellationToken)
    {
        displayName = NormalizeDisplayName(displayName);
        var token = PairingPrefix + RandomToken(32);
        var expiresAt = DateTime.UtcNow.AddMinutes(options.PairingMinutes);
        await using var session = await ownerDataSource.OpenAsync(ownerId, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.sync_pairing_tokens (
              owner_id, token_hash, expires_at, requested_client_name)
            values (@owner_id, @token_hash, @expires_at, @display_name);
            """;
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("token_hash", Hash(token));
        command.Parameters.AddWithValue("expires_at", expiresAt);
        command.Parameters.AddWithValue("display_name", displayName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new(token, displayName, expiresAt);
    }

    public async Task<DeviceCredentialResult?> ExchangePairingTokenAsync(
        string pairingToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pairingToken)
            || !pairingToken.StartsWith(PairingPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var publicTokenId = RandomToken(16);
        var secret = RandomToken(32);
        var expiresAt = DateTime.UtcNow.AddDays(options.CredentialDays);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OwnerTransactionContext.ApplyApiCoordinatorAsync(
            connection,
            transaction,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select owner_id, sync_client_id
            from app.consume_sync_pairing_token(
              @token_hash, 'paired FIT client', @public_token_id,
              @secret_hash, @expires_at);
            """;
        command.Parameters.AddWithValue("token_hash", Hash(pairingToken));
        command.Parameters.AddWithValue("public_token_id", publicTokenId);
        command.Parameters.AddWithValue("secret_hash", Hash(secret));
        command.Parameters.AddWithValue("expires_at", expiresAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        var ownerId = reader.GetGuid(0);
        var clientId = reader.GetGuid(1);
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return new(
            ownerId,
            clientId,
            $"{CredentialPrefix}{publicTokenId}.{secret}",
            expiresAt,
            ["fit.upload"]);
    }

    public async Task<AuthenticatedSyncClient?> AuthenticateAsync(
        string? authorization,
        CancellationToken cancellationToken)
    {
        const string scheme = "FitUpload ";
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith(scheme, StringComparison.Ordinal))
        {
            return null;
        }
        var credential = authorization[scheme.Length..].Trim();
        if (!credential.StartsWith(CredentialPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var parts = credential[CredentialPrefix.Length..].Split('.', 2);
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await OwnerTransactionContext.ApplyApiCoordinatorAsync(
            connection,
            transaction,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select owner_id, sync_client_id, scopes
            from app.authenticate_sync_client(@public_token_id, @secret_hash);
            """;
        command.Parameters.AddWithValue("public_token_id", parts[0]);
        command.Parameters.AddWithValue("secret_hash", Hash(parts[1]));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        var result = new AuthenticatedSyncClient(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetFieldValue<string[]>(2));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private string Hash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string RandomToken(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "The synchronization client name must contain 1 to 80 characters.",
                nameof(value));
        }
        return normalized;
    }
}

public sealed record PairingTokenResult(
    string PairingToken,
    string DisplayName,
    DateTime ExpiresAt);

public sealed record DeviceCredentialResult(
    Guid OwnerId,
    Guid ClientId,
    string Credential,
    DateTime ExpiresAt,
    IReadOnlyList<string> Scopes);

public sealed record AuthenticatedSyncClient(
    Guid OwnerId,
    Guid ClientId,
    IReadOnlyList<string> Scopes);
