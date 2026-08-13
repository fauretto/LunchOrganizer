using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Email.Abstractions;

public interface IDailySummaryBuilder
{
    Task<OperationResult<DailySummaryDto>> BuildAsync(DateOnly date, CancellationToken ct = default);
}
