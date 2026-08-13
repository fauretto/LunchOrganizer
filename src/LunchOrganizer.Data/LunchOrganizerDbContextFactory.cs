using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LunchOrganizer.Data;

/// <summary>
/// Design-time-only factory used exclusively by EF Core tooling (`dotnet ef migrations add`,
/// `dotnet ef migrations script`, etc.) to construct a <see cref="LunchOrganizerDbContext"/>
/// without needing to run the Web host or read `config/database.json`.
///
/// This connection string is NEVER used at runtime — the running application always builds its
/// connection string from <see cref="LunchOrganizer.Domain.Configuration.DatabaseOptions"/> via
/// <see cref="DatabaseOptionsExtensions.BuildConnectionString"/>. It only needs to point at a
/// syntactically valid Postgres server so EF Core tooling can generate migrations offline; the
/// database referenced here does not need to exist.
/// </summary>
public class LunchOrganizerDbContextFactory : IDesignTimeDbContextFactory<LunchOrganizerDbContext>
{
    public LunchOrganizerDbContext CreateDbContext(string[] args)
    {
        const string designTimeConnectionString =
            "Host=localhost;Port=5432;Database=lunchorganizer;Username=postgres;Password=changeme";

        var optionsBuilder = new DbContextOptionsBuilder<LunchOrganizerDbContext>();
        optionsBuilder.UseNpgsql(designTimeConnectionString);

        return new LunchOrganizerDbContext(optionsBuilder.Options);
    }
}
