using LunchOrganizer.Domain.Time;
using LunchOrganizer.Email.Abstractions;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Fakes.Services;
using LunchOrganizer.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LunchOrganizer.Fakes;

public static class FakeServiceCollectionExtensions
{
    public static IServiceCollection AddLunchOrganizerFakes(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<FakeDataStore>();

        // Registered as its own concrete singleton (in addition to IEmailSender) so callers/tests can
        // resolve FakeEmailSender directly and inspect SentMessages while still sharing the same instance.
        services.AddSingleton<FakeEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());

        services.AddScoped<IWeekService, FakeWeekService>();
        services.AddScoped<IEmployeeService, FakeEmployeeService>();
        services.AddScoped<IPricingService, FakePricingService>();
        services.AddScoped<IMenuService, FakeMenuService>();
        services.AddScoped<IBookingService, FakeBookingService>();
        services.AddScoped<IReportService, FakeReportService>();
        services.AddScoped<IDailySummaryBuilder, FakeDailySummaryBuilder>();

        return services;
    }
}
