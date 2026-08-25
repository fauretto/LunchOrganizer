using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// In-memory, dictionary-backed fake of <see cref="IBookingRepository"/> for unit tests. Auto-increments
/// ids starting at 1. <see cref="UpsertAsync"/> emulates the real atomic upsert on (employee, date) by
/// finding-or-replacing the row in place. <see cref="UpsertManyAsync"/> is left to the interface's default
/// sequential-fallback implementation, which is sufficient for single-threaded unit tests.
/// </summary>
public sealed class FakeBookingRepository : IBookingRepository
{
    private readonly Dictionary<int, Booking> _bookings = new();
    private int _nextId = 1;

    public Task<IReadOnlyList<Booking>> GetForEmployeeAndWeekAsync(int employeeId, DateOnly monday, CancellationToken ct = default)
    {
        var friday = monday.AddDays(4);
        IReadOnlyList<Booking> results = _bookings.Values
            .Where(b => b.EmployeeId == employeeId && b.BookingDate >= monday && b.BookingDate <= friday)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<Booking?> GetForEmployeeAndDateAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        var booking = _bookings.Values.FirstOrDefault(b => b.EmployeeId == employeeId && b.BookingDate == date);
        return Task.FromResult(booking);
    }

    public Task<IReadOnlyList<Booking>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<Booking> results = _bookings.Values.Where(b => b.BookingDate == date).ToList();
        return Task.FromResult(results);
    }

    public Task<IReadOnlyList<Booking>> GetForEmployeeBetweenAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        IReadOnlyList<Booking> results = _bookings.Values
            .Where(b => (employeeId is null || b.EmployeeId == employeeId) && b.BookingDate >= from && b.BookingDate <= to)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<Booking> UpsertAsync(Booking booking, CancellationToken ct = default)
    {
        var existing = _bookings.Values.FirstOrDefault(b => b.EmployeeId == booking.EmployeeId && b.BookingDate == booking.BookingDate);
        if (existing is not null)
        {
            existing.MenuId = booking.MenuId;
            existing.PriceSnapshot = booking.PriceSnapshot;
            existing.UserName = booking.UserName;
            existing.UserFullName = booking.UserFullName;
            existing.UserEmail = booking.UserEmail;
            return Task.FromResult(existing);
        }

        booking.Id = _nextId++;
        _bookings[booking.Id] = booking;
        return Task.FromResult(booking);
    }

    public Task DeleteAsync(int employeeId, DateOnly bookingDate, CancellationToken ct = default)
    {
        var existing = _bookings.Values.FirstOrDefault(b => b.EmployeeId == employeeId && b.BookingDate == bookingDate);
        if (existing is not null)
        {
            _bookings.Remove(existing.Id);
        }

        return Task.CompletedTask;
    }
}
