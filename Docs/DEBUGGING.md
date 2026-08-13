# LunchOrganizer — Debugging Guide

How to run, debug and inspect LunchOrganizer on **this laptop**. Everything below was executed on this machine on 12 August 2026; commands are shown exactly as they were run, not as they ought to work.

For deploying to another machine, see [`INSTALLATION.md`](INSTALLATION.md). For what the application looks like to the people who use it, see [`USER_GUIDE.md`](USER_GUIDE.md) (French: [`USER_GUIDE.fr.md`](USER_GUIDE.fr.md)). For what still needs testing by hand, see [`TEST_CHECKLIST.md`](TEST_CHECKLIST.md).

---

## 1. What is already installed here

| Component | Status on this machine |
|---|---|
| .NET SDK | **10.0.302** (and 9.0.316). `global.json` pins the `10.0.3xx` band. |
| PostgreSQL | **16.14**, Windows service `postgresql-x64-16`, running. |
| `psql.exe` | **`D:\PostgreSQL\bin\psql.exe`** — **not on `PATH`**. Use the full path. |
| Data directory | `D:\Ismeca\NativeNET\PostgreSQL\data_16.3` |
| Database | `lunchorganizer`, created by the application itself on first run. |
| Credentials | `config/database.local.json` — git-ignored, holds the real password. `config/database.json` keeps the `changeme` placeholder. |

Nothing else needs installing to debug.

---

## 2. First run

```powershell
cd C:\Projects_Git\Data\GitPerso\LunchOrganizer
dotnet tool restore      # restores the local dotnet-ef tool (.config/dotnet-tools.json)
dotnet build LunchOrganizer.sln
dotnet run --project src/LunchOrganizer.Web
```

The site listens on **http://localhost:5226** (and **https://localhost:7047** with the `https` profile). Both come from `src/LunchOrganizer.Web/Properties/launchSettings.json`.

- **Booking page:** `/`
- **Admin:** `/admin` → login **`massimo` / `changeme`** (from `config/admin-users.json`)
- **Language:** French by default; the `FR · EN` switch is in the header.

On the very first start the application creates the database, applies the migration, installs `citext` and `unaccent`, and — because `config/database.local.json` sets `"Seed": true` — inserts demo data. Expect a log line like:

```
Database bootstrap complete: database created, 1 migration(s) applied, seeding ran.
```

To choose a different port without editing `launchSettings.json`:

```powershell
dotnet run --project src/LunchOrganizer.Web --no-launch-profile --urls http://localhost:5310
```

---

## 3. Debugging in an IDE

### Visual Studio / Rider

Open `LunchOrganizer.sln`, set **LunchOrganizer.Web** as the startup project, press **F5**. The `http` and `https` launch profiles are already defined. Breakpoints in components, ViewModels, services and repositories all hit normally — this is Blazor **Server**, so all of that code runs in the .NET process, not in the browser.

To debug the mailer instead, set **LunchOrganizer.Mailer** as the startup project and give it arguments (see §6).

### VS Code

Use the C# Dev Kit's generated launch configuration for `LunchOrganizer.Web`, or attach to a running `dotnet` process.

### What the browser is *not* doing

There is no WebAssembly and no client-side C#. The browser holds a SignalR circuit; UI events travel to the server, run there, and the resulting DOM diff comes back. So:

- **C# breakpoints** → the IDE.
- **CSS, layout, focus rings, print preview** → browser dev tools.
- **A dead button** is usually a *render mode* problem, not a logic problem — see §9.

---

## 4. Inspecting the database

`psql` is not on `PATH`, so use the full path. Password from `config/database.local.json`.

```powershell
$psql = 'D:\PostgreSQL\bin\psql.exe'
$env:PGPASSWORD = 'P0stgr3sP0stgr3s'

# tables
& $psql -h localhost -U postgres -d lunchorganizer -c "\dt"

# what is in there
& $psql -h localhost -U postgres -d lunchorganizer -c "select count(*) from bookings;"

# today's bookings, readable
& $psql -h localhost -U postgres -d lunchorganizer -c "
  select e.full_name, m.menu_number, b.price_snapshot
  from bookings b
  join employees e on e.id = b.employee_id
  join menus m on m.id = b.menu_id
  where b.booking_date = current_date
  order by m.menu_number, e.full_name;"

$env:PGPASSWORD = $null
```

An interactive session:

```powershell
$env:PGPASSWORD='P0stgr3sP0stgr3s'; & 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d lunchorganizer
```

Useful once inside: `\dt` tables · `\d bookings` one table in full · `\di` indexes · `\dx` extensions · `\q` quit.

### Confirming the integrity rules are live

These should all be **rejected**. If any succeeds, something is wrong with the schema:

```sql
BEGIN;
-- Tuesday's menu on a Monday → violates the composite FK
INSERT INTO bookings (employee_id, booking_date, menu_id, price_snapshot)
SELECT (SELECT id FROM employees LIMIT 1), m.menu_date + 1, m.id, 12.50
FROM menus m ORDER BY m.id LIMIT 1;
ROLLBACK;
```

Others worth trying the same way: a second booking for the same `(employee_id, booking_date)`; an employee whose name differs only by case; deleting an employee who has bookings; `menu_number = 0`.

---

## 5. Resetting the database

The fastest way to get back to a clean, seeded state — the application recreates everything on the next start:

```powershell
$env:PGPASSWORD='P0stgr3sP0stgr3s'
& 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d postgres -c "DROP DATABASE IF EXISTS lunchorganizer WITH (FORCE);"
$env:PGPASSWORD=$null
dotnet run --project src/LunchOrganizer.Web
```

`WITH (FORCE)` disconnects anything still holding the database — needed if a debug session is lingering.

To wipe the data but keep the schema:

```sql
TRUNCATE bookings, menus, daily_prices, employees, email_log RESTART IDENTITY CASCADE;
```

**Seeding** is controlled by `"Seed"` in `config/database.local.json`. It is idempotent, so it will not duplicate rows if it runs again. Set it to `false` to work against an empty database.

---

## 6. Debugging the daily email — without sending anything

The default mode is `PickupDirectory`, which writes an `.eml` file instead of contacting any mail server. Nothing can escape to a real mailbox by accident.

```powershell
# today
dotnet run --project src/LunchOrganizer.Mailer -- --dry-run

# a specific day
dotnet run --project src/LunchOrganizer.Mailer -- --date 2026-08-17 --dry-run

# help
dotnet run --project src/LunchOrganizer.Mailer -- --help
```

The `.eml` lands in **`src\LunchOrganizer.Mailer\bin\Debug\net10.0\mail-drop\`** (the path in `email.json` is relative to the executable, not to the repository). Double-click it to open it in Outlook exactly as a recipient would see it.

| Exit code | Meaning |
|---|---|
| `0` | Sent |
| `1` | Failed |
| `2` | Skipped — no bookings that day |
| `3` | Already handled — another process had reserved the day |

`--dry-run` never touches `email_log`, so you can run it repeatedly on the same date. Without `--dry-run`, the day is reserved and a second run returns `3` by design — that is the double-send guard, not a bug. To re-run a real send for a date:

```sql
DELETE FROM email_log WHERE summary_date = DATE '2026-08-17';
```

The mailer reads the **real PostgreSQL database** — the same rows the web application shows. It never creates or migrates the database: if PostgreSQL is unreachable, or the database exists but has no LunchOrganizer tables, the mailer prints a plain-language message naming `config/database.json` and exits `1` without sending. Start the web application once to create the schema.

### 6.1 Triggering the email from the website instead

The web application can also run the summary on a timer, which is handy when you want to watch it happen in the app's own log rather than in a separate console. It is **off by default** and should stay off in production — the scheduled task (`INSTALLATION.md` §7) is the real mechanism, because it does not depend on the website being up.

Create `config/email.local.json` (git-ignored) with just the two keys you want to override:

```json
{
  "Email": {
    "EnableInAppScheduler": true,
    "SendTimeLocal": "14:30:00"
  }
}
```

Set `SendTimeLocal` a couple of minutes into the future, restart the web application, and watch its console. You should see the scheduler stay quiet until that minute and then log one of:

```
Scheduled daily summary run completed: …
Scheduled daily summary run was already handled: …
Scheduled daily summary run failed: …
```

With `Recipients` still empty you will get the `failed` line reading *"No recipients configured … the summary was built but not sent"* — note **built**, which means it really did query the database. Delete `email.local.json` afterwards; with the scheduler disabled the app instead logs *"The in-app daily summary scheduler is disabled; DailySummaryHostedService will not run."* at startup.

> This is a **real** run, not a dry run: it reserves the day in `email_log`. Clear the row before re-testing the same date (see the `DELETE` above).

---

## 7. Running the tests

```powershell
dotnet test LunchOrganizer.sln                                   # all 48
dotnet test --filter "FullyQualifiedName~Concurrency" -v n       # the 6 concurrency scenarios, per test
dotnet test --filter "FullyQualifiedName~Unit"                   # rules only, no database
```

The concurrency tests need a **running PostgreSQL**. They create and drop their own `lunchorganizer_test` database and never touch `lunchorganizer`. They read connection details from `config/database.local.json`; if they cannot connect they fail loudly rather than skipping, which is deliberate — a concurrency suite that silently no-ops is worse than none.

A healthy run shows real durations (≈1 s for the ten-parallel-menu-insert test). Millisecond-level timings across the board would mean they are not reaching the database.

---

## 8. Changing behaviour while debugging

All of these are JSON edits — no rebuild. The web app watches the files and reloads them (`reloadOnChange: true`); a page refresh is enough.

Prefer editing the **`.local.json`** variant: it is git-ignored, so your local experiments never end up in a commit.

| To do this | Edit | Setting |
|---|---|---|
| Test the "past 9 o'clock" lock at 10:00 | `config/app.local.json` | `"BookingCutOffLocalTime": "23:00:00"` (or a time already past) |
| Change the price of a day with no override | `config/app.local.json` | `"DefaultLunchPrice"` |
| Force English regardless of browser | `config/app.local.json` | `"DefaultCulture": "en-CH"` |
| Add an admin account | `config/admin-users.local.json` | another entry in `Users` |
| Fire the scheduler inside the web app | `config/email.local.json` | `"EnableInAppScheduler": true`, `"SendTimeLocal"` a minute or two ahead |
| Send through a real SMTP server | `config/email.local.json` | `"Mode": "Smtp"` + host/port/credentials |

Admin passwords may be written either as plain text or as `"sha256:<hex>"`. Both are accepted. Editing `admin-users.json` **revokes an active session**, not just future logins — the cookie is re-validated against the file on every request.

---

## 9. Troubleshooting

**A button or checkbox does nothing.**
The page rendered as static HTML with no interactive circuit. Blazor components need `@rendermode InteractiveServer`, and it cannot be applied to `MainLayout` — the layout receives a `RenderFragment` (`Body`) that is not serializable across the render boundary. It belongs on the leaf components (`Booking.razor`, `AppHeader.razor`, `ToastHost.razor`, `ConfirmDialogHost.razor`). Confirm from the browser's page source that `_framework/blazor.web.js` is referenced and that a `blazor-server-component-state` element is present.

**`Address already in use` / the port is taken.**

```powershell
Get-NetTCPConnection -LocalPort 5226 -State Listen | Select-Object OwningProcess
Stop-Process -Id <pid> -Force
```

**`FileNotFoundException` for `app.json` on startup.**
Configuration is read from `AppContext.BaseDirectory\config`, i.e. **next to the built executable**, not from the repository root. The `.csproj` copies `config/*.json` there on build. If you moved the output by hand, copy `config/` with it.

**`password authentication failed for user "postgres"`.**
The app fell back to `config/database.json`, which contains `changeme` by design. Check that `config/database.local.json` exists and is valid JSON — a trailing comma silently invalidates the file and the override is skipped.

**`permission denied to create database`.**
The configured role lacks `CREATEDB`. Either grant it, or set `"AutoCreateDatabase": false` and provision with `Scripts/create_database.ps1`.

**The cut-off behaves unexpectedly.**
Rules use **server local time**, audit columns use UTC. On this machine local time is CEST (UTC+2), so a row written at 09:00 local is stored as `07:00Z`. That is correct, not a bug.

**The language switch appears twice.**
A fixed bug worth recognising if it returns: `ConfigurationBinder` *appends* to a pre-populated `ICollection<T>` default rather than replacing it, so `SupportedCultures` accumulated duplicates. Handled by a `PostConfigure` de-duplication in `Program.cs`.

**Tests fail with a connection error.**
PostgreSQL is not running (`Get-Service postgresql-x64-16`) or `config/database.local.json` is wrong. The concurrency tests are not designed to skip.

---

## 10. Known gaps as of 13 August 2026

These are real, verified, and outstanding — not hypotheticals.

> **Closed on 13 August 2026:** the mailer not being connected to PostgreSQL; the mailer not reading `*.local.json` overrides; the in-app scheduler never being registered; skipped-day reasons not using `ReasonCode`; delete-versus-deactivate being inferred via `IReportService`; and the `UnitTest1.cs` placeholder. See §6 and the agent summaries.

1. **No SMTP details yet.** `config/email.json` has `Mode: "PickupDirectory"` and an empty `Recipients` list, so a real (non-dry) run reserves the day, builds the summary from real data, then stops with *"No recipients configured"* and logs `Failed`. This is config-only — no code change is needed once the host and recipient list are known.
2. **Two UI flows have never been exercised by a human**, only by build, tests and a server-side prerender: the whole-week *"Apply Menu N"* skip-reason list, and the admin delete-versus-deactivate confirmation. Both were rewritten on 13 August 2026 to use the service's own contracts. They are covered by the pending **V4** walkthrough — see the script in §6 of the agent frontend summary.
3. **Every seeded employee currently has exactly one booking**, so the admin *delete* branch (employee with no history) cannot be reached with the demo data as-is. Register a new employee from the booking page to get one, then try removing them.
4. **The live cut-off countdown does not tick** — it is computed per page load, so the remaining time only refreshes when the page does.
5. **The accent-insensitive search fallback** (for a server without `unaccent`) has no automated test. `unaccent` *is* installed here, so the primary path is the one being exercised.
6. **No automated accessibility or contrast check** has been run. AA was designed for — tokens, focus rings, ARIA roles, keyboard navigation — but not machine-verified.
