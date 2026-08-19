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
/// syntactically valid SQL Server instance so EF Core tooling can generate migrations offline; the
/// database referenced here does not need to exist.
///
/// <c>Encrypt=False</c> is required here because LocalDB does not support encryption. This has no
/// bearing on production, which uses `config/database.json`, where `Encrypt` is `true`.
/// </summary>
public class LunchOrganizerDbContextFactory : IDesignTimeDbContextFactory<LunchOrganizerDbContext>
{
    public LunchOrganizerDbContext CreateDbContext(string[] args)
    {
        const string designTimeConnectionString =
            "Server=(localdb)\\MSSQLLocalDB;Database=lunchorganizer;Integrated Security=True;Encrypt=False;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<LunchOrganizerDbContext>();
        optionsBuilder.UseSqlServer(designTimeConnectionString);

        return new LunchOrganizerDbContext(optionsBuilder.Options);
    }
}
