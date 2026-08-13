using System.Collections.Concurrent;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Email;
using LunchOrganizer.Email.Abstractions;

namespace LunchOrganizer.Fakes.Services;

/// <summary>
/// Captures every message instead of sending it. Public (and registered as a singleton) so tests/dev
/// tooling can inspect <see cref="SentMessages"/> to see what would have been sent.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> SentMessages { get; } = new();

    public Task<OperationResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        SentMessages.Enqueue(message);
        return Task.FromResult(OperationResult.Ok("Captured by fake email sender."));
    }
}
