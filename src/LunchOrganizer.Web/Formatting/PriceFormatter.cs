using System.Globalization;

namespace LunchOrganizer.Web.Formatting;

public static class PriceFormatter
{
    public static string Format(decimal amount, string currency, CultureInfo culture) =>
        $"{currency} {amount.ToString("N2", culture)}";
}
