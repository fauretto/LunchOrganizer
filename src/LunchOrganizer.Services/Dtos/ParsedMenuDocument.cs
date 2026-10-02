namespace LunchOrganizer.Services.Dtos;

/// <summary>
/// One non-empty menu-number/description pair extracted from a day's row.
/// </summary>
/// <param name="MenuNumber">The menu's number within the day (1-based; column position in the document, the header's declared "MENU n", or an in-cell "MENU n" label).</param>
/// <param name="Description">The non-empty, whitespace-normalized menu description text.</param>
/// <param name="Price">The price stated in the document for this menu, overriding the day price; null when the document states none.</param>
public sealed record ParsedMenuEntry(int MenuNumber, string Description, decimal? Price = null);

/// <summary>
/// One recognized day row: its resolved calendar date, the raw day-label text as it appeared in
/// the document (for diagnostics), and its non-empty menus ordered by <see cref="ParsedMenuEntry.MenuNumber"/>.
/// </summary>
/// <param name="Date">The calendar date resolved for this row (see the year-rollover algorithm in <c>MenuDocumentParser</c>).</param>
/// <param name="DayLabel">The raw, as-found text of the row's first cell (e.g. "LUNDI, 17.12"), kept for diagnostics and error messages.</param>
/// <param name="Menus">The row's non-empty menus, ordered by <see cref="ParsedMenuEntry.MenuNumber"/> ascending. May be empty when every description cell in the row was blank.</param>
public sealed record ParsedMenuDay(DateOnly Date, string DayLabel, IReadOnlyList<ParsedMenuEntry> Menus);

/// <summary>
/// A soft warning — a day row whose day-name label disagrees with the resolved date's actual day
/// of week. Never an error; the caller turns it into a confirmation prompt.
/// </summary>
/// <param name="Date">The date resolved for the row from its day/month cell and the applicable year.</param>
/// <param name="DocumentDayLabel">The raw day-label text as it appeared in the document (e.g. "JEUDI, 17.12").</param>
/// <param name="ActualDayOfWeek">The day of week that <paramref name="Date"/> actually falls on.</param>
public sealed record WeekdayMismatch(DateOnly Date, string DocumentDayLabel, DayOfWeek ActualDayOfWeek);

/// <summary>
/// The full result of parsing one .docx weekly-menu document: every recognized day row, split
/// into working days and skipped non-working days, plus any weekday/label mismatches found along
/// the way.
/// </summary>
/// <param name="WeeksParsed">The number of top-level tables that yielded at least one day row.</param>
/// <param name="WorkingDays">Monday-Friday rows in document order, each possibly with an empty <c>Menus</c> list when every description cell in that row was blank.</param>
/// <param name="SkippedNonWorkingDays">Saturday/Sunday rows: excluded from import but reported to the user (plan §9 decision: skip and report).</param>
/// <param name="WeekdayMismatches">Soft warnings about day rows whose label disagrees with the resolved date's actual day of week. See <see cref="WeekdayMismatch"/>.</param>
public sealed record ParsedMenuDocument(
    int WeeksParsed,
    IReadOnlyList<ParsedMenuDay> WorkingDays,
    IReadOnlyList<ParsedMenuDay> SkippedNonWorkingDays,
    IReadOnlyList<WeekdayMismatch> WeekdayMismatches);
