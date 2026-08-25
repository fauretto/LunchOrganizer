namespace LunchOrganizer.Domain.Configuration;

/// <summary>
/// Configuration for the optional, best-effort capture of the Windows user of the client PC that
/// made a booking. Every setting here is a knob on a feature that must never block, throw, or
/// prevent a booking from succeeding — see <see cref="Identity.PcUserInfo"/> for the value it
/// populates.
/// </summary>
public sealed class PcUserOptions
{
    public const string SectionName = "PcUser";

    /// <summary>
    /// Master switch. When <c>false</c>, the whole feature is inert and the PC user context always
    /// returns <see cref="Identity.PcUserInfo.Empty"/> without touching Active Directory or the HTTP
    /// context.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <c>false</c>, only the <c>domain\username</c> is recorded; no Active Directory lookup
    /// happens at all (so no full name, no email).
    /// </summary>
    public bool ResolveDirectoryDetails { get; set; } = true;

    /// <summary>Hard ceiling, in seconds, on the Active Directory lookup.</summary>
    public int DirectoryTimeoutSeconds { get; set; } = 3;

    /// <summary>How long, in minutes, a resolved Active Directory result is cached per user.</summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>
    /// When <c>true</c>, allows a development-only fallback that records the identity of the SERVER
    /// PROCESS running the app when nothing else is available. This is deliberately wrong in
    /// production — on a real deployment it would record the IIS application pool account, not the
    /// client PC's user — and it exists only so the feature can be exercised on a developer machine,
    /// where the server process happens to run as the developer.
    /// </summary>
    public bool AllowServerUserFallbackInDevelopment { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, exposes the temporary <c>GET /whoami</c> diagnostic page that reports what
    /// Windows identity (if any) the app receives. Must be <c>false</c> in production.
    /// </summary>
    public bool Diagnostics { get; set; } = false;
}
