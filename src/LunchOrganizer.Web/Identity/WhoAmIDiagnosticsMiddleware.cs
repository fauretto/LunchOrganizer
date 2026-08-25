using System.Security.Claims;
using System.Text;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Identity;

/// <summary>
/// TEMPORARY diagnostic middleware that reports what Windows identity (if any) the app receives
/// for the current request. Gated by <see cref="PcUserOptions.Diagnostics"/> — must be removed
/// before production.
/// </summary>
public sealed class WhoAmIDiagnosticsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<PcUserOptions> options,
        IAuthenticationSchemeProvider schemeProvider,
        ILogger<WhoAmIDiagnosticsMiddleware> logger)
    {
        if (!string.Equals(context.Request.Path, "/whoami", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!options.CurrentValue.Diagnostics)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var sb = new StringBuilder();

        try
        {
            sb.AppendLine("=== 1. PRE-AUTHENTICATION HttpContext.User (this is the one that matters) ===");
            var user = context.User;
            var identity = user.Identity;
            sb.AppendLine($"IsAuthenticated: {identity?.IsAuthenticated}");
            sb.AppendLine($"Identity.Name: {identity?.Name ?? "(null)"}");
            sb.AppendLine($"Identity.AuthenticationType: {identity?.AuthenticationType ?? "(null)"}");
            sb.AppendLine($"Identity CLR type: {identity?.GetType().FullName}");
            sb.AppendLine($"Is WindowsIdentity instance: {identity is System.Security.Principal.WindowsIdentity}");
            sb.AppendLine($"Passes PcUserCaptureMiddleware.IsWindowsIdentity gate: {PcUserCaptureMiddleware.IsWindowsIdentity(user)}");

            var claims = user.Claims.ToList();
            sb.AppendLine($"Claim count: {claims.Count}");
            foreach (var claim in claims.Take(15))
            {
                sb.AppendLine($"  {claim.Type} = {claim.Value}");
            }

            sb.AppendLine();
            sb.AppendLine("=== 2. IIS SERVER VARIABLES (ground truth from IIS) ===");
            var serverVariables = context.Features.Get<IServerVariablesFeature>();
            if (serverVariables is null)
            {
                sb.AppendLine("IServerVariablesFeature not available (app is probably not hosted by IIS).");
            }
            else
            {
                string[] variableNames = ["LOGON_USER", "AUTH_TYPE", "AUTH_USER", "REMOTE_USER"];
                foreach (var name in variableNames)
                {
                    try
                    {
                        var value = serverVariables[name];
                        sb.AppendLine($"  {name} = {(string.IsNullOrEmpty(value) ? "(empty)" : value)}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"  {name} = (error reading variable: {ex.GetType().Name}: {ex.Message})");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== 3. REQUEST AUTH HEADER ===");
            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) && authHeader.Count > 0)
            {
                var headerValue = authHeader[0] ?? string.Empty;
                var spaceIndex = headerValue.IndexOf(' ');
                var scheme = spaceIndex >= 0 ? headerValue[..spaceIndex] : headerValue;
                var remainderLength = spaceIndex >= 0 ? headerValue.Length - spaceIndex - 1 : 0;
                sb.AppendLine($"Authorization header present. Scheme: {scheme}, remainder length: {remainderLength}");
            }
            else
            {
                sb.AppendLine("Authorization header present: false");
            }

            sb.AppendLine($"WWW-Authenticate response header set: {context.Response.Headers.ContainsKey("WWW-Authenticate")}");

            sb.AppendLine();
            sb.AppendLine("=== 4. REGISTERED AUTHENTICATION SCHEMES ===");
            var schemes = await schemeProvider.GetAllSchemesAsync();
            foreach (var scheme in schemes)
            {
                sb.AppendLine($"  {scheme.Name} -> {scheme.HandlerType.Name}");
            }

            var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
            sb.AppendLine($"Default authenticate scheme: {defaultScheme?.Name ?? "(null)"}");

            sb.AppendLine();
            sb.AppendLine("=== 5. ACTIVE DIRECTORY LOOKUP FOR THE ABOVE NAME ===");
            var resolver = context.RequestServices.GetService<IPcUserResolver>();
            if (resolver is null)
            {
                sb.AppendLine("IPcUserResolver is not registered.");
            }
            else
            {
                var nameToResolve = identity?.Name;
                if (string.IsNullOrEmpty(nameToResolve))
                {
                    sb.AppendLine("Skipped — no name to look up.");
                }
                else
                {
                    try
                    {
                        var info = await resolver.ResolveAsync(nameToResolve, context.RequestAborted);
                        sb.AppendLine($"UserName: {info.UserName}");
                        sb.AppendLine($"UserFullName: {info.UserFullName}");
                        sb.AppendLine($"UserEmail: {info.UserEmail}");
                        sb.AppendLine($"HasAttribution: {info.HasAttribution}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"Lookup failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== 6. SERVER PROCESS IDENTITY (WRONG ANSWER — for contrast only) ===");
            sb.AppendLine($"Environment.UserName: {Environment.UserName}");
            string? currentWindowsIdentityName = null;
            if (OperatingSystem.IsWindows())
            {
                currentWindowsIdentityName = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name;
            }

            sb.AppendLine($"WindowsIdentity.GetCurrent()?.Name: {currentWindowsIdentityName ?? "(null)"}");
            sb.AppendLine("If section 1 shows this same name, Windows Auth is NOT working and the app is seeing itself.");

            sb.AppendLine();
            sb.AppendLine("=== 7. HOSTING ===");
            sb.AppendLine($"Scheme: {context.Request.Scheme}");
            sb.AppendLine($"Host: {context.Request.Host}");
            sb.AppendLine($"Path: {context.Request.Path}");
            sb.AppendLine($"RemoteIpAddress: {context.Connection.RemoteIpAddress}");
            sb.AppendLine($"ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(not set)"}");
            sb.AppendLine($"ASPNETCORE_IIS_HTTPAUTH: {Environment.GetEnvironmentVariable("ASPNETCORE_IIS_HTTPAUTH") ?? "(not set)"}");
        }
        catch (Exception ex)
        {
            sb.AppendLine();
            sb.AppendLine($"Failed to generate the full diagnostics report: {ex.GetType().FullName}: {ex.Message}");
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(sb.ToString());
    }
}

/// <summary>
/// Registers <see cref="WhoAmIDiagnosticsMiddleware"/> in the request pipeline.
/// </summary>
public static class WhoAmIDiagnosticsMiddlewareExtensions
{
    public static IApplicationBuilder UseWhoAmIDiagnostics(this IApplicationBuilder app) =>
        app.UseMiddleware<WhoAmIDiagnosticsMiddleware>();
}
