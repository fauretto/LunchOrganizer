using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;
using LunchOrganizer.Web.ViewModels;
using BookingResource = LunchOrganizer.Web.Resources.Booking;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises <see cref="BookingViewModel.InitializeAsync"/>'s URL-restore logic: resolving an optional
/// week-Monday query param (clamped to not precede <see cref="BookingViewModel.CurrentWeek"/>) and an
/// optional employee id query param, both best-effort (an invalid/missing employee id must never throw).
/// </summary>
public sealed class BookingViewModelInitializeTests
{
    private static (BookingViewModel Vm, FakeClock Clock, FakeEmployeeRepository EmployeeRepo) CreateSut()
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
        var bookingService = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier);

        var vm = new BookingViewModel(
            bookingService, employeeService, weekService, notifier, clock, appOptions,
            new FakeStringLocalizer<BookingResource>(), new FakeErrorMessageResolver(), new FakeToastService());

        return (vm, clock, employeeRepo);
    }

    [Fact]
    public async Task InitializeAsync_WithValidWeekAndEmployeeId_RestoresSelectedWeekAndEmployeeAndLoadsWeekView()
    {
        var (vm, clock, employeeRepo) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19);

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Alice Example", IsActive = true });
        var weekMonday = new DateOnly(2026, 8, 24);

        await vm.InitializeAsync(weekMonday, employee.Id);

        vm.SelectedWeek.Should().NotBeNull();
        vm.SelectedWeek!.Monday.Should().Be(weekMonday);
        vm.SelectedEmployee.Should().NotBeNull();
        vm.SelectedEmployee!.Id.Should().Be(employee.Id);
        vm.SelectedWeekView.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithNonExistentEmployeeId_LeavesSelectedEmployeeNullAndDoesNotThrow()
    {
        var (vm, clock, _) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19);

        var act = async () => await vm.InitializeAsync(employeeId: 999);

        await act.Should().NotThrowAsync();
        vm.SelectedEmployee.Should().BeNull();
        vm.SelectedWeek.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithPastWeekMonday_ClampsSelectedWeekToCurrentWeek()
    {
        var (vm, clock, _) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19);
        var pastMonday = new DateOnly(2026, 8, 10);

        await vm.InitializeAsync(pastMonday);

        vm.SelectedWeek.Should().NotBeNull();
        vm.SelectedWeek!.Monday.Should().Be(vm.CurrentWeek!.Monday);
    }

    [Fact]
    public async Task InitializeAsync_WithNoArguments_SelectsCurrentWeekAndNoEmployee()
    {
        var (vm, clock, _) = CreateSut();
        clock.Today = new DateOnly(2026, 8, 19);

        await vm.InitializeAsync();

        vm.SelectedWeek.Should().NotBeNull();
        vm.SelectedWeek!.Monday.Should().Be(vm.CurrentWeek!.Monday);
        vm.SelectedEmployee.Should().BeNull();
    }
}
