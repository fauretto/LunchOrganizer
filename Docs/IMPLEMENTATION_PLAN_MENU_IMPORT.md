# Implementation Plan — Word (.docx) Menu Import

Status: **awaiting validation**
Target branch: `LunchOrganizer_ImportMenus`
Author: analysis pass, 2026-08-20

---

## 1. Feasibility verdict

**Yes — the file is fully parseable, deterministically, with no fuzzy matching or heuristics beyond one regex.**

Both sample files were unpacked and their `word/document.xml` inspected. The structure is
machine-friendly and completely regular.

### 1.1 What the document actually contains

`word/document.xml` body is a flat sequence of top-level elements — no nested tables, no
headers/footers involvement, no text boxes, no merged cells (`gridSpan` / `vMerge` are absent
everywhere):

```
p, p, p, p, p, tbl, p, p, p, p, p, p, tbl, p, p, p, p, p, p, tbl, p, sectPr
```

Each of the 3 `tbl` elements is one working week, with 6 rows × 4 cells:

| row | cell 0 | cell 1 | cell 2 | cell 3 |
|---|---|---|---|---|
| R00 | `Jour` | `MENU 1` | `MENU 2` | `MENU 3` |
| R01 | `LUNDI 17.08` | *description* | *description* | *description* |
| R02 | `MARDI 18.08` | … | … | … |
| R03 | `MERCREDI 19.08` | … | … | … |
| R04 | `JEUDI 20.08` | … | … | … |
| R05 | `VENDREDI 21.08` | … | … | … |

Menu descriptions are multi-line within a single cell — the lines are `<w:br/>` elements inside one
paragraph, e.g.:

```
Jambon braisé sauce madère
Pâtes au beurre
Petits pois carottes
```

That maps 1:1 onto `dbo.menus.description` (`nvarchar(max)`, nullable) with `\n` separators, which is
already how the app stores free-text descriptions.

### 1.2 Extracted content — sample file 1

3 weeks × 5 days × 3 menus = **45 menu rows**, dates `17.08 → 21.08`, `24.08 → 28.08`,
`31.08 → 04.09`. Note week 3 already crosses a *month* boundary (Aug → Sep) inside one table.

### 1.3 Extracted content — sample file 2 (year-rollover test file)

3 weeks, dates `17.12 → 21.12`, `24.12 → 28.12`, and **`31.12 → 01.01, 02.01, 03.01, 04.01`** — the
year-boundary case, exactly as described. 45 menu rows.

### 1.4 Two important findings about the page headers

The paragraph above each table carries a week caption that **does** contain a year, e.g.
`SEMAINE DU 17 AU 21 AOÛT 2026`. It is tempting to read the year from there. **Do not.** Sample
file 2's third caption reads:

> `SEMAINE DU 31 AOÛT AU 4 JANVIER 2026`

— wrong month (`AOÛT` instead of `DÉCEMBRE`) and a year that contradicts the rows it labels. The
captions are hand-typed prose and are demonstrably unreliable. **The plan ignores them entirely**
and derives every date from the row cells plus the user-selected year. (This is also why asking the
user for the year, as you specified, is the right call.)

Second finding, which drives decision **D2** below: the weekday *labels* in sample file 2 are only
consistent with year **2018** (or 2029), not 2026 — `17.12.2026` is a Thursday, not a `LUNDI`.
Sample file 1's labels are consistent with 2026. So weekday-vs-date agreement is a genuinely useful
"did you pick the right year?" signal, but it **cannot be a hard error** or sample file 2 becomes
un-importable for any year the tester is likely to pick.

---

## 2. Parsing approach

### 2.1 No new NuGet dependency

The whole parse is doable with the base class library: `System.IO.Compression.ZipArchive` to open the
`.docx` (it is a ZIP) and `System.Xml.Linq.XDocument` to read `word/document.xml`. Both ship in the
shared framework — nothing to restore, nothing extra to publish to the on-prem IIS box.
`DocumentFormat.OpenXml` would also work and is more general, but the document shape here is so
regular that it buys nothing and adds a dependency to a project that currently has none beyond
EF Core. → see decision **D1**.

### 2.2 Cell-text extraction rules

For each `w:tc`, walk its `w:p` children; for each paragraph walk descendants **in document order**:

- `w:t` → append its value
- `w:br`, `w:cr` → append `\n`
- `w:tab` → append a space
- skip anything under `w:instrText`, `w:delText` (tracked deletions) and `mc:Fallback`

Join paragraphs with `\n`. Then normalize: NBSP (`U+00A0`) → space, trim each line, drop leading and
trailing blank lines, collapse 2+ consecutive blank lines to one. Result is the
`dbo.menus.description` value verbatim.

### 2.3 Row/column recognition

- Tables: `body.Elements(w:tbl)` only — top level, in document order. Nested tables ignored.
- **Day rows** are identified by the first cell matching the day/date regex (below). Any row whose
  first cell does not match — the `Jour` header row, stray spacer rows — is skipped, not an error.
- **Menu numbers** come from the header row when present: cells matching `^\s*MENU\s*(\d+)\s*$`
  (case-insensitive) give the explicit number per column index. If no header row is found or it does
  not match, fall back to positional numbering (first column after the day column = Menu 1, etc.).
  This makes the parser tolerant of 2-menu or 4-menu variants of the template.
- **Empty description cell** → no menu row is inserted for that (day, menu number). The day is still
  imported with its non-empty menus.

### 2.4 Day/date regex

Applied to the whitespace-normalized single-line form of the first cell:

```
^(?<day>\p{L}+)\s*,?\s*(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})\.?$
```

`RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`, with a hard timeout (e.g. 250 ms).

Day-label lookup table (accent- and case-insensitive, `String.Normalize` + diacritic strip):
`LUNDI, MARDI, MERCREDI, JEUDI, VENDREDI, SAMEDI, DIMANCHE`. An unrecognized day word is **not** an
error on its own — it only disables the §2.6 cross-check for that row.

### 2.5 Year assignment — the rollover algorithm

Iterate **every day row across all tables in document order** (not per table — a file could split a
Dec week and a Jan week into separate tables, and this handles both):

```
currentYear   = selectedYear
previousKey   = null                    // (month, day)
rolloverUsed  = false

for each dayRow:
    key = (month, day)
    if previousKey is not null and key < previousKey:          // compare month, then day
        if previousKey.month != 12 or key.month != 1: → ERROR MenuImportDatesNotChronological
        if rolloverUsed:                                        → ERROR MenuImportDatesNotChronological
        currentYear++
        rolloverUsed = true
    date = DateOnly(currentYear, month, day)                    // validated, see below
    previousKey = key
```

Two deliberate guards:

- The only accepted backwards step is **December → January**. Any other decrease (e.g. `15.08` after
  `03.09`) means the weeks are out of chronological order, or a typo — that is an error, not a
  silent five-year jump.
- At most **one** rollover per document. A monthly file can cross at most one new year.

Date validity is checked explicitly: month `1..12`, day `1..DateTime.DaysInMonth(year, month)`. This
catches `31.02` and `29.02` in a non-leap year → `MenuImportInvalidDate`.

Against sample file 2 with `selectedYear = 2018` this yields: `2018-12-17..21`, `2018-12-24..28`,
`2018-12-31`, then `2019-01-01..04`. Verified by hand against the extracted rows.

### 2.6 Weekday cross-check — soft for Mon–Fri, hard for a weekend landing

**Revised after runtime verification. See §10 for the finding that forced this.**

A row's classification is decided by the **document's own day label**, never by the resolved date —
the resolved date depends on the user-selected year, so keying off it lets a wrong year silently
delete working-day rows.

| label | resolved date | outcome |
|---|---|---|
| `SAMEDI` / `DIMANCHE` | anything | skipped (a genuine weekend row the document declares) |
| unrecognized | Sat/Sun | skipped |
| unrecognized | Mon–Fri | imported |
| `LUNDI`…`VENDREDI` | **Sat or Sun** | **hard error** `MenuImportWeekdayMismatch`, nothing imported |
| `LUNDI`…`VENDREDI` | Mon–Fri | imported; a label/date disagreement is recorded as a soft mismatch |

A working-day-labelled row landing on a weekend is near-certain proof of a wrong year, and both
alternatives are worse than refusing: silently dropping the row loses data, and importing it creates
rows the Mon–Fri booking UI can never display.

For rows where the day label was recognized and both label and date stay within Monday–Friday,
compare them; mismatches are collected, **not** thrown. If any mismatch exists, the UI shows a
confirmation dialog before committing anything:

> The weekday names in this document do not match the year you selected (2026). For example the row
> "LUNDI 17.12" falls on a Thursday. Check the selected year. Import anyway?

Listing up to 3 examples. Cancel → nothing happens. Confirm → proceed. This is the single most
effective protection against importing a whole month against the wrong year, and it keeps sample
file 2 importable. → see decision **D2**.

---

## 3. Validation and error taxonomy

All failures flow through the existing `OperationResult` + `ErrorCodes` + `IErrorMessageResolver`
channel — no new mechanism, no user-facing English strings in the service layer.

| New `ErrorCodes` constant | Raised when | `MessageArgs` |
|---|---|---|
| `MenuImportInvalidDocument` | not a ZIP, no `word/document.xml`, malformed XML — covers `.doc`, `.pdf`, renamed `.txt`, truncated files | — |
| `MenuImportFileTooLarge` | uploaded stream exceeds the configured cap | arg0: cap in MB |
| `MenuImportNoDataFound` | zero tables, or zero day rows matched, or zero non-empty descriptions | — |
| `MenuImportInvalidDate` | day/month combination invalid for the resolved year | arg0: the offending cell text |
| `MenuImportDatesNotChronological` | non-Dec→Jan backwards step, or a second rollover | arg0: previous date, arg1: offending cell text |
| `MenuImportDuplicateInDocument` | the same (date, menu number) appears twice in the file — e.g. a week pasted twice | arg0: the date |
| `MenuImportDuplicateMenusExist` | the database already holds menus for dates in this import | arg0: count of conflicting dates, arg1: up to 10 dates joined, `dd.MM.yyyy` |
| `MenuImportWeekdayMismatch` | a row labelled `LUNDI`–`VENDREDI` resolves onto a Saturday or Sunday (§2.6) | arg0: the offending cell text, arg1: the resolved date |
| `MenuImportFailed` | any other exception (DB down, timeout, unexpected) | — |

`MaxMenusPerDayReached` (existing) is reused when a parsed day would exceed
`AppOptions.MaxMenusPerDay` (currently 10).

Each new code gets an entry in **both** `Resources/Errors.resx` and `Resources/Errors.fr.resx`.

---

## 4. Atomicity — how the rollback is guaranteed

This needs care, because the existing repositories deliberately create **one `DbContext` per call and
`SaveChanges` immediately** (see `MenuRepository`). Looping over `IMenuService.AddMenuAsync` would
produce exactly the partial-data situation you ruled out. So the import gets its own repository
method that owns a single context and a single explicit transaction:

```
IMenuRepository.ImportAsync(IReadOnlyList<Menu> menus, CancellationToken ct)
```

Implementation in `MenuRepository`:

1. `await using var db = await factory.CreateDbContextAsync(ct);`
2. `await using var tx = await db.Database.BeginTransactionAsync(ct);`
3. Compute `min`/`max` `MenuDate` over the batch; read the existing menus in that window
   (`AsNoTracking`).
4. Conflict check (see **D3**) → if any, **roll back and throw `MenuImportConflictException`**
   carrying the conflicting dates. Nothing was written.
5. `db.Menus.AddRange(menus)` — leaving `CreatedAtUtc`/`UpdatedAtUtc` to the column defaults and
   `Version` to `ApplyVersionMaintenance` (which sets 1 on insert), exactly as `AddAsync` does today.
6. `await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);`
7. `catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueViolation(ex))` → a concurrent import
   won the race between steps 3 and 6 → roll back, throw `MenuImportConflictException`.
8. Any other exception → the `await using` on the transaction rolls back on dispose; rethrow.

The explicit transaction is required not for the insert (EF already wraps a single `SaveChanges`) but
to make the *duplicate read* and the *insert* one unit. The unique index
`ix_menus_menu_date_menu_number` is the backstop for the race window.

`MenuImportConflictException` goes in `Domain/Common/`, mirroring the existing
`DeleteRestrictedException` idiom, and carries `IReadOnlyList<DateOnly> ConflictingDates`.

---

## 5. Files to create / change

### 5.1 New files

| File | Purpose |
|---|---|
| `src/LunchOrganizer.Domain/Common/MenuImportConflictException.cs` | duplicate-detection signal from repo → service |
| `src/LunchOrganizer.Services/Dtos/ParsedMenuDocument.cs` | `ParsedMenuDocument`, `ParsedMenuDay`, `ParsedMenuEntry`, `WeekdayMismatch` records |
| `src/LunchOrganizer.Services/Dtos/MenuImportResultDto.cs` | `WeeksParsed, DaysImported, MenusImported, FirstDate, LastDate` |
| `src/LunchOrganizer.Services/Abstractions/IMenuDocumentParser.cs` | `ParsedMenuDocument Parse(Stream docx, int year)` — throws `MenuDocumentParseException` with an `ErrorCodes` value + args |
| `src/LunchOrganizer.Services/Import/MenuDocumentParser.cs` | the §2 parser. Pure, no I/O beyond the stream, no DB, no DI dependencies |
| `src/LunchOrganizer.Services/Import/MenuDocumentParseException.cs` | internal transport for a parse failure's error code + args |
| `src/LunchOrganizer.Services/Abstractions/IMenuImportService.cs` | see §5.3 |
| `src/LunchOrganizer.Services/Services/MenuImportService.cs` | orchestration: parse → validate → repo → map failures |
| `src/LunchOrganizer.Web/Components/Admin/MenuImportDialog.razor` (+ `.razor.css`) | the modal: year `<select>`, `<InputFile>`, Import/Cancel, inline result/error block |

### 5.2 Modified files

| File | Change |
|---|---|
| `src/LunchOrganizer.Domain/Common/ErrorCodes.cs` | add the 8 constants from §3 |
| `src/LunchOrganizer.Data/Repositories/Abstractions/IMenuRepository.cs` | add `ImportAsync` + XML doc stating the atomicity contract |
| `src/LunchOrganizer.Data/Repositories/MenuRepository.cs` | implement §4 |
| `src/LunchOrganizer.Services/ServicesServiceCollectionExtensions.cs` | `AddScoped<IMenuDocumentParser, MenuDocumentParser>()`, `AddScoped<IMenuImportService, MenuImportService>()` |
| `src/LunchOrganizer.Web/ViewModels/AdminMenusViewModel.cs` | import dialog state + `ImportAsync` (§5.4) |
| `src/LunchOrganizer.Web/Components/Admin/MenusAndPricesPanel.razor` (+ `.razor.css`) | "Import menu" button in the top section; host `<MenuImportDialog>` |
| `src/LunchOrganizer.Web/Resources/Errors.resx` / `Errors.fr.resx` | 8 new entries |
| `src/LunchOrganizer.Web/Resources/Admin.resx` / `Admin.fr.resx` | ~12 new UI keys (§5.5) |

`LunchOrganizer.Fakes` needs **no** change: `IMenuImportService` is a brand-new interface and
`Program.cs` never calls `AddLunchOrganizerFakes()`.

### 5.3 Service contract

```csharp
public interface IMenuImportService
{
    /// Parses and validates only — no writes. Used to surface weekday mismatches before committing.
    Task<OperationResult<MenuImportPreviewDto>> PreviewAsync(Stream docx, int year, CancellationToken ct = default);

    /// Parses, validates and inserts atomically. Rolls back on any failure.
    Task<OperationResult<MenuImportResultDto>> ImportAsync(Stream docx, int year, CancellationToken ct = default);
}
```

Two calls rather than one because the weekday-mismatch confirmation (§2.6) must happen *between*
parse and commit. The stream is a `MemoryStream` held by the ViewModel, rewound (`Position = 0`)
between the two calls, so the file is read from the browser only once.

`MenuImportPreviewDto`: `WeeksParsed, DaysParsed, MenusParsed, FirstDate, LastDate,
IReadOnlyList<string> WeekdayMismatchExamples`.

`ImportAsync` responsibilities, in order:
1. `parser.Parse(stream, year)` — catch `MenuDocumentParseException` → `Fail(code, args)`
2. no day rows / no non-empty descriptions → `MenuImportNoDataFound`
3. duplicate (date, number) within the document → `MenuImportDuplicateInDocument`
4. per-date menu count > `AppOptions.MaxMenusPerDay` → `MaxMenusPerDayReached`
5. `menuRepo.ImportAsync(entities, ct)` — catch `MenuImportConflictException` →
   `MenuImportDuplicateMenusExist`; catch `Exception` → log + `MenuImportFailed`
6. on success, `notifier.NotifyChanged(date)` for each imported date so other open circuits refresh
7. return `Ok(MenuImportResultDto)`

**No past-date rejection** in this path — see decision **D4**.

### 5.4 ViewModel additions (`AdminMenusViewModel`)

State: `IsImportDialogOpen`, `ImportYear` (int, defaults to `_clock.Today.Year`),
`ImportFileName`, `ImportErrorMessage`, `ImportSummaryMessage`.

Methods:
- `OpenImportDialog()` / `CloseImportDialog()` — resets state
- `SetImportYear(int)`
- `Task OnImportFileSelectedAsync(IBrowserFile file)` — extension check `.docx`
  (case-insensitive), size check against the cap, `OpenReadStream(maxAllowedSize)` copied into a
  `MemoryStream`, stored in a field
- `Task ImportAsync()` wrapped in `RunGuardedAsync` (the existing double-submit guard):
  1. `PreviewAsync` → on failure, set `ImportErrorMessage` via `_errorResolver.Resolve(...)`, keep
     the dialog open, return
  2. if `WeekdayMismatchExamples` is non-empty → `_confirmDialogService.ConfirmAsync(...)`; if
     declined, return with no write
  3. rewind stream, `ImportAsync` → on failure, set `ImportErrorMessage` **and**
     `_toastService.ShowError(...)`, keep the dialog open
  4. on success: `_toastService.ShowSuccess(Loc["MenusImportSuccessTemplate", menus, days, first, last])`,
     `await GoToWeekAsync(result.FirstDate)` so the admin immediately sees imported data, close the
     dialog, dispose the `MemoryStream`

`Dispose()` must also dispose any retained `MemoryStream`.

### 5.5 UI

**Top section of the Menus & Prices tab** — a toolbar row above the existing `<WeekPicker>`, inside
the `no-print` div:

```razor
<div class="menus-panel-toolbar no-print">
    <button type="button" class="menus-import-button" disabled="@Vm.IsBusy"
            @onclick="Vm.OpenImportDialog">@Loc["MenusImportButton"]</button>
</div>
```

**`MenuImportDialog.razor`** — modal, styled on the existing `ConfirmDialogHost.razor` +
`.razor.css` conventions (same overlay/panel class structure, `role="dialog"`, `aria-modal="true"`,
Escape closes, focus on the year select on open):

- Title: `MenusImportDialogTitle`
- Explanatory line: `MenusImportDialogHint` ("Select the year the document refers to, then choose
  the Word file.")
- Year `<select>`: `_clock.Today.Year - 1` … `_clock.Today.Year + 2`, default = current year
- `<InputFile accept=".docx" OnChange="..." />` with label `MenusImportFileLabel`; shows the chosen
  file name
- Buttons: `MenusImportConfirmButton` (disabled until a file is chosen, and while `IsBusy`),
  `Shared:ButtonCancel`
- `<BusyIndicator IsBusy="Vm.IsBusy" />`
- Inline result block: red `role="alert"` for `ImportErrorMessage`, so the failure reason stays on
  screen (a toast alone is too transient for a message listing conflicting dates)

New `Admin.resx` / `Admin.fr.resx` keys: `MenusImportButton`, `MenusImportDialogTitle`,
`MenusImportDialogHint`, `MenusImportYearLabel`, `MenusImportFileLabel`, `MenusImportNoFileChosen`,
`MenusImportConfirmButton`, `MenusImportSuccessTemplate`, `MenusImportWeekdayMismatchConfirm`,
`MenusImportInvalidExtension`, `MenusImportBusyLabel`.

French wording is the default culture (`fr-CH`) — both resx files must be filled, not just the
neutral one.

### 5.6 Upload plumbing

- Size cap: **10 MB**, as a constant in `BusinessRules` (`MaxMenuImportBytes`), enforced twice —
  `IBrowserFile.Size` before opening, and `OpenReadStream(maxAllowedSize: …)`.
- The samples are ~220 KB, well inside Blazor Server's default `InputFile` streaming behaviour, so no
  `HubOptions.MaximumReceiveMessageSize` change is expected. To be confirmed at runtime (§7).
- `MenusAndPricesPanel` already runs under `@rendermode InteractiveServer` (set on `Admin.razor`), so
  `InputFile` works as-is.

---

## 6. Decision points — please confirm before implementation

| # | Question | Recommendation |
|---|---|---|
| **D1** | DOCX parsing: BCL (`ZipArchive` + `XDocument`) or add the `DocumentFormat.OpenXml` NuGet package? | **BCL.** No new dependency, nothing extra to publish on-prem, and the document shape is regular enough that OpenXml adds no value here. |
| **D2** | Weekday labels vs. selected year: hard error, soft confirm, or ignored? Note sample file 2's labels only fit **2018**, so a hard error makes it un-importable for 2026. | **Soft confirm** (§2.6). Best wrong-year protection without blocking your test file. |
| **D3** | "Menus already exist" = same `(menu_date, menu_number)` key, or **any** menu already present on a date being imported? | **Any menu on an imported date.** Key-level matching would let a partially-populated day silently gain the missing menu numbers — a mixed state, which is what you asked to avoid. |
| **D4** | Should the import reject dates in the past? `AddMenuAsync` does for single adds. Sample file 1 covers 17–19.08.2026, already past as of today (20.08.2026). | **Allow past dates on import.** It is a bulk data load, and the existing UI already forbids editing/deleting past menus. Rejecting would make file 1 un-importable. |
| **D5** | May I write to your dev SQLEXPRESS `lunchorganizer` database during end-to-end verification, and how should the imported rows be cleaned up (I delete them by date range / you restore from `Backups/lunchorganizer.bak` / do not touch the DB and I verify the parser only)? | **Yes, using sample file 2 with year 2018** — dates far from any real data — then I delete exactly that date range afterwards and report the row counts before/after. |
| **D6** | Year dropdown range: `current-1 … current+2`? | Yes; it covers "importing next January's file in December" and a late correction to last year. |

---

## 7. Verification I will perform (no unit or regression tests, per your instruction)

1. `dotnet build LunchOrganizer.sln` — clean, zero new warnings.
2. **Parser check without touching the database**: a throwaway console harness created *in the
   scratchpad, outside the repository*, referencing `LunchOrganizer.Services`, that runs
   `MenuDocumentParser` over both sample files and prints every resolved `(date, menu number,
   description)`. Confirms the §2.5 rollover produces `2019-01-01..04` for file 2 / year 2018, and
   the correct Aug–Sep dates for file 1 / year 2026.
3. **End-to-end happy path** (pending **D5**): run the app, log in to `/admin`, Menus & Prices →
   Import menu → year 2018 → sample file 2 → expect the success message and the imported menus
   visible in the week view.
4. **Duplicate handling**: immediately re-import the same file → expect the duplicate error message
   naming the conflicting dates, and a `SELECT COUNT(*)` proving no rows were added.
5. **Bad input handling**: a `.txt` renamed to `.docx`, a truncated `.docx`, and a valid `.docx`
   containing no tables → each must produce its own message and no rows.
6. Cleanup: delete the rows for the verified date range and re-report the count.

## 8. Tests for you to run

**Happy path**
1. Sample file 1, year 2026 → 45 menus over 15 days, `17.08.2026`–`04.09.2026`.
2. Sample file 2, year 2018 → 45 menus, and specifically `01.01.2019`–`04.01.2019` present with the
   right descriptions.
3. Real production file for a normal month.
4. Import January's file in December (year dropdown = next year).
5. After import, book a lunch against an imported menu and check the booking + confirmation email
   render the multi-line description correctly.
6. Check a multi-line description renders as expected in `MenuCard`, the booking grid, and the daily
   summary email.

**Year / rollover**
7. Sample file 2 with year 2026 → **refused** with the weekday-mismatch error, nothing imported
   (see §10 — this replaces the earlier "confirm and import anyway" expectation).
7b. A file whose day labels disagree with the selected year but stay inside Monday–Friday → the soft
   confirmation dialog appears; **Cancel** → nothing imported; **Confirm** → imported.
8. A file whose weeks are deliberately out of chronological order → chronology error, nothing
   imported.
9. A file containing `29.02` with a non-leap year selected → invalid-date error.

**Duplicates / rollback**
10. Re-import the exact same file → error, zero rows added.
11. Delete **one** menu from an already-imported day, re-import → error, and the deleted menu is
    **not** re-created (proves all-or-nothing).
12. Import a file that overlaps an already-imported month by one week only → error naming that week's
    dates.
13. Two browser tabs importing the same file simultaneously → exactly one succeeds, the other
    reports duplicates.

**Bad input**
14. `.pdf`, `.doc`, `.xlsx`, and a `.txt` renamed to `.docx`.
15. A zero-byte file; a truncated/corrupt `.docx`.
16. A `.docx` with no tables; one with tables but no recognizable day rows.
17. A file larger than 10 MB → size error.
18. Open the dialog and press Import with no file chosen → button disabled / no-op.
19. Cancel the dialog mid-selection → no writes.

**Template variants**
20. A file with 2 menu columns, and one with 4 → both import with correct `menu_number` values.
21. A file with one description cell left empty → that menu is skipped, the rest import.
22. A file with more menu columns than `AppOptions.MaxMenusPerDay` → error.
23. A file whose day rows include `SAMEDI`/`DIMANCHE` → confirm the intended behaviour (currently
    they *would* be imported; the booking UI only shows Mon–Fri, so they would be invisible dead
    rows — tell me if these should instead be rejected or silently dropped).

**UI / non-functional**
24. Both languages (`fr-CH` default and `en-CH`) for every new label, message and error.
25. Switch language while the dialog is open.
26. Keyboard only: tab order, Escape closes the dialog, focus lands on the year select.
27. `/admin` while logged out → still redirected to login (import must not be reachable
    unauthenticated).
28. Import while a second browser has the Menus & Prices tab open on an affected week → the second
    tab refreshes (via `IBookingChangeNotifier`).
29. Import a large real file and confirm no SignalR message-size failure in the browser console.

---

## 9. Resolved: non-working-day rows

The template only ever contains Mon–Fri, but the parser must cope with a Saturday or Sunday row, and
`dbo.menus` has no weekday constraint while the booking UI (`WeekService.GetWorkingDaysAsync`) only
ever renders Mon–Fri.

**Resolution:** rows the document itself labels `SAMEDI`/`DIMANCHE` are skipped, and the count is
reported in the success message. Rows labelled as working days that resolve onto a weekend are a hard
error — see §2.6 and §10.

## 10. Runtime finding that changed §2.6

The parser was verified with a scratchpad console harness over both sample files before any database
was touched. Five of six cases behaved exactly as designed. One did not, and it mattered.

**Case: sample file 2 parsed against year 2026** (the "wrong" year for that file, whose weekday
labels only fit 2018). The original rule classified a row as a weekend row using the *resolved date*.
In 2026 the rows labelled `MERCREDI` and `JEUDI` land on Saturdays and Sundays, so six days —
**18 of the file's 45 menus** — were silently discarded, and the parse reported a clean success of
27 menus over 9 days.

Two distinct mistakes in the original design:

1. **The skip decision used the resolved date instead of the document's label.** The weekend-skip
   rule was only ever meant for a file that genuinely lists Saturday/Sunday menus. A row labelled
   `MERCREDI` is not a weekend row, whatever year the user picks.
2. **The disclosure understated the loss.** The success message would have said "6 weekend day(s)
   were skipped", not "18 menus were discarded".

This was the interaction of two separately-reasonable decisions — D2 (weekday mismatch is a soft
confirm) and §9 (skip weekend rows) — which together produced silent partial data loss, precisely the
outcome the feature exists to prevent. Fixed per the §2.6 table: label decides classification, and a
working-day label on a weekend date is now a refusal.

**Consequence for testing:** test 7 in §8 changes. Sample file 2 imported against year 2026 now
**fails by design** with `MenuImportWeekdayMismatch` and imports nothing, rather than prompting for
confirmation. The soft confirmation path now only covers label/date disagreements that stay within
Monday–Friday and therefore lose no rows.
