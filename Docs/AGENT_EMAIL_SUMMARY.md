# Agent — Email · Execution history

**Scope:** `src/LunchOrganizer.Email/`, `src/LunchOrganizer.Mailer/`, `Scripts/register-mailer-task.ps1`
**Reference:** [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) §8, §11.8
**Rule:** an entry is added **only when the step has been validated**. Steps in `Pending` state are the roadmap, not a claim of work done.

---

## Roadmap

| Step | Description | Gate | Status |
|------|-------------|------|--------|
| E0 | Contracts + fakes received from Phase 0 | — | Pending |
| E1 | `DailySummaryBuilder` — one-query snapshot of the day, grouped per menu, employees alphabetical, menus with zero bookings omitted | — | Pending |
| E2 | HTML + plain-text body renderer, subject `{SubjectPrefix}{dd.MM.yyyy}`, CHF/`fr-CH` formatting, body localized via `Email:Language` (default `fr`, English available) | **V5** | Pending |
| E3 | `PickupDirectoryEmailSender` — writes `.eml` to `./mail-drop` (development default and V5 evidence) | **V5** | Pending |
| E4 | `SmtpEmailSender` — MailKit, anonymous relay and authenticated STARTTLS, both driven by `config/email.json` | — | Pending |
| E5 | `DailySummaryMailService` — reserve the day in `email_log`, skip when empty, send, record outcome, map to exit codes | — | Pending |
| E6 | `LunchOrganizer.Mailer` console app — `--date`, `--dry-run`, `--help`, exit codes 0/1/2/3 | — | Pending |
| E7 | `DailySummaryHostedService` for development, gated by `EnableInAppScheduler` | — | **Validated 13.08.2026** (see E11 — it existed but was never registered) |
| E8 | `Scripts/register-mailer-task.ps1` — Windows Task Scheduler registration, weekdays at `SendTimeLocal` | — | Pending |
| E9 | Idempotency test: two mailer processes started simultaneously → exactly one email (plan §11.11 scenario 5) | **V5** | Covered by `ConcurrencyTests.TwoToFiveParallelMailerBegins_SameDate_ExactlyOneSucceeds`; cross-process path confirmed 13.08.2026 |
| E10 | Mailer wired to the real PostgreSQL database, `*.local.json` overrides loaded, fail-fast preflight instead of bootstrapping | — | **Validated 13.08.2026** |
| E11 | In-app scheduler actually registered in `LunchOrganizer.Web`, and its captive-dependency fixed | — | **Validated 13.08.2026** |

---

## Validated steps

### E11 — In-app scheduler registered, and its captive dependency fixed
- **Validated:** 2026-08-13
- **The bug:** `DailySummaryHostedService` and `AddDailySummaryScheduler()` both existed, but **`LunchOrganizer.Web/Program.cs` never called them** and registered none of the email services they depend on. Setting `Email:EnableInAppScheduler: true` therefore did nothing at all, silently — plan §8.2 promised a behaviour the code could not deliver.
- **The second bug, found while fixing the first:** `DailySummaryHostedService` is a `BackgroundService`, which the host registers as a **singleton**, yet it injected `IDailySummaryMailService` directly. Since that service must be **scoped** in the web app (it reaches the scoped, EF Core-backed repositories), this is a captive dependency and would have thrown at startup under development-time scope validation. Merely registering the scheduler without fixing this would have broken the web application.
- **Implemented:**
  - `DailySummaryHostedService` now takes `IServiceScopeFactory` and creates a **fresh scope per run**, resolving `IDailySummaryMailService` inside it. A per-run scope is deliberate: a long-lived one would hold an EF Core context and its pooled connection open for a day at a time. `ComputeDelayUntilNextRun` is untouched, so `DailySummaryHostedServiceSchedulingTests` still compiles against it.
  - `Web/Program.cs` gained the email pipeline registrations with the **same lifetimes the mailer uses** — builder and mail service scoped, renderer and both senders and the `IEmailSender` mode-switch factory singleton — followed by `AddDailySummaryScheduler()`. `IClock` was not re-registered (`AddLunchOrganizerServices()` already does, via `TryAddSingleton`).
- **Files:** `src/LunchOrganizer.Email/Scheduling/DailySummaryHostedService.cs`, `src/LunchOrganizer.Web/Program.cs`. Also deleted `tests/LunchOrganizer.Tests/UnitTest1.cs` (empty template placeholder) — **the suite is now 48 tests, not 49, and that is correct**.
- **Verified by:** (orchestrator, not agent report)
  - `dotnet build` 0 errors, no new warnings; `dotnet test` **48/48 passing**.
  - **Disabled path:** started the web application with the shipped configuration. It booted cleanly — proving the captive dependency is gone, since scope validation would otherwise have thrown at startup — and logged *"The in-app daily summary scheduler is disabled; DailySummaryHostedService will not run."*
  - **Enabled path:** placed an `email.local.json` override in the build output only (the repository's `config/` was never touched) setting `EnableInAppScheduler: true` and `SendTimeLocal` two minutes ahead. The "disabled" line disappeared — which independently also confirms the web app's `email.local.json` override chain — and at the appointed minute the scheduler fired and logged *"Scheduled daily summary run failed: No recipients configured …; **the summary was built but not sent**."*
  - That wording is the proof that matters: the summary was **built**, meaning the hosted service resolved the scoped mail service from its new scope and queried the real database. It then wrote an `email_log` row with `booking_count = 1`, matching the single real booking for 2026-08-13. The override file and the test row were both removed afterwards; `email_log` is empty again.
- **Open points:** the failure is the empty `Email:Recipients` list, not the scheduler. Nothing further is needed here once SMTP details exist.

### E10 — Mailer connected to the real PostgreSQL database
- **Validated:** 2026-08-13
- **Implemented:**
  - Replaced `InMemoryBookingRepository` + `InMemoryEmailLogRepository` (and the `// TODO(integration)` block) with `builder.Services.AddLunchOrganizerData()` — the same registration `LunchOrganizer.Web` uses. `DemoDataSeeder.cs` deleted.
  - Added the four optional `*.local.json` files to the configuration chain, each immediately after its base file, so `config/database.local.json` overrides the `changeme` password.
  - **Lifetime correction:** `AddLunchOrganizerData()` registers its repositories as **scoped**, so `IDailySummaryBuilder` and `IDailySummaryMailService` moved from `AddSingleton` to `AddScoped`, and the mail service is now resolved from `host.Services.CreateScope()`. A singleton capturing a scoped repository would have thrown at resolve time. `ValidateScopes`/`ValidateOnBuild` are now switched on so any future regression fails at startup rather than at first use.
  - **Preflight, not bootstrap:** the mailer never creates or migrates the database. It checks `CanConnectAsync()` and then `GetAppliedMigrationsAsync()`, and on failure prints a plain-language message naming `config/database.json` and exits `1`. The web application remains the sole owner of schema creation.
- **Files:** `src/LunchOrganizer.Mailer/Program.cs` (edited), `src/LunchOrganizer.Mailer/DemoDataSeeder.cs` (deleted). `src/LunchOrganizer.Email/InMemory/` deliberately left in place — two email test fixtures depend on it.
- **Verified by:** (orchestrator, not agent report)
  - `dotnet build` 0 errors; `dotnet test` **49/49 passing**.
  - The mailer's own EF Core log shows the real query against `bookings`, including the composite-FK join `b.booking_date = m.menu_date` — impossible against the in-memory store.
  - **Content compared row by row against `psql`.** The seeded database holds one booking per date across eight dates; the deleted demo seeder put *seven* bookings on a single date. A dry run for 2026-08-13 produced exactly **one** booking — *Bob Dupont*, Menu 2 — matching the SQL. The rendered description *"Chicken curry with basmati rice"* appears under **Menu 2**, which is where the real database has it; the demo seeder had that same text under **Menu 1**. Name-matching alone would have proved nothing, since both data sets share names.
  - `database.local.json` override confirmed live: `config/database.json` still carries `changeme`, yet the connection succeeded.
  - A date with no bookings (2026-08-14) returned `Skipped`, exit `2`.
  - A real (non-dry) run wrote a row to `email_log`; a second run hit the `ON CONFLICT (summary_date) DO NOTHING` guard and returned `AlreadyHandled`, exit `3`. **This is the first proof of the double-send guard against the real UNIQUE constraint** rather than against the in-memory stub. The test row was deleted afterwards, leaving `email_log` empty.
- **Open points:**
  - `Email:Recipients` is still empty, so a real run reserves the day, builds the summary, then records `Failed` with *"No recipients configured"*. Config-only; blocked on SMTP details.
  - The concurrent form of the guard is already covered by `ConcurrencyTests.TwoToFiveParallelMailerBegins_SameDate_ExactlyOneSucceeds` (four parallel `TryBeginAsync` calls against real PostgreSQL → exactly one winner, one row), which is plan §11.11 scenario 5. What the 13 August run added is the *cross-process* path through the actual `LunchOrganizer.Mailer.exe`.

<!--
Entry template — append one block per validated step:

### E1 — Daily summary builder
- **Validated:** YYYY-MM-DD
- **Implemented:** …
- **Files:** …
- **Verified by:** …
- **Open points:** …
-->

---

## Open decision — delivery mechanism (plan §8.3)

| Option | Status |
|--------|--------|
| A — Corporate SMTP relay, anonymous, port 25 | Awaiting the relay host name from Massimo (needed at V5) |
| B — Authenticated SMTP (service mailbox) | Fallback if no relay exists |
| C — Microsoft Graph `sendMail` | Only if IT requires it; needs an app registration |
| D — Pickup directory (`.eml` files) | **Active default** — development and V5 validation |

Development proceeds on **D**; switching to A or B is a `config/email.json` edit, not a code change.

---

## Non-negotiable constraints for this agent

1. **Reserve before work.** `INSERT … ON CONFLICT (summary_date) DO NOTHING`; zero rows affected means another process owns the day — exit `3` (plan §11.8).
2. **One query for the summary.** Never a loop of per-menu queries that could interleave with a booking write.
3. **Zero bookings → no email**, recorded as `Skipped`. Explicit requirement from the specification.
4. **The subject prefix comes from configuration**, never a literal in code.
5. **Credentials are never logged**, not even at Debug level.
6. **No git commands, ever.**
