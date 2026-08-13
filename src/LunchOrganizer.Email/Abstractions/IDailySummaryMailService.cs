using LunchOrganizer.Email;

namespace LunchOrganizer.Email.Abstractions;

public interface IDailySummaryMailService
{
    Task<DailySummaryRunResult> RunAsync(DateOnly date, bool dryRun, CancellationToken ct = default);
}
