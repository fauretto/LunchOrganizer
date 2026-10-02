using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Services.Services;

public sealed class BookingService(
    IBookingRepository bookingRepo,
    IMenuRepository menuRepo,
    IEmployeeRepository employeeRepo,
    IPricingService pricingService,
    IClock clock,
    IOptionsMonitor<AppOptions> appOptions,
    IBookingChangeNotifier notifier,
    ILogger<BookingService> logger) : IBookingService
{
    public async Task<WeekViewDto> GetWeekViewAsync(int employeeId, WeekIdentifier week, CancellationToken ct = default)
    {
        var friday = week.Monday.AddDays(4);

        var allBookings = await bookingRepo.GetForEmployeeBetweenAsync(null, week.Monday, friday, ct);
        var menus = await menuRepo.GetByWeekAsync(week.Monday, ct);
        var prices = await pricingService.GetPricesForWeekAsync(week.Monday, ct);
        var employee = await employeeRepo.GetByIdAsync(employeeId, ct);

        var days = new List<DayViewDto>();
        var workingDays = new[] { week.Monday, week.Monday.AddDays(1), week.Monday.AddDays(2), week.Monday.AddDays(3), week.Monday.AddDays(4) };

        foreach (var date in workingDays)
        {
            var editState = EditStateHelper.Compute(date, clock, appOptions.CurrentValue.BookingCutOffLocalTime);
            var price = prices.First(p => p.PriceDate == date).Price;
            var dayMenus = menus
                .Where(m => m.MenuDate == date)
                .OrderBy(m => m.MenuNumber)
                .Select(m => new MenuDto(m.Id, m.MenuDate, m.MenuNumber, m.Description, allBookings.Count(b => b.MenuId == m.Id), m.Price)) // Price: per-menu override shown in the booking grid
                .ToList();
            var selectedMenuId = allBookings.FirstOrDefault(b => b.EmployeeId == employeeId && b.BookingDate == date)?.MenuId;

            days.Add(new DayViewDto(date, date.DayOfWeek, editState, price, dayMenus, selectedMenuId));
        }

        return new WeekViewDto(week, employeeId, employee?.FullName, days);
    }

    public async Task<OperationResult<BookingDto>> BookDayAsync(BookingRequest request, CancellationToken ct = default)
    {
        var editState = EditStateHelper.Compute(request.BookingDate, clock, appOptions.CurrentValue.BookingCutOffLocalTime);
        if (editState == DayEditState.LockedPast)
        {
            return OperationResult<BookingDto>.Fail("This day is in the past.", ErrorCodes.BookingDateInPast);
        }

        if (editState == DayEditState.LockedCutOff)
        {
            return OperationResult<BookingDto>.Fail(
                "The booking cut-off has passed for today.",
                ErrorCodes.BookingCutOffPassed,
                new object?[] { appOptions.CurrentValue.BookingCutOffLocalTime });
        }

        var menu = await menuRepo.GetByIdAsync(request.MenuId, ct);
        if (menu is null || menu.MenuDate != request.BookingDate)
        {
            return OperationResult<BookingDto>.Fail("Menu not found for that date.", ErrorCodes.MenuNotFoundForDay);
        }

        var employee = await employeeRepo.GetByIdAsync(request.EmployeeId, ct);
        if (employee is null)
        {
            return OperationResult<BookingDto>.Fail("Employee not found.", ErrorCodes.EmployeeNotFound);
        }

        var price = await pricingService.GetEffectivePriceAsync(menu, ct);

        logger.LogDebug(
            "Resolved booking price {Price} for {EmployeeId} on {BookingDate}, menu {MenuId}, source: {PriceSource}.",
            price, request.EmployeeId, request.BookingDate, request.MenuId, menu.Price is not null ? "menu override" : "day price");

        var saved = await bookingRepo.UpsertAsync(
            new Booking
            {
                EmployeeId = request.EmployeeId,
                BookingDate = request.BookingDate,
                MenuId = request.MenuId,
                PriceSnapshot = price,
                UserName = request.BookedBy?.UserName,
                UserFullName = request.BookedBy?.UserFullName,
                UserEmail = request.BookedBy?.UserEmail
            },
            ct);

        notifier.NotifyChanged(request.BookingDate);

        return OperationResult<BookingDto>.Ok(new BookingDto(
            saved.Id,
            employee.Id,
            employee.FullName,
            saved.BookingDate,
            saved.MenuId,
            menu.MenuNumber,
            menu.Description,
            saved.PriceSnapshot));
    }

    public async Task<OperationResult> CancelDayAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        var editState = EditStateHelper.Compute(date, clock, appOptions.CurrentValue.BookingCutOffLocalTime);
        if (editState == DayEditState.LockedPast)
        {
            return OperationResult.Fail("This day is in the past.", ErrorCodes.BookingDateInPast);
        }

        if (editState == DayEditState.LockedCutOff)
        {
            return OperationResult.Fail(
                "The booking cut-off has passed for today.",
                ErrorCodes.BookingCutOffPassed,
                new object?[] { appOptions.CurrentValue.BookingCutOffLocalTime });
        }

        await bookingRepo.DeleteAsync(employeeId, date, ct);
        notifier.NotifyChanged(date);

        return OperationResult.Ok();
    }

    public async Task<OperationResult<WeekBookingResultDto>> BookWeekAsync(WeekBookingRequest request, CancellationToken ct = default)
    {
        var days = new[] { request.Monday, request.Monday.AddDays(1), request.Monday.AddDays(2), request.Monday.AddDays(3), request.Monday.AddDays(4) };

        var skipped = new List<SkippedDayDto>();
        var toApply = new List<(DateOnly Date, int MenuId, decimal Price)>();

        foreach (var date in days)
        {
            var editState = EditStateHelper.Compute(date, clock, appOptions.CurrentValue.BookingCutOffLocalTime);

            if (editState == DayEditState.LockedPast)
            {
                skipped.Add(new SkippedDayDto(date, "Day is in the past", ErrorCodes.BookingDateInPast));
                continue;
            }

            if (editState == DayEditState.LockedCutOff)
            {
                skipped.Add(new SkippedDayDto(
                    date,
                    "Booking cut-off has passed for today",
                    ErrorCodes.BookingCutOffPassed,
                    new object?[] { appOptions.CurrentValue.BookingCutOffLocalTime }));
                continue;
            }

            var menu = (await menuRepo.GetByDateAsync(date, ct)).FirstOrDefault(m => m.MenuNumber == request.MenuNumber);
            if (menu is null)
            {
                skipped.Add(new SkippedDayDto(date, $"No Menu {request.MenuNumber} on this day", ErrorCodes.MenuNotFoundForDay));
                continue;
            }

            var price = await pricingService.GetEffectivePriceAsync(menu, ct);

            logger.LogDebug(
                "Resolved booking price {Price} for {EmployeeId} on {Date}, menu {MenuId}, source: {PriceSource}.",
                price, request.EmployeeId, date, menu.Id, menu.Price is not null ? "menu override" : "day price");

            toApply.Add((date, menu.Id, price));
        }

        var applied = new List<DateOnly>();

        if (toApply.Count > 0)
        {
            var bookings = toApply
                .Select(t => new Booking
                {
                    EmployeeId = request.EmployeeId,
                    BookingDate = t.Date,
                    MenuId = t.MenuId,
                    PriceSnapshot = t.Price,
                    UserName = request.BookedBy?.UserName,
                    UserFullName = request.BookedBy?.UserFullName,
                    UserEmail = request.BookedBy?.UserEmail
                })
                .ToList();

            await bookingRepo.UpsertManyAsync(bookings, ct);

            foreach (var d in toApply.Select(t => t.Date))
            {
                notifier.NotifyChanged(d);
            }

            applied.AddRange(toApply.Select(t => t.Date));
        }

        return OperationResult<WeekBookingResultDto>.Ok(new WeekBookingResultDto(applied, skipped));
    }
}
