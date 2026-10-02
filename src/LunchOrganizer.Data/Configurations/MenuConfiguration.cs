using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus", t =>
        {
            t.HasCheckConstraint("ck_menus_menu_number_positive", "menu_number >= 1");
            t.HasCheckConstraint("ck_menus_price_non_negative", "price IS NULL OR price >= 0");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityColumn();

        builder.Property(m => m.MenuDate)
            .HasColumnName("menu_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(m => m.MenuNumber)
            .HasColumnName("menu_number")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(m => m.Description)
            .HasColumnName("description")
            .HasColumnType("nvarchar(max)");

        builder.Property(m => m.Price)
            .HasColumnName("price")
            .HasColumnType("decimal(10,2)");

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        // PostgreSQL's xmin was server-maintained; SQL Server has no equivalent, so the concurrency
        // token becomes an app-managed bigint (see LunchOrganizerDbContext.ApplyVersionMaintenance).
        builder.Property(m => m.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .IsConcurrencyToken()
            .HasDefaultValueSql("1");

        builder.HasIndex(m => new { m.MenuDate, m.MenuNumber })
            .IsUnique()
            .HasDatabaseName("ix_menus_menu_date_menu_number");

        builder.HasAlternateKey(m => new { m.Id, m.MenuDate });

        builder.HasIndex(m => m.MenuDate)
            .HasDatabaseName("ix_menus_menu_date");
    }
}
