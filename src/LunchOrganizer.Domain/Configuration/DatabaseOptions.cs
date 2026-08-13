namespace LunchOrganizer.Domain.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "lunchorganizer";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MaintenanceDatabase { get; set; } = "postgres";
    public bool AutoCreateDatabase { get; set; } = true;
    public bool Seed { get; set; } = false;
    public int MaxPoolSize { get; set; } = 50;
    public int CommandTimeoutSeconds { get; set; } = 30;
}
