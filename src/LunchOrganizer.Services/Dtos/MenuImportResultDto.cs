namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// The parse-only result shown to the user for confirmation before a menu import is committed to
/// the database. Nothing has been persisted yet at this point.
/// </summary>
/// <param name="WeeksParsed">The number of top-level tables (weeks) that yielded at least one day row.</param>
/// <param name="DaysParsed">The total number of working-day rows recognized in the document.</param>
/// <param name="MenusParsed">The total number of non-empty menu entries recognized across all working days.</param>
/// <param name="FirstDate">The earliest resolved working-day date in the document.</param>
/// <param name="LastDate">The latest resolved working-day date in the document.</param>
/// <param name="WeekdayMismatchExamples">
/// At most 3 already-formatted, culture-invariant strings describing weekday/label mismatches
/// found during parsing, e.g. <c>"LUNDI 17.12 → 17.12.2026 is a Thursday"</c>.
/// </param>
/// <param name="SkippedNonWorkingDayCount">The number of Saturday/Sunday rows found and excluded from the import.</param>
public sealed record MenuImportPreviewDto(
    int WeeksParsed,
    int DaysParsed,
    int MenusParsed,
    DateOnly FirstDate,
    DateOnly LastDate,
    IReadOnlyList<string> WeekdayMismatchExamples,
    int SkippedNonWorkingDayCount);

/// <summary>
/// The result of a successful, committed menu import: what was actually written to the database.
/// </summary>
/// <param name="WeeksParsed">The number of top-level tables (weeks) that yielded at least one day row.</param>
/// <param name="DaysImported">The number of working days for which menus were imported.</param>
/// <param name="MenusImported">The total number of menu rows inserted.</param>
/// <param name="FirstDate">The earliest imported date.</param>
/// <param name="LastDate">The latest imported date.</param>
/// <param name="SkippedNonWorkingDayCount">The number of Saturday/Sunday rows found and excluded from the import.</param>
public sealed record MenuImportResultDto(
    int WeeksParsed,
    int DaysImported,
    int MenusImported,
    DateOnly FirstDate,
    DateOnly LastDate,
    int SkippedNonWorkingDayCount);
