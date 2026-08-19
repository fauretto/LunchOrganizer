# Test Plan — SQL Server migration + confirmation emails

**Written 18 August 2026. Self-contained: everything needed to run this is below.**

This is the single consolidated test plan for three changes that are all awaiting validation:

1. **The PostgreSQL → SQL Server migration** (`IMPLEMENTATION_PLAN_SQLSERVER.md`, steps 1–10 done)
2. **Per-employee booking confirmation emails** (`IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md`, done)
3. **Mandatory email when self-registering** (`IMPLEMENTATION_PLAN_REGISTRATION_EMAIL.md`, done 18 Aug)

They landed one after the other with no validation in between, so they must be verified together.
Change 3 is what makes change 2 actually reach self-registered employees — before it, everyone who
registered from the booking page was stored with `email = NULL` and silently skipped.

> **`Docs/TEST_CHECKLIST.md` is now partly stale** — it was written for the PostgreSQL build and its
> setup commands use `psql`. Its UI-flow sections (§A, §B) are still valid and still unexecuted; only
> its database commands are obsolete. Use the SQL Server equivalents in §0 below.

---

## Status at a glance

| Area | State |
|---|---|
| Build | ✅ 0 errors, 0 warnings |
| Automated tests | ✅ 39/39 email · ✅ unit suite green · ⚠️ 5/6 concurrency (see D1) |
| Database creation (`Seed` true **and** false) | ✅ **verified by Massimo, 2026-08-18** — closed, do not re-run |
| `MERGE`/`HOLDLOCK` upserts under parallel load | ✅ proven by 5 passing concurrency tests |
| Integrity attacks (§2) | ❌ not run |
| Accent/case rules end-to-end (§3) | ❌ not run |
| Confirmation emails (§4) | ❌ not run |
| Registration email requirement (§4b) | ❌ not run — new, 18 Aug |
| Mailer end-to-end (§5) | ❌ not run |
| Data migration, 26 rows (§6) | ❌ not started — step 13 |
| **Open defect** | ❌ **D1 — menu numbering race. Fix before sign-off.** |

**Nothing here may be signed off while D1 is open.**

---

## 0. Setup

### Connect to the database

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d lunchorganizer
```

`-E` = Windows auth · `-C` = trust server certificate. **Do not add `-N`** — LocalDB does not support
encryption and the connection will be refused.

See what the database really holds (the SQL Server replacement for the old `psql` one-liner in
`TEST_CHECKLIST.md`):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -d lunchorganizer -Q `
"SELECT b.booking_date, m.menu_number, e.full_name, b.price_snapshot
 FROM bookings b
 JOIN employees e ON e.id = b.employee_id
 JOIN menus m ON m.id = b.menu_id
 ORDER BY 1, 2;"
```

### Run the app

```powershell
cd C:\Projects_Git\Data\GitPerso\LunchOrganizer
dotnet run --project src/LunchOrganizer.Web
```

Admin login is `massimo` / `changeme`.

### Environment (confirmed 2026-08-18)

SQL Server 2025 (RTM-CU3) Express, `17.0.4025.3`, instance `(localdb)\MSSQLLocalDB`, Windows auth as
`COHU\mfauro` (sysadmin, can `CREATE DATABASE`). Server collation `SQL_Latin1_General_CP1_CI_AS`.
Named pipes only — nothing on TCP 1433.

---

## 1. Automated tests

```powershell
dotnet build LunchOrganizer.sln -c Debug
dotnet test
```

- [ ] Build succeeds, 0 errors
- [ ] All email tests pass (39 at time of writing)
- [ ] All unit tests pass
- [ ] All 6 concurrency tests pass — **currently 5/6, blocked by D1**

Run one group only:
```powershell
dotnet test --filter "FullyQualifiedName~LunchOrganizer.Tests.Email"
dotnet test --filter "FullyQualifiedName~LunchOrganizer.Tests.Concurrency"
```

The concurrency tests create and drop their own `lunchorganizer_test` database; they never touch
`lunchorganizer`.

---

## 2. Integrity attacks — the database must refuse all five

These are the five guarantees that were proven on PostgreSQL at the V3 sign-off. Each must be **refused
by SQL Server itself**, not by application code. Run each against a scratch date so nothing real is
disturbed.

Setup:
```sql
INSERT INTO menus (menu_date, menu_number, description, created_at_utc, updated_at_utc, version)
VALUES ('2027-09-01', 1, 'Attack test menu',
        CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1);
INSERT INTO employees (full_name, email, is_active, created_at_utc, updated_at_utc, version)
VALUES ('Attack Tester', NULL, 1,
        CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1);
```

**2.1 — A booking must not reference a menu on a different date** (composite FK; the single most
important guarantee in the schema). Expect **error 547**.
```sql
INSERT INTO bookings (employee_id, booking_date, menu_id, price_snapshot, created_at_utc, updated_at_utc, version)
SELECT e.id, '2027-09-02', m.id, 12.50,
       CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1
FROM employees e, menus m
WHERE e.full_name = 'Attack Tester' AND m.menu_date = '2027-09-01' AND m.menu_number = 1;
```
- [ ] Rejected, error 547

**2.2 — One employee cannot book two lunches on the same day.** Insert the booking correctly first
(same as above but `booking_date = '2027-09-01'`), then run it a second time. Expect **2627 or 2601**.
- [ ] First insert succeeds
- [ ] Second insert rejected, error 2627 or 2601

**2.3 — Employee names are case-insensitive.** Expect **2627/2601** from `ix_employees_full_name`.
```sql
INSERT INTO employees (full_name, email, is_active, created_at_utc, updated_at_utc, version)
VALUES ('ATTACK TESTER', NULL, 1,
        CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1);
```
- [ ] Rejected

**2.4 — An employee with bookings cannot be deleted.** Expect **error 547**.
```sql
DELETE FROM employees WHERE full_name = 'Attack Tester';
```
- [ ] Rejected, error 547

**2.5 — "Menu 0" cannot exist.** Expect a **CHECK constraint violation**
(`ck_menus_menu_number_positive`).
```sql
INSERT INTO menus (menu_date, menu_number, description, created_at_utc, updated_at_utc, version)
VALUES ('2027-09-01', 0, 'Should fail',
        CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1);
```
- [ ] Rejected

Cleanup:
```sql
DELETE FROM bookings WHERE booking_date = '2027-09-01';
DELETE FROM menus WHERE menu_date = '2027-09-01';
DELETE FROM employees WHERE full_name = 'Attack Tester';
```

---

## 3. Accent and case rules — **both halves must hold**

This is the likeliest subtle failure in the whole migration. PostgreSQL's `citext` was
case-insensitive but accent-**sensitive**, with accent-insensitive *search* layered on via `unaccent()`.
SQL Server reproduces that with two different collations. Getting one right and the other wrong is
easy and would not be caught by any other test here.

**3.1 — "Chloe" and "Chloé" are different people** (column collation `Latin1_General_CI_AS`). Both
inserts must **succeed**:
```sql
INSERT INTO employees (full_name, email, is_active, created_at_utc, updated_at_utc, version)
VALUES ('Chloé Bernard', NULL, 1, CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1),
       ('Chloe Bernard', NULL, 1, CAST(SYSUTCDATETIME() AS datetimeoffset), CAST(SYSUTCDATETIME() AS datetimeoffset), 1);
```
- [ ] **Both rows inserted.** If the second is rejected, the column collation is wrong (`CI_AI`
      instead of `CI_AS`) and a real employee could never be registered.

**3.2 — but searching "chloe" finds "Chloé Bernard"** (search overrides to `Latin1_General_CI_AI`):
```sql
SELECT full_name FROM employees
WHERE full_name COLLATE Latin1_General_CI_AI LIKE '%chloe%' COLLATE Latin1_General_CI_AI;
```
- [ ] Returns **both** rows

**3.3 — the same through the UI.** Booking page → type `chloe` in the name box.
- [ ] Autocomplete offers `Chloé Bernard`

Cleanup: `DELETE FROM employees WHERE full_name IN ('Chloé Bernard', 'Chloe Bernard');`
(Skip the delete for any name that already existed in the seed data.)

---

## 4. Confirmation emails

The feature: when the daily summary goes to the kitchen, every employee who booked that day also gets
a personal confirmation — **but only if they have an email address on record.**

**Setup — the seeded data gives every employee an address, so it only exercises the happy path.**
Clear one to test the skip rule:
```sql
UPDATE employees SET email = NULL WHERE full_name = 'Alice Martin';
```
Note who has an address and who does not before you start:
```sql
SELECT full_name, email FROM employees ORDER BY full_name;
```

**4.1 — Dry run.** Pick a date that has bookings.
```powershell
dotnet run --project src/LunchOrganizer.Mailer -- --date 2026-08-18 --dry-run
```
- [ ] The pickup directory (`Email:PickupDirectory`, default `./mail-drop`) contains the kitchen
      summary **plus one file per employee who has an address**
- [ ] **No file** for the employee whose email was set to NULL
- [ ] Console reports `Confirmations: N sent, 1 skipped (no email), 0 failed`
- [ ] `email_log` was **not** touched (dry run):
      `SELECT * FROM email_log WHERE summary_date = '2026-08-18';`

**4.2 — Read one confirmation.** Open one of the generated employee files.
- [ ] Correct employee name, correct menu number and description, correct price and currency
- [ ] Correct language (`Email:Language`, default `fr`)
- [ ] **Contains no other employee's name** — a confirmation must never leak the day's full list

**4.3 — Real send.** Pickup-directory mode is sufficient; no SMTP server needed.
```powershell
dotnet run --project src/LunchOrganizer.Mailer -- --date 2026-08-18
```
- [ ] Summary and confirmations all produced
- [ ] `email_log` has exactly **one** row for the date, status `Sent`
- [ ] Process exit code is `0` (`echo $LASTEXITCODE`)

**4.4 — The toggle works.** Set `"SendEmployeeConfirmations": false` in `config/email.json`, pick a
fresh date with bookings, re-run.
- [ ] Summary is sent, **no** confirmations produced

**4.5 — A confirmation problem must not fail the run.** Set one employee's email to something the
sender will reject (`UPDATE employees SET email = 'broken@' WHERE …`), run for a fresh date.
- [ ] Summary still sends, exit code still `0`, failure counted in the console summary

Restore afterwards:
```sql
UPDATE employees SET email = 'alice.martin@cohu.com' WHERE full_name = 'Alice Martin';
```

---

## 4b. Registration email requirement (booking page)

New on 18 August. Nobody has clicked any of this — the automated tests cover the view model, not the
rendered page.

**4b.1 — The field appears and gates the button.** On the booking page, type a name that does not exist
(e.g. `Zzz Testperson`).
- [ ] The register panel appears and now shows an **email field** with a line explaining the address is
      used to send the booking confirmation
- [ ] The **confirm button is disabled** while the field is empty
- [ ] No validation error is shown on the untouched empty field
- [ ] Typing `bob` shows the invalid-address hint and the button stays disabled
- [ ] Typing `bob@example.com` clears the hint and enables the button **as you type**, not only on blur
- [ ] Confirm → the booking grid appears

Then check what was actually stored:
```sql
SELECT full_name, email, is_active FROM employees WHERE full_name = 'Zzz Testperson';
```
- [ ] The row exists, `email` is exactly what you typed, `is_active = 1`

**4b.2 — Both languages.** Switch to EN and repeat 4b.1 with a different name.
- [ ] Label, placeholder, explanation and error message are **all English** — no French leaking through

**4b.3 — The field does not leak between people.**
- [ ] Type an unknown name, type an address, then **cancel** and select an existing employee instead.
      Re-open the register panel for another unknown name — the email field must be **empty**, not
      still holding the previous address.

**4b.4 — Deactivated employee re-registering.** This is the case the fix in §4.3 of the plan addresses.
```sql
UPDATE employees SET is_active = 0, email = NULL WHERE full_name = 'Alice Martin';
```
- [ ] `Alice Martin` no longer appears in the autocomplete
- [ ] Typing `Alice Martin` offers to register her
- [ ] Registering with an address succeeds, and:
```sql
SELECT full_name, email, is_active FROM employees WHERE full_name = 'Alice Martin';
```
- [ ] `is_active` is back to `1` **and** `email` holds the new address — **not** NULL. A NULL here means
      the address was silently discarded, which is the exact bug this change fixed.
- [ ] Her **existing bookings are still intact** (reactivation must not have created a duplicate row):
      `SELECT COUNT(*) FROM employees WHERE full_name = 'Alice Martin';` returns `1`

**4b.5 — An existing address is never overwritten.** With an active employee who already has an email,
a re-registration attempt must leave the original address untouched. (Hard to reach through the UI —
the automated test covers it; check here only if you can produce the situation.)

**4b.6 — End to end, the whole point of the feature.** Book a lunch for the employee you registered in
4b.1, then run the mailer for that date with `--dry-run`.
- [ ] A confirmation file **is** produced for them, and they are counted in `sent` — **not** in
      `skipped (no email)`

Restore afterwards:
```sql
UPDATE employees SET is_active = 1, email = 'alice.martin@cohu.com' WHERE full_name = 'Alice Martin';
DELETE FROM employees WHERE full_name LIKE 'Zzz %';
```

---

## 5. Mailer end-to-end

- [ ] `--help` prints usage
- [ ] A date with **no** bookings → "no bookings", exit code `2`
- [ ] A **non-working day** (Saturday) → skipped, exit code `2`
- [ ] **Run twice for the same date** → the second run reports already-handled, exit code `3`, and
      **no duplicate emails are produced**. This is the `email_log` reservation doing its job; it is
      what stops the kitchen being emailed twice.
- [ ] Stop SQL Server (`sqllocaldb stop MSSQLLocalDB`) and run → a plain-language "cannot reach the
      database" message, not a stack trace. Restart with `sqllocaldb start MSSQLLocalDB`.

---

## 6. Data migration — the 26 rows (step 13, not yet started)

The live PostgreSQL database still holds the real data and is untouched:

| Table | Rows |
|---|---|
| `employees` | 5 |
| `menus` | 6 |
| `bookings` | 9 |
| `daily_prices` | 5 |
| `email_log` | 1 |

PostgreSQL credentials were backed up outside the repository before `config/database.local.json` was
overwritten. `psql.exe` is at `D:\PostgreSQL\bin\psql.exe` and is **not** on `PATH`.

When the migration script is written, verify:
- [ ] Row counts match per table
- [ ] All 9 bookings still resolve to the correct employee **and** menu (ids preserved via
      `SET IDENTITY_INSERT`)
- [ ] Identity counters reseeded — a new employee/menu/booking inserts without a key collision
- [ ] The admin report for the same date range is identical on both databases

---

## 7. Open defects

### D1 — Menu numbering race under parallel insert · **BLOCKS SIGN-OFF**

`ConcurrencyTests.TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers` fails:

```
Cannot insert duplicate key row in object 'dbo.menus'
with unique index 'ix_menus_menu_date_menu_number'. Duplicate key value is (2027-04-10, 5).
```

**Not an error-mapping bug** — `SqlServerErrors.IsUniqueViolation` correctly matches both 2601 and
2627. `MenuRepository.AddAsync` uses read-then-insert (`SELECT MAX(menu_number)+1`, then `INSERT`) with
only **3** retries, while the test fires **10** concurrent inserts. Under READ COMMITTED, too many
racers read the same `MAX` for three attempts to converge.

This is a **pre-existing probabilistic design** that PostgreSQL happened to survive, not something the
migration introduced. Raising the retry count lowers the odds without fixing the cause. The proper fix
— computing the number inside the `INSERT` under `UPDLOCK, HOLDLOCK` — is written out in
`IMPLEMENTATION_PLAN_SQLSERVER.md`, section "OPEN DEFECT".

- [ ] Fixed
- [ ] All 6 concurrency tests pass

### Known limitations (accepted, not defects)

- **LocalDB is necessary but not sufficient.** It is a lightweight single-user instance over named
  pipes. A `HOLDLOCK` defect could pass here and still fail on a real server. **§1's concurrency tests
  must be re-run against the real target server before go-live.**
- **Confirmation emails use one language for everyone** — there is no per-employee language column.
- **A failed confirmation is never retried**, because the day is already reserved in `email_log`. A
  duplicate email was judged worse than a missing courtesy note.
- **Employee email addresses are not validated at data entry.** The admin UI accepts any string;
  malformed addresses are only caught at send time, and MimeKit's parser is lenient enough that
  something like `not-an-email` will still be attempted.

---

## 8. Sign-off

| Section | Result | Date | By |
|---|---|---|---|
| 1 · Automated tests | | | |
| 2 · Integrity attacks | | | |
| 3 · Accent and case rules | | | |
| 4 · Confirmation emails | | | |
| 5 · Mailer end-to-end | | | |
| 6 · Data migration | | | |
| 7 · D1 fixed | | | |

Database creation with `Seed: true` and `Seed: false` — ✅ **verified by Massimo Fauro, 2026-08-18.**

Once every row above is green, `IMPLEMENTATION_PLAN_SQLSERVER.md` steps 11–12 (delete the PostgreSQL
scripts and documentation, write the SQL Server `create_database.sql`) are unblocked. **Not before** —
that ordering is decision D3, and it exists so the working reference implementation is not destroyed
while its replacement is still unproven.
