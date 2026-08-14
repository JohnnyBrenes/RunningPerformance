using Microsoft.Extensions.Configuration;
using Npgsql;
using RunningPerformance.Infrastructure.Database;
using Xunit;

namespace RunningPerformance.IntegrationTests;

public sealed class DatabaseConfigurationTests
{
    [Fact]
    public void ResolveConnectionStringAcceptsSupabasePostgresUri()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://postgres.project%2Dref:p%40ss%3Aword@pooler.example.test:6543/postgres?sslmode=require"
        });

        var result = new NpgsqlConnectionStringBuilder(
            DatabaseConfiguration.ResolveConnectionString(configuration));

        Assert.Equal("pooler.example.test", result.Host);
        Assert.Equal(6543, result.Port);
        Assert.Equal("postgres", result.Database);
        Assert.Equal("postgres.project-ref", result.Username);
        Assert.Equal("p@ss:word", result.Password);
        Assert.Equal(SslMode.Require, result.SslMode);
        Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);
        Assert.Equal("running-performance-api", result.ApplicationName);
    }

    [Fact]
    public void ResolveConnectionStringKeepsNpgsqlKeyValueFormat()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
        });

        var result = new NpgsqlConnectionStringBuilder(
            DatabaseConfiguration.ResolveConnectionString(configuration));

        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(54322, result.Port);
        Assert.Equal("postgres", result.Database);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
