namespace LunchOrganizer.Domain;

public static class BusinessRules
{
    /// <summary>Fallback max menus/day if AppOptions.MaxMenusPerDay is not configured.</summary>
    public const int DefaultMaxMenusPerDay = 10;

    /// <summary>Max retry attempts for optimistic insert races (e.g. menu-number collisions).</summary>
    public const int MaxInsertRetryAttempts = 3;

    /// <summary>Autocomplete debounce delay used by the UI, in milliseconds.</summary>
    public const int AutocompleteDebounceMilliseconds = 250;
}
