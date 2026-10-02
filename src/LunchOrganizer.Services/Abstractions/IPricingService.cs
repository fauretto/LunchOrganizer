using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IPricingService
{
    Task<decimal> GetEffectivePriceAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Effective price for a specific menu: <paramref name="menu"/>'s own <see cref="Menu.Price"/> override if set, otherwise the day's price (see the date-based overload).</summary>
    Task<decimal> GetEffectivePriceAsync(Menu menu, CancellationToken ct = default);
    Task<IReadOnlyList<DailyPriceDto>> GetPricesForWeekAsync(DateOnly monday, CancellationToken ct = default);
    Task<OperationResult> SetPriceAsync(DateOnly date, decimal price, CancellationToken ct = default);
    Task<OperationResult> SetPriceForWeekAsync(DateOnly monday, decimal price, CancellationToken ct = default);
}
