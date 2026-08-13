using Microsoft.Extensions.Localization;

namespace LunchOrganizer.Web.Formatting;

/// <summary>
/// Turns a <see cref="TimeSpan"/> remaining-until-cut-off into a natural, localized phrase
/// (e.g. "23 minutes", "1 hour 5 minutes", "2 hours"). Callers are responsible for not invoking
/// this once the cut-off has actually passed; the zero/negative handling here is defensive only.
/// </summary>
public static class DurationPhraseFormatter
{
    public static string Format(TimeSpan remaining, IStringLocalizer<LunchOrganizer.Web.Resources.Booking> loc)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return loc["RemainingTimeLessThanMinute"];
        }

        var totalMinutes = (int)remaining.TotalMinutes;

        if (totalMinutes < 1)
        {
            return loc["RemainingTimeLessThanMinute"];
        }

        if (totalMinutes < 60)
        {
            return totalMinutes == 1
                ? loc["RemainingTimeMinuteSingular"]
                : loc["RemainingTimeMinutesPluralTemplate", totalMinutes];
        }

        var hours = totalMinutes / 60;
        var leftoverMinutes = totalMinutes % 60;

        if (leftoverMinutes == 0)
        {
            return hours == 1
                ? loc["RemainingTimeHourSingularExact"]
                : loc["RemainingTimeHoursPluralExactTemplate", hours];
        }

        return hours == 1
            ? loc["RemainingTimeHourSingularWithMinutesTemplate", leftoverMinutes]
            : loc["RemainingTimeHoursPluralWithMinutesTemplate", hours, leftoverMinutes];
    }
}
