namespace LunchOrganizer.Domain;

public static class BusinessRules
{
    /// <summary>Fallback max menus/day if AppOptions.MaxMenusPerDay is not configured.</summary>
    public const int DefaultMaxMenusPerDay = 10;

    /// <summary>Max retry attempts for optimistic insert races (e.g. menu-number collisions).</summary>
    public const int MaxInsertRetryAttempts = 3;

    /// <summary>Autocomplete debounce delay used by the UI, in milliseconds.</summary>
    public const int AutocompleteDebounceMilliseconds = 250;

    /// <summary>Maximum accepted size of an uploaded menu-import .docx file, in bytes.</summary>
    public const long MaxMenuImportBytes = 10L * 1024 * 1024;

    /// <summary>
    /// <see cref="MaxMenuImportBytes"/> expressed in whole megabytes, for the user-facing
    /// "file too large" message. Keep the two in sync.
    /// </summary>
    public const int MaxMenuImportMegabytes = 10;
}
