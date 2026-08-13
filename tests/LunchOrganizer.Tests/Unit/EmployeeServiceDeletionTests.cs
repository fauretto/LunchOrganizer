using FluentAssertions;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;
using LunchOrganizer.Services.Services;
using LunchOrganizer.Tests.TestSupport;

namespace LunchOrganizer.Tests.Unit;

/// <summary>
/// Exercises the delete-or-deactivate fallback logic in <see cref="EmployeeService"/>: a zero-bookings
/// employee is hard-deleted, while an employee with bookings is deactivated instead.
/// </summary>
public sealed class EmployeeServiceDeletionTests
{
    private static (EmployeeService Service, FakeEmployeeRepository Repo) CreateSut()
    {
        var repo = new FakeEmployeeRepository();
        var appOptions = new StaticOptionsMonitor<AppOptions>(new AppOptions());
        var service = new EmployeeService(repo, appOptions);
        return (service, repo);
    }

    [Fact]
    public async Task DeleteOrDeactivateAsync_EmployeeWithZeroBookings_DeletesEmployee()
    {
        var (service, repo) = CreateSut();
        var employee = await repo.AddAsync(new Employee { FullName = "No Bookings Employee", IsActive = true });

        var result = await service.DeleteOrDeactivateAsync(employee.Id);

        result.IsSuccess.Should().BeTrue();
        (await repo.GetByIdAsync(employee.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteOrDeactivateWithOutcomeAsync_EmployeeWithZeroBookings_ReturnsDeleted()
    {
        var (service, repo) = CreateSut();
        var employee = await repo.AddAsync(new Employee { FullName = "No Bookings Employee Two", IsActive = true });

        var result = await service.DeleteOrDeactivateWithOutcomeAsync(employee.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(EmployeeDeletionOutcome.Deleted);
    }

    [Fact]
    public async Task DeleteOrDeactivateAsync_EmployeeWithBookings_DeactivatesInsteadOfDeleting()
    {
        var (service, repo) = CreateSut();
        var employee = await repo.AddAsync(new Employee { FullName = "Has Bookings Employee", IsActive = true });
        repo.EmployeeIdsWithBookings.Add(employee.Id);

        var result = await service.DeleteOrDeactivateAsync(employee.Id);

        result.IsSuccess.Should().BeTrue();
        var stillThere = await repo.GetByIdAsync(employee.Id);
        stillThere.Should().NotBeNull();
        stillThere!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOrDeactivateWithOutcomeAsync_EmployeeWithBookings_ReturnsDeactivated()
    {
        var (service, repo) = CreateSut();
        var employee = await repo.AddAsync(new Employee { FullName = "Has Bookings Employee Two", IsActive = true });
        repo.EmployeeIdsWithBookings.Add(employee.Id);

        var result = await service.DeleteOrDeactivateWithOutcomeAsync(employee.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(EmployeeDeletionOutcome.Deactivated);
    }
}
