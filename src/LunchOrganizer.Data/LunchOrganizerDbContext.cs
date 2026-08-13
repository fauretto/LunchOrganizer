using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data;

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

        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LunchOrganizerDbContext).Assembly);
    }
}
