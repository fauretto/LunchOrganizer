using System.Globalization;

namespace LunchOrganizer.Email.Localization;

internal static class EmailCulture
{
    public static CultureInfo Resolve(string? language) =>
        string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("en-CH")
            : CultureInfo.GetCultureInfo("fr-CH"); // fr-CH is the default, matching EmailOptions.Language default "fr"
}
