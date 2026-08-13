using LunchOrganizer.Domain.Enums;

namespace LunchOrganizer.Domain.Entities;

public class EmailLogEntry
{
    public DateOnly SummaryDate { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
    public EmailSendStatus Status { get; set; }
    public string? Recipients { get; set; }
    public int BookingCount { get; set; }
    public string? ErrorMessage { get; set; }
}
