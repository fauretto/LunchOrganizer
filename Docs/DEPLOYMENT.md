# LunchOrganizer — Deployment Runbook

How to publish a new release of LunchOrganizer to a server, and how the rehearsal environment on
this laptop mirrors the real one. Everything marked *verified* was executed on this machine on
19 August 2026; commands are shown exactly as they were run.

> ### Looking for the step-by-step procedure?
>
> This document is the **reference** — why things are the way they are, permissions detail, and
> troubleshooting. For a numbered procedure to follow, use one of:
>
> | Task | Procedure |
> |---|---|
> | Release to the **production server** | [`PUBLISH_TO_PRODUCTION_SERVER.md`](PUBLISH_TO_PRODUCTION_SERVER.md) |
> | Run the site on **this laptop** under IIS | [`RUN_LOCALLY_UNDER_IIS.md`](RUN_LOCALLY_UNDER_IIS.md) |

For first-time setup on a fresh machine, see [`INSTALLATION.md`](INSTALLATION.md). For the
operational scripts, see [`../Scripts/README.md`](../Scripts/README.md).

---

## 1. The two environments

The laptop is deliberately a faithful rehearsal of the production server. **One line of
configuration differs between them.**

| | This laptop | Production server |
|---|---|---|
| Web host | IIS + ASP.NET Core Module V2, in-process | same |
| Database engine | SQL Server 2022 **Express** — service `MSSQL$SQLEXPRESS`, Automatic | SQL Server (service) |
| `Database:Server` | `localhost\SQLEXPRESS` | `SQLSRV01` or `SQLSRV01,1433` |
| Authentication | Windows (`IntegratedSecurity: true`) | Windows |
| Config files | `admin-users.json`, `app.json`, `database.json`, `email.json` | same four |

That is the whole difference. There are **no `*.local.json` override files** in either environment —
see §5 for why that matters.

> **A real database service is the requirement, not a preference.** LocalDB —
> `(localdb)\MSSQLLocalDB` — cannot be used. It is a per-user engine that runs as a child of an
> interactive session; under IIS it fails in session 0 with `sqlservr.exe` crashing during startup
> (`0xc06d007e`). Verified the hard way on 18–19 August 2026. If a machine only has LocalDB,
> install SQL Server Express before going further.

---

## 2. Publishing

```powershell
.\Scripts\publish-production.cmd -Clean
```

Run it from the repo root, or from anywhere with the full path and the call operator:

```powershell
& 'C:\Projects_Git\Data\GitPerso\LunchOrganizer\Scripts\publish-production.cmd' -Clean
```

Output lands in `publish\Web\` and `publish\Mailer\` (`/publish` is gitignored). Each `config\`
folder should contain exactly four files and no `*.local.json`:

```powershell
Get-ChildItem -Recurse -Filter *.local.json .\publish   # must return nothing
```

Options: `-SelfContained` (bundle the runtime, for a server without ASP.NET Core 10),
`-Runtime <rid>` (default `win-x64`), `-OutputRoot <path>`, `-Configuration <cfg>`.

---

## 3. Deploying

**Stop the application pool first.** With in-process hosting `w3wp` holds the assemblies open and
the copy fails on locked files. Stop the pool in IIS Manager, or drop an `app_offline.htm` into the
site root and remove it afterwards.

```powershell
robocopy ".\publish\Web"    "D:\Data\WebSites\LunchOrganizer\Web"    /E
robocopy ".\publish\Mailer" "D:\Data\WebSites\LunchOrganizer\Mailer" /E
```

Start the pool. Done — two steps, no scripts, no post-copy fix-ups.

On the production server the only extra step is editing `config\database.json` once, the first time,
to point `Server` at the real instance. That file is overwritten by every subsequent deploy, so
either re-apply the edit or keep a copy of the production `database.json` alongside the release.

---

## 4. Database setup on a new server

The application creates and migrates its own schema on startup. `DatabaseBootstrapper` reads
`config\database.json`:

| Setting | Effect |
|---|---|
| `AutoCreateDatabase` | When `true`, creates the database if it is missing |
| `MaintenanceDatabase` | Database used before the app database exists — `master` |
| `Seed` | When `true`, inserts starter data after creating the schema |

Verified on this laptop on 19 August 2026 — the bootstrapper created the database on a bare
SQL Express instance and applied the schema in one pass:

```
Applying migration '20260818092820_InitialCreate'.
Database bootstrap complete: database created, 1 migration(s) applied, seeding skipped.
```

**Permissions.** The account the app connects as needs `db_owner` on `lunchorganizer` to apply
migrations. It additionally needs `dbcreator` only if you want `AutoCreateDatabase` to do the
creating. On a DBA-managed server the usual arrangement is: the DBA creates the empty database and
grants `db_owner`, and `AutoCreateDatabase` is set to `false`.

**Which Windows account connects.** Locally, SQL Server is on the same machine, so the pool's
`ApplicationPoolIdentity` works once it has a login (`IIS APPPOOL\<pool name>`). **On production
this changes if SQL Server is on a different host:** a virtual application-pool identity cannot
authenticate across the network — it presents as the machine account `DOMAIN\WEBSERVER$`. So the
production server needs either a login for that machine account, or the pool running as a domain
service account. Settle this with whoever administers `SQLSRV01` before the first deploy.

> **Naming trap on this laptop.** The IIS *site* is `LunchOrganiser` (with an **s**) but the
> *application pool* is `LunchOrganizer` (with a **z**). The virtual account follows the **pool**
> name. Granting `IIS APPPOOL\LunchOrganiser` fails with
> `Msg 15401 — Windows NT user or group ... not found`. Confirm which name you have before creating
> the login:
>
> ```powershell
> (New-Object System.Security.Principal.NTAccount('IIS APPPOOL\LunchOrganizer')).Translate([System.Security.Principal.SecurityIdentifier])
> ```

What was granted here, on 19 August 2026:

```sql
CREATE LOGIN [IIS APPPOOL\LunchOrganizer] FROM WINDOWS;
USE [lunchorganizer];
CREATE USER [IIS APPPOOL\LunchOrganizer] FOR LOGIN [IIS APPPOOL\LunchOrganizer];
ALTER ROLE db_owner ADD MEMBER [IIS APPPOOL\LunchOrganizer];
```

Verified by impersonation (`EXECUTE AS LOGIN` / `EXECUTE AS USER`) that this account can see the
database from `master`, read `__EFMigrationsHistory`, and acquire `__EFMigrationsLock` — the three
things `DatabaseBootstrapper` does at startup.

---

## 5. Why there are no `*.local.json` files

Both `.csproj` files exclude them from publish output:

```xml
<None Include="../../config/*.json" Exclude="../../config/*.local.json" ... CopyToOutputDirectory="PreserveNewest" />
<None Include="../../config/*.local.json" ... CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" />
```

Local dev builds still get overrides in `bin\Debug`; publishes never do. Keep it that way.

This exists because of a two-day outage on 18–19 August 2026. A developer `database.local.json`
pointing at LocalDB was swept into a production publish by the old `config/*.json` glob and, because
`Program.cs` loads `.local.json` *after* the base files, it silently overrode the real config on the
server. Fixing that then created the opposite trap: the override became load-bearing, and a deploy
that replaced the `config\` folder deleted it, changing the failure rather than removing it.

Now that both environments run a real SQL Server service, `database.json` alone is correct
everywhere and there is nothing to override. **Do not reintroduce a `database.local.json` on a
server.** If a machine genuinely needs different settings, edit its `database.json`.

---

## 6. Verifying a deployment

1. `http://localhost/` returns 200 and the booking page (title `Réservation`).
2. The event log shows a clean start:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='IIS AspNetCore Module V2'} -MaxEvents 5 | Format-List TimeCreated, Message
```

3. The data is reachable and the app is connecting as the identity you expect:

```powershell
sqlcmd -S "localhost\SQLEXPRESS" -d lunchorganizer -E -W -s "|" -Q "SET NOCOUNT ON; SELECT login_name, program_name FROM sys.dm_exec_sessions WHERE program_name LIKE '%Lunch%';"
```

Note that the booking page is **Blazor Server** — the first HTML response is only a shell, and menu
data arrives over the SignalR circuit. A tool that doesn't execute JavaScript (`Invoke-WebRequest`,
`curl`) will not see menu text in the response body. That is normal and not a sign of a database
problem; a database failure shows up as HTTP 500.30 at startup, not as an empty page.

---

## 7. Troubleshooting HTTP 500.30

500.30 means the app threw during startup. IIS shows nothing useful; the event log has the real
exception, because the ASP.NET Core Module captures the process's stdout on a startup failure.

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-1)} -MaxEvents 100 |
    Where-Object { $_.ProviderName -match 'IIS AspNetCore Module V2|\.NET Runtime' } |
    Select-Object -First 5 TimeCreated, ProviderName, Id, Message | Format-List
```

| Message | Meaning | Fix |
|---|---|---|
| `error: 26 - Error Locating Server/Instance Specified` | The named instance doesn't exist or isn't running | Check `Get-Service MSSQL*` and `Database:Server` |
| `error: 50 - ... Cannot create an automatic instance` | Config points at LocalDB | Point at a real instance — §1 |
| `Login failed for user 'IIS APPPOOL\...'` / `'DOMAIN\MACHINE$'` | Pool identity has no SQL login | §4 |
| `CREATE DATABASE permission denied` | `AutoCreateDatabase: true` without `dbcreator` | Pre-create the database, or grant it |
| `Database:Server must be set ...` | `config\` missing or unreadable by the pool identity | Check the folder and its ACLs |

**The decisive test** — run the deployed executable yourself, bypassing IIS:

```powershell
cd D:\Data\WebSites\LunchOrganizer\Web
$env:ASPNETCORE_URLS='http://localhost:5199'; .\LunchOrganizer.Web.exe
```

If it starts here but not under IIS, the binaries and configuration are fine and the problem is the
**application pool identity or its database permissions**. That single test is what isolated both
failures on 18–19 August.

After repeated startup failures IIS **Rapid-Fail Protection stops the pool entirely** — a recycle is
not enough, it must be started.

---

## 8. The daily mailer task

```
Scripts\register-mailer-task.cmd -ExecutablePath "D:\Data\WebSites\LunchOrganizer\Mailer\LunchOrganizer.Mailer.exe" -TimeLocal "09:01"
```

The task defaults to running as `SYSTEM`. That is fine against a real SQL Server service provided
`NT AUTHORITY\SYSTEM` (or the machine account, for a remote server) has a login with `db_owner`.
Otherwise pass `-UserName <domain\account> -Password <password>`.

The in-app scheduler is off (`"EnableInAppScheduler": false` in `config/email.json`), so the
scheduled task is the only thing that sends the daily summary. Not yet registered on this laptop as
of 19 August 2026.

---

## 9. Before the first real production deployment

The SQL Server port's **§9 verification gate has never been run** — see
[`IMPLEMENTATION_PLAN_SQLSERVER.md`](IMPLEMENTATION_PLAN_SQLSERVER.md), which states plainly that the
code had not been executed against a database. Steps 11–13 of that plan are still open. The largest
unproven risk is the `MERGE … WITH (HOLDLOCK)` concurrency logic (§5.2).

Run that gate here, against SQL Express, before shipping to `SQLSRV01`. Running it against LocalDB
would have been a weak test anyway — different edition, different startup path.

Also still open:

- **No HTTPS binding.** The event log shows `Failed to determine the https port for redirect`.
  Admin passwords are submitted through a login form, so this matters as soon as the site is reached
  over the network. See [`INSTALLATION.md`](INSTALLATION.md) §4.3.
- **`Scripts\create_database.ps1` and `create_database.sql` are obsolete** — PostgreSQL-era, and
  generated from a migration that no longer exists. See [`../Scripts/README.md`](../Scripts/README.md).
