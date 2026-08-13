using System.Collections.Concurrent;
using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;

namespace LunchOrganizer.Email.InMemory;

/// <summary>
/// Thread-safe, in-process, non-persistent stand-in for the real EF Core-backed
/// <see cref="IEmailLogRepository"/> implementation (from <c>LunchOrganizer.Data</c>), used by
/// <c>LunchOrganizer.Mailer</c> until the backend agent delivers the real one. Swapping it out is a
/// one-line DI change in <c>LunchOrganizer.Mailer/Program.cs</c> with no change to any other
/// Email-project code. It also backs the Email test suite's fake-repository needs (e.g. the
/// reservation-idempotency test).
/// </summary>
public sealed class InMemoryEmailLogRepository(IClock clock) : IEmailLogRepository
{
    private readonly ConcurrentDictionary<DateOnly, EmailLogEntry> _entries = new();

    public Task<bool> TryBeginAsync(DateOnly date, CancellationToken ct = default)
    {
        var entry = new EmailLogEntry
        {
            SummaryDate = date,
            SentAtUtc = clock.UtcNow,
            Status = EmailSendStatus.Reserved,
            Recipients = null,
            BookingCount = 0,
            ErrorMessage = null
        };

        return Task.FromResult(_entries.TryAdd(date, entry));
    }

    public Task CompleteAsync(DateOnly date, EmailSendStatus status, string? recipients, int bookingCount, string? errorMessage, CancellationToken ct = default)
    {
        _entries.AddOrUpdate(
            date,
            addValueFactory: _ => new EmailLogEntry
            {
                SummaryDate = date,
                SentAtUtc = clock.UtcNow,
                Status = status,
                Recipients = recipients,
                BookingCount = bookingCount,
                ErrorMessage = errorMessage
            },
            updateValueFactory: (_, existing) =>
            {
                existing.Status = status;
                existing.Recipients = recipients;
                existing.BookingCount = bookingCount;
                existing.ErrorMessage = errorMessage;
                existing.SentAtUtc = clock.UtcNow;
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task<EmailLogEntry?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        _entries.TryGetValue(date, out var entry);
        return Task.FromResult(entry);
    }
}
