namespace LunchOrganizer.Domain.Configuration;

/// <summary>
/// Strongly-typed binding for the "Database" configuration section. Describes how to reach the SQL
/// Server instance that backs the application; see <see cref="Validate"/> for the invariants enforced
/// before any connection is attempted.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// The SQL Server instance to connect to, passed through verbatim as <c>Data Source</c>. There is
    /// deliberately no separate <c>Port</c> property: a port belongs inside this value as
    /// <c>HOST,1433</c>, which is also the only form that can express a named instance
    /// (<c>HOST\SQLEXPRESS</c>) or LocalDB (<c>(localdb)\MSSQLLocalDB</c>) — a dedicated <c>Port</c>
    /// field would be meaningless for either of those two cases.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    public string Database { get; set; } = "lunchorganizer";

    /// <summary>
    /// When <see langword="true"/>, connects with Windows authentication and <see cref="Username"/>/
    /// <see cref="Password"/> are ignored. When <see langword="false"/>, a SQL login is required.
    /// </summary>
    public bool IntegratedSecurity { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The database used to reach the server before the application database necessarily exists (e.g.
    /// to check for/create it). SQL Server's is <c>master</c>, unlike PostgreSQL's <c>postgres</c>.
    /// </summary>
    public string MaintenanceDatabase { get; set; } = "master";

    /// <summary>
    /// Defaults to <see langword="true"/> because Microsoft.Data.SqlClient 4.0+ defaults encryption on;
    /// set explicitly here so behaviour doesn't silently depend on the driver version installed.
    /// </summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>
    /// Must be <see langword="true"/> for a typical on-prem server presenting a self-signed
    /// certificate, otherwise the TLS handshake fails with "certificate chain was issued by an
    /// authority that is not trusted".
    /// </summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>
    /// Escape hatch: when non-blank, this string is used verbatim (only <c>Initial Catalog</c> may be
    /// overridden for the maintenance-database connection) and every field above is ignored. Covers
    /// scenarios the structured fields can't anticipate — Azure SQL with Entra ID, failover partners,
    /// <c>ApplicationIntent=ReadOnly</c>, custom certificates — and lets an operator unblock a
    /// deployment without a rebuild.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public bool AutoCreateDatabase { get; set; } = true;

    public bool Seed { get; set; } = false;

    public int MaxPoolSize { get; set; } = 50;

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Fails fast, in plain operator-readable language, rather than letting a misconfiguration surface
    /// later as an obscure driver error. Invoked from <c>BuildConnectionString()</c> in
    /// <c>LunchOrganizer.Data</c>, so it always runs before any connection attempt.
    /// </summary>
    public void Validate()
    {
        var hasConnectionString = !string.IsNullOrWhiteSpace(ConnectionString);

        if (!hasConnectionString && string.IsNullOrWhiteSpace(Server))
        {
            throw new InvalidOperationException(
                "Database:Server must be set (e.g. 'localhost\\SQLEXPRESS' or 'SQLSRV01,1433'), or Database:ConnectionString must be provided instead.");
        }

        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new InvalidOperationException("Database:Database must be set to the name of the application database.");
        }

        if (!hasConnectionString && !IntegratedSecurity && string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException(
                "Database:Username is required when Database:IntegratedSecurity is false.");
        }
    }
}
