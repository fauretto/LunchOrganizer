# Session summary — 13 August 2026

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer`
**Session outcome:** the daily-email pipeline is now genuinely connected end to end, three plan-versus-code gaps were closed, and the project's documentation set is complete. The application is ready for Massimo's own manual testing, which is where it now sits.

Written to be read **cold**. It assumes no memory of the session. The 12 August 2026 equivalent is [`2026-08-12-session-summary.md`](2026-08-12-session-summary.md) and its environment facts (§7 there) are still accurate — they are not repeated here.

> **⚠ There were TWO sessions on 13 August 2026.** This first part covers the email/scheduler/frontend-DTO session. A **second session** later the same day fixed the UI language switch — see [**Part 2**](#part-2--session-summary--13-august-2026-second-session) at the end of this file. Part 2 corrects two statements in Part 1's §8 and supersedes nothing else. If you only read one part, note that the build/test totals in Part 1 §1 (**48 tests**) were superseded by Part 2 (**52 tests**).

---

## 1. What changed today

| # | Change | Why it mattered |
|---|---|---|
| 1 | **Mailer connected to the real PostgreSQL database** | It had been producing convincing summaries from in-memory demo data |
| 2 | **In-app scheduler actually registered in the web app** | `EnableInAppScheduler: true` did nothing at all — silently. Config that lied |
| 3 | **Frontend adopted `SkippedDayDto.ReasonCode`/`ReasonArgs`** | Skip reasons were inferred from present UI state rather than the service's own answer |
| 4 | **Frontend adopted `HasBookingsAsync` / `DeleteOrDeactivateWithOutcomeAsync`** | Delete-vs-deactivate was inferred by pulling an employee's entire booking history, then re-reading a list |
| 5 | `UnitTest1.cs` deleted | Template placeholder. **Suite is now 48 tests, not 49 — this is correct** |
| 6 | `Docs/USER_GUIDE.md` + `Docs/USER_GUIDE.fr.md` written | Last missing plan §13 deliverable |
| 7 | `Docs/TEST_CHECKLIST.md` written | What Massimo tests next |

Final state: **build 0 errors, 48/48 tests passing.** Nothing committed — the repository still has zero commits.

---

## 2. Two bugs that were not on anyone's list

Both were found by checking the plan against the code rather than by trusting the notes.

**The in-app scheduler was dead code.** `DailySummaryHostedService` and `AddDailySummaryScheduler()` both existed in `LunchOrganizer.Email`, but `Web/Program.cs` never called them and registered none of the email services they need. Plan §8.2 promised behaviour the code could not deliver, and the failure mode was silence.

**A captive dependency, found while fixing the first.** `DailySummaryHostedService` is a `BackgroundService` — a **singleton** — yet injected `IDailySummaryMailService` directly. That service must be **scoped** in the web app because it reaches the scoped EF Core repositories. Simply registering the scheduler would have stopped the web application from starting. It now takes `IServiceScopeFactory` and creates a fresh scope per run.

**The same class of bug had already bitten the mailer earlier in the session.** `AddLunchOrganizerData()` registers repositories as **scoped**, while the mailer registered its whole email pipeline as **singleton**. The swap described in yesterday's notes would have thrown at resolve time. Fixed the same way, and `ValidateScopes`/`ValidateOnBuild` are now switched on in the mailer so any recurrence fails at startup instead of at first use.

> **Carry-forward rule:** in this solution, anything that touches a repository is scoped. A singleton — including any `BackgroundService` — must resolve it through `IServiceScopeFactory`, never by injection.

---

## 3. One note in yesterday's summary was wrong

Yesterday's outstanding-work list said skipped-day reasons were "currently untranslatable English". **They were not.** `BookingViewModel` already localized them and never displayed the raw English `Reason`.

The real defect was subtler and worth stating precisely: the view model re-derived each reason from the **freshly reloaded day's `EditState`**, cross-referenced by date — a guess about the past inferred from present state, which can disagree with what the service actually did. It now switches on `ReasonCode` and takes the cut-off time from `ReasonArgs[0]`. The old `EditState` logic survives as `BuildFallbackSkipReason` for null codes, which is genuinely reachable because `FakeBookingService` supplied none; that fake was updated to emit the same codes as the real service.

---

## 4. Verification actually performed

Recorded because it is the evidence behind every "it works" above. In each case the agent's report was treated as a claim to be checked, not a result.

**Mailer → real database.** Names alone proved nothing — the demo seeder and the seeded database share names like *Alice Martin*. Two properties did discriminate:
- The demo seeder put **7 bookings on one date**; the real database holds **1 per date across 8 dates**. A dry run for 2026-08-13 produced exactly **one** booking, matching SQL.
- The email rendered *"Chicken curry with basmati rice"* under **Menu 2**, where the real database has it. The demo seeder had that same text under **Menu 1**.
- EF Core logged the real query including the composite-FK join `b.booking_date = m.menu_date`.
- `config/database.json` still holds `changeme`, yet the connection succeeded — proving the `.local.json` override chain.

**Double-send guard, against the real constraint for the first time.** A non-dry run wrote an `email_log` row; a second hit `ON CONFLICT (summary_date) DO NOTHING` and returned exit `3`. Yesterday's notes called this unprovable until the real repository existed.

**In-app scheduler, both ways.**
- Shipped config: the app boots clean — which itself proves the captive dependency is gone, since scope validation would otherwise throw — and logs *"the in-app daily summary scheduler is disabled"*.
- Enabled via an `email.local.json` placed in the **build output only** (the repository's `config/` was never touched), with `SendTimeLocal` two minutes ahead: the disabled line vanished, and at the appointed minute it logged *"Scheduled daily summary run failed: No recipients configured … **the summary was built but not sent**"*. **Built** is the proof — it resolved the scoped mail service from its new scope and queried the real database. It wrote `booking_count = 1`, matching the single real booking for that date.

**Frontend changes.** Build, 48/48 tests, line-by-line read of both files, and a live server-side prerender: logged in over HTTP and fetched `/admin`, which returned 200 with the employee list rendered — proving `AdminEmployeesViewModel` constructs and loads against the real database with the new calls in place.

**All test artefacts were removed.** Every `email_log` row created during testing was deleted; the override file was deleted; the database ended at 8 employees / 8 bookings / 0 `email_log` rows, and `config/` at its original five files.

---

## 5. The limit of that verification — read this before trusting the UI

**No button in this application has ever been clicked by anyone.** Browser automation was declined this session, and Blazor Server's interactive handlers cannot be driven over plain HTTP. The two flows rewritten today are code-correct and render-correct, but have never been *operated*:

1. the whole-week *"Apply Menu N"* skipped-day list, and
2. the admin delete-versus-deactivate confirmation.

These are §A of [`TEST_CHECKLIST.md`](../TEST_CHECKLIST.md) and are the highest-value manual tests in the project.

**A data condition that blocks one of them:** every seeded employee has exactly one booking, so the admin *delete* branch (employee with no history) is unreachable as-is. Register a new employee from the booking page to get one.

---

## 6. One judgment call left open

`EmployeesNonDeletableConfirmTemplate` reads *"{0} has {1} bookings"*, but `HasBookingsAsync` returns only a bool. Rather than reword the dialog or extend the service contract, the `IReportService` call was kept **inside the has-bookings branch only**, purely to supply that count. The common no-history path no longer touches the report service at all.

Dropping the count from the message would let `IReportService` leave `AdminEmployeesViewModel` entirely. Massimo was told; no decision taken.

---

## 7. Checkpoint status

| Checkpoint | Status |
|---|---|
| **V1** plan · **V2** schema · **V3** backend | ✅ Validated previously |
| **V4** UI walkthrough | ⏳ **Still pending — now the critical path.** §A and §B of `TEST_CHECKLIST.md` |
| **V5** email sample | ✅ **Now genuinely satisfied** — the `.eml` is built from real database rows, verified against SQL |
| **V6** end-to-end | ⏳ Unblocked and ready; §C of the checklist |

---

## 8. What remains

Ordered by what blocks what.

| # | Item | Owner |
|---|---|---|
| 1 | **V4 walkthrough** — §A then §B of `TEST_CHECKLIST.md` | **Massimo** |
| 2 | **SMTP relay host + recipient list** — every email path is proven right up to this line; config-only, no code change | **Massimo** |
| 3 | V6 end-to-end (§C) | After 1 |
| 4 | Real deployment onto a target machine (§E) — never performed | After 1–3 |
| 5 | Live cut-off countdown does not tick (computed per page load) | Small code change |
| 6 | No toast on new-employee registration | Small code change |
| 7 | `unaccent` fallback path untested | Test only |
| 8 | No machine-verified accessibility/contrast check | Tooling |
| 9 | Nine benign `MSB3277` EF-version warnings | Cosmetic |

**Open UI question, deliberately left open:** the menu grid appears only after a name is entered — should the week's menus be visible beforehand? Massimo chose to try the current behaviour first and ask for a change if it bothers him.

**Deployment hazard, unchanged:** both `.csproj` files copy `config/*.json` by wildcard, so `database.local.json` — the real password — lands in `dotnet publish` output. Delete it before distributing.

---

## 9. Working rules confirmed again today

- **All code produced by Sonnet agents** (standing global directive), then verified by the orchestrator — build, tests, live run, direct SQL — never trusted from the agent's own report. This caught real problems again today.
- **Never commit to git.** Zero commits, still.
- **Check the plan against the code, not against the notes.** Every one of today's three surprises — the dead scheduler, the captive dependency, the mis-described skip-reason bug — came from reading the source rather than the carried-forward summary.
- **Prefer a discriminating test over a confirming one.** The mailer verification only became meaningful once a property was found that demo data and real data did *not* share.

---

## 10. Document index

| File | Contents |
|---|---|
| `Docs/TEST_CHECKLIST.md` | **Start here** — everything not yet exercised by a human |
| `Docs/IMPLEMENTATION_PLAN.md` | The validated specification |
| `Docs/DATABASE_SCHEMA.md` | Schema reference (V2) |
| `Docs/DEBUGGING.md` | Running and debugging here. §6.1 is new: triggering the email from the website. §10 is the current gap list |
| `Docs/INSTALLATION.md` | Target-machine deployment and setting-by-setting configuration |
| `Docs/USER_GUIDE.md` / `USER_GUIDE.fr.md` | End-user guide, English and French |
| `Docs/AGENT_EMAIL_SUMMARY.md` | E10 (mailer → database) and E11 (scheduler) records |
| `Docs/AGENT_FRONTEND_SUMMARY.md` | F11 record, including the V4 walkthrough script |
| `Docs/AGENT_BACKEND_SUMMARY.md` | Backend history, V2/V3 records |

---
---

# Part 2 — Session summary — 13 August 2026 (second session)

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer`
**Session outcome:** the UI language switch went from "changes nothing until you press F5" to working on click, and the booking page now carries its own state (`?week=`/`?employee=`) so the switch's page reload no longer wipes the selected week, the employee and the grid. Build is 0 errors and **52/52 tests pass** (was 48). The project still waits on the same thing it waited on before: **a human operating the UI**. Nothing is committed; the repository still has zero commits.

Written to be read **cold**. Part 1 above is the earlier session on this same date; its §7 checkpoint table and §10 document index are still accurate and are not repeated. Part 1's §5 ("no button in this application has ever been clicked") is **still true** and now matters more — see §5 below.

---

## 1. What changed in this session

| # | Change | Why it mattered |
|---|---|---|
| 1 | Language switch now writes the culture cookie, then `Navigation.NavigateTo(Navigation.Uri, forceLoad: true)` | The auto-apply path was **dead code**. Culture only ever took effect on a manual refresh |
| 2 | Deleted the entire live-switch mechanism: `CultureState.Changed`, `CultureState.SetCulture`, and `Routes.razor`'s `@key` + `@code` subscription | It could never have worked, and it *looked* like it should — actively misleading to the next reader |
| 3 | `CultureState` reduced to read-only `Current` + `bool IsSupported(string)` | Keeps the genuine supported-culture guard without pretending it can mutate culture |
| 4 | Booking page URL now mirrors its own state: `/?week=YYYY-MM-DD&employee=<id>` | The reload lost week + employee. `AppHeader` is a layout component and cannot know booking state, so the **URL** carries it |
| 5 | `BookingViewModel.InitializeAsync(DateOnly? weekMonday = null, int? employeeId = null)` restores from those params | Rehydrates week, employee, name field and grid after the reload |
| 6 | **Fixed an HTTP 500**: `/?employee=abc` crashed the home page | An unparseable `[SupplyParameterFromQuery] int?` throws *during render* |
| 7 | 4 new unit tests, 3 new test fakes, and the test project now references `LunchOrganizer.Web` | Nothing had ever referenced the Web project from tests |

**Files changed (7):** `src\LunchOrganizer.Web\Localization\CultureState.cs`, `...\Localization\CultureCookie.cs` (comment only), `...\wwwroot\js\culture.js` (comment only), `...\Components\Routes.razor`, `...\Components\Layout\AppHeader.razor`, `...\Components\Pages\Booking.razor`, `...\ViewModels\BookingViewModel.cs`. Plus `tests\LunchOrganizer.Tests\Unit\BookingViewModelInitializeTests.cs` (new), `tests\LunchOrganizer.Tests\TestSupport\{FakeStringLocalizer,FakeToastService,FakeErrorMessageResolver}.cs` (new), `tests\LunchOrganizer.Tests\LunchOrganizer.Tests.csproj`.

Final state, verified directly: **0 errors**, and

```
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52, Duration: 2 s - LunchOrganizer.Tests.dll (net10.0)
```

---

## 2. Problems found that were not on anyone's list

**The language-switch auto-apply was dead code in three independent ways.** Any one of them alone was fatal:

1. **`Routes.razor` was static SSR.** `App.razor:19` renders `<Routes />` with no render mode and `Routes.razor` declared none, so its `StateHasChanged` could never produce a second render and its `@key="CultureState.Current.Name"` remount never fired at runtime.
2. **A scope split.** `CultureState` is registered `AddScoped` (`Program.cs:68`). A **static-SSR** component resolves it from the **HTTP request** scope; an **interactive** component resolves it from the **circuit** scope. `Routes` and `AppHeader` therefore held *two different instances*, so the event `Routes` subscribed to could never be raised by `AppHeader`.
3. **`CultureInfo.CurrentCulture` is thread-static.** Mutating it inside a circuit event handler, on a pooled thread, is unreliable by construction.

> **Carry-forward rule (Blazor Web App with per-component islands):** a `scoped` service shared between a static-SSR component and an interactive component is **two instances, not one**. Never wire an event or shared mutable state between the two halves. If a service must be shared, it has to be the circuit's, and both ends must be interactive.

> **Carry-forward rule (culture):** in Blazor Server the culture comes from the **request** (`RequestLocalizationMiddleware`). Do not try to change it in-circuit. Write the cookie and reload.

**An HTTP 500 reachable from the address bar.** `[SupplyParameterFromQuery] public int? EmployeeParam` throws `System.InvalidOperationException: Cannot parse the value 'abc' as type 'System.Nullable`1[System.Int32]' for 'employee'.` The throw happens during render, so a hand-edited, truncated or stale URL returned a 500 from the application's home page.

> **Carry-forward rule:** bind any user-editable query parameter as `string?` and parse it yourself (`TryParse`) so a bad value degrades to null. Blazor's built-in conversion throws, and a throw during render is a 500. `Booking.razor` now does this for **both** `week` and `employee`, each with a comment saying not to "simplify" it back.

**Two doc comments asserted things that were not true.** `CultureState.cs:7-13` claimed that setting `CurrentCulture` in a circuit "persists across that circuit's subsequent renders … no HTTP navigation or page reload needed" — the precise false premise that caused the bug. `BookingViewModel`'s `InitializeAsync` remarks described a language switch as "a page remount … [that] reuses this same scoped ViewModel instance", which is now doubly wrong (it is a full reload, hence a new circuit and a new instance). Both were rewritten.

> **Carry-forward rule:** a comment asserting framework behaviour is a *claim*, not documentation. This codebase's comments are unusually good, which makes the wrong ones unusually dangerous.

---

## 3. Corrections to earlier documents

- **`Docs/IMPLEMENTATION_PLAN.md:374`** says the switch "Sets the cookie and reloads the current page keeping the selected week, employee and unsaved selection." Before this session **none** of that was implemented. Now the cookie, the reload, the week and the employee all are — but **"unsaved selection" is still not**. The line is accurate except for that last clause.
- **`Docs/USER_GUIDE.fr.md:169-170`** told users the page reloads on a language switch. That was **inaccurate** before this session (it did not reload) and is **now accurate**.
- **Part 1 §8 item 5** ("Live cut-off countdown does not tick") — untouched this session, still open. Listed again in §7 below so nothing is lost.
- Neither of the two doc files above was edited — see §6.

---

## 4. Verification actually performed

Every "it works" below is backed by output produced in this session. Agent reports were treated as claims and re-checked independently; see §9.

**Build and tests, run directly:** `0 Error(s)`, 9 `MSB3277` warnings, and the 52/52 line quoted in §1.

**Culture cookie round-trip — chosen because it discriminates.** The cookie was sent in *exactly* the URL-encoded form `culture.js` writes (`c%3Den-CH%7Cuic%3Den-CH`), not a hand-built value, so the check would have failed if the JS encoding were wrong:

| Cookie sent | `<html lang>` | Active button | Its `aria-label` |
|---|---|---|---|
| *(none — config default)* | `fr` | — | — |
| `c=en-CH\|uic=en-CH` | `en` | English | `Switch to English` |
| `c=fr-CH\|uic=fr-CH` | `fr` | French | `Passer en français` |

The active-button and `aria-label` columns matter: they prove `CultureState.Current` reflects the cookie after a reload *and* that `IStringLocalizer` resolved the right resource file.

**State restore against the real PostgreSQL database.** `?employee=1` prefilled the name input with `value="Alice Martin"` and rendered **5** `grid-cell-number` cells (one working week); with **no** parameter the same page rendered **0** cells and no name. The no-parameter baseline is what makes this meaningful — it proves the parameter is doing the work rather than the page always rendering a name.

**The `week` parameter genuinely selects the week:** `week=2026-08-10` → heading `lundi 10 août`; `week=2026-08-17` → `lundi 17 août`.

**Malformed / stale URLs — all HTTP 200, no unhandled exceptions:**

| URL | Result |
|---|---|
| `?week=2026-08-10&employee=1` | Alice Martin, 5 cells, correct week |
| `?employee=abc` · `?employee=1.5` · `?employee=-1` | 200, no employee, no crash *(`abc` was **500** before the fix)* |
| `?employee=99999` | 200, no employee — stale link degrades cleanly |
| `?week=not-a-date&employee=1` | 200, falls back to current week, employee kept |
| `?week=2026-07-06&employee=1` | past week clamped to the current week |
| `?employee=1&employee=2` | 200, first value wins |

**The combined scenario — the one that actually matters.** Same URL, cookie flipped:

| Cookie | `<html lang>` | Week heading | Name | Grid cells |
|---|---|---|---|---|
| `fr-CH` | `fr` | `lundi 10 août` | Alice Martin | 5 |
| `en-CH` | `en` | `Monday 10 August` | Alice Martin | 5 |

Language changes; week, employee and grid survive. Note the heading proves both the resource lookup *and* the culture-sensitive date formatting.

**Server log:** zero occurrences of `unhandled exception` across every request above.

**A warning-count discrepancy was chased down rather than waved away.** An agent reported 10 warnings including one that looked new. An incremental `dotnet build` shows **9** (all `MSB3277`); a full `dotnet build --no-incremental` additionally surfaces one pre-existing `warning CS9113: Parameter 'clock' is unread.` in `src\LunchOrganizer.Email`. Build-mode dependent, pre-existing, unrelated to this work.

---

## 5. The limit of that verification — read this before trusting the language switch

**No button in this application has ever been clicked.** Part 1 §5 said this and it is still true. It now cuts deeper, in a way that is easy to miss:

**Only the *read* half of the new feature is proven.** `OnAfterRender` does not run during prerendering, so plain HTTP cannot exercise the code that *writes* `week`/`employee` into the URL. Concretely:

- ✅ **Proven:** a URL that already contains `?week=…&employee=…` rehydrates week, employee, name field and grid correctly, against the real database, in both languages.
- ❌ **Not proven:** that clicking a week arrow or picking a name actually *puts* those parameters into the URL. That is `Booking.razor`'s `OnAfterRender` → `NavigateTo(target, replace: true)`.
- ❌ **Not proven:** that the `OnAfterRender` loop guard terminates in practice. The navigate → re-render → navigate cycle was traced **by reading the code only**. If the URL-equality comparison is ever not exact, this is an infinite render loop.
- ❌ **Not proven:** the click path itself — `SwitchCulture` → JS cookie write → `forceLoad` reload — since it is a Blazor interactive handler.

**So the honest status is: if the URL has the parameters, everything works; whether the application puts them there is unverified.** Browser verification was offered twice this session and not run (it was declined in an earlier session; no authorisation was given this time either). It is item 1 in §7.

Other gaps:
- **Unsaved (un-submitted) menu ticks are still lost** on a language switch. `ReloadSelectedWeekViewAsync` re-seeds `PendingSelections` from *saved* state (`BookingViewModel.cs:547-549`), so previously submitted bookings do come back — only pending edits do not. By design, not verified.
- The new tests are **unit-level against fakes**. There are no component/bUnit tests in this project, so no test exercises `Booking.razor` itself.
- Visiting a bare `/` now immediately rewrites the address bar to include `?week=…`. Intended (it is what makes the reload work) but never seen in a browser.

---

## 6. Decisions and open judgment calls

**Decided 13 August 2026 — Option A (cookie + reload) over Option B (make the live switch work).** B would have required whole-app interactivity (changing render behaviour for `Login.razor`, `Error`, `NotFound`), reading culture from `CultureState` everywhere instead of `CurrentCulture`, and fixing four sites that cache already-translated text (`BookingViewModel.SkippedDaysWithLocalizedReasons`, all toast text, confirm-dialog bodies, `PriceEditor._editText`) — and `<html lang>` in `App.razor:2` would *still* have lagged, because the root component is always static. A fixes all of those for free.

**Then decided — A+ variant (a) over variant (b).** Massimo tried plain A, found losing the selections annoying, and asked for A+. Two ways to build it: **(a)** the booking page keeps the URL in sync with its own state, or **(b)** `AppHeader` asks a shared service what to preserve. (a) was chosen because (b) couples a layout header to one specific page. The payoff: **`AppHeader.razor` needed no change at all** — it reloads `Navigation.Uri`, which now already carries the state.

**Decided — the URL carries the employee *id*, not the name.** A name in a query string lands in browser history and request logs, which is undesirable on a shared office machine.

**Decided — it must be `NavigationManager.NavigateTo(..., replace: true)`, never a JS `history.replaceState`.** Only `NavigationManager`'s own navigation updates `NavigationManager.Uri`, and `AppHeader` reads `Navigation.Uri` to build its reload target. A raw `replaceState` would update the address bar but leave `Navigation.Uri` stale, silently defeating the whole feature. This is recorded in a comment in `Booking.razor` because it is invisible and easy to "clean up".

**Left open — preserving unsaved menu ticks.** Would need `sessionStorage`: write on every `SetPendingSelection`, restore on init. Massimo was told; **no decision taken.** Deliberately not built, to keep this change small ahead of the never-yet-performed V4 walkthrough.

**Left open — the two inaccurate doc lines** (`IMPLEMENTATION_PLAN.md:374`, `USER_GUIDE.fr.md:169-170`). Not edited, because their correct final wording depends on the `sessionStorage` decision above.

**Carried over untouched from Part 1 §6** — `EmployeesNonDeletableConfirmTemplate` still reads "{0} has {1} bookings" and `IReportService` is still injected into `AdminEmployeesViewModel` purely to supply that count. No decision taken.

---

## 7. What remains

Ordered by what blocks what.

| # | Item | Owner |
|---|---|---|
| 1 | **Browser verification of the language switch** — click → cookie → reload → URL params, plus the `OnAfterRender` loop guard. Closes the §5 gap. Offered twice, needs authorisation | **Massimo** |
| 2 | **V4 walkthrough** — §A then §B of `TEST_CHECKLIST.md`. Still the critical path | **Massimo** |
| 3 | **SMTP relay host + recipient list** — config only, no code change | **Massimo** |
| 4 | Decide whether unsaved menu ticks must survive a switch (`sessionStorage`) | **Massimo** |
| 5 | Reword `IMPLEMENTATION_PLAN.md:374` + `USER_GUIDE.fr.md:169-170` | after 4 |
| 6 | V6 end-to-end (§C) | after 2 |
| 7 | Real deployment onto a target machine (§E) — never performed | after 2–3 |
| 8 | Live cut-off countdown does not tick (computed per page load) | small code change |
| 9 | No toast on new-employee registration | small code change |
| 10 | `unaccent` fallback path untested | test only |
| 11 | No machine-verified accessibility/contrast check | tooling |
| 12 | Nine `MSB3277` warnings + one `CS9113` ('clock' unread, `LunchOrganizer.Email`) | cosmetic |

**Deployment hazard, unchanged from Part 1:** both `.csproj` files copy `config/*.json` by wildcard, so `database.local.json` — the real password — lands in `dotnet publish` output. Delete it before distributing.

---

## 8. Environment facts worth not rediscovering

New this session; Part 1 and the 12 August summary's §7 remain accurate for everything else.

- **Run the app:** `dotnet run --project src/LunchOrganizer.Web --launch-profile http --no-build` → `http://localhost:5226` (profiles are in `src\LunchOrganizer.Web\Properties\launchSettings.json`). It answers in about **1 second**.
- **On Windows the running app locks its build output** — stop the server before rebuilding, or the build fails.
- **Forcing a culture over plain HTTP** (no browser needed): send header `Cookie: .AspNetCore.Culture=c%3Den-CH%7Cuic%3Den-CH`. That is exactly the encoding `culture.js` writes, so it is also the honest way to test the cookie contract.
- **Useful prerendered-HTML assertion markers:** `grid-cell-number` occurs **5** times for a fully rendered working week; the selected employee appears as `value="Alice Martin"`; the week heading appears as `lundi 10 ao&#xFB;t` / `Monday 10 August`.
- **Localized text is HTML-encoded in the response** — `ao&#xFB;t` = *août*, `fran&#xE7;ais` = *français*. Grep for the encoded form or you will conclude, wrongly, that nothing rendered.
- **Seeded data:** employee **id 1 = Alice Martin**; 8 employees, 1 booking each.
- **Warning counts are build-mode dependent:** incremental `dotnet build` = 9, `--no-incremental` = 10. Both are pre-existing.
- The test project now references `LunchOrganizer.Web`; `tests\...\LunchOrganizer.Tests.csproj:36`. (Line 18 of that file mentions `LunchOrganizer.Web.csproj` in a *comment* — not a duplicate reference.)

---

## 9. Working rules confirmed or established

- **All code produced by Sonnet agents** (standing global directive), then verified by the orchestrator — reading each final file, plus build, tests, a live server and real HTTP requests. Never trusted from the agent's own report. **This is what caught the `?employee=abc` 500**, which the agent's own verification pass had reported as fully green.
- **Never commit to git.** Zero commits, still.
- **Agent reports are claims.** Two inaccuracies surfaced this session: a warning count reported as 10-with-one-new (actually build-mode dependent and pre-existing), and a "verified" report that missed a 500 reachable from the address bar.
- **A spec error is the orchestrator's, not the agent's.** The 500 existed because the instructions said `string?` + manual parse for `week` but `int?` for `employee`. The agent implemented the spec correctly. Worth recording because the fix is *reviewing your own spec for asymmetry*, not distrusting the agent more.
- **Prefer a discriminating test over a confirming one** (established in Part 1, applied twice more here): the no-parameter baseline for state restore, and sending the cookie in `culture.js`'s exact encoding rather than a hand-built value.
- **Check the plan against the code, not against the notes** (Part 1). Reapplied: the dead switch was found by reading `App.razor`/`Routes.razor` render modes, not by trusting `IMPLEMENTATION_PLAN.md:374`, which described behaviour that had never been implemented.
