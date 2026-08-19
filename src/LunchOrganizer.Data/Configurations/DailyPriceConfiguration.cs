using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class DailyPriceConfiguration : IEntityTypeConfiguration<DailyPrice>
{
    public void Configure(EntityTypeBuilder<DailyPrice> builder)
    {
        builder.ToTable("daily_prices", t =>
            t.HasCheckConstraint("ck_daily_prices_price_non_negative", "price >= 0"));

        builder.HasKey(d => d.PriceDate);

        builder.Property(d => d.PriceDate)
            .HasColumnName("price_date")
            .HasColumnType("date");

        builder.Property(d => d.Price)
            .HasColumnName("price")
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        builder.Property(d => d.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        // PostgreSQL's xmin was server-maintained; SQL Server has no equivalent, so the concurrency
        // token becomes an app-managed bigint (see LunchOrganizerDbContext.ApplyVersionMaintenance).
        builder.Property(d => d.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .IsConcurrencyToken()
            .HasDefaultValueSql("1");
    }
}
