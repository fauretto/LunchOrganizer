namespace LunchOrganizer.Domain.Common;

/// <summary>
/// Thrown by <c>IMenuRepository.ImportAsync</c> when the database already contains at least one
/// menu for a date being imported. By the time this is thrown, the transaction has already been
/// rolled back, so callers can rely on there being no partial data — either the whole batch was
/// written, or none of it was.
/// </summary>
public sealed class MenuImportConflictException : Exception
{
    public MenuImportConflictException(IReadOnlyList<DateOnly> conflictingDates)
        : base($"Menu import aborted: the database already contains menus for {conflictingDates.Count} of the imported date(s).")
    {
        ConflictingDates = conflictingDates;
    }

    /// <summary>The distinct dates, ascending, that already have at least one menu in the database.</summary>
    public IReadOnlyList<DateOnly> ConflictingDates { get; }
}
