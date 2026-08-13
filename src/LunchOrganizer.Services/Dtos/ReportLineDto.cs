namespace LunchOrganizer.Services.Dtos;

public sealed record ReportLineDto(DateOnly Date, DayOfWeek DayOfWeek, string EmployeeName, int MenuNumber, string? MenuDescription, decimal Price);
