namespace LunchOrganizer.Services.Dtos;

public sealed record WeekViewDto(
    WeekIdentifier Week,
    int? EmployeeId,
    string? EmployeeName,
    IReadOnlyList<DayViewDto> Days);
