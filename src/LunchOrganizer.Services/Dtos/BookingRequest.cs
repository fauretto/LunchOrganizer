namespace LunchOrganizer.Services.Dtos;

/// <summary>Request to book (or change) a single employee's lunch for one day.</summary>
public sealed record BookingRequest(int EmployeeId, DateOnly BookingDate, int MenuId);
