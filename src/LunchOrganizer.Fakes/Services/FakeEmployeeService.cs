using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeEmployeeService(FakeDataStore store, IClock clock) : IEmployeeService
{
    public Task<IReadOnlyList<EmployeeDto>> SearchAsync(string fragment, CancellationToken ct = default)
    {
        var results = store.Employees.Values
            .Where(e => e.IsActive && e.FullName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<EmployeeDto>>(results);
    }

    public Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = store.Employees.TryGetValue(id, out var employee) ? ToDto(employee) : null;
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<EmployeeDto>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        var results = store.Employees.Values
            .Where(e => includeInactive || e.IsActive)
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<EmployeeDto>>(results);
    }

    public Task<OperationResult<EmployeeDto>> RegisterAsync(string fullName, string? email = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Task.FromResult(OperationResult<EmployeeDto>.Fail("Name is required."));
        }

        var trimmed = fullName.Trim();

        // Whitespace collapses to null, not "", matching EmployeeService.RegisterAsync.
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        lock (store.RegistrationLock)
        {
            var existing = store.Employees.Values
                .FirstOrDefault(e => string.Equals(e.FullName, trimmed, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Reproduces EmployeeService.RegisterAsync's reactivation fix: a deactivated employee
                // whose name matches, or one missing an email while a new one was supplied, is
                // reactivated / filled in rather than silently returned as-is. An existing non-empty
                // email is never overwritten.
                var needsReactivation = !existing.IsActive;
                var needsEmail = normalizedEmail is not null && string.IsNullOrWhiteSpace(existing.Email);
                if (needsReactivation || needsEmail)
                {
                    if (needsReactivation) existing.IsActive = true;
                    if (needsEmail) existing.Email = normalizedEmail;
                    existing.UpdatedAtUtc = clock.UtcNow;
                    existing.Version++;
                }

                return Task.FromResult(OperationResult<EmployeeDto>.Ok(ToDto(existing), "Employee already registered."));
            }

            var now = clock.UtcNow;
            var id = store.NextEmployeeId();
            var employee = new Employee
            {
                Id = id,
                FullName = trimmed,
                Email = normalizedEmail,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Version = 1
            };
            store.Employees[id] = employee;

            return Task.FromResult(OperationResult<EmployeeDto>.Ok(ToDto(employee), "Employee registered."));
        }
    }

    public Task<OperationResult<EmployeeDto>> UpdateAsync(int id, string fullName, string? email, CancellationToken ct = default)
    {
        if (!store.Employees.TryGetValue(id, out var employee))
        {
            return Task.FromResult(OperationResult<EmployeeDto>.Fail("Employee not found.", "NOT_FOUND"));
        }

        employee.FullName = fullName;
        employee.Email = email;
        employee.UpdatedAtUtc = clock.UtcNow;
        employee.Version++;

        return Task.FromResult(OperationResult<EmployeeDto>.Ok(ToDto(employee), "Employee updated."));
    }

    public Task<OperationResult> DeleteOrDeactivateAsync(int id, CancellationToken ct = default)
    {
        if (!store.Employees.TryGetValue(id, out var employee))
        {
            return Task.FromResult(OperationResult.Fail("Employee not found.", "NOT_FOUND"));
        }

        var hasBookings = store.Bookings.Values.Any(b => b.EmployeeId == id);
        if (!hasBookings)
        {
            store.Employees.TryRemove(id, out _);
            return Task.FromResult(OperationResult.Ok("Employee deleted."));
        }

        employee.IsActive = false;
        employee.UpdatedAtUtc = clock.UtcNow;
        employee.Version++;

        return Task.FromResult(OperationResult.Ok("Employee has existing bookings; deactivated instead."));
    }

    public Task<bool> HasBookingsAsync(int employeeId, CancellationToken ct = default) =>
        Task.FromResult(store.Bookings.Values.Any(b => b.EmployeeId == employeeId));

    public Task<OperationResult<EmployeeDeletionOutcome>> DeleteOrDeactivateWithOutcomeAsync(int id, CancellationToken ct = default)
    {
        if (!store.Employees.TryGetValue(id, out var employee))
        {
            return Task.FromResult(OperationResult<EmployeeDeletionOutcome>.Fail("Employee not found.", "NOT_FOUND"));
        }

        var hasBookings = store.Bookings.Values.Any(b => b.EmployeeId == id);
        if (!hasBookings)
        {
            store.Employees.TryRemove(id, out _);
            return Task.FromResult(OperationResult<EmployeeDeletionOutcome>.Ok(EmployeeDeletionOutcome.Deleted, "Employee deleted."));
        }

        employee.IsActive = false;
        employee.UpdatedAtUtc = clock.UtcNow;
        employee.Version++;

        return Task.FromResult(OperationResult<EmployeeDeletionOutcome>.Ok(EmployeeDeletionOutcome.Deactivated, "Employee has existing bookings; deactivated instead."));
    }

    private static EmployeeDto ToDto(Employee e) => new(e.Id, e.FullName, e.Email, e.IsActive);
}
