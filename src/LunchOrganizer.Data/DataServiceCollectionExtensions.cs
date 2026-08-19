using LunchOrganizer.Data.Abstractions;
using LunchOrganizer.Data.Repositories;
using LunchOrganizer.Data.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LunchOrganizer.Data;

/// <summary>
/// Registers the EF Core / SQL Server-backed repositories and the database bootstrapper with the DI container.
/// </summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddLunchOrganizerData(this IServiceCollection services)
    {
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IDailyPriceRepository, DailyPriceRepository>();
        services.AddScoped<IEmailLogRepository, EmailLogRepository>();

        services.AddScoped<IDatabaseBootstrapper, DatabaseBootstrapper>();

        return services;
    }
}
