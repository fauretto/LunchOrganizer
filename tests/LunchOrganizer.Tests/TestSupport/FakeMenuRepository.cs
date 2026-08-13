using LunchOrganizer.Data.Repositories.Abstractions;
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
