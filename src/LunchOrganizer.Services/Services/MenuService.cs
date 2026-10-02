using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Configuration;
using LunchOrganizer.Domain.Entities;
using LunchOrganizer.Domain.Time;
using LunchOrganizer.Services.Abstractions;
using LunchOrganizer.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunchOrganizer.Services.Services;

public sealed class MenuService(
    IMenuRepository menuRepo,
    IBookingRepository bookingRepo,
    IClock clock,
    IOptionsMonitor<AppOptions> appOptions,
    ILogger<MenuService> logger) : IMenuService
{
    public async Task<IReadOnlyList<MenuDto>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var menus = await menuRepo.GetByDateAsync(date, ct);
        var bookings = await bookingRepo.GetForDateAsync(date, ct);
        var counts = bookings.GroupBy(b => b.MenuId).ToDictionary(g => g.Key, g => g.Count());

        return menus
            .Select(m => new MenuDto(m.Id, m.MenuDate, m.MenuNumber, m.Description, counts.GetValueOrDefault(m.Id), m.Price))
            .ToList();
    }

    public async Task<IReadOnlyList<MenuDto>> GetForWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        var friday = monday.AddDays(4);
        var menus = await menuRepo.GetByWeekAsync(monday, ct);
        var bookings = await bookingRepo.GetForEmployeeBetweenAsync(null, monday, friday, ct);
        var counts = bookings.GroupBy(b => b.MenuId).ToDictionary(g => g.Key, g => g.Count());

        return menus
            .Select(m => new MenuDto(m.Id, m.MenuDate, m.MenuNumber, m.Description, counts.GetValueOrDefault(m.Id), m.Price))
            .ToList();
    }

    public async Task<OperationResult<MenuDto>> AddMenuAsync(DateOnly date, string? description, CancellationToken ct = default)
    {
        if (date < clock.Today)
        {
            return OperationResult<MenuDto>.Fail("Cannot add a menu for a past date.", ErrorCodes.MenuDateInPast);
        }

        var existingCount = (await menuRepo.GetByDateAsync(date, ct)).Count;
        var max = appOptions.CurrentValue.MaxMenusPerDay <= 0 ? BusinessRules.DefaultMaxMenusPerDay : appOptions.CurrentValue.MaxMenusPerDay;
        if (existingCount >= max)
        {
            return OperationResult<MenuDto>.Fail(
                $"Maximum of {max} menus per day reached.",
                ErrorCodes.MaxMenusPerDayReached,
                new object?[] { max });
        }

        var nextNumber = await menuRepo.GetNextMenuNumberAsync(date, ct);
        var menu = await menuRepo.AddAsync(new Menu { MenuDate = date, MenuNumber = nextNumber, Description = description }, ct);
        return OperationResult<MenuDto>.Ok(ToDto(menu, 0));
    }

    public async Task<OperationResult<MenuDto>> UpdateDescriptionAsync(int menuId, string? description, CancellationToken ct = default)
    {
        var menu = await menuRepo.GetByIdAsync(menuId, ct);
        if (menu is null)
        {
            return OperationResult<MenuDto>.Fail("Menu not found.", ErrorCodes.MenuNotFoundForDay);
        }

        menu.Description = description;

        try
        {
            await menuRepo.UpdateAsync(menu, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<MenuDto>.Fail("Someone else changed this menu.", ErrorCodes.ConcurrencyConflict);
        }

        return OperationResult<MenuDto>.Ok(ToDto(menu, await CountBookingsAsync(menu.Id, menu.MenuDate, ct)));
    }

    public async Task<OperationResult<MenuDto>> UpdatePriceAsync(int menuId, decimal? price, CancellationToken ct = default)
    {
        if (price is { } p && (p < 0 || p > 1000))
        {
            return OperationResult<MenuDto>.Fail("The supplied price is invalid.", ErrorCodes.PriceInvalid);
        }

        var menu = await menuRepo.GetByIdAsync(menuId, ct);
        if (menu is null)
        {
            return OperationResult<MenuDto>.Fail("Menu not found.", ErrorCodes.MenuNotFoundForDay);
        }

        menu.Price = price;

        try
        {
            await menuRepo.UpdateAsync(menu, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<MenuDto>.Fail("Someone else changed this menu.", ErrorCodes.ConcurrencyConflict);
        }

        logger.LogInformation("Updated menu {MenuId} price to {Price} (null means the day price applies).", menuId, price);

        return OperationResult<MenuDto>.Ok(ToDto(menu, await CountBookingsAsync(menu.Id, menu.MenuDate, ct)));
    }

    public async Task<OperationResult> DeleteAsync(int menuId, CancellationToken ct = default)
    {
        var menu = await menuRepo.GetByIdAsync(menuId, ct);
        if (menu is null)
        {
            return OperationResult.Fail("Menu not found.", ErrorCodes.MenuNotFoundForDay);
        }

        if (menu.MenuDate < clock.Today)
        {
            return OperationResult.Fail("Cannot delete a menu for a past date.", ErrorCodes.MenuDateInPast);
        }

        var count = await CountBookingsAsync(menuId, menu.MenuDate, ct);
        if (count > 0)
        {
            return OperationResult.Fail($"{count} employee(s) already booked this menu.", ErrorCodes.MenuHasBookings, new object?[] { count });
        }

        try
        {
            await menuRepo.DeleteAsync(menuId, ct);
        }
        catch (DeleteRestrictedException)
        {
            var recount = await CountBookingsAsync(menuId, menu.MenuDate, ct);
            return OperationResult.Fail("Menu cannot be deleted: it now has bookings.", ErrorCodes.MenuHasBookings, new object?[] { Math.Max(recount, 1) });
        }

        return OperationResult.Ok("Menu deleted.");
    }

    public async Task<OperationResult> CopyDescriptionToWeekAsync(int sourceMenuId, CancellationToken ct = default)
    {
        var source = await menuRepo.GetByIdAsync(sourceMenuId, ct);
        if (source is null)
        {
            return OperationResult.Fail("Menu not found.", ErrorCodes.MenuNotFoundForDay);
        }

        var diff = (int)source.MenuDate.DayOfWeek == 0 ? 6 : (int)source.MenuDate.DayOfWeek - 1;
        var monday = source.MenuDate.AddDays(-diff);

        var weekMenus = await menuRepo.GetByWeekAsync(monday, ct);
        var targets = weekMenus.Where(m => m.MenuNumber == source.MenuNumber && m.MenuDate != source.MenuDate);

        var updatedCount = 0;
        foreach (var target in targets)
        {
            target.Description = source.Description;
            target.Price = source.Price;

            try
            {
                await menuRepo.UpdateAsync(target, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return OperationResult.Fail("Someone else changed a menu while copying.", ErrorCodes.ConcurrencyConflict);
            }

            updatedCount++;
        }

        return OperationResult.Ok($"Updated {updatedCount} menu(s).");
    }

    private async Task<int> CountBookingsAsync(int menuId, DateOnly date, CancellationToken ct) =>
        (await bookingRepo.GetForDateAsync(date, ct)).Count(b => b.MenuId == menuId);

    private static MenuDto ToDto(Menu m, int bookingCount) => new(m.Id, m.MenuDate, m.MenuNumber, m.Description, bookingCount, m.Price);
}
