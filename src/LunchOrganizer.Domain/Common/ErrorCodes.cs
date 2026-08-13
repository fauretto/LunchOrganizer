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
}
