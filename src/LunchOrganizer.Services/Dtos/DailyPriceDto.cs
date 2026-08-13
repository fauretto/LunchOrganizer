namespace LunchOrganizer.Services.Dtos;

public sealed record DailyPriceDto(DateOnly PriceDate, decimal Price);
