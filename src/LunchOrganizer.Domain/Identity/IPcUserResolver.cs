namespace LunchOrganizer.Domain.Identity;

/// <summary>
/// Resolves a Windows account name into a <see cref="PcUserInfo"/> enriched with the directory's
/// display name and mail address when they can be obtained.
/// </summary>
public interface IPcUserResolver
{
    /// <summary>
    /// Takes a Windows account name as it appears in a Windows token (<c>DOMAIN\samAccountName</c>,
    /// or a bare <c>samAccountName</c>) and returns a <see cref="PcUserInfo"/> enriched with the
    /// directory's display name and mail when they can be obtained. Implementations must never
    /// throw and must never block indefinitely: on any failure they return at least the name they
    /// were given, and <see cref="PcUserInfo.Empty"/> if even that was absent.
    /// </summary>
    Task<PcUserInfo> ResolveAsync(string? windowsUserName, CancellationToken ct = default);
}
