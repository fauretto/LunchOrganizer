using System.Globalization;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Services;

public sealed class WeekService(IClock clock) : IWeekService
{
    public Task<WeekIdentifier> GetWeekIdentifierAsync(DateOnly anyDateInWeek, CancellationToken ct = default)
    {
        var diff = (int)anyDateInWeek.DayOfWeek == 0 ? 6 : (int)anyDateInWeek.DayOfWeek - 1;
        var monday = anyDateInWeek.AddDays(-diff);

        var mondayDateTime = monday.ToDateTime(TimeOnly.MinValue);
        var isoYear = ISOWeek.GetYear(mondayDateTime);
        var isoWeek = ISOWeek.GetWeekOfYear(mondayDateTime);
        var label = $"{isoYear:D4}-W{isoWeek:D2}";

        return Task.FromResult(new WeekIdentifier(isoYear, isoWeek, monday, label));
    }

    public Task<WeekIdentifier> GetCurrentWeekAsync(CancellationToken ct = default) =>
        GetWeekIdentifierAsync(clock.Today, ct);

    public Task<WeekIdentifier> GetNextWeekAsync(WeekIdentifier current, CancellationToken ct = default) =>
        GetWeekIdentifierAsync(current.Monday.AddDays(7), ct);

    public Task<WeekIdentifier> GetPreviousWeekAsync(WeekIdentifier current, CancellationToken ct = default) =>
        GetWeekIdentifierAsync(current.Monday.AddDays(-7), ct);

    public Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(DateOnly monday, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DateOnly>>(
        [
            monday,
            monday.AddDays(1),
            monday.AddDays(2),
            monday.AddDays(3),
            monday.AddDays(4)
        ]);
}
