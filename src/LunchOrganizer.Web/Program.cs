using System.Globalization;
using System.Threading.RateLimiting;
using LunchOrganizer.Web.Components;
using LunchOrganizer.Web.Components.Shared.Confirmation;
using LunchOrganizer.Web.Components.Shared.Toasts;
using LunchOrganizer.Web.Endpoints;
using LunchOrganizer.Data;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Scheduling;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Email.Services;
using LunchOrganizer.Fakes;
using LunchOrganizer.Services;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Notifications;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration: hand-editable JSON under repo-root config/, copied next to the build output ----
var configDirectory = Path.Combine(AppContext.BaseDirectory, "config");

builder.Configuration
    .AddJsonFile(Path.Combine(configDirectory, "app.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "app.local.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "database.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "database.local.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "email.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "email.local.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "admin-users.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configDirectory, "admin-users.local.json"), optional: true, reloadOnChange: true);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
// ConfigurationBinder appends config array items onto AppOptions.SupportedCultures's non-empty
// default list rather than replacing it (a documented .NET config-binding behavior for
// pre-populated ICollection<T> properties), so normalize away the resulting duplicates here —
// this runs on every (re)bind, including config reloadOnChange, and fixes the shared value for
// every consumer of IOptionsMonitor<AppOptions> (AppHeader.razor, CultureState, etc.).
builder.Services.PostConfigure<AppOptions>(options =>
    options.SupportedCultures = options.SupportedCultures
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList());
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// admin-users.json has no wrapping section — the whole file IS the AdminUsersOptions object.
builder.Services.Configure<AdminUsersOptions>(builder.Configuration);

// ---- Database: ALWAYS AddDbContextFactory, NEVER AddDbContext (Blazor Server DbContext lifetime rule) ----
builder.Services.AddDbContextFactory<LunchOrganizerDbContext>((sp, options) =>
{
    var dbOptions = sp.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;
    // CommandTimeoutSeconds is applied here (not as a connection-string keyword) — see the matching
    // comment in DatabaseOptionsExtensions.BuildConnectionString.
    options.UseSqlServer(dbOptions.BuildConnectionString(), sql => sql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
});

// Fully implemented already — safe to wire up now regardless of backend/fakes swap:
builder.Services.AddSingleton<IBookingChangeNotifier, BookingChangeNotifier>();

// ---- Localization: resource-based, French-default / English-toggle (see config/app.json) ----
builder.Services.AddLocalization();
builder.Services.AddScoped<LunchOrganizer.Web.Localization.CultureState>();
builder.Services.AddScoped<LunchOrganizer.Web.Localization.IErrorMessageResolver, LunchOrganizer.Web.Localization.ErrorMessageResolver>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IConfirmDialogService, ConfirmDialogService>();
builder.Services.AddScoped<LunchOrganizer.Web.ViewModels.BookingViewModel>();
builder.Services.AddScoped<LunchOrganizer.Web.ViewModels.ReportViewModel>();
builder.Services.AddScoped<LunchOrganizer.Web.ViewModels.AdminEmployeesViewModel>();
builder.Services.AddScoped<LunchOrganizer.Web.ViewModels.AdminMenusViewModel>();

// Validate the configured cultures against the host at startup. The DI container isn't built
// yet, so a minimal standalone logger factory is used just for this block; never throw out of
// here — an invalid culture entry is dropped/replaced with a warning instead of crashing.
using (var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole()))
{
    var startupLogger = startupLoggerFactory.CreateLogger("Startup.Localization");

    var appOptionsForCulture = builder.Configuration.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();

    var effectiveCultures = new List<CultureInfo>();
    foreach (var cultureName in appOptionsForCulture.SupportedCultures)
    {
        try
        {
            effectiveCultures.Add(CultureInfo.GetCultureInfo(cultureName));
        }
        catch (CultureNotFoundException)
        {
            startupLogger.LogWarning("Configured supported culture '{CultureName}' is not a valid culture on this host and will be ignored.", cultureName);
        }
    }

    CultureInfo? effectiveDefaultCulture = null;
    try
    {
        effectiveDefaultCulture = CultureInfo.GetCultureInfo(appOptionsForCulture.DefaultCulture);
    }
    catch (CultureNotFoundException)
    {
        startupLogger.LogWarning("Configured default culture '{CultureName}' is not a valid culture on this host.", appOptionsForCulture.DefaultCulture);
    }

    if (effectiveDefaultCulture is null || !effectiveCultures.Any(c => c.Name.Equals(effectiveDefaultCulture.Name, StringComparison.OrdinalIgnoreCase)))
    {
        effectiveDefaultCulture = effectiveCultures.FirstOrDefault();
        if (effectiveDefaultCulture is null)
        {
            startupLogger.LogWarning("No valid supported culture is configured; falling back to en-US for both default and supported culture.");
            effectiveDefaultCulture = CultureInfo.GetCultureInfo("en-US");
            effectiveCultures.Add(effectiveDefaultCulture);
        }
        else
        {
            startupLogger.LogWarning("Falling back to '{CultureName}' as the default culture.", effectiveDefaultCulture.Name);
        }
    }

    builder.Services.Configure<RequestLocalizationOptions>(options =>
    {
        options.DefaultRequestCulture = new RequestCulture(effectiveDefaultCulture);
        options.SupportedCultures = effectiveCultures;
        options.SupportedUICultures = effectiveCultures;
    });
}

// ---- Real, SQL Server-backed repositories and business services (replaces the Phase-1 fakes) ----
builder.Services.AddLunchOrganizerData();
builder.Services.AddLunchOrganizerServices();

// ---- Email pipeline + optional in-app daily summary scheduler (plan §8.2) ----
// These registrations use the same lifetimes as LunchOrganizer.Mailer.Program.cs: scoped for
// anything that consumes the scoped, EF Core-backed repositories from AddLunchOrganizerData()
// above, singleton for everything else. DailySummaryHostedService self-gates on
// Email:EnableInAppScheduler (default false), so registering it here unconditionally is always
// safe even when the flag is off. In normal production use, the daily summary is actually sent
// by LunchOrganizer.Mailer.exe, run once a day under Windows Task Scheduler — this in-app
// scheduler exists only as a development-time convenience. If both were ever enabled at once
// for the same day, the email_log reservation (see IEmailLogRepository) guarantees that exactly
// one of them wins and only a single email is sent.
builder.Services.AddScoped<IDailySummaryBuilder, DailySummaryBuilder>();
builder.Services.AddSingleton<IDailySummaryBodyRenderer, DailySummaryBodyRenderer>();

// Per-employee booking confirmations (plan §4.9): the renderer is stateless like the summary
// renderer above, so singleton; the sender is scoped only to keep its lifetime consistent with the
// scoped IDailySummaryMailService it sits alongside — it consumes nothing scoped today.
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

builder.Services.AddScoped<IDailySummaryMailService, DailySummaryMailService>();
builder.Services.AddDailySummaryScheduler();

// ---- Admin authentication: cookie auth backed by config/admin-users.json (plan §6.5) ----
// Login/logout happen through plain HTTP endpoints (Endpoints/AdminAuthEndpoints.cs), never
// through a C#-invoked SignInAsync/SignOutAsync inside an interactive Server circuit — see the
// render-mode note in the implementation plan for why.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.Cookie.Name = "LunchOrganizer.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var username = context.Principal?.Identity?.Name;
            var adminUsersMonitor = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AdminUsersOptions>>();

            var stillExists = username is not null &&
                adminUsersMonitor.CurrentValue.Users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            if (!stillExists)
            {
                // The account was removed (or renamed) from admin-users.json since this cookie
                // was issued — invalidate the session immediately rather than waiting for the
                // cookie to expire on its own.
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ---- Rate limiting: 5 attempts/minute/IP on the admin login POST endpoint (plan §6.5) ----
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("admin-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
            QueueLimit = 0,
        }));

    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.Redirect("/admin/login?error=ratelimited");
        return ValueTask.CompletedTask;
    };
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// ---- Ensure the database exists and is migrated (and optionally seeded) before serving requests ----
using (var startupScope = app.Services.CreateScope())
{
    var bootstrapper = startupScope.ServiceProvider.GetRequiredService<LunchOrganizer.Data.Abstractions.IDatabaseBootstrapper>();
    await bootstrapper.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseRateLimiter();

app.MapStaticAssets();
app.MapAdminAuthEndpoints();
app.MapAdminReportEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
