using LunchOrganizer.Domain.Configuration;
using Npgsql;

namespace LunchOrganizer.Data;

public static class DatabaseOptionsExtensions
{
    /// <summary>
    /// Builds a Postgres connection string from <see cref="DatabaseOptions"/>. Pass <paramref name="overrideDatabase"/>
    /// to connect to a different database (e.g. the maintenance database) while reusing host/credentials/pool settings.
    /// </summary>
    public static string BuildConnectionString(this DatabaseOptions options, string? overrideDatabase = null)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = overrideDatabase ?? options.Database,
            Username = options.Username,
            Password = options.Password,
            MaxPoolSize = options.MaxPoolSize,
            CommandTimeout = options.CommandTimeoutSeconds,
            Pooling = true,
        };
        return builder.ConnectionString;
    }
}
