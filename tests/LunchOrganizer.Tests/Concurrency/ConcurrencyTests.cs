using FluentAssertions;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LunchOrganizer.Tests.Concurrency;

/// <summary>
/// Exercises real concurrency behavior of the Postgres-backed repositories against a real, migrated
/// "lunchorganizer_test" database (see <see cref="ConcurrencyTestFixture"/>). Each test uses its own
/// distinct seed data (employee names, dates spread across March-September 2027) so tests can never
/// collide with each other even under parallel execution within the collection.
/// </summary>
[Collection("ConcurrencyTests")]
public sealed class ConcurrencyTests(ConcurrencyTestFixture fixture)
{
    [Fact]
    public async Task FiftyParallelUpserts_SameEmployeeAndDay_ResultInExactlyOneRow()
    {
        var date = new DateOnly(2027, 3, 10);
        var employee = await fixture.EmployeeRepository.AddAsync(new Employee { FullName = "Concurrency Test Employee One", IsActive = true });

        var menus = new List<Menu>();
        for (var i = 1; i <= 3; i++)
        {
            menus.Add(await fixture.MenuRepository.AddAsync(new Menu { MenuDate = date, MenuNumber = i, Description = $"Menu {i}" }));
        }

        var tasks = Enumerable.Range(0, 50).Select(i => fixture.BookingRepository.UpsertAsync(new Booking
        {
            EmployeeId = employee.Id,
            BookingDate = date,
            MenuId = menus[i % menus.Count].Id,
            PriceSnapshot = 10m,
        }));

        await Task.WhenAll(tasks);

        var single = await fixture.BookingRepository.GetForEmployeeAndDateAsync(employee.Id, date);
        single.Should().NotBeNull();

        var allForDate = await fixture.BookingRepository.GetForDateAsync(date);
        allForDate.Count(b => b.EmployeeId == employee.Id).Should().Be(1);
    }

    [Fact]
    public async Task TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers()
    {
        var date = new DateOnly(2027, 4, 10);

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var number = await fixture.MenuRepository.GetNextMenuNumberAsync(date);
            await fixture.MenuRepository.AddAsync(new Menu { MenuDate = date, MenuNumber = number, Description = $"M{number}" });
        });

        await Task.WhenAll(tasks);

        var menus = await fixture.MenuRepository.GetByDateAsync(date);
        menus.Should().HaveCount(10);
        menus.Select(m => m.MenuNumber).Should().BeEquivalentTo(Enumerable.Range(1, 10));
    }

    [Fact]
    public async Task DeleteMenuWhileBookingsInserted_NeverLeavesOrphanedBooking()
    {
        var date = new DateOnly(2027, 5, 10);
        var menu = await fixture.MenuRepository.AddAsync(new Menu { MenuDate = date, MenuNumber = 1, Description = "Contested Menu" });

        var employees = new List<Employee>();
        for (var i = 1; i <= 5; i++)
        {
            employees.Add(await fixture.EmployeeRepository.AddAsync(new Employee { FullName = $"Concurrency Test Employee Three {i}", IsActive = true }));
        }

        var bookingTasks = employees.Select(e => Task.Run(async () =>
        {
            try
            {
                await fixture.BookingRepository.UpsertAsync(new Booking
                {
                    EmployeeId = e.Id,
                    BookingDate = date,
                    MenuId = menu.Id,
                    PriceSnapshot = 10m,
                });
                return (Ok: true, Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Ok: false, Exception: ex);
            }
        }));

        var deleteTask = Task.Run(async () =>
        {
            try
            {
                await fixture.MenuRepository.DeleteAsync(menu.Id);
                return (Ok: true, Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Ok: false, Exception: ex);
            }
        });

        var allTasks = bookingTasks.Append(deleteTask).ToList();
        var results = await Task.WhenAll(allTasks);

        // Every outcome must be either a success or one of the expected races; nothing else is acceptable.
        foreach (var (ok, exception) in results)
        {
            if (ok)
            {
                continue;
            }

            (exception is DeleteRestrictedException or DbUpdateException or PostgresException).Should().BeTrue(
                $"unexpected exception type: {exception?.GetType().FullName}: {exception?.Message}");
        }

        var bookingsForDate = await fixture.BookingRepository.GetForDateAsync(date);
        foreach (var booking in bookingsForDate.Where(b => b.MenuId == menu.Id))
        {
            var stillExists = await fixture.MenuRepository.GetByIdAsync(menu.Id);
            stillExists.Should().NotBeNull($"booking {booking.Id} references menu {menu.Id} which must still exist");
        }
    }

    [Fact]
    public async Task TwentyParallelRegisterSameName_ResultInOneEmployeeIdSharedByAll()
    {
        const string name = "Concurrency Test Employee Four";

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            fixture.EmployeeRepository.AddAsync(new Employee { FullName = name, IsActive = true }));

        var results = await Task.WhenAll(tasks);

        results.Select(e => e.Id).Distinct().Should().ContainSingle();

        var found = await fixture.EmployeeRepository.GetByNameAsync(name);
        found.Should().NotBeNull();
        found!.Id.Should().Be(results[0].Id);

        var searchResults = await fixture.EmployeeRepository.SearchByNameAsync(name, 100);
        searchResults.Should().ContainSingle();
    }

    [Fact]
    public async Task TwoToFiveParallelMailerBegins_SameDate_ExactlyOneSucceeds()
    {
        var date = new DateOnly(2027, 7, 10);

        var tasks = Enumerable.Range(0, 4).Select(_ => fixture.EmailLogRepository.TryBeginAsync(date));

        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);

        var entry = await fixture.EmailLogRepository.GetAsync(date);
        entry.Should().NotBeNull();

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var rowCount = await db.EmailLogEntries.CountAsync(e => e.SummaryDate == date);
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentMenuEdits_SecondStaleSaveThrowsConcurrencyException()
    {
        var date = new DateOnly(2027, 8, 10);
        var seeded = await fixture.MenuRepository.AddAsync(new Menu { MenuDate = date, MenuNumber = 1, Description = "Original" });

        var copy1 = await fixture.MenuRepository.GetByIdAsync(seeded.Id);
        var copy2 = await fixture.MenuRepository.GetByIdAsync(seeded.Id);

        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();

        copy1!.Description = "Edited by copy 1";
        copy2!.Description = "Edited by copy 2";

        await fixture.MenuRepository.UpdateAsync(copy1);

        await FluentActions.Awaiting(() => fixture.MenuRepository.UpdateAsync(copy2))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
