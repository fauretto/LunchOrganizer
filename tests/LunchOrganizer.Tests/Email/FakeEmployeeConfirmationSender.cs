using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Tests.Email;

/// <summary>
/// Test double for <see cref="IEmployeeConfirmationSender"/> used by
/// <see cref="DailySummaryMailServiceIdempotencyTests"/> to observe how <c>DailySummaryMailService</c>
/// invokes the confirmation step, and to simulate it throwing so the "never fails the run" rule
/// (plan §4 rule 3) can be exercised without going through the real sender.
/// </summary>
internal sealed class FakeEmployeeConfirmationSender : IEmployeeConfirmationSender
{
    private readonly ConfirmationSendOutcome _outcome;
    private readonly Exception? _throws;

    public FakeEmployeeConfirmationSender(ConfirmationSendOutcome? outcome = null, Exception? throws = null)
    {
        _outcome = outcome ?? new ConfirmationSendOutcome(0, 0, 0);
        _throws = throws;
    }

    public List<(DailySummaryDto Summary, bool DryRun)> Calls { get; } = new();

    public Task<ConfirmationSendOutcome> SendAllAsync(DailySummaryDto summary, bool dryRun, CancellationToken ct = default)
    {
        Calls.Add((summary, dryRun));

        if (_throws is not null)
        {
            throw _throws;
        }

        return Task.FromResult(_outcome);
    }
}
