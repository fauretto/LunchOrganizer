using System.Security.Claims;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Security;
using LunchOrganizer.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Endpoints;

/// <summary>
/// Plain ASP.NET Core minimal-API endpoints (not Blazor components) that perform the actual
/// cookie sign-in/sign-out for admin authentication. These exist outside the Blazor render-mode
/// boundary because an interactive Server circuit has no live mutable HTTP response to call
/// SignInAsync/SignOutAsync on — see the architectural note in the implementation plan.
/// </summary>
public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Antiforgery: relying on the app's global app.UseAntiforgery() middleware to validate
        // the token that Login.razor's <AntiforgeryToken /> renders into the static HTML form —
        // NOT calling .DisableAntiforgery() here. Verified end-to-end with curl (see summary).
        app.MapPost("/admin/login", HandleLoginAsync)
            .RequireRateLimiting("admin-login");

        // Explicit Delegate cast: HandleLogoutAsync's signature (HttpContext) -> Task<IResult> is
        // otherwise ambiguous with the RequestDelegate overload of MapPost (HttpContext) -> Task,
        // since Task<IResult> converts to Task — that overload would silently discard the
        // returned IResult and never write the redirect (see ASP0016).
        app.MapPost("/admin/logout", (Delegate)HandleLogoutAsync);

        return app;
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpContext context,
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        IOptionsMonitor<AdminUsersOptions> adminUsersMonitor,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("LunchOrganizer.Web.AdminAuth");

        var matchingUsers = adminUsersMonitor.CurrentValue.Users
            .Where(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingUsers.Count > 1)
        {
            logger.LogWarning(
                "admin-users.json contains duplicate entries for admin username '{Username}'. Only the first entry is used.",
                username);
        }

        var user = matchingUsers.FirstOrDefault();

        // Never log the supplied or stored password — only the attempted username.
        if (user is not null && !AdminPasswordHasher.IsSupportedFormat(user.Password))
        {
            logger.LogWarning(
                "Stored password for admin username '{Username}' is not in the required 'pbkdf2-sha256:' format. " +
                "Generate a new hash using the LunchOrganizer.AdminHash tool and update admin-users.json.",
                username);
        }

        if (user is null || !AdminPasswordHasher.Verify(password, user.Password))
        {
            logger.LogInformation("Admin login failed for username '{Username}'.", username);
            return Results.Redirect("/admin/login?error=invalid");
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim("DisplayName", user.DisplayName));
        // This is what the admin authorization policy requires: without it, an IIS
        // Windows-authenticated visitor who never signed in through this endpoint cannot
        // satisfy the policy, even though their Windows principal is also "authenticated".
        identity.AddClaim(new Claim(AdminAuthorization.AdminClaimType, AdminAuthorization.AdminClaimValue));
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        logger.LogInformation("Admin login succeeded for username '{Username}'.", username);

        var redirectTarget = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')
            ? returnUrl
            : "/admin";

        return Results.Redirect(redirectTarget);
    }

    private static async Task<IResult> HandleLogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/admin/login");
    }
}
