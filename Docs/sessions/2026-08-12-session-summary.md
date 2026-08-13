# Session summary — 12 August 2026

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer` (also reachable as `D:\Data\GitPerso\LunchOrganizer` — same files)
**Session outcome:** from an empty repository containing only a specification text file, to a working, database-backed Blazor web application with a validated schema, 49 passing tests, and full documentation.

This file is written to be read **cold** at the start of the next session. It assumes no memory of today.

---

## 1. Where we ended up

| Component | State |
|---|---|
| Solution (`net10.0`, 8 projects) | Builds, 0 errors |
| PostgreSQL database `lunchorganizer` | **Live** — created by the app itself, schema validated, seeded |
| Web application | **Fully wired to the real database.** Booking page, admin pages, bilingual FR/EN, admin login |
| Daily email pipeline | Implemented and tested, **but still reading in-memory demo data — see §5** |
| Tests | **49/49 passing**, including 6 real-PostgreSQL concurrency scenarios |
| Documentation | Plan, schema, debugging guide, installation guide, three agent histories |

---

## 2. Decisions taken today

| # | Decision |
|---|---|
| Menus | Attached to a **specific date** — "Menu 2 on Monday" ≠ "Menu 2 on Tuesday" |
| Price | Per **day**, same for all menus that day, edited on the admin page; a default comes from config |
| Price history | Each booking **freezes** its price at booking time; later edits never rewrite past bookings |
| Menu management | **Admin-only** (moved off the booking page at Massimo's request) |
| Authentication | Admin pages only; users in a Notepad++-editable JSON file. Booking page stays anonymous |
| Framework | **.NET 10 LTS** — .NET 9 left support in May 2026 |
| Locale | **Swiss / CHF** — `dd.MM.yyyy`, `1'234.50`, `fr-CH` |
| Language | **French default with an English toggle**, `.resx` + culture cookie |
| Email scheduling | Standalone console app under Windows Task Scheduler (survives app restarts), at **09:01** — one minute after the cut-off, so a booking committing at 08:59:59 cannot be missed |
| Working days | Mon–Fri, no holiday calendar; **zero bookings → no email** |
| Concurrency | Added at Massimo's request as plan §11 — the whole design rests on it |
| UI audience | Non-technical: plan §6.6 governs all UI decisions |

---

## 3. Verification actually performed (not merely reported by agents)

Recorded because it is the evidence behind the "it works" claim:

- **`dotnet test` → 49/49 passed.**
- **Concurrency tests genuinely hit PostgreSQL** — per-test durations of 995 ms (10 parallel menu inserts) and 423 ms (20 parallel registrations) prove real round trips rather than stubs.
- **All five integrity guarantees attacked directly in SQL and all five rejected by the database:** booking a menu on a foreign date; a second lunch for the same employee/day; `ALICE MARTIN` beside `Alice Martin`; deleting an employee with history; creating "Menu 0".
- **Live database inspected via `psql`:** 5 tables + `__EFMigrationsHistory`, `full_name` is `citext`, composite FK present, seed = 8 employees / 45 menus / 8 bookings / 3 prices.
- **Extensions:** `citext`, `unaccent`, `plpgsql`. Accent-insensitive search confirmed — `%chloe%` matches `Chloé Bernard`.
- **Web app runs against real PostgreSQL** and serves the booking page in French (*"Semaine du lundi 10 août"*), with English via `Accept-Language`. Interactivity confirmed (`blazor.web.js` + component state present).
- **`dotnet publish` executed** for both apps and the output inspected.

---

## 4. Checkpoint status

| Checkpoint | Status |
|---|---|
| **V1** — implementation plan | ✅ Validated (rev 6) |
| **V2** — database schema | ✅ Validated by Massimo |
| **V3** — backend + concurrency | ✅ Passed on orchestrator verification |
| **V4** — UI walkthrough | ⏳ **Not done — Massimo's walkthrough is still pending** |
| **V5** — email sample | ⚠️ Content approved in principle; sample was built from **demo data** |
| **V6** — end-to-end | ⏳ Blocked by §5 |

---

## 5. TOMORROW'S FIRST TASK — connect the mailer to PostgreSQL

> ✅ **DONE on 13 August 2026.** All three parts were implemented and verified against the live database — plus a fourth issue this section did not anticipate: `AddLunchOrganizerData()` registers its repositories as **scoped**, while the mailer registered the whole email pipeline as **singleton**, so the straight swap described below would have thrown at resolve time. See `Docs/AGENT_EMAIL_SUMMARY.md` § E10 for the record. The section is kept unedited below as the original brief.

**The problem.** `src/LunchOrganizer.Mailer/Program.cs` still registers in-memory repositories behind a `// TODO(integration)` marker (around lines 84–96) and seeds them with demo data. The mailer therefore produces a convincing but **entirely fictitious** summary. The web application is correctly wired; only the mailer is not.

**The fix — three parts:**

1. **Swap the repository registrations.** Replace
   ```csharp
   var bookingRepository = new InMemoryBookingRepository();
   DemoDataSeeder.Seed(bookingRepository, date);
   builder.Services.AddSingleton<IBookingRepository>(bookingRepository);
   builder.Services.AddSingleton<IEmailLogRepository, InMemoryEmailLogRepository>();
   ```
   with the real EF Core registrations — `AddLunchOrganizerData(...)` from `LunchOrganizer.Data`, the same extension method `src/LunchOrganizer.Web/Program.cs` already calls. Delete `src/LunchOrganizer.Mailer/DemoDataSeeder.cs`.

   ⚠️ **Do NOT delete `src/LunchOrganizer.Email/InMemory/`** — two email tests (`DailySummaryMailServiceIdempotencyTests`, `DailySummaryBuilderTests`) depend on those classes. Only the *Mailer's* use of them goes away.

2. **Load the `.local.json` overrides.** The mailer's configuration block (around lines 62–67) loads only the four base files, so it would use the `changeme` password. Add the four override lines exactly as `Web/Program.cs` has them:
   ```csharp
   .AddJsonFile(Path.Combine(configDirectory, "app.local.json"),         optional: true, reloadOnChange: false)
   .AddJsonFile(Path.Combine(configDirectory, "database.local.json"),    optional: true, reloadOnChange: false)
   .AddJsonFile(Path.Combine(configDirectory, "email.local.json"),       optional: true, reloadOnChange: false)
   .AddJsonFile(Path.Combine(configDirectory, "admin-users.local.json"), optional: true, reloadOnChange: false)
   ```
   Each `.local.json` must come **immediately after** its base file so it overrides rather than being overridden.

3. **Decide whether the mailer should run the bootstrapper.** Recommendation: **no**. It should fail with a clear message if the database is absent, rather than creating one. The web application owns schema creation.

**How to verify the fix:**

```powershell
# make sure real data exists for the date
$env:PGPASSWORD='P0stgr3sP0stgr3s'
& 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d lunchorganizer -c `
  "select b.booking_date, m.menu_number, e.full_name from bookings b join employees e on e.id=b.employee_id join menus m on m.id=b.menu_id order by 1,2;"

# then run against the same date and compare, name by name
dotnet run --project src/LunchOrganizer.Mailer -- --date <a date with bookings> --dry-run
```

The `.eml` must list **exactly** the employees the SQL query returned. Today's demo data used names like *Alice Martin, Bob Dupont, Giulia Conti* — the seeded database uses the same names, **so compare the actual bookings per date, not just the names**, or the check proves nothing.

Then re-run `dotnet test` (49 must still pass) and confirm a real (non-dry) run writes a row to `email_log`.

---

## 6. Everything else outstanding

Ordered by value.

| # | Task | Notes |
|---|---|---|
| 1 | **Mailer → real database** (§5) | Blocks V6 and any real use of the daily email |
| 2 | **V4 UI walkthrough with Massimo** | Not yet done. Walk it as a first-time non-technical user, in both languages, per plan §6.6 |
| 3 | **Frontend adopts the new backend contracts** | `SkippedDayDto.ReasonCode`/`ReasonArgs` (skip reasons are currently untranslatable English); `IEmployeeService.HasBookingsAsync` and `DeleteOrDeactivateWithOutcomeAsync` (delete-vs-deactivate is presently inferred via `IReportService`) |
| 4 | **SMTP details** from Massimo | Then `Mode: "Smtp"` + host + `Recipients`. Config-only, no code change |
| 5 | **V6 end-to-end** | Book in the UI → verify in SQL → mailer sends the matching summary |
| 6 | Live cut-off countdown | Currently computed per page load, not ticking |
| 7 | Toast on new-employee registration | Only the panel disappearing confirms it today |
| 8 | Delete `tests/LunchOrganizer.Tests/UnitTest1.cs` | Leftover template placeholder |
| 9 | Test the accent-insensitive **fallback** path | Only the `unaccent` path is exercised; `unaccent` is installed here |
| 10 | Automated accessibility/contrast check | AA designed for, never machine-verified |
| 11 | `Docs/USER_GUIDE.md` | Not started |
| 12 | Consider suppressing the 9–12 `MSB3277` warnings | Benign EF Core version-unification noise in the Tests project |

**Open UI question for Massimo:** the menu grid only appears after a name is entered. Should the week's menus be visible before identifying yourself? Cheap to change now.

---

## 7. Environment facts worth not rediscovering

| Fact | Value |
|---|---|
| .NET SDKs | 10.0.302 (used) and 9.0.316; `global.json` pins the `10.0.3xx` band |
| PostgreSQL | 16.14, service `postgresql-x64-16`, running |
| **`psql.exe`** | **`D:\PostgreSQL\bin\psql.exe` — NOT on `PATH`** |
| Database | `lunchorganizer`; role `postgres` is superuser with `CREATEDB` |
| Credentials | `config/database.local.json` (**git-ignored**). `config/database.json` keeps `changeme` |
| Web ports | `http://localhost:5226`, `https://localhost:7047` (from `launchSettings.json`) |
| Admin login | `massimo` / `changeme` |
| `.eml` output | `src\LunchOrganizer.Mailer\bin\Debug\net10.0\mail-drop\` |
| EF tooling | Local tool — run `dotnet tool restore` first |
| Test database | `lunchorganizer_test`, created and dropped by the concurrency fixture |
| Publish hazard | `config/*.json` is copied by wildcard, so **`database.local.json` (real password) lands in publish output** — delete it before distributing |

---

## 8. Working rules established this session

- **All code is produced by Sonnet agents** (standing global directive), then verified by the orchestrator — build, tests, live run, direct SQL — rather than trusted from the agent's own report. This caught real problems today.
- **Never commit to git.** Committing is Massimo's alone. The repository still has **zero commits**. Three agents ran read-only `git status` despite the rule; nothing was written, but the rule stands and should be repeated in every agent brief.
- **File ownership prevents collisions.** `Web/Program.cs` belongs to one agent at a time; the backend exposes DI extension methods rather than editing the web project.
- **Contract changes are additive**, so a finished project keeps compiling.
- **Agent histories** (`Docs/AGENT_*_SUMMARY.md`) get an entry only when a step is **validated**, and they carry a "carry-forward notes" section so nothing is lost across a context compaction.

---

## 9. Document index

| File | Contents |
|---|---|
| `Docs/IMPLEMENTATION_PLAN.md` | The validated specification. §6.6 non-technical UI, §6.7 bilingual, §11 concurrency, §10.7 installation scope |
| `Docs/DATABASE_SCHEMA.md` | Schema reference, ER diagram, verbatim DDL (V2 deliverable) |
| `Docs/DEBUGGING.md` | Running and debugging on this laptop. **§10 lists every known gap** |
| `Docs/INSTALLATION.md` | Target-machine deployment and setting-by-setting configuration |
| `Docs/AGENT_BACKEND_SUMMARY.md` | Backend history, V2 and V3 records, environment facts |
| `Docs/AGENT_FRONTEND_SUMMARY.md` | Frontend roadmap and constraints |
| `Docs/AGENT_EMAIL_SUMMARY.md` | Email roadmap and the delivery-mode decision table |
| `LaunchOrganizer.txt` | Massimo's original specification |
| `AnswersToClaudeBeforeImplementation.txt` | His answers to the first eleven clarifying questions |
