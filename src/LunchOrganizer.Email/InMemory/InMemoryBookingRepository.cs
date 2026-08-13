using System.Collections.Concurrent;
using System.Threading;
using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Email.InMemory;

/// <summary>
/// Thread-safe, in-process, non-persistent stand-in for the real EF Core-backed
/// <see cref="IBookingRepository"/> implementation (from <c>LunchOrganizer.Data</c>), used by
/// <c>LunchOrganizer.Mailer</c> until the backend agent delivers the real one. Swapping it out is a
/// one-line DI change in <c>LunchOrganizer.Mailer/Program.cs</c> with no change to any other
/// Email-project code. It also backs the Email test suite's fake-repository needs (e.g. the
/// reservation-idempotency test).
/// </summary>
public sealed class InMemoryBookingRepository : IBookingRepository
{
    private readonly ConcurrentDictionary<string, Booking> _bookings = new();
    private int _nextId;

    private static string BuildKey(int employeeId, DateOnly date) => $"{employeeId}:{date:O}";

    /// <summary>Inserts or overwrites the given bookings by their (employeeId, bookingDate) key, for preloading sample data.</summary>
    public void Seed(IEnumerable<Booking> bookings)
    {
        foreach (var booking in bookings)
        {
            if (booking.Id > _nextId)
            {
                _nextId = booking.Id;
            }

            _bookings[BuildKey(booking.EmployeeId, booking.BookingDate)] = booking;
        }
    }

    public Task<IReadOnlyList<Booking>> GetForEmployeeAndWeekAsync(int employeeId, DateOnly monday, CancellationToken ct = default)
    {
        var sunday = monday.AddDays(6);
        IReadOnlyList<Booking> result = _bookings.Values
            .Where(b => b.EmployeeId == employeeId && b.BookingDate >= monday && b.BookingDate <= sunday)
            .OrderBy(b => b.BookingDate)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Booking?> GetForEmployeeAndDateAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        _bookings.TryGetValue(BuildKey(employeeId, date), out var booking);
        return Task.FromResult(booking);
    }

    public Task<IReadOnlyList<Booking>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<Booking> result = _bookings.Values
            .Where(b => b.BookingDate == date)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Booking>> GetForEmployeeBetweenAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        IReadOnlyList<Booking> result = _bookings.Values
            .Where(b => b.BookingDate >= from && b.BookingDate <= to && (employeeId == null || b.EmployeeId == employeeId.Value))
            .OrderBy(b => b.BookingDate)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Booking> UpsertAsync(Booking booking, CancellationToken ct = default)
    {
        var key = BuildKey(booking.EmployeeId, booking.BookingDate);
        var now = DateTimeOffset.UtcNow;

        var stored = _bookings.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                if (booking.Id == 0)
                {
                    booking.Id = Interlocked.Increment(ref _nextId);
                }

                booking.CreatedAtUtc = booking.CreatedAtUtc == default ? now : booking.CreatedAtUtc;
                booking.UpdatedAtUtc = now;
                booking.Version = 1;
                return booking;
            },
            updateValueFactory: (_, existing) =>
            {
                booking.Id = existing.Id;
                booking.CreatedAtUtc = existing.CreatedAtUtc;
                booking.UpdatedAtUtc = now;
                booking.Version = existing.Version + 1;
                return booking;
            });

        return Task.FromResult(stored);
    }

    public Task DeleteAsync(int employeeId, DateOnly bookingDate, CancellationToken ct = default)
    {
        _bookings.TryRemove(BuildKey(employeeId, bookingDate), out _);
        return Task.CompletedTask;
    }
}
