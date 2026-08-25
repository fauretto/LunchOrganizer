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

        // Mirrors DailySummaryBuilder's EmployeeBookings so the Web project's fake data path stays
        // representative of the real one (plan §5).
        var employeeBookings = bookingsForDate
            .Select(b =>
            {
                store.Employees.TryGetValue(b.EmployeeId, out var employee);
                store.Menus.TryGetValue(b.MenuId, out var menu);
                return (Booking: b, Employee: employee, Menu: menu);
            })
            .Where(t => t.Employee is not null && t.Menu is not null)
            .OrderBy(t => t.Employee!.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(t => new EmployeeBookingConfirmationDto(
                t.Booking.EmployeeId,
                t.Employee!.FullName,
                t.Employee!.Email,
                t.Menu!.MenuNumber,
                t.Menu!.Description,
                t.Booking.PriceSnapshot,
                t.Booking.UserName,
                t.Booking.UserFullName,
                t.Booking.UserEmail))
            .ToList();

        var summary = new DailySummaryDto(date, bookingsForDate.Count, groups, clock.UtcNow, employeeBookings);
        return Task.FromResult(OperationResult<DailySummaryDto>.Ok(summary));
    }
}
