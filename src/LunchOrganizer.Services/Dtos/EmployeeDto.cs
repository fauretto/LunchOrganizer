namespace LunchOrganizer.Services.Dtos;

public sealed record EmployeeDto(int Id, string FullName, string? Email, bool IsActive);
