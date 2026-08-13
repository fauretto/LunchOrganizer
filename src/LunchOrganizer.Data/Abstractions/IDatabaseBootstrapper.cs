namespace LunchOrganizer.Data.Abstractions;

/// <summary>
/// Abstraction responsible for initializing the database (e.g. ensuring the database and schema exist)
/// before the application starts serving requests.
/// </summary>
public interface IDatabaseBootstrapper
{
    /// <summary>Performs database initialization, such as creating the database/schema if it does not already exist.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
