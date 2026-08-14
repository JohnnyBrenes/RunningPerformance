using System.Reflection;
using Npgsql;
using RunningPerformance.Infrastructure.Database;
using Testcontainers.PostgreSql;
using Xunit;

namespace RunningPerformance.IntegrationTests;

public sealed class OwnerTransactionContextTests
{
    [Fact]
    public void OwnerContextRequiresAnExplicitTransaction()
    {
        var method = typeof(OwnerTransactionContext).GetMethod(
            nameof(OwnerTransactionContext.ApplyAsync),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "NpgsqlTransaction");
    }

    [Fact]
    public async Task OwnerContextIsTransactionLocalAndDoesNotLeakThroughThePool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:17.6-bookworm")
            .WithDatabase("running_performance_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync(cancellationToken);

        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            MaxPoolSize = 1,
            MinPoolSize = 1,
            NoResetOnClose = false
        }.ConnectionString;
        await using var pool = NpgsqlDataSource.Create(connectionString);
        await InitializeProbeSchemaAsync(pool, cancellationToken);

        var ownerA = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var ownerDataSource = new OwnerDataSource(pool);
        int physicalConnectionId;
        await using (var ownerSession = await ownerDataSource.OpenAsync(ownerA, cancellationToken))
        {
            physicalConnectionId = ownerSession.Connection.ProcessID;
            await using var command = ownerSession.Connection.CreateCommand();
            command.Transaction = ownerSession.Transaction;
            command.CommandText = "select current_user, app.current_owner_id(), count(*) from app.pool_probe;";
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                Assert.True(await reader.ReadAsync(cancellationToken));
                Assert.Equal("rp_api", reader.GetString(0));
                Assert.Equal(ownerA, reader.GetGuid(1));
                Assert.Equal(1, reader.GetInt64(2));
            }

            await ownerSession.CommitAsync(cancellationToken);
        }

        await using var reusedConnection = await pool.OpenConnectionAsync(cancellationToken);
        Assert.Equal(physicalConnectionId, reusedConnection.ProcessID);
        await using var transaction = await reusedConnection.BeginTransactionAsync(cancellationToken);
        await using var pooledCommand = reusedConnection.CreateCommand();
        pooledCommand.Transaction = transaction;
        pooledCommand.CommandText = """
            set local role rp_api;
            select nullif(current_setting('request.jwt.claim.sub', true), ''), count(*)
            from app.pool_probe;
            """;
        await using var pooledReader = await pooledCommand.ExecuteReaderAsync(cancellationToken);
        Assert.True(await pooledReader.ReadAsync(cancellationToken));
        Assert.True(pooledReader.IsDBNull(0));
        Assert.Equal(0, pooledReader.GetInt64(1));
    }

    private static async Task InitializeProbeSchemaAsync(
        NpgsqlDataSource pool,
        CancellationToken cancellationToken)
    {
        await using var command = pool.CreateCommand("""
            create role rp_api nologin noinherit nobypassrls;
            grant rp_api to postgres;
            create schema app;
            create function app.current_owner_id() returns uuid language sql stable
            as $$ select nullif(current_setting('request.jwt.claim.sub', true), '')::uuid $$;
            create table app.pool_probe (owner_id uuid primary key);
            insert into app.pool_probe values
              ('11111111-1111-4111-8111-111111111111'),
              ('22222222-2222-4222-8222-222222222222');
            alter table app.pool_probe enable row level security;
            alter table app.pool_probe force row level security;
            create policy pool_probe_owner on app.pool_probe
              using (owner_id = app.current_owner_id());
            grant usage on schema app to rp_api;
            grant execute on function app.current_owner_id() to rp_api;
            grant select on app.pool_probe to rp_api;
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
