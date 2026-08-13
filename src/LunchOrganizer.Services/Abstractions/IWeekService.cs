using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IWeekService
{
    Task<WeekIdentifier> GetWeekIdentifierAsync(DateOnly anyDateInWeek, CancellationToken ct = default);
    Task<WeekIdentifier> GetCurrentWeekAsync(CancellationToken ct = default);
    Task<WeekIdentifier> GetNextWeekAsync(WeekIdentifier current, CancellationToken ct = default);
    Task<WeekIdentifier> GetPreviousWeekAsync(WeekIdentifier current, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(DateOnly monday, CancellationToken ct = default);
}
