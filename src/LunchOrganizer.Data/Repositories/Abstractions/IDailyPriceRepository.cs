using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Data.Repositories.Abstractions;

/// <summary>
/// Repository abstraction for querying and persisting <see cref="DailyPrice"/> entities.
/// </summary>
public interface IDailyPriceRepository
{
    /// <summary>Retrieves the daily price for a given date, or null if not set.</summary>
    Task<DailyPrice?> GetAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Retrieves all daily prices within the given date range, inclusive.</summary>
    Task<IReadOnlyList<DailyPrice>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Atomic upsert on the price_date primary key; last writer wins. Existing bookings' price_snapshot
    /// values are never touched by this call (see implementation plan §11.6).
    /// </summary>
    Task<DailyPrice> UpsertAsync(DailyPrice price, CancellationToken ct = default);
}
