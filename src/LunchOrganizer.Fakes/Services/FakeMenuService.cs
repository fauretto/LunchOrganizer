using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Fakes.Internal;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;

namespace LunchOrganizer.Fakes.Services;

internal sealed class FakeMenuService(FakeDataStore store, IClock clock) : IMenuService
{
    public Task<IReadOnlyList<MenuDto>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var result = store.Menus.Values
            .Where(m => m.MenuDate == date)
            .OrderBy(m => m.MenuNumber)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<MenuDto>>(result);
    }

    public Task<IReadOnlyList<MenuDto>> GetForWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        var days = DateHelpers.WorkingDays(monday).ToHashSet();
        var result = store.Menus.Values
            .Where(m => days.Contains(m.MenuDate))
            .OrderBy(m => m.MenuDate)
            .ThenBy(m => m.MenuNumber)
            .Select(ToDto)
            .ToList();

        return Task.FromResult<IReadOnlyList<MenuDto>>(result);
    }

    public Task<OperationResult<MenuDto>> AddMenuAsync(DateOnly date, string? description, CancellationToken ct = default)
    {
        var existingNumbers = store.Menus.Values.Where(m => m.MenuDate == date).Select(m => m.MenuNumber).ToList();
        var nextNumber = existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;

        var now = clock.UtcNow;
        var id = store.NextMenuId();
        var menu = new Menu
        {
            Id = id,
            MenuDate = date,
            MenuNumber = nextNumber,
            Description = description,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        store.Menus[id] = menu;

        return Task.FromResult(OperationResult<MenuDto>.Ok(ToDto(menu)));
    }

    public Task<OperationResult<MenuDto>> UpdateDescriptionAsync(int menuId, string? description, CancellationToken ct = default)
    {
        if (!store.Menus.TryGetValue(menuId, out var menu))
        {
            return Task.FromResult(OperationResult<MenuDto>.Fail("Menu not found.", "NOT_FOUND"));
        }

        menu.Description = description;
        menu.UpdatedAtUtc = clock.UtcNow;
        menu.Version++;

        return Task.FromResult(OperationResult<MenuDto>.Ok(ToDto(menu)));
    }

    public Task<OperationResult<MenuDto>> UpdatePriceAsync(int menuId, decimal? price, CancellationToken ct = default)
    {
        if (price is { } p && (p < 0 || p > 1000))
        {
            return Task.FromResult(OperationResult<MenuDto>.Fail("Invalid price.", ErrorCodes.PriceInvalid));
        }

        if (!store.Menus.TryGetValue(menuId, out var menu))
        {
            return Task.FromResult(OperationResult<MenuDto>.Fail("Menu not found.", "NOT_FOUND"));
        }

        menu.Price = price;
        menu.UpdatedAtUtc = clock.UtcNow;
        menu.Version++;

        return Task.FromResult(OperationResult<MenuDto>.Ok(ToDto(menu)));
    }

    public Task<OperationResult> DeleteAsync(int menuId, CancellationToken ct = default)
    {
        if (!store.Menus.TryGetValue(menuId, out var menu))
        {
            return Task.FromResult(OperationResult.Fail("Menu not found.", "NOT_FOUND"));
        }

        if (menu.MenuDate < clock.Today)
        {
            return Task.FromResult(OperationResult.Fail("Cannot delete a menu for a past date."));
        }

        var bookingCount = store.Bookings.Values.Count(b => b.MenuId == menuId);
        if (bookingCount > 0)
        {
            return Task.FromResult(OperationResult.Fail($"{bookingCount} employee(s) already booked this menu."));
        }

        store.Menus.TryRemove(menuId, out _);
        return Task.FromResult(OperationResult.Ok("Menu deleted."));
    }

    public Task<OperationResult> CopyDescriptionToWeekAsync(int sourceMenuId, CancellationToken ct = default)
    {
        if (!store.Menus.TryGetValue(sourceMenuId, out var source))
        {
            return Task.FromResult(OperationResult.Fail("Menu not found.", "NOT_FOUND"));
        }

        var monday = DateHelpers.MondayOf(source.MenuDate);
        var otherDays = DateHelpers.WorkingDays(monday).Where(d => d != source.MenuDate);

        var updated = 0;
        var now = clock.UtcNow;
        foreach (var day in otherDays)
        {
            var target = store.Menus.Values.FirstOrDefault(m => m.MenuDate == day && m.MenuNumber == source.MenuNumber);
            if (target is null)
            {
                continue;
            }

            target.Description = source.Description;
            target.Price = source.Price;
            target.UpdatedAtUtc = now;
            target.Version++;
            updated++;
        }

        return Task.FromResult(OperationResult.Ok($"Updated {updated} menu(s)."));
    }

    private MenuDto ToDto(Menu m) =>
        new(m.Id, m.MenuDate, m.MenuNumber, m.Description, store.Bookings.Values.Count(b => b.MenuId == m.Id), m.Price);
}
