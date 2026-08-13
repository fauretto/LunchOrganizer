using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Services;

public sealed class ReportService(IBookingRepository bookingRepo) : IReportService
{
    public async Task<ReportDto> GetReportAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var bookings = await bookingRepo.GetForEmployeeBetweenAsync(employeeId, from, to, ct);

        var lines = bookings
            .Select(b => new ReportLineDto(
                b.BookingDate,
                b.BookingDate.DayOfWeek,
                b.Employee!.FullName,
                b.Menu!.MenuNumber,
                b.Menu!.Description,
                b.PriceSnapshot))
            .OrderBy(l => l.Date)
            .ThenBy(l => l.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = lines.Sum(l => l.Price);
        return new ReportDto(lines, total);
    }
}
