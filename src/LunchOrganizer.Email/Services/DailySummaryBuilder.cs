using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Localization;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email.Services;

/// <summary>
/// Builds the <see cref="DailySummaryDto"/> for a given date from a single repository query,
/// grouping bookings by menu and listing the distinct employee names who booked each menu.
/// </summary>
public sealed class DailySummaryBuilder(
    IBookingRepository bookingRepository,
    IOptionsMonitor<EmailOptions> emailOptions,
    IClock clock) : IDailySummaryBuilder
{
    public async Task<OperationResult<DailySummaryDto>> BuildAsync(DateOnly date, CancellationToken ct = default)
    {
        var bookings = await bookingRepository.GetForDateAsync(date, ct);

        if (bookings.Count == 0)
        {
            return OperationResult<DailySummaryDto>.Fail($"No bookings exist for {date:yyyy-MM-dd}.", ErrorCodes.NoBookingsForDate);
        }

        var culture = EmailCulture.Resolve(emailOptions.CurrentValue.Language);
        var comparer = StringComparer.Create(culture, ignoreCase: true);

        var groups = bookings
            .Where(b => b.Menu is not null && b.Employee is not null)
            .GroupBy(b => b.MenuId)
            .Select(g =>
            {
                var menu = g.First().Menu!;
                var names = g
                    .Select(b => b.Employee!.FullName)
                    .Distinct(comparer)
                    .OrderBy(n => n, comparer)
                    .ToList();

                return new DailySummaryMenuGroupDto(g.Key, menu.MenuNumber, menu.Description, names);
            })
            .Where(g => g.EmployeeNames.Count > 0)
            .OrderBy(g => g.MenuNumber)
            .ToList();

        // Same rows, same guard, as the menu grouping above — carried out of the one query the
        // summary already runs so the confirmation feature needs no second repository call
        // (plan §4.3, and the one-query rule in plan §11.8).
        var employeeBookings = bookings
            .Where(b => b.Menu is not null && b.Employee is not null)
            .OrderBy(b => b.Employee!.FullName, comparer)
            .Select(b => new EmployeeBookingConfirmationDto(
                b.EmployeeId,
                b.Employee!.FullName,
                b.Employee!.Email,
                b.Menu!.MenuNumber,
                b.Menu!.Description,
                b.PriceSnapshot,
                b.UserName,
                b.UserFullName,
                b.UserEmail))
            .ToList();

        var summary = new DailySummaryDto(date, bookings.Count, groups, clock.UtcNow, employeeBookings);
        return OperationResult<DailySummaryDto>.Ok(summary);
    }
}
