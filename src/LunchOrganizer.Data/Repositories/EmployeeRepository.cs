using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / Npgsql-backed implementation of <see cref="IEmployeeRepository"/>.
/// </summary>
public sealed class EmployeeRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> SearchByNameAsync(string fragment, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        try
        {
            return await db.Employees
                .FromSqlInterpolated($"""
                    SELECT id, full_name, email, is_active, created_at_utc, updated_at_utc, xmin
                    FROM employees
                    WHERE unaccent(full_name) ILIKE unaccent('%' || {fragment} || '%')
                    ORDER BY full_name
                    """)
                .AsNoTracking()
                .Take(take)
                .ToListAsync(ct);
        }
        catch (PostgresException pg) when (pg.SqlState == "42883")
        {
            // unaccent() extension is not installed on this database — fall back to a plain ILIKE search.
            return await db.Employees
                .FromSqlInterpolated($"""
                    SELECT id, full_name, email, is_active, created_at_utc, updated_at_utc, xmin
                    FROM employees
                    WHERE full_name ILIKE '%' || {fragment} || '%'
                    ORDER BY full_name
                    """)
                .AsNoTracking()
                .Take(take)
                .ToListAsync(ct);
        }
    }

    public async Task<Employee?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Employee?> GetByNameAsync(string fullName, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.FullName == fullName, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetAllAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Employees.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        return await query.OrderBy(e => e.FullName).ToListAsync(ct);
    }

    public async Task<Employee> AddAsync(Employee employee, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO employees (full_name, email, is_active, created_at_utc, updated_at_utc)
            VALUES ({employee.FullName}, {employee.Email}, {employee.IsActive}, now(), now())
            ON CONFLICT (full_name) DO NOTHING
            """, ct);

        return await db.Employees
            .FromSqlInterpolated($"""
                SELECT id, full_name, email, is_active, created_at_utc, updated_at_utc, xmin
                FROM employees
                WHERE full_name = {employee.FullName}
                """)
            .AsNoTracking()
            .SingleAsync(ct);
    }

    public async Task UpdateAsync(Employee employee, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Employees.Attach(employee);
        db.Entry(employee).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Load the tracked entity so its concurrency token (the PostgreSQL xmin system column,
        // mapped via IsRowVersion) is populated. Removing a bare stub would send xmin = 0 in the
        // DELETE's WHERE clause, match no rows, and raise DbUpdateConcurrencyException while leaving
        // the employee undeleted.
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
        {
            return;
        }

        db.Employees.Remove(employee);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23503")
        {
            throw new DeleteRestrictedException("Employee cannot be deleted: referenced by existing bookings.", ex);
        }
    }

    public async Task<bool> HasAnyBookingAsync(int employeeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking().AnyAsync(b => b.EmployeeId == employeeId, ct);
    }
}
