namespace LunchOrganizer.Domain.Common;

/// <summary>
/// Stable error-code keys used as the <see cref="OperationResult.ErrorCode"/> /
/// <see cref="OperationResult{T}.ErrorCode"/> value on failures. The UI maps each of these keys
/// to a localized resource string (French default, English toggle) and formats it with the
/// operation's <c>MessageArgs</c>. Each constant's literal value equals its own name.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The requested week lies entirely in the past.</summary>
    public const string WeekInPast = "WeekInPast";

    /// <summary>The requested booking date lies in the past.</summary>
    public const string BookingDateInPast = "BookingDateInPast";

    /// <summary>The booking cut-off time for the day has already passed. arg0: cut-off time.</summary>
    public const string BookingCutOffPassed = "BookingCutOffPassed";

    /// <summary>No menu exists for the requested day.</summary>
    public const string MenuNotFoundForDay = "MenuNotFoundForDay";

    /// <summary>No menu exists anywhere in the requested week.</summary>
    public const string MenuNotFoundInWeek = "MenuNotFoundInWeek";

    /// <summary>The requested employee could not be found.</summary>
    public const string EmployeeNotFound = "EmployeeNotFound";

    /// <summary>An employee name is required but was missing or blank.</summary>
    public const string EmployeeNameRequired = "EmployeeNameRequired";

    /// <summary>An employee with this name already exists. arg0: the conflicting name.</summary>
    public const string EmployeeNameAlreadyExists = "EmployeeNameAlreadyExists";

    /// <summary>The employee cannot be removed because they have bookings. arg0: booking count.</summary>
    public const string EmployeeHasBookings = "EmployeeHasBookings";

    /// <summary>The menu cannot be removed because it has bookings. arg0: booking count.</summary>
    public const string MenuHasBookings = "MenuHasBookings";

    /// <summary>The menu's date lies in the past.</summary>
    public const string MenuDateInPast = "MenuDateInPast";

    /// <summary>The menu number conflicts with another menu on the same day.</summary>
    public const string MenuNumberConflict = "MenuNumberConflict";

    /// <summary>The maximum number of menus per day has been reached. arg0: the configured maximum.</summary>
    public const string MaxMenusPerDayReached = "MaxMenusPerDayReached";

    /// <summary>The supplied price is invalid.</summary>
    public const string PriceInvalid = "PriceInvalid";

    /// <summary>The supplied date range is invalid.</summary>
    public const string DateRangeInvalid = "DateRangeInvalid";

    /// <summary>The operation lost a concurrency check against a concurrent update.</summary>
    public const string ConcurrencyConflict = "ConcurrencyConflict";

    /// <summary>The daily summary email has already been sent for this date.</summary>
    public const string EmailAlreadySentForDate = "EmailAlreadySentForDate";

    /// <summary>There are no bookings for the requested date.</summary>
    public const string NoBookingsForDate = "NoBookingsForDate";

    /// <summary>An unexpected, non-business failure occurred.</summary>
    public const string Unexpected = "Unexpected";

    /// <summary>The uploaded file is not a readable Word (.docx) document (not a ZIP, no word/document.xml, or malformed XML).</summary>
    public const string MenuImportInvalidDocument = "MenuImportInvalidDocument";

    /// <summary>The uploaded menu-import file exceeds the maximum allowed size. arg0: maximum allowed size in MB.</summary>
    public const string MenuImportFileTooLarge = "MenuImportFileTooLarge";

    /// <summary>The document contained no recognizable menu data.</summary>
    public const string MenuImportNoDataFound = "MenuImportNoDataFound";

    /// <summary>A day cell in the document could not be resolved to a valid date. arg0: the offending day cell text.</summary>
    public const string MenuImportInvalidDate = "MenuImportInvalidDate";

    /// <summary>The resolved dates in the document are not in chronological order. arg0: the previous resolved date, arg1: the offending day cell text.</summary>
    public const string MenuImportDatesNotChronological = "MenuImportDatesNotChronological";

    /// <summary>The same date appears more than once within the imported document. arg0: the date that appears more than once.</summary>
    public const string MenuImportDuplicateInDocument = "MenuImportDuplicateInDocument";

    /// <summary>
    /// A day row the document labels as a working day (Monday–Friday) resolves onto a Saturday or
    /// Sunday for the selected year — near-certain proof the wrong year was chosen.
    /// arg0: the offending day cell text. arg1: the resolved date.
    /// </summary>
    public const string MenuImportWeekdayMismatch = "MenuImportWeekdayMismatch";

    /// <summary>The database already contains menus for one or more of the imported dates. arg0: count of conflicting dates, arg1: the conflicting dates as a pre-joined string.</summary>
    public const string MenuImportDuplicateMenusExist = "MenuImportDuplicateMenusExist";

    /// <summary>An unexpected failure occurred while importing the document; nothing was saved.</summary>
    public const string MenuImportFailed = "MenuImportFailed";
}
