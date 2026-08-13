using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / Npgsql-backed implementation of <see cref="IEmailLogRepository"/>.
/// </summary>
public sealed class EmailLogRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IEmailLogRepository
{
    public async Task<bool> TryBeginAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO email_log (summary_date, sent_at_utc, status, recipients, booking_count, error_message)
            VALUES ({date}, now(), {EmailSendStatus.Reserved.ToString()}, NULL, 0, NULL)
            ON CONFLICT (summary_date) DO NOTHING
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
