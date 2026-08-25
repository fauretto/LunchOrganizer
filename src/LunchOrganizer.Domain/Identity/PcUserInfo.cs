namespace LunchOrganizer.Domain.Identity;

/// <summary>
/// Records the Windows user of the CLIENT PC that made a booking — a different person from the
/// employee the booking is for (e.g. a receptionist or colleague booking lunch on someone else's
/// behalf). All three properties are optional: <c>null</c> is a normal, expected, and totally fine
/// value for every one of them. Capturing this data is a nice-to-have and must never block a
/// booking.
/// </summary>
public sealed record PcUserInfo(string? UserName, string? UserFullName, string? UserEmail)
{
    /// <summary>An instance with all three properties <c>null</c>, for when no PC-user data is available.</summary>
    public static readonly PcUserInfo Empty = new(null, null, null);

    public const int MaxUserNameLength = 256;
    public const int MaxUserFullNameLength = 256;
    public const int MaxUserEmailLength = 320;

    /// <summary>
    /// True when there is a human-readable name to attribute the booking to (<see cref="UserName"/>
    /// or <see cref="UserFullName"/> is non-null and not just whitespace). <see cref="UserEmail"/> is
    /// deliberately not considered here — an email address alone does not count as attribution.
    /// </summary>
    public bool HasAttribution =>
        !string.IsNullOrWhiteSpace(UserName) || !string.IsNullOrWhiteSpace(UserFullName);

    /// <summary>
    /// The single normalisation point for building a <see cref="PcUserInfo"/>. Each input is trimmed
    /// independently, turned into <c>null</c> if the trimmed value is empty, and otherwise truncated
    /// to its corresponding <c>Max…Length</c> constant. This guards against a long Active Directory
    /// <c>displayName</c> (or similar) in exactly one place, so raw AD values can never fail a
    /// database <c>INSERT</c> on an nvarchar length constraint. Never throws, for any input,
    /// including <c>null</c>, empty, or whitespace-only strings.
    /// </summary>
    public static PcUserInfo Create(string? userName, string? userFullName, string? userEmail)
    {
        return new PcUserInfo(
            Normalize(userName, MaxUserNameLength),
            Normalize(userFullName, MaxUserFullNameLength),
            Normalize(userEmail, MaxUserEmailLength));

        static string? Normalize(string? value, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            return trimmed.Substring(0, Math.Min(trimmed.Length, maxLength));
        }
    }
}
