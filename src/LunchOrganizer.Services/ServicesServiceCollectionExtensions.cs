using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Import;
using LunchOrganizer.Services.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LunchOrganizer.Services;

/// <summary>
/// Registers the business services (and the clock they depend on) with the DI container.
/// </summary>
public static class ServicesServiceCollectionExtensions
{
    public static IServiceCollection AddLunchOrganizerServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<IWeekService, WeekService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IMenuDocumentParser, MenuDocumentParser>();
        services.AddScoped<IMenuImportService, MenuImportService>();

        return services;
    }
}
