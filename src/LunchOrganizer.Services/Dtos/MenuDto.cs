namespace LunchOrganizer.Services.Dtos;

public sealed record MenuDto(int Id, DateOnly MenuDate, int MenuNumber, string? Description, int BookingCount, decimal? Price);
