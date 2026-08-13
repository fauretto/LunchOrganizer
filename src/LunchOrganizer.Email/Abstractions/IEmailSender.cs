using LunchOrganizer.Domain.Common;
using LunchOrganizer.Email;

namespace LunchOrganizer.Email.Abstractions;

public interface IEmailSender
{
    Task<OperationResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
