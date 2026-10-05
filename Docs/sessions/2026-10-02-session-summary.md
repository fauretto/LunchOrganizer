# Session summary — 2 October 2026

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer`
**Session outcome:** the user could not import `Docs/Menus/Menu octobre 2026.docx` (a new monthly format) via Admin > Menus & Prices > Import menu. The parser was extended to read this format (2-digit year in the day cell, no header row, in-cell "MENU n" labels, year cross-check against the selected import year) and, per the user's request, to pick up a trailing price in the description (e.g. `14.80-`) as a per-menu override. Per-menu pricing was then threaded through the domain, EF migration, booking price resolution, admin UI and booking-page UI. All changes are **uncommitted**; a commit message is prepared. The booking-page bracket display has **not yet been seen rendered** — the user must restart the debug session to verify it.

Written to be read **cold**. Previous summary: [`2026-08-25-session-summary.md`](2026-08-25-session-summary.md) — unrelated feature area (admin authorization under Windows Authentication / PBKDF2 password hashing); its §8 environment facts (D: symlink, config divergence, `AdminHash` CLI, PBKDF2 format) are still accurate and referenced below instead of repeated. Its closing note that no menu-import UI element had ever been rendered in a browser is extended, not contradicted, by this session: the new price UI (admin price field, booking-page brackets) is likewise still unverified in a browser.

---

## 1. What changed today

| # | Change | Why it mattered |
|---|---|---|
| 1 | `src/LunchOrganizer.Services/Import/MenuDocumentParser.cs` — `DayCellRegex` gained an optional 2- or 4-digit year group `y` (2-digit → 2000+yy); new `MatchedDayRow.DocumentYear`; year cross-check in `ResolveDates` (after the Dec→Jan rollover) throws the new `MenuImportYearMismatch` when the document's year disagrees with the selected import year | Root cause of the reported bug: the old regex only accepted `DAY dd.mm` and matched 0 day rows against `LUNDI 05.10.26`, producing `MenuImportNoDataFound` |
| 2 | Same file — in-cell "MENU n" label detection (priority: in-cell label > header row > column position), label paragraph removed from the description; new `TrailingPriceRegex` extracts a trailing price (optional `CHF`/`Fr.` prefix, `\d{1,3}[.,]\d{2}`, optional trailing `-`/`.`/`–`, value must be >0 and ≤1000) and strips it from the description | The new document has no header row, and each cell's first paragraph is a "MENU n" label that would otherwise have landed in the description; the trailing price is the user's requested per-menu price source |
| 3 | `ParsedMenuEntry` → `ParsedMenuEntry(int MenuNumber, string Description, decimal? Price = null)`; parser constructor overload taking `ILogger<MenuDocumentParser>` plus a parameterless ctor using `NullLogger`; Information-level parse summary, Debug per extracted price, Warning before the year-mismatch throw | Carries the new price through the parse result; logging follows the project's existing convention per the global directive |
| 4 | `ErrorCodes.MenuImportYearMismatch` (`src/LunchOrganizer.Domain/Common/ErrorCodes.cs:98`) + `Errors.resx` / `Errors.fr.resx` entries | User's explicit decision: year mismatch is a hard error with full rollback, not a warning or silent acceptance |
| 5 | `Menu.Price` (`decimal?`, `src/LunchOrganizer.Domain/Entities/Menu.cs:10`); `MenuConfiguration` — `price decimal(10,2)` nullable + check constraint `ck_menus_price_non_negative` (`price IS NULL OR price >= 0`); migration `20261002153820_AddMenuPrice` (+ Designer, model snapshot) | New per-menu price column. Applied automatically at startup by `DatabaseBootstrapper.MigrateAsync` — confirmed present in `__EFMigrationsHistory` on the test-bench DB (§4) |
| 6 | `IPricingService.GetEffectivePriceAsync(Menu menu, ct)` / `PricingService` — resolves to `menu.Price ?? <day price> ?? AppOptions.DefaultLunchPrice`; `BookingService.BookDayAsync` / `BookWeekAsync` (lines 81, 169) call it and log the price source (`"menu override"` vs `"day price"`) | Single place that implements the price-precedence rule the user asked for |
| 7 | `MenuDto` gains `decimal? Price` (no default value, see §2.3); `MenuService.UpdatePriceAsync` (0–1000, reuses `ErrorCodes.PriceInvalid`, same concurrency pattern as `UpdateDescriptionAsync`); `CopyDescriptionToWeekAsync` also copies `Price`; `MenuImportService` maps `Price` and logs the count of priced menus; Fakes (`FakePricingService`, `FakeBookingService`, `FakeMenuService`) updated | Admin-side CRUD for the new field, plus "apply to every open day" carrying price along |
| 8 | `MenuCard.razor`/`.css` — optional price input (blank = day price applies); `AdminMenusViewModel.UpdateMenuPriceAsync`; `MenusAndPricesPanel.razor` wiring; new resx keys `MenusMenuPriceLabel`, `MenusMenuPricePlaceholder`, `MenusInvalidPrice` (Admin.resx/.fr.resx) | Admin UI for setting/clearing a per-menu price |
| 9 | `WeeklyBookingGrid.razor` — `<span class="menu-cell-price">(CHF 14.80)</span>` shown only when `menu.Price != day.Price`; `.razor.css` uses `--color-warning`/`--color-warning-bg`, bold, `--radius-sm`; ARIA label suffix; new resx keys `GridCellMenuPriceAriaLabelSuffixTemplate`, `GridCellMenuPriceTooltip` (Booking.resx/.fr.resx) | User's requested booking-page display: bracketed and highlighted when the menu price differs from the day price |
| 10 | `Docs/USER_GUIDE.md`, `.fr.md`, `.it.md` updated | Documents menu price and the new monthly import format / year-mismatch refusal |

**Build/test status** (as reported during the session, not re-run independently — see §5 for why):
- Solution build before Visual Studio locked `bin/Debug`: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- Full `dotnet test` (parser-only change, before the `MenuDto` fix): `Failed: 1, Passed: 100, Skipped: 0, Total: 101` — the one failure is the pre-existing `ConcurrencyTests.TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers` race, already flagged as a known issue in earlier sessions (no "100/101 was new" claim here).
- After the `MenuDto` fix, built into a scratch `-o` folder (workaround for the VS lock, confirmed still active — `devenv.exe` is running, see §4): web build 0 errors; test run from that scratch folder: `95 passed, 6 failed`, all 6 being `ConcurrencyTests` failing only because `ConcurrencyTestFixture.FindRepoRoot()` (`tests/LunchOrganizer.Tests/Concurrency/ConcurrencyTestFixture.cs:204-221`) walks up looking for `LunchOrganizer.sln` and cannot find it outside the repo tree — not a regression.

`git diff --stat` confirms 39 files changed (546 insertions, 80 deletions); untracked: the two migration files, `Docs/commits/2026-10-02-commit-message.md`, `Docs/UsersGuidePrompt.txt`, `Docs/Menus/Menu octobre 2026.docx`, `Docs/Menus/menu septembre 26.pdf`, and two `Backups/*` entries.

---

## 2. Problems found that were not on anyone's list

1. **A Sonnet subagent's PowerShell byte-manipulation script truncated `MenuDocumentParser.cs` to 15 bytes** while attempting to restore a UTF-8 BOM and fix literal NBSP characters. The auto-mode permission classifier then blocked the same agent's attempt to restore the file. Surfaced to the user, who approved restoring from the scratchpad copy (`scratchpad\spec\MenuDocumentParser.cs`, 845 lines). **Carry-forward rule:** never use byte-level scripts to edit source files — use the Edit tool; a write blocked by the permission classifier must be escalated to the user, not retried by another agent. Note: tool parameters normalize literal NBSP/U+202F to plain spaces, so the Edit tool cannot match them directly — the restore used a single line-restricted `perl` substitution, verified by diff. The file's BOM was **not** restored (cosmetic; file is now without BOM).
2. The same incident had turned `'\u00A0'`/`'\u202F'` character-literal escapes into invisible literal characters; the restore put the escapes back (line 778 in the current file).
3. **Booking-page special price was not showing**, found by the user on the test bench. Cause: `BookingService.GetWeekViewAsync` built `MenuDto` without passing `m.Price`; the constructor's `= null` default on that positional parameter hid the omission from the compiler (the admin code path through `MenuService` did pass it, so review of that path found nothing wrong). Fixed at `BookingService.cs:43` (now `new MenuDto(m.Id, m.MenuDate, m.MenuNumber, m.Description, allBookings.Count(b => b.MenuId == m.Id), m.Price)`); the default was then removed from the `MenuDto` record so the compiler flags every construction site. **Carry-forward rule:** when adding a field to a positional DTO, don't give it a default — let the compiler enumerate every call site.
4. **Visual Studio locks `bin/Debug` while debugging `LunchOrganizer.Web`**, so `dotnet build` of the full solution fails with MSB3027/MSB3021 copy errors. Confirmed still the case right now (`devenv.exe` PID 15220 running, ~1.7 GB). Workaround used: build into a scratch `-o` folder — but that then makes `ConcurrencyTests` fail for the unrelated reason in item 5/§1 (`FindRepoRoot` can't find `LunchOrganizer.sln` outside the repo).
5. **Admin password of `massimo` cannot be retrieved** (the user asked). It is stored as a one-way PBKDF2 hash (`pbkdf2-sha256:210000:...`, format and iteration count per the 25 August session) in `config/admin-users.json`; confirmed today's `git diff` on that file is exactly a password-token replacement (same structure, new salt/hash), consistent with a reset rather than any code change. Reset tool: `dotnet run --project src/LunchOrganizer.AdminHash` (see 25 August summary §8 for full usage). This file's change is **not covered** by the prepared commit message and was not made by Claude.

---

## 4. Verification actually performed

- **Scratch console comparison** (`scratchpad\parsercheck`, outside the repo): new parser vs. HEAD parser on real files.
  - October 2026 file, year 2026 → 20 working days, 3 menus/day, no leftover "MENU n" text, 4 priced menus at 14.80 (08.10, 15.10, 22.10, 29.10, all Menu 1); 27.10 Menu 2 has no in-cell label and falls back to column position.
  - October 2026 file, year 2025 → throws with `MenuImportYearMismatch`, args `[LUNDI 05.10.26, 2025, 2026]`.
  - Both pre-existing weekly-format files (`Menus_A4_Paysage_Chaque_Semaine_Au_Coeur_De_France.docx` with year 2026, and a second with year 2029) produce **byte-identical** output to the original (HEAD) parser, `Price` null throughout.
  - This check is discriminating: it diffs old-parser vs. new-parser output on old-format files, so a regression in the previously-working path would have shown up.
- **Read-only `sqlcmd`** against `localhost\SQLEXPRESS`, db `lunchorganizer`: `__EFMigrationsHistory` contains `20261002153820_AddMenuPrice`; October 2026 menus show price 14.80 on Menu 1 of 08.10/15.10/22.10/29.10, `NULL` elsewhere — confirms the user had already imported the October file through the running app with the new code, on the test bench.
- **Symbol/file verification performed in this write-up session** (grep against the actual working tree, not taken on faith from the facts handed over): confirmed existence and current signatures of `DayCellRegex`, `TrailingPriceRegex`, `MenuImportYearMismatch` (`ErrorCodes.cs:98`, and 3 usage sites in the parser), `MatchedDayRow.DocumentYear`, `ParsedMenuEntry(int MenuNumber, string Description, decimal? Price = null)`, `Menu.Price`, `ck_menus_price_non_negative`, the two migration files, `MenuDto`'s current (default-free) signature, `IPricingService.GetEffectivePriceAsync` (both overloads), `MenuService.UpdatePriceAsync`, the `menu-cell-price` span and its two new resx keys in `WeeklyBookingGrid.razor`, and all three new Admin resx keys. Also confirmed via `git diff config/admin-users.json` that its change is exactly a password-token swap (same JSON shape, new salt/hash) — consistent with a manual reset, not a code change.
- Confirmed `devenv.exe` is currently running (the VS-lock condition in §2 item 4 is live, not historical).
- Confirmed the prepared commit message (`Docs/commits/2026-10-02-commit-message.md`, 13 lines) matches the facts handed over (parser, price extraction, migration, booking price precedence, booking-page brackets, admin price field, logging, user guide).
- `git status --porcelain` / `git diff --stat` re-run during this write-up match the file list and line counts in §1 exactly (39 files, +546/-80; 8 untracked paths).

## 5. The limit of that verification

- Build/test numbers in §1 are **as reported during the working session**, not re-run by this write-up. Re-running a full solution build now would hit the same VS lock (confirmed still active) and would not be comparable to the numbers already captured; re-running was deliberately avoided to not produce a spurious, differently-scoped number under a different heading.
- Not verified at all this session: the booking-page bracket display has **not been seen rendered** (user must restart the VS debug session); a priced menu's booking storing 14.80 into `PriceSnapshot` has not been observed end-to-end; the admin `MenuCard` price input has never been clicked; "Apply this menu to every open days" with a priced menu is untested; French/English/Italian UI labels for the new fields have not been viewed; production has not been touched and the migration has not been applied there; the `ConcurrencyTests` flake itself was not investigated (only its cause in the scratch-folder run was identified); the missing BOM was not restored.
- This write-up's own grep-based symbol checks confirm the code **exists and compiles as described in isolation** (names, signatures, line numbers) — they do not re-confirm runtime behavior beyond what the scratch-console comparison and the sqlcmd read already established.

## 6. Decisions and open judgment calls

| # | Decision | Reasoning |
|---|---|---|
| D1 | Year mismatch between the document and the selected import year is a **hard error with full rollback** (option A), not a warning (B) or silent ignore (C) | User's explicit choice |
| D2 | The trailing price in a menu description (e.g. `14.80-`) is used as that **menu's** price, not the day's | User's explicit request, prompted by noticing the pattern in Thursday Menu 1 descriptions |
| D3 | Price precedence: `menu.Price ?? day price ?? AppOptions.DefaultLunchPrice` | Mirrors existing day-price fallback logic in one new method rather than duplicating the rule at each call site |
| D4 | Special price shown **in brackets and highlighted** on the booking page, only when it differs from the day price | User's explicit request |
| D5 | Label detection priority: in-cell "MENU n" label > header row > column position | Matches both document formats without branching on a format flag |
| D6 | Restore the truncated parser file from the scratchpad backup rather than attempt a further automated fix | The file was reduced to 15 bytes; a scratchpad copy existed and was verifiably close to correct — safer than another automated edit attempt |

---

## 7. What remains

| # | Item | Owner |
|---|---|---|
| 1 | Test-bench checks (full list given to the user in chat): DB backup; import October file with year 2026/2025/duplicate; import an old-format file; booking page brackets shown/not-shown; booking price snapshot 14.80 appears in report/CSV/email; apply-to-every-open-day with a priced menu; admin price set/clear/invalid; day-price change interaction; FR/EN/IT labels | user |
| 2 | Commit: `git commit -F Docs/commits/2026-10-02-commit-message.md`, including the two untracked migration files; decide whether to include `config/admin-users.json` (not covered by the message, likely a manual reset — see §2 item 5); exclude `Backups/*`, `Docs/Menus/*` sample files, `Docs/UsersGuidePrompt.txt` | user |
| 3 | Publish to production — the migration auto-applies on startup; back up the production DB first (`Backups/lunchorganizer_before_new_columns_in_menu.bak` already exists locally, made by the user) | user |
| 4 | Optional: investigate the `ConcurrencyTests.TenParallelAddMenu` race itself (separate from the `FindRepoRoot`-outside-repo artifact noted in §1/§2) | code |
| 5 | Optional: `Docs/Menus/menu septembre 26.pdf` is not supported — only `.docx` is parsed | code (if requested) |

---

## 8. Environment facts worth not rediscovering

- Repo is also reachable at `D:\Data\GitPerso\LunchOrganizer` — same tree, not a stale copy (symlink confirmed in the 25 August summary §8; still applies).
- `dotnet-ef` is a local tool (`.config/dotnet-tools.json`); migrations command: `--project src/LunchOrganizer.Data --startup-project src/LunchOrganizer.Web`.
- `sqlcmd`: `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd`, use `-S localhost\SQLEXPRESS -d lunchorganizer -E -C`.
- `scripts/create_database.sql` is obsolete (already noted in an earlier summary).
- Admin password reset / PBKDF2 token format and the `LunchOrganizer.AdminHash` CLI: see 25 August summary §8 — unchanged today.
- Visual Studio debugging `LunchOrganizer.Web` locks `bin/Debug`, breaking a full-solution `dotnet build`; building to a scratch `-o` folder works but breaks `ConcurrencyTests` via `FindRepoRoot` (§1, §2 item 4).

## 9. Working rules confirmed or established

- All code generation/editing goes through a Sonnet agent with precise, file-by-file instructions, reviewed afterwards against the actual diff (global directive) — followed throughout.
- Logging added in critical sections (parse summary/prices/year-mismatch warning, price-source resolution at booking time) following the project's existing convention.
- No commits or pushes made; commit message prepared separately for the user to run.
- A write blocked by the permission classifier is escalated to the user, never silently retried by another agent (§2 item 1).
- When adding a field to a positional record/DTO, omit any default value so the compiler surfaces every construction site that needs updating (§2 item 3).
- Discriminating verification preferred over confirming verification: the scratch-console comparison was run against both the new file (to prove the fix) and the old files (to prove no regression), and the DB check used a value test (14.80 present on the expected dates/menu numbers only) rather than a presence-only check.
