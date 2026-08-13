using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Email.Rendering;

public sealed record RenderedSummaryEmail(string Subject, string HtmlBody, string TextBody);

public interface IDailySummaryBodyRenderer
{
    RenderedSummaryEmail Render(DailySummaryDto summary, EmailOptions options);
}
