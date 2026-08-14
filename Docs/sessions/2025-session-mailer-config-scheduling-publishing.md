# Session Summary — Mailer: Configuration, Scheduling, Testing & Production Publishing

**Repository:** LunchOrganizer (branch `main`, remote `origin` → github.com/fauretto/LunchOrganizer)
**Solution:** `LunchOrganizer.sln`
**Environment:** Visual Studio Enterprise 2026 (18.9.0), PowerShell, .NET 10

---

## Overview

This session was a deep-dive into **`LunchOrganizer.Mailer`** — how it is configured, how
it is scheduled, how to test it (PickupDirectory and production), and how to publish the
solution for deployment. It concluded with the creation of publishing scripts under
`Scripts\`.

---

## 1. Configuration files: `database.json` vs `database.local.json` (and email equivalents)

**Who decides which is used:** the .NET `ConfigurationBuilder`. Both hosts
(`src/LunchOrganizer.Web/Program.cs` and `src/LunchOrganizer.Mailer/Program.cs`) register
both files and **layer** them — it is never "one or the other."

- `database.json` — `optional: false` → **must exist**; the committed base/default.
- `database.local.json` — `optional: true` → loaded **only if present**, read **after** the
  base file, so matching keys **override** (a merge, not a swap). Typically git-ignored,
  per-developer/per-machine.
- The same base + `.local` override pattern applies to `email.json` / `email.local.json`,
  `app.json`, and `admin-users.json`.

The **test project** is different: `tests/LunchOrganizer.Tests/Concurrency/ConcurrencyTestFixture.cs`
reads `config/database.local.json` directly and hardcodes only a separate test database
name so it never collides with the developer's normal `lunchorganizer` database.

---

## 2. What the Mailer is

A **run-once console app** (`src/LunchOrganizer.Mailer/Program.cs`) that sends — or previews —
the daily lunch-booking summary email.

```
LunchOrganizer.Mailer [--date yyyy-MM-dd] [--dry-run]
LunchOrganizer.Mailer --help
```

Exit codes: `0` Sent · `1` Failed · `2` Skipped (no bookings) · `3` AlreadyHandled.

It **never creates the database schema** — it fails fast if PostgreSQL is unreachable or
migrations are missing. The web app owns schema creation, so the web app must be started
once first.

A verified dry-run during the session:
```
[DRY RUN] 2025-01-15 — Skipped: No bookings for 2025-01-15; nothing sent. (bookings: 0, exit code 2)
```

---

## 3. Which config file holds the email parameters, and WHERE

- **Source (edited in repo):** `config/email.json` (+ optional `config/email.local.json`).
- **Runtime (what the exe reads):** a `config` folder **next to the executable** —
  `AppContext.BaseDirectory\config` (see `Program.cs`). Each project's `.csproj` copies
  `../../config/*.json` into its own build output (`CopyToOutputDirectory="PreserveNewest"`).

Effective locations:
- Web: `src\LunchOrganizer.Web\bin\Debug\net10.0\config\`
- Mailer: `src\LunchOrganizer.Mailer\bin\Debug\net10.0\config\`
- Production: `<exe folder>\config\`

**Important:** the Web app and Mailer each read the `config` folder next to their **own**
exe — they are **separate physical copies** originating from the same repo files. Edit the
repo copies and rebuild to keep both in sync.

Key `Email` parameters (bound to `EmailOptions`, consumed by `SmtpEmailSender`):
`Mode` (Smtp | PickupDirectory), `SmtpHost`, `SmtpPort`, `UseStartTls`, `Username`,
`Password`, `SenderName`, `SenderAddress`, `Recipients`, `SubjectPrefix`,
`SubjectDateFormat`, `Language`, `PickupDirectory`.

---

## 4. Scheduling: two independent mechanisms

The Mailer has **no internal timer/loop**. "Every weekday at 09:01" comes from outside.

| | In-app scheduler (dev convenience) | Windows Task Scheduler (production) |
|---|---|---|
| What | `DailySummaryHostedService` (`BackgroundService`) inside the **Web app** | `LunchOrganizer.Mailer.exe` run once a day by the OS |
| File | `src/LunchOrganizer.Email/Scheduling/DailySummaryHostedService.cs` | `Scripts/register-mailer-task.ps1` |
| Enabled by | `Email:EnableInAppScheduler = true` | Always (registered as an OS task) |
| Default | **`false`** (self-gates off and logs "disabled") | The intended production path |
| Timing source | `Email:SendTimeLocal` (DST-safe, working days only) | `-TimeLocal` on the registration script |

- **Who launches the exe daily in production:** the Windows Task Scheduler service, per a job
  created by an **admin running `register-mailer-task.ps1` once** at deployment time
  (elevated). Nothing registers it automatically — not the web app, not the build.
- `register-mailer-task.ps1` controls **WHEN / as WHOM** the exe runs (weekly trigger,
  Mon–Fri, default 09:01, SYSTEM or a service account). It never touches `email.json`.
- `email.json` controls **HOW** the email is sent. Timing (`SendTimeLocal`,
  `EnableInAppScheduler`) is only relevant to the **in-app** path.

**Double-send guard:** even if both mechanisms fired for the same day, an atomic `email_log`
reservation keyed on `summary_date` (`INSERT … ON CONFLICT (summary_date) DO NOTHING`)
guarantees exactly one send; the loser gets `AlreadyHandled` (exit code 3).

---

## 5. PickupDirectory testing (walked through live)

Plan: run the web server, book lunches, let the scheduler emit an `.eml` into `mail-drop`.

Gotchas discovered and resolved during the session:
1. **Scheduler off by default** — must set `EnableInAppScheduler: true` (in
   `email.local.json`) for the in-app path; otherwise it logs "disabled" and never fires.
   For a quick test, set `SendTimeLocal` a couple of minutes into the future instead of
   waiting for 09:00.
2. **`.eml` location** — `PickupDirectory` is `./mail-drop` relative to the **exe**, i.e.
   `src\LunchOrganizer.Web\bin\Debug\net10.0\mail-drop\`, not a repo-root folder.
3. **`AlreadyHandled`** — got *"already reserved or sent by another process"* for
   `2026-08-13`; this is the double-send guard working. To re-test the same day:
   `DELETE FROM email_log WHERE summary_date = '2026-08-13';` (or `--dry-run`, which
   bypasses the reservation).
4. **No recipients** — got *"No recipients configured … the summary was built but not sent."*
   `Recipients` must be non-empty even in PickupDirectory mode (it forms the `To:` line).
   Adding `"Recipients": [ "test@example.com" ]` fixed it and the `.eml` was produced.

Reminder for cleanup: revert throwaway test values (`Recipients` back to `[]`,
`EnableInAppScheduler` back to `false`) before committing the base `email.json`.

---

## 6. Production publishing

**Do NOT** copy the whole solution tree or the raw `bin\Release` folder. Use
**`dotnet publish`** per app. Two independent deployables:

- `LunchOrganizer.Web` — the Blazor Server website
- `LunchOrganizer.Mailer` — the daily email job (run by Task Scheduler)

Class libraries (Domain, Data, Services, Email) are pulled in automatically.

Production sequence:
1. Publish both apps (Release).
2. Copy the **contents** of each publish folder to its own server directory
   (e.g. `C:\Apps\LunchOrganizer.Web`, `C:\Apps\LunchOrganizer.Mailer`).
3. Edit each app's `config\database.json` / `email.json` (or `*.local.json`) for
   production; set `AutoCreateDatabase: false` for the Mailer.
4. Start the Web app once to apply EF Core migrations / create the schema.
5. Register the mailer task pointing at the deployed exe.

Runtime prerequisite: framework-dependent publish needs the .NET 10 ASP.NET Core Runtime
(Hosting Bundle) on the server; alternatively publish `--self-contained` to bundle the
runtime.

---

## Files Created / Changed This Session

| File | Purpose |
|------|---------|
| `Scripts/publish-production.cmd` | **Self-contained batch publisher** (final form). Runs `dotnet publish -c Release` directly for Web then Mailer — no PowerShell involved. Parses `-OutputRoot` (default `<repo>\publish`), `-Configuration` (default Release), `-SelfContained`, `-Runtime` (default win-x64), `-Clean`. Verified working via `cmd /c`. |
| `Docs/INSTALLATIONFORDUMMIES.md` | Beginner-friendly server installation guide (see section below). |
| `src/LunchOrganizer.Web/Components/Admin/MenusAndPricesPanel.razor` | **Bug fix** — added missing `@` to `Currency="@AppOptionsMonitor.CurrentValue.Currency"` (was rendering the literal expression text next to the price). |
| `README.md` (repo root) | New top-level README with a **Deployment** pointer to the installation guide. |

**Removed:** `Scripts/publish-production.ps1` — the `.cmd` no longer wraps a `.ps1`; it was
deleted to avoid confusion.

### Why the `.cmd` was rewritten (execution-policy discovery)
The original `.cmd` wrapped `publish-production.ps1` with `-ExecutionPolicy Bypass`. On this
machine that **failed**: PowerShell execution policy is enforced by **Group Policy**
(`AllSigned`-style), which the command-line `-ExecutionPolicy Bypass` switch **cannot
override** (it demands a digitally signed script). The fix was to make the `.cmd` fully
self-contained — calling `dotnet publish` directly — so it works under locked-down policy.
Note: `register-mailer-task.ps1` still requires PowerShell (Task Scheduler cmdlets) and is
subject to the same policy on locked-down machines.

Default output (no `-OutputRoot`):
```
D:\Data\GitPerso\LunchOrganizer\publish\Web\
D:\Data\GitPerso\LunchOrganizer\publish\Mailer\
```

---

## Server Installation Guide (`Docs/INSTALLATIONFORDUMMIES.md`)

A step-by-step, beginner-friendly deployment guide was written and saved. Key points:

- **Concept:** a published ASP.NET Core app is not static HTML dropped in a web root — it
  must be **hosted** by a process. The app folder can live anywhere (e.g.
  `C:\Apps\LunchOrganizer.Web`); a host makes it browser-reachable.
- **Prerequisites:** PostgreSQL; **.NET 10 Hosting Bundle**; restart IIS after installing it.
- **Copy** the two publish folders to separate server directories.
- **Configure** `config\database.json` (Web `AutoCreateDatabase=true`, Mailer `false`),
  `email.json`, `admin-users.json`.
- **Option A — IIS (recommended):** App Pool = "No Managed Code", site → physical path =
  Web folder, bindings (HTTP/HTTPS), folder permissions for `IIS AppPool\...`. The app
  forces HTTPS (`UseHttpsRedirection`), so a valid certificate is needed.
- **Option B — Windows Service (Kestrel, no IIS):** create a service with `sc.exe` pointing
  at `dotnet.exe <dll>` (or the exe if self-contained), set `ASPNETCORE_URLS` /
  `ASPNETCORE_ENVIRONMENT=Production`, grant folder access, open the firewall port. Includes
  start/stop/delete and troubleshooting.
- **Mailer (both options):** test with `--dry-run`, then register the Task Scheduler job via
  `register-mailer-task.ps1`.
- App facts referenced: `web.config` hosting model `inprocess`; DB bootstrap on startup;
  `app.json` currency `CHF` / culture `fr-CH` / cut-off `09:00`; admin login `/admin/login`.

---

## Key Reference Files

- `src/LunchOrganizer.Mailer/Program.cs` — CLI args, config load order, DB preflight, run-once flow
- `src/LunchOrganizer.Mailer/LunchOrganizer.Mailer.csproj` — copies `config/*.json` to output
- `src/LunchOrganizer.Domain/Configuration/EmailOptions.cs` — email parameter model
- `src/LunchOrganizer.Domain/Configuration/DatabaseOptions.cs` — database parameter model
- `src/LunchOrganizer.Email/Sending/SmtpEmailSender.cs` — how SMTP settings are used
- `src/LunchOrganizer.Email/Scheduling/DailySummaryHostedService.cs` — in-app scheduler
- `src/LunchOrganizer.Web/Program.cs` — registers the (default-off) in-app scheduler
- `Scripts/register-mailer-task.ps1` — production Windows Task Scheduler registration

---

## Open / Optional Follow-ups

- Add `publish/` to `.gitignore` so build output isn't committed.
- Optionally add a `register-mailer-task.cmd` wrapper (elevated) for the server side — note
  Task Scheduler cmdlets still require PowerShell, so a pure `.cmd` would call `schtasks.exe`.
- Revert throwaway PickupDirectory test values in `config/email.json` before committing.
