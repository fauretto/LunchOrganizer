using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Web.Identity;

/// <summary>
/// Resolves a Windows account name against Active Directory to enrich it with a display name and
/// mail address. Every failure mode — an unreachable domain, a machine off the domain, a
/// permissions error, a malformed name, a timeout — degrades to "record the account name only";
/// nothing here is ever allowed to throw out to a caller or to block a booking.
/// </summary>
public sealed class ActiveDirectoryPcUserResolver(
    IOptionsMonitor<PcUserOptions> options,
    IMemoryCache cache,
    ILogger<ActiveDirectoryPcUserResolver> logger) : IPcUserResolver
{
    /// <inheritdoc />
    public async Task<PcUserInfo> ResolveAsync(string? windowsUserName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsUserName))
        {
            return PcUserInfo.Empty;
        }

        // The name-only result is what every failure path below falls back to.
        var nameOnly = PcUserInfo.Create(windowsUserName, null, null);

        try
        {
            var currentOptions = options.CurrentValue;

            if (!currentOptions.ResolveDirectoryDetails)
            {
                return nameOnly;
            }

            if (!OperatingSystem.IsWindows())
            {
                logger.LogDebug("Skipping Active Directory lookup for {User}: not running on Windows.", windowsUserName);
                return nameOnly;
            }

            var cacheKey = $"pc-user:{windowsUserName.ToLowerInvariant()}";
            if (cache.TryGetValue<PcUserInfo>(cacheKey, out var cached))
            {
                return cached!;
            }

            var samAccountName = ExtractSamAccountName(windowsUserName);
            if (string.IsNullOrEmpty(samAccountName))
            {
                return nameOnly;
            }

            var result = await LookupWithTimeoutAsync(windowsUserName, samAccountName, currentOptions, ct);

            cache.Set(cacheKey, result, TimeSpan.FromMinutes(Math.Max(1, currentOptions.CacheMinutes)));

            return result;
        }
        catch (Exception ex)
        {
            // Deliberately catching every exception: a directory that is unreachable, a machine off
            // the domain, a permissions failure, or a malformed name must all degrade to "record the
            // name only", never surface to the caller.
            logger.LogWarning(ex, "Active Directory lookup for {User} failed; recording the account name only.", windowsUserName);
            return nameOnly;
        }
    }

    // Callable only from the OperatingSystem.IsWindows()-guarded branch of ResolveAsync above.
    [SupportedOSPlatform("windows")]
    private async Task<PcUserInfo> LookupWithTimeoutAsync(
        string windowsUserName, string samAccountName, PcUserOptions currentOptions, CancellationToken ct)
    {
        var nameOnly = PcUserInfo.Create(windowsUserName, null, null);

        var lookupTask = Task.Run(() => LookupInDirectory(windowsUserName, samAccountName), ct);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, currentOptions.DirectoryTimeoutSeconds)), ct);

        var winner = await Task.WhenAny(lookupTask, timeoutTask);

        if (winner == timeoutTask)
        {
            logger.LogWarning(
                "Active Directory lookup for {User} exceeded {Timeout}s; recording the account name only.",
                windowsUserName, currentOptions.DirectoryTimeoutSeconds);

            // The abandoned lookup task must never surface as an unobserved task exception.
            _ = lookupTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

            return nameOnly;
        }

        return await lookupTask;
    }

    [SupportedOSPlatform("windows")]
    private static PcUserInfo LookupInDirectory(string windowsUserName, string samAccountName)
    {
        using var ctx = new PrincipalContext(ContextType.Domain);
        using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, samAccountName);

        if (user is null)
        {
            return PcUserInfo.Create(windowsUserName, null, null);
        }

        // The first argument stays the original domain-qualified name; the stripped sam account
        // name is only used for the directory query above.
        return PcUserInfo.Create(windowsUserName, user.DisplayName, user.EmailAddress);
    }

    private static string ExtractSamAccountName(string windowsUserName)
    {
        var afterDomain = windowsUserName.Contains('\\')
            ? windowsUserName[(windowsUserName.LastIndexOf('\\') + 1)..]
            : windowsUserName;

        var atIndex = afterDomain.IndexOf('@');
        return atIndex >= 0 ? afterDomain[..atIndex] : afterDomain;
    }
}
