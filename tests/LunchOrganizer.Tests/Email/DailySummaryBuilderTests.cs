using FluentAssertions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Email.InMemory;
using LunchOrganizer.Email.Services;

namespace LunchOrganizer.Tests.Email;

public class DailySummaryBuilderTests
{
    private static readonly DateOnly Date = new(2026, 8, 17);

    [Fact]
    public async Task BuildAsync_GroupsBookingsByMenuInMenuNumberOrder()
    {
        var repo = new InMemoryBookingRepository();
        repo.Seed(new[]
        {
            TestData.Booking(1, "Employee One", Date, menuId: 30, menuNumber: 3, menuDescription: "Menu three"),
            TestData.Booking(2, "Employee Two", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(3, "Employee Three", Date, menuId: 20, menuNumber: 2, menuDescription: "Menu two"),
        });

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MenuGroups.Select(g => g.MenuNumber).Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task BuildAsync_OrdersEmployeeNamesAlphabeticallyWithinEachMenu()
    {
        var repo = new InMemoryBookingRepository();
        repo.Seed(new[]
        {
            TestData.Booking(1, "Zoé Martin", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(2, "Amélie Dupont", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(3, "Bernard Rossi", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
        });

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MenuGroups.Should().HaveCount(1);
        result.Value!.MenuGroups[0].EmployeeNames.Should().ContainInOrder("Amélie Dupont", "Bernard Rossi", "Zoé Martin");
    }

    [Fact]
    public async Task BuildAsync_OmitsMenusWithZeroBookings()
    {
        var repo = new InMemoryBookingRepository();
        repo.Seed(new[]
        {
            TestData.Booking(1, "Employee One", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(2, "Employee Two", Date, menuId: 30, menuNumber: 3, menuDescription: "Menu three"),
        });

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MenuGroups.Should().HaveCount(2);
        result.Value!.MenuGroups.Select(g => g.MenuNumber).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public async Task BuildAsync_ReturnsNoBookingsForDate_WhenDayHasNoBookings()
    {
        var repo = new InMemoryBookingRepository();

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NoBookingsForDate);
    }

    [Fact]
    public async Task BuildAsync_TotalBookingCountMatchesRowCount()
    {
        var repo = new InMemoryBookingRepository();
        var bookings = new[]
        {
            TestData.Booking(1, "Employee One", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(2, "Employee Two", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(3, "Employee Three", Date, menuId: 20, menuNumber: 2, menuDescription: "Menu two"),
        };
        repo.Seed(bookings);

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalBookingCount.Should().Be(bookings.Length);
    }

    [Fact]
    public async Task BuildAsync_OnlyQueriesTheDateRequested()
    {
        var otherDate = Date.AddDays(1);
        var repo = new InMemoryBookingRepository();
        repo.Seed(new[]
        {
            TestData.Booking(1, "Today Employee", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
            TestData.Booking(2, "Other Day Employee", otherDate, menuId: 10, menuNumber: 1, menuDescription: "Menu one"),
        });

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalBookingCount.Should().Be(1);
        result.Value!.MenuGroups.Should().ContainSingle();
        result.Value!.MenuGroups[0].EmployeeNames.Should().ContainSingle().Which.Should().Be("Today Employee");
    }
}
