using System.Net.Sockets;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email.Sending;

/// <summary>
/// Sends email over SMTP via MailKit. Never logs credentials (username/password) at any level.
/// </summary>
public sealed class SmtpEmailSender(
    IOptionsMonitor<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private const int MaxAttempts = 2;

    public async Task<OperationResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var opts = options.CurrentValue;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var client = new SmtpClient
                {
                    Timeout = 30000
                };

                await client.ConnectAsync(
                    opts.SmtpHost,
                    opts.SmtpPort,
                    opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                    ct);

                if (!string.IsNullOrEmpty(opts.Username))
                {
                    await client.AuthenticateAsync(opts.Username, opts.Password, ct);
                }

                var mime = MimeMessageFactory.Create(message);
                await client.SendAsync(mime, ct);
                await client.DisconnectAsync(true, ct);

                logger.LogInformation(
                    "Sent email via SMTP to {SmtpHost}:{SmtpPort} with subject {Subject}.",
                    opts.SmtpHost,
                    opts.SmtpPort,
                    message.Subject);

                return OperationResult.Ok("Sent.");
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
            {
                lastException = ex;

                if (attempt < MaxAttempts)
                {
                    logger.LogWarning(
                        ex,
                        "Transient failure ({ExceptionType}) sending email via SMTP to {SmtpHost}:{SmtpPort} with subject {Subject}; retrying.",
                        ex.GetType().Name,
                        opts.SmtpHost,
                        opts.SmtpPort,
                        message.Subject);

                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        logger.LogError(
            lastException,
            "Failed to send email via SMTP to {SmtpHost}:{SmtpPort} with subject {Subject}.",
            opts.SmtpHost,
            opts.SmtpPort,
            message.Subject);

        return OperationResult.Fail($"Failed to send email via SMTP: {lastException?.Message}");
    }
}
