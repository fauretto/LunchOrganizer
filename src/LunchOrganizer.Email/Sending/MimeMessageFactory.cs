using LunchOrganizer.Email;
using MimeKit;

namespace LunchOrganizer.Email.Sending;

/// <summary>Builds a MimeKit <see cref="MimeMessage"/> from a frozen <see cref="EmailMessage"/>.</summary>
internal static class MimeMessageFactory
{
    public static MimeMessage Create(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.From.Name, message.From.Address));
        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        mime.Subject = message.Subject;
        var builder = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
