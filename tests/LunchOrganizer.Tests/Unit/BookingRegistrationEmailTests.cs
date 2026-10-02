using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Email.Validation;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;
using LunchOrganizer.Web.ViewModels;
using BookingResource = LunchOrganizer.Web.Resources.Booking;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises the booking-page self-registration email requirement: the mandatory-email gate in
/// <see cref="BookingViewModel.RegisterNewEmployeeAsync"/>, storage/trimming in
/// <see cref="EmployeeService.RegisterAsync"/>, the deactivated-employee reactivation fix, and the
/// "never overwrite an existing address" rule. Uses the real <see cref="EmployeeService"/> over a
/// <see cref="FakeEmployeeRepository"/>, same pattern as <see cref="BookingViewModelInitializeTests"/>.
/// </summary>
public sealed class BookingRegistrationEmailTests
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
        var bookingService = new BookingService(bookingRepo, menuRepo, employeeRepo, pricingService, clock, appOptions, notifier, NullLogger<BookingService>.Instance);

        var vm = new BookingViewModel(
            bookingService, employeeService, weekService, notifier, clock, appOptions,
            new FakeStringLocalizer<BookingResource>(), new FakeErrorMessageResolver(), new FakeToastService());

        return (vm, clock, employeeRepo);
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_WithValidAddress_StoresItOnTheNewEmployee()
    {
        var (vm, _, employeeRepo) = CreateSut();

        var success = await vm.RegisterNewEmployeeAsync("New Person", "person@example.com");

        success.Should().BeTrue();
        vm.SelectedEmployee.Should().NotBeNull();

        var stored = await employeeRepo.GetByIdAsync(vm.SelectedEmployee!.Id);
        stored.Should().NotBeNull();
        stored!.Email.Should().Be("person@example.com");
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_WithBlankAddress_IsRejectedAndServiceNeverReached()
    {
        var (vm, _, employeeRepo) = CreateSut();

        var success = await vm.RegisterNewEmployeeAsync("New Person", "   ");

        success.Should().BeFalse();
        vm.SelectedEmployee.Should().BeNull();
        (await employeeRepo.GetAllAsync(includeInactive: true)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("no-at-sign.com")]
    [InlineData(" ")]
    public async Task RegisterNewEmployeeAsync_WithMalformedAddress_IsRejectedAndServiceNeverReached(string email)
    {
        var (vm, _, employeeRepo) = CreateSut();

        var success = await vm.RegisterNewEmployeeAsync("New Person", email);

        success.Should().BeFalse();
        vm.SelectedEmployee.Should().BeNull();
        (await employeeRepo.GetAllAsync(includeInactive: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_WithSurroundingWhitespace_TrimsBeforeStorage()
    {
        var (vm, _, employeeRepo) = CreateSut();

        var success = await vm.RegisterNewEmployeeAsync("Trim Person", "  a@b.com  ");

        success.Should().BeTrue();
        var stored = await employeeRepo.GetByIdAsync(vm.SelectedEmployee!.Id);
        stored!.Email.Should().Be("a@b.com");
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_OnSuccess_ClearsNewEmployeeEmail()
    {
        var (vm, _, _) = CreateSut();
        vm.NewEmployeeEmail = "person@example.com";

        var success = await vm.RegisterNewEmployeeAsync("New Person", "person@example.com");

        success.Should().BeTrue();
        vm.NewEmployeeEmail.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectEmployeeAsync_WhenSelectingAnExistingEmployee_ClearsNewEmployeeEmail()
    {
        var (vm, _, employeeRepo) = CreateSut();
        vm.NewEmployeeEmail = "typed-for-someone-else@example.com";

        var employee = await employeeRepo.AddAsync(new Employee { FullName = "Alice Example", IsActive = true });
        var dto = new EmployeeDto(employee.Id, employee.FullName, employee.Email, employee.IsActive);

        await vm.SelectEmployeeAsync(dto);

        vm.NewEmployeeEmail.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_MatchingADeactivatedEmployee_ReactivatesAndStoresTheNewAddress()
    {
        var (vm, _, employeeRepo) = CreateSut();
        var dormant = await employeeRepo.AddAsync(new Employee { FullName = "Dormant Person", IsActive = true });
        dormant.IsActive = false;
        await employeeRepo.UpdateAsync(dormant);

        var success = await vm.RegisterNewEmployeeAsync("Dormant Person", "dormant@example.com");

        success.Should().BeTrue();
        var stored = await employeeRepo.GetByIdAsync(dormant.Id);
        stored!.IsActive.Should().BeTrue();
        stored.Email.Should().Be("dormant@example.com");
    }

    [Fact]
    public async Task RegisterNewEmployeeAsync_MatchingAnEmployeeWithAnExistingEmail_DoesNotOverwriteIt()
    {
        var (vm, _, employeeRepo) = CreateSut();
        var employee = await employeeRepo.AddAsync(new Employee
        {
            FullName = "Has Email",
            Email = "original@example.com",
            IsActive = true
        });

        var success = await vm.RegisterNewEmployeeAsync("Has Email", "typed@example.com");

        success.Should().BeTrue();
        var stored = await employeeRepo.GetByIdAsync(employee.Id);
        stored!.Email.Should().Be("original@example.com");
    }

    [Fact]
    public void IsNewEmployeeEmailValid_AgreesWithEmailAddressValidation_ForANormalAddress()
    {
        var (vm, _, _) = CreateSut();
        vm.NewEmployeeEmail = "person@example.com";

        vm.IsNewEmployeeEmailValid.Should().BeTrue();
        vm.IsNewEmployeeEmailValid.Should().Be(EmailAddressValidation.IsValidFormat(vm.NewEmployeeEmail));
    }
}
