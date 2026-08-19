# Session handover — 18 August 2026

**PostgreSQL → SQL Server migration · per-employee confirmation emails · mandatory email on self-registration**

Read this first in a future session. It records what is **done**, what is **proven**, and — most
importantly — what is done but **not yet proven**. The gap between those last two is where the risk is.

---

## 1. One-paragraph state of the project

The application no longer uses PostgreSQL at all. It targets SQL Server, builds clean (0 errors,
0 warnings, all 8 projects), and three features landed back-to-back **with no validation between them**.
Five of six concurrency tests pass against a real SQL Server; the sixth is a genuine open defect (D1).
No human has clicked any button in this application, ever — that was already true before this session and
is still true now.

---

## 2. What changed this session

### 2.1 PostgreSQL → SQL Server migration (steps 1–10 of `Docs/IMPLEMENTATION_PLAN_SQLSERVER.md`)

Decision taken at the start: **SQL Server only**, no dual-provider abstraction. The trigger for that call
was measuring the live database and finding **26 rows total** — that made "carry the data across" a small
script rather than a workstream, and removed the main argument for keeping both providers.

Steps 1–10 are complete. **Steps 11–13 are not started** and are deliberately blocked behind validation:

- **Step 11** — delete the PostgreSQL docs and scripts
- **Step 12** — write `Scripts/create_database.sql` for SQL Server
- **Step 13** — migrate the 26 rows (`SET IDENTITY_INSERT` + `DBCC CHECKIDENT`)

Do **not** do step 11 before the verification gate passes. Deleting the PostgreSQL scripts destroys the
fallback while the replacement is still unproven.

### 2.2 Per-employee confirmation emails (`Docs/IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md`)

When the daily summary goes to the kitchen, each employee who booked also receives their own confirmation
— **only if they have an email address on record**. Every employee gets an independent attempt inside its
own `try`/`catch`, so one bad address can never stop the rest of the loop. The summary's own
`Status` / `BookingCount` / `SuggestedExitCode` are unaffected by confirmation failures, by design.

### 2.3 Mandatory email when self-registering (`Docs/IMPLEMENTATION_PLAN_REGISTRATION_EMAIL.md`)

The booking page's "register?" panel now requires a valid email before the confirm button enables.

**Why this existed at all:** `RegisterAsync` took only a name, so *every* employee who ever self-registered
was stored with `email = NULL` — and 2.2 skips exactly those rows. The people most likely to want a
confirmation were structurally guaranteed never to receive one. 2.3 is what makes 2.2 actually work.

Three judgement calls, all recorded in the plan:

- Validation is one shared `LunchOrganizer.Email.Validation.EmailAddressValidation.IsValidFormat`, used by
  **both** the UI gate and `EmployeeConfirmationSender`. Two copies could drift, and if the UI accepted
  something the sender rejects, a user would be told they registered for confirmations and then silently
  receive none.
- `MailboxAddress.TryParse` **alone is too lenient** for a UI gate — MimeKit accepts a bare local-part with
  no domain. The shared validator adds a `Contains('@')` check. The invariant is one-directional: the UI
  may be stricter than the sender, **never looser**.
- A blank address collapses to `NULL`, not `""`, so the database and the sender's `IsNullOrWhiteSpace`
  check agree on what "no address" means.

**Scope note worth revisiting:** the reactivation fix (below) also affects the **admin** add-employee path,
because both share `RegisterAsync`. An admin adding a name matching a deactivated employee now reactivates
that row instead of silently getting back an inactive one. Desirable, consistent — but it was never
explicitly requested. Massimo has been told; it can be scoped back to the booking page if he prefers.

### 2.4 A pre-existing bug found and fixed along the way

The autocomplete searches **active** employees only. So a **deactivated** employee typing their own name
was offered "register?" — and `EmployeeRepository.AddAsync` is a get-or-create whose
`INSERT … WHERE NOT EXISTS` silently skipped the insert, returned the old inactive row, and **discarded the
address just typed**. The user saw the booking grid and believed they had registered. They had not.

Harmless-looking before 2.3; actively misleading after it. `EmployeeService.RegisterAsync` now reactivates
the row and fills a missing email, while **never overwriting an existing address**.

---

## 3. Verified vs. not verified — read this before trusting anything above

| Claim | Evidence |
|---|---|
| Solution builds, 0 errors 0 warnings | ✅ run directly, this session |
| Database creation with `Seed: true` **and** `Seed: false` | ✅ **verified by Massimo, 18 Aug** — closed, do not re-run |
| MERGE/HOLDLOCK upserts survive parallel load | ✅ 5 passing concurrency tests against real SQL Server |
| Both collations behave as designed | ✅ pre-verified with live SQL before implementing |
| `sp_getapplock` / `sp_releaseapplock` return 0 | ✅ verified live |
| Menu numbering under parallel insert | ❌ **fails — defect D1** |
| Confirmation emails | ❌ **never executed** |
| Registration email requirement | ❌ **never executed** — tests written, not run |
| Any UI behaviour whatsoever | ❌ **no button has ever been clicked** |
| Data migration of the 26 rows | ❌ not started |

Everything in the ❌ rows is **code review and reasoning only**. Do not describe it as working.

---

## 4. Open defect D1 — menu numbering race · BLOCKS SIGN-OFF

`TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers` fails:

```
Cannot insert duplicate key row … 'ix_menus_menu_date_menu_number'. Duplicate key value is (2027-04-10, 5).
```

**Diagnosed — this is not an error-mapping bug.** `SqlServerErrors` correctly handles both 2601 and 2627.
The real cause: `MenuRepository.AddAsync` computes the next menu number with a **read-then-insert** guarded
only by `BusinessRules.MaxInsertRetryAttempts = 3`, while the test fires **10** concurrent inserts. It is a
pre-existing probabilistic design that PostgreSQL happened to survive.

**Proposed fix, documented but NOT applied:** compute the number inside the INSERT itself under
`UPDLOCK, HOLDLOCK`, the same pattern already used in `EmployeeRepository.AddAsync`. Roughly a
one-statement change to one method.

Nothing may be signed off while D1 is open.

---

## 5. What to do next

Everything below is **awaiting Massimo's decision** — he controls when tests run, for token-cost reasons.

1. **Fix D1** — small, well-understood, and it unblocks sign-off.
2. **Run the existing 6 concurrency tests.** Now doubly relevant: `TwentyParallelRegisterSameName` hammers
   `EmployeeService.RegisterAsync`, the exact method 2.4 modified, and has not run since. Expect **5/6**
   until D1 is fixed.
3. **Run the 9 new unit tests** in `tests/LunchOrganizer.Tests/Unit/BookingRegistrationEmailTests.cs`.
4. **Manual validation** per `Docs/TEST_PLAN_SQLSERVER_MIGRATION.md` §2–§5 and §4b.
5. **Then** steps 11–13.

### On testing concurrent access to the booking page specifically

Massimo asked about this at the end of the session. The honest position:

- **Can be tested:** the database contention behind the page — that is what the 6 concurrency tests cover.
- **Cannot currently be tested:** the page itself. Blazor Server drives clicks over a SignalR WebSocket
  circuit, so scripted HTTP will not press buttons. The test project has **no bUnit, no Playwright, no
  Selenium, no `WebApplicationFactory`** — only xUnit and FluentAssertions.
- **Untested risks are display-level, not storage-level:** whether user A's grid shows a stale selection
  after user B changes something, whether a newly-locked day refreshes, whether `IBookingChangeNotifier`
  reaches every circuit.
- **Middle option offered, not built:** a test spinning up N concurrent `BookingViewModel` instances against
  the real service and LocalDB — the same view model the page binds to, minus rendering and transport.

No decision taken. Massimo will choose.

---

## 6. Environment

No full SQL Server engine on this laptop. There **is** a running LocalDB:

- Instance `(localdb)\MSSQLLocalDB` — SQL Server 2025 (RTM-CU3) Express 17.0.4025.3
- `COHU\mfauro` is sysadmin; `CREATE DATABASE` permitted
- **LocalDB does not support encryption.** `Encrypt=true` fails with *"Encryption not supported on SQL
  Server."* `config/database.local.json` therefore sets `"Encrypt": false` — **do not copy that to
  production**, where `config/database.json` correctly keeps `Encrypt: true`.
- `config/database.local.json` contains `//` comments documenting that caveat. `AddJsonFile` tolerates
  them; **`JsonSerializer.Deserialize` does not** — `ConcurrencyTestFixture` needs
  `ReadCommentHandling = JsonCommentHandling.Skip` and `AllowTrailingCommas = true`. This already bit once.

LocalDB is necessary but **not sufficient** — re-run the concurrency tests against the real target server
before go-live.

---

## 7. SQL Server porting knowledge worth not rediscovering

| PostgreSQL | SQL Server |
|---|---|
| `citext` | `nvarchar(200) COLLATE Latin1_General_CI_AS` |
| `unaccent()` in the predicate | explicit `COLLATE Latin1_General_CI_AI` in the predicate only |
| `timestamptz` | `datetimeoffset(7)` |
| `now()` | `CAST(SYSUTCDATETIME() AS datetimeoffset)` |
| `xmin` system column | app-managed `version bigint` + `IsConcurrencyToken()` + `SaveChanges` override |
| `INSERT … ON CONFLICT DO UPDATE … RETURNING` | `MERGE … WITH (HOLDLOCK) … OUTPUT inserted.*` |
| `ON CONFLICT DO NOTHING` | `INSERT … SELECT … WHERE NOT EXISTS (SELECT 1 … WITH (UPDLOCK, HOLDLOCK))` |
| `pg_advisory_lock` | `sp_getapplock` / `sp_releaseapplock`, `@LockOwner='Session'` |
| unique `23505` | **`2627` and `2601`** — both, they are different errors |
| FK `23503` · db exists `42P04` · no permission `42501` | `547` · `1801` · `262` |

Five traps that cost real time this session:

1. **`WITH (HOLDLOCK)` on MERGE is mandatory.** A bare MERGE lets two concurrent racers both reach
   `WHEN NOT MATCHED`. MERGE must also end with a semicolon.
2. **The collation split is deliberate.** Column `CI_AS` (case-insensitive, accent-**sensitive**) keeps
   `Chloe` and `Chloé` distinct for uniqueness; the search predicate overrides to `CI_AI` so typing
   `chloe` still finds `Chloé`. Both halves must hold — getting one right and the other wrong is the
   likeliest silent failure, and nothing else in the test plan would catch it.
3. **No bare `ORDER BY` inside composed raw SQL.** `Take`, `OrderBy`, `Single*`, `First*`, `Count*` all
   compose and make EF wrap the SQL in a derived table → SQL Server error 1033. (`AsNoTracking()` does not
   compose.) PostgreSQL allowed it, which is why it worked before. Order in LINQ instead.
4. **`sp_getapplock` needs `CommandType.StoredProcedure`.** With `CommandType.Text` and
   `EXEC @result = …`, SqlClient does not reliably bind the return value.
5. **Microsoft.Data.SqlClient 4.0+ defaults to `Encrypt=true`** — see §6.

---

## 8. Document map

| Path | What it is |
|---|---|
| `Docs/IMPLEMENTATION_PLAN_SQLSERVER.md` | Master migration plan. §8 has the progress log and D1. |
| `Docs/IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md` | Confirmation-email feature spec |
| `Docs/IMPLEMENTATION_PLAN_REGISTRATION_EMAIL.md` | Registration-email feature spec + implementation notes |
| `Docs/TEST_PLAN_SQLSERVER_MIGRATION.md` | **The consolidated test plan** — start here for validation |
| `Docs/TEST_CHECKLIST.md` | Partly stale (psql commands). UI sections §A/§B still valid and unexecuted. |

**All of these are untracked in git** (`??`). They survive into a future session, but they are not in
version history and `git clean -fd` would remove them. Worth committing — by Massimo, not by Claude.

---

## 9. Standing constraints — these are not optional

From `C:\Users\mfauro\.claude\CLAUDE.md` (global, applies to every session):

- **All code generation and source editing goes through a Sonnet agent** (`Agent` tool, `model: sonnet`),
  with precise file-by-file instructions, then reviewed and verified. Applies to everything, including
  one-off utilities. Non-code artifacts — markdown, plans, this document — may be authored directly.
- **Never commit to git.**

Session-level, set by Massimo for token-cost control:

- **Ask before running `dotnet test` or launching the app.** `dotnet build` is fine unprompted.
- He decides when validation happens. Do not start a test run to be helpful.

### One process lesson from this session

I once told an agent that a grep for `ON CONFLICT|unaccent` must return zero hits. Those phrases legitimately
appear in explanatory prose, so the agent reworded six comment blocks to satisfy the proxy. That was my
instruction's fault, not the agent's. Later prompts said explicitly: *"Do NOT edit a comment merely to make a
grep pass."* Keep that line in agent instructions.
