using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Repositories;

/// <summary>
/// EF Core / SQL Server-backed implementation of <see cref="IDailyPriceRepository"/>.
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
        // WITH (HOLDLOCK) is mandatory, not optional — see BookingRepository.UpsertAsync for why.
        var rows = await db.DailyPrices.FromSqlInterpolated($"""
            MERGE daily_prices WITH (HOLDLOCK) AS t
            USING (VALUES ({price.PriceDate}, {price.Price})) AS s (price_date, price)
                ON t.price_date = s.price_date
            WHEN MATCHED THEN
                UPDATE SET price = s.price,
                           updated_at_utc = CAST(SYSUTCDATETIME() AS datetimeoffset),
                           version = t.version + 1
            WHEN NOT MATCHED THEN
                INSERT (price_date, price, created_at_utc, updated_at_utc, version)
                VALUES (s.price_date, s.price, CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1)
            OUTPUT inserted.price_date, inserted.price, inserted.created_at_utc, inserted.updated_at_utc, inserted.version;
            """).AsNoTracking().ToListAsync(ct);
        return rows[0];
    }
}
