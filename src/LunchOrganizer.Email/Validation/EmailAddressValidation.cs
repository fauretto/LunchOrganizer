using MimeKit;

namespace LunchOrganizer.Email.Validation;

/// <summary>
/// Single shared "is this a plausible email address" check, used by both the booking page's UI gate
/// and <see cref="LunchOrganizer.Email.Services.EmployeeConfirmationSender"/>. Anything the booking
/// page accepts must also be accepted by the sender, or a user could be told they registered for
/// confirmations and then silently never receive one — so this validator may only ever be stricter
/// than the sender, never looser.
/// </summary>
public static class EmailAddressValidation
{
    public static bool IsValidFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // MimeKit is deliberately lenient: TryParse accepts a bare local-part with no "@domain" at
        // all (fine for parsing mail that already arrived, too permissive for a UI gate that is
        // supposed to reject "bob"). The extra Contains('@') check closes that gap.
        return MailboxAddress.TryParse(value, out var mailbox) && mailbox.Address.Contains('@');
    }
}
