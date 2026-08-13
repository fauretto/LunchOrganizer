using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / Npgsql-backed implementation of <see cref="IBookingRepository"/>.
/// </summary>
public sealed class BookingRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IBookingRepository
{
    public async Task<IReadOnlyList<Booking>> GetForEmployeeAndWeekAsync(int employeeId, DateOnly monday, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var friday = monday.AddDays(4);
        return await db.Bookings.AsNoTracking()
            .Include(b => b.Employee)
            .Include(b => b.Menu)
            .Where(b => b.EmployeeId == employeeId && b.BookingDate >= monday && b.BookingDate <= friday)
            .ToListAsync(ct);
    }

    public async Task<Booking?> GetForEmployeeAndDateAsync(int employeeId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking()
            .Include(b => b.Employee)
            .Include(b => b.Menu)
            .SingleOrDefaultAsync(b => b.EmployeeId == employeeId && b.BookingDate == date, ct);
    }

    public async Task<IReadOnlyList<Booking>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking()
            .Include(b => b.Employee)
            .Include(b => b.Menu)
            .Where(b => b.BookingDate == date)
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.Employee!.FullName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Booking>> GetForEmployeeBetweenAsync(int? employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Bookings.AsNoTracking()
            .Include(b => b.Employee)
            .Include(b => b.Menu)
            .Where(b => b.BookingDate >= from && b.BookingDate <= to);

        if (employeeId is not null)
        {
            query = query.Where(b => b.EmployeeId == employeeId.Value);
        }

        return await query
            .OrderBy(b => b.BookingDate)
            .ThenBy(b => b.Employee!.FullName)
            .ToListAsync(ct);
    }

    public async Task<Booking> UpsertAsync(Booking booking, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Bookings.FromSqlInterpolated($"""
            INSERT INTO bookings (employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc)
            VALUES ({booking.EmployeeId}, {booking.BookingDate}, {booking.MenuId}, {booking.PriceSnapshot}, now(), now())
            ON CONFLICT (employee_id, booking_date)
            DO UPDATE SET menu_id = EXCLUDED.menu_id, price_snapshot = EXCLUDED.price_snapshot, updated_at_utc = now()
            RETURNING id, employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc, xmin
            """).AsNoTracking().ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<Booking>> UpsertManyAsync(IReadOnlyList<Booking> bookings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var results = new List<Booking>(bookings.Count);
        foreach (var booking in bookings)
        {
            var rows = await db.Bookings.FromSqlInterpolated($"""
                INSERT INTO bookings (employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc)
                VALUES ({booking.EmployeeId}, {booking.BookingDate}, {booking.MenuId}, {booking.PriceSnapshot}, now(), now())
                ON CONFLICT (employee_id, booking_date)
                DO UPDATE SET menu_id = EXCLUDED.menu_id, price_snapshot = EXCLUDED.price_snapshot, updated_at_utc = now()
                RETURNING id, employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc, xmin
                """).AsNoTracking().ToListAsync(ct);
            results.Add(rows[0]);
        }

        await tx.CommitAsync(ct);
        return results;
    }

    public async Task DeleteAsync(int employeeId, DateOnly bookingDate, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Bookings
            .Where(b => b.EmployeeId == employeeId && b.BookingDate == bookingDate)
            .ExecuteDeleteAsync(ct);
    }
}
