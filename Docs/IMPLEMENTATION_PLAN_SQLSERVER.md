# Implementation Plan — Migrate LunchOrganizer from PostgreSQL to SQL Server

**Status (2026-08-18):** **Steps 1–10 complete and compiling; steps 11–13 blocked at the §9
verification gate.** The code has never been executed against a database — see "Where this stands"
below before doing anything else.
**Supersedes:** `IMPLEMENTATION_PLAN_MULTIDB.md` (dual-provider design, abandoned — see §0).

---

## Where this stands — read this first

| | |
|---|---|
| **Done** | Steps 1–10. `dotnet build LunchOrganizer.sln -c Debug --no-incremental` → 0 errors, 1 pre-existing unrelated warning. |
| **Not done** | Steps 11 (delete PostgreSQL docs/scripts), 12 (`create_database.sql` for SQL Server), 13 (migrate the 26 rows). |
| **Blocking them** | The §9 verification gate has **not been run**. Not one line of this migration has touched a database. |
| **Biggest unproven risk** | `MERGE … WITH (HOLDLOCK)` correctness (§5.2). Also unproven: the bootstrapper's create-if-missing path, collation behaviour in real queries, and the `version` concurrency check. |
| **Test environment** | Ready — `(localdb)\MSSQLLocalDB`, SQL Server 2025 Express, sysadmin. See §10. |

**Do not start step 11.** Deleting the PostgreSQL scripts and documentation before verification would
destroy the only working reference implementation while the replacement is still unproven. That is the
entire purpose of decision D3.

### ⚠ A new feature landed between step 10 and the gate

**Per-employee booking confirmation emails** — see `Docs/IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md`.
It was implemented *after* steps 1–10 and *before* the §9 gate ran, so the gate must be run against
the combined result. §9.7 (mailer end-to-end) is widened accordingly; that plan carries its own
verification list which should be run in the same sitting.

### To resume: run the §9 gate

Preconditions:
1. **§9.2 is already closed** — database creation was verified by Massimo with `Seed: true` and
   `Seed: false` on 2026-08-18. Leave the existing `lunchorganizer` database in place; do **not** drop
   it. `Database:Seed` can stay at whatever value the local config currently has.
2. The live PostgreSQL database is untouched and still holds the 26 rows. The PostgreSQL credentials
   that were in `config/database.local.json` were backed up outside the repository before being
   overwritten; recover them from there if PostgreSQL access is needed for step 13.

Then work through §9.1 and §9.3–§9.9 in order. Expect to find defects: two runtime-only bugs were
already caught by inspection during steps 6–9 (see the progress log in §8), and that class of error is
exactly what this gate exists to surface.
**Goal:** SQL Server becomes the application's only database engine. Every trace of PostgreSQL is
removed from code, packages, scripts and documentation. The existing 26 rows of production data are
carried across with their identifiers intact.

---

## 0. Decisions taken (2026-08-18)

The original request was to make the engine a *configuration choice* (PostgreSQL **or** SQL Server).
I planned that in `IMPLEMENTATION_PLAN_MULTIDB.md`, then we tested the assumption behind it and found
it didn't hold: there is no installation that needs PostgreSQL. Dual support would have been a
permanent tax on an option that would never be exercised.

| # | Decision | Consequence |
|---|---|---|
| **D1** | **SQL Server only.** No provider abstraction, no configuration switch. | Drops 3 of the 15 dual-plan steps outright and shrinks 8 more. No `Provider` key is needed in `database.json` after all. |
| **D2** | **Carry the existing 26 rows across**, ids preserved. | Adds a one-off conversion script (§7) with its own verification. |
| **D3** | **PostgreSQL is deleted last, not first.** | The working, V3-verified PostgreSQL build stays intact and runnable until SQL Server has passed the same attacks. Demolition is step 11, not step 1. |
| **D4** | Authentication: **both Windows Integrated and SQL login**, selected by `IntegratedSecurity`. | Carried over from the dual plan. |
| **D5** | Concurrency token: **app-managed `int version` column**. | Rationale revisited in §5.1 — the original reason changed, the conclusion did not. |
| **D6** | Name matching: **unique = case-insensitive + accent-sensitive; search = case- and accent-insensitive**. | Exact parity with today's `citext` + `unaccent()` behaviour, so no user-visible change. §5.3. |

**The dual-provider plan is not deleted from history.** If PostgreSQL is ever needed again, the design
work is recoverable from git rather than needing to be redone.

---

## 1. Why this is a rewrite of the data layer, not a connection-string change

I read every file in `src/LunchOrganizer.Data`. PostgreSQL is baked into eight distinct layers. The
size of this list is the real content of the plan.

| # | Coupling | Where | Action |
|---|---|---|---|
| 1 | `UseNpgsql(...)` | `Web/Program.cs:60`, `Mailer/Program.cs:88`, `LunchOrganizerDbContextFactory.cs:25`, `ConcurrencyTestFixture.cs:67` | → `UseSqlServer(...)` |
| 2 | `NpgsqlConnectionStringBuilder` | `DatabaseOptionsExtensions.cs` | → `SqlConnectionStringBuilder` |
| 3 | Bootstrapper: `NpgsqlConnection`, `pg_database`, `pg_advisory_lock`, `CREATE EXTENSION unaccent`, SqlStates `42P04`/`42501` | `DatabaseBootstrapper.cs` | Rewritten end to end (§6.3) |
| 4 | Column types `citext`/`timestamptz`/`numeric`/`text`/`boolean`, `now()` defaults, `UseIdentityAlwaysColumn()`, `HasPostgresExtension("citext")` | all 5 `Configurations/*.cs`, `LunchOrganizerDbContext.cs:22` | Retyped in place (§6.1) |
| 5 | The entire `Migrations/` folder (Npgsql annotations, `xid` rowversion columns) | `Migrations/*` | Deleted, regenerated fresh (§6.2) |
| 6 | Raw SQL `INSERT … ON CONFLICT … DO UPDATE … RETURNING … xmin` | `BookingRepository` (×2), `DailyPriceRepository`, `EmailLogRepository`, `EmployeeRepository` | Rewritten as `MERGE`/`WHERE NOT EXISTS` (§5.2) |
| 7 | Raw SQL `unaccent(...) ILIKE ...` + `42883` fallback | `EmployeeRepository.SearchByNameAsync` | Replaced by a collation clause; the fallback path disappears (§5.3) |
| 8 | `catch (PostgresException) when SqlState is "23505"/"23503"` | `MenuRepository` (×2), `EmployeeRepository` (×2), `EmployeeService.cs:68`, `ConcurrencyTests.cs:120` | → `SqlException.Number` (§6.4) |

Roughly 60 % of each repository is plain, provider-neutral LINQ and needs no change at all. The other
40 % — six raw-SQL statements and six exception filters — is where the work is.

**Note on #8:** `EmployeeService.cs:68` is the one place *outside* the Data project that inspects a
PostgreSQL exception type. That abstraction leak gets closed as part of this work rather than
re-created with a SQL Server type: the Data layer will translate to the existing
`DeleteRestrictedException` / `ErrorCodes` vocabulary so the Services project stops referencing a
database driver at all.

---

## 2. Configuration — the new `database.json`

With a single engine there is no `Provider` key and no nested blocks. The section keeps its name and
four of its keys, so operator-facing churn is limited to the connection fields.

### `config/database.json` (committed, placeholder credentials)

```jsonc
{
  "Database": {
    // ---- Connection ----
    "Server": "localhost\\SQLEXPRESS",
    "Database": "lunchorganizer",
    "IntegratedSecurity": true,
    "Username": "",
    "Password": "",
    "MaintenanceDatabase": "master",
    "Encrypt": true,
    "TrustServerCertificate": true,

    // Optional escape hatch — if non-empty, wins over every field above.
    "ConnectionString": "",

    // ---- Unchanged from the PostgreSQL build ----
    "AutoCreateDatabase": true,
    "Seed": false,
    "MaxPoolSize": 50,
    "CommandTimeoutSeconds": 30
  }
}
```

Removed: `Host`, `Port`. Changed default: `MaintenanceDatabase` `postgres` → `master`.
Added: `IntegratedSecurity`, `Encrypt`, `TrustServerCertificate`, `ConnectionString`.

**There is deliberately no `Port` field.** A SQL Server port belongs inside `Server` as `HOST,1433` —
that is the form SQL Server itself documents, and it is the only form that also expresses named
instances (`HOST\SQLEXPRESS`) and LocalDB (`(localdb)\MSSQLLocalDB`). A separate `Port` field would be
meaningless for two of those three cases.

### `config/database.local.json` (git-ignored, real credentials)

Only the keys that differ from the base file, exactly as today:

```jsonc
{
  "Database": {
    "Server": "SQLSRV01",
    "IntegratedSecurity": true,
    "Seed": false
  }
}
```

### The `ConnectionString` escape hatch

If non-empty it wins outright and every other connection field is ignored. Five lines of code that
cover everything the structured fields can never anticipate: Azure SQL with Entra ID, failover
partners, `ApplicationIntent=ReadOnly`, custom certificates. It is also the pressure valve if a field
turns out to be missing after deployment — an operator can unblock themselves without a rebuild.
`MaxPoolSize` and `CommandTimeoutSeconds` are still applied on top unless the string already sets them.

### Startup validation

Fail fast, in plain language, rather than surfacing as an obscure driver error later:
- `IntegratedSecurity: false` with a blank `Username` → explicit configuration error.
- Blank `Server` or `Database` → explicit configuration error.

---

## 3. The connection string

Built with `Microsoft.Data.SqlClient.SqlConnectionStringBuilder`:

| JSON field | SqlClient keyword | Note |
|---|---|---|
| `Server` | `Data Source` | `HOST`, `HOST,1433`, `HOST\INSTANCE`, `(localdb)\MSSQLLocalDB` — passed through verbatim |
| `Database` | `Initial Catalog` | |
| `IntegratedSecurity: true` | `Integrated Security=True` | Windows auth; `Username`/`Password` ignored |
| `IntegratedSecurity: false` | `User ID` + `Password` | SQL login |
| `Encrypt` | `Encrypt` | **Defaults to `true` in Microsoft.Data.SqlClient 4.0+** — the most common cause of "it worked with the old driver" |
| `TrustServerCertificate` | `TrustServerCertificate` | Must be `true` for a typical on-prem server with a self-signed certificate, else the handshake fails with *"The certificate chain was issued by an authority that is not trusted"* |
| `MaxPoolSize` | `Max Pool Size` | |
| — | `Application Name=LunchOrganizer` | Set unconditionally — makes the app identifiable in `sys.dm_exec_sessions` and DBA tooling. Free diagnostics. |

Windows auth:
```
Data Source=localhost\SQLEXPRESS;Initial Catalog=lunchorganizer;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Max Pool Size=50;Application Name=LunchOrganizer
```

SQL login:
```
Data Source=SQLSRV01,1433;Initial Catalog=lunchorganizer;User ID=lunchapp;Password=***;Encrypt=True;TrustServerCertificate=True;Max Pool Size=50;Application Name=LunchOrganizer
```

`CommandTimeoutSeconds` is applied via `options.UseSqlServer(cs, o => o.CommandTimeout(n))` rather than
as a connection-string keyword, so behaviour doesn't depend on driver version.

---

## 4. What ports cleanly (most of it)

Worth stating plainly, because it bounds the risk: **every integrity guarantee in the V2-validated
schema survives unchanged.**

- The composite foreign key `bookings(menu_id, booking_date) → menus(id, menu_date)` — the single most
  important guarantee in the schema — is supported identically, backed by the same
  `UNIQUE (id, menu_date)` alternate key.
- `UNIQUE (employee_id, booking_date)` and `UNIQUE (menu_date, menu_number)` — identical.
- All three CHECK constraints — identical syntax, ported verbatim.
- `ON DELETE RESTRICT` → `DeleteBehavior.Restrict` (emits `ON DELETE NO ACTION`); same practical
  effect, error 547 instead of 23503.
- `email_log`'s primary key on `summary_date`, which is what makes double-sending structurally
  impossible — identical.
- `DevelopmentSeeder` is plain EF Core and needs **no change at all**.
- All the LINQ in all five repositories — no change.

Nothing in the design has to be weakened to fit SQL Server.

---

## 5. The three genuinely hard problems

### 5.1 The concurrency token — decision revisited

All four mutable entities carry `public uint Version { get; set; }` mapped with `.IsRowVersion()`. On
PostgreSQL, Npgsql maps a `uint` rowversion to the **`xmin` system column** — zero storage, no column
in any `CREATE TABLE`. Genuinely elegant, and completely non-portable.

In the dual plan I chose an app-managed `int version` column specifically so the `uint Version`
contract would stay identical on both engines. **That reason no longer exists.** So the choice is
genuinely reopened, and `rowversion` — SQL Server's idiomatic, server-maintained 8-byte token — is now
viable. I re-examined it and am still recommending the app-managed `int`:

| | App-managed `int version` (chosen) | `byte[] RowVersion` |
|---|---|---|
| Domain entities | **Untouched.** `uint Version` keeps working. | `uint Version` → `byte[]` on 4 entities, which `Docs/` describes as *frozen contracts* |
| Blast radius | `DbContext` override + 4 entity configs | 4 Domain entities, 5 repositories' Attach+Modified path, `LunchOrganizer.Fakes`, `LunchOrganizer.Email/InMemory/`, tests |
| Who maintains it | Application (`SaveChangesAsync` override + `version = t.version + 1` in the MERGEs) | The server, automatically |
| Failure mode | A *future* hand-written raw `UPDATE` that forgets `version = version + 1` silently weakens the check | None |
| Storage | 8 bytes/row (`bigint` — see progress log in §8) | 8 bytes/row |

The `rowversion` failure mode is genuinely better. It loses on blast radius: it would ripple through
the Domain contracts, both fake implementations and the repository update path, to protect against a
hazard confined to six raw statements we are writing and reviewing right now. **§9 records the residual
risk** so it isn't forgotten: any future raw `UPDATE` against these tables must increment `version`.

Implementation:

```csharp
// LunchOrganizerDbContext — entity contract unchanged, uint Version keeps its meaning
public override Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
    {
        var version = entry.Metadata.FindProperty("Version");
        if (version is not null)
        {
            entry.Property("Version").CurrentValue = unchecked((uint)entry.Property("Version").CurrentValue! + 1);
        }
    }
    return base.SaveChangesAsync(ct);
}
```

`EmailLogEntry` has no `Version` property and gains none — it is written once per day via the
reserve-then-complete pattern, not edited concurrently.

### 5.2 The six atomic upserts

`INSERT … ON CONFLICT … DO UPDATE … RETURNING` has no SQL Server equivalent. The translation is
`MERGE` with an **explicit `HOLDLOCK`** and an `OUTPUT` clause:

```sql
MERGE bookings WITH (HOLDLOCK) AS t
USING (VALUES (@employeeId, @bookingDate, @menuId, @price)) AS s (employee_id, booking_date, menu_id, price_snapshot)
    ON t.employee_id = s.employee_id AND t.booking_date = s.booking_date
WHEN MATCHED THEN
    UPDATE SET menu_id        = s.menu_id,
               price_snapshot = s.price_snapshot,
               updated_at_utc = CAST(SYSUTCDATETIME() AS datetimeoffset),
               version        = t.version + 1
WHEN NOT MATCHED THEN
    INSERT (employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc, version)
    VALUES (s.employee_id, s.booking_date, s.menu_id, s.price_snapshot,
            CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1)
OUTPUT inserted.id, inserted.employee_id, inserted.booking_date, inserted.menu_id,
       inserted.price_snapshot, inserted.created_at_utc, inserted.updated_at_utc, inserted.version;
```

**`WITH (HOLDLOCK)` is not optional and is the highest-risk detail in this plan.** A bare `MERGE` takes
only an update lock on matched rows; two concurrent MERGEs on a *non-existent* key can both fall
through to `WHEN NOT MATCHED`, and one gets a primary-key violation. `HOLDLOCK` (= `SERIALIZABLE`)
makes the server take a range lock on the key, which is what makes this genuinely equivalent to
`ON CONFLICT`. This is a well-known SQL Server footgun. It will be **proven by the concurrency suite,
not asserted** (§8.6).

For `ON CONFLICT DO NOTHING` — the mailer's day reservation and employee get-or-create — the simpler
and safer form is a guarded insert:

```sql
INSERT INTO email_log (summary_date, sent_at_utc, status, recipients, booking_count, error_message)
SELECT @date, CAST(SYSUTCDATETIME() AS datetimeoffset), @status, NULL, 0, NULL
WHERE NOT EXISTS (SELECT 1 FROM email_log WITH (UPDLOCK, HOLDLOCK) WHERE summary_date = @date);
```

`@@ROWCOUNT` is 1 for the winner and 0 for the loser — exactly the contract
`IEmailLogRepository.TryBeginAsync` already documents. `UPDLOCK, HOLDLOCK` on the existence check is
what serialises the racers.

### 5.3 Case and accent sensitivity — a trap worth stating twice

`citext` is **case-insensitive but accent-sensitive**. Today "ALICE MARTIN" collides with "Alice Martin"
(correctly rejected as a duplicate), while "Chloé" and "Chloe" are two different people. Search is
*separately* made accent-insensitive by `unaccent()`.

The tempting SQL Server move — one `CI_AI` collation on the column — **would silently change
behaviour**: it makes the *unique index* accent-insensitive too, so "Chloe Bernard" could never be
registered in a database that already contains "Chloé Bernard". A real employee would be rejected as a
duplicate.

The faithful translation uses two collations for two different jobs:

- **Column collation `Latin1_General_CI_AS`** (case-insensitive, accent-**sensitive**) — exact `citext`
  parity for the unique index and for `WHERE e.FullName == fullName`.
- **Explicit `COLLATE Latin1_General_CI_AI` in the search predicate only** — reproduces `unaccent()`:
  ```sql
  WHERE full_name COLLATE Latin1_General_CI_AI LIKE @pattern COLLATE Latin1_General_CI_AI
  ```

This is a **net simplification**: no extension to install, no privilege to grant, and the entire
`42883` "unaccent isn't available" fallback path in `EmployeeRepository` disappears. The
`%chloe%` → `Chloé Bernard` behaviour verified at V3 is preserved.

While rewriting this query I'll also escape `LIKE` metacharacters (`%`, `_`, `[`) in the fragment.
Today's PostgreSQL path doesn't escape them either — a pre-existing cosmetic bug (a user typing `%`
matches everything), not a security issue, and cheap to fix while the code is open.

---

## 6. Schema and infrastructure mapping

### 6.1 Types

| Concept | PostgreSQL (today) | SQL Server (new) |
|---|---|---|
| `employees.full_name` | `citext` | `nvarchar(200) COLLATE Latin1_General_CI_AS` |
| free text (`description`, `email`, `recipients`, `error_message`) | `text` | `nvarchar(max)` |
| `email_log.status` | `text` + `HasConversion<string>()` | `nvarchar(20)` + same conversion |
| `is_active` | `boolean` | `bit` |
| ids, counts | `integer` | `int` |
| `*_date` | `date` | `date` |
| `*_at_utc` | `timestamptz` | `datetimeoffset(7)` |
| money | `numeric(10,2)` | `decimal(10,2)` |
| audit default | `now()` | `CAST(SYSUTCDATETIME() AS datetimeoffset)` — yields a `+00:00` offset, matching the `_utc` column name. (`SYSDATETIMEOFFSET()` would record the server's local offset.) |
| surrogate key | `GENERATED ALWAYS AS IDENTITY` (`UseIdentityAlwaysColumn()`) | `IDENTITY(1,1)` (`UseIdentityColumn()`) |
| concurrency | `xmin` system column | `version int NOT NULL DEFAULT 1` (§5.1) |

`GENERATED ALWAYS` → `IDENTITY(1,1)` is a small semantic loosening: PostgreSQL forbids inserting an
explicit id outright, whereas SQL Server permits it under `SET IDENTITY_INSERT ON`. That is not a
regression to work around — it is precisely the mechanism the data migration in §7 depends on.

### 6.2 Migrations

The `Migrations/` folder is deleted and a single fresh `InitialCreate` is generated against the SQL
Server model. **No migration-ID preservation is needed** — this was the highest-risk step in the dual
plan and it disappears entirely, because the SQL Server database has no `__EFMigrationsHistory` to
protect.

`LunchOrganizerDbContextFactory` (design-time only, used by `dotnet ef`) gets a SQL Server
connection string. It remains never-used at runtime.

### 6.3 Bootstrapper

`IDatabaseBootstrapper` keeps its interface and `DatabaseBootstrapper` keeps its shape
(create-if-missing → lock → migrate → unlock → seed → log). Only the internals change:

| Step | PostgreSQL (today) | SQL Server (new) |
|---|---|---|
| Does the DB exist? | `SELECT 1 FROM pg_database WHERE datname = @n` | `SELECT 1 FROM sys.databases WHERE name = @n` |
| Create it | `CREATE DATABASE "name"` | `CREATE DATABASE [name]` |
| Maintenance DB | `postgres` | `master` |
| Migration lock | `pg_advisory_lock(872615001)` | `EXEC sp_getapplock @Resource='LunchOrganizer_Migrate', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=60000` |
| Release | `pg_advisory_unlock(...)` | `EXEC sp_releaseapplock @Resource='LunchOrganizer_Migrate', @LockOwner='Session'` |
| Extensions | `citext` (required), `unaccent` (opportunistic) | **none** — collations replace both; the whole extension block is deleted |
| "Already exists" race | tolerate `42P04` | tolerate `1801` |
| No permission | `42501` → *"grant CREATEDB"* | `262` → *"grant the `dbcreator` role or CREATE DATABASE permission"* |

Two SQL Server specifics:
- The existing code defensively rejects `"` in the database name. The SQL Server equivalent must reject
  `]` (bracket-quoted identifier). Same intent, different metacharacter.
- `sp_getapplock` with `@LockOwner='Session'` requires the lock to be taken and released on the **same
  open connection**. The existing code already holds one connection open across the migration, so the
  structure carries over unchanged.

### 6.4 Error numbers

| Condition | PostgreSQL `SqlState` | SQL Server `SqlException.Number` |
|---|---|---|
| Unique violation | `23505` | `2627` (constraint) **and** `2601` (unique index) — **both** must be handled |
| FK violation | `23503` | `547` |
| Database already exists | `42P04` | `1801` |
| Cannot create database | `42501` | `262` |

### 6.5 Packages

`LunchOrganizer.Data.csproj`: drop `Npgsql.EntityFrameworkCore.PostgreSQL`, add
`Microsoft.EntityFrameworkCore.SqlServer` at a version matching the pinned EF Core 10.0.11.
`Microsoft.EntityFrameworkCore.Design` stays. No other project's packages change.

---

## 7. Data migration — the 26 rows

Measured directly against the live database, not estimated:

| Table | Rows | Identity? |
|---|---|---|
| `employees` | 5 | yes |
| `menus` | 6 | yes |
| `bookings` | 9 | yes |
| `daily_prices` | 5 | no (PK is `price_date`) |
| `email_log` | 1 | no (PK is `summary_date`) |
| **Total** | **26** | |

Approach — a one-off, reviewable script, run once after the SQL Server schema exists:

1. Export each table from PostgreSQL as ordered `INSERT` statements (`psql` at
   `D:\PostgreSQL\bin\psql.exe`, which is not on `PATH`).
2. Insert in FK order: `employees` → `menus` → `daily_prices` → `bookings` → `email_log`.
3. `SET IDENTITY_INSERT … ON` around the three identity tables so ids are preserved — **this matters**:
   `bookings.employee_id` and `bookings.menu_id` must keep pointing at the same rows, and the composite
   FK `(menu_id, booking_date)` must still resolve.
4. Set `version = 1` on every migrated row (there is no `xmin` value to carry over; it was never
   application-visible).
5. `DBCC CHECKIDENT (…, RESEED, <max id>)` on all three identity tables, so the next insert doesn't
   collide with a migrated id.
6. Convert `timestamptz` → `datetimeoffset` — a straight value copy; both store an instant with an
   offset, so no arithmetic is involved.

Verification: row counts match per table; every `booking` still resolves to its employee and menu;
`SELECT` the same report for the same date range on both databases and diff the output.

At 26 rows this is small enough to eyeball the entire result set, which is the real quality control.

---

## 8. Work breakdown

Ordered so the tree builds throughout, and so **PostgreSQL keeps working until SQL Server has proven
itself** (D3). Demolition is step 11.

| # | Step | Files | Risk |
|---|---|---|---|
| 1 | ✅ **DONE** — Restructure `DatabaseOptions` for SQL Server (`Server`, `IntegratedSecurity`, `Encrypt`, `TrustServerCertificate`, `ConnectionString`; drop `Host`/`Port`) + `Validate()` | `Domain/Configuration/DatabaseOptions.cs` | Low |
| 2 | ✅ **DONE** — New `config/database.json` + `database.local.json` shape | `config/*.json` | Low |
| 3 | ✅ **DONE** — `BuildConnectionString` → `SqlConnectionStringBuilder` | `Data/DatabaseOptionsExtensions.cs` | Low |
| 4 | ✅ **DONE** — Retype the 5 entity configurations; drop `HasPostgresExtension`; `UseIdentityColumn()`; add `version` column; column collation | `Data/Configurations/*`, `LunchOrganizerDbContext.cs` | Medium |
| 5 | ✅ **DONE** — `SaveChanges`/`SaveChangesAsync` overrides incrementing `Version` (§5.1) | `LunchOrganizerDbContext.cs` | Medium |
| 6 | ✅ **DONE** — Rewrite the 6 raw SQL statements as `MERGE`/guarded-`INSERT` (§5.2) + the search query (§5.3) | 5 repositories | **High** |
| 7 | ✅ **DONE** — Exception filters → `SqlException.Number`; close the `EmployeeService` driver leak (§1 note) | 5 repositories, `Services/EmployeeService.cs` | Medium |
| 8 | ✅ **DONE** — Rewrite `DatabaseBootstrapper` internals (§6.3) | `Data/DatabaseBootstrapper.cs` | Medium |
| 9 | ✅ **DONE** — Swap packages; `UseSqlServer` in Web + Mailer + design-time factory; delete `Migrations/`, generate fresh `InitialCreate` | `*.csproj`, 2 `Program.cs`, `Migrations/` | Medium |
| 10 | ✅ **DONE** (pulled into step 9) — Port `ConcurrencyTestFixture` + `ConcurrencyTests` to SQL Server | `tests/…/Concurrency/*` | Medium |
| **—** | **VERIFICATION GATE — §9 must pass before step 11** | | |
| 11 | **Demolition:** remove every remaining PostgreSQL reference — `Scripts/create_database.{sql,ps1}`, comments, XML docs saying "Npgsql-backed", `Docs/DATABASE_SCHEMA.md`, `INSTALLATION.md`, `INSTALLATIONFORDUMMIES.md`, `README.md`, `Scripts/README.md`, `AGENT_BACKEND_SUMMARY.md` | repo-wide | Low |
| 12 | `Scripts/create_database.sql` + `.ps1` rewritten for SQL Server (offline install path) | `Scripts/` | Low |
| 13 | Data migration script + run (§7) | `Scripts/` | Medium |

Step 11 is deliberately a single, reviewable sweep: `grep -ri "npgsql\|postgres\|citext\|xmin\|ILIKE"`
must return **zero hits outside git history** when it is done. That grep is the completion criterion.

### Progress log

**Steps 1–5 completed 2026-08-18.** `dotnet build LunchOrganizer.sln -c Debug` → **0 errors, 0 warnings**,
verified independently of the implementing agent's report.

Two corrections to this plan, discovered during implementation:

1. **Steps 1–5 do not compile in isolation** — the "ordered so the tree builds throughout" claim in §8
   was wrong for this slice. Two items had to be pulled forward:
   - **From step 9:** the `Microsoft.EntityFrameworkCore.SqlServer` 10.0.11 package reference, because
     step 3 needs `SqlConnectionStringBuilder` and step 4 needs `UseIdentityColumn()`. It was **added
     alongside** Npgsql, not in place of it — `DatabaseBootstrapper` and both `Program.cs` files still
     reference Npgsql and still compile.
   - **From step 10:** `ConcurrencyTestFixture.cs` set `DatabaseOptions.Host`/`.Port` in two places and
     called `BuildConnectionString()`. It now builds its PostgreSQL connection string inline from its
     own JSON DTO, so it compiles without depending on the migrated options type.
2. **The `version` column is `bigint`, not `int`** (§5.1 said 4 bytes; it is 8). The CLR property is
   `uint`, whose range exceeds `int`, so EF Core maps it to `bigint` natively. Forcing `int` would have
   required a value converter and overflow handling to save 4 bytes per row — not worth it. This makes
   the storage cost identical to `rowversion`, which slightly weakens (but does not reverse) the §5.1
   argument: the decision still rests on blast radius, not on storage.

**Known-broken between here and step 9 — expected, not a defect:**
`BuildConnectionString()` now emits a SQL Server connection string, but `DatabaseBootstrapper` still
feeds it to `NpgsqlConnection` and both `Program.cs` files still call `UseNpgsql(...)`. The solution
**compiles but cannot connect to any database** until step 9. Additionally, from step 4 onward the EF
model no longer matches the PostgreSQL migration in `Migrations/`, so the PostgreSQL runtime path is
dead regardless.

This refines D3: *"PostgreSQL is deleted last"* means its **code, scripts and documentation** survive
until the verification gate passes — it does **not** mean the app keeps running on PostgreSQL through
the middle of the migration. The live PostgreSQL database itself is untouched and still holds the 26
rows; `config/database.local.json`'s previous PostgreSQL credentials were backed up outside the repo
before being overwritten.

**Concurrency tests are skipped, not passing.** All six `[Fact]`s in `ConcurrencyTests.cs` carry
`Skip = "Ported to SQL Server in step 10 of Docs/IMPLEMENTATION_PLAN_SQLSERVER.md"`. They target
PostgreSQL and cannot pass against the migrated model. Step 10 must remove every one of those `Skip`
arguments; a green suite with them still in place is not a passing gate.

> **Superseded at step 9** — the fixture was ported and all six `Skip` arguments were removed, so the
> tests are live again. They have not yet been *run*; see the §9 gate.

---

**Steps 6–9 completed 2026-08-18**, plus the step-10 fixture port (forced early — see below).
`dotnet build LunchOrganizer.sln -c Debug --no-incremental` → **0 errors, 1 warning** (pre-existing
`CS9113` in `LunchOrganizer.Email/DailySummaryMailService.cs`, untouched by this migration).

Verified independently of the implementing agents:
- No live Npgsql reference anywhere in `src` or `tests` — no `using Npgsql`, `UseNpgsql`,
  `NpgsqlConnection`, `PostgresException`, or `Npgsql.*`. The package reference is gone from
  `LunchOrganizer.Data.csproj`. Remaining textual hits are explanatory prose only (comments saying
  *why* the SQL Server design differs from `xmin`/`citext`/`unaccent`), which are worth keeping.
- New migration `20260818092820_InitialCreate` inspected by eye: five tables; `full_name` as
  `nvarchar(200)` collated `Latin1_General_CI_AS`; `version bigint` on the four mutable tables and
  **absent from `email_log`**; all three CHECK constraints; `AK_menus_id_menu_date`; the composite FK
  `bookings(menu_id, booking_date) → menus(id, menu_date)` with `ReferentialAction.Restrict`; all five
  indexes; `datetimeoffset(7)` audit columns with `CAST(SYSUTCDATETIME() AS datetimeoffset)` defaults;
  no `citext`, no `xid`, no Npgsql annotations.
- All six concurrency-test `Skip` arguments removed — the suite is live again.

### Two defects found and fixed during these steps

Both would have compiled cleanly and failed only at runtime; recording them so the class of error is
remembered.

1. **`ORDER BY` inside composed raw SQL** (`EmployeeRepository.SearchByNameAsync`). `.Take(take)` is a
   composing LINQ operator, so EF Core wraps the raw SQL in a derived table. PostgreSQL permits
   `ORDER BY` in a subquery; **SQL Server rejects it** (error 1033) unless `TOP`/`OFFSET`/`FOR XML` is
   present. Fixed by moving the ordering out of the raw SQL into `.OrderBy(e => e.FullName)`. All other
   `FromSql*` call sites were audited: `AddAsync`'s read-back composes via `SingleAsync` but has no
   `ORDER BY`; the three `MERGE` sites use only `AsNoTracking()`, which does not compose. **General
   rule for this codebase: raw SQL that is composed over must not contain a bare `ORDER BY`.**
2. **`sp_getapplock` return value binding.** The first implementation used `CommandType.Text` with
   `"EXEC @result = sp_getapplock …"` and a `ParameterDirection.ReturnValue` parameter. SqlClient does
   not reliably bind a return-value parameter to a same-named T-SQL variable inside a text batch; had
   it not bound, `(int)param.Value` would have thrown **inside the migration path**. Replaced with the
   canonical `CommandType.StoredProcedure` form, which is unambiguous.

### ⚠ OPEN DEFECT — menu numbering race under parallel insert

Found 2026-08-18 when the concurrency suite was run for the first time against SQL Server LocalDB.

**Result: 5 of 6 passed.** That is a substantial de-risking — the `MERGE … WITH (HOLDLOCK)` upserts,
the guarded inserts, the composite FK and the delete guards all behaved correctly under genuine
parallel load. The one failure:

```
TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers
SqlException: Cannot insert duplicate key row in object 'dbo.menus'
with unique index 'ix_menus_menu_date_menu_number'.
The duplicate key value is (2027-04-10, 5).
```

**Diagnosis — not an error-mapping bug.** `SqlServerErrors.IsUniqueViolation` correctly matches 2601
*and* 2627, so the retry filter does fire. The problem is that `MenuRepository.AddAsync` uses
**read-then-insert with bounded retries**: `SELECT MAX(menu_number) + 1`, then `INSERT`, retrying on a
unique violation up to `BusinessRules.MaxInsertRetryAttempts` = **3**. The test fires **10** concurrent
inserts for the same date. Under SQL Server's default READ COMMITTED, many racers read the same `MAX`
before any of them inserts, so three attempts are not enough to converge.

**This is a pre-existing design weakness, not something the migration introduced.** The algorithm was
always probabilistic; PostgreSQL happened to win the race and SQL Server does not. Raising the retry
count would only lower the probability, not remove it.

**Proposed fix — make the numbering atomic, matching the pattern used everywhere else in this
codebase.** One statement, with a range lock on the date's rows so concurrent inserts for the same
date serialise:

```sql
INSERT INTO menus (menu_date, menu_number, description, created_at_utc, updated_at_utc, version)
OUTPUT inserted.id, inserted.menu_date, inserted.menu_number, inserted.description,
       inserted.created_at_utc, inserted.updated_at_utc, inserted.version
SELECT {menuDate},
       COALESCE((SELECT MAX(menu_number) FROM menus WITH (UPDLOCK, HOLDLOCK)
                 WHERE menu_date = {menuDate}), 0) + 1,
       {description},
       CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1;
```

`UPDLOCK, HOLDLOCK` on the `MAX` subquery is what makes this deterministic rather than lucky — the
same reasoning as the `MERGE … WITH (HOLDLOCK)` in §5.2 and the guarded inserts in §5.2's second half.

**Note on the contract:** this makes the menu number server-assigned and ignores the caller-supplied
`Menu.MenuNumber`. That is already the effective behaviour — the existing retry loop overwrites
`menu.MenuNumber` on every attempt after the first. The change should be reflected in
`IMenuRepository.AddAsync`'s XML doc.

Keeping the retry loop afterwards is optional; with the range lock it should never trigger.

### Scope pulled forward

The **step-10 fixture port happened here**, not in step 10. Removing the Npgsql package broke
`ConcurrencyTestFixture.cs`, which still used `NpgsqlConnection`, so it had to be ported in the same
step: `SqlConnection`, `master` as the maintenance database, the new config DTO shape, and
`ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE` before `DROP` (SQL Server blocks a drop with
live sessions, where PostgreSQL used `pg_terminate_backend`). Step 10 is therefore now **complete**,
and the next action is the §9 verification gate.

**Nothing in steps 1–9 has been executed against a database.** Every claim above is from compilation
and code inspection only. The `MERGE`/`HOLDLOCK` correctness in §5.2 — the highest risk in this
migration — remains entirely unproven until §9.5 runs.

---

## 9. Verification gate

The V3 sign-off in `AGENT_BACKEND_SUMMARY.md` set the bar: guarantees were *attacked directly in SQL*
and confirmed rejected by the server. The same standard applies, and **step 11 does not start until
every item below passes.**

1. `dotnet build` clean; `dotnet test` green — all pre-existing tests, ported.
2. ~~Fresh run against a **non-existent** database: the bootstrapper creates it, all 5 tables plus
   `__EFMigrationsHistory` exist, and `Seed: true` populates 8 employees / 45 menus / 8 bookings /
   3 prices.~~
   **✅ ALREADY VERIFIED by Massimo Fauro, 2026-08-18** — database creation exercised with both
   `Seed: true` and `Seed: false`. This item is closed; do not re-run it, and do not drop/recreate the
   `lunchorganizer` database on LocalDB to re-test it (see the note in §10, now superseded).
   Still open from this area: nothing.
3. **The five V3 attacks, re-run against SQL Server**, each confirmed rejected *by the server*, not by
   application code:
   - booking a menu on a date other than the menu's own `menu_date` → **547**
   - a second lunch for the same employee on the same day → **2627/2601**
   - `ALICE MARTIN` alongside `Alice Martin` → rejected by the `CI_AS` unique index
   - deleting an employee who has bookings → **547**
   - creating "Menu 0" → CHECK constraint violation
4. **Both halves of §5.3 together**: `Chloe Bernard` **is** accepted alongside `Chloé Bernard`, *and*
   searching `chloe` **does** match `Chloé Bernard`. Getting one right and the other wrong is the
   likeliest subtle failure in this whole migration.
5. The six §11.11 concurrency scenarios, with genuine parallel round trips — **this is where the
   `MERGE`/`HOLDLOCK` correctness of §5.2 stands or falls.** Per-test durations get checked, as at V3,
   to confirm the tests aren't accidentally stubbing out rather than hitting the server.
6. Optimistic concurrency proven live: two concurrent edits to the same menu, the loser gets
   `DbUpdateConcurrencyException`, and `version` is confirmed to have incremented.
7. Mailer end-to-end, including its preflight and the double-send reservation (run it twice for the
   same date; exactly one email).
8. Data migration (§7) verified: row counts match, all 9 bookings resolve to the right employee and
   menu, and the admin report for the same date range is identical on both databases.
9. Web app runs, books a lunch, and renders the admin report in French.

### Residual risks recorded

- **Any future hand-written raw `UPDATE`** against `employees`/`menus`/`bookings`/`daily_prices` must
  increment `version`, or optimistic concurrency silently weakens for that path (§5.1). To be stated in
  `DATABASE_SCHEMA.md` and in the backend agent's non-negotiable constraints.
- **Any future upsert** must carry `WITH (HOLDLOCK)`. Same reason, same places.

---

## 10. Test environment — confirmed 2026-08-18

Probed directly on the development laptop; no full SQL Server engine is installed, but **LocalDB is
present, running, and sufficient for every check in §9**.

| Property | Value |
|---|---|
| Instance | `(localdb)\MSSQLLocalDB` — running, auto-create enabled |
| Version | SQL Server 2025 (RTM-CU3) **17.0.4025.3**, Express Edition |
| Authentication | Windows, `COHU\mfauro`, **sysadmin** |
| `CREATE DATABASE` | permitted (`HAS_PERMS_BY_NAME` = 1) |
| Server collation | `SQL_Latin1_General_CP1_CI_AS` |
| Tooling | `sqlcmd` (ODBC 17) and `SqlLocalDB.exe`, both on `PATH` |
| Transport | named pipes only; nothing listening on TCP 1433 |
| No engine service | only `SQLWriter` (VSS) — client components, not an engine |

### Verified capabilities (live, before implementation)

- **Both collations exist and behave exactly as §5.3 requires** — this de-risks the subtlest part of
  the migration:
  - `Latin1_General_CI_AS`: `'ALICE MARTIN' = 'Alice Martin'` → **equal** (duplicate, correct)
  - `Latin1_General_CI_AS`: `'Chloe' = 'Chloé'` → **different** (two people, correct)
  - `Latin1_General_CI_AI`: `'Chloe' = 'Chloé'` → **equal** (search matches, correct)
- `sp_getapplock` / `sp_releaseapplock` both return 0 — §6.3's migration lock is sound.

### ⚠ LocalDB does not support encryption

Connecting with encryption on fails: *"Encryption not supported on SQL Server."* `config/database.json`
therefore keeps `Encrypt: true` (correct for a real server, and matching Microsoft.Data.SqlClient
4.0+'s own default), while `config/database.local.json` overrides it to `false` **for LocalDB only**.
That override must not be copied to a production config.

### ⚠ LocalDB is necessary but not sufficient for the concurrency gate

LocalDB is a full engine — `MERGE`, `HOLDLOCK`, `sp_getapplock` and both collations all work — but it
is a lightweight single-user instance over named pipes. If it serialises parallel work more
aggressively than a real server, a `HOLDLOCK` defect could pass here and still fail on a production
instance. **§9.5 must be re-run against the real target server before go-live**; a green run on
LocalDB alone does not close that risk.

### Pre-existing empty database

An empty `lunchorganizer` database (0 tables, files under `C:\Users\mfauro\`) was found on LocalDB,
created during this session — most likely by a sub-agent probing connectivity; origin not established
with certainty. **It is to be dropped immediately before §9.2**, so the create-if-missing path is
genuinely exercised rather than skipped. Approved 2026-08-18.

---

## 10b. Outstanding input

**SQL Server connection details**, needed before step 6 can be verified (steps 1–5 do not need them):

1. **Server / instance** as it should appear in `Data Source` — `localhost\SQLEXPRESS`,
   `(localdb)\MSSQLLocalDB`, `SQLSRV01`, `SQLSRV01,1433`.
2. **Edition and version** (Express / LocalDB / Developer / Standard; 2019 / 2022). LocalDB is
   sufficient — it supports `MERGE`, `HOLDLOCK`, `sp_getapplock`, collations and `CREATE DATABASE`,
   which is everything this plan relies on.
3. **Authentication** — "Windows auth, my session has rights", or a SQL login and password.
4. **Can the login `CREATE DATABASE`** (`dbcreator` role or equivalent)? If not, the auto-create path
   can't be exercised and `Scripts/create_database.sql` becomes the only install route — better to know
   before than after.

These go into `config/database.local.json`, which is git-ignored; no credential reaches the repository.

## 11. Out of scope

- Any change to schema design, business rules, or UI behaviour.
- Any git operation. (Standing instruction.)
- Supporting PostgreSQL. That is the point of this document.
