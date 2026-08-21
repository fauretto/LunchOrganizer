using LunchOrganizer.Domain.Common;
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

    /// <summary>
    /// Imports a whole batch of menus as a single all-or-nothing operation: every menu in
    /// <paramref name="menus"/> is inserted inside one <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
    /// and one explicit database transaction, so either all of them are written or none are.
    /// </summary>
    /// <remarks>
    /// The duplicate pre-check (is there already a menu for any date in the batch?) and the insert
    /// itself run inside that same transaction, as one unit. If the check finds that the database
    /// already has at least one menu row for any date in the batch, the transaction is rolled back —
    /// nothing is written — and a <see cref="MenuImportConflictException"/> is thrown. A concurrent
    /// importer that manages to win the race between that check and this insert is instead caught via
    /// the unique index <c>ix_menus_menu_date_menu_number</c> rejecting the insert; that case is
    /// likewise rolled back and surfaced to the caller as the same <see cref="MenuImportConflictException"/>.
    /// Any other failure rolls back the transaction and rethrows the original exception unchanged.
    /// <para>
    /// This cannot be built by simply calling <see cref="AddAsync"/> in a loop: each <see cref="AddAsync"/>
    /// call opens its own <see cref="Microsoft.EntityFrameworkCore.DbContext"/> and commits immediately
    /// (see its implementation), so a failure partway through such a loop would leave the earlier,
    /// already-committed menus in place while the rest are missing — the exact partial-data outcome
    /// this method exists to prevent.
    /// </para>
    /// </remarks>
    Task ImportAsync(IReadOnlyList<Menu> menus, CancellationToken ct = default);
}
