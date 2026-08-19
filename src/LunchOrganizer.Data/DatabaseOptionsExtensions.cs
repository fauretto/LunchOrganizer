using LunchOrganizer.Domain.Configuration;
using Microsoft.Data.SqlClient;

namespace LunchOrganizer.Data;

public static class DatabaseOptionsExtensions
{
    /// <summary>
    /// Builds a SQL Server connection string from <see cref="DatabaseOptions"/>. Pass <paramref name="overrideDatabase"/>
    /// to connect to a different database (e.g. the maintenance database, "master") while reusing server/credentials/pool settings.
    /// </summary>
    public static string BuildConnectionString(this DatabaseOptions options, string? overrideDatabase = null)
    {
        options.Validate();

        SqlConnectionStringBuilder builder;

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            // Escape hatch: wins over every structured field. Only the target database is ever
            // overridden on top of it, so the maintenance-database connection still works.
            builder = new SqlConnectionStringBuilder(options.ConnectionString);
            if (overrideDatabase is not null)
            {
                builder.InitialCatalog = overrideDatabase;
            }
        }
        else
        {
            builder = new SqlConnectionStringBuilder
            {
                DataSource = options.Server,
                InitialCatalog = overrideDatabase ?? options.Database,
                Encrypt = options.Encrypt,
                TrustServerCertificate = options.TrustServerCertificate,
                ApplicationName = "LunchOrganizer",
                Pooling = true,
            };

            if (options.IntegratedSecurity)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = options.Username;
                builder.Password = options.Password;
            }
        }

        // Respect an explicit ConnectionString that already sets its own pool size.
        if (!builder.ContainsKey("Max Pool Size"))
        {
            builder.MaxPoolSize = options.MaxPoolSize;
        }

        // CommandTimeoutSeconds is intentionally not applied here — it is not a connection-string
        // keyword. It is applied via UseSqlServer(cs, o => o.CommandTimeout(n)) in a later step, so
        // don't mistake its absence here for an oversight.

        return builder.ConnectionString;
    }
}
