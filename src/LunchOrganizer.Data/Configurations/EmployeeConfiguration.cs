using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityColumn();

        builder.Property(e => e.FullName)
            .HasColumnName("full_name")
            .HasColumnType("nvarchar(200)")
            .UseCollation("Latin1_General_CI_AS")
            .IsRequired();

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasColumnType("nvarchar(max)");

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("bit")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired()
            .HasDefaultValueSql("CAST(SYSUTCDATETIME() AS datetimeoffset)");

        // PostgreSQL's xmin was server-maintained; SQL Server has no equivalent, so the concurrency
        // token becomes an app-managed bigint (see LunchOrganizerDbContext.ApplyVersionMaintenance).
        builder.Property(e => e.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .IsConcurrencyToken()
            .HasDefaultValueSql("1");

        builder.HasIndex(e => e.FullName)
            .IsUnique()
            .HasDatabaseName("ix_employees_full_name");
    }
}
