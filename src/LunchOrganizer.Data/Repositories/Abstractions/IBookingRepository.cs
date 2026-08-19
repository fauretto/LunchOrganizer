using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Data.Repositories.Abstractions;

/// <summary>
/// Repository abstraction for querying and persisting <see cref="Booking"/> entities.
/// </summary>
public interface IBookingRepository
{
    /// <summary>Retrieves all bookings for an employee within the week starting on the given Monday.</summary>
    Task<IReadOnlyList<Booking>> GetForEmployeeAndWeekAsync(int employeeId, DateOnly monday, CancellationToken ct = default);

    /// <summary>Retrieves the booking for an employee on a given date, or null if not found.</summary>
    Task<Booking?> GetForEmployeeAndDateAsync(int employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>Retrieves all bookings for all employees on a given date.</summary>
    Task<IReadOnlyList<Booking>> GetForDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Pass employeeId = null to fetch bookings for all employees in the range (admin report).</summary>
    Task<IReadOnlyList<Booking>> GetForEmployeeBetweenAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Atomic upsert: a single MERGE statement that inserts a new booking or updates the existing one
    /// for the same employee/day, executed as a single statement with no read-modify-write window.
    /// Two concurrent bookings for the same employee/day resolve to exactly one row, last writer wins,
    /// no exception (see implementation plan §11.4).
    /// </summary>
    Task<Booking> UpsertAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Deletes the booking for an employee on a given date, if any.</summary>
    Task DeleteAsync(int employeeId, DateOnly bookingDate, CancellationToken ct = default);

    /// <summary>
    /// Upserts multiple bookings for possibly-different employees/dates inside a SINGLE database
    /// transaction (all commit or none), applying each upsert in the order given by the caller — used
    /// by BookingService.BookWeekAsync so that "book my whole week" is atomic across days (plan §11.4).
    /// Each individual upsert is still the same atomic MERGE statement as UpsertAsync, just run
    /// repeatedly inside one transaction/one context instead of one per call.
    /// Declared as a default interface method (with this naive sequential fallback body) purely so that
    /// pre-existing implementers of this interface elsewhere in the solution (e.g. an in-memory test
    /// double) keep compiling without modification; the real, SQL Server-backed <c>BookingRepository</c>
    /// overrides this with a true single-transaction implementation.
    /// </summary>
    async Task<IReadOnlyList<Booking>> UpsertManyAsync(IReadOnlyList<Booking> bookings, CancellationToken ct = default)
    {
        var results = new List<Booking>(bookings.Count);
        foreach (var booking in bookings)
        {
            results.Add(await UpsertAsync(booking, ct));
        }
        return results;
    }
}
