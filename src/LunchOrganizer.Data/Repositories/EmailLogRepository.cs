using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / SQL Server-backed implementation of <see cref="IEmailLogRepository"/>.
/// </summary>
public sealed class EmailLogRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IEmailLogRepository
{
    public async Task<bool> TryBeginAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // A guarded INSERT, not a MERGE: there is nothing to update on the losing side, so this
        // replaces PostgreSQL's insert-or-do-nothing-on-duplicate behavior. WITH (UPDLOCK, HOLDLOCK)
        // on the existence check is what serialises two racing callers — @@ROWCOUNT (returned here as
        // `affected`) is 1 for the winner and 0 for the loser, which is exactly the contract
        // TryBeginAsync documents, and it is what gives the mailer its exactly-once guarantee.
        // email_log has no `version` column: it is written once per day via this reserve-then-complete
        // pattern, never edited concurrently.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO email_log (summary_date, sent_at_utc, status, recipients, booking_count, error_message)
            SELECT {date}, CAST(SYSUTCDATETIME() AS datetimeoffset), {EmailSendStatus.Reserved.ToString()}, NULL, 0, NULL
            WHERE NOT EXISTS (SELECT 1 FROM email_log WITH (UPDLOCK, HOLDLOCK) WHERE summary_date = {date});
            """, ct);
        return affected > 0;
    }

    public async Task CompleteAsync(DateOnly date, EmailSendStatus status, string? recipients, int bookingCount, string? errorMessage, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entry = await db.EmailLogEntries.SingleAsync(e => e.SummaryDate == date, ct);
        entry.SentAtUtc = DateTimeOffset.UtcNow;
        entry.Status = status;
        entry.Recipients = recipients;
        entry.BookingCount = bookingCount;
        entry.ErrorMessage = errorMessage;
        await db.SaveChangesAsync(ct);
    }

    public async Task<EmailLogEntry?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.EmailLogEntries.AsNoTracking().SingleOrDefaultAsync(e => e.SummaryDate == date, ct);
    }
}
