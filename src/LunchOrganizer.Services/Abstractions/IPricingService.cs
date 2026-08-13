using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IPricingService
{
    Task<decimal> GetEffectivePriceAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<DailyPriceDto>> GetPricesForWeekAsync(DateOnly monday, CancellationToken ct = default);
    Task<OperationResult> SetPriceAsync(DateOnly date, decimal price, CancellationToken ct = default);
    Task<OperationResult> SetPriceForWeekAsync(DateOnly monday, decimal price, CancellationToken ct = default);
}
