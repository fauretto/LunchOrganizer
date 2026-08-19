using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Email.Rendering;
using LunchOrganizer.Email.Sending;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Email;

/// <summary>
/// Orchestrates one run of the daily summary email: builds the summary, renders the body, and either
/// writes a local preview (dry run) or sends it for real while guarding against duplicate sends via
/// <see cref="IEmailLogRepository"/>. Also triggers the per-employee booking confirmations (plan §4.7)
/// after the summary itself has succeeded — never before, and never in a way that can change the
/// summary's own outcome.
/// </summary>
public sealed class DailySummaryMailService(
    IDailySummaryBuilder summaryBuilder,
    IDailySummaryBodyRenderer bodyRenderer,
    IEmailSender emailSender,
    PickupDirectoryEmailSender previewSender,
    IEmailLogRepository emailLogRepository,
    IEmployeeConfirmationSender confirmationSender,
    IOptionsMonitor<EmailOptions> emailOptions,
    IClock clock,
    ILogger<DailySummaryMailService> logger) : IDailySummaryMailService
{
    public async Task<DailySummaryRunResult> RunAsync(DateOnly date, bool dryRun, CancellationToken ct = default)
    {
        var options = emailOptions.CurrentValue;

        if (!options.WorkingDays.Contains(date.DayOfWeek))
        {
            logger.LogInformation("{Date} is not a configured working day; nothing to send.", date);
            return new DailySummaryRunResult(EmailSendStatus.Skipped, $"{date:yyyy-MM-dd} is not a configured working day; nothing to send.", 0, 2);
        }

        if (!dryRun)
        {
            var reserved = await emailLogRepository.TryBeginAsync(date, ct);
            if (!reserved)
            {
                logger.LogInformation("The daily summary for {Date} was already reserved or sent by another process.", date);
                return DailySummaryRunResult.AlreadyHandled($"The daily summary for {date:yyyy-MM-dd} was already reserved or sent by another process.");
            }
        }

        try
        {
            var buildResult = await summaryBuilder.BuildAsync(date, ct);

            if (!buildResult.IsSuccess)
            {
                if (buildResult.ErrorCode == ErrorCodes.NoBookingsForDate)
                {
                    if (!dryRun)
                    {
                        await emailLogRepository.CompleteAsync(date, EmailSendStatus.Skipped, null, 0, null, ct);
                    }

                    return new DailySummaryRunResult(EmailSendStatus.Skipped, $"No bookings for {date:yyyy-MM-dd}; nothing sent.", 0, 2);
                }

                if (!dryRun)
                {
                    await emailLogRepository.CompleteAsync(date, EmailSendStatus.Failed, null, 0, buildResult.Message, ct);
                }

                return new DailySummaryRunResult(EmailSendStatus.Failed, $"Unexpected error: {buildResult.Message}", 0, 1);
            }

            var summary = buildResult.Value!;
            var rendered = bodyRenderer.Render(summary, options);
            var from = new EmailAddress(options.SenderName, options.SenderAddress);
            var message = new EmailMessage(rendered.Subject, rendered.HtmlBody, rendered.TextBody, from, options.Recipients);

            if (dryRun)
            {
                var previewResult = await previewSender.SendAsync(message, ct);
                if (previewResult.IsSuccess)
                {
                    // Confirmations are previewed too (routed to the pickup directory, D3), after the
                    // summary preview itself succeeded. email_log is never touched on a dry run.
                    var confirmationSuffix = await SendConfirmationsSafelyAsync(summary, options, dryRun: true, ct);

                    return new DailySummaryRunResult(
                        EmailSendStatus.Sent,
                        $"Dry run: wrote the {summary.TotalBookingCount}-booking summary for {date:yyyy-MM-dd} to the pickup directory. Nothing was sent and email_log was not touched.{confirmationSuffix}",
                        summary.TotalBookingCount,
                        0);
                }

                return new DailySummaryRunResult(
                    EmailSendStatus.Failed,
                    $"Dry run failed to write the preview: {previewResult.Message}",
                    summary.TotalBookingCount,
                    1);
            }

            if (options.Recipients.Count == 0)
            {
                const string noRecipientsMessage = "No recipients configured in config/email.json (Email:Recipients is empty); the summary was built but not sent.";
                await emailLogRepository.CompleteAsync(date, EmailSendStatus.Failed, null, summary.TotalBookingCount, noRecipientsMessage, ct);
                return new DailySummaryRunResult(EmailSendStatus.Failed, noRecipientsMessage, summary.TotalBookingCount, 1);
            }

            var sendResult = await emailSender.SendAsync(message, ct);
            if (sendResult.IsSuccess)
            {
                await emailLogRepository.CompleteAsync(date, EmailSendStatus.Sent, string.Join(";", options.Recipients), summary.TotalBookingCount, null, ct);

                // Confirmations are sent only after the summary send is durably recorded as Sent —
                // the kitchen email is the one that matters, and D1/D2 require it never be put at risk
                // for the sake of the per-employee courtesy copies.
                var confirmationSuffix = await SendConfirmationsSafelyAsync(summary, options, dryRun: false, ct);
                return new DailySummaryRunResult(EmailSendStatus.Sent, $"Sent to {options.Recipients.Count} recipient(s).{confirmationSuffix}", summary.TotalBookingCount, 0);
            }

            await emailLogRepository.CompleteAsync(date, EmailSendStatus.Failed, null, summary.TotalBookingCount, sendResult.Message, ct);
            return new DailySummaryRunResult(EmailSendStatus.Failed, sendResult.Message ?? "Failed to send the daily summary email.", summary.TotalBookingCount, 1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while running the daily summary mail service for {Date}.", date);

            if (!dryRun)
            {
                try
                {
                    await emailLogRepository.CompleteAsync(date, EmailSendStatus.Failed, null, 0, ex.Message, ct);
                }
                catch
                {
                    // Best effort only; don't mask the original error.
                }
            }

            return new DailySummaryRunResult(EmailSendStatus.Failed, $"Unexpected error: {ex.Message}", 0, 1);
        }
    }

    /// <summary>
    /// Sends the per-employee confirmations for an already-successful summary run and returns a
    /// human-readable suffix to append to <see cref="DailySummaryRunResult.Message"/>. Gated on
    /// <see cref="EmailOptions.SendEmployeeConfirmations"/>, and wrapped so that an unexpected
    /// exception here can never turn this already-successful run into a failure (plan §4 rule 3) —
    /// the caller's <see cref="DailySummaryRunResult.Status"/>, <see cref="DailySummaryRunResult.BookingCount"/>
    /// and <see cref="DailySummaryRunResult.SuggestedExitCode"/> are decided by the summary send alone.
    /// </summary>
    private async Task<string> SendConfirmationsSafelyAsync(DailySummaryDto summary, EmailOptions options, bool dryRun, CancellationToken ct)
    {
        if (!options.SendEmployeeConfirmations)
        {
            return string.Empty;
        }

        try
        {
            var outcome = await confirmationSender.SendAllAsync(summary, dryRun, ct);
            return $" Confirmations: {outcome.Sent} sent, {outcome.SkippedNoEmail} skipped (no email), {outcome.Failed} failed.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while sending employee lunch confirmations for {Date}.", summary.Date);
            return " Confirmations: failed to run.";
        }
    }
}
