namespace LunchOrganizer.Domain.Configuration;

public enum EmailDeliveryMode
{
    PickupDirectory,
    Smtp
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDeliveryMode Mode { get; set; } = EmailDeliveryMode.PickupDirectory;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 25;
    public bool UseStartTls { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = new();
    public string SubjectPrefix { get; set; } = "COHU booked lunch for ";
    public string SubjectDateFormat { get; set; } = "dd.MM.yyyy";

    /// <summary>
    /// Language of the daily summary email body, independent of the website's language toggle,
    /// because the recipient is the kitchen/caterer.
    /// </summary>
    public string Language { get; set; } = "fr";
    public TimeOnly SendTimeLocal { get; set; } = new TimeOnly(9, 1);
    public List<DayOfWeek> WorkingDays { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    };
    public bool SkipWhenNoBookings { get; set; } = true;
    public string PickupDirectory { get; set; } = "./mail-drop";
    public bool EnableInAppScheduler { get; set; } = false;
}
