using System.Security.Principal;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Identity;

/// <summary>
/// Scoped implementation of <see cref="IPcUserContext"/> — one instance per HTTP request and one
/// per Blazor circuit. Resolves the PC user once per scope, trying (in order) a value already set
/// by <see cref="PcUserCaptureMiddleware"/>, a value persisted from the prerendering pass, the
/// current circuit's authentication state (only if it is genuinely a Windows identity), and —
/// only in development — the server process's own identity.
/// </summary>
public sealed class PcUserContext(
    IOptionsMonitor<PcUserOptions> options,
    PersistentComponentState persistentState,
    AuthenticationStateProvider authenticationStateProvider,
    IPcUserResolver resolver,
    IHostEnvironment hostEnvironment,
    ILogger<PcUserContext> logger) : IPcUserContext
{
    private PcUserInfo? _value;
    private bool _resolved;

    /// <inheritdoc />
    public void Set(PcUserInfo value)
    {
        _value = value;
        _resolved = true;
    }

    /// <inheritdoc />
    public async Task<PcUserInfo> GetCurrentAsync(CancellationToken ct = default)
    {
        if (!options.CurrentValue.Enabled)
        {
            return PcUserInfo.Empty;
        }

        if (_resolved)
        {
            return _value ?? PcUserInfo.Empty;
        }

        try
        {
            var result = await ResolveAsync(ct);
            _value = result;
            _resolved = true;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve the PC user for the current scope.");
            _value = PcUserInfo.Empty;
            _resolved = true;
            return PcUserInfo.Empty;
        }
    }

    private async Task<PcUserInfo> ResolveAsync(CancellationToken ct)
    {
        // Layer 2: a value persisted during the static-rendering pass, for the circuit to pick up.
        try
        {
            if (persistentState.TryTakeFromJson<PcUserPayload>("pc-user", out var payload) && payload is not null)
            {
                var fromPayload = PcUserInfo.Create(payload.UserName, payload.UserFullName, payload.UserEmail);
                if (fromPayload.HasAttribution)
                {
                    return fromPayload;
                }
            }
        }
        catch (Exception ex)
        {
            // TryTakeFromJson is only valid during component/service initialisation in the circuit;
            // outside that window it can return false or throw.
            logger.LogDebug(ex, "Could not read the persisted PC user from PersistentComponentState.");
        }

        // Layer 3: the circuit's own authentication state, accepted only if it is genuinely a
        // Windows identity — a logged-in admin's cookie principal must never be mistaken for it.
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var authUser = authState.User;
        if (PcUserCaptureMiddleware.IsWindowsIdentity(authUser))
        {
            var resolved = await resolver.ResolveAsync(authUser.Identity?.Name, ct);
            if (resolved.HasAttribution)
            {
                return resolved;
            }
        }

        // Layer 4: development-only fallback that records the SERVER PROCESS identity, never valid
        // in production.
        var currentOptions = options.CurrentValue;
        if (currentOptions.AllowServerUserFallbackInDevelopment && hostEnvironment.IsDevelopment() && OperatingSystem.IsWindows())
        {
            var serverUserName = WindowsIdentity.GetCurrent()?.Name;
            logger.LogWarning(
                "Using the SERVER PROCESS identity ({User}) as the PC user fallback — this is only valid on a developer machine and is never correct in production.",
                serverUserName);

            var resolved = await resolver.ResolveAsync(serverUserName, ct);
            if (resolved.HasAttribution)
            {
                return resolved;
            }
        }

        return PcUserInfo.Empty;
    }
}

/// <summary>
/// Plain DTO shape persisted via <see cref="PersistentComponentState"/> — deliberately not
/// <see cref="PcUserInfo"/> itself, whose computed <see cref="PcUserInfo.HasAttribution"/> property
/// and static members make it a poor serialisation target.
/// </summary>
internal sealed record PcUserPayload(string? UserName, string? UserFullName, string? UserEmail);
