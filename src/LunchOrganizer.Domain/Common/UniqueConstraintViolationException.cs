namespace LunchOrganizer.Domain.Common;

/// <summary>
/// Thrown by a repository's Update method when SQL Server rejected the write with a unique-constraint
/// or unique-index violation (error 2627 or 2601, replacing PostgreSQL SqlState 23505) — i.e. another
/// row already has the value being written. Carries no business-specific ErrorCode: the repository
/// only reports the structural fact; the calling service decides which user-facing ErrorCode (e.g.
/// EmployeeNameAlreadyExists) applies. Exists so the Services layer never has to reference a database
/// driver exception type directly.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
