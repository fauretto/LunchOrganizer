using System.Security.Claims;
using LunchOrganizer.Domain.Configuration;
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

        var user = adminUsersMonitor.CurrentValue.Users
            .FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        // Never log the supplied or stored password — only the attempted username.
        if (user is null || !AdminPasswordVerifier.Verify(password, user.Password))
        {
            logger.LogInformation("Admin login failed for username '{Username}'.", username);
            return Results.Redirect("/admin/login?error=invalid");
        }

        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim("DisplayName", user.DisplayName));
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
