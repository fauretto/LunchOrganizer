using System.Globalization;

namespace LunchOrganizer.Email.Localization;

/// <summary>
/// Every user-facing string for the daily summary email body, in French (default) and English,
/// keyed off <see cref="LunchOrganizer.Domain.Configuration.EmailOptions.Language"/>
/// ("fr"/unrecognized => French; "en" => English).
/// </summary>
internal static class EmailBodyText
{
    public static bool IsFrench(string? language) =>
        !string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase);

    public static string HeaderTitle(string? language) =>
        IsFrench(language) ? "Récapitulatif des repas réservés" : "Booked lunches summary";

    public static string DateLabel(string? language) =>
        IsFrench(language) ? "Date" : "Date";

    public static string TotalLine(string? language, int total)
    {
        if (IsFrench(language))
        {
            return $"Total : {total} repas réservé(s)";
        }

        var lunchWord = total == 1 ? "lunch" : "lunches";
        return $"Total: {total} {lunchWord} booked";
    }

    public static string MenuHeading(string? language, int menuNumber, int count)
    {
        if (IsFrench(language))
        {
            return $"Menu {menuNumber} — {count} repas";
        }

        var lunchWord = count == 1 ? "lunch" : "lunches";
        return $"Menu {menuNumber} — {count} {lunchWord}";
    }

    public static string NoDescription(string? language) =>
        IsFrench(language) ? "(pas de description)" : "(no description)";

    public static string EmployeesHeading(string? language) =>
        IsFrench(language) ? "Personnes inscrites :" : "Booked employees:";

    public static string Footer(string? language, DateTimeOffset generatedAtUtc)
    {
        var local = TimeZoneInfo.ConvertTime(generatedAtUtc, TimeZoneInfo.Local);
        var datePart = local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var timePart = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        return IsFrench(language)
            ? $"Message généré automatiquement le {datePart} à {timePart}."
            : $"This message was generated automatically on {datePart} at {timePart}.";
    }

    public static string HtmlDocumentTitle(string? language) => HeaderTitle(language);

    // ---- Per-employee booking confirmation strings (plan §4.5) ----

    public static string ConfirmationHeaderTitle(string? language) =>
        IsFrench(language) ? "Confirmation de votre repas" : "Your lunch booking confirmation";

    public static string ConfirmationGreeting(string? language, string name) =>
        IsFrench(language) ? $"Bonjour {name}," : $"Hello {name},";

    public static string ConfirmationIntro(string? language, string date) =>
        IsFrench(language) ? $"Votre repas est confirmé pour le {date} :" : $"Your lunch is confirmed for {date}:";

    public static string ConfirmationMenuLabel(string? language, int menuNumber) => $"Menu {menuNumber}";

    public static string ConfirmationPriceLabel(string? language, decimal price, string currency)
    {
        var priceText = price.ToString("0.00", CultureInfo.InvariantCulture);
        return IsFrench(language) ? $"Prix : {priceText} {currency}" : $"Price: {priceText} {currency}";
    }

    public static string ConfirmationClosing(string? language) =>
        IsFrench(language) ? "Bon appétit !" : "Enjoy your meal!";

    public static string? ConfirmationBookedBy(string? language, string? userName, string? userFullName, string? userEmail)
    {
        string displayName;
        if (!string.IsNullOrWhiteSpace(userFullName))
        {
            displayName = userFullName;
        }
        else if (!string.IsNullOrWhiteSpace(userName))
        {
            displayName = userName;
        }
        else
        {
            return null;
        }

        var parenthetical = !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(userFullName)
            ? $" ({userName})"
            : string.Empty;

        var emailPart = !string.IsNullOrWhiteSpace(userEmail) ? $" : e-mail : {userEmail}" : string.Empty;

        return IsFrench(language)
            ? $"Votre repas a été réservé par {displayName}{parenthetical}{emailPart}"
            : $"Your lunch has been booked by {displayName}{parenthetical}{emailPart}";
    }
}
