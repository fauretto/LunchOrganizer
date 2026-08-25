using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / SQL Server-backed implementation of <see cref="IBookingRepository"/>.
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
        // WITH (HOLDLOCK) is mandatory, not optional: a bare MERGE only takes an update lock on rows
        // it matches, so two concurrent MERGEs against a key that does not yet exist can both fall
        // through to WHEN NOT MATCHED and one gets a primary-key violation. HOLDLOCK (= SERIALIZABLE)
        // makes the server take a range lock on the key, which is what makes this genuinely
        // equivalent to PostgreSQL's upsert (INSERT ... DO UPDATE) guarantee.
        var rows = await db.Bookings.FromSqlInterpolated($"""
            MERGE bookings WITH (HOLDLOCK) AS t
            USING (VALUES ({booking.EmployeeId}, {booking.BookingDate}, {booking.MenuId}, {booking.PriceSnapshot},
                           CAST({booking.UserName} AS nvarchar(256)),
                           CAST({booking.UserFullName} AS nvarchar(256)),
                           CAST({booking.UserEmail} AS nvarchar(320))))
                  AS s (employee_id, booking_date, menu_id, price_snapshot, user_name, user_fullname, user_email)
                ON t.employee_id = s.employee_id AND t.booking_date = s.booking_date
            WHEN MATCHED THEN
                UPDATE SET menu_id        = s.menu_id,
                           price_snapshot = s.price_snapshot,
                           user_name      = s.user_name,
                           user_fullname  = s.user_fullname,
                           user_email     = s.user_email,
                           updated_at_utc = CAST(SYSUTCDATETIME() AS datetimeoffset),
                           version        = t.version + 1
            WHEN NOT MATCHED THEN
                INSERT (employee_id, booking_date, menu_id, price_snapshot, user_name, user_fullname, user_email, created_at_utc, updated_at_utc, version)
                VALUES (s.employee_id, s.booking_date, s.menu_id, s.price_snapshot, s.user_name, s.user_fullname, s.user_email,
                        CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1)
            OUTPUT inserted.id, inserted.employee_id, inserted.booking_date, inserted.menu_id,
                   inserted.price_snapshot, inserted.created_at_utc, inserted.updated_at_utc, inserted.version,
                   inserted.user_name, inserted.user_fullname, inserted.user_email;
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
            // WITH (HOLDLOCK) is mandatory, not optional: a bare MERGE only takes an update lock on
            // rows it matches, so two concurrent MERGEs against a key that does not yet exist can both
            // fall through to WHEN NOT MATCHED and one gets a primary-key violation. HOLDLOCK
            // (= SERIALIZABLE) makes the server take a range lock on the key, which is what makes this
            // genuinely equivalent to PostgreSQL's upsert (INSERT ... DO UPDATE) guarantee.
            var rows = await db.Bookings.FromSqlInterpolated($"""
                MERGE bookings WITH (HOLDLOCK) AS t
                USING (VALUES ({booking.EmployeeId}, {booking.BookingDate}, {booking.MenuId}, {booking.PriceSnapshot},
                               CAST({booking.UserName} AS nvarchar(256)),
                               CAST({booking.UserFullName} AS nvarchar(256)),
                               CAST({booking.UserEmail} AS nvarchar(320))))
                      AS s (employee_id, booking_date, menu_id, price_snapshot, user_name, user_fullname, user_email)
                    ON t.employee_id = s.employee_id AND t.booking_date = s.booking_date
                WHEN MATCHED THEN
                    UPDATE SET menu_id        = s.menu_id,
                               price_snapshot = s.price_snapshot,
                               user_name      = s.user_name,
                               user_fullname  = s.user_fullname,
                               user_email     = s.user_email,
                               updated_at_utc = CAST(SYSUTCDATETIME() AS datetimeoffset),
                               version        = t.version + 1
                WHEN NOT MATCHED THEN
                    INSERT (employee_id, booking_date, menu_id, price_snapshot, user_name, user_fullname, user_email, created_at_utc, updated_at_utc, version)
                    VALUES (s.employee_id, s.booking_date, s.menu_id, s.price_snapshot, s.user_name, s.user_fullname, s.user_email,
                            CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1)
                OUTPUT inserted.id, inserted.employee_id, inserted.booking_date, inserted.menu_id,
                       inserted.price_snapshot, inserted.created_at_utc, inserted.updated_at_utc, inserted.version,
                       inserted.user_name, inserted.user_fullname, inserted.user_email;
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
