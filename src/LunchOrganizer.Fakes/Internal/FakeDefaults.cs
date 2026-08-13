namespace LunchOrganizer.Fakes.Internal;

/// <summary>Hardcoded business constants used by the fake implementations instead of reading config/*.json.</summary>
internal static class FakeDefaults
{
    public const decimal DefaultLunchPrice = 12.50m;
    public static readonly TimeOnly BookingCutOffLocalTime = new(9, 0);
}
