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
}
