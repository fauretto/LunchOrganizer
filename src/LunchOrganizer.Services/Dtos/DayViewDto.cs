namespace LunchOrganizer.Services.Dtos;

public sealed record DayViewDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    LunchOrganizer.Domain.Enums.DayEditState EditState,
    decimal Price,
    IReadOnlyList<MenuDto> Menus,
    int? SelectedMenuId);
