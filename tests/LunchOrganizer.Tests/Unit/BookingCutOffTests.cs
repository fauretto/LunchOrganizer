using FluentAssertions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises the booking cut-off gating in <see cref="BookingService"/> indirectly through
/// <see cref="BookingService.BookDayAsync"/> and <see cref="BookingService.CancelDayAsync"/>, since the
/// underlying <c>EditStateHelper</c> is internal to LunchOrganizer.Services and not directly testable.
/// </summary>
public sealed class BookingCutOffTests
{
    private static readonly DateOnly BookingDate = new(2026, 8, 17);
    private static readonly TimeOnly CutOff = new(9, 0);

    private static (BookingService Service, FakeClock Clock, FakeBookingRepository BookingRepo, int EmployeeId, int MenuId) CreateSut()
    {
        var clock = new FakeClock { Today = BookingDate, LocalTimeOfDay = new TimeOnly(8, 0) };
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions { BookingCutOffLocalTime = CutOff, DefaultLunchPrice = 12.50m });

        var employeeRepo = new FakeEmployeeRepository();
        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();
        var priceRepo = new FakeDailyPriceRepository();
        var pricingService = new PricingService(priceRepo, appOptions);
        var notifier = new FakeBookingChangeNotifier();

        var employee = employeeRepo.AddAsync(new Employee { FullName = "Alice Example", IsActive = true }).GetAwaiter().GetResult();
        var menu = menuRepo.AddAsync(new Menu { MenuDate = BookingDate, MenuNumber = 1, Description = "Menu 1" }).GetAwaiter().GetResult();

        var service = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier);

        return (service, clock, bookingRepo, employee.Id, menu.Id);
    }

    [Theory]
    [InlineData(8, 59, 59, true)]
    [InlineData(9, 0, 0, false)]
    [InlineData(9, 0, 1, false)]
    public async Task BookDayAsync_AtCutOffBoundary_SucceedsBeforeCutOffAndFailsAtOrAfter(int hour, int minute, int second, bool expectSuccess)
    {
        var (service, clock, _, employeeId, menuId) = CreateSut();
        clock.LocalTimeOfDay = new TimeOnly(hour, minute, second);

        var result = await service.BookDayAsync(new BookingRequest(employeeId, BookingDate, menuId));

        result.IsSuccess.Should().Be(expectSuccess);
        if (!expectSuccess)
        {
            result.ErrorCode.Should().Be(ErrorCodes.BookingCutOffPassed);
        }
    }

    [Theory]
    [InlineData(8, 59, 59, true)]
    [InlineData(9, 0, 0, false)]
    [InlineData(9, 0, 1, false)]
    public async Task CancelDayAsync_AtCutOffBoundary_SucceedsBeforeCutOffAndFailsAtOrAfter(int hour, int minute, int second, bool expectSuccess)
    {
        var (service, clock, _, employeeId, menuId) = CreateSut();

        // Book first while clearly editable, at a neutral time well before any cut-off boundary.
        clock.LocalTimeOfDay = new TimeOnly(7, 0, 0);
        var bookResult = await service.BookDayAsync(new BookingRequest(employeeId, BookingDate, menuId));
        bookResult.IsSuccess.Should().BeTrue();

        clock.LocalTimeOfDay = new TimeOnly(hour, minute, second);

        var cancelResult = await service.CancelDayAsync(employeeId, BookingDate);

        cancelResult.IsSuccess.Should().Be(expectSuccess);
        if (!expectSuccess)
        {
            cancelResult.ErrorCode.Should().Be(ErrorCodes.BookingCutOffPassed);
        }
    }
}
