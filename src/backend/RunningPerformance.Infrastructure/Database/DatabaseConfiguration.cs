using Microsoft.Extensions.Configuration;
using Npgsql;

namespace RunningPerformance.Infrastructure.Database;

public static class DatabaseConfiguration
{
    private const string LocalDevelopmentConnection =
        "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres";

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration.GetConnectionString("Database")
            ?? configuration["DATABASE_URL"];
        var builder = CreateBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalDevelopmentConnection : configured);
        builder.GssEncryptionMode = GssEncryptionMode.Disable;
        builder.ApplicationName = "running-performance-api";

        return builder.ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder CreateBuilder(string configured)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return new NpgsqlConnectionStringBuilder(configured);
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length != 2
            || string.IsNullOrWhiteSpace(userInfo[0])
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("DATABASE_URL is not a valid PostgreSQL URI.");
        }

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("DATABASE_URL must include a database name.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1])
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var components = pair.Split('=', 2);
            if (components.Length == 2
                && components[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(Uri.UnescapeDataString(components[1]), true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }

        return builder;
    }
}
