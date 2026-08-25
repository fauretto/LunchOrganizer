using System.Globalization;
using System.Net;
using System.Text;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Localization;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email.Rendering;

/// <summary>
/// Renders one employee's personal booking-confirmation email. Mirrors
/// <see cref="DailySummaryBodyRenderer"/>'s structure and HTML almost exactly (same 600px centred
/// Outlook-safe table, same <c>#1B4F87</c> header, same fonts and padding, same footer), but the body
/// contains only <c>booking</c>'s own menu and price — never the other employees who booked that day.
/// </summary>
/// <remarks>
/// Needs <see cref="IOptionsMonitor{TOptions}"/> of <see cref="AppOptions"/> to read the currency
/// symbol (<see cref="AppOptions.Currency"/>) for the price line — the only new dependency this
/// feature introduces (plan §4.5).
/// </remarks>
public sealed class EmployeeConfirmationBodyRenderer(IOptionsMonitor<AppOptions> appOptions) : IEmployeeConfirmationBodyRenderer
{
    public RenderedConfirmationEmail Render(DailySummaryDto summary, EmployeeBookingConfirmationDto booking, EmailOptions options)
    {
        var subject = options.ConfirmationSubjectPrefix + summary.Date.ToString(options.SubjectDateFormat, CultureInfo.InvariantCulture);
        var currency = appOptions.CurrentValue.Currency;
        var textBody = BuildTextBody(summary, booking, options.Language, currency);
        var htmlBody = BuildHtmlBody(summary, booking, options.Language, currency);

        return new RenderedConfirmationEmail(subject, htmlBody, textBody);
    }

    private static string BuildTextBody(DailySummaryDto summary, EmployeeBookingConfirmationDto booking, string? language, string currency)
    {
        var sb = new StringBuilder();
        var dateText = summary.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var description = string.IsNullOrWhiteSpace(booking.MenuDescription)
            ? EmailBodyText.NoDescription(language)
            : booking.MenuDescription;

        sb.Append(EmailBodyText.ConfirmationHeaderTitle(language)).AppendLine();
        sb.AppendLine();
        sb.Append(EmailBodyText.ConfirmationGreeting(language, booking.FullName)).AppendLine();
        sb.Append(EmailBodyText.ConfirmationIntro(language, dateText)).AppendLine();
        sb.AppendLine();
        sb.Append(EmailBodyText.ConfirmationMenuLabel(language, booking.MenuNumber)).AppendLine();
        sb.Append(description).AppendLine();
        sb.Append(EmailBodyText.ConfirmationPriceLabel(language, booking.PriceSnapshot, currency)).AppendLine();

        var bookedBy = EmailBodyText.ConfirmationBookedBy(language, booking.BookedByUserName, booking.BookedByUserFullName, booking.BookedByUserEmail);
        if (bookedBy is not null)
        {
            sb.AppendLine();
            sb.Append(bookedBy).AppendLine();
        }

        sb.AppendLine();
        sb.Append(EmailBodyText.ConfirmationClosing(language)).AppendLine();
        sb.AppendLine();
        sb.Append("--").AppendLine();
        sb.Append(EmailBodyText.Footer(language, summary.GeneratedAtUtc)).AppendLine();

        return sb.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
    }

    private static string BuildHtmlBody(DailySummaryDto summary, EmployeeBookingConfirmationDto booking, string? language, string currency)
    {
        var sb = new StringBuilder();
        var dateText = summary.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        sb.Append("<html><head><meta charset=\"utf-8\"><title>")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationHeaderTitle(language)))
          .Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;\">");

        // Outer 100%-wide table used purely to center the 600px inner table (standard email-HTML trick).
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"font-family: Segoe UI, Arial, sans-serif;\">");
        sb.Append("<tr><td align=\"center\">");

        // Inner fixed-width table.
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:600px; font-family: Segoe UI, Arial, sans-serif;\">");

        // Header row.
        sb.Append("<tr><td style=\"background-color:#1B4F87; color:#ffffff; font-weight:bold; font-size:18px; padding:16px;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationHeaderTitle(language)))
          .Append("</td></tr>");

        // Greeting row.
        sb.Append("<tr><td style=\"padding:12px 16px 0 16px; font-size:14px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationGreeting(language, booking.FullName)))
          .Append("</td></tr>");

        // Intro row.
        sb.Append("<tr><td style=\"padding:4px 16px 12px 16px; font-size:14px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationIntro(language, dateText)))
          .Append("</td></tr>");

        // Menu heading.
        sb.Append("<tr><td style=\"padding:0 16px 2px 16px; font-weight:bold; font-size:15px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationMenuLabel(language, booking.MenuNumber)))
          .Append("</td></tr>");

        var description = string.IsNullOrWhiteSpace(booking.MenuDescription)
            ? EmailBodyText.NoDescription(language)
            : booking.MenuDescription;
        sb.Append("<tr><td style=\"padding:0 16px 8px 16px; font-style:italic; color:#666666; font-size:13px;\">")
          .Append(WebUtility.HtmlEncode(description))
          .Append("</td></tr>");

        // Price row.
        sb.Append("<tr><td style=\"padding:0 16px 12px 16px; font-size:14px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationPriceLabel(language, booking.PriceSnapshot, currency)))
          .Append("</td></tr>");

        // Booked-by row (only when there is something to say).
        var bookedBy = EmailBodyText.ConfirmationBookedBy(language, booking.BookedByUserName, booking.BookedByUserFullName, booking.BookedByUserEmail);
        if (bookedBy is not null)
        {
            sb.Append("<tr><td style=\"padding:0 16px 12px 16px; font-size:14px; color:#000000;\">")
              .Append(WebUtility.HtmlEncode(bookedBy))
              .Append("</td></tr>");
        }

        // Closing row.
        sb.Append("<tr><td style=\"padding:0 16px 16px 16px; font-size:14px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.ConfirmationClosing(language)))
          .Append("</td></tr>");

        // Footer row.
        sb.Append("<tr><td style=\"padding:16px; font-size:11px; color:#999999;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.Footer(language, summary.GeneratedAtUtc)))
          .Append("</td></tr>");

        sb.Append("</table>"); // inner table
        sb.Append("</td></tr></table>"); // outer table
        sb.Append("</body></html>");

        return sb.ToString();
    }
}
