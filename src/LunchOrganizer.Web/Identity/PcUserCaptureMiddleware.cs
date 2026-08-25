using System.Security.Claims;
using System.Security.Principal;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Identity;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Identity;

/// <summary>
/// Captures the Windows identity IIS put on the incoming request, before anything else can
/// overwrite it.
/// </summary>
/// <remarks>
/// Its position in the pipeline is load-bearing: it must run BEFORE <c>UseAuthentication()</c>,
/// because the cookie handler replaces <see cref="HttpContext.User"/> for a logged-in admin and
/// would otherwise hide the Windows identity that IIS set.
/// </remarks>
public sealed class PcUserCaptureMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Accepted authentication types for a genuine Windows identity, beyond a plain
    /// <see cref="WindowsIdentity"/> instance.
    /// </summary>
    private static readonly string[] WindowsAuthenticationTypes = ["Negotiate", "NTLM", "Kerberos", "Windows"];

    public async Task InvokeAsync(
        HttpContext context,
        IPcUserContext pcUserContext,
        IPcUserResolver resolver,
        IOptionsMonitor<PcUserOptions> options,
        ILogger<PcUserCaptureMiddleware> logger)
    {
        if (!options.CurrentValue.Enabled)
        {
            await next(context);
            return;
        }

        try
        {
            var user = context.User;
            if (user.Identity is { IsAuthenticated: true } && IsWindowsIdentity(user))
            {
                var name = user.Identity.Name;
                var info = await resolver.ResolveAsync(name, context.RequestAborted);
                pcUserContext.Set(info);
            }
        }
        catch (Exception ex)
        {
            // A failure here must never break the request — log and move on.
            logger.LogWarning(ex, "Failed to capture the PC user for the current request.");
        }

        await next(context);
    }

    /// <summary>
    /// True when <paramref name="principal"/>'s identity is genuinely a Windows identity — either a
    /// <see cref="WindowsIdentity"/> instance, or an identity whose <c>AuthenticationType</c> is one
    /// of Negotiate/NTLM/Kerberos/Windows (case-insensitive). Without this guard, a logged-in
    /// admin's cookie principal could be mistaken for the PC user — a wrong answer that looks
    /// completely plausible in the database.
    /// </summary>
    internal static bool IsWindowsIdentity(ClaimsPrincipal? principal)
    {
        var identity = principal?.Identity;
        if (identity is null)
        {
            return false;
        }

        if (identity is WindowsIdentity)
        {
            return true;
        }

        return identity.AuthenticationType is not null &&
            WindowsAuthenticationTypes.Contains(identity.AuthenticationType, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Registers <see cref="PcUserCaptureMiddleware"/> in the request pipeline.
/// </summary>
public static class PcUserCaptureMiddlewareExtensions
{
    public static IApplicationBuilder UsePcUserCapture(this IApplicationBuilder app) =>
        app.UseMiddleware<PcUserCaptureMiddleware>();
}
