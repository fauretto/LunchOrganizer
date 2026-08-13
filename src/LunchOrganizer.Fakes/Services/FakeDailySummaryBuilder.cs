using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeDailySummaryBuilder(FakeDataStore store, IClock clock) : IDailySummaryBuilder
{
    public Task<OperationResult<DailySummaryDto>> BuildAsync(DateOnly date, CancellationToken ct = default)
    {
        var bookingsForDate = store.Bookings.Values.Where(b => b.BookingDate == date).ToList();

        var groups = bookingsForDate
            .GroupBy(b => b.MenuId)
            .Select(g =>
            {
                store.Menus.TryGetValue(g.Key, out var menu);
                var names = g
                    .Select(b => store.Employees.TryGetValue(b.EmployeeId, out var e) ? e.FullName : string.Empty)
                    .Where(n => n.Length > 0)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new DailySummaryMenuGroupDto(g.Key, menu?.MenuNumber ?? 0, menu?.Description, names);
            })
            .Where(g => g.EmployeeNames.Count > 0)
            .OrderBy(g => g.MenuNumber)
            .ToList();

        var summary = new DailySummaryDto(date, bookingsForDate.Count, groups, clock.UtcNow);
        return Task.FromResult(OperationResult<DailySummaryDto>.Ok(summary));
    }
}
