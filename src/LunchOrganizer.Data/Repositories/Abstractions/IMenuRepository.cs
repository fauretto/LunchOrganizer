using LunchOrganizer.Domain.Entities;

namespace LunchOrganizer.Data.Repositories.Abstractions;

/// <summary>
/// Repository abstraction for querying and persisting <see cref="Menu"/> entities.
/// </summary>
public interface IMenuRepository
{
    /// <summary>Retrieves all menus for a given date.</summary>
    Task<IReadOnlyList<Menu>> GetByDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Retrieves all menus for the week starting on the given Monday.</summary>
    Task<IReadOnlyList<Menu>> GetByWeekAsync(DateOnly monday, CancellationToken ct = default);

    /// <summary>Retrieves a menu by id, or null if not found.</summary>
    Task<Menu?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new menu. The unique constraint on (menu_date, menu_number) can be violated by a concurrent
    /// insert computing the same next number; implementations must catch the Postgres 23505 unique-violation
    /// error and retry with a freshly recomputed menu number, up to BusinessRules.MaxInsertRetryAttempts times
    /// (see implementation plan §11.6).
    /// </summary>
    Task<Menu> AddAsync(Menu menu, CancellationToken ct = default);

    /// <summary>Updates an existing menu.</summary>
    Task UpdateAsync(Menu menu, CancellationToken ct = default);

    /// <summary>
    /// Deletes a menu. The composite foreign key from bookings(menu_id, booking_date) guarantees this fails
    /// with a foreign-key violation (23503) if a booking references the menu, even if a concurrent booking
    /// insert races with this delete (see implementation plan §11.5). Callers should pre-check with
    /// <see cref="HasBookingsAsync"/> for a friendly message, but must not rely on it as the sole guard.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Returns true if the given menu has at least one booking referencing it.</summary>
    Task<bool> HasBookingsAsync(int menuId, CancellationToken ct = default);

    /// <summary>Computes the next available menu number for the given date.</summary>
    Task<int> GetNextMenuNumberAsync(DateOnly date, CancellationToken ct = default);
}
