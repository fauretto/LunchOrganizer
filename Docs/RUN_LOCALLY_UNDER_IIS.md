# Procedure — Run LunchOrganizer on THIS LAPTOP under IIS

Step-by-step procedure for deploying and running the site locally, under IIS, on this laptop
(`CHLCF-DFXG4M3`). This environment is a deliberate rehearsal of the production server: **one line
of configuration differs between them.**

To release to the real production server instead, use
[`PUBLISH_TO_PRODUCTION_SERVER.md`](PUBLISH_TO_PRODUCTION_SERVER.md) — a different procedure, do not
mix them up. For background and troubleshooting detail, see [`DEPLOYMENT.md`](DEPLOYMENT.md).

---

> ### ⚠ Building in Release inside Visual Studio is NOT enough
>
> A Visual Studio build produces `bin\Release\net10.0\`, which is **not deployable** — it has no
> generated `web.config` for IIS. You must **publish**. The script in Step 1 runs
> `dotnet publish -c Release` itself, so you do not need to build in Visual Studio first.

---

## This laptop's setup

| | |
|---|---|
| Site folder | `D:\Data\WebSites\LunchOrganizer\Web` |
| Mailer folder | `D:\Data\WebSites\LunchOrganizer\Mailer` |
| IIS **site** name | `LunchOrganiser` — with an **s** |
| IIS **app pool** name | `LunchOrganizer` — with a **z** |
| Database | SQL Server 2022 Express, `localhost\SQLEXPRESS`, service `MSSQL$SQLEXPRESS` (Automatic) |
| URL | <http://localhost/> |

The site/pool spelling difference is real and has already cost time — the virtual account follows
the **pool** name, so it is `IIS APPPOOL\LunchOrganizer`.

---

## Part A — One-time setup (already done on 19 August 2026)

Recorded here so it can be rebuilt if the laptop is reinstalled. **You do not need to repeat this.**

| # | What was done |
|---|---|
| A1 | .NET 10 Hosting Bundle installed — ASP.NET Core Runtime 10.0.11 present |
| A2 | IIS site `LunchOrganiser` created, pointing at `D:\Data\WebSites\LunchOrganizer\Web` |
| A3 | App pool `LunchOrganizer` set to **No Managed Code** |
| A4 | **SQL Server 2022 Express installed** — this is essential, see the warning below |
| A5 | SQL login created for the app pool and granted `db_owner` |
| A6 | Database `lunchorganizer` created by the application itself on first start |
| A7 | App pool identity set to `ApplicationPoolIdentity` — **see status note below** |

> **Status of A7 as of 19 August 2026:** the SQL login (A5) is created and verified, but the app
> pool identity may still be set to `COHU\mfauro`, left over from a LocalDB workaround. Check it in
> IIS Manager → Application Pools → **LunchOrganizer** → Advanced Settings → *Process Model* →
> **Identity**. If it is not `ApplicationPoolIdentity`, change it — otherwise the site stays tied to
> that account's Windows password and will break when the password expires. Confirm which account
> is actually in use with check 3 in Part C.

| A8 | Scheduled task `LunchOrganizer Daily Summary Mailer` registered, and **`NT AUTHORITY\SYSTEM` granted database access** — see below |

The SQL grants from A5 and A8, for reference:

```sql
-- A5: the web application, running as the app pool identity
CREATE LOGIN [IIS APPPOOL\LunchOrganizer] FROM WINDOWS;
GO
USE [lunchorganizer];
GO
CREATE USER [IIS APPPOOL\LunchOrganizer] FOR LOGIN [IIS APPPOOL\LunchOrganizer];
ALTER ROLE db_owner ADD MEMBER [IIS APPPOOL\LunchOrganizer];
GO

-- A8: the daily mailer scheduled task, which runs as SYSTEM (a DIFFERENT identity)
CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_owner ADD MEMBER [NT AUTHORITY\SYSTEM];
```

> ### ⚠ A server login is not the same as database access
>
> SQL Server setup creates a server-level login for `NT AUTHORITY\SYSTEM` automatically, so the
> account looks provisioned when it is not. Without a **database user** and a role, the Mailer
> connects to the instance and then fails to open `lunchorganizer` — it exits non-zero and writes no
> email, while Task Scheduler's history still shows the task completing. That is exactly what
> happened on 19 August 2026.
>
> Note the mailer needs its own grant because **the task runs as a different identity from the
> website**: the site is the app pool account, the task is `SYSTEM`.

> ### ⚠ LocalDB cannot be used
>
> `(localdb)\MSSQLLocalDB` is a per-user engine that runs as a child of an interactive session. Under
> IIS it fails in session 0 — `sqlservr.exe` crashes during startup with `0xc06d007e`. This was
> diagnosed the hard way on 18–19 August 2026. **A real SQL Server service is required**, which is
> why SQL Server Express is installed. Never point `config\database.json` at LocalDB.

---

## Part B — Deploying a change

### Step 1 — Publish

From the repository root:

```powershell
.\Scripts\publish-production.cmd -Clean
```

Or, from any other directory:

```powershell
& 'C:\Projects_Git\Data\GitPerso\LunchOrganizer\Scripts\publish-production.cmd' -Clean
```

**This is the only script you need to run.** Output goes to `publish\Web\` and `publish\Mailer\`.

### Step 2 — Stop the application pool

The DLLs are locked while the site runs. In IIS Manager → **Application Pools** → select
**LunchOrganizer** → **Stop**.

### Step 3 — Copy the files

```powershell
robocopy ".\publish\Web"    "D:\Data\WebSites\LunchOrganizer\Web"    /E
robocopy ".\publish\Mailer" "D:\Data\WebSites\LunchOrganizer\Mailer" /E
```

Use `/E` and nothing else. **Never `/MIR` or `/PURGE`**, and never delete the destination folder
first — those remove files the release does not carry.

No configuration edit is needed here. The committed `config\database.json` already names
`localhost\SQLEXPRESS`, which is correct for this laptop. (On the production server this is the one
line that changes.)

### Step 4 — Start the application pool

IIS Manager → **Application Pools** → **LunchOrganizer** → **Start**.

### Step 5 — Open the site

<http://localhost/> — you should get the booking page, titled **Réservation**.

---

## Part C — Verifying

```powershell
# 1. Does it respond?
(Invoke-WebRequest -UseBasicParsing http://localhost/ -TimeoutSec 60).StatusCode

# 2. Did it start cleanly?
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='IIS AspNetCore Module V2'} -MaxEvents 5 |
    Format-List TimeCreated, Message

# 3. Which account is it connecting to SQL Server as?
sqlcmd -S "localhost\SQLEXPRESS" -d lunchorganizer -E -W -s "|" -Q "SET NOCOUNT ON; SELECT DISTINCT login_name, program_name FROM sys.dm_exec_sessions WHERE program_name LIKE '%Lunch%';"

# 4. Is the data there?
sqlcmd -S "localhost\SQLEXPRESS" -d lunchorganizer -E -W -s "|" -Q "SET NOCOUNT ON; SELECT 'employees' t, COUNT(*) n FROM employees UNION ALL SELECT 'menus', COUNT(*) FROM menus UNION ALL SELECT 'bookings', COUNT(*) FROM bookings UNION ALL SELECT 'daily_prices', COUNT(*) FROM daily_prices;"
```

Check 3 should report `IIS APPPOOL\LunchOrganizer`. As of 19 August 2026 check 4 returns
3 employees, 9 menus, 3 bookings, 5 daily prices.

> The booking page is **Blazor Server** — the first HTML response is only a shell, and the menu data
> arrives afterwards over a SignalR connection. `Invoke-WebRequest` and `curl` do not run JavaScript,
> so they will not show menu text in the response body. This is normal and is **not** a sign of a
> database problem.

---

## Part D — Faster loop while developing

Deploying to IIS for every code change is slow. For ordinary development, skip IIS entirely and run
Kestrel directly — it uses the same `config\` files and the same SQL Express database:

```powershell
dotnet run --project src\LunchOrganizer.Web
```

Then browse the URL it prints. Use the full IIS procedure above when you specifically want to test
the deployment itself — hosting model, app pool identity, SQL permissions — which is exactly the
part that broke in August and the part production shares.

---

## If something goes wrong

| Symptom | Meaning | Fix |
|---|---|---|
| **HTTP 503** Service Unavailable | The application pool is stopped or disabled | Start it (Step 4). IIS also stops a pool automatically after repeated crashes — Rapid-Fail Protection — in which case a recycle is not enough, it must be **started** |
| **HTTP 500.30** | The app threw during startup | Read the event log, below |
| **HTTP 500** with the site otherwise up | A request failed, not startup | Application logs |
| Copy fails, files in use | Pool still running | Step 2 |

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-1)} -MaxEvents 100 |
    Where-Object { $_.ProviderName -match 'IIS AspNetCore Module V2|\.NET Runtime' } |
    Select-Object -First 5 TimeCreated, ProviderName, Id, Message | Format-List
```

| Message | Cause | Fix |
|---|---|---|
| `error: 26 - Error Locating Server/Instance Specified` | SQL Express not running, or wrong instance name | `Get-Service MSSQL*` |
| `error: 50 - ... automatic instance` | Config is pointing at LocalDB | Fix `config\database.json` — see the warning in Part A |
| `Login failed for user 'IIS APPPOOL\LunchOrganizer'` | SQL login missing | Re-apply the grant in Part A |

**The decisive test** — run the deployed executable directly, bypassing IIS:

```powershell
cd D:\Data\WebSites\LunchOrganizer\Web
$env:ASPNETCORE_URLS='http://localhost:5199'; .\LunchOrganizer.Web.exe
```

If it starts this way but not under IIS, the files and configuration are fine and the problem is the
**app pool identity or its SQL permissions**. That single test isolated both August failures.
