namespace LunchOrganizer.Services.Dtos;

public sealed record DailySummaryDto(DateOnly Date, int TotalBookingCount, IReadOnlyList<DailySummaryMenuGroupDto> MenuGroups, DateTimeOffset GeneratedAtUtc);
