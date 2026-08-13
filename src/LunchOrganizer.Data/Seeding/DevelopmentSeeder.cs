using LunchOrganizer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LunchOrganizer.Data.Seeding;

/// <summary>
/// Seeds a small, plausible development data set (employees, menus, daily price overrides and
/// bookings) into an otherwise-empty database. No-op if any employee already exists.
/// </summary>
internal static class DevelopmentSeeder
{
    public static async Task SeedAsync(
        IDbContextFactory<LunchOrganizerDbContext> factory,
        CancellationToken ct = default,
        decimal defaultPrice = 12.50m)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        if (await db.Employees.AnyAsync(ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.Now);

        static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

        // ---- Employees ----------------------------------------------------------------------
        string[] names =
        [
            "Alice Martin", "Bob Dupont", "Chloé Bernard", "David Rossi",
            "Elena Fischer", "Farid Haddad", "Giulia Conti", "Hugo Meyer"
        ];

        var employees = new List<Employee>();
        foreach (var name in names)
        {
            var parts = name.Split(' ', 2);
            var first = parts[0];
            var last = parts[1];
            var email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@cohu.com";

            var employee = new Employee
            {
                FullName = name,
                Email = email,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            employees.Add(employee);
            db.Employees.Add(employee);
        }

        await db.SaveChangesAsync(ct);

        // ---- Menus: current ISO week + next two weeks, Mon-Fri, 3 menus/day -----------------
        string[] descriptionTemplates =
        [
            "Chicken curry with basmati rice",
            "Vegetarian lasagna",
            "Grilled salmon with seasonal vegetables",
            "Beef bourguignon with mashed potatoes",
            "Quinoa Buddha bowl"
        ];

        var currentMonday = MondayOf(today);
        var menusByDateAndNumber = new Dictionary<(DateOnly Date, int Number), Menu>();
        var templateIndex = 0;

        for (var week = 0; week < 3; week++)
        {
            var weekMonday = currentMonday.AddDays(week * 7);

            for (var dayOffset = 0; dayOffset < 5; dayOffset++)
            {
                var day = weekMonday.AddDays(dayOffset);

                for (var menuNumber = 1; menuNumber <= 3; menuNumber++)
                {
                    var description = descriptionTemplates[templateIndex % descriptionTemplates.Length];
                    templateIndex++;

                    var menu = new Menu
                    {
                        MenuDate = day,
                        MenuNumber = menuNumber,
                        Description = description,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                    db.Menus.Add(menu);
                    menusByDateAndNumber[(day, menuNumber)] = menu;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        // ---- Daily price overrides (most days fall back to defaultPrice) --------------------
        (DateOnly Date, decimal Price)[] priceOverridePlan =
        [
            (currentMonday.AddDays(3), 13.00m),      // Thursday, week 1
            (currentMonday.AddDays(7 + 1), 11.75m),  // Tuesday, week 2
            (currentMonday.AddDays(14), 14.00m)      // Monday, week 3
        ];

        var overrides = new Dictionary<DateOnly, decimal>();
        foreach (var (date, price) in priceOverridePlan)
        {
            db.DailyPrices.Add(new DailyPrice
            {
                PriceDate = date,
                Price = price,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            overrides[date] = price;
        }

        await db.SaveChangesAsync(ct);

        decimal EffectivePrice(DateOnly date) =>
            overrides.TryGetValue(date, out var price) ? price : defaultPrice;

        // ---- Bookings: 8 bookings spread across the 3 seeded weeks ---------------------------
        (DateOnly Date, int EmployeeIndex, int MenuNumber)[] bookingPlan =
        [
            (currentMonday.AddDays(0), 0, 1),          // Week 1, Monday
            (currentMonday.AddDays(3), 1, 2),          // Week 1, Thursday (price override)

            (currentMonday.AddDays(7 + 0), 2, 1),      // Week 2, Monday
            (currentMonday.AddDays(7 + 1), 3, 3),      // Week 2, Tuesday (price override)
            (currentMonday.AddDays(7 + 4), 4, 2),      // Week 2, Friday

            (currentMonday.AddDays(14 + 0), 5, 1),     // Week 3, Monday (price override)
            (currentMonday.AddDays(14 + 2), 6, 2),     // Week 3, Wednesday
            (currentMonday.AddDays(14 + 4), 7, 3)      // Week 3, Friday
        ];

        foreach (var (date, employeeIndex, menuNumber) in bookingPlan)
        {
            var menu = menusByDateAndNumber[(date, menuNumber)];
            var employee = employees[employeeIndex];

            db.Bookings.Add(new Booking
            {
                EmployeeId = employee.Id,
                BookingDate = date,
                MenuId = menu.Id,
                PriceSnapshot = EffectivePrice(date),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
