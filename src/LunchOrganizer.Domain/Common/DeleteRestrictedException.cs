namespace LunchOrganizer.Domain.Common;

/// <summary>
/// Thrown by a repository's Delete method when PostgreSQL rejected the delete with a foreign-key
/// violation (SqlState 23503) — i.e. rows elsewhere still reference the entity being deleted, even
/// if a pre-check said otherwise a moment earlier (a delete-vs-insert race, plan §11.5). Carries no
/// business-specific ErrorCode: the repository only reports the structural fact; the calling service
/// decides which user-facing ErrorCode (e.g. MenuHasBookings / EmployeeHasBookings) applies.
/// </summary>
public sealed class DeleteRestrictedException : Exception
{
    public DeleteRestrictedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
