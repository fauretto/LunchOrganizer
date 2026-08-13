namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// <paramref name="Label"/> must be populated culture-neutrally — e.g. an ISO-8601 week string
/// such as "2026-W34" — never a hard-coded English phrase like "Week 34 · ...". Implementations
/// are responsible for populating it accordingly; this record only carries the contract.
/// </summary>
public sealed record WeekIdentifier(int IsoYear, int IsoWeek, DateOnly Monday, string Label);
