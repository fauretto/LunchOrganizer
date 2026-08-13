using FluentAssertions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises <see cref="BookingService.BookWeekAsync"/>'s per-day skip/apply logic across a single week,
/// covering both distinct skip reasons (<see cref="ErrorCodes.BookingDateInPast"/> and
/// <see cref="ErrorCodes.MenuNotFoundForDay"/>) alongside successful applies in one call.
/// </summary>
public sealed class BookWeekPartialApplicationTests
{
    // Week of Monday 17 August 2026 (verified Monday).
    private static readonly DateOnly Monday = new(2026, 8, 17);
    private static readonly DateOnly Tuesday = new(2026, 8, 18);
    private static readonly DateOnly Wednesday = new(2026, 8, 19);
    private static readonly DateOnly Thursday = new(2026, 8, 20);
    private static readonly DateOnly Friday = new(2026, 8, 21);

    private const int TargetMenuNumber = 3;

    [Fact]
    public async Task BookWeekAsync_MixOfPastMissingMenuAndAvailableDays_AppliesOnlyEligibleDaysAndSkipsRestWithCorrectReasons()
    {
        // "Today" is Tuesday: Monday is in the past (regardless of menu availability), Tuesday/Wed/Thu/Fri
        // are all >= Today so they are gated only on menu availability, not on the past-date rule.
        var clock = new FakeClock { Today = Tuesday, LocalTimeOfDay = new TimeOnly(8, 0) };
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions { BookingCutOffLocalTime = new TimeOnly(9, 0), DefaultLunchPrice = 12.50m });

        var employeeRepo = new FakeEmployeeRepository();
        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();
        var priceRepo = new FakeDailyPriceRepository();
        var pricingService = new PricingService(priceRepo, appOptions);
        var notifier = new FakeBookingChangeNotifier();

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Dana Example", IsActive = true });

        // Monday: no Menu 3 seeded at all — irrelevant, it will be skipped for being in the past anyway.
        await menuRepo.AddAsync(new Menu { MenuDate = Monday, MenuNumber = 1, Description = "Monday Menu 1" });

        // Tuesday (== Today, editable): seed the target menu number -> should be applied.
        await menuRepo.AddAsync(new Menu { MenuDate = Tuesday, MenuNumber = TargetMenuNumber, Description = "Tuesday Menu 3" });

        // Wednesday (future, editable): seed only a different-numbered menu -> Menu 3 missing -> skipped.
        await menuRepo.AddAsync(new Menu { MenuDate = Wednesday, MenuNumber = 1, Description = "Wednesday Menu 1" });

        // Thursday and Friday (future, editable): seed the target menu number -> should be applied.
        await menuRepo.AddAsync(new Menu { MenuDate = Thursday, MenuNumber = TargetMenuNumber, Description = "Thursday Menu 3" });
        await menuRepo.AddAsync(new Menu { MenuDate = Friday, MenuNumber = TargetMenuNumber, Description = "Friday Menu 3" });

        var service = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier);

        var result = await service.BookWeekAsync(new WeekBookingRequest(employee.Id, Monday, TargetMenuNumber));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AppliedDates.Should().Equal(Tuesday, Thursday, Friday);

        result.Value!.SkippedDays.Should().HaveCount(2);
        result.Value!.SkippedDays[0].Date.Should().Be(Monday);
        result.Value!.SkippedDays[0].ReasonCode.Should().Be(ErrorCodes.BookingDateInPast);
        result.Value!.SkippedDays[1].Date.Should().Be(Wednesday);
        result.Value!.SkippedDays[1].ReasonCode.Should().Be(ErrorCodes.MenuNotFoundForDay);

        (await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, Tuesday)).Should().NotBeNull();
        (await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, Thursday)).Should().NotBeNull();
        (await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, Friday)).Should().NotBeNull();
        (await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, Monday)).Should().BeNull();
        (await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, Wednesday)).Should().BeNull();
    }
}
