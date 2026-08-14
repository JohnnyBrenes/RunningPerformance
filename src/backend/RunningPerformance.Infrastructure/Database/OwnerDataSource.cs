using Npgsql;

namespace RunningPerformance.Infrastructure.Database;

public sealed class OwnerDataSource(NpgsqlDataSource dataSource)
{
    public async Task<OwnerDbSession> OpenAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        return await OpenAsync(ownerId, workerRole: false, cancellationToken);
    }

    public async Task<OwnerDbSession> OpenWorkerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        return await OpenAsync(ownerId, workerRole: true, cancellationToken);
    }

    private async Task<OwnerDbSession> OpenAsync(
        Guid ownerId,
        bool workerRole,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Owner ID cannot be empty.", nameof(ownerId));
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                if (workerRole)
                {
                    await OwnerTransactionContext.ApplyWorkerAsync(
                        connection,
                        transaction,
                        ownerId,
                        cancellationToken);
                }
                else
                {
                    await OwnerTransactionContext.ApplyAsync(
                        connection,
                        transaction,
                        ownerId,
                        cancellationToken);
                }
                return new OwnerDbSession(connection, transaction);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class OwnerDbSession(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction) : IAsyncDisposable
{
    private bool _completed;

    public NpgsqlConnection Connection { get; } = connection;

    public NpgsqlTransaction Transaction { get; } = transaction;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await Transaction.RollbackAsync();
            }
            catch (InvalidOperationException)
            {
                // The transaction may already have been completed by PostgreSQL.
            }
        }

        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
