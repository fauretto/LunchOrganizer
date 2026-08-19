using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Tests.Email;

internal static class TestData
{
    public static EmailOptions DefaultOptions(string language = "fr", string pickupDirectory = "") => new()
    {
        Mode = EmailDeliveryMode.PickupDirectory,
        SenderName = "Lunch Organizer",
        SenderAddress = "lunch-organizer@cohu.com",
        Recipients = new List<string> { "kitchen@cohu.com" },
        SubjectPrefix = "COHU booked lunch for ",
        SubjectDateFormat = "dd.MM.yyyy",
        Language = language,
        SendTimeLocal = new TimeOnly(9, 1),
        WorkingDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
        SkipWhenNoBookings = true,
        PickupDirectory = string.IsNullOrEmpty(pickupDirectory)
            ? Path.Combine(Path.GetTempPath(), "LunchOrganizerTests", Guid.NewGuid().ToString("N"))
            : pickupDirectory,
        EnableInAppScheduler = false
    };

    public static Booking Booking(int employeeId, string employeeName, DateOnly date, int menuId, int menuNumber, string? menuDescription, decimal price = 12.50m, string? employeeEmail = null)
    {
        var now = DateTimeOffset.UtcNow;
        var employee = new Employee { Id = employeeId, FullName = employeeName, Email = employeeEmail, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 };
        var menu = new Menu { Id = menuId, MenuDate = date, MenuNumber = menuNumber, Description = menuDescription, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1 };
        return new Booking
        {
            Id = employeeId, EmployeeId = employeeId, Employee = employee, BookingDate = date,
            MenuId = menuId, Menu = menu, PriceSnapshot = price, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1
        };
    }
}
