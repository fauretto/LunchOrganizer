using LunchOrganizer.Data.Repositories.Abstractions;
using LunchOrganizer.Domain.Common;
using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Tests.TestSupport;

/// <summary>
/// In-memory, dictionary-backed fake of <see cref="IMenuRepository"/> for unit tests. Auto-increments
/// ids starting at 1. <see cref="DeleteAsync"/> is deliberately unrestricted (no FK-style guard) because
/// the booking-count guard under test in these suites lives in <c>MenuService</c>, not the repository.
/// </summary>
public sealed class FakeMenuRepository : IMenuRepository
{
    private readonly Dictionary<int, Menu> _menus = new();
    private int _nextId = 1;

    public Task<IReadOnlyList<Menu>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<Menu> results = _menus.Values.Where(m => m.MenuDate == date).ToList();
        return Task.FromResult(results);
    }

    public Task<IReadOnlyList<Menu>> GetByWeekAsync(DateOnly monday, CancellationToken ct = default)
    {
        var friday = monday.AddDays(4);
        IReadOnlyList<Menu> results = _menus.Values
            .Where(m => m.MenuDate >= monday && m.MenuDate <= friday)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<Menu?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        _menus.TryGetValue(id, out var menu);
        return Task.FromResult(menu);
    }

    public Task<Menu> AddAsync(Menu menu, CancellationToken ct = default)
    {
        menu.Id = _nextId++;
        _menus[menu.Id] = menu;
        return Task.FromResult(menu);
    }

    /// <summary>
    /// Reproduces the real repository's all-or-nothing import behaviour closely enough for service-level
    /// tests: throws <see cref="MenuImportConflictException"/> when any date in <paramref name="menus"/>
    /// already has a menu, and otherwise inserts the whole batch. Unlike the real implementation, there is
    /// no transaction to roll back here — the conflict check simply happens before any mutation, so this
    /// fake can never leave partial data either.
    /// </summary>
    public Task ImportAsync(IReadOnlyList<Menu> menus, CancellationToken ct = default)
    {
        if (menus.Count == 0)
        {
            return Task.CompletedTask;
        }

        var importDates = menus.Select(m => m.MenuDate).ToHashSet();
        var conflicts = _menus.Values
            .Select(m => m.MenuDate)
            .Where(importDates.Contains)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (conflicts.Count > 0)
        {
            throw new MenuImportConflictException(conflicts);
        }

        foreach (var menu in menus)
        {
            menu.Id = _nextId++;
            _menus[menu.Id] = menu;
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(Menu menu, CancellationToken ct = default)
    {
        _menus[menu.Id] = menu;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _menus.Remove(id);
        return Task.CompletedTask;
    }

    public Task<bool> HasBookingsAsync(int menuId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<int> GetNextMenuNumberAsync(DateOnly date, CancellationToken ct = default)
    {
        var max = _menus.Values.Where(m => m.MenuDate == date).Select(m => m.MenuNumber).DefaultIfEmpty(0).Max();
        return Task.FromResult(max + 1);
    }
}
