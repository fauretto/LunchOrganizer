using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>Exercises the booking-count and past-date guards in <see cref="MenuService.DeleteAsync"/>.</summary>
public sealed class MenuServiceDeletionTests
{
    private static readonly DateOnly Today = new(2026, 8, 17);

    private static (MenuService Service, FakeMenuRepository MenuRepo, FakeBookingRepository BookingRepo) CreateSut()
    {
        var clock = new FakeClock { Today = Today, LocalTimeOfDay = new TimeOnly(8, 0) };
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions());

        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();

        var service = new MenuService(menuRepo, bookingRepo, clock, appOptions, NullLogger<MenuService>.Instance);

        return (service, menuRepo, bookingRepo);
    }

    [Fact]
    public async Task DeleteAsync_MenuWithBookings_FailsWithMenuHasBookingsAndReportsCount()
    {
        var (service, menuRepo, bookingRepo) = CreateSut();
        var futureDate = Today.AddDays(3);
        var menu = await menuRepo.AddAsync(new Menu { MenuDate = futureDate, MenuNumber = 1 });

        await bookingRepo.UpsertAsync(new Booking { EmployeeId = 1, BookingDate = futureDate, MenuId = menu.Id, PriceSnapshot = 10m });
        await bookingRepo.UpsertAsync(new Booking { EmployeeId = 2, BookingDate = futureDate, MenuId = menu.Id, PriceSnapshot = 10m });

        var result = await service.DeleteAsync(menu.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MenuHasBookings);
        result.MessageArgs.Should().NotBeNull();
        result.MessageArgs![0].Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_PastDatedMenuWithNoBookings_FailsWithMenuDateInPast()
    {
        var (service, menuRepo, _) = CreateSut();
        var pastDate = Today.AddDays(-3);
        var menu = await menuRepo.AddAsync(new Menu { MenuDate = pastDate, MenuNumber = 1 });

        var result = await service.DeleteAsync(menu.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MenuDateInPast);
    }

    [Fact]
    public async Task DeleteAsync_FutureUnbookedMenu_SucceedsAndRemovesMenu()
    {
        var (service, menuRepo, _) = CreateSut();
        var futureDate = Today.AddDays(3);
        var menu = await menuRepo.AddAsync(new Menu { MenuDate = futureDate, MenuNumber = 1 });

        var result = await service.DeleteAsync(menu.Id);

        result.IsSuccess.Should().BeTrue();
        (await menuRepo.GetByIdAsync(menu.Id)).Should().BeNull();
    }
}
