using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// In-memory, dictionary-backed fake of <see cref="IDailyPriceRepository"/> for unit tests, keyed on
/// <see cref="DailyPrice.PriceDate"/> exactly like the real Postgres primary key.
/// </summary>
public sealed class FakeDailyPriceRepository : IDailyPriceRepository
{
    private readonly Dictionary<DateOnly, DailyPrice> _prices = new();

    public Task<DailyPrice?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        _prices.TryGetValue(date, out var price);
        return Task.FromResult(price);
    }

    public Task<IReadOnlyList<DailyPrice>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        IReadOnlyList<DailyPrice> results = _prices.Values
            .Where(p => p.PriceDate >= from && p.PriceDate <= to)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<DailyPrice> UpsertAsync(DailyPrice price, CancellationToken ct = default)
    {
        _prices[price.PriceDate] = price;
        return Task.FromResult(price);
    }
}
