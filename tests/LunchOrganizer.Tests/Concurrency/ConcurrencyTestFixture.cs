using System.Text.Json;
using LunchOrganizer.Data;
using LunchOrganizer.Data.Repositories;
using LunchOrganizer.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LunchOrganizer.Tests.Concurrency;

/// <summary>
/// Spins up (and tears down) a dedicated "lunchorganizer_test" PostgreSQL database, migrated with the
/// real EF Core migrations, and exposes the real Postgres-backed repositories for the Concurrency test
/// suite to exercise directly. Connection details (host/port/username/password) are read from
/// config/database.local.json at the repo root; only the target database name is hardcoded here so
/// tests never collide with a developer's normal "lunchorganizer" database.
/// </summary>
public sealed class ConcurrencyTestFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "lunchorganizer_test";

    public IDbContextFactory<LunchOrganizerDbContext> Factory { get; private set; } = null!;

    public EmployeeRepository EmployeeRepository { get; private set; } = null!;
    public MenuRepository MenuRepository { get; private set; } = null!;
    public BookingRepository BookingRepository { get; private set; } = null!;
    public DailyPriceRepository DailyPriceRepository { get; private set; } = null!;
    public EmailLogRepository EmailLogRepository { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var configPath = Path.Combine(repoRoot, "config", "database.local.json");

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Config file not found at '{configPath}'.");
            }

            var json = await File.ReadAllTextAsync(configPath);
            var configFile = JsonSerializer.Deserialize<ConfigFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var dbSection = configFile?.Database;

            if (dbSection is null
                || string.IsNullOrWhiteSpace(dbSection.Host)
                || string.IsNullOrWhiteSpace(dbSection.Username)
                || string.IsNullOrWhiteSpace(dbSection.Password))
            {
                throw new InvalidOperationException($"Required fields (Host/Username/Password) are missing or blank in '{configPath}'.");
            }

            var options = new DatabaseOptions
            {
                Host = dbSection.Host!,
                Port = dbSection.Port == 0 ? 5432 : dbSection.Port,
                Username = dbSection.Username!,
                Password = dbSection.Password!,
                Database = TestDatabaseName,
                MaintenanceDatabase = "postgres",
            };

            await RecreateTestDatabaseAsync(options);

            var connectionString = options.BuildConnectionString(overrideDatabase: TestDatabaseName);
            var dbContextOptions = new DbContextOptionsBuilder<LunchOrganizerDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            await using (var db = new LunchOrganizerDbContext(dbContextOptions))
            {
                await db.Database.MigrateAsync();
            }

            Factory = new PlainDbContextFactory(dbContextOptions);
            EmployeeRepository = new EmployeeRepository(Factory);
            MenuRepository = new MenuRepository(Factory);
            BookingRepository = new BookingRepository(Factory);
            DailyPriceRepository = new DailyPriceRepository(Factory);
            EmailLogRepository = new EmailLogRepository(Factory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot reach PostgreSQL for ConcurrencyTests — config/database.local.json not found or connection failed: {ex.Message}",
                ex);
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var configPath = Path.Combine(repoRoot, "config", "database.local.json");
            if (!File.Exists(configPath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var configFile = JsonSerializer.Deserialize<ConfigFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var dbSection = configFile?.Database;
            if (dbSection is null || string.IsNullOrWhiteSpace(dbSection.Host) || string.IsNullOrWhiteSpace(dbSection.Username))
            {
                return;
            }

            var options = new DatabaseOptions
            {
                Host = dbSection.Host!,
                Port = dbSection.Port == 0 ? 5432 : dbSection.Port,
                Username = dbSection.Username!,
                Password = dbSection.Password ?? string.Empty,
                Database = TestDatabaseName,
                MaintenanceDatabase = "postgres",
            };

            await using var conn = new NpgsqlConnection(options.BuildConnectionString("postgres"));
            await conn.OpenAsync();

            await using (var terminateCmd = conn.CreateCommand())
            {
                terminateCmd.CommandText =
                    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TestDatabaseName}' AND pid <> pg_backend_pid();";
                await terminateCmd.ExecuteNonQueryAsync();
            }

            await using (var dropCmd = conn.CreateCommand())
            {
                dropCmd.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName};";
                await dropCmd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            // Teardown failures must never mask a test failure; best-effort cleanup only.
        }
    }

    private static async Task RecreateTestDatabaseAsync(DatabaseOptions options)
    {
        await using var conn = new NpgsqlConnection(options.BuildConnectionString("postgres"));
        await conn.OpenAsync();

        await using (var terminateCmd = conn.CreateCommand())
        {
            terminateCmd.CommandText =
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TestDatabaseName}' AND pid <> pg_backend_pid();";
            await terminateCmd.ExecuteNonQueryAsync();
        }

        await using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName};";
            await dropCmd.ExecuteNonQueryAsync();
        }

        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = $"CREATE DATABASE {TestDatabaseName};";
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    private static string FindRepoRoot()
    {
        var searched = new List<string>();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            searched.Add(dir.FullName);
            if (File.Exists(Path.Combine(dir.FullName, "LunchOrganizer.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate LunchOrganizer.sln by walking up from '{AppContext.BaseDirectory}'. Searched: {string.Join(", ", searched)}");
    }

    private sealed class ConfigFile
    {
        public DatabaseSection? Database { get; set; }
    }

    private sealed class DatabaseSection
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    /// <summary>Trivial <see cref="IDbContextFactory{TContext}"/> wrapping a fixed <see cref="DbContextOptions{TContext}"/>.</summary>
    private sealed class PlainDbContextFactory(DbContextOptions<LunchOrganizerDbContext> options) : IDbContextFactory<LunchOrganizerDbContext>
    {
        public LunchOrganizerDbContext CreateDbContext() => new(options);
    }
}

[CollectionDefinition("ConcurrencyTests")]
public sealed class ConcurrencyTestCollection : ICollectionFixture<ConcurrencyTestFixture>
{
}
