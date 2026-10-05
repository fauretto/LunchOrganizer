using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Identity;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;
using LunchOrganizer.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using BookingResource = LunchOrganizer.Web.Resources.Booking;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Regression coverage for the bug where <see cref="BookingViewModel.SaveAsync"/>'s per-day loop read
/// the live <see cref="BookingViewModel.PendingSelections"/> property on every iteration. Each
/// <c>BookDayAsync</c>/<c>CancelDayAsync</c> call ends by synchronously calling
/// <c>IBookingChangeNotifier.NotifyChanged</c>, which (via this same VM's own <c>OnBookingChanged</c>
/// handler, subscribed in the constructor) reseeds <c>PendingSelections</c> from freshly reloaded,
/// now-partially-persisted state mid-loop — making the remaining days in the loop see "no change
/// pending" and get silently skipped. The fix snapshots <c>PendingSelections</c> once before the loop
/// (immune to the reseed) and suppresses this VM's own reload-triggered notifications for the
/// duration of the submit (<c>_isSubmittingOwnChanges</c>), while still reacting normally to
/// notifications that arrive once a submit has fully completed.
/// </summary>
public sealed class BookingViewModelSaveTests
{
    private static (
        BookingViewModel Vm,
        FakeClock Clock,
        FakeEmployeeRepository EmployeeRepo,
        FakeMenuRepository MenuRepo,
        FakeBookingRepository BookingRepo,
        BookingService BookingService) CreateSut()
    {
        var clock = new FakeClock();
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions());
        var employeeRepo = new FakeEmployeeRepository();
        var menuRepo = new FakeMenuRepository();
        var bookingRepo = new FakeBookingRepository();
        var priceRepo = new FakeDailyPriceRepository();
        var pricingService = new PricingService(priceRepo, appOptions);
        var weekService = new WeekService(clock);
        var employeeService = new EmployeeService(employeeRepo, appOptions);
        var notifier = new FakeBookingChangeNotifier();
        var bookingService = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier, NullLogger<BookingService>.Instance);

        var vm = new BookingViewModel(
            bookingService, employeeService, weekService, notifier, clock, appOptions,
            new FakeStringLocalizer<BookingResource>(), new FakeErrorMessageResolver(), new FakeToastService());

        return (vm, clock, employeeRepo, menuRepo, bookingRepo, bookingService);
    }

    /// <summary>Seeds one Menu (MenuNumber 1) per weekday of <paramref name="weekMonday"/> and returns each day's menu id.</summary>
    private static async Task<Dictionary<DateOnly, int>> SeedOneMenuPerWeekdayAsync(FakeMenuRepository menuRepo, DateOnly weekMonday)
    {
        var menuIdsByDate = new Dictionary<DateOnly, int>();
        for (var i = 0; i < 5; i++)
        {
            var date = weekMonday.AddDays(i);
            var menu = await menuRepo.AddAsync(new Menu { MenuDate = date, MenuNumber = 1, Description = "Menu 1" });
            menuIdsByDate[date] = menu.Id;
        }

        return menuIdsByDate;
    }

    [Fact]
    public async Task SaveAsync_WithFiveDaysSelected_BooksAllFiveDays()
    {
        var (vm, clock, employeeRepo, menuRepo, bookingRepo, _) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19); // Wednesday
        var weekMonday = new DateOnly(2026, 8, 24);

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Alice Example", IsActive = true });
        var menuIdsByDate = await SeedOneMenuPerWeekdayAsync(menuRepo, weekMonday);

        await vm.InitializeAsync(weekMonday, employee.Id);

        foreach (var (date, menuId) in menuIdsByDate)
        {
            vm.SetPendingSelection(date, menuId);
        }

        await vm.SaveAsync();

        var bookings = await bookingRepo.GetForEmployeeAndWeekAsync(employee.Id, weekMonday);
        bookings.Should().HaveCount(5);
        foreach (var (date, menuId) in menuIdsByDate)
        {
            bookings.Should().ContainSingle(b => b.BookingDate == date && b.MenuId == menuId);
        }

        // Key assertion: under the bug, only 1 of 5 days ends up booked here (the fakes complete
        // synchronously, so the reseed happens right after the first booking; in the real app,
        // with async DB calls, it was typically 2 of 5) — the reseed-mid-loop made the rest look unchanged.
        vm.SelectedWeekView.Should().NotBeNull();
        foreach (var day in vm.SelectedWeekView!.Days)
        {
            day.SelectedMenuId.Should().Be(menuIdsByDate[day.Date]);
        }
    }

    [Fact]
    public async Task SaveAsync_WithMixedBookAndCancel_AppliesAllChanges()
    {
        var (vm, clock, employeeRepo, menuRepo, bookingRepo, _) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19); // Wednesday
        var weekMonday = new DateOnly(2026, 8, 24);

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Bob Example", IsActive = true });
        var menuIdsByDate = await SeedOneMenuPerWeekdayAsync(menuRepo, weekMonday);

        // Second menu on the Monday, so that day can be switched to a different menu id.
        var monday = weekMonday;
        var secondMenuOnMonday = await menuRepo.AddAsync(new Menu { MenuDate = monday, MenuNumber = 2, Description = "Menu 2" });

        await vm.InitializeAsync(weekMonday, employee.Id);

        // Baseline: book all 5 days first.
        foreach (var (date, menuId) in menuIdsByDate)
        {
            vm.SetPendingSelection(date, menuId);
        }

        await vm.SaveAsync();

        var baseline = await bookingRepo.GetForEmployeeAndWeekAsync(employee.Id, weekMonday);
        baseline.Should().HaveCount(5);

        // Now apply mixed changes:
        // - Monday: switch to the second menu.
        // - Tuesday, Wednesday: deselect (toggle off by re-selecting the already-booked menu id).
        // - Thursday, Friday: left untouched.
        var tuesday = weekMonday.AddDays(1);
        var wednesday = weekMonday.AddDays(2);

        vm.SetPendingSelection(monday, secondMenuOnMonday.Id);
        vm.SetPendingSelection(tuesday, menuIdsByDate[tuesday]);
        vm.SetPendingSelection(wednesday, menuIdsByDate[wednesday]);

        await vm.SaveAsync();

        var finalBookings = await bookingRepo.GetForEmployeeAndWeekAsync(employee.Id, weekMonday);
        finalBookings.Should().HaveCount(3);

        finalBookings.Should().ContainSingle(b => b.BookingDate == monday && b.MenuId == secondMenuOnMonday.Id);
        finalBookings.Should().NotContain(b => b.BookingDate == tuesday);
        finalBookings.Should().NotContain(b => b.BookingDate == wednesday);

        var thursday = weekMonday.AddDays(3);
        var friday = weekMonday.AddDays(4);
        finalBookings.Should().ContainSingle(b => b.BookingDate == thursday && b.MenuId == menuIdsByDate[thursday]);
        finalBookings.Should().ContainSingle(b => b.BookingDate == friday && b.MenuId == menuIdsByDate[friday]);
    }

    [Fact]
    public async Task SaveAsync_DoesNotSuppressExternalNotificationsAfterwards()
    {
        var (vm, clock, employeeRepo, menuRepo, bookingRepo, bookingService) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19); // Wednesday
        var weekMonday = new DateOnly(2026, 8, 24);

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Carla Example", IsActive = true });
        var menuIdsByDate = await SeedOneMenuPerWeekdayAsync(menuRepo, weekMonday);

        await vm.InitializeAsync(weekMonday, employee.Id);

        var monday = weekMonday;
        vm.SetPendingSelection(monday, menuIdsByDate[monday]);

        await vm.SaveAsync();

        // SaveAsync has fully completed here (and its finally block has already cleared
        // _isSubmittingOwnChanges), so an external booking on a different day of the same week must
        // still trigger a normal reload via OnBookingChanged -> ReloadAfterNotificationAsync.
        var friday = weekMonday.AddDays(4);
        var bookResult = await bookingService.BookDayAsync(new BookingRequest(employee.Id, friday, menuIdsByDate[friday], PcUserInfo.Empty));
        bookResult.IsSuccess.Should().BeTrue();

        // ReloadAfterNotificationAsync is fire-and-forget from OnBookingChanged, so poll briefly for it
        // to complete rather than asserting immediately.
        var reflected = false;
        for (var i = 0; i < 50 && !reflected; i++)
        {
            await Task.Delay(20);
            reflected = vm.SelectedWeekView!.Days.First(d => d.Date == friday).SelectedMenuId == menuIdsByDate[friday];
        }

        reflected.Should().BeTrue("the VM's own in-flight-submit suppression must not block notifications that arrive after the submit has completed");
    }
}
