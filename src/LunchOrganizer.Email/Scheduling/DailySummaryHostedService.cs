using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Enums;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LunchOrganizer.Tests")]

namespace LunchOrganizer.Email.Scheduling;

/// <summary>
/// Background service that runs the daily summary mail service once per configured working day, at
/// <see cref="EmailOptions.SendTimeLocal"/>. Self-gates on <see cref="EmailOptions.EnableInAppScheduler"/>
/// so it is always safe to register. Each run is executed in its own freshly created DI scope, to
/// avoid captive dependencies / long-lived EF Core contexts.
/// </summary>
public sealed class DailySummaryHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailOptions> emailOptions,
    IClock clock,
    ILogger<DailySummaryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!emailOptions.CurrentValue.EnableInAppScheduler)
        {
            logger.LogInformation("The in-app daily summary scheduler is disabled; DailySummaryHostedService will not run.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRun(emailOptions.CurrentValue, clock);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Resolve a fresh scope (and thus a fresh mail service / EF Core context) for each run,
                // rather than holding one open for the lifetime of this singleton background service —
                // a long-lived scope would keep an EF Core context (and its pooled connection) open for
                // a day at a time.
                using var scope = scopeFactory.CreateScope();
                var mailService = scope.ServiceProvider.GetRequiredService<IDailySummaryMailService>();
                var result = await mailService.RunAsync(clock.Today, dryRun: false, stoppingToken);

                switch (result.Status)
                {
                    case EmailSendStatus.Sent:
                    case EmailSendStatus.Skipped when result.SuggestedExitCode != 3:
                        logger.LogInformation("Scheduled daily summary run completed: {Message}", result.Message);
                        break;
                    case EmailSendStatus.Skipped:
                        logger.LogWarning("Scheduled daily summary run was already handled: {Message}", result.Message);
                        break;
                    default:
                        logger.LogError("Scheduled daily summary run failed: {Message}", result.Message);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception while running the scheduled daily summary mail service.");
            }
        }
    }

    /// <summary>
    /// Computes the delay until the next scheduled run by re-deriving "now" and the next candidate run
    /// time from <see cref="IClock.LocalNow"/> on every call, rather than assuming a fixed 24h period,
    /// so the schedule does not drift across daylight-saving-time transitions.
    /// </summary>
    internal static TimeSpan ComputeDelayUntilNextRun(EmailOptions options, IClock clock)
    {
        var now = clock.LocalNow;
        var candidate = now.Date.Add(options.SendTimeLocal.ToTimeSpan());
        if (candidate <= now)
        {
            candidate = candidate.AddDays(1);
        }

        while (!options.WorkingDays.Contains(candidate.DayOfWeek))
        {
            candidate = candidate.AddDays(1);
        }

        var delay = candidate - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }
}
