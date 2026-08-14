using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / Npgsql-backed implementation of <see cref="IMenuRepository"/>.
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
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                lastException = ex;
            }
        }

        throw lastException!;
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

        // Load the tracked entity so its concurrency token (the PostgreSQL xmin system column,
        // mapped via IsRowVersion) is populated. Removing a bare stub would send xmin = 0 in the
        // DELETE's WHERE clause, match no rows, and raise DbUpdateConcurrencyException while leaving
        // the menu undeleted.
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23503")
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
