using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// In-memory, dictionary-backed fake of <see cref="IEmployeeRepository"/> for unit tests. Auto-increments
/// ids starting at 1. <see cref="AddAsync"/> emulates the real "atomic get-or-create by exact full name"
/// semantics with a case-sensitive exact match. Bookings-existence is driven purely by the test-settable
/// <see cref="EmployeeIdsWithBookings"/> set, not by any real booking data.
/// </summary>
public sealed class FakeEmployeeRepository : IEmployeeRepository
{
    private readonly Dictionary<int, Employee> _employees = new();
    private int _nextId = 1;

    /// <summary>Employee ids that <see cref="HasAnyBookingAsync"/> should report as having bookings.</summary>
    public HashSet<int> EmployeeIdsWithBookings { get; } = new();

    public Task<IReadOnlyList<Employee>> SearchByNameAsync(string fragment, int take, CancellationToken ct = default)
    {
        IReadOnlyList<Employee> results = _employees.Values
            .Where(e => e.FullName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .Take(take)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<Employee?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        _employees.TryGetValue(id, out var employee);
        return Task.FromResult(employee);
    }

    public Task<Employee?> GetByNameAsync(string fullName, CancellationToken ct = default)
    {
        var employee = _employees.Values.FirstOrDefault(e => e.FullName == fullName);
        return Task.FromResult(employee);
    }

    public Task<IReadOnlyList<Employee>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        IReadOnlyList<Employee> results = _employees.Values
            .Where(e => includeInactive || e.IsActive)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<Employee> AddAsync(Employee employee, CancellationToken ct = default)
    {
        var existing = _employees.Values.FirstOrDefault(e => e.FullName == employee.FullName);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        employee.Id = _nextId++;
        _employees[employee.Id] = employee;
        return Task.FromResult(employee);
    }

    public Task UpdateAsync(Employee employee, CancellationToken ct = default)
    {
        _employees[employee.Id] = employee;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        if (EmployeeIdsWithBookings.Contains(id))
        {
            throw new DeleteRestrictedException(
                $"Employee {id} has bookings.",
                new InvalidOperationException("Simulated foreign-key violation."));
        }

        _employees.Remove(id);
        return Task.CompletedTask;
    }

    public Task<bool> HasAnyBookingAsync(int employeeId, CancellationToken ct = default) =>
        Task.FromResult(EmployeeIdsWithBookings.Contains(employeeId));
}
