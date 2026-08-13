namespace LunchOrganizer.Services.Dtos;

/// <summary>One menu's booking group for the daily summary email: the menu plus the alphabetical list of employee names who booked it.</summary>
public sealed record DailySummaryMenuGroupDto(int MenuId, int MenuNumber, string? Description, IReadOnlyList<string> EmployeeNames);
