using FluentAssertions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Verifies that <see cref="BookingService.BookDayAsync"/> and <see cref="BookingService.CancelDayAsync"/>
/// both reject a date that lies strictly before <c>IClock.Today</c> with <see cref="ErrorCodes.BookingDateInPast"/>.
/// </summary>
public sealed class PastDayRejectionTests
{
    private static readonly DateOnly PastDate = new(2026, 8, 10);
    private static readonly DateOnly Today = new(2026, 8, 17);

    private static (BookingService Service, int EmployeeId, int MenuId) CreateSut()
    {
        var clock = new FakeClock { Today = Today, LocalTimeOfDay = new TimeOnly(8, 0) };
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions { BookingCutOffLocalTime = new TimeOnly(9, 0), DefaultLunchPrice = 12.50m });

        var employeeRepo = new FakeEmployeeRepository();
        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();
        var priceRepo = new FakeDailyPriceRepository();
        var pricingService = new PricingService(priceRepo, appOptions);
        var notifier = new FakeBookingChangeNotifier();

        var employee = employeeRepo.AddAsync(new Employee { FullName = "Bob Example", IsActive = true }).GetAwaiter().GetResult();
        var menu = menuRepo.AddAsync(new Menu { MenuDate = PastDate, MenuNumber = 1, Description = "Menu 1" }).GetAwaiter().GetResult();

        var service = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier);

        return (service, employee.Id, menu.Id);
    }

    [Fact]
    public async Task BookDayAsync_ForPastDate_FailsWithBookingDateInPast()
    {
        var (service, employeeId, menuId) = CreateSut();

        var result = await service.BookDayAsync(new BookingRequest(employeeId, PastDate, menuId));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BookingDateInPast);
    }

    [Fact]
    public async Task CancelDayAsync_ForPastDate_FailsWithBookingDateInPast()
    {
        var (service, employeeId, _) = CreateSut();

        var result = await service.CancelDayAsync(employeeId, PastDate);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BookingDateInPast);
    }
}
