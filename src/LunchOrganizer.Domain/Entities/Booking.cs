namespace LunchOrganizer.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly BookingDate { get; set; }
    public int MenuId { get; set; }
    public Menu? Menu { get; set; }
    public decimal PriceSnapshot { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }

    // Windows user of the client PC that made the booking. Entirely optional: all three are
    // nullable and null is normal (e.g. when the client PC's identity could not be resolved).
    public string? UserName { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
}
