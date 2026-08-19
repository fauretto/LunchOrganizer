using System.Text.Json;
using LunchOrganizer.Data;
using LunchOrganizer.Data.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Tests.Concurrency;

/// <summary>
/// Spins up (and tears down) a dedicated "lunchorganizer_test" SQL Server database, migrated with the
/// real EF Core migrations, and exposes the real SQL Server-backed repositories for the Concurrency
/// test suite to exercise directly. Connection details (server/integrated security/username/password)
/// are read the same way the application reads them: config/database.json at the repo root is the
/// required base, and config/database.local.json — if present — is an optional developer overlay
/// whose non-null values take precedence. Only the target database name is hardcoded here so tests
/// never collide with a developer's normal "lunchorganizer" database.
/// </summary>
public sealed class ConcurrencyTestFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "lunchorganizer_test";

    // config/database.json (and its optional .local.json overlay) are hand-edited and may carry "//"
    // comments and trailing commas, both of which the .NET configuration provider (AddJsonFile)
    // accepts. JsonSerializer does not accept them by default, so this reader must be configured to
    // tolerate them too.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
            var dbSection = await LoadDatabaseSectionAsync();

            await RecreateTestDatabaseAsync(dbSection);

            var connectionString = BuildConnectionString(dbSection, TestDatabaseName);
            var dbContextOptions = new DbContextOptionsBuilder<LunchOrganizerDbContext>()
                .UseSqlServer(connectionString)
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
                $"Cannot reach SQL Server for ConcurrencyTests — config/database.json not found or connection failed: {ex.Message}",
                ex);
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            var dbSection = await LoadDatabaseSectionAsync();

            await DropTestDatabaseAsync(dbSection);
        }
        catch
        {
            // Teardown failures must never mask a test failure; best-effort cleanup only.
        }
    }

    /// <summary>
    /// Loads config/database.json (required) and layers config/database.local.json (optional) on top
    /// of it, mirroring how Program.cs configures the real application: the base file must exist and
    /// declare a non-blank Server, while the local overlay — commonly used to point a developer's own
    /// machine at a differently-named instance or SQL-auth credentials — is applied only when present,
    /// with its non-null fields winning over the base file's.
    /// </summary>
    private static async Task<DatabaseSection> LoadDatabaseSectionAsync()
    {
        var repoRoot = FindRepoRoot();
        var basePath = Path.Combine(repoRoot, "config", "database.json");

        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException($"Config file not found at '{basePath}'.");
        }

        var baseJson = await File.ReadAllTextAsync(basePath);
        var baseSection = JsonSerializer.Deserialize<ConfigFile>(baseJson, JsonOptions)?.Database;

        if (baseSection is null || string.IsNullOrWhiteSpace(baseSection.Server))
        {
            throw new InvalidOperationException($"Required field 'Server' is missing or blank in '{basePath}'.");
        }

        // The overlay is a developer convenience and is genuinely optional — its absence is the normal
        // case now that the project targets a shared SQL Server Express instance, so no error/warning here.
        var overlayPath = Path.Combine(repoRoot, "config", "database.local.json");
        if (File.Exists(overlayPath))
        {
            var overlayJson = await File.ReadAllTextAsync(overlayPath);
            var overlaySection = JsonSerializer.Deserialize<ConfigFile>(overlayJson, JsonOptions)?.Database;

            if (overlaySection is not null)
            {
                // Present-wins: only fields the overlay actually sets override the base file's values.
                baseSection.Server = overlaySection.Server ?? baseSection.Server;
                baseSection.IntegratedSecurity = overlaySection.IntegratedSecurity ?? baseSection.IntegratedSecurity;
                baseSection.Username = overlaySection.Username ?? baseSection.Username;
                baseSection.Password = overlaySection.Password ?? baseSection.Password;
                baseSection.Encrypt = overlaySection.Encrypt ?? baseSection.Encrypt;
            }
        }

        return baseSection;
    }

    private static async Task RecreateTestDatabaseAsync(DatabaseSection dbSection)
    {
        var adminConnectionString = BuildConnectionString(dbSection, "master");
        await using var conn = new SqlConnection(adminConnectionString);
        await conn.OpenAsync();

        await using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = $"""
                IF DB_ID('{TestDatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{TestDatabaseName}];
                END
                """;
            await dropCmd.ExecuteNonQueryAsync();
        }

        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = $"CREATE DATABASE [{TestDatabaseName}];";
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropTestDatabaseAsync(DatabaseSection dbSection)
    {
        var adminConnectionString = BuildConnectionString(dbSection, "master");
        await using var conn = new SqlConnection(adminConnectionString);
        await conn.OpenAsync();

        await using var dropCmd = conn.CreateCommand();
        dropCmd.CommandText = $"""
            IF DB_ID('{TestDatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{TestDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{TestDatabaseName}];
            END
            """;
        await dropCmd.ExecuteNonQueryAsync();
    }

    private static string BuildConnectionString(DatabaseSection dbSection, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dbSection.Server,
            InitialCatalog = database,
            // Defaults to false only when the config leaves Encrypt unset. In practice config/database.json
            // sets Encrypt: true, and TrustServerCertificate is hardcoded true below, so this happily
            // encrypts against the real SQL Server Express instance the tests now target.
            Encrypt = dbSection.Encrypt ?? false,
            TrustServerCertificate = true,
        };

        // Absent (null) means "not specified by the overlay" and defaults to true, same as production.
        if (dbSection.IntegratedSecurity ?? true)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = dbSection.Username ?? string.Empty;
            builder.Password = dbSection.Password ?? string.Empty;
        }

        return builder.ConnectionString;
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
        public string? Server { get; set; }

        // Nullable so the overlay-merge logic can tell "not present in this file" (null) apart from
        // "explicitly set to false" — a plain bool defaulting to true would make an overlay that only
        // sets, say, Server silently force IntegratedSecurity back to true. Null is treated as true by
        // BuildConnectionString, preserving the original default.
        public bool? IntegratedSecurity { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool? Encrypt { get; set; }
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
