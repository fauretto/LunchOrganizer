using LunchOrganizer.Data.Abstractions;
using LunchOrganizer.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LunchOrganizer.Data;

/// <summary>
/// Ensures the target database exists, applies pending EF Core migrations (serialized across
/// concurrently starting instances via a PostgreSQL advisory lock), and optionally seeds
/// development data.
/// </summary>
public sealed class DatabaseBootstrapper(
    IDbContextFactory<LunchOrganizerDbContext> factory,
    IOptionsMonitor<LunchOrganizer.Domain.Configuration.DatabaseOptions> options,
    ILogger<DatabaseBootstrapper> logger) : IDatabaseBootstrapper
{
    /// <summary>
    /// Arbitrary constant; must only be stable/consistent across all instances of this app so they
    /// contend for the same PostgreSQL advisory lock when migrating concurrently.
    /// </summary>
    private const long AdvisoryLockKey = 872_615_001;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var dbOptions = options.CurrentValue;

        var databaseCreated = false;

        if (dbOptions.AutoCreateDatabase)
        {
            databaseCreated = await EnsureDatabaseExistsAsync(dbOptions, ct);
        }

        int pendingCount;

        await using (var conn = new NpgsqlConnection(dbOptions.BuildConnectionString()))
        {
            await conn.OpenAsync(ct);

            await using var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", conn);
            lockCmd.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);

            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var pending = await db.Database.GetPendingMigrationsAsync(ct);
                pendingCount = pending.Count();
                await db.Database.MigrateAsync(ct);

                // unaccent is a commonly-available contrib extension used for accent-insensitive
                // employee-name search (EmployeeRepository.SearchByNameAsync). It is installed only
                // if this PostgreSQL install actually offers it, so there is no hard dependency on
                // its presence: if the server doesn't have it, or the role lacks privilege to create
                // it, the app simply falls back to plain ILIKE search.
                try
                {
                    await using var checkUnaccentCmd = new NpgsqlCommand(
                        "SELECT EXISTS(SELECT 1 FROM pg_available_extensions WHERE name = 'unaccent')", conn);
                    var isAvailable = (bool)(await checkUnaccentCmd.ExecuteScalarAsync(ct))!;

                    if (isAvailable)
                    {
                        await using var createUnaccentCmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS unaccent;", conn);
                        await createUnaccentCmd.ExecuteNonQueryAsync(ct);
                    }
                }
                catch (PostgresException ex) when (ex.SqlState == "42501")
                {
                    logger.LogWarning(
                        ex,
                        "Insufficient privilege to install the 'unaccent' PostgreSQL extension; falling back to plain ILIKE search.");
                }
            }
            finally
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", conn);
                unlockCmd.Parameters.AddWithValue("key", AdvisoryLockKey);
                await unlockCmd.ExecuteNonQueryAsync(ct);
            }
        }

        if (dbOptions.Seed)
        {
            await DevelopmentSeeder.SeedAsync(factory, ct);
        }

        logger.LogInformation(
            "Database bootstrap complete: database {Action}, {PendingCount} migration(s) applied, seeding {SeedState}.",
            databaseCreated ? "created" : "already existed",
            pendingCount,
            dbOptions.Seed ? "ran" : "skipped");
    }

    private async Task<bool> EnsureDatabaseExistsAsync(LunchOrganizer.Domain.Configuration.DatabaseOptions dbOptions, CancellationToken ct)
    {
        await using var maintConn = new NpgsqlConnection(dbOptions.BuildConnectionString(dbOptions.MaintenanceDatabase));
        await maintConn.OpenAsync(ct);

        await using var checkCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", maintConn);
        checkCmd.Parameters.AddWithValue("name", dbOptions.Database);
        var exists = await checkCmd.ExecuteScalarAsync(ct);

        if (exists is not null)
        {
            logger.LogInformation("Database '{Database}' already exists.", dbOptions.Database);
            return false;
        }

        if (dbOptions.Database.Contains('"'))
        {
            throw new InvalidOperationException(
                $"Database name '{dbOptions.Database}' contains an invalid character and cannot be used in a CREATE DATABASE statement.");
        }

        try
        {
            // The database name cannot be parameterized because it's a SQL identifier, not a value.
            // It is defensively validated above (rejecting double quotes) and comes from trusted
            // application configuration, so building the statement via string interpolation is safe here.
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbOptions.Database}\"", maintConn);
            await createCmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04")
        {
            logger.LogInformation("Another process created database '{Database}' concurrently.", dbOptions.Database);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            throw new InvalidOperationException(
                $"PostgreSQL role '{dbOptions.Username}' cannot create database '{dbOptions.Database}': it lacks the CREATEDB privilege. " +
                $"Grant it (ALTER ROLE {dbOptions.Username} CREATEDB;) or set Database:AutoCreateDatabase to false and run Scripts/create_database.sql manually as a privileged role.",
                ex);
        }

        logger.LogInformation("Database '{Database}' created.", dbOptions.Database);
        return true;
    }
}
