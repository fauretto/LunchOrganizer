using LunchOrganizer.Domain.Enums;

namespace LunchOrganizer.Email;

/// <summary>
/// Outcome of one run of the daily summary mail service, used by LunchOrganizer.Mailer to decide its process
/// exit code: 0 = Sent, 1 = Failed, 2 = Skipped (no bookings), 3 = AlreadyHandled (another process already
/// reserved/sent this day's email_log row, see implementation plan §11.8).
/// </summary>
public sealed record DailySummaryRunResult(EmailSendStatus Status, string Message, int BookingCount, int SuggestedExitCode)
{
    public static DailySummaryRunResult AlreadyHandled(string message) =>
        new(EmailSendStatus.Skipped, message, 0, SuggestedExitCode: 3);
}
