using LunchOrganizer.Domain.Time;

namespace LunchOrganizer.Tests.Email;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    public DateTime LocalNow { get; set; } = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Local);
    public DateOnly Today { get; set; } = new DateOnly(2026, 8, 17);
    public TimeOnly LocalTimeOfDay { get; set; } = new TimeOnly(8, 0);
}
