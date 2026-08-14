using Npgsql;

namespace RunningPerformance.Infrastructure.Database;

public static class OwnerTransactionContext
{
    public static async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await ApplyRoleAsync(connection, transaction, ownerId, "rp_api", cancellationToken);
    }

    public static async Task ApplyWorkerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await ApplyRoleAsync(connection, transaction, ownerId, "rp_worker", cancellationToken);
    }

    public static async Task ApplyWorkerCoordinatorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "set local role rp_worker;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ApplyApiCoordinatorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "set local role rp_api;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ownerId,
        string role,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            set local role {role};
            select
              set_config('request.jwt.claim.sub', @owner_id, true),
              set_config('request.jwt.claim.role', 'authenticated', true),
              set_config(
                'request.jwt.claims',
                jsonb_build_object('sub', @owner_id, 'role', 'authenticated')::text,
                true);
            """;
        command.Parameters.AddWithValue("owner_id", ownerId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
