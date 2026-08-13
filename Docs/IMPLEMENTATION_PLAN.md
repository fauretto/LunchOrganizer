# LunchOrganizer — Implementation Plan

> **STATUS: VALIDATED — 12 August 2026.**
> Validated by Massimo Fauro, conditional on the addition of §11 (concurrency), now included.
> This is the reference plan. Any later change is recorded in the revision history below.

- **Author:** Claude (orchestrator)
- **Date:** 2026-08-12

### Revision history

| Rev | Date | Change |
|-----|------|--------|
| 1 | 2026-08-12 | Initial plan submitted for validation. |
| 2 | 2026-08-12 | Target framework fixed to `net10.0` (§1.1); Swiss/CHF locale fixed (§1.2). |
| 3 | 2026-08-12 | **§11 Concurrency and multi-user correctness** added at your request; risk table, schema and repository sections updated accordingly. Plan validated. |
| 4 | 2026-08-12 | **§10.7 Installation procedure** added at your request — `Docs/INSTALLATION.md`, written after V6, covering target-machine prerequisites and a setting-by-setting configuration guide. |
| 5 | 2026-08-12 | **§6.6 Designing for a non-technical user** added at your request; it governs all of §6 and is the script for the V4 review. Week labels, error wording and the V4 checkpoint updated accordingly. |
| 6 | 2026-08-12 | **§6.7 Bilingual interface** — French default with an English toggle, `.resx` localization, culture cookie. `OperationResult.ErrorCode` becomes load-bearing; §1.2, §8.1 and `app.json` updated. |
- **Project root:** `C:\Projects_Git\Data\GitPerso\LunchOrganizer` (also reachable as `D:\Data\GitPerso\LunchOrganizer`)
- **Source specification:** `LaunchOrganizer.txt`
- **Clarifications:** `AnswersToClaudeBeforeImplementation.txt`

---

## 1. Confirmed decisions

| # | Topic | Decision |
|---|-------|----------|
| 1 | Menu granularity | Menus are attached to a **specific date**. `Menu 1` on Monday is a different row from `Menu 1` on Tuesday. The UI offers "copy this description to the whole week" for convenience. |
| 2 | Price ownership | Price is **per day**, identical for every menu of that day, edited on the **admin page**. A default price from configuration applies when a day has no explicit price. |
| 3 | Price history | Each booking stores a **price snapshot** taken when the booking is created. Later price edits never rewrite past bookings. The admin report sums snapshots. |
| 4 | Menu management | **Admin-only.** The booking page is read-only regarding menus (it displays them); creation, description editing and deletion live on the admin page. |
| 5 | Authentication | Admin pages are protected by a **login form**. Users and passwords live in a plain JSON file, hand-editable with Notepad++. Low-security by design. The booking page stays anonymous. |
| 6 | Email delivery | Mechanism to be chosen during implementation — options are proposed in §8.3. The code is written against an `IEmailSender` abstraction so the choice is a configuration change, not a rewrite. |
| 7 | Scheduling | **Separate console executable driven by Windows Task Scheduler** (survives web-app restarts), plus an optional in-app hosted service for development. Both share the same code. |
| 8 | Working days | Monday–Friday, no holiday calendar. **Zero bookings → no email is sent.** |
| 9 | PostgreSQL | Already installed on this laptop (PostgreSQL 16, service `postgresql-x64-16`). Credentials to be provided when testing starts. |
| 10 | Platform | Blazor Web App with **Server interactivity** on **.NET 10 (LTS)**, direct DB access from components, no separate API layer, background service hosted in-process for dev. |
| 11 | Context management | Compaction happens at **explicit safe checkpoints** only (all agents in a validated intermediate state). `Docs/` progress files are detailed enough that no state is lost across a compaction. |

### 1.1 Target framework — resolved

**`net10.0` (.NET 10 LTS).** .NET 9 is a Standard-Term Support release that left Microsoft support in May 2026, so it no longer receives security patches; .NET 10 is supported until November 2028 and its SDK (`10.0.302`) is already installed on this laptop. The Blazor Web App / Server-interactivity model is identical. `global.json` pins the SDK to the `10.0.3xx` band for reproducible builds.

### 1.2 Locale and currency — resolved

**Swiss formatting, CHF.**

| Aspect | Value |
|--------|-------|
| Formatting culture | `fr-CH` (default) / `en-CH` (English toggle) — both produce identical Swiss dates and numbers |
| Currency | `CHF`, displayed as `CHF 12.50` |
| Number format | Apostrophe thousands separator, dot decimal — `1'234.50` |
| Date format | `dd.MM.yyyy` — `17.08.2026` |
| Week | Monday-first, ISO-8601 |
| UI language | **French (default) with an English toggle** — see §6.7 |

Dates and numbers are formatted identically in both languages; only the words change. `de-CH` would also produce the same output if a German toggle is ever wanted.

---

## 2. Solution structure

```
LunchOrganizer/
├─ LunchOrganizer.sln
├─ global.json                      # pins the SDK band
├─ src/
│  ├─ LunchOrganizer.Domain/        # entities, enums, business-rule constants — no dependencies
│  ├─ LunchOrganizer.Data/          # EF Core DbContext, repositories, migrations, DB bootstrapper
│  ├─ LunchOrganizer.Services/      # business services, DTOs, service interfaces
│  ├─ LunchOrganizer.Email/         # summary builder + IEmailSender implementations (class library)
│  ├─ LunchOrganizer.Web/           # Blazor Web App: Views (.razor), ViewModels, theme, admin auth
│  └─ LunchOrganizer.Mailer/        # console app invoked by Windows Task Scheduler
├─ tests/
│  └─ LunchOrganizer.Tests/         # xUnit: business rules, summary builder, repositories
├─ config/                          # JSON configuration, hand-editable (see §9)
├─ Scripts/                         # offline DB creation script (written after schema validation)
└─ Docs/                            # this plan, agent summaries, debug guide
```

**Dependency direction:** `Domain ← Data ← Services ← {Web, Email}`, `Email ← Mailer`. Nothing depends on `Web`.

### 2.1 MVVM mapping

| MVVM role | Where | Notes |
|-----------|-------|-------|
| View | `LunchOrganizer.Web/Components/Pages/*.razor`, `Components/Shared/*.razor` | Markup and binding only. No business logic, no direct repository calls. |
| ViewModel | `LunchOrganizer.Web/ViewModels/*.cs` | Plain C# classes, `INotifyPropertyChanged`, registered **scoped**. Hold page state, validation messages, busy flags, and call services. |
| Service | `LunchOrganizer.Services/*.cs` | Business rules (cut-off, deletion guards, price resolution, week computation). The only layer that knows the rules. |
| Repository | `LunchOrganizer.Data/Repositories/*.cs` | EF Core persistence. No business rules. |
| Model | `LunchOrganizer.Domain/Entities/*.cs` | Entities + value objects. |

Views obtain their ViewModel by injection; the ViewModel raises `PropertyChanged`, and a small `ViewModelComponentBase` calls `StateHasChanged` on it. This keeps Blazor idiomatic while honouring the MVVM separation you asked for.

---

## 3. Database design

**Database name:** `lunchorganizer` · **Schema:** `public` · **Collation-sensitive lookups** handled via `citext` for employee names.

Every mutable table additionally exposes PostgreSQL's system column **`xmin`** as an EF Core optimistic-concurrency token. It is a system column, so it appears in no `CREATE TABLE` statement and costs nothing — see §11.3.

### 3.1 Tables

**`employees`**

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `integer` | PK, generated always as identity |
| `full_name` | `citext` | NOT NULL, UNIQUE (case-insensitive) |
| `email` | `text` | NULL |
| `is_active` | `boolean` | NOT NULL DEFAULT true |
| `created_at_utc` | `timestamptz` | NOT NULL DEFAULT now() |
| `updated_at_utc` | `timestamptz` | NOT NULL DEFAULT now() |

Index: `ix_employees_full_name` on `full_name` (supports the autocomplete prefix/contains search).

**`daily_prices`**

| Column | Type | Constraints |
|--------|------|-------------|
| `price_date` | `date` | PK |
| `price` | `numeric(10,2)` | NOT NULL, CHECK (`price >= 0`) |
| `created_at_utc` / `updated_at_utc` | `timestamptz` | NOT NULL |

A day with no row uses `App:DefaultLunchPrice` from configuration.

**`menus`**

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `integer` | PK identity |
| `menu_date` | `date` | NOT NULL |
| `menu_number` | `integer` | NOT NULL, CHECK (`menu_number >= 1`) — the label "Menu 1/2/3…" and the display order |
| `description` | `text` | NULL (content is optional, per the spec) |
| `created_at_utc` / `updated_at_utc` | `timestamptz` | NOT NULL |

Constraints: `UNIQUE (menu_date, menu_number)`, plus `UNIQUE (id, menu_date)` — the second one exists solely to support the composite foreign key below.
Index: `ix_menus_menu_date`.

**`bookings`**

| Column | Type | Constraints |
|--------|------|-------------|
| `id` | `integer` | PK identity |
| `employee_id` | `integer` | NOT NULL, FK → `employees(id)` ON DELETE RESTRICT |
| `booking_date` | `date` | NOT NULL |
| `menu_id` | `integer` | NOT NULL |
| `price_snapshot` | `numeric(10,2)` | NOT NULL, CHECK (`>= 0`) |
| `created_at_utc` / `updated_at_utc` | `timestamptz` | NOT NULL |

Constraints:
- `UNIQUE (employee_id, booking_date)` — **one menu per employee per day**, enforced by the database, not just by code.
- `FOREIGN KEY (menu_id, booking_date) REFERENCES menus(id, menu_date) ON DELETE RESTRICT` — a composite FK that makes it **structurally impossible** to book Tuesday's menu on a Monday. This is the key integrity guarantee of the model.

Indexes: `ix_bookings_booking_date` (daily email + admin report), `ix_bookings_employee_date`.

**`email_log`**

| Column | Type | Constraints |
|--------|------|-------------|
| `summary_date` | `date` | PK — one row per day, guarantees idempotency |
| `sent_at_utc` | `timestamptz` | NOT NULL |
| `status` | `text` | NOT NULL — `Sent` / `Skipped` / `Failed` |
| `recipients` | `text` | NULL |
| `booking_count` | `integer` | NOT NULL |
| `error_message` | `text` | NULL |

The PK makes a double send impossible if the task and the dev hosted service both fire.

### 3.2 Entity-relationship

```
employees 1 ──< bookings >── 1 menus
                   │              │
                   └─ booking_date ┘   (composite FK: menu_id + booking_date)

daily_prices ─ (by date, no FK) ─ menus / bookings
email_log     (standalone, one row per day)
```

`daily_prices` deliberately has no foreign key to `menus`: a price can be defined for a day that has no menus yet.

### 3.3 Deletion rules (enforced in services, backed by `ON DELETE RESTRICT`)

- **Menu:** deletable only if `menu_date >= today` **and** it has zero bookings. Both conditions are checked in the service with a clear message explaining which one failed.
- **Employee:** hard-deletable only if the employee has **zero bookings, ever**. Otherwise the admin page offers **deactivation** (`is_active = false`) — the employee disappears from autocomplete but their history and reports stay intact. This is the practical reading of "delete only if he never booked a lunch".
- **Day price:** editable at any time; edits never touch existing `price_snapshot` values.

### 3.4 Database creation at startup

On web-app startup, `DatabaseBootstrapper`:
1. reads `config/database.json`;
2. connects to the **maintenance database** (`postgres`) with the configured credentials;
3. queries `pg_database` for `lunchorganizer` and issues `CREATE DATABASE` if it is absent;
4. connects to `lunchorganizer`, ensures the `citext` extension, and applies **EF Core migrations** (`Database.Migrate()`);
5. optionally seeds demo data when `Seed: true` (development only).

Steps 2–3 require a role with `CREATEDB`. If the configured user lacks it, startup fails with an explicit, actionable message rather than a stack trace. The whole behaviour is switchable with `AutoCreateDatabase: true|false`.

### 3.5 Offline script (`Scripts/`) — produced **after** you validate the schema

- `Scripts/create_database.sql` — idempotent DDL: `CREATE DATABASE`, extension, tables, constraints, indexes, and the EF `__EFMigrationsHistory` row so the app does not try to re-apply migration 1.
- `Scripts/create_database.ps1` — thin wrapper locating `psql.exe` (it is not on PATH here), prompting for host/user/password, and running the SQL.
- `Scripts/README.md` — usage.

---

## 4. Business rules (single source of truth: `BookingRulesService`)

| Rule | Definition |
|------|------------|
| **Week** | ISO-8601 week, Monday-first, working days Monday–Friday. Identified by `(IsoYear, IsoWeek)`. |
| **Selectable week** | Current ISO week or any future one. Past weeks are rejected by the service, not just hidden by the UI. |
| **Editable day** | `date > today` → editable. `date == today` → editable **only if** local time `< 09:00`. `date < today` → read-only. |
| **Cut-off time** | `09:00` **server local time**, value read from configuration (`App:BookingCutOffLocalTime`) so it can be changed without recompiling. |
| **One booking per day** | Choosing a different menu for a day the employee already booked is an *update*, never a second row. |
| **Price resolution** | `daily_prices[date]` if present, otherwise `App:DefaultLunchPrice`. Resolved at booking time and frozen into `price_snapshot`. |
| **Whole-week booking** | "Book the whole week with Menu N" applies only to the days that are still editable and that actually have a `Menu N`; the result screen reports exactly which days were applied and which were skipped, and why. |

Every rule is unit-tested with a fixed, injectable clock (`IClock`) — no test depends on the real time of day.

---

## 5. Backend — services and repositories

### 5.1 Repositories (`LunchOrganizer.Data`)

| Interface | Key members |
|-----------|-------------|
| `IEmployeeRepository` | `SearchByNameAsync(fragment, take)`, `GetByIdAsync`, `GetByNameAsync`, `GetAllAsync(includeInactive)`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `HasAnyBookingAsync` |
| `IMenuRepository` | `GetByDateAsync(date)`, `GetByWeekAsync(monday)`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `HasBookingsAsync(menuId)`, `GetNextMenuNumberAsync(date)` |
| `IBookingRepository` | `GetForEmployeeAndWeekAsync`, `GetForEmployeeAndDateAsync`, `GetForDateAsync(date)`, `GetForEmployeeBetweenAsync(from, to)`, `UpsertAsync`, `DeleteAsync` |
| `IDailyPriceRepository` | `GetAsync(date)`, `GetRangeAsync(from, to)`, `UpsertAsync` |
| `IEmailLogRepository` | `TryBeginAsync(date)`, `CompleteAsync(date, status, …)`, `GetAsync(date)` |

All methods are async and take a `CancellationToken`. Repositories return **entities**; services return **DTOs**. Every repository method opens and disposes its **own short-lived `DbContext`** obtained from `IDbContextFactory` — see §11.1, which is a hard architectural rule, not a preference.

### 5.2 Services (`LunchOrganizer.Services`)

| Service | Responsibility |
|---------|----------------|
| `IBookingService` | Load a week view for an employee, book/modify/cancel a single day, book a whole week, enforce cut-off and past-day rules. Returns `OperationResult<T>` carrying success + user-facing message rather than throwing for expected failures. |
| `IMenuService` | Weekly menu CRUD, "copy description to whole week", deletion guards, menu numbering. |
| `IEmployeeService` | Autocomplete search, CRUD, delete/deactivate guards. |
| `IPricingService` | Resolve the effective price for a date, edit day prices, bulk-set a week. |
| `IReportService` | Admin report: bookings for an employee (or all employees) between two dates, with per-line price and period total. |
| `IWeekService` | ISO week ↔ dates conversion, week labels ("W34 · 17–21 Aug 2026"), working-day enumeration. |
| `IClock` | `Now`, `Today` — injected everywhere, faked in tests. |

### 5.3 Validation checkpoint **V2 — schema**

Before any repository code is written, the backend agent produces `Docs/DATABASE_SCHEMA.md` (tables, constraints, ER diagram, sample queries) for your approval. **`Scripts/create_database.sql` is generated only after you approve it**, exactly as you required.

---

## 6. Frontend — pages and components

> **Read §6.6 first.** The audience is office employees booking lunch in fifteen seconds, not engineers. §6.6 governs every decision in this section, and it overrides any convention that would be natural in a developer-facing tool.

### 6.1 Route map

| Route | Page | Access |
|-------|------|--------|
| `/` | **Booking** | Anonymous |
| `/admin/login` | Login | Anonymous |
| `/admin` | Admin shell → *Report* / *Employees* / *Menus & Prices* tabs | Authenticated |

### 6.2 Booking page

Layout, top to bottom:

1. **Week selector** — two large buttons, **This week** and **Next week**, cover the overwhelming majority of use; a discreet arrow and date picker handle the rest. The heading reads **"Week of Monday 17 August"**, not `W34`. The "previous" arrow is absent on the current week rather than present-but-disabled — a control you can never use is clutter.
2. **Employee field** — text input with **debounced autocomplete** (250 ms, min 2 characters) over registered employees, keyboard-navigable. Unknown names are offered as "Register *«name»* as a new employee" — a single confirm creates the employee.
3. **Today status banner** — as soon as an employee is identified: *"Today (Mon 17 Aug): **Menu 2 booked** · editable until 09:00"* or *"No lunch booked for today"* + the remaining time before cut-off.
4. **Weekly grid** — the digital version of the paper sheet: one column per working day, one row per menu, radio-style selection. Locked cells (past days, or today after 09:00) are visibly disabled with a tooltip explaining why. Each day column shows its price.
5. **Whole-week shortcut** — "Apply Menu N to every open day of the week", with a result summary of applied/skipped days.
6. **Submit** — one explicit save for the whole week; a toast confirms, and the grid reloads from the database so what you see is always persisted state.

### 6.3 Admin pages

- **Report tab** — employee autocomplete (or *All employees*), start/end date pickers, table `Date · Day · Menu · Description · Price`, **period total** in a summary card, CSV export.
- **Employees tab** — searchable list, add/edit inline, delete button enabled only when the employee has no bookings (otherwise it offers *Deactivate* with the reason shown).
- **Menus & Prices tab** — week selector, editable menu cards per day (add menu, edit description, delete with guard), day-price editor with "apply this price to the whole week".

### 6.4 Design system

- **Palette** — desaturated blue scale (`#0F2A47` → `#1B4F87` → `#2E6DB4` → `#5B93D3` → `#E8F0F9`), neutral greys, one restrained amber for warnings and one muted red for destructive actions. No flashy colours, per your request.
- **Gradients** — subtle blue gradients on the header bar, primary buttons and the active-day column.
- **Customisable** — every colour, radius, shadow and font is a CSS custom property in `wwwroot/css/theme.css`. Re-theming means editing that one file; a `theme.sample-dark.css` shows how.
- **Typography** — system font stack (Segoe UI Variable first), fluid type scale.
- **Responsive** — the weekly grid becomes a vertical day-by-day stack below 900 px.
- **Accessibility** — labelled inputs, visible focus rings, AA contrast, full keyboard operation of the grid and autocomplete.
- **No heavy UI framework** — hand-written CSS. Predictable, fast, and genuinely customisable.

### 6.5 Admin authentication

`config/admin-users.json`:

```json
{
  "Users": [
    { "Username": "massimo", "DisplayName": "Massimo Fauro", "Password": "changeme" }
  ]
}
```

- Cookie authentication, custom `AuthenticationStateProvider`, `[Authorize]` on `/admin/*`.
- The file is **plain text and Notepad++-editable**, as you asked.
- A password may *optionally* be written as `"sha256:<hex>"`; the verifier accepts both forms. This costs nothing and lets you harden a single account later without changing code.
- Changes to the file are picked up on reload — no restart needed.
- Failed attempts are rate-limited (5 per minute per IP) purely to avoid log noise.

### 6.6 Designing for a non-technical user

The person using this site books lunch between two meetings, on a phone or a shared kiosk, and has never read a manual. The paper sheet they are replacing took four seconds and never showed an error. **That is the bar.** Anything that would only make sense to someone who knows there is a database behind the page is a defect.

**Vocabulary — plain language, never system language**

| Never write | Write instead |
|-------------|---------------|
| "Week 34" | "Week of Monday 17 August" |
| "Submit" / "Commit changes" | "Book my lunches" |
| "Validation error: employee not found" | "We don't know that name yet. Add **Marie Dubois** as a new person?" |
| "Booking rejected: cut-off exceeded" | "It's past 9:00, so today's lunch can't be changed any more. You can still book from tomorrow." |
| "Concurrency conflict (`xmin` mismatch)" | "Someone else just changed this. Here's the latest version — please check it before saving again." |
| "Operation completed successfully" | "Saved — you're booked for Menu 2 on Monday." |
| "FK constraint violation" | "3 people already chose this menu, so it can't be removed." |

No error message names a table, a column, a status code, or an exception. No message ends without telling the user what they can do next.

**Nothing technical is ever visible.** No database ids in the page or in the URL, no ISO week numbers as the primary label, no UTC timestamps, no raw decimals (`12.5` is shown as `CHF 12.50`), no JSON, no stack traces. When something genuinely breaks, the user sees *"Something went wrong and your booking was not saved. Please try again."* — the technical detail goes to the log, and, on the admin page only, behind a discreet "details" link.

**Keep the mental model they already have.** The weekly grid deliberately mirrors the paper sheet: days across, menus down, a mark in the cell. Familiarity is the single biggest usability win available here, and it is free. Do not "improve" it into a wizard, a stepper, or a list of dropdowns.

**Make the state obvious without reading**

- The whole menu cell is clickable — not a 16-pixel radio button. Minimum 44 px touch targets throughout.
- A chosen menu is unmistakable at a glance: filled blue, a check mark, and a bold label. Never colour alone (colour-blind users, and a projector on the office wall).
- A locked cell carries a **visible short reason on the cell itself** — "Past" or "Closed at 9:00" — not only a tooltip. Tooltips do not exist on touch devices and are invisible to someone in a hurry.
- The cut-off is expressed as time remaining, in human terms: *"You can still change today's lunch for another 47 minutes"*, not *"cut-off 09:00"*.

**One obvious thing to do per screen.** A single visually dominant primary action; everything else is quiet. The booking page opens focused on the name field, and pressing Enter moves forward. The whole flow — name, five clicks, one button — must be completable without a mouse and without scrolling on a 1366×768 laptop.

**Confirmations state consequences, not "are you sure"**
*"Remove Marie Dubois? She has never booked a lunch, so nothing is lost."* versus *"Marie Dubois has 34 bookings, so she can't be removed. You can set her to inactive instead — her history stays, and her name stops appearing in the list."* The second sentence is the one that matters, and it is the reason the guard exists.

**Empty states teach.** *"No menus have been set for this week yet. An administrator adds them from the Admin page."* — never a blank table, never "0 results".

**Protect people from the duplicate-name trap.** "M. Fauro" and "Massimo Fauro" becoming two employees is the most likely real-world data problem in this application. Autocomplete is accent- and case-insensitive and matches on any part of the name; creating a new person is a deliberate, clearly worded confirmation showing the similar names already registered — never a silent side effect of typing.

**The admin page has the same audience.** It is used by an office or HR colleague, not a DBA. Same vocabulary rules, same forgiving inputs. Prices accept `12.50`, `12,50` and `12` alike and are echoed back formatted, because a comma should never produce an error message.

**Practical office details, deliberately in scope**

- The weekly view and the admin report **print cleanly** on one page — office users print things.
- The layout works on a phone (the grid stacks day by day below 900 px).
- Everything is reachable by keyboard, with visible focus, AA contrast, and labelled inputs.

**The V4 review is conducted from this section.** I will walk the pages as a first-time non-technical user — no explanation, no manual — and any moment requiring an engineer's knowledge is a defect to fix before validation. The walkthrough is done **in both languages** (§6.7).

### 6.7 Bilingual interface — French (default) and English

**Mechanism** — standard ASP.NET Core localization: `IStringLocalizer` backed by `.resx` resource files, with `RequestLocalizationMiddleware` reading a culture cookie.

| Aspect | Decision |
|--------|----------|
| Supported cultures | `fr-CH` (default) and `en-CH`. Both give `17.08.2026`, `1'234.50` and `CHF` — **only the words change, never the formats**. |
| First visit | The browser's `Accept-Language` decides; anything that is not English lands on French. |
| Persistence | A one-year culture cookie. The choice survives restarts and is per-person, not global. |
| Switch | A quiet `FR · EN` text control in the header — no flags. Flags denote countries, not languages, and Switzerland is exactly where that goes wrong. |
| Switching behaviour | Sets the cookie and reloads the current page **keeping the selected week, employee and unsaved selection** — a language switch must never cost the user their work. |
| Startup validation | Both cultures are verified to exist on the host at startup (ICU availability differs across environments); a missing culture logs a warning and falls back rather than crashing. |

**Resource files** — `src/LunchOrganizer.Web/Resources/`, split by area to keep them manageable: `Shared`, `Booking`, `Admin`, `Errors`, `Emails`. The neutral `.resx` holds **English**; `.fr.resx` holds **French**. A key missing from French therefore degrades to English rather than showing a raw key.

**The hard rule: no user-visible literal string anywhere in a `.razor`, ViewModel, or service.** Every word a human reads comes from a resource key — including validation messages, confirmation dialogs, empty states, button labels, page titles, and the `aria-label` attributes. Services return an `ErrorCode` plus parameters in `OperationResult`; the **UI** turns that into a sentence in the right language. This is why `OperationResult.ErrorCode` exists, and it is now load-bearing rather than optional.

**Translation quality matters as much as coverage.** The French is the primary language and must read as French written by a person — *"Vous pouvez encore modifier votre repas d'aujourd'hui pendant 47 minutes"*, not a word-for-word rendering of the English. The §6.6 vocabulary rules apply in both languages: no jargon in either.

**Day and month names** come from the culture, never from a hard-coded array — that is the most common way a bilingual UI ends up with English weekdays inside French sentences.

**The daily email has its own language setting** (`Email:Language`, default `fr`), because its recipient is the kitchen or the caterer and has nothing to do with who happens to be browsing the site.

---

## 7. Validation checkpoints

| ID | Checkpoint | What you review | Blocks |
|----|-----------|-----------------|--------|
| **V1** | This plan | Architecture, model, UI, process | Everything |
| **V2** | Database schema (`Docs/DATABASE_SCHEMA.md`) | Tables, constraints, rules | Repositories + `Scripts/create_database.sql` |
| **V3** | Backend services | Compile + unit tests green, rules honoured, **concurrency suite green** (§11.11) | Frontend wiring |
| **V4** | UI review | Live pages in the browser, look & feel, **and a first-time non-technical user walkthrough per §6.6** | Polish pass |
| **V5** | Email sample | A real generated `.eml` opened locally | SMTP wiring |
| **V6** | End-to-end | Full run against real PostgreSQL | Delivery |

Each checkpoint is also a **safe compaction point** (§12).

---

## 8. Email agent

### 8.1 Content

- **Language:** `Email:Language` (default `fr`) — independent of the website's language toggle, since the recipient is the kitchen or the caterer (§6.7).
- **Subject:** `{SubjectPrefix}{date}` → `COHU booked lunch for 17.08.2026`. Both the prefix and the date format come from `config/email.json`, so the subject stays exactly as you specified regardless of the body language.
- **Body:** HTML with a plain-text alternative —
  - a header line with the date and the total number of booked lunches;
  - one block per menu: `Menu 2 — 7 lunches`, its description, then the alphabetical list of employees;
  - menus with zero bookings are omitted;
  - a footer noting the generation timestamp.
- **Skip:** if the day has zero bookings, nothing is sent; `email_log` records `Skipped`.

### 8.2 Scheduling

- **Production:** `LunchOrganizer.Mailer.exe` run by **Windows Task Scheduler**, weekdays at 09:00. Exit codes: `0` sent, `2` skipped (no bookings), `1` failed. A registration script `Scripts/register-mailer-task.ps1` creates the task for you.
- **Development:** an `IHostedService` inside the web app, enabled by `Email:EnableInAppScheduler: true`, fires at the same time.
- **Idempotency:** both paths reserve the day in `email_log` first, so the summary can never be sent twice — even if both are enabled at once.
- **Manual trigger:** `LunchOrganizer.Mailer.exe --date 2026-08-17 --dry-run` writes the `.eml` to disk without sending. This is what produces the V5 sample.

### 8.3 Delivery options (your open point 6 — decided during implementation)

| Option | How it works | Needs | Verdict |
|--------|--------------|-------|---------|
| **A — Corporate SMTP relay** | MailKit → internal relay, port 25, anonymous, sender allowed by IP | The COHU relay host name | **Most likely the right answer** in a corporate network. Simplest, no credentials to store. |
| **B — Authenticated SMTP** | MailKit → `smtp.office365.com:587` STARTTLS with a service mailbox | A mailbox + app password | Works if there is no relay. Password sits in the JSON config. |
| **C — Microsoft Graph** | Graph `sendMail` with an Entra ID app registration | IT approval for an app registration | Cleanest security, slowest to obtain. |
| **D — Pickup directory** | Writes `.eml` files to a folder | Nothing | **Development default**, and the V5 validation mechanism. |

The code targets `IEmailSender`; switching option is a config edit. We start on **D**, and move to **A** or **B** as soon as you have the relay details — that is the only email question I will need to come back to you about.

---

## 9. Configuration files (`config/`, all Notepad++-editable)

| File | Content |
|------|---------|
| `database.json` | `Host`, `Port`, `Database` (`lunchorganizer`), `Username`, `Password`, `MaintenanceDatabase` (`postgres`), `AutoCreateDatabase`, `Seed` |
| `email.json` | `Mode` (`PickupDirectory` / `Smtp`), `SmtpHost`, `SmtpPort`, `UseStartTls`, `Username`, `Password`, `SenderName`, `SenderAddress`, `Recipients[]`, `SubjectPrefix` (`"COHU booked lunch for "`), `SubjectDateFormat` (`dd.MM.yyyy`), `Language` (`fr`), `SendTimeLocal` (`09:01`, see §11.8), `WorkingDays`, `SkipWhenNoBookings`, `PickupDirectory`, `EnableInAppScheduler` |
| `admin-users.json` | Admin accounts (§6.5) |
| `app.json` | `DefaultLunchPrice`, `Currency` (`CHF`), `DefaultCulture` (`fr-CH`), `SupportedCultures` (`["fr-CH","en-CH"]`), `BookingCutOffLocalTime` (`09:00`), `AutocompleteMinChars`, `MaxMenusPerDay` |

`appsettings.json` keeps only ASP.NET plumbing (logging, hosting); all business configuration is in `config/`. Real credentials are never committed — `config/*.local.json` overrides are git-ignored, and the committed files carry placeholders.

---

## 10. Agent orchestration

Three Sonnet agents work in parallel on **disjoint folders**, so they never edit the same file.

### 10.1 Phase 0 — Contracts (orchestrator, before any agent starts)

I create the solution skeleton and, most importantly, **all shared interfaces, DTOs and configuration classes** in `Domain` and `Services`. These are the contract the three agents build against, which is what makes true parallel work possible. Nothing else is written in this phase.

### 10.2 Phase 1 — Parallel implementation

| Agent | Owns | Delivers |
|-------|------|----------|
| **Backend** | `Domain/`, `Data/`, `Services/`, `tests/`, `Scripts/` | Entities, EF Core mapping + migrations, repositories, services, `DatabaseBootstrapper`, unit tests, and — **after V2 only** — the offline SQL script. |
| **Frontend** | `Web/Components/`, `Web/ViewModels/`, `Web/wwwroot/`, admin auth | Pages, components, ViewModels, theme, login. Runs against **in-memory fake services** implementing the Phase-0 interfaces, so it never waits for the backend. |
| **Email** | `Email/`, `Mailer/` | Summary builder, `IEmailSender` implementations (pickup dir + SMTP), console app with `--date`/`--dry-run`, hosted service, Task Scheduler registration script. Also builds against the interfaces + fakes. |

### 10.3 Phase 2 — Integration (orchestrator)

Swap the fakes for the real services in DI, run the app against real PostgreSQL, fix the seams, run the full test suite.

### 10.4 Phase 3 — Delivery

Produced **after V6 validation**, once the implementation is complete and verified:

- `Docs/DEBUGGING.md` — debug environment setup on this laptop.
- `Docs/INSTALLATION.md` — deployment on a target machine (see §10.7).
- `Docs/USER_GUIDE.md` — short end-user guide.
- Final update of the three agent summary files.

### 10.7 Installation procedure (`Docs/INSTALLATION.md`)

Written at the **end** of the implementation, once everything is validated — not before, so that it describes what was actually built rather than what was intended. It is a procedure someone else can follow on a machine that has never seen this project, and it must cover:

**1. Prerequisites on the target machine**

| Item | Detail to document |
|------|--------------------|
| Operating system | Supported Windows versions; whether Linux is viable |
| .NET | ASP.NET Core Runtime 10 (LTS) for a published build, or the SDK for a source build — with the exact download link and how to verify (`dotnet --list-runtimes`) |
| PostgreSQL | Minimum version (16), how to install, how to verify the service is running, and the required role privileges (`LOGIN`, and `CREATEDB` if auto-creation is used) |
| Network | Ports the site listens on, firewall rules, access to the SMTP relay |
| Accounts | The Windows account running the Task Scheduler job and the rights it needs |

**2. Database setup** — the two routes, side by side: automatic creation on first start (what it needs and what it does), or the offline `Scripts/create_database.sql` route for a locked-down server where the app's role may not create databases.

**3. Deployment** — `dotnet publish` command and output, where to copy the files, how to run the site (console, Windows Service, or IIS with the ASP.NET Core Module), and how to confirm it is up.

**4. Configuration files — the core of the document.** For each file in `config/`, a table of **every setting**: purpose, type, default, whether it must be changed for a new installation, and a worked example. Written so a non-developer with Notepad++ can do it.

| File | What must be reviewed on a new machine |
|------|----------------------------------------|
| `database.json` | Host, port, database name, username, password, `AutoCreateDatabase` |
| `email.json` | `Mode`, relay host/port/credentials, sender, **recipients**, subject prefix, `SendTimeLocal`, `EnableInAppScheduler` |
| `admin-users.json` | Replace the default `massimo` / `changeme` account — called out as a **mandatory first step** |
| `app.json` | `DefaultLunchPrice`, currency, culture, cut-off time |

Includes the `*.local.json` override mechanism and an explicit warning that these files contain credentials in clear text and must be protected by NTFS permissions rather than by obscurity.

**5. Scheduled email task** — running `Scripts/register-mailer-task.ps1`, what it creates, how to verify it in Task Scheduler, how to test with `--dry-run`, and how to read the exit codes.

**6. First-run verification checklist** — an ordered list ending in a confirmed booking visible in the database and a generated `.eml`.

**7. Troubleshooting** — the failures most likely on a fresh machine: PostgreSQL unreachable, insufficient privileges, port already in use, SMTP refused, task not firing, wrong cut-off due to machine timezone. Each with its symptom, cause and fix.

**8. Upgrade and uninstall** — replacing a deployed version (migrations run automatically), backing up the database (`pg_dump`), and removing the scheduled task.

### 10.5 Synchronisation points

```
Phase 0 ──► V1 (plan)
   │
   ├─ Backend  ──► V2 (schema) ──► repositories/services ──► SQL script ──► V3
   ├─ Frontend ──────────────────► UI on fakes ──────────────────────────► V4
   └─ Email    ──────────────────► builder + sender ──► .eml sample ─────► V5
                                                                            │
                                        Phase 2 integration ◄───────────────┘
                                                   │
                                                   ▼
                                                  V6
```

The only hard dependency is **V2 before the SQL script**. Everything else runs concurrently.

### 10.6 Progress tracking

One file per agent, updated **only when an intermediate step is validated**, as you required:

- `Docs/AGENT_BACKEND_SUMMARY.md`
- `Docs/AGENT_FRONTEND_SUMMARY.md`
- `Docs/AGENT_EMAIL_SUMMARY.md`

Each entry records: step ID, date, what was implemented, files touched, how it was verified, validation status, and open points. This is the detailed execution history you asked for — and it is also what makes compaction safe.

---

## 11. Concurrency and multi-user correctness

The application is multi-user by nature: several employees book at the same time, an admin edits menus while people book them, and the mailer reads the day's bookings while the web app writes them. The rule followed throughout is **the database is the arbiter** — every invariant that matters is enforced by a constraint, and application checks exist only to produce friendly messages, never as the sole protection. A check-then-act sequence in C# is a race; a constraint is not.

### 11.1 The Blazor Server trap — `DbContext` lifetime

A `DbContext` registered as *scoped* in Blazor Server lives for the **entire user circuit** (minutes or hours, not one request) and `DbContext` is **not thread-safe**. Two component events overlapping on the same circuit — a click while a debounced autocomplete query is in flight — throw *"A second operation was started on this context instance"* or, worse, silently corrupt the change tracker.

**Rule, applied without exception:**

- Register with `AddDbContextFactory<LunchOrganizerDbContext>(…)` — **never** `AddDbContext` in the Web project.
- Every repository method creates its own context via `await using var db = await _factory.CreateDbContextAsync(ct);` and disposes it at the end of the call.
- Contexts are never stored in fields, ViewModels, or component state.
- All read queries use `AsNoTracking()`; writes use short, explicit, single-purpose contexts.

This also keeps connections in the Npgsql pool for milliseconds instead of hours.

### 11.2 Service and ViewModel lifetimes

| Component | Lifetime | Rationale |
|-----------|----------|-----------|
| `IDbContextFactory` | Singleton | Thread-safe by design. |
| Repositories, services | Scoped | Stateless; hold only the factory. |
| ViewModels | Scoped (per circuit) | Per-user state. Never singleton. |
| `IClock`, notifier, config accessors | Singleton | Immutable or internally synchronised. |

**No mutable `static` state anywhere.** Any cache uses `IMemoryCache` or `ConcurrentDictionary`. Configuration is read through `IOptionsMonitor<T>` (thread-safe, and it picks up Notepad++ edits to `admin-users.json` without a restart).

### 11.3 Optimistic concurrency

Every mutable entity carries PostgreSQL's system column **`xmin`** as a concurrency token (`.UseXminAsConcurrencyToken()` in Npgsql) — no extra column, no manual bumping, and it detects *any* concurrent modification.

When two admins edit the same menu description, the second save raises `DbUpdateConcurrencyException`. The UI does **not** show a stack trace and does **not** silently overwrite: it reloads the current values and tells the user *"This menu was changed by someone else while you were editing. The current text is shown below — reapply your change if you still want it."* The same handling applies to employees and day prices.

### 11.4 Bookings — the hot path

The single most contended operation is two browser tabs (or two devices) booking for the same employee and day.

- `UNIQUE (employee_id, booking_date)` makes a duplicate physically impossible.
- Writes use PostgreSQL's atomic upsert — `INSERT … ON CONFLICT (employee_id, booking_date) DO UPDATE SET menu_id = …, price_snapshot = …, updated_at_utc = now()` — executed as **one statement**. There is no read-modify-write window, so the outcome of a race is well defined: last writer wins, exactly one row, no exception.
- Different employees touch different rows and never block each other.
- **Whole-week booking** runs as a single transaction (all five days commit or none), processing dates in ascending order so two concurrent week-writes can never deadlock.
- The cut-off is re-evaluated **inside** the service immediately before the write, using `IClock` — never trusted from the browser. A page left open across 09:00 cannot sneak a late booking through.

### 11.5 Delete-versus-use races

Classic TOCTOU: the admin's "does this menu have bookings?" check passes, and an employee books it a millisecond before the `DELETE` executes.

- The service check produces the friendly *"3 employees already booked this menu"* message in the normal case.
- The **foreign key is the actual guarantee**: PostgreSQL takes a row-share lock on the referenced menu when a booking is inserted, so a concurrent `DELETE` cannot succeed behind its back. If it loses the race, PostgreSQL raises `23503` (foreign-key violation), which the repository catches and converts into the same friendly message.

Identical treatment for employee deletion (`ON DELETE RESTRICT` on `bookings.employee_id`).

### 11.6 Insert races

| Race | Handling |
|------|----------|
| Two admins add a menu to the same day → both compute `menu_number = 4` | `UNIQUE (menu_date, menu_number)` rejects one; the repository catches `23505` and retries with a recomputed number, up to 3 attempts. |
| Two employees register the same new name simultaneously | `INSERT … ON CONFLICT (full_name) DO NOTHING` followed by a re-select — an atomic "get-or-create" that returns the same employee to both. |
| Two admins set the day price at once | Atomic upsert on the `price_date` primary key; last writer wins, and existing `price_snapshot` values are untouched by design. |

### 11.7 Startup races

Two instances starting at once (the web app and the mailer, or a debug session next to a running app) must not both create the database or apply migrations.

- `CREATE DATABASE` catches `42P04` (*duplicate database*) and treats it as success.
- Migrations run inside a **PostgreSQL advisory lock** (`pg_advisory_lock`), so the second instance waits and then finds nothing to do rather than colliding inside `__EFMigrationsHistory`.

### 11.8 Mailer versus web app

- The day is **reserved** before any work: `INSERT INTO email_log (summary_date, …) VALUES (…) ON CONFLICT (summary_date) DO NOTHING`. Zero rows affected means another process owns the day — this one exits with code `3` (*already handled*). Two schedulers firing at once therefore produce exactly one email.
- The summary is read in **one query** (a single consistent snapshot), never as a loop of per-menu queries that could interleave with a write.
- **The 09:00 boundary.** The cut-off is strict (`< 09:00:00.000` local), so any booking committing at or after 09:00 is rejected — nothing can be lost *after* the mailer reads. The only theoretical gap is a transaction that began at 08:59:59.9 and commits a few milliseconds later. To close it, `SendTimeLocal` defaults to **09:01**: the email still reports the 09:00 cut-off state, one minute of margin removes the boundary entirely, and the value is in `config/email.json` if you prefer exactly 09:00.

### 11.9 UI-level concurrency

- **Double-submit:** each ViewModel command is guarded by a `SemaphoreSlim(1,1)` plus an `IsBusy` flag that disables the button. A double-click cannot start two saves, and the server re-validates regardless.
- **Cross-circuit refresh:** a singleton `IBookingChangeNotifier` raises an event after every committed write; open circuits viewing the same week reload and re-render through `InvokeAsync(StateHasChanged)` — the correct way to touch a component from another thread. So an admin adding a menu is seen by employees already on that week, without a manual refresh.
- **Stale state:** after every save the grid reloads from the database. What the user sees is always persisted state, never a hopeful local mutation.
- **Circuit isolation:** ViewModels are per-circuit; one user's typing can never leak into another's page.

### 11.10 Isolation level and pooling

Read Committed (the PostgreSQL default) is sufficient, because correctness rests on constraints and atomic single-statement writes rather than on transaction isolation. Multi-statement operations (whole-week booking, delete-with-guard) use explicit transactions kept as short as possible. Npgsql pooling is enabled with a configured `MaxPoolSize`; short-lived contexts (§11.1) keep the pool healthy under load.

### 11.11 Verification

Concurrency is tested, not assumed. `LunchOrganizer.Tests` includes a **`ConcurrencyTests`** suite running against a real PostgreSQL instance:

1. 50 parallel upserts for the same employee and day → exactly one row, no exception.
2. 10 parallel "add menu" calls on the same day → 10 menus, numbers 1–10, no gaps or duplicates.
3. Delete a menu while bookings are being inserted → either a clean refusal or a clean delete, never an orphaned booking.
4. 20 parallel "register new employee" calls with the same name → one employee, all callers get the same id.
5. Two mailer processes started simultaneously → exactly one email, one `email_log` row.
6. Concurrent edits of one menu → the second save reports a concurrency conflict rather than a silent overwrite.

These tests are part of **V3** (services) and re-run at **V6** (end-to-end).

---

## 12. Context management

Compaction is performed **only** when all three agents sit at a validated intermediate state — in practice, at checkpoints V2…V6. Before compacting, the three summary files are brought up to date so that the full state (decisions, files, next steps) survives in `Docs/` rather than in conversation memory.

---

## 13. Deliverables

| Path | Content |
|------|---------|
| `Docs/IMPLEMENTATION_PLAN.md` | This document |
| `Docs/DATABASE_SCHEMA.md` | Schema reference (V2) |
| `Docs/DEBUGGING.md` | Debug setup and step-by-step instructions |
| `Docs/INSTALLATION.md` | Target-machine prerequisites, deployment, and a setting-by-setting configuration guide (§10.7) — **written last, after V6** |
| `Docs/AGENT_*_SUMMARY.md` | Per-agent execution history |
| `Docs/USER_GUIDE.md` | Short end-user guide |
| `Scripts/create_database.sql` + `.ps1` | Offline DB creation (after V2) |
| `Scripts/register-mailer-task.ps1` | Task Scheduler registration |
| `src/`, `tests/`, `config/` | The application |

---

## 14. Risks and mitigations

| Risk | Mitigation |
|------|-----------|
| PostgreSQL role lacks `CREATEDB` | Explicit startup error naming the missing privilege; `Scripts/create_database.sql` as the manual fallback. |
| Corporate SMTP unknown | Pickup-directory mode ships first; SMTP is a config change (§8.3). |
| Web app down at 09:00 | Task Scheduler runs a standalone executable — independent of the web app. |
| Two schedulers double-send | `email_log` primary key on the date makes it impossible. |
| Cut-off / timezone confusion | Local time for rules, UTC for audit columns, single `IClock`, unit-tested. |
| Menu deleted while someone is booking | `ON DELETE RESTRICT` plus a service guard; the UI reloads persisted state after every save. |
| Culture-dependent parsing of prices | All money is `decimal` end-to-end (never `double`); `numeric(10,2)` in PostgreSQL; input parsing uses the configured culture explicitly, never the thread default. |
| Concurrent access / lost updates | Constraint-first design, atomic upserts, `xmin` optimistic concurrency, and a dedicated test suite — see §11. |
| `DbContext` misuse in Blazor Server | `AddDbContextFactory` only; a code-review gate at V3 rejects any injected `DbContext` (§11.1). |
| Long-lived circuits holding connections | Short-lived contexts return connections to the Npgsql pool immediately (§11.10). |

---

## 15. Open questions

All blocking questions are resolved. Three items remain, each needed later, none blocking Phase 0:

1. **PostgreSQL credentials** — host, port, user, password, and whether that user has `CREATEDB`. Needed at **V3**.
2. **SMTP details** — corporate relay host, or a service mailbox with credentials (see §8.3). Needed at **V5**.
3. **Recipient address(es)** for the daily summary, and the sender address to display. Needed at **V5**.

I will ask for each one at the checkpoint that needs it, not before.
