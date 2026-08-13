namespace LunchOrganizer.Services.Dtos;

public sealed record BookingDto(
    int Id,
    int EmployeeId,
    string EmployeeName,
    DateOnly BookingDate,
    int MenuId,
    int MenuNumber,
    string? MenuDescription,
    decimal PriceSnapshot);
