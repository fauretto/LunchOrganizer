namespace LunchOrganizer.Domain.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTime LocalNow => DateTime.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public TimeOnly LocalTimeOfDay => TimeOnly.FromDateTime(DateTime.Now);
}
