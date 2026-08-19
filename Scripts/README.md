# Scripts

This folder contains operational scripts for LunchOrganizer that live outside the .NET solution itself.

> **Database engine: SQL Server.** The application connects with `UseSqlServer`
> (`src/LunchOrganizer.Web/Program.cs:62`, `src/LunchOrganizer.Mailer/Program.cs:90`) and is configured
> through `config/database.json`. An earlier revision targeted PostgreSQL; the two `create_database.*`
> scripts below are leftovers from that era and **no longer work** — see
> [Obsolete](#obsolete-create_databasesql--create_databaseps1).

For the full deployment procedure see [`../Docs/DEPLOYMENT.md`](../Docs/DEPLOYMENT.md).

## Database provisioning: the app does it itself

There is no provisioning script to run. On startup, `DatabaseBootstrapper` (in `LunchOrganizer.Data`)
creates the database if it is missing and applies any outstanding EF Core migrations, driven by
`config/database.json`:

| Setting | Effect |
| --- | --- |
| `AutoCreateDatabase` | When `true`, creates the database if it does not exist |
| `MaintenanceDatabase` | The database used to connect before the app database exists — `master` for SQL Server |
| `Seed` | When `true`, inserts starter data after creating the schema |

Verified on this laptop on 19 August 2026 against a bare SQL Server 2022 Express instance:

```
Applying migration '20260818092820_InitialCreate'.
Database bootstrap complete: database created, 1 migration(s) applied, seeding skipped.
```

So the deployment order is: configure `config/database.json`, start the Web app once, and the schema
is in place. The connecting account needs `db_owner` on the database, plus `dbcreator` if
`AutoCreateDatabase` is to do the creating.

**SQL Server must be a real service.** LocalDB (`(localdb)\MSSQLLocalDB`) cannot be used under IIS —
it is a per-user engine tied to an interactive session and its `sqlservr.exe` crashes in session 0.
Use SQL Server Express or a full instance.

## Deployment: `publish-production.cmd`

Publishes both deployable apps in Release configuration into their own subfolders under an output
root (default `<repo>\publish`):

```powershell
.\Scripts\publish-production.cmd                    # framework-dependent; server needs the .NET runtime
.\Scripts\publish-production.cmd -Clean             # wipe the output root first
.\Scripts\publish-production.cmd -SelfContained     # bundle the runtime (win-x64 by default)
.\Scripts\publish-production.cmd -OutputRoot C:\publish\LunchOrganizer
```

From another directory, use the call operator so PowerShell runs the path instead of echoing it:

```powershell
& 'C:\Projects_Git\Data\GitPerso\LunchOrganizer\Scripts\publish-production.cmd' -Clean
```

Copy the *contents* of `publish\Web\` and `publish\Mailer\` to the server with `robocopy /E`, after
stopping the application pool. See [`../Docs/DEPLOYMENT.md`](../Docs/DEPLOYMENT.md) §3.

### `*.local.json` is never published

Both `.csproj` files exclude developer overrides from publish output
(`CopyToPublishDirectory="Never"`), so they reach `bin\Debug` for local runs but never a server.
Keep it that way — a `*.local.json` on a server silently overrides the real configuration, which
caused a two-day outage on 18–19 August 2026. Neither environment uses one now.

## Obsolete: `create_database.sql` + `create_database.ps1`

**These two scripts do not work against the current codebase. Do not run them.** They are kept only
until the offline provisioning route is rebuilt for SQL Server.

They are broken in two independent ways:

1. **Wrong dialect.** `create_database.sql` is PostgreSQL — it uses `citext` and bare `text` column
   types and has no `GO` batch separators. `create_database.ps1` drives it with `psql.exe`.
2. **Generated from a migration that no longer exists.** Its header records that it came from
   `20260812075601_InitialCreate`, which was removed during the SQL Server migration and replaced by
   `20260818092820_InitialCreate`.

### Rebuilding the offline route for SQL Server

Only needed for a DBA-managed server where the application's own login may not create databases or
run DDL. Regenerate the SQL from the current migration (this emits SQL Server syntax with `GO`
separators):

```powershell
dotnet ef migrations script --idempotent `
  --project src/LunchOrganizer.Data/LunchOrganizer.Data.csproj `
  --startup-project src/LunchOrganizer.Data/LunchOrganizer.Data.csproj `
  --output Scripts/create_database.sql
```

Then rewrite `create_database.ps1` to drive `sqlcmd.exe` instead of `psql.exe` — connecting with
`-S <server> -E` for Windows authentication, creating the database with a `CREATE DATABASE` guarded
by `IF DB_ID(...) IS NULL`, and running the script with `-b` so it stops on error. `sqlcmd` is the
runner that understands the `GO` separators the generated script contains.

## The daily email: `register-mailer-task.ps1` + `register-mailer-task.cmd`

Registers the daily summary email as a Windows scheduled task.

```
Scripts\register-mailer-task.cmd -ExecutablePath "D:\Data\WebSites\LunchOrganizer\Mailer\LunchOrganizer.Mailer.exe" -TimeLocal "09:01"
```

Run the `.cmd`, not the `.ps1`: Group Policy blocks direct `.ps1` execution on this machine and
overrides `-ExecutionPolicy Bypass`, so the wrapper reads the script text and invokes it as a
`[scriptblock]` instead. It also self-elevates via UAC.

The task defaults to running as `SYSTEM`, which is fine provided that account has a SQL login with
`db_owner`. Otherwise pass `-UserName <domain\account> -Password <password>`.
