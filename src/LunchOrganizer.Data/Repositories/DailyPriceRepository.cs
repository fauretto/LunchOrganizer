using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / Npgsql-backed implementation of <see cref="IDailyPriceRepository"/>.
/// </summary>
public sealed class DailyPriceRepository(IDbContextFactory<LunchOrganizerDbContext> factory) : IDailyPriceRepository
{
    public async Task<DailyPrice?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DailyPrices.AsNoTracking().SingleOrDefaultAsync(p => p.PriceDate == date, ct);
    }

    public async Task<IReadOnlyList<DailyPrice>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DailyPrices.AsNoTracking()
            .Where(p => p.PriceDate >= from && p.PriceDate <= to)
            .OrderBy(p => p.PriceDate)
            .ToListAsync(ct);
    }

    public async Task<DailyPrice> UpsertAsync(DailyPrice price, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.DailyPrices.FromSqlInterpolated($"""
            INSERT INTO daily_prices (price_date, price, created_at_utc, updated_at_utc)
            VALUES ({price.PriceDate}, {price.Price}, now(), now())
            ON CONFLICT (price_date) DO UPDATE SET price = EXCLUDED.price, updated_at_utc = now()
            RETURNING price_date, price, created_at_utc, updated_at_utc, xmin
            """).AsNoTracking().ToListAsync(ct);
        return rows[0];
    }
}
