using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeBookingService(
    FakeDataStore store,
    IClock clock,
    IMenuService menuService,
    IPricingService pricingService) : IBookingService
{
    public async Task<WeekViewDto> GetWeekViewAsync(int employeeId, WeekIdentifier week, CancellationToken ct = default)
    {
        store.Employees.TryGetValue(employeeId, out var employee);

        var days = new List<DayViewDto>();
        foreach (var date in DateHelpers.WorkingDays(week.Monday))
        {
            var editState = GetEditState(date);
            var price = await pricingService.GetEffectivePriceAsync(date, ct).ConfigureAwait(false);
            var menus = await menuService.GetForDateAsync(date, ct).ConfigureAwait(false);
            var selectedMenuId = store.Bookings.TryGetValue(FakeDataStore.BookingKey(employeeId, date), out var booking)
                ? booking.MenuId
                : (int?)null;

            days.Add(new DayViewDto(date, date.DayOfWeek, editState, price, menus, selectedMenuId));
        }

        return new WeekViewDto(week, employeeId, employee?.FullName, days);
    }

    public Task<OperationResult<BookingDto>> BookDayAsync(BookingRequest request, CancellationToken ct = default)
    {
        var editState = GetEditState(request.BookingDate);
        if (editState != DayEditState.Editable)
        {
            return Task.FromResult(OperationResult<BookingDto>.Fail("This day can no longer be booked.", "LOCKED"));
        }

        if (!store.Menus.TryGetValue(request.MenuId, out var menu) || menu.MenuDate != request.BookingDate)
        {
            return Task.FromResult(OperationResult<BookingDto>.Fail("Menu not found for that date.", "NOT_FOUND"));
        }

        var price = EffectivePrice(request.BookingDate);
        var booking = Upsert(request.EmployeeId, request.BookingDate, request.MenuId, price);

        store.Employees.TryGetValue(request.EmployeeId, out var employee);
        var dto = new BookingDto(
            booking.Id,
            booking.EmployeeId,
            employee?.FullName ?? string.Empty,
            booking.BookingDate,
            booking.MenuId,
            menu.MenuNumber,
            menu.Description,
            booking.PriceSnapshot);

        return Task.FromResult(OperationResult<BookingDto>.Ok(dto, "Booked."));
    }

    public Task<OperationResult> CancelDayAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        var editState = GetEditState(date);
        if (editState != DayEditState.Editable)
        {
            return Task.FromResult(OperationResult.Fail("This day can no longer be modified.", "LOCKED"));
        }

        store.Bookings.TryRemove(FakeDataStore.BookingKey(employeeId, date), out _);
        return Task.FromResult(OperationResult.Ok("Booking cancelled."));
    }

    public Task<OperationResult<WeekBookingResultDto>> BookWeekAsync(WeekBookingRequest request, CancellationToken ct = default)
    {
        var applied = new List<DateOnly>();
        var skipped = new List<SkippedDayDto>();

        foreach (var date in DateHelpers.WorkingDays(request.Monday))
        {
            var editState = GetEditState(date);
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
                    new object?[] { FakeDefaults.BookingCutOffLocalTime }));
                continue;
            }

            var menu = store.Menus.Values.FirstOrDefault(m => m.MenuDate == date && m.MenuNumber == request.MenuNumber);
            if (menu is null)
            {
                skipped.Add(new SkippedDayDto(date, $"No Menu {request.MenuNumber} on this day", ErrorCodes.MenuNotFoundForDay));
                continue;
            }

            Upsert(request.EmployeeId, date, menu.Id, EffectivePrice(date));
            applied.Add(date);
        }

        return Task.FromResult(OperationResult<WeekBookingResultDto>.Ok(new WeekBookingResultDto(applied, skipped)));
    }

    private decimal EffectivePrice(DateOnly date) =>
        store.Prices.TryGetValue(date, out var p) ? p.Price : FakeDefaults.DefaultLunchPrice;

    private Booking Upsert(int employeeId, DateOnly date, int menuId, decimal price)
    {
        var key = FakeDataStore.BookingKey(employeeId, date);
        var now = clock.UtcNow;

        return store.Bookings.AddOrUpdate(
            key,
            _ => new Booking
            {
                Id = store.NextBookingId(),
                EmployeeId = employeeId,
                BookingDate = date,
                MenuId = menuId,
                PriceSnapshot = price,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = 1
            },
            (_, existing) =>
            {
                existing.MenuId = menuId;
                existing.PriceSnapshot = price;
                existing.UpdatedAtUtc = now;
                existing.Version++;
                return existing;
            });
    }

    private DayEditState GetEditState(DateOnly date)
    {
        if (date < clock.Today)
        {
            return DayEditState.LockedPast;
        }

        if (date == clock.Today && clock.LocalTimeOfDay >= FakeDefaults.BookingCutOffLocalTime)
        {
            return DayEditState.LockedCutOff;
        }

        return DayEditState.Editable;
    }
}
