using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / SQL Server-backed implementation of <see cref="IEmployeeRepository"/>.
/// </summary>
public sealed class EmployeeRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IEmployeeRepository
{
    public async Task<IReadOnlyList<Employee>> SearchByNameAsync(string fragment, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Escape LIKE metacharacters before wrapping the fragment in wildcards, otherwise a user
        // typing "%" would match every employee. The `[` replacement MUST come first: doing it after
        // the other two would re-escape the literal brackets that "%" and "_" just introduced.
        var escaped = fragment.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        var pattern = $"%{escaped}%";

        // Two different collations are deliberately in play here, mirroring PostgreSQL's citext +
        // accent-folding-function split exactly: the column's own collation (Latin1_General_CI_AS,
        // set in the entity configuration) is case-insensitive but accent-SENSITIVE, so "Chloe
        // Bernard" and "Chloé Bernard" remain two distinct people for the uniqueness check. Search
        // below deliberately overrides to CI_AI (accent-INSENSITIVE) so that typing "chloe" still
        // finds "Chloé Bernard" — SQL Server needs no extension for this, unlike PostgreSQL, so there
        // is exactly one code path here now.
        return await db.Employees
            .FromSqlInterpolated($"""
                SELECT id, full_name, email, is_active, created_at_utc, updated_at_utc, version
                FROM employees
                WHERE full_name COLLATE Latin1_General_CI_AI LIKE {pattern} COLLATE Latin1_General_CI_AI
                """)
            .AsNoTracking()
            // Ordering must happen here, not in the raw SQL: .Take() is a composing operator, so EF
            // Core wraps the raw SQL in a derived table (SELECT TOP(@take) * FROM (<raw sql>) AS e),
            // and SQL Server rejects a bare ORDER BY inside a derived table (error 1033). PostgreSQL
            // allowed it, which is why this worked before the migration.
            .OrderBy(e => e.FullName)
            .Take(take)
            .ToListAsync(ct);
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

        // The existence check compares full_name under the column's own collation
        // (Latin1_General_CI_AS, case-insensitive / accent-sensitive — see SearchByNameAsync for why
        // that split matters), which is what replaces PostgreSQL's citext: "ALICE MARTIN" still
        // matches an existing "Alice Martin" and is correctly treated as a duplicate.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO employees (full_name, email, is_active, created_at_utc, updated_at_utc, version)
            SELECT {employee.FullName}, {employee.Email}, {employee.IsActive},
                   CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1
            WHERE NOT EXISTS (SELECT 1 FROM employees WITH (UPDLOCK, HOLDLOCK) WHERE full_name = {employee.FullName});
            """, ct);

        return await db.Employees
            .FromSqlInterpolated($"""
                SELECT id, full_name, email, is_active, created_at_utc, updated_at_utc, version
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            throw new UniqueConstraintViolationException("Employee cannot be updated: another employee already has this name.", ex);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Load the tracked entity so its concurrency token (the app-managed `version` column) is
        // populated. Removing a bare stub would send version = 0 in the DELETE's WHERE clause, match
        // no rows, and raise DbUpdateConcurrencyException while leaving the employee undeleted.
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
        catch (DbUpdateException ex) when (SqlServerErrors.IsForeignKeyViolation(ex))
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
