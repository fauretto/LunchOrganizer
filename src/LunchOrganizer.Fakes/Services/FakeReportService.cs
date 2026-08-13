using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeReportService(FakeDataStore store) : IReportService
{
    public Task<ReportDto> GetReportAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var lines = store.Bookings.Values
            .Where(b => b.BookingDate >= from && b.BookingDate <= to && (employeeId == null || b.EmployeeId == employeeId))
            .Select(b =>
            {
                store.Employees.TryGetValue(b.EmployeeId, out var employee);
                store.Menus.TryGetValue(b.MenuId, out var menu);
                return new ReportLineDto(
                    b.BookingDate,
                    b.BookingDate.DayOfWeek,
                    employee?.FullName ?? string.Empty,
                    menu?.MenuNumber ?? 0,
                    menu?.Description,
                    b.PriceSnapshot);
            })
            .OrderBy(l => l.Date)
            .ThenBy(l => l.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = lines.Sum(l => l.Price);
        return Task.FromResult(new ReportDto(lines, total));
    }
}
