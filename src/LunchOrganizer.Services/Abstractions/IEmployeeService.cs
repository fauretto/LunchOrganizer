using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> SearchAsync(string fragment, CancellationToken ct = default);
    Task<EmployeeDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDto>> GetAllAsync(bool includeInactive, CancellationToken ct = default);

    /// <summary>
    /// Atomic get-or-create by name (see IEmployeeRepository.AddAsync contract, plan §11.6).
    /// <paramref name="email"/> is optional so the admin "add employee" call site
    /// (<c>AdminEmployeesViewModel.cs</c>) keeps compiling unchanged — email stays optional for
    /// admin-created employees by design; only the booking page's self-registration path enforces it.
    /// </summary>
    Task<OperationResult<EmployeeDto>> RegisterAsync(string fullName, string? email = null, CancellationToken ct = default);

    Task<OperationResult<EmployeeDto>> UpdateAsync(int id, string fullName, string? email, CancellationToken ct = default);

    /// <summary>Hard-deletes if the employee has zero bookings ever; otherwise deactivates. The result message states which happened.</summary>
    Task<OperationResult> DeleteOrDeactivateAsync(int id, CancellationToken ct = default);

    /// <summary>Returns true if the given employee has at least one booking, ever.</summary>
    Task<bool> HasBookingsAsync(int employeeId, CancellationToken ct = default);

    /// <summary>
    /// Same behavior as <see cref="DeleteOrDeactivateAsync"/> (hard-deletes if zero bookings ever,
    /// otherwise deactivates) but additionally returns an explicit discriminator of which outcome
    /// occurred, so callers don't have to infer it by re-reading the employee afterwards.
    /// </summary>
    Task<OperationResult<EmployeeDeletionOutcome>> DeleteOrDeactivateWithOutcomeAsync(int id, CancellationToken ct = default);
}
