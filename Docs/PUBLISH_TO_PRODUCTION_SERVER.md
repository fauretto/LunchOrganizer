# Procedure — Publish LunchOrganizer to the PRODUCTION SERVER

Step-by-step procedure for releasing a new version of LunchOrganizer to the **production server**.

For running the site on your own laptop instead, use
[`RUN_LOCALLY_UNDER_IIS.md`](RUN_LOCALLY_UNDER_IIS.md) — a different procedure, do not mix them up.
For background, permissions detail and troubleshooting, see [`DEPLOYMENT.md`](DEPLOYMENT.md).

---

> ### ⚠ Building in Release inside Visual Studio is NOT enough
>
> A Visual Studio build produces `bin\Release\net10.0\`, which is **not deployable**. It lacks the
> generated `web.config` that IIS needs, and its file layout is not what the server expects.
>
> You must **publish**, which is a different operation. The script in Step 1 runs
> `dotnet publish -c Release` itself, so **you do not need to build in Visual Studio first** — the
> script does the whole thing. If you already built in Release, no harm; just run the script anyway.

---

## Part A — One-time server setup

Do this once per server. Skip to Part B for a routine release.

| # | What | Detail |
|---|---|---|
| A1 | **Install the .NET 10 Hosting Bundle** | Not just the runtime — the *Hosting Bundle*, which installs the ASP.NET Core Module for IIS. <https://dotnet.microsoft.com/download/dotnet/10.0> |
| A2 | **Create the IIS site** | Point it at the folder you will deploy into, e.g. `D:\Sites\LunchOrganizer\Web`. |
| A3 | **Set the app pool to "No Managed Code"** | .NET CLR version → *No Managed Code*. The ASP.NET Core Module hosts the app, not the CLR. |
| A4 | **Note the app pool's exact name** | You need it in A6. ⚠ It is not necessarily the same as the *site* name — on the laptop the site is `LunchOrganiser` but the pool is `LunchOrganizer`. |
| A5 | **Confirm the SQL Server instance and auth** | Production uses Windows authentication. Get the instance name (e.g. `SQLSRV01` or `SQLSRV01,1433`) from whoever administers it. |
| A6 | **Create the SQL login and grant it** | See "Which account connects" below. |
| A7 | **Give the app pool identity read access** to the deployment folder, including `config\`. |

### Which account connects to SQL Server

This depends on whether SQL Server runs on the **same machine** as IIS:

- **Same machine** — the pool's `ApplicationPoolIdentity` works. Create a login for
  `IIS APPPOOL\<pool name>`.
- **Different machine** (likely for `SQLSRV01`) — a virtual app pool identity **cannot**
  authenticate across the network; it arrives at SQL Server as the machine account
  `DOMAIN\WEBSERVERNAME$`. You need either a login for that machine account, or the app pool
  running as a **domain service account** with a login. **Settle this with the DBA before deploy
  day** — it is the single most likely thing to block a first deployment.

Then, whichever account it is:

```sql
CREATE LOGIN [<account>] FROM WINDOWS;
GO
USE [lunchorganizer];
GO
CREATE USER [<account>] FOR LOGIN [<account>];
ALTER ROLE db_owner ADD MEMBER [<account>];
```

`db_owner` is required so EF Core can apply migrations. Grant the server role `dbcreator` **only**
if you want the application to create the database itself (`AutoCreateDatabase: true`). On a
DBA-managed server the usual arrangement is the opposite: the DBA creates an empty `lunchorganizer`
database, grants `db_owner`, and you set `AutoCreateDatabase: false` in Step 4.

---

## Part B — Every release

### Step 1 — Publish

From the repository root on your laptop:

```powershell
.\Scripts\publish-production.cmd -Clean
```

From any other directory, use the full path with the call operator:

```powershell
& 'C:\Projects_Git\Data\GitPerso\LunchOrganizer\Scripts\publish-production.cmd' -Clean
```

**This is the only script you need to run.** It builds both applications in Release and publishes
them to:

```
publish\Web\      → the website
publish\Mailer\   → the daily email console app
```

Add `-SelfContained` if the server does **not** have the .NET 10 runtime installed. Everything else
defaults correctly.

### Step 2 — Check the output before you copy

```powershell
Get-ChildItem -Recurse -Filter *.local.json .\publish
```

**This must return nothing.** A `*.local.json` file reaching a server silently overrides its real
configuration — that caused a two-day outage on 18–19 August 2026. The project files are configured
to exclude them, so this is a confirmation, not a fix.

### Step 3 — Take the site offline

The application's DLLs are locked while it runs, so the copy will fail otherwise. Either:

- **IIS Manager** → Application Pools → select the pool → **Stop**, or
- drop a file named `app_offline.htm` into the site root (visitors see it; delete it afterwards).

### Step 4 — Copy the files to the server

```powershell
robocopy ".\publish\Web"    "\\SERVER\D$\Sites\LunchOrganizer\Web"    /E
robocopy ".\publish\Mailer" "\\SERVER\D$\Sites\LunchOrganizer\Mailer" /E
```

Adjust the destinations to match Part A. Copy by any means you like — the important part is the
**`/E` flag and nothing else**:

> ### ⚠ Never use `/MIR` or `/PURGE`, and never delete the destination folder first
>
> `/E` copies and overwrites but never deletes files the source doesn't have. `/MIR` and `/PURGE`
> delete them — including anything the server needs that isn't in the release.

### Step 5 — Set the production configuration

Edit `config\database.json` **on the server**:

```json
{
  "Database": {
    "Server": "SQLSRV01",
    "Database": "lunchorganizer",
    "IntegratedSecurity": true,
    "Encrypt": true,
    "TrustServerCertificate": true,
    "AutoCreateDatabase": false,
    "Seed": false
  }
}
```

`Server` is normally the only value that differs from the committed file. Set `AutoCreateDatabase`
to `false` if the DBA pre-created the database (Part A6).

Also check `config\email.json` — the SMTP relay host and recipients — and `config\admin-users.json`.

> **This step repeats on every release.** Step 4 overwrites `config\database.json` with the
> committed version. Either re-apply the edit each time, or keep a copy of the production
> `config\` folder outside the site and restore it after copying. Do **not** solve this by creating
> a `database.local.json` — that is the trap that caused the August outage.

### Step 6 — Bring the site back up

Start the application pool in IIS Manager (or delete `app_offline.htm`).

On the **first** deployment only, the application creates the schema at startup. Watch for it in
Step 7.

### Step 7 — Verify

1. Open the site in a browser. You should get the booking page, titled **Réservation**.
2. Check the event log on the server:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='IIS AspNetCore Module V2'} -MaxEvents 5 | Format-List TimeCreated, Message
```

Expect `Application '...' started successfully.` On a first deployment you should also see the
schema being created:

```
Applying migration '20260818092820_InitialCreate'.
Database bootstrap complete: database created, 1 migration(s) applied, seeding skipped.
```

3. Confirm the app is connecting as the account you intended:

```powershell
sqlcmd -S "SQLSRV01" -d lunchorganizer -E -W -s "|" -Q "SET NOCOUNT ON; SELECT DISTINCT login_name, program_name FROM sys.dm_exec_sessions WHERE program_name LIKE '%Lunch%';"
```

> The booking page is **Blazor Server**: the first HTML response is only a shell and the menu data
> arrives afterwards over a SignalR connection. Checking with `curl` or `Invoke-WebRequest` will
> not show menu text. That is normal. A real database failure shows up as **HTTP 500.30**, not as
> an empty page.

### Step 8 — Register the daily email (one-time per server)

```
Scripts\register-mailer-task.cmd -ExecutablePath "D:\Sites\LunchOrganizer\Mailer\LunchOrganizer.Mailer.exe" -TimeLocal "09:01"
```

Run the `.cmd`, not the `.ps1` — it self-elevates and works where Group Policy blocks `.ps1` files.

The task runs as **`SYSTEM`** by default, which is a *different identity from the web app's*. Give it
database access too, or the task will appear to succeed while sending nothing:

```sql
USE [lunchorganizer];
CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_owner ADD MEMBER [NT AUTHORITY\SYSTEM];
```

For a **remote** SQL Server, `SYSTEM` arrives as the machine account `DOMAIN\WEBSERVER$` — grant that
instead. Or avoid the question entirely with `-UserName <domain\account> -Password <password>`.

> ### ⚠ A server login is not the same as database access
>
> SQL Server setup creates a server-level login for `NT AUTHORITY\SYSTEM` automatically, so it looks
> like the account is already provisioned. It is not: without a **database user** and a role, the
> Mailer connects to the instance and then fails to open `lunchorganizer`. This cost half a day on
> 19 August 2026 — the task history showed the task completing, but no email was ever written.
>
> Check both levels:
>
> ```sql
> SELECT name, type_desc FROM sys.server_principals WHERE name = N'NT AUTHORITY\SYSTEM';        -- login
> USE [lunchorganizer];
> SELECT name FROM sys.database_principals WHERE name = N'NT AUTHORITY\SYSTEM';                  -- user
> ```

---

## If something goes wrong

**HTTP 500.30** means the app threw while starting. IIS shows nothing useful; the real exception is
in the event log:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-1)} -MaxEvents 100 |
    Where-Object { $_.ProviderName -match 'IIS AspNetCore Module V2|\.NET Runtime' } |
    Select-Object -First 5 TimeCreated, ProviderName, Id, Message | Format-List
```

| Message | Cause | Fix |
|---|---|---|
| `error: 26 - Error Locating Server/Instance Specified` | Wrong instance name, or SQL Server not running | Step 5 |
| `Login failed for user 'DOMAIN\SERVER$'` | Remote SQL Server, no login for the machine account | Part A6 |
| `Login failed for user 'IIS APPPOOL\...'` | No SQL login for the pool identity | Part A6 |
| `CREATE DATABASE permission denied` | `AutoCreateDatabase: true` without `dbcreator` | Set it `false`, have the DBA create the database |
| `error: 50 - ... automatic instance` | Config points at LocalDB | LocalDB cannot be used under IIS — Step 5 |

**The decisive test** — run the deployed executable directly on the server, bypassing IIS:

```powershell
cd D:\Sites\LunchOrganizer\Web
$env:ASPNETCORE_URLS='http://localhost:5199'; .\LunchOrganizer.Web.exe
```

If it starts this way but not under IIS, the files and configuration are fine and the problem is the
**app pool identity or its SQL permissions**. If it fails both ways, it is configuration.

**To roll back:** keep the previous `publish\` output, or a zip of the site folder, and copy it back
the same way. There is no database rollback — migrations are forward-only, so a release that adds a
migration cannot be undone by copying old files alone.

---

## Before the very first production deployment

The SQL Server port's **§9 verification gate has never been run** — see
[`IMPLEMENTATION_PLAN_SQLSERVER.md`](IMPLEMENTATION_PLAN_SQLSERVER.md). The riskiest unproven area is
the `MERGE … WITH (HOLDLOCK)` concurrency logic. Run that gate on the laptop first
([`RUN_LOCALLY_UNDER_IIS.md`](RUN_LOCALLY_UNDER_IIS.md)), not on the production server.

Also still open: **no HTTPS binding** is configured. Admin passwords are submitted through a login
form, so arrange a certificate before the site is reachable over the network. See
[`INSTALLATION.md`](INSTALLATION.md) §4.3.
