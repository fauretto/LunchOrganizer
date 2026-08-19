using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data;

/// <summary>
/// Standing caveat: any future hand-written raw <c>UPDATE</c> against <c>employees</c>, <c>menus</c>,
/// <c>bookings</c> or <c>daily_prices</c> must increment <c>version</c> itself. Those tables' optimistic
/// concurrency check is entirely application-managed (see <see cref="ApplyVersionMaintenance"/>) — there
/// is no server-side trigger or computed column backing it, unlike PostgreSQL's <c>xmin</c>.
/// </summary>
public class LunchOrganizerDbContext : DbContext
{
    public LunchOrganizerDbContext(DbContextOptions<LunchOrganizerDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<DailyPrice> DailyPrices => Set<DailyPrice>();
    public DbSet<EmailLogEntry> EmailLogEntries => Set<EmailLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LunchOrganizerDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyVersionMaintenance();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyVersionMaintenance();
        return base.SaveChanges();
    }

    /// <summary>
    /// PostgreSQL's <c>xmin</c> system column was server-maintained: every UPDATE bumped it for free.
    /// Its SQL Server replacement — the app-managed <c>version bigint</c> column — is not, so the
    /// application must increment it here, before every save.
    /// <para>
    /// Incrementing <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry.CurrentValue"/>
    /// after the entity has been loaded/attached leaves
    /// <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry.OriginalValue"/> untouched.
    /// That is exactly what makes EF Core's concurrency check work: it emits
    /// <c>WHERE version = &lt;original&gt;</c> and <c>SET version = &lt;original&gt; + 1</c>, so a
    /// concurrent writer that already advanced the row causes the predicate to match zero rows and EF
    /// to throw <see cref="DbUpdateConcurrencyException"/>.
    /// </para>
    /// </summary>
    private void ApplyVersionMaintenance()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            var versionProperty = entry.Metadata.FindProperty("Version");
            if (versionProperty is null)
            {
                // EmailLogEntry has no Version property — written once per day, never concurrently edited.
                continue;
            }

            var property = entry.Property("Version");
            switch (entry.State)
            {
                case EntityState.Added:
                    if (property.CurrentValue is uint currentAdded && currentAdded == 0)
                    {
                        property.CurrentValue = 1u;
                    }
                    break;
                case EntityState.Modified:
                    property.CurrentValue = unchecked((uint)property.CurrentValue! + 1);
                    break;
            }
        }
    }
}
