using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LunchOrganizer.Email.Scheduling;

/// <summary>
/// Registers the in-app daily summary scheduler.
/// </summary>
/// <remarks>
/// This must be called by the Web project's own <c>Program.cs</c>, alongside its own registrations for
/// <see cref="LunchOrganizer.Email.Abstractions.IDailySummaryMailService"/>,
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> of
/// <see cref="LunchOrganizer.Domain.Configuration.EmailOptions"/>, and
/// <see cref="LunchOrganizer.Domain.Time.IClock"/>. <see cref="DailySummaryHostedService"/> self-gates on
/// <see cref="LunchOrganizer.Domain.Configuration.EmailOptions.EnableInAppScheduler"/> (default
/// <c>false</c>), so calling <see cref="AddDailySummaryScheduler"/> unconditionally is always safe, even
/// when the in-app scheduler is disabled by configuration.
/// </remarks>
public static class DailySummarySchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddDailySummaryScheduler(this IServiceCollection services)
    {
        services.AddHostedService<DailySummaryHostedService>();
        return services;
    }
}
