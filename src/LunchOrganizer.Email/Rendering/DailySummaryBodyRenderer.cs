using System.Globalization;
using System.Net;
using System.Text;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Localization;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Email.Rendering;

/// <summary>
/// Stateless renderer that turns a <see cref="DailySummaryDto"/> into the subject, plain-text body,
/// and Outlook-safe HTML body of the daily lunch summary email.
/// </summary>
public sealed class DailySummaryBodyRenderer : IDailySummaryBodyRenderer
{
    public RenderedSummaryEmail Render(DailySummaryDto summary, EmailOptions options)
    {
        var subject = options.SubjectPrefix + summary.Date.ToString(options.SubjectDateFormat, CultureInfo.InvariantCulture);
        var textBody = BuildTextBody(summary, options.Language);
        var htmlBody = BuildHtmlBody(summary, options.Language);

        return new RenderedSummaryEmail(subject, htmlBody, textBody);
    }

    private static string BuildTextBody(DailySummaryDto summary, string? language)
    {
        var sb = new StringBuilder();
        var dateText = summary.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        sb.Append(EmailBodyText.HeaderTitle(language)).Append(" - ").Append(dateText).AppendLine();
        sb.Append(EmailBodyText.TotalLine(language, summary.TotalBookingCount)).AppendLine();
        sb.AppendLine();

        foreach (var group in summary.MenuGroups)
        {
            sb.Append(EmailBodyText.MenuHeading(language, group.MenuNumber, group.EmployeeNames.Count)).AppendLine();

            var description = string.IsNullOrWhiteSpace(group.Description)
                ? EmailBodyText.NoDescription(language)
                : group.Description;
            sb.Append(description).AppendLine();

            foreach (var employeeName in group.EmployeeNames)
            {
                sb.Append("  - ").Append(employeeName).AppendLine();
            }

            sb.AppendLine();
        }

        sb.Append("--").AppendLine();
        sb.Append(EmailBodyText.Footer(language, summary.GeneratedAtUtc)).AppendLine();

        return sb.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
    }

    private static string BuildHtmlBody(DailySummaryDto summary, string? language)
    {
        var sb = new StringBuilder();
        var dateText = summary.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        sb.Append("<html><head><meta charset=\"utf-8\"><title>")
          .Append(WebUtility.HtmlEncode(EmailBodyText.HtmlDocumentTitle(language)))
          .Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;\">");

        // Outer 100%-wide table used purely to center the 600px inner table (standard email-HTML trick).
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"font-family: Segoe UI, Arial, sans-serif;\">");
        sb.Append("<tr><td align=\"center\">");

        // Inner fixed-width table.
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:600px; font-family: Segoe UI, Arial, sans-serif;\">");

        // Header row.
        sb.Append("<tr><td style=\"background-color:#1B4F87; color:#ffffff; font-weight:bold; font-size:18px; padding:16px;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.HeaderTitle(language)))
          .Append(" - ")
          .Append(WebUtility.HtmlEncode(dateText))
          .Append("</td></tr>");

        // Total row.
        sb.Append("<tr><td style=\"padding:12px 16px; font-size:14px; color:#000000;\">")
          .Append(WebUtility.HtmlEncode(EmailBodyText.TotalLine(language, summary.TotalBookingCount)))
          .Append("</td></tr>");

        var isFirstGroup = true;
        foreach (var group in summary.MenuGroups)
        {
            if (!isFirstGroup)
            {
                sb.Append("<tr><td style=\"padding:0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td style=\"background-color:#dddddd; font-size:1px; line-height:1px;\">&nbsp;</td></tr></table></td></tr>");
            }
            isFirstGroup = false;

            sb.Append("<tr><td style=\"padding:12px 16px 2px 16px; font-weight:bold; font-size:15px; color:#000000;\">")
              .Append(WebUtility.HtmlEncode(EmailBodyText.MenuHeading(language, group.MenuNumber, group.EmployeeNames.Count)))
              .Append("</td></tr>");

            var description = string.IsNullOrWhiteSpace(group.Description)
                ? EmailBodyText.NoDescription(language)
                : group.Description;
            sb.Append("<tr><td style=\"padding:0 16px 8px 16px; font-style:italic; color:#666666; font-size:13px;\">")
              .Append(WebUtility.HtmlEncode(description))
              .Append("</td></tr>");

            foreach (var employeeName in group.EmployeeNames)
            {
                sb.Append("<tr><td style=\"padding:0 16px 2px 32px; font-size:13px; color:#000000;\">&bull; ")
                  .Append(WebUtility.HtmlEncode(employeeName))
                  .Append("</td></tr>");
            }

            sb.Append("<tr><td style=\"padding:0 0 8px 0;\">&nbsp;</td></tr>");
        }

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
