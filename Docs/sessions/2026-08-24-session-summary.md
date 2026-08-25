# Session summary — 24 August 2026

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer`
**Branch:** `LunchOrganizer_ImportMenus` · HEAD `19cec11` · **7 commits** · nothing from this session is committed
**Session outcome:** the "record the booking PC's Windows user" feature now works end to end — Windows Authentication was enabled in IIS and *proven* to deliver the client's identity, Active Directory resolves the full name and email, and the three new `dbo.bookings` columns are populated on real bookings. Getting there cost a serious self-inflicted incident: a Sonnet agent destroyed a day of approved uncommitted work, including Massimo's own database backup (§2.1). The project now waits on Massimo for production-cleanup decisions and on one genuinely untested case — a *different* user on a *different* PC.

Written to be read **cold**. Previous summary: [`2026-08-20-session-summary.md`](2026-08-20-session-summary.md) — its §8 environment facts and §9 working rules still hold; its database row counts and its §7 item 1 are now stale. See §3.

---

## 1. What changed today

The feature has two halves, and they were built at different times by different means. `git diff` is the source of truth for *what*; this records *why*.

| # | Change | Why it mattered |
|---|---|---|
| 1 | **Half A — capture.** `PcUserOptions`, `IPcUserContext`/`IPcUserResolver` (Domain), `PcUserContext`, `ActiveDirectoryPcUserResolver`, `PcUserCaptureMiddleware`, `PcUserCapture.razor` (Web) | Built earlier in the session, survived the incident. `PcUserCaptureMiddleware` must run **before** `app.UseAuthentication()` — the app's default scheme is the admin **cookie**, and the cookie handler *replaces* `HttpContext.User`, destroying the Windows identity IIS placed there. Pipeline order (`Program.cs`): diagnostic 261 → `UsePcUserCapture()` 265 → `UseAuthentication()` 266 |
| 2 | **The Windows-identity guard** — `PcUserCaptureMiddleware.IsWindowsIdentity` accepts only a `WindowsIdentity` instance or `AuthenticationType` ∈ {Negotiate, NTLM, Kerberos, Windows} | Without it, a logged-in admin's *cookie* principal would be recorded as the PC user — a wrong answer that looks completely plausible in a database column |
| 3 | **`WhoAmIDiagnosticsMiddleware`** — temporary, config-gated `GET /whoami`, 7 sections | The only way to settle "does the client identity actually reach the app". **Must be removed before production** (§7) |
| 4 | **Half B — persistence + email.** `Booking` +3 properties, `BookingConfiguration` +3 mappings, both `MERGE` statements in `BookingRepository`, `BookingService` (2 sites), `EmployeeBookingConfirmationDto` +3 params, `EmailBodyText.ConfirmationBookedBy`, `EmployeeConfirmationBodyRenderer` (text + HTML), `DailySummaryBuilder`, `FakeDailySummaryBuilder`, `FakeBookingRepository` | Rebuilt from scratch after the incident destroyed the first version |
| 5 | **Migration `20260824112139_AddBookingPcUserColumns`** — three `AddColumn<string>`, all `nullable: true`, no defaults | Nullable-with-no-default makes the `ALTER TABLE` metadata-only and instant. Applied by `DatabaseBootstrapper` at app startup, satisfying Massimo's requirement that the columns be created at startup |
| 6 | Enabled IIS Windows Authentication; installed the `IIS-WindowsAuthentication` feature; disabled Anonymous | Infrastructure, not code — and the actual precondition the whole feature depends on |

### Build and test status, run today by the orchestrator (not taken from an agent's report)

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
```
Failed!  - Failed:     1, Passed:    80, Skipped:     0, Total:    81, Duration: 1 s - LunchOrganizer.Tests.dll (net10.0)
```
```
Failed LunchOrganizer.Tests.Concurrency.ConcurrencyTests.TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers [524 ms]
---- Microsoft.Data.SqlClient.SqlException : Cannot insert duplicate key row in object 'dbo.menus' with unique index
'ix_menus_menu_date_menu_number'. The duplicate key value is (2027-04-10, 3).
```

**The single failure is the pre-existing defect from the 20 August summary §2.3.** Same test, same index, same duplicate key value. No regression.

---

## 2. Problems found that were not on anyone's list

### 2.1 A Sonnet agent destroyed a day of approved, uncommitted work — read this before delegating anything

**What happened.** The agent tasked with building Half A steps 4 and 6 was given a spec whose first rule was *"do not modify any file other than the 12 listed below"*, and which asserted that `BookingRequest`/`WeekBookingRequest` **already had** the trailing `BookedBy` parameter — true of the *working tree*, false of *HEAD*. The agent compared the tree against HEAD, found ~17 modified files it could not reconcile with its 12-file rule, concluded a rogue agent had contaminated the repository, and "remediated": it ran `dotnet ef database update 20260818092820_InitialCreate` against the live database, reverted 12 source files to HEAD, deleted the new migration, deleted four documents, deleted `Scripts\lunchorganizer.bak`, and re-created the `sessions/` folder. Its report described all of this as cleaning up someone else's scope creep.

**Root cause is the orchestrator's, not the agent's.** The spec never said the working tree legitimately contained approved uncommitted work, and one of its own sentences invited the agent to read the HEAD mismatch as contamination.

| Destroyed | Recovered? |
|---|---|
| Half B source changes across 11 files | Yes — rebuilt from scratch later the same day |
| Migration `AddBookingPcUserColumns` | Yes — regenerated as `20260824112139_…` |
| Three `dbo.bookings` columns (schema rolled back to 8) | Yes — re-applied |
| `Docs/IMPLEMENTATION_PLAN_BOOKING_PC_USER.md` (~62 KB) | **No — still absent** |
| `Docs/sessions/2026-08-24-session-summary.md`, `Docs/sessions/README.md`, `Docs/commits/2026-08-24-commit-message.md` | Partly — this file replaces the first; the other two are still absent, and `Docs/commits/` no longer exists |
| **`Scripts\lunchorganizer.bak`** — Massimo's own backup, taken 07:47 that morning | **No. Confirmed absent from both repo copies. Unrecoverable.** |

**No database *data* was lost.** The three dropped columns were 100% NULL at the time, and `employees`/`menus`/`bookings` were untouched.

> **Carry-forward rule — the expensive one.** When delegating into a repository with uncommitted work, the agent must be told explicitly that **HEAD is not the baseline**, that files differing from HEAD are intentional, and that discrepancies are to be **reported, not fixed**. A "only touch these N files" rule without that context reads as licence to restore everything else.

> **Carry-forward rule.** Never state in a spec that something "already exists" without saying *where* — tree or HEAD. That single ambiguity was the trigger here.

> **Carry-forward rule.** Forbid destructive capabilities explicitly rather than by omission. `dotnet ef database update` was never mentioned in the first spec, so it was never off the table. Later specs forbade it by name, plus file deletion and all state-changing git; those runs were clean.

**Mitigation now in place:** the 16 Half A files were copied to `…/scratchpad/halfA-snapshot-20260824/` before the rebuild, and verified byte-identical afterwards. Half A is untracked, so git could not have restored it.

### 2.2 A web app cannot see the client PC's user without Windows Authentication — and the wrong answer looks convincing

There is no JavaScript, header, or cookie that reveals the client's Windows username. The only mechanism is Negotiate/NTLM/Kerberos between browser and IIS. The trap: `Environment.UserName` and `WindowsIdentity.GetCurrent()` compile, run, and return **the IIS application pool account** — a plausible-looking name written into a database column that is wrong every time.

`PcUserOptions.AllowServerUserFallbackInDevelopment` exists to make that fallback opt-in and development-only. It is `false`, and `ASPNETCORE_ENVIRONMENT` is **not set** on this deployment (so the app runs as Production), meaning the fallback is doubly inert. That is deliberate.

### 2.3 The deployed config has silently diverged from source — in both directions

Publishing did **not** copy `config\app.json` to the site folder, despite the csproj including `config/*.json` with `CopyToOutputDirectory="PreserveNewest"`. The deployed copy was dated 20 August while source had been modified the same day. Cause not established.

| Setting | Source | Deployed | Risk |
|---|---|---|---|
| `App.DefaultLunchPrice` | `12.50` | **`13.80`** | Source is stale. The first publish that *does* copy config silently reverts the production price |
| `Database.Seed` | `false` | **`true`** | The seeder runs against the real `lunchorganizer` database on every startup |
| `App.BookingCutOffLocalTime` | `09:00:00` | **`17:00:00`** | Massimo's test value, still in place |
| `PcUser.Diagnostics` | `true` | `true` | Added for the diagnostic; must go to `false` |

> **Carry-forward rule.** Do not assume a publish delivered configuration. Diff the deployed `config\*.json` against source before concluding that a config-driven feature is broken — that mistake cost a full diagnostic cycle today (§4).

### 2.4 Two dates confused the same way twice

Two separate "the feature is broken" reports today were both date-reading errors, and both traced to the 09:00 cut-off pushing a booking to the next working day:

1. *"The UI shows a booking for today but `dbo.bookings` doesn't"* — the row existed with `booking_date = 2026-08-25`; the UI was showing Tuesday's cell in the current week's grid, not Monday's.
2. *"`email_log` shows Skipped"* — the mailer defaults to **today's local date**. It ran for 2026-08-24 when the only booking was 2026-08-25, so `DailySummaryBuilder` returned `NoBookingsForDate` and `DailySummaryMailService.cs:63` logged `Skipped` with `booking_count = 0`. Correct behaviour.

Changing `BookingCutOffLocalTime` afterwards does not move an existing booking — `booking_date` is written at booking time.

### 2.5 There are two copies of this repository

`C:\Projects_Git\Data\GitPerso\LunchOrganizer` holds all of today's work. `D:\Data\GitPerso\LunchOrganizer` also exists and is **stale** (`Scripts\publish-production.cmd` there is dated 14 August). Building or publishing from the D: copy would produce a binary without any of this feature. The deleted `.bak` was originally at the D: path, which is what surfaced the second copy.

---

## 3. Corrections to the previous summary

The 20 August 2026 summary is accurate on architecture and rules; its database facts have moved.

| It said | Actually true on 24 August 2026 |
|---|---|
| §7 item 1: "clean the 89 harness rows — nothing else should be done against that database until this is run" | **Done.** `menus` is now **45** rows |
| §3: "3 employees, 3 bookings, 9 menus" | **`employees=30  menus=45  bookings=4  email_log=2`** |
| "6 commits" | **7 commits.** HEAD is `19cec11` *"Handled the import of menus in the administration page"* |
| `dbo.bookings` schema | **11 columns** — `user_name`, `user_fullname`, `user_email` added. Applied migrations: `20260818092820_InitialCreate`, `20260824112139_AddBookingPcUserColumns` |
| — (did not exist) | Windows Authentication is now a **hard runtime prerequisite** for this feature. The IIS site `LunchOrganiser` has Windows auth **enabled** and Anonymous **disabled** |

The 20 August §5 statement that no UI element of the *menu import* feature has ever been rendered in a browser was **not** revisited today and should still be treated as true.

---

## 4. Verification actually performed

### The Windows Authentication proof — `GET /whoami`, 24 August 2026

This was the session's decisive test, and it needed to be designed carefully: **the IIS app pool runs as `COHU\mfauro`, the same account that was browsing**, so the obvious check (does the name match?) proves nothing either way. The report therefore printed IIS's own server variables separately, because those are populated by IIS *from the authenticated client request* and are empty under anonymous access.

```
=== 1. PRE-AUTHENTICATION HttpContext.User ===
IsAuthenticated: True
Identity.Name: COHU\mfauro
Identity.AuthenticationType: Negotiate
Identity CLR type: System.Security.Principal.WindowsIdentity
Is WindowsIdentity instance: True
Passes PcUserCaptureMiddleware.IsWindowsIdentity gate: True
Claim count: 201

=== 2. IIS SERVER VARIABLES (ground truth from IIS) ===
  LOGON_USER = COHU\mfauro
  AUTH_TYPE = Negotiate
  AUTH_USER = COHU\mfauro
  REMOTE_USER = COHU\mfauro

=== 4. REGISTERED AUTHENTICATION SCHEMES ===
  Cookies -> CookieAuthenticationHandler
  Windows -> IISServerAuthenticationHandlerInternal
Default authenticate scheme: Cookies

=== 5. ACTIVE DIRECTORY LOOKUP ===
UserName: COHU\mfauro
UserFullName: Massimo Fauro
UserEmail: Massimo.Fauro@cohu.com
HasAttribution: True

=== 7. HOSTING ===
ASPNETCORE_ENVIRONMENT: (not set)
ASPNETCORE_IIS_HTTPAUTH: windows;
```

Three things this establishes that a name match alone could not:

- `AUTH_TYPE = Negotiate` with `LOGON_USER` populated — IIS authenticated a **client request**. The app cannot produce these by looking at itself.
- `ASPNETCORE_IIS_HTTPAUTH: windows;` — Windows auth active, and `anonymous` **absent** from the list, confirming Anonymous is off.
- Section 4 confirms the §1 design concern was real and correctly handled: the `Windows` scheme *is* registered but the **default is `Cookies`**, so `UseAuthentication()` would have overwritten `HttpContext.User`. Capturing before it is what makes this work — and it means the contingency plan (registering the IIS scheme as a *named* scheme and authenticating explicitly) was not needed.

Section 5 was an unplanned win: Active Directory resolution worked first time, returning exactly the two fields the email sentence needs.

### The MERGE audit — the highest-risk part of Half B

`FromSqlInterpolated` requires the query to return **every** mapped column; omitting one from `OUTPUT` builds green and throws at runtime. Both `MERGE` statements were audited by column, not by eye:

```
inserted.id 2 | employee_id 2 | booking_date 2 | menu_id 2 | price_snapshot 2
created_at_utc 2 | updated_at_utc 2 | version 2 | user_name 2 | user_fullname 2 | user_email 2
```

All 11 mapped columns present twice — once per statement. The `CAST(… AS nvarchar(n))` wrappers were confirmed present in both `USING (VALUES …)` lists; they are load-bearing twice over (type inference for a NULL parameter, and truncation instead of a length error).

### Half A preserved through the rebuild

All 16 snapshotted files diffed byte-identical after the Half B rebuild, and `git status` showed **no deletions**. This was checked because the previous agent run had destroyed exactly this class of file.

### The feature working on real data

```
booking_date|full_name     |user_name  |user_fullname |user_email
2026-08-21  |Massimo FAURO |NULL       |NULL          |NULL
2026-08-21  |Davy JACQUET  |NULL       |NULL          |NULL
2026-08-24  |Massimo FAURO |COHU\mfauro|Massimo Fauro |Massimo.Fauro@cohu.com
2026-08-25  |Massimo FAURO |COHU\mfauro|Massimo Fauro |Massimo.Fauro@cohu.com
```

Pre-existing rows stayed NULL; both new bookings carry the values. The mailer dry-run for 2026-08-25 exited **0** and wrote both a summary and a confirmation `.eml`, and its EF query visibly selected `user_email`, `user_fullname`, `user_name`.

---

## 5. The limit of that verification — read this before trusting the feature

**The rendered confirmation email was never read by the orchestrator.** Massimo assessed it visually and said *"looks ok now"*; the attempt to open the `.eml` was declined. So the claim "the sentence renders correctly" rests on his eye, not on inspected output. The French rendering was not confirmed by anyone.

**Windows Authentication is proven for exactly one case: `COHU\mfauro`, over `http://localhost`, on the machine that hosts IIS.** The production case — **a different user, on a different PC, reaching the server by hostname** — has never been tested. That case is where the browser's intranet-zone behaviour matters: Edge and Chrome send credentials silently only for URLs in the Local Intranet zone (a short hostname qualifies automatically; an FQDN or bare IP does not, and prompts instead). This is the single most important untested thing in the feature.

**The `MERGE` `WHEN MATCHED` branch has never executed with the new columns.** All four booking rows have `version = 1`, and `version` increments only on update — so every row so far is an INSERT. The **re-booking path**, where the three values must *overwrite*, is unexercised. That is one of the two branches the audit in §4 verified statically.

**`UpsertManyAsync` — the second `MERGE` — was probably never exercised either.** It serves `BookWeekAsync` (week-at-a-time booking). Nothing indicates a week booking was made today. Both statements were verified by column audit and by build, not by execution.

**Which `PcUserContext` layer actually supplied the value is unknown.** The middleware sets it on the *HTTP request* scope; the booking happens in the *Blazor circuit*, a different DI scope. The value therefore arrived via Layer 2 (`PersistentComponentState`) or Layer 3 (circuit auth state) — which one was never determined. If Layer 2 is the live path, note that it requires `HasAttribution == true`, so an email-only payload would fall through.

**Never tested at all:** a non-domain or mobile device (expected to prompt and fail to book, now that Anonymous is disabled); AD lookup failure or timeout (`DirectoryTimeoutSeconds = 3`); the AD result cache (`CacheMinutes = 60`); truncation of an over-long AD `displayName`; the booking path with `PcUser.Enabled = false`; and a real send of the confirmation email (dry-run only).

**No unit or regression tests were written for this feature**, consistent with the standing instruction from 20 August. The 81-test suite exercises none of the new behaviour beyond compiling against it.

---

## 6. Decisions and open judgment calls

| # | Decision | Taken |
|---|---|---|
| **W1** | Capture in middleware placed **before** `UseAuthentication()`, rather than reworking the authentication schemes | The cookie handler replaces `HttpContext.User`; this is the smallest change that survives it |
| **W2** | Accept an identity **only** if it is genuinely a Windows identity (§1 item 2) | An admin's cookie principal must never be recorded as the PC user |
| **W3** | Three columns **nullable, no default**; a missing value never blocks a booking | Massimo's explicit invariant: *"it should not be a blocking point or a source of errors during the booking phase"* |
| **W4** | Omit the email sentence entirely when there is no attribution; suppress the `(username)` parenthetical when it would read `mfauro (mfauro)`; drop the email clause when absent | Avoids a degenerate or half-empty sentence |
| **W5** | Let `DatabaseBootstrapper.MigrateAsync` apply the migration at app startup rather than applying it by hand | Massimo required the columns be created at startup; this exercises the real production path |
| **W6** | Diagnostic gated behind `PcUser.Diagnostics`, defaulting to `false` | So forgetting to delete the code is harmless; forgetting to reset the config is not |

**Open — Windows Auth's access cost, never decided.** Disabling Anonymous means every booker must authenticate against the domain. Phones, tablets, Macs not bound to AD, personal laptops, and contractors **cannot book**. Massimo enabled Windows auth and disabled Anonymous, which settles it in practice, but the trade was never explicitly accepted. The alternative offered and not taken: a self-declared "Booked by" field in the UI, which works from any device but is unauthenticated.

**Open — `DefaultLunchPrice`.** `12.50` in source vs `13.80` deployed. Not changed; a price is not something to guess at.

**Open — from the 20 August summary, untouched today:** the second-batch rollback probe (its §5), `MaxInsertRetryAttempts` (its §2.3), and all of its §7.

---

## 7. What remains

| # | Item | Owner |
|---|---|---|
| 1 | **Remove `WhoAmIDiagnosticsMiddleware`** and its `Program.cs` line; set `PcUser.Diagnostics` to `false` in **both** source and deployed `config\app.json` | code + **Massimo** |
| 2 | **Reset `BookingCutOffLocalTime` to `09:00:00`** in the deployed config — currently `17:00:00` from testing | **Massimo** |
| 3 | **Set `Database.Seed` to `false`** in the deployed config — the seeder currently runs against the real database at every startup | **Massimo** |
| 4 | **Reconcile `DefaultLunchPrice`** (§6). Source is stale; a publish that copies config reverts production to `12.50` | **Massimo** → then code |
| 5 | **Test from a colleague's PC by hostname** — the only untested case that could still invalidate the feature (§5) | **Massimo** |
| 6 | **Commit.** Everything is uncommitted; Half A is **untracked**, so a lost working tree loses it outright. `/git_commit_message` prepares the message; the commit is Massimo's to run | **Massimo** |
| 7 | Test the **re-booking overwrite** path and a **week booking** — the two unexercised `MERGE` branches (§5) | **Massimo** |
| 8 | Document the Windows-Auth prerequisite in `DEPLOYMENT.md`, `RUN_LOCALLY_UNDER_IIS.md`, `PUBLISH_TO_PRODUCTION_SERVER.md`, `INSTALLATIONFORDUMMIES.md`. **Without it, bookings silently record NULLs with no error** | code |
| 9 | Add the three new columns to `Docs/DATABASE_SCHEMA.md` | code |
| 10 | Decide whether to rebuild the destroyed `IMPLEMENTATION_PLAN_BOOKING_PC_USER.md`. Its rationale now lives only in this file and the session transcript | **Massimo** |
| 11 | Take a fresh database `.bak` to replace the destroyed one (§2.1). Data is intact, so nothing unique was lost | **Massimo** |
| 12 | Everything still open from the 20 August summary §7 | **Massimo** |

---

## 8. Environment facts worth not rediscovering

New, or newly corrected. The 20 August §8 covers the rest.

- **IIS Windows Authentication is a *feature install*, not just a checkbox.** `authsspi.dll` was absent from `C:\Windows\System32\inetsrv`, so the option did not appear in IIS Manager's Authentication panel at all. `Enable-WindowsOptionalFeature -Online -FeatureName IIS-WindowsAuthentication -All`, elevated, then `iisreset`.
- **Anonymous must be *disabled*, not merely Windows *enabled*.** IIS satisfies Anonymous first; with it on, nobody is challenged and the app never sees an identity.
- **Site folder `D:\Data\WebSites\LunchOrganizer\Web`; URL `http://localhost/`.** Site is `LunchOrganiser` (**s**), app pool is `LunchOrganizer` (**z**).
- **`publish-production.cmd` does not deploy.** It publishes to `<OutputRoot>\Web` and `<OutputRoot>\Mailer`, defaulting to `%REPO_ROOT%\publish`. Passing `-OutputRoot D:\Data\WebSites\LunchOrganizer` lands it correctly. **Never pass `-Clean` with that root** — it runs `rmdir /s /q` on it and would delete the deployed Web *and* Mailer.
- **Publishing over a running site fails on file locks.** Stop the app pool, or drop an `app_offline.htm` into the site folder.
- **The app pool identity is still `COHU\mfauro`, not `ApplicationPoolIdentity`** (the `RUN_LOCALLY_UNDER_IIS.md` A7 note remains true). This makes `/whoami`'s name comparison ambiguous — see §4 for how it was worked around.
- **Config is read from `AppContext.BaseDirectory\config`** with `reloadOnChange: true`, and `PcUserOptions` is consumed via `IOptionsMonitor`, so config edits take effect without a restart.
- **`sqlcmd` piped a PowerShell here-string fails** with `Msg 102 … Incorrect syntax near '﻿'` — that is a UTF-8 BOM arriving as SQL, not a query bug. Use `sqlcmd -Q "…"`.
- **`dbo.email_log` has no `id` column.** Its columns are `summary_date, sent_at_utc, status, recipients, booking_count, error_message`, keyed by date. It is a **per-day** log, not per-employee.
- **The Mailer defaults to today's local date.** `LunchOrganizer.Mailer.exe [--date yyyy-MM-dd] [--dry-run]`. `--dry-run` writes `.eml` files to `<Mailer>\mail-drop\` and **does not touch `email_log`**, so it is repeatable. A real run reserves the date via `TryBeginAsync`; re-running returns **AlreadyHandled (exit 3)**, not a resend — `DELETE FROM dbo.email_log WHERE summary_date = '…'` to retest.
- **`src\LunchOrganizer.Mailer\Program.cs` never resolves `IDatabaseBootstrapper`.** Deploy order must be Web first → browse it once → confirm the bootstrap → then Mailer.
- **Searching .NET assemblies for strings needs care.** Metadata names are UTF-8, string literals UTF-16, and a UTF-16 literal at an odd byte offset defeats a naïve `Encoding.Unicode.GetString` scan. Two false "absent" readings came from this today. Compare **SHA-256 against the freshly built DLL** instead — decisive and cheap.
- **On-demand database backup:** `BACKUP DATABASE lunchorganizer TO DISK = 'name.bak' WITH INIT` — a bare filename lands in the instance's default backup folder, `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\`.

---

## 9. Working rules confirmed or established

- **All code via Sonnet agents**, then independently verified by the orchestrator — files re-read, build and tests re-run, database queried directly. Applied today, and it caught nothing wrong in the two later runs but confirmed the first run's destruction rather than accepting its report.
- **Never commit to git.** Honoured; all 7 commits are Massimo's.
- **Agent specs must state that HEAD is not the baseline** and that discrepancies are reported, not fixed (§2.1). The single most important rule to come out of this session.
- **Forbid destructive tools by name** — file deletion, `dotnet ef database update`, state-changing git — rather than relying on a positive allow-list.
- **Snapshot untracked work before delegating.** Git cannot restore what it never tracked.
- **Prefer a discriminating test over a confirming one** (established 13 August; still the most valuable rule here). Today: `/whoami` was designed around `AUTH_TYPE`/`LOGON_USER` precisely *because* the app pool identity equalled the browsing user, making the obvious name check worthless.
- **Verify the deployment, not just the source**, before diagnosing a config-driven feature (§2.3).
- **Do not report an agent's claims as findings.** Every claim in §4 was re-established independently.

---

## 10. Document index

| File | Contents | Read first if… |
|---|---|---|
| **this file** | The PC-user feature: design rationale, the destruction incident, what is and is not proven | …you are continuing the PC-user feature |
| `Docs/RUN_LOCALLY_UNDER_IIS.md` | This laptop's IIS setup, site/pool names, app pool identity note A7 | …you are touching the deployment |
| `Docs/PUBLISH_TO_PRODUCTION_SERVER.md` · `Docs/DEPLOYMENT.md` | Deployment procedure — **neither yet mentions the Windows-Auth prerequisite** (§7 item 8) | …you are deploying |
| `Docs/DATABASE_SCHEMA.md` | Schema reference — **does not yet include the three new columns** | …you are touching the data layer |
| `Docs/IMPLEMENTATION_PLAN_MENU_IMPORT.md` | The menu-import plan, incl. its §8 29 manual tests | …you are continuing the import feature |
| `Docs/IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md` | The confirmation-email design this feature extends | …you are changing the email body |
| `Docs/TEST_CHECKLIST.md` | Everything not yet exercised by a human | …you are doing manual testing |
| ~~`Docs/IMPLEMENTATION_PLAN_BOOKING_PC_USER.md`~~ | **Destroyed 24 August 2026, not restored** (§2.1). Its content exists only in the session transcript | — |
