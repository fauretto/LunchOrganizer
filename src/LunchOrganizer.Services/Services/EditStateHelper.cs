using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;

namespace LunchOrganizer.Services.Services;

/// <summary>
/// Shared logic to compute a day's <see cref="DayEditState"/> against the current clock and the
/// configured booking cut-off time. Every service that needs edit-state logic should call
/// <see cref="Compute"/>, passing the cut-off freshly read from <c>IOptionsMonitor&lt;AppOptions&gt;.CurrentValue</c>
/// each time so that a config reload takes effect immediately.
/// </summary>
internal static class EditStateHelper
{
    public static DayEditState Compute(DateOnly date, IClock clock, TimeOnly cutOff)
    {
        if (date < clock.Today)
        {
            return DayEditState.LockedPast;
        }

        if (date == clock.Today && clock.LocalTimeOfDay >= cutOff)
        {
            return DayEditState.LockedCutOff;
        }

        return DayEditState.Editable;
    }
}
