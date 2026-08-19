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

    [Fact]
    public async Task BuildAsync_PopulatesEmployeeBookings_OneRowPerBookingWithEmailMenuAndFrozenPrice()
    {
        var repo = new InMemoryBookingRepository();
        repo.Seed(new[]
        {
            TestData.Booking(1, "Zoé Martin", Date, menuId: 10, menuNumber: 1, menuDescription: "Menu one", price: 11.00m, employeeEmail: "zoe@example.com"),
            TestData.Booking(2, "Amélie Dupont", Date, menuId: 20, menuNumber: 2, menuDescription: "Menu two", price: 14.50m, employeeEmail: null),
        });

        var builder = new DailySummaryBuilder(repo, new TestOptionsMonitor<Domain.Configuration.EmailOptions>(TestData.DefaultOptions()), new FakeClock());

        var result = await builder.BuildAsync(Date);

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeBookings.Should().NotBeNull();
        // Ordered by employee name with the same culture-aware comparer as the menu grouping.
        result.Value!.EmployeeBookings!.Select(b => b.FullName).Should().ContainInOrder("Amélie Dupont", "Zoé Martin");

        var zoe = result.Value!.EmployeeBookings!.Single(b => b.EmployeeId == 1);
        zoe.Email.Should().Be("zoe@example.com");
        zoe.MenuNumber.Should().Be(1);
        zoe.MenuDescription.Should().Be("Menu one");
        zoe.PriceSnapshot.Should().Be(11.00m);

        var amelie = result.Value!.EmployeeBookings!.Single(b => b.EmployeeId == 2);
        amelie.Email.Should().BeNull();
        amelie.MenuNumber.Should().Be(2);
        amelie.PriceSnapshot.Should().Be(14.50m);
    }
}
