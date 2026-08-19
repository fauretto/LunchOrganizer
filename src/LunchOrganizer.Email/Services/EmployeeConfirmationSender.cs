using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Email.Validation;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email.Services;

/// <summary>
/// Loops <see cref="DailySummaryDto.EmployeeBookings"/> and sends each employee their own booking
/// confirmation. Every employee gets an independent attempt: one skip or failure must never prevent
/// the loop from reaching the rest (plan §4.6, §4 rule 2).
/// </summary>
public sealed class EmployeeConfirmationSender(
    IEmployeeConfirmationBodyRenderer bodyRenderer,
    IEmailSender emailSender,
    PickupDirectoryEmailSender previewSender,
    IOptionsMonitor<EmailOptions> emailOptions,
    ILogger<EmployeeConfirmationSender> logger) : IEmployeeConfirmationSender
{
    public async Task<ConfirmationSendOutcome> SendAllAsync(DailySummaryDto summary, bool dryRun, CancellationToken ct = default)
    {
        var options = emailOptions.CurrentValue;
        var bookings = summary.EmployeeBookings ?? Array.Empty<EmployeeBookingConfirmationDto>();

        var sent = 0;
        var skippedNoEmail = 0;
        var failed = 0;

        foreach (var booking in bookings)
        {
            // Each employee's send is wrapped in its own try/catch so that an unexpected exception
            // (a bad render, a transient sender fault, anything) can never escape the loop body and
            // deprive every subsequent employee of their confirmation (plan §4 rule 2).
            try
            {
                if (string.IsNullOrWhiteSpace(booking.Email))
                {
                    skippedNoEmail++;
                    continue;
                }

                // An address MailboxAddress can't parse is functionally the same as having none (D5).
                // The employee's name is logged, not the malformed address, to keep it out of logs.
                if (!EmailAddressValidation.IsValidFormat(booking.Email))
                {
                    logger.LogWarning(
                        "Skipping the lunch confirmation for {EmployeeName}: their recorded email address is not a valid address.",
                        booking.FullName);
                    skippedNoEmail++;
                    continue;
                }

                var rendered = bodyRenderer.Render(summary, booking, options);
                var from = new EmailAddress(options.SenderName, options.SenderAddress);
                var message = new EmailMessage(rendered.Subject, rendered.HtmlBody, rendered.TextBody, from, new[] { booking.Email });

                // Dry runs are routed to the pickup directory, same as the summary preview, and never
                // touch the real sender (plan D3).
                var sender = dryRun ? (IEmailSender)previewSender : emailSender;
                var result = await sender.SendAsync(message, ct);

                if (result.IsSuccess)
                {
                    sent++;
                }
                else
                {
                    failed++;
                    logger.LogWarning(
                        "Failed to send the lunch confirmation for {EmployeeName}: {Message}",
                        booking.FullName, result.Message);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "Unexpected error while sending the lunch confirmation for {EmployeeName}.", booking.FullName);
            }
        }

        return new ConfirmationSendOutcome(sent, skippedNoEmail, failed);
    }
}
