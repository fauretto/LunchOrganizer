namespace LunchOrganizer.Domain.Entities;

public class DailyPrice
{
    public DateOnly PriceDate { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }
}
