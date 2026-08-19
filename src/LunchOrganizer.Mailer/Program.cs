using System.Globalization;
using LunchOrganizer.Data;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Email.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string usage = """
    LunchOrganizer.Mailer — sends (or previews) the daily lunch-booking summary email.

    Usage:
      LunchOrganizer.Mailer [--date yyyy-MM-dd] [--dry-run]
      LunchOrganizer.Mailer --help

    Options:
      --date <yyyy-MM-dd>   The summary date to process. Defaults to today (local date) if omitted.
      --dry-run             Build and write the email without actually sending it.
      --help, -h            Show this help text.

    Exit codes:
      0  Sent
      1  Failed
      2  Skipped (no bookings that day)
      3  AlreadyHandled (another process already reserved/sent this day)
    """;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(usage);
    return 0;
}

var dryRun = args.Contains("--dry-run");

var date = DateOnly.FromDateTime(DateTime.Now);
var dateIndex = Array.IndexOf(args, "--date");
if (dateIndex >= 0)
{
    if (dateIndex + 1 >= args.Length ||
        !DateOnly.TryParseExact(args[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
    {
        Console.Error.WriteLine("Error: --date requires a value in yyyy-MM-dd format.");
        return 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);

// Fail loudly at startup — not at first use — if a singleton ever captures one of the scoped,
// EF Core-backed repositories.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
}));

// ---- Configuration: hand-editable JSON under repo-root config/, copied next to the build output ----
var configDirectory = Path.Combine(AppContext.BaseDirectory, "config");
builder.Configuration
    .AddJsonFile(Path.Combine(configDirectory, "app.json"), optional: false, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "app.local.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "database.json"), optional: false, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "database.local.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "email.json"), optional: false, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "email.local.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "admin-users.json"), optional: false, reloadOnChange: false)
    .AddJsonFile(Path.Combine(configDirectory, "admin-users.local.json"), optional: true, reloadOnChange: false);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// admin-users.json has no wrapping section — the whole file IS the AdminUsersOptions object.
builder.Services.Configure<AdminUsersOptions>(builder.Configuration);

// ---- Database: ALWAYS AddDbContextFactory, NEVER AddDbContext ----
builder.Services.AddDbContextFactory<LunchOrganizerDbContext>((sp, options) =>
{
    var dbOptions = sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;
    // CommandTimeoutSeconds is applied here (not as a connection-string keyword) — see the matching
    // comment in DatabaseOptionsExtensions.BuildConnectionString.
    options.UseSqlServer(dbOptions.BuildConnectionString(), sql => sql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
});

builder.Services.AddSingleton<IClock, SystemClock>();

// Real, SQL Server-backed repositories — the same ones the web application uses.
builder.Services.AddLunchOrganizerData();

// Scoped, not Singleton: these consume the scoped, EF Core-backed IBookingRepository /
// IEmailLogRepository registered by AddLunchOrganizerData(), and a singleton cannot
// consume a scoped dependency (ValidateScopes above would throw at startup if it did).
builder.Services.AddScoped<IDailySummaryBuilder, DailySummaryBuilder>();
builder.Services.AddSingleton<IDailySummaryBodyRenderer, DailySummaryBodyRenderer>();

// Per-employee booking confirmations (plan §4.9) — mirrors the Web project's registrations above.
builder.Services.AddSingleton<IEmployeeConfirmationBodyRenderer, EmployeeConfirmationBodyRenderer>();
builder.Services.AddScoped<IEmployeeConfirmationSender, EmployeeConfirmationSender>();

builder.Services.AddSingleton<PickupDirectoryEmailSender>();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<IEmailSender>(sp =>
{
    var mode = sp.GetRequiredService<IOptionsMonitor<EmailOptions>>().CurrentValue.Mode;
    return mode == EmailDeliveryMode.Smtp
        ? sp.GetRequiredService<SmtpEmailSender>()
        : sp.GetRequiredService<PickupDirectoryEmailSender>();
});

// Scoped for the same reason as IDailySummaryBuilder above.
builder.Services.AddScoped<IDailySummaryMailService, DailySummaryMailService>();

using var host = builder.Build();

// Preflight: the mailer must never create the database schema — the web application owns that.
// Fail fast, in plain language a non-technical operator can act on, if SQL Server isn't reachable
// or the schema hasn't been created yet.
try
{
    var dbContextFactory = host.Services.GetRequiredService<IDbContextFactory<LunchOrganizerDbContext>>();
    await using var preflightDb = await dbContextFactory.CreateDbContextAsync();

    if (!await preflightDb.Database.CanConnectAsync())
    {
        Console.Error.WriteLine(
            "Cannot reach the LunchOrganizer database. The daily summary email was NOT sent.\n" +
            "Check the connection settings in config/database.json (or config/database.local.json).");
        return 1;
    }

    var appliedMigrations = await preflightDb.Database.GetAppliedMigrationsAsync();
    if (!appliedMigrations.Any())
    {
        Console.Error.WriteLine(
            "The LunchOrganizer database exists but has no LunchOrganizer tables yet. The daily summary email was NOT sent.\n" +
            "Start the web application once to create the database schema, then re-run the mailer.");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "Cannot reach the LunchOrganizer database. The daily summary email was NOT sent.\n" +
        "Check the connection settings in config/database.json (or config/database.local.json).\n" +
        $"Details: {ex.Message}");
    return 1;
}

using var scope = host.Services.CreateScope();
var mailService = scope.ServiceProvider.GetRequiredService<IDailySummaryMailService>();

try
{
    var result = await mailService.RunAsync(date, dryRun);
    var prefix = dryRun ? "[DRY RUN] " : string.Empty;
    Console.WriteLine($"{prefix}{date:yyyy-MM-dd} — {result.Status}: {result.Message} (bookings: {result.BookingCount}, exit code {result.SuggestedExitCode})");
    return result.SuggestedExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    return 1;
}
