namespace LunchOrganizer.Web.Formatting;

public static class PriceParser
{
    /// <summary>Accepts "12.50", "12,50", and "12" alike (plan §6.6) — a comma must never produce an error.</summary>
    public static bool TryParse(string? input, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var normalized = input.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
