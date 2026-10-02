using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakePricingService(FakeDataStore store, IClock clock) : IPricingService
{
    public Task<decimal> GetEffectivePriceAsync(DateOnly date, CancellationToken ct = default)
    {
        var price = store.Prices.TryGetValue(date, out var p) ? p.Price : FakeDefaults.DefaultLunchPrice;
        return Task.FromResult(price);
    }

    public Task<decimal> GetEffectivePriceAsync(Menu menu, CancellationToken ct = default)
    {
        if (menu.Price is { } menuPrice)
        {
            return Task.FromResult(menuPrice);
        }

        return GetEffectivePriceAsync(menu.MenuDate, ct);
    }

    public Task<IReadOnlyList<DailyPriceDto>> GetPricesForWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        var result = DateHelpers.WorkingDays(monday)
            .Select(d => new DailyPriceDto(d, store.Prices.TryGetValue(d, out var p) ? p.Price : FakeDefaults.DefaultLunchPrice))
            .ToList();

        return Task.FromResult<IReadOnlyList<DailyPriceDto>>(result);
    }

    public Task<OperationResult> SetPriceAsync(DateOnly date, decimal price, CancellationToken ct = default)
    {
        if (price < 0)
        {
            return Task.FromResult(OperationResult.Fail("Price cannot be negative."));
        }

        UpsertPrice(date, price);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> SetPriceForWeekAsync(DateOnly monday, decimal price, CancellationToken ct = default)
    {
        if (price < 0)
        {
            return Task.FromResult(OperationResult.Fail("Price cannot be negative."));
        }

        foreach (var day in DateHelpers.WorkingDays(monday))
        {
            UpsertPrice(day, price);
        }

        return Task.FromResult(OperationResult.Ok());
    }

    private void UpsertPrice(DateOnly date, decimal price)
    {
        var now = clock.UtcNow;
        store.Prices.AddOrUpdate(
            date,
            _ => new DailyPrice { PriceDate = date, Price = price, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 },
            (_, existing) =>
            {
                existing.Price = price;
                existing.UpdatedAtUtc = now;
                existing.Version++;
                return existing;
            });
    }
}
