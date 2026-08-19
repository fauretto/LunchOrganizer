namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// <paramref name="EmployeeBookings"/> is optional (defaults to <see langword="null"/>) because nine
/// existing renderer tests construct this record with the positional arguments that predate the
/// per-employee confirmation feature (plan §4.2). The real builder always populates it; a
/// <see langword="null"/> value is treated as "no confirmations to send" by its consumers.
/// </summary>
public sealed record DailySummaryDto(
    DateOnly Date,
    int TotalBookingCount,
    IReadOnlyList<DailySummaryMenuGroupDto> MenuGroups,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<EmployeeBookingConfirmationDto>? EmployeeBookings = null);
