namespace LunchOrganizer.Services.Dtos;

public sealed record ReportDto(IReadOnlyList<ReportLineDto> Lines, decimal Total);
