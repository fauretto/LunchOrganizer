namespace LunchOrganizer.Domain.Configuration;

public sealed class AppOptions
{
    public const string SectionName = "App";

    public decimal DefaultLunchPrice { get; set; } = 0m;
    public string Currency { get; set; } = "CHF";
    public string DefaultCulture { get; set; } = "fr-CH";
    public IList<string> SupportedCultures { get; set; } = new List<string> { "fr-CH", "en-CH" };
    public TimeOnly BookingCutOffLocalTime { get; set; } = new TimeOnly(9, 0);
    public int AutocompleteMinChars { get; set; } = 2;
    public int AutocompleteMaxResults { get; set; } = 10;
    public int MaxMenusPerDay { get; set; } = 10;
}
