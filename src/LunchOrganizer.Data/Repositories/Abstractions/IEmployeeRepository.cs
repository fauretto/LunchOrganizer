using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Data.Repositories.Abstractions;

/// <summary>
/// Repository abstraction for querying and persisting <see cref="Employee"/> entities.
/// </summary>
public interface IEmployeeRepository
{
    /// <summary>Searches for employees whose full name contains the given fragment, returning at most <paramref name="take"/> results.</summary>
    Task<IReadOnlyList<Employee>> SearchByNameAsync(string fragment, int take, CancellationToken ct = default);

    /// <summary>Retrieves an employee by id, or null if not found.</summary>
    Task<Employee?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Retrieves an employee by exact full name, or null if not found.</summary>
    Task<Employee?> GetByNameAsync(string fullName, CancellationToken ct = default);

    /// <summary>Retrieves all employees, optionally including inactive ones.</summary>
    Task<IReadOnlyList<Employee>> GetAllAsync(bool includeInactive, CancellationToken ct = default);

    /// <summary>
    /// Atomic get-or-create by full name. Must be implemented as a single INSERT ... ON CONFLICT (full_name) DO NOTHING
    /// followed by a re-select, so that concurrent callers registering the same new name all receive the same
    /// employee id (see implementation plan §11.6). Never a plain check-then-insert.
    /// </summary>
    Task<Employee> AddAsync(Employee employee, CancellationToken ct = default);

    /// <summary>Updates an existing employee.</summary>
    Task UpdateAsync(Employee employee, CancellationToken ct = default);

    /// <summary>Deletes an employee by id.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Returns true if the given employee has at least one booking.</summary>
    Task<bool> HasAnyBookingAsync(int employeeId, CancellationToken ct = default);
}
