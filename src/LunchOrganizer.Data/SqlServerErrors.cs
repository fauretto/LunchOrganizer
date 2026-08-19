using Microsoft.Data.SqlClient;

namespace LunchOrganizer.Data;

/// <summary>
/// Classifies <see cref="SqlException"/> error numbers surfaced through EF Core's
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>. Replaces the PostgreSQL
/// <c>SqlState</c> checks this codebase used to make (unique violation <c>23505</c>, foreign-key
/// violation <c>23503</c>) with the equivalent SQL Server <see cref="SqlException.Number"/> checks.
/// </summary>
internal static class SqlServerErrors
{
    /// <summary>
    /// True if <paramref name="ex"/> (or its <see cref="Exception.InnerException"/>, which is how
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> presents it) is a SQL Server
    /// unique-constraint or unique-index violation. Replaces PostgreSQL SqlState <c>23505</c>.
    /// SQL Server reports this as error 2627 when the uniqueness comes from a PRIMARY KEY/UNIQUE
    /// constraint, or 2601 when it comes from a unique index — this codebase has both, so both must
    /// be checked.
    /// </summary>
    public static bool IsUniqueViolation(Exception ex)
    {
        var sqlEx = Unwrap(ex);
        return sqlEx is not null && (sqlEx.Number == 2627 || sqlEx.Number == 2601);
    }

    /// <summary>
    /// True if <paramref name="ex"/> (or its <see cref="Exception.InnerException"/>) is a SQL Server
    /// foreign-key-constraint violation. Replaces PostgreSQL SqlState <c>23503</c>.
    /// </summary>
    public static bool IsForeignKeyViolation(Exception ex)
    {
        var sqlEx = Unwrap(ex);
        return sqlEx is not null && sqlEx.Number == 547;
    }

    private static SqlException? Unwrap(Exception ex) => ex as SqlException ?? ex.InnerException as SqlException;
}
