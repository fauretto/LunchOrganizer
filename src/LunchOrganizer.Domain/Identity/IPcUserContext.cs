namespace LunchOrganizer.Domain.Identity;

/// <summary>
/// The per-scope access point for the PC user: one instance per HTTP request and one per Blazor
/// circuit.
/// </summary>
public interface IPcUserContext
{
    /// <summary>
    /// Returns the PC user resolved for the current scope. Never throws and returns
    /// <see cref="PcUserInfo.Empty"/> when nothing is known.
    /// </summary>
    Task<PcUserInfo> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Called by the HTTP-request capture middleware to record the resolved PC user.</summary>
    void Set(PcUserInfo value);
}
