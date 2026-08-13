using System.Globalization;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeWeekService(IClock clock) : IWeekService
{
    public Task<WeekIdentifier> GetWeekIdentifierAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var monday = DateHelpers.MondayOf(anyDateInWeek);
        var friday = monday.AddDays(4);
        var mondayDateTime = monday.ToDateTime(TimeOnly.MinValue);
        var isoYear = ISOWeek.GetYear(mondayDateTime);
        var isoWeek = ISOWeek.GetWeekOfYear(mondayDateTime);
        var label = $"Week {isoWeek} · {monday:ddd d} → {friday:ddd d MMMM yyyy}";

        return Task.FromResult(new WeekIdentifier(isoYear, isoWeek, monday, label));
    }

    public Task<WeekIdentifier> GetCurrentWeekAsync(CancellationToken ct = default) =>
        GetWeekIdentifierAsync(clock.Today, ct);

    public Task<WeekIdentifier> GetNextWeekAsync(WeekIdentifier current, CancellationToken ct = default) =>
        GetWeekIdentifierAsync(current.Monday.AddDays(7), ct);

    public Task<WeekIdentifier> GetPreviousWeekAsync(WeekIdentifier current, CancellationToken ct = default) =>
        GetWeekIdentifierAsync(current.Monday.AddDays(-7), ct);

    public Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(DateOnly monday, CancellationToken ct = default) =>
        Task.FromResult(DateHelpers.WorkingDays(monday));
}
