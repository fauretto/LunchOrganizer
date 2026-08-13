namespace LunchOrganizer.Services.Dtos;

/// <summary>Discriminates what <see cref="LunchOrganizer.Services.Abstractions.IEmployeeService.DeleteOrDeactivateWithOutcomeAsync"/> actually did.</summary>
public enum EmployeeDeletionOutcome
{
    Deleted,
    Deactivated
}
