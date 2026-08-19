using LunchOrganizer.Data.Abstractions;
using LunchOrganizer.Data.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;

namespace LunchOrganizer.Data;

/// <summary>
/// Ensures the target database exists, applies pending EF Core migrations (serialized across
/// concurrently starting instances via a SQL Server application lock), and optionally seeds
/// development data.
/// </summary>
public sealed class DatabaseBootstrapper(
    IDbContextFactory<LunchOrganizerDbContext> factory,
    IOptionsMonitor<LunchOrganizer.Domain.Configuration.DatabaseOptions> options,
    ILogger<DatabaseBootstrapper> logger) : IDatabaseBootstrapper
{
    /// <summary>
    /// Resource name passed to sp_getapplock/sp_releaseapplock; must only be stable/consistent across
    /// all instances of this app so they contend for the same SQL Server application lock when
    /// migrating concurrently.
    /// </summary>
    private const string MigrationLockResource = "LunchOrganizer_Migrate";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var dbOptions = options.CurrentValue;

        var databaseCreated = false;

        if (dbOptions.AutoCreateDatabase)
        {
            databaseCreated = await EnsureDatabaseExistsAsync(dbOptions, ct);
        }

        int pendingCount;

        await using (var conn = new SqlConnection(dbOptions.BuildConnectionString()))
        {
            await conn.OpenAsync(ct);

            // @LockOwner = 'Session' ties the lock to this open connection (rather than to the current
            // transaction), which is why the connection is held open across the whole migration and the
            // lock is released explicitly, on this same connection, in the finally block below.
            await using var lockCmd = new SqlCommand("sp_getapplock", conn) { CommandType = CommandType.StoredProcedure };
            lockCmd.Parameters.AddWithValue("@Resource", MigrationLockResource);
            lockCmd.Parameters.AddWithValue("@LockMode", "Exclusive");
            lockCmd.Parameters.AddWithValue("@LockOwner", "Session");
            lockCmd.Parameters.AddWithValue("@LockTimeout", 60000);
            var lockResultParam = lockCmd.Parameters.Add("@ReturnValue", SqlDbType.Int);
            lockResultParam.Direction = ParameterDirection.ReturnValue;
            await lockCmd.ExecuteNonQueryAsync(ct);

            // sp_getapplock signals through its return value, not an exception: 0 = granted
            // immediately, 1 = granted after waiting, and any negative value is a failure (-1 timeout,
            // -2 cancelled, -3 deadlock victim, -999 parameter/other error). Must not proceed to
            // migrate without the lock.
            var lockResult = (int)lockResultParam.Value;
            if (lockResult < 0)
            {
                throw new InvalidOperationException(
                    $"Could not acquire the '{MigrationLockResource}' migration lock (sp_getapplock returned {lockResult}).");
            }

            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var pending = await db.Database.GetPendingMigrationsAsync(ct);
                pendingCount = pending.Count();
                await db.Database.MigrateAsync(ct);

                // Deliberately no extension setup here: unlike PostgreSQL's unaccent, SQL Server needs
                // no extension for accent-insensitive search — it's handled by an explicit
                // COLLATE Latin1_General_CI_AI override in EmployeeRepository.SearchByNameAsync.
            }
            finally
            {
                await using var unlockCmd = new SqlCommand("sp_releaseapplock", conn) { CommandType = CommandType.StoredProcedure };
                unlockCmd.Parameters.AddWithValue("@Resource", MigrationLockResource);
                unlockCmd.Parameters.AddWithValue("@LockOwner", "Session");
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
        // The maintenance database is "master" here (DatabaseOptions.MaintenanceDatabase default),
        // unlike PostgreSQL's "postgres".
        await using var maintConn = new SqlConnection(dbOptions.BuildConnectionString(dbOptions.MaintenanceDatabase));
        await maintConn.OpenAsync(ct);

        await using var checkCmd = new SqlCommand("SELECT 1 FROM sys.databases WHERE name = @name", maintConn);
        checkCmd.Parameters.AddWithValue("@name", dbOptions.Database);
        var exists = await checkCmd.ExecuteScalarAsync(ct);

        if (exists is not null)
        {
            logger.LogInformation("Database '{Database}' already exists.", dbOptions.Database);
            return false;
        }

        if (dbOptions.Database.Contains(']'))
        {
            throw new InvalidOperationException(
                $"Database name '{dbOptions.Database}' contains an invalid character and cannot be used in a CREATE DATABASE statement.");
        }

        try
        {
            // The database name cannot be parameterized because it's a SQL identifier, not a value.
            // It is defensively validated above (rejecting the closing bracket used to quote SQL
            // Server identifiers) and comes from trusted application configuration, so building the
            // statement via string interpolation is safe here.
            await using var createCmd = new SqlCommand($"CREATE DATABASE [{dbOptions.Database}]", maintConn);
            await createCmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 1801)
        {
            logger.LogInformation("Another process created database '{Database}' concurrently.", dbOptions.Database);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 262)
        {
            throw new InvalidOperationException(
                $"The login cannot create database '{dbOptions.Database}': it lacks permission to create databases. " +
                "Grant it the 'dbcreator' server role (or CREATE ANY DATABASE permission), or set Database:AutoCreateDatabase to false and run Scripts/create_database.sql manually as a privileged login.",
                ex);
        }

        logger.LogInformation("Database '{Database}' created.", dbOptions.Database);
        return true;
    }
}
