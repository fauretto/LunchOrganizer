using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LunchOrganizer.Services.Services;

public sealed class EmployeeService(IEmployeeRepository repo, IOptionsMonitor<AppOptions> appOptions) : IEmployeeService
{
    public async Task<IReadOnlyList<EmployeeDto>> SearchAsync(string fragment, CancellationToken ct = default)
    {
        var results = await repo.SearchByNameAsync(fragment, appOptions.CurrentValue.AutocompleteMaxResults, ct);
        return results.Where(e => e.IsActive).Select(ToDto).ToList();
    }

    public async Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var employee = await repo.GetByIdAsync(id, ct);
        return employee is null ? null : ToDto(employee);
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        var results = await repo.GetAllAsync(includeInactive, ct);
        return results.Select(ToDto).ToList();
    }

    public async Task<OperationResult<EmployeeDto>> RegisterAsync(string fullName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return OperationResult<EmployeeDto>.Fail("Name is required.", ErrorCodes.EmployeeNameRequired);
        }

        var employee = await repo.AddAsync(new Employee { FullName = fullName.Trim(), IsActive = true }, ct);
        return OperationResult<EmployeeDto>.Ok(ToDto(employee));
    }

    public async Task<OperationResult<EmployeeDto>> UpdateAsync(int id, string fullName, string? email, CancellationToken ct = default)
    {
        var employee = await repo.GetByIdAsync(id, ct);
        if (employee is null)
        {
            return OperationResult<EmployeeDto>.Fail("Employee not found.", ErrorCodes.EmployeeNotFound);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return OperationResult<EmployeeDto>.Fail("Name is required.", ErrorCodes.EmployeeNameRequired);
        }

        employee.FullName = fullName.Trim();
        employee.Email = email;

        try
        {
            await repo.UpdateAsync(employee, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<EmployeeDto>.Fail("Someone else changed this employee.", ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return OperationResult<EmployeeDto>.Fail(
                "That name is already used by another employee.",
                ErrorCodes.EmployeeNameAlreadyExists,
                new object?[] { employee.FullName });
        }

        return OperationResult<EmployeeDto>.Ok(ToDto(employee));
    }

    public async Task<bool> HasBookingsAsync(int employeeId, CancellationToken ct = default) =>
        await repo.HasAnyBookingAsync(employeeId, ct);

    public async Task<OperationResult> DeleteOrDeactivateAsync(int id, CancellationToken ct = default)
    {
        var (found, _, errorCode, message) = await DeleteOrDeactivateCoreAsync(id, ct);
        return found ? OperationResult.Ok(message) : OperationResult.Fail(message, errorCode);
    }

    public async Task<OperationResult<EmployeeDeletionOutcome>> DeleteOrDeactivateWithOutcomeAsync(int id, CancellationToken ct = default)
    {
        var (found, outcome, errorCode, message) = await DeleteOrDeactivateCoreAsync(id, ct);
        return found
            ? OperationResult<EmployeeDeletionOutcome>.Ok(outcome, message)
            : OperationResult<EmployeeDeletionOutcome>.Fail(message, errorCode);
    }

    private async Task<(bool Found, EmployeeDeletionOutcome Outcome, string? ErrorCode, string Message)> DeleteOrDeactivateCoreAsync(int id, CancellationToken ct)
    {
        var employee = await repo.GetByIdAsync(id, ct);
        if (employee is null)
        {
            return (false, default, ErrorCodes.EmployeeNotFound, "Employee not found.");
        }

        var hasBookings = await repo.HasAnyBookingAsync(id, ct);
        if (!hasBookings)
        {
            try
            {
                await repo.DeleteAsync(id, ct);
                return (true, EmployeeDeletionOutcome.Deleted, null, "Employee deleted.");
            }
            catch (DeleteRestrictedException)
            {
                // Race: a booking appeared between our check and the delete — fall back to deactivation.
            }
        }

        employee.IsActive = false;
        await repo.UpdateAsync(employee, ct);
        return (true, EmployeeDeletionOutcome.Deactivated, null, "Employee has existing bookings; deactivated instead.");
    }

    private static EmployeeDto ToDto(Employee e) => new(e.Id, e.FullName, e.Email, e.IsActive);
}
