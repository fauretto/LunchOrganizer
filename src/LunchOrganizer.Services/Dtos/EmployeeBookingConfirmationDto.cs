namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// One employee's own booking for the day, carried alongside <see cref="DailySummaryDto"/> so the
/// per-employee confirmation email can be rendered without a second repository query (plan §4.1).
/// </summary>
public sealed record EmployeeBookingConfirmationDto(
    int EmployeeId,
    string FullName,
    string? Email,
    int MenuNumber,
    string? MenuDescription,
    decimal PriceSnapshot);
