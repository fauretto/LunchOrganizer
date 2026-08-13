namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// <paramref name="Reason"/> is an English developer-facing fallback only — never show it to an
/// end user as-is. <paramref name="ReasonCode"/> is a stable <see cref="LunchOrganizer.Domain.Common.ErrorCodes"/>
/// value (when set) that the UI should localize instead, formatted with <paramref name="ReasonArgs"/>.
/// </summary>
public sealed record SkippedDayDto(DateOnly Date, string Reason, string? ReasonCode = null, IReadOnlyList<object?>? ReasonArgs = null);

public sealed record WeekBookingResultDto(IReadOnlyList<DateOnly> AppliedDates, IReadOnlyList<SkippedDayDto> SkippedDays);
