using FluentAssertions;
using LunchOrganizer.Email.Scheduling;

namespace LunchOrganizer.Tests.Email;

public class DailySummaryHostedServiceSchedulingTests
{
    [Fact]
    public void ComputeDelayUntilNextRun_BeforeSendTimeOnWorkingDay_ReturnsDelayLaterToday()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock { LocalNow = new DateTime(2026, 8, 17, 8, 0, 0) }; // Monday

        var delay = DailySummaryHostedService.ComputeDelayUntilNextRun(options, clock);

        delay.Should().Be(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ComputeDelayUntilNextRun_AfterSendTimeOnWorkingDay_ReturnsDelayToNextWorkingDay()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock { LocalNow = new DateTime(2026, 8, 17, 10, 0, 0) }; // Monday, after 09:01

        var delay = DailySummaryHostedService.ComputeDelayUntilNextRun(options, clock);

        clock.LocalNow.Add(delay).Should().Be(new DateTime(2026, 8, 18, 9, 1, 0));
    }

    [Fact]
    public void ComputeDelayUntilNextRun_OnFridayAfterSendTime_SkipsWeekendToMonday()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock { LocalNow = new DateTime(2026, 8, 14, 10, 0, 0) }; // Friday, after send time

        var delay = DailySummaryHostedService.ComputeDelayUntilNextRun(options, clock);

        clock.LocalNow.Add(delay).Should().Be(new DateTime(2026, 8, 17, 9, 1, 0));
    }

    [Fact]
    public void ComputeDelayUntilNextRun_NeverReturnsNegative()
    {
        var options = TestData.DefaultOptions();
        var clock = new FakeClock { LocalNow = new DateTime(2026, 8, 14, 10, 0, 0) }; // Friday, after send time

        var delay = DailySummaryHostedService.ComputeDelayUntilNextRun(options, clock);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
