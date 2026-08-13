using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t =>
            t.HasCheckConstraint("ck_bookings_price_snapshot_non_negative", "price_snapshot >= 0"));

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(b => b.EmployeeId)
            .HasColumnName("employee_id")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(b => b.BookingDate)
            .HasColumnName("booking_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(b => b.MenuId)
            .HasColumnName("menu_id")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(b => b.PriceSnapshot)
            .HasColumnName("price_snapshot")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(b => b.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(b => b.Version)
            .IsRowVersion();

        builder.HasIndex(b => new { b.EmployeeId, b.BookingDate })
            .IsUnique()
            .HasDatabaseName("ix_bookings_employee_date");

        builder.HasIndex(b => b.BookingDate)
            .HasDatabaseName("ix_bookings_booking_date");

        builder.HasOne(b => b.Employee)
            .WithMany(e => e.Bookings)
            .HasForeignKey(b => b.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Menu)
            .WithMany(m => m.Bookings)
            .HasForeignKey(b => new { b.MenuId, b.BookingDate })
            .HasPrincipalKey(m => new { m.Id, m.MenuDate })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
