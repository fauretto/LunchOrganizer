using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Verifies that a booking's <see cref="Booking.PriceSnapshot"/> is captured at booking time and is
/// never retroactively affected by a later price change for the same date.
/// </summary>
public sealed class PriceSnapshotImmutabilityTests
{
    private static readonly DateOnly BookingDate = new(2026, 8, 17);

    [Fact]
    public async Task ChangingPriceAfterBooking_DoesNotAffectAlreadyCapturedPriceSnapshot()
    {
        var clock = new FakeClock { Today = BookingDate, LocalTimeOfDay = new TimeOnly(8, 0) };
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions { BookingCutOffLocalTime = new TimeOnly(9, 0), DefaultLunchPrice = 0m });

        var employeeRepo = new FakeEmployeeRepository();
        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();
        var priceRepo = new FakeDailyPriceRepository();
        var pricingService = new PricingService(priceRepo, appOptions);
        var notifier = new FakeBookingChangeNotifier();

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Carol Example", IsActive = true });
        var menu = await menuRepo.AddAsync(new Menu { MenuDate = BookingDate, MenuNumber = 1 });

        var originalPrice = 12.50m;
        await priceRepo.UpsertAsync(new DailyPrice { PriceDate = BookingDate, Price = originalPrice });

        var service = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier, NullLogger<BookingService>.Instance);

        var bookResult = await service.BookDayAsync(new BookingRequest(employee.Id, BookingDate, menu.Id));
        bookResult.IsSuccess.Should().BeTrue();
        bookResult.Value!.PriceSnapshot.Should().Be(originalPrice);

        var newPrice = 18.00m;
        var setPriceResult = await pricingService.SetPriceAsync(BookingDate, newPrice);
        setPriceResult.IsSuccess.Should().BeTrue();

        var refetched = await bookingRepo.GetForEmployeeAndDateAsync(employee.Id, BookingDate);

        refetched.Should().NotBeNull();
        refetched!.PriceSnapshot.Should().Be(originalPrice);
        refetched.PriceSnapshot.Should().NotBe(newPrice);
    }
}
