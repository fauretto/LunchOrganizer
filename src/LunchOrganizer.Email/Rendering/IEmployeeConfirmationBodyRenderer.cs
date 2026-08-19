using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Email.Rendering;

public sealed record RenderedConfirmationEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Renders one employee's personal lunch-booking confirmation: subject, HTML body, and plain-text
/// body. The rendered content carries only <paramref name="booking"/>'s own details — never the rest
/// of <paramref name="summary"/>'s employees — which is the privacy property the feature exists to
/// preserve (plan §4.4, §4 rule 4).
/// </summary>
public interface IEmployeeConfirmationBodyRenderer
{
    RenderedConfirmationEmail Render(DailySummaryDto summary, EmployeeBookingConfirmationDto booking, EmailOptions options);
}
