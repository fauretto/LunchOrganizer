namespace LunchOrganizer.Email;

public sealed record EmailMessage(
    string Subject,
    string HtmlBody,
    string TextBody,
    EmailAddress From,
    IReadOnlyList<string> To);
