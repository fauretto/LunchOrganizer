namespace LunchOrganizer.Domain.Entities;

public class Menu
{
    public int Id { get; set; }
    public DateOnly MenuDate { get; set; }
    public int MenuNumber { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
