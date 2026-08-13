using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus", t =>
            t.HasCheckConstraint("ck_menus_menu_number_positive", "menu_number >= 1"));

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(m => m.MenuDate)
            .HasColumnName("menu_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(m => m.MenuNumber)
            .HasColumnName("menu_number")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(m => m.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(m => m.Version)
            .IsRowVersion();

        builder.HasIndex(m => new { m.MenuDate, m.MenuNumber })
            .IsUnique()
            .HasDatabaseName("ix_menus_menu_date_menu_number");

        builder.HasAlternateKey(m => new { m.Id, m.MenuDate });

        builder.HasIndex(m => m.MenuDate)
            .HasDatabaseName("ix_menus_menu_date");
    }
}
