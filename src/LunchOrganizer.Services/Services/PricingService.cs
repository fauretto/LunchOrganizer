using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Services.Services;

public sealed class PricingService(IDailyPriceRepository repo, IOptionsMonitor<AppOptions> appOptions) : IPricingService
{
    public async Task<decimal> GetEffectivePriceAsync(DateOnly date, CancellationToken ct = default)
    {
        var p = await repo.GetAsync(date, ct);
        return p?.Price ?? appOptions.CurrentValue.DefaultLunchPrice;
    }

    public async Task<decimal> GetEffectivePriceAsync(Menu menu, CancellationToken ct = default)
    {
        if (menu.Price is { } menuPrice)
        {
            return menuPrice;
        }

        return await GetEffectivePriceAsync(menu.MenuDate, ct);
    }

    public async Task<IReadOnlyList<DailyPriceDto>> GetPricesForWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        var defaultPrice = appOptions.CurrentValue.DefaultLunchPrice;
        var days = new[] { monday, monday.AddDays(1), monday.AddDays(2), monday.AddDays(3), monday.AddDays(4) };
        var existing = await repo.GetRangeAsync(monday, monday.AddDays(4), ct);
        var byDate = existing.ToDictionary(p => p.PriceDate, p => p.Price);

        var result = days
            .Select(d => new DailyPriceDto(d, byDate.TryGetValue(d, out var price) ? price : defaultPrice))
            .ToList();

        return result;
    }

    public async Task<OperationResult> SetPriceAsync(DateOnly date, decimal price, CancellationToken ct = default)
    {
        if (price < 0)
        {
            return OperationResult.Fail("Price cannot be negative.", ErrorCodes.PriceInvalid);
        }

        await repo.UpsertAsync(new DailyPrice { PriceDate = date, Price = price }, ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetPriceForWeekAsync(DateOnly monday, decimal price, CancellationToken ct = default)
    {
        if (price < 0)
        {
            return OperationResult.Fail("Price cannot be negative.", ErrorCodes.PriceInvalid);
        }

        var days = new[] { monday, monday.AddDays(1), monday.AddDays(2), monday.AddDays(3), monday.AddDays(4) };
        foreach (var day in days)
        {
            await repo.UpsertAsync(new DailyPrice { PriceDate = day, Price = price }, ct);
        }

        return OperationResult.Ok();
    }
}
