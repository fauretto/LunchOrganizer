namespace LunchOrganizer.Fakes.Internal;

/// <summary>Small shared date helpers used across the fake services (ISO Monday-start week math).</summary>
internal static class DateHelpers
{
    /// <summary>Returns the Monday of the ISO week containing <paramref name="date"/>.</summary>
    public static DateOnly MondayOf(DateOnly date)
    {
        var diff = (int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1;
        return date.AddDays(-diff);
    }

    /// <summary>Returns the five working days (Monday..Friday) starting at <paramref name="monday"/>.</summary>
    public static IReadOnlyList<DateOnly> WorkingDays(DateOnly monday) =>
        [monday, monday.AddDays(1), monday.AddDays(2), monday.AddDays(3), monday.AddDays(4)];
}
