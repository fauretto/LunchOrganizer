using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / SQL Server-backed implementation of <see cref="IMenuRepository"/>.
/// </summary>
public sealed class MenuRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IMenuRepository
{
    public async Task<IReadOnlyList<Menu>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Menus.AsNoTracking()
            .Where(m => m.MenuDate == date)
            .OrderBy(m => m.MenuNumber)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Menu>> GetByWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var friday = monday.AddDays(4);
        return await db.Menus.AsNoTracking()
            .Where(m => m.MenuDate >= monday && m.MenuDate <= friday)
            .OrderBy(m => m.MenuDate)
            .ThenBy(m => m.MenuNumber)
            .ToListAsync(ct);
    }

    public async Task<Menu?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Menus.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<int> GetNextMenuNumberAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await GetNextMenuNumberAsync(db, date, ct);
    }

    private static async Task<int> GetNextMenuNumberAsync(LunchOrganizerDbContext db, DateOnly date, CancellationToken ct)
    {
        return 1 + (await db.Menus.AsNoTracking()
            .Where(m => m.MenuDate == date)
            .Select(m => (int?)m.MenuNumber)
            .MaxAsync(ct) ?? 0);
    }

    public async Task<Menu> AddAsync(Menu menu, CancellationToken ct = default)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= BusinessRules.MaxInsertRetryAttempts; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);

            if (attempt > 1)
            {
                menu.MenuNumber = await GetNextMenuNumberAsync(db, menu.MenuDate, ct);
            }

            db.Menus.Add(menu);

            try
            {
                await db.SaveChangesAsync(ct);

                return await db.Menus.AsNoTracking().SingleAsync(m => m.Id == menu.Id, ct);
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
            {
                lastException = ex;
            }
        }

        throw lastException!;
    }

    public async Task ImportAsync(IReadOnlyList<Menu> menus, CancellationToken ct = default)
    {
        if (menus.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        // The explicit transaction exists to make the duplicate READ and the INSERT one unit. A bare
        // SaveChanges is already atomic on its own, so this transaction is about the check, not the write.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var min = menus.Min(m => m.MenuDate);
        var max = menus.Max(m => m.MenuDate);
        var importDates = menus.Select(m => m.MenuDate).ToHashSet();

        // A single indexed range scan on ix_menus_menu_date, then an in-memory filter against the
        // (small) set of imported dates — deliberately avoiding a large IN (...) list of dates.
        var existingDates = await db.Menus.AsNoTracking()
            .Where(m => m.MenuDate >= min && m.MenuDate <= max)
            .Select(m => m.MenuDate)
            .Distinct()
            .ToListAsync(ct);

        var conflicts = existingDates.Where(importDates.Contains).OrderBy(d => d).ToList();

        if (conflicts.Count > 0)
        {
            // Conflict rule (plan decision D3): ANY existing menu on a date being imported is a conflict,
            // not merely a matching (menu_date, menu_number) pair. Key-level matching would let a
            // partially-populated day silently gain the missing menu numbers, producing exactly the mixed
            // state this feature must prevent.
            await tx.RollbackAsync(ct);
            throw new MenuImportConflictException(conflicts);
        }

        try
        {
            // Leave CreatedAtUtc/UpdatedAtUtc to the column defaults and Version to
            // LunchOrganizerDbContext.ApplyVersionMaintenance (which sets 1 on insert) — exactly as
            // AddAsync does today.
            db.Menus.AddRange(menus);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))
        {
            // A concurrent importer won the race between the read above and this insert. The losing side
            // cannot cheaply know which specific dates collided, so it reports the whole batch's distinct
            // dates as the conflict set.
            await tx.RollbackAsync(ct);
            throw new MenuImportConflictException(menus.Select(m => m.MenuDate).Distinct().OrderBy(d => d).ToList());
        }
        // Any other exception propagates as-is: `await using` on the transaction rolls it back on
        // dispose. Do not add a redundant catch/rollback/rethrow here for the general case.
    }

    public async Task UpdateAsync(Menu menu, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Menus.Attach(menu);
        db.Entry(menu).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        // Load the tracked entity so its concurrency token (the app-managed `version` column) is
        // populated. Removing a bare stub would send version = 0 in the DELETE's WHERE clause, match
        // no rows, and raise DbUpdateConcurrencyException while leaving the menu undeleted.
        var menu = await db.Menus.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (menu is null)
        {
            return;
        }

        db.Menus.Remove(menu);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsForeignKeyViolation(ex))
        {
            throw new DeleteRestrictedException("Menu cannot be deleted: referenced by existing bookings.", ex);
        }
    }

    public async Task<bool> HasBookingsAsync(int menuId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking().AnyAsync(b => b.MenuId == menuId, ct);
    }
}
