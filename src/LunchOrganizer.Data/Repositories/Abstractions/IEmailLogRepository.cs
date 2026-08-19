using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;

namespace LunchOrganizer.Data.Repositories.Abstractions;

/// <summary>
/// Repository abstraction for the email send log, which guards against duplicate lunch-summary emails.
/// </summary>
public interface IEmailLogRepository
{
    /// <summary>
    /// Reserves the day for sending via a guarded INSERT that only succeeds if no row for the date
    /// already exists. Returns true if this call reserved the day (zero rows previously existed),
    /// false if another process already owns the day. Callers that get false must stop and not send
    /// (see implementation plan §11.8) — this is the sole idempotency guarantee against double sends.
    /// </summary>
    Task<bool> TryBeginAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Marks a previously reserved day as complete, recording its outcome.</summary>
    Task CompleteAsync(DateOnly date, EmailSendStatus status, string? recipients, int bookingCount, string? errorMessage, CancellationToken ct = default);

    /// <summary>Retrieves the email log entry for a given date, or null if none exists.</summary>
    Task<EmailLogEntry?> GetAsync(DateOnly date, CancellationToken ct = default);
}
