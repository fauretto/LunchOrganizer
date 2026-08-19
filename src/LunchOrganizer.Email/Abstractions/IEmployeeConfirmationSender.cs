using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Email.Abstractions;

/// <summary>
/// Result of one <see cref="IEmployeeConfirmationSender.SendAllAsync"/> call. Never affects
/// <see cref="LunchOrganizer.Email.DailySummaryRunResult"/>'s status or exit code (plan §4 rule 3) —
/// it exists purely so the caller can report the counts in its human-readable message.
/// </summary>
public sealed record ConfirmationSendOutcome(int Sent, int SkippedNoEmail, int Failed);

/// <summary>
/// Sends one personal booking-confirmation email per entry in <see cref="DailySummaryDto.EmployeeBookings"/>.
/// Kept separate from <see cref="IDailySummaryMailService"/> so that method stays readable and this
/// logic is independently testable (plan §4.6).
/// </summary>
public interface IEmployeeConfirmationSender
{
    /// <summary>
    /// Sends a confirmation to every employee booking in <paramref name="summary"/> that has a usable
    /// email address. Skipping or failing one employee never stops the others — every entry gets its
    /// own attempt regardless of how earlier entries fared (plan §4 rule 2).
    /// </summary>
    Task<ConfirmationSendOutcome> SendAllAsync(DailySummaryDto summary, bool dryRun, CancellationToken ct = default);
}
