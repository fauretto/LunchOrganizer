# Agent — Backend · Execution history

**Scope:** `src/LunchOrganizer.Domain/`, `src/LunchOrganizer.Data/`, `src/LunchOrganizer.Services/`, `tests/LunchOrganizer.Tests/`, `Scripts/`
**Reference:** [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) §3, §4, §5, §11
**Rule:** an entry is added **only when the step has been validated**. Steps in `Pending` state are the roadmap, not a claim of work done.

---

## Roadmap

| Step | Description | Gate | Status |
|------|-------------|------|--------|
| B0 | Contracts received from Phase 0 (entities, repository interfaces, DTOs) | — | Pending |
| B1 | EF Core model: Fluent API mapping, `citext`, composite FK `(menu_id, booking_date)`, unique constraints, `xmin` concurrency tokens | **V2** | Pending |
| B2 | `Docs/DATABASE_SCHEMA.md` written and submitted for validation | **V2** | Pending |
| B3 | Initial migration generated and applied to a scratch database | after V2 | Pending |
| B4 | `Scripts/create_database.sql` + `create_database.ps1` + `Scripts/README.md` — **only after V2** | after V2 | Pending |
| B5 | Repository implementations — short-lived contexts from `IDbContextFactory`, atomic upserts, get-or-create, unique-violation retries | — | Pending |
| B6 | `DatabaseBootstrapper` — create-if-missing, `42P04` tolerance, advisory-locked migration | — | Pending |
| B7 | Services: `IWeekService`, `IPricingService`, `IEmployeeService`, `IMenuService`, `IBookingService`, `IReportService` | — | Pending |
| B8 | Unit tests with a faked `IClock` — cut-off, past days, deletion guards, price snapshot, whole-week partial application | **V3** | Pending |
| B9 | `ConcurrencyTests` against real PostgreSQL — the six scenarios of plan §11.11 | **V3** | Pending |

---

## Carry-forward notes for run 2

Recorded here so they survive a context compaction.

1. **`IBookingRepository.GetForDateAsync` must eager-load `Booking.Employee` and `Booking.Menu`** (`.Include(...)`). The email agent's `DailySummaryBuilder` depends on it: plan §11.8 requires the daily summary to be read in **one** query, and it needs employee names and menu descriptions from that single call. Raised by the email agent as an assumption not stated verbatim in the interface's XML doc — make it true, and document it there. The same applies to `GetForEmployeeBetweenAsync` for the admin report.
2. **`src/LunchOrganizer.Email/InMemory/`** holds temporary in-memory repository stand-ins the email agent wrote to develop against. At Phase 2 integration they are replaced by the real repositories in `Mailer/Program.cs`; confirm they are no longer resolved anywhere in production paths.
3. **V2 judgement calls** in `Docs/DATABASE_SCHEMA.md` §5 were accepted as-is unless the user says otherwise at validation.
4. **Contract gaps found by the frontend agent** — fix in the real services (the frozen interfaces may be extended in run 2, with the frontend notified):
   - `IEmployeeService.DeleteOrDeactivateAsync` has **no outcome discriminator**, so the UI cannot tell whether it deleted or deactivated; it currently infers this via `IReportService`. Add an explicit result, plus `HasBookingsAsync(int employeeId)`.
   - `WeekIdentifier.Label` is a hard-coded English string in the wrong format. Either drop it or make it culture-driven — the UI ignores it today and builds its own heading from `.Monday`. Prefer dropping it: a formatted label has no business in a DTO in a bilingual app.
   - `WeekBookingResultDto.SkippedDayDto.Reason` is raw English text. It must carry an **`ErrorCodes` value plus `MessageArgs`**, not a sentence — same rule as `OperationResult` (plan §6.7).
   - Real services must emit the documented `ErrorCodes` constants. The fakes use ad-hoc strings (`"LOCKED"`, `"NOT_FOUND"`) which the UI can only render as a generic message.

---

## Validated steps

### B0–B3 — Database model · **V2 PASSED**

- **Validated:** 2026-08-12 by Massimo Fauro ("the schema is validated, go ahead").
- **Implemented:**
  - EF Core Fluent API mapping split into five `IEntityTypeConfiguration<T>` classes; `OnModelCreating` applies them and declares the `citext` extension.
  - Migration `20260812075601_InitialCreate` covering all five tables.
  - `LunchOrganizerDbContextFactory` — design-time only, so `dotnet ef` can run without real credentials.
  - `.config/dotnet-tools.json` pinning `dotnet-ef` 10.0.11.
- **Files:** `src/LunchOrganizer.Data/Configurations/*.cs`, `LunchOrganizerDbContext.cs`, `LunchOrganizerDbContextFactory.cs`, `Migrations/20260812075601_InitialCreate*.cs`, `Docs/DATABASE_SCHEMA.md`, `.config/dotnet-tools.json`.
- **Verified by:** orchestrator, independently of the agent's report — read the generated DDL and the model snapshot rather than trusting the summary. Confirmed present: composite FK `bookings(menu_id, booking_date) → menus(id, menu_date) ON DELETE RESTRICT` with its backing alternate key; `UNIQUE (employee_id, booking_date)`; `citext` unique `full_name`; PK on `email_log.summary_date`; three CHECK constraints; four named indexes; `now()` defaults. `xmin` confirmed mapped as `HasColumnName("xmin")`, type `xid`, `ValueGeneratedOnAddOrUpdate`, and absent from every `CREATE TABLE` — i.e. the PostgreSQL system column, costing no storage.
- **Judgement calls accepted at validation:** unique constraints double as the required indexes; EF's automatic `IX_bookings_menu_id_booking_date` kept; `IsRowVersion()` over `UseXminAsConcurrencyToken()`; design-time-only connection string; tool manifest location. (`Docs/DATABASE_SCHEMA.md` §5.)
- **Open points:** none. `Scripts/create_database.sql` unblocked by this validation and produced in run 2.

### B4–B9 — Repositories, services, bootstrapper, scripts, tests · **V3 PASSED**

- **Validated:** 2026-08-12, by orchestrator verification against the live database (see below).
- **Implemented:** five repositories (each opening its own short-lived `DbContext` from `IDbContextFactory`); atomic `INSERT … ON CONFLICT … DO UPDATE … RETURNING` upserts for bookings and prices; get-or-create for employees; `23505` retry on menu numbering; `23503` translated into `MenuHasBookings` / `EmployeeHasBookings`; `TryBeginAsync` reservation for the mailer; `DatabaseBootstrapper` (create-if-absent, `42P04` tolerated, migrations under `pg_advisory_lock` 872615001, opportunistic `unaccent` install); `DevelopmentSeeder`; all seven services with rules enforced against `IClock` immediately before each write; `Scripts/create_database.{sql,ps1}` + README; 24 backend tests.
- **Verified by orchestrator, independently of the agent's report:**
  - `dotnet test` → **49/49 passed**.
  - Concurrency tests confirmed to genuinely exercise PostgreSQL, not stub out — per-test durations of 995 ms (10 parallel menu adds) and 423 ms (20 parallel registrations) prove real round trips. All six plan §11.11 scenarios pass.
  - Live database inspected via `psql`: five tables + `__EFMigrationsHistory`; `full_name` is `citext`; composite FK and all unique indexes present; seed = 8 employees / 45 menus / 8 bookings / 3 prices.
  - **Guarantees attacked directly in SQL and all five rejected by PostgreSQL:** booking a menu on a foreign date; a second lunch for the same employee and day; `ALICE MARTIN` alongside `Alice Martin`; deleting an employee who has bookings; creating "Menu 0".
  - Extensions installed: `citext`, `unaccent`, `plpgsql`. Accent-insensitive search confirmed live — `%chloe%` matches `Chloé Bernard`.
  - Web app runs against real PostgreSQL and serves the booking page in French.
- **Deviations accepted:** `IBookingRepository.UpsertManyAsync` added as a C# **default interface method** rather than abstract, because making it abstract would break `LunchOrganizer.Email`'s in-memory repository, which the backend agent was forbidden to edit. Not Web-facing. `FakeEmployeeService` gained two method bodies to keep compiling after the additive `IEmployeeService` changes.
- **Open points:** `UnitTest1.cs` template placeholder still present and should be deleted; the accent-insensitive fallback path (when `unaccent` is unavailable) has no automated test; the backend agent did not line-by-line review every generated file, relying on build/test/live verification.

### Environment confirmed — 2026-08-12

PostgreSQL **16.14** at `localhost:5432`. Role `postgres` is superuser **with `CREATEDB`**, so the auto-create path is viable rather than needing the offline script. `citext` present in `pg_available_extensions`. Database `lunchorganizer` did not exist at validation time, so bootstrap creation is genuinely exercised on first run. **`psql.exe` is at `D:\PostgreSQL\bin\psql.exe` and is not on `PATH`** — the installation guide must say so. Credentials live in `config/database.local.json` (git-ignored); `config/database.json` keeps the `changeme` placeholder.

<!--
Entry template — append one block per validated step:

### B1 — EF Core model
- **Validated:** YYYY-MM-DD
- **Implemented:** …
- **Files:** …
- **Verified by:** …
- **Open points:** …
-->

---

## Non-negotiable constraints for this agent

1. **`IDbContextFactory` only.** A `DbContext` injected directly into a repository, service, or ViewModel is a defect, not a style choice (plan §11.1).
2. **No business rule outside the service layer.** Repositories persist; they do not decide.
3. **`Scripts/create_database.sql` is not written before V2 validation.** Explicit requirement from the specification.
4. **Money is `decimal`, dates are `DateOnly`, audit columns are UTC.**
5. **No git commands, ever.**
