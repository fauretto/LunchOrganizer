using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LunchOrganizer.Data.Configurations;

public class EmailLogEntryConfiguration : IEntityTypeConfiguration<EmailLogEntry>
{
    public void Configure(EntityTypeBuilder<EmailLogEntry> builder)
    {
        builder.ToTable("email_log");

        builder.HasKey(e => e.SummaryDate);

        builder.Property(e => e.SummaryDate)
            .HasColumnName("summary_date")
            .HasColumnType("date");

        builder.Property(e => e.SentAtUtc)
            .HasColumnName("sent_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.Recipients)
            .HasColumnName("recipients")
            .HasColumnType("text");

        builder.Property(e => e.BookingCount)
            .HasColumnName("booking_count")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");
    }
}
