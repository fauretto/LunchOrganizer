using System.Collections.Concurrent;
using System.Threading;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Time;

namespace LunchOrganizer.Fakes.Internal;

/// <summary>
/// Thread-safe in-memory store shared (as a singleton) by every fake service. Backed entirely by
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> and seeded once, from the constructor, with a plausible
/// data set for driving UI development.
/// </summary>
internal sealed class FakeDataStore
{
    public ConcurrentDictionary<int, Employee> Employees { get; } = new();
    public ConcurrentDictionary<int, Menu> Menus { get; } = new();

    /// <summary>Key = $"{employeeId}:{bookingDate:O}" — one booking per employee per day.</summary>
    public ConcurrentDictionary<string, Booking> Bookings { get; } = new();

    public ConcurrentDictionary<DateOnly, DailyPrice> Prices { get; } = new();

    /// <summary>Dedicated lock used by FakeEmployeeService for the atomic get-or-create in RegisterAsync.</summary>
    public object RegistrationLock { get; } = new();

    private readonly object _seedLock = new();

    private int _employeeIdSeq;
    private int _menuIdSeq;
    private int _bookingIdSeq;

    public FakeDataStore(IClock clock)
    {
        Seed(clock);
    }

    public int NextEmployeeId() => Interlocked.Increment(ref _employeeIdSeq);

    public int NextMenuId() => Interlocked.Increment(ref _menuIdSeq);

    public int NextBookingId() => Interlocked.Increment(ref _bookingIdSeq);

    public static string BookingKey(int employeeId, DateOnly date) => $"{employeeId}:{date:O}";

    private void Seed(IClock clock)
    {
        lock (_seedLock)
        {
            if (!Employees.IsEmpty)
            {
                return;
            }

            var now = clock.UtcNow;

            // ---- Employees ----------------------------------------------------------------
            string[] names =
            [
                "Alice Martin", "Bob Dupont", "Chloé Bernard", "David Rossi",
                "Elena Fischer", "Farid Haddad", "Giulia Conti", "Hugo Meyer"
            ];

            var employeeIds = new int[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var id = NextEmployeeId();
                employeeIds[i] = id;
                var parts = names[i].Split(' ', 2);
                var email = $"{parts[0].ToLowerInvariant()}.{parts[1].ToLowerInvariant()}@cohu.com";

                Employees[id] = new Employee
                {
                    Id = id,
                    FullName = names[i],
                    Email = email,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = 1
                };
            }

            // ---- Menus: current ISO week + next two weeks, Mon-Fri, 3 menus/day -----------
            string[] descriptionTemplates =
            [
                "Chicken curry with basmati rice",
                "Vegetarian lasagna",
                "Grilled salmon with seasonal vegetables",
                "Beef bourguignon with mashed potatoes",
                "Quinoa Buddha bowl"
            ];

            var currentMonday = DateHelpers.MondayOf(clock.Today);
            var week1Days = DateHelpers.WorkingDays(currentMonday);
            var menusByDateAndNumber = new Dictionary<(DateOnly Date, int Number), int>();
            var templateIndex = 0;

            for (var week = 0; week < 3; week++)
            {
                var weekMonday = currentMonday.AddDays(week * 7);
                foreach (var day in DateHelpers.WorkingDays(weekMonday))
                {
                    for (var menuNumber = 1; menuNumber <= 3; menuNumber++)
                    {
                        var id = NextMenuId();
                        var description = descriptionTemplates[templateIndex % descriptionTemplates.Length];
                        templateIndex++;

                        Menus[id] = new Menu
                        {
                            Id = id,
                            MenuDate = day,
                            MenuNumber = menuNumber,
                            Description = description,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now,
                            Version = 1
                        };
                        menusByDateAndNumber[(day, menuNumber)] = id;
                    }
                }
            }

            // ---- Daily price overrides (most days fall back to the 12.50 default) ---------
            (DateOnly Date, decimal Price)[] priceOverrides =
            [
                (currentMonday.AddDays(3), 13.00m),      // Thursday, current week
                (currentMonday.AddDays(7 + 1), 11.75m),  // Tuesday, next week
                (currentMonday.AddDays(14), 14.00m)      // Monday, week after next
            ];

            foreach (var (date, price) in priceOverrides)
            {
                Prices[date] = new DailyPrice
                {
                    PriceDate = date,
                    Price = price,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = 1
                };
            }

            decimal EffectivePrice(DateOnly date) =>
                Prices.TryGetValue(date, out var p) ? p.Price : FakeDefaults.DefaultLunchPrice;

            // ---- Bookings: a handful spread across past/today/future of the current week --
            var pastDays = week1Days.Where(d => d < clock.Today).ToList();
            var isTodayWorking = week1Days.Contains(clock.Today);
            var futureDays = week1Days.Where(d => d > clock.Today).ToList();

            var plan = new List<(DateOnly Date, int EmployeeIndex, int MenuNumber)>();

            if (pastDays.Count > 0)
            {
                // Closest past working day: used to exercise the LockedPast UI state.
                var pastDate = pastDays[^1];
                plan.Add((pastDate, 0, 1));
                plan.Add((pastDate, 1, 2));
            }

            if (isTodayWorking)
            {
                plan.Add((clock.Today, 2, 1));
                plan.Add((clock.Today, 3, 2));
                plan.Add((clock.Today, 4, 3));
            }

            if (futureDays.Count > 0)
            {
                plan.Add((futureDays[0], 5, 2));
                plan.Add((futureDays[0], 6, 3));
            }

            if (futureDays.Count > 1)
            {
                plan.Add((futureDays[1], 7, 1));
                plan.Add((futureDays[1], 0, 2));
            }

            // Safety net: whatever weekday "today" happens to be, still land in the 8-12 range
            // by spilling extra bookings into next week's working days if needed.
            if (plan.Count < 8)
            {
                var nextWeekDays = DateHelpers.WorkingDays(currentMonday.AddDays(7));
                var fillerEmployee = 0;
                foreach (var day in nextWeekDays)
                {
                    if (plan.Count >= 8)
                    {
                        break;
                    }

                    plan.Add((day, fillerEmployee % names.Length, 1));
                    fillerEmployee++;
                }
            }

            foreach (var (date, employeeIndex, menuNumber) in plan)
            {
                if (!menusByDateAndNumber.TryGetValue((date, menuNumber), out var menuId))
                {
                    continue;
                }

                var employeeId = employeeIds[employeeIndex];
                var bookingId = NextBookingId();
                var key = BookingKey(employeeId, date);

                Bookings[key] = new Booking
                {
                    Id = bookingId,
                    EmployeeId = employeeId,
                    BookingDate = date,
                    MenuId = menuId,
                    PriceSnapshot = EffectivePrice(date),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    Version = 1
                };
            }
        }
    }
}
