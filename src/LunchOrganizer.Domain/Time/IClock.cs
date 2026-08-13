namespace LunchOrganizer.Domain.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTime LocalNow { get; }
    DateOnly Today { get; }
    TimeOnly LocalTimeOfDay { get; }
}
