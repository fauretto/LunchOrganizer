using LunchOrganizer.Domain.Common;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Services.Abstractions;

public interface IMenuService
{
    Task<IReadOnlyList<MenuDto>> GetForDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<MenuDto>> GetForWeekAsync(DateOnly monday, CancellationToken ct = default);
    Task<OperationResult<MenuDto>> AddMenuAsync(DateOnly date, string? description, CancellationToken ct = default);
    Task<OperationResult<MenuDto>> UpdateDescriptionAsync(int menuId, string? description, CancellationToken ct = default);

    /// <summary>Deletable only if the menu date is today-or-future and it has zero bookings (plan §3.3).</summary>
    Task<OperationResult> DeleteAsync(int menuId, CancellationToken ct = default);

    /// <summary>Copies one menu's description to the menu with the same MenuNumber on every other day of that week.</summary>
    Task<OperationResult> CopyDescriptionToWeekAsync(int sourceMenuId, CancellationToken ct = default);
}
