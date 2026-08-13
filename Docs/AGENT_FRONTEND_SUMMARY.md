# Agent — Frontend · Execution history

**Scope:** `src/LunchOrganizer.Web/Components/`, `ViewModels/`, `wwwroot/`, admin authentication
**Reference:** [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) §6, §11.9
**Rule:** an entry is added **only when the step has been validated**. Steps in `Pending` state are the roadmap, not a claim of work done.

---

## Roadmap

| Step | Description | Gate | Status |
|------|-------------|------|--------|
| F0 | Contracts + fakes received from Phase 0; app runs against in-memory services | — | Pending |
| F1 | Design system: `theme.css` custom properties (blue scale, gradients), typography, layout primitives, `theme.sample-dark.css` | — | Pending |
| F1b | Localization infrastructure: `.resx` resources (Shared/Booking/Admin/Errors), `RequestLocalizationMiddleware`, culture cookie, `FR · EN` header switch preserving page state | **V4** | Pending |
| F2 | App shell: header, navigation, toast host, confirmation dialog, busy indicator | — | Pending |
| F3 | Shared components: `WeekPicker`, `EmployeeAutocomplete`, `WeeklyBookingGrid`, `MenuCard`, `PriceEditor` | — | Pending |
| F4 | Booking page + `BookingViewModel` — week selection, employee identification, today banner, weekly grid, whole-week shortcut, submit | **V4** | Pending |
| F5 | Admin authentication: login page, cookie auth, `AuthenticationStateProvider` over `admin-users.json`, `[Authorize]` on `/admin/*`, rate limiting | — | Pending |
| F6 | Admin · Report tab — employee + date range, per-line price, period total, CSV export | **V4** | Pending |
| F7 | Admin · Employees tab — list, add, edit, delete-or-deactivate with guard messages | **V4** | Pending |
| F8 | Admin · Menus & Prices tab — weekly menu CRUD, copy description to week, day price editor | **V4** | Pending |
| F9 | Responsive pass (< 900 px stacking), accessibility pass (focus, contrast, keyboard grid) | **V4** | Pending |
| F10 | Concurrency UX: double-submit guard, optimistic-concurrency conflict message, cross-circuit refresh via `IBookingChangeNotifier` | **V4** | Pending |
| F11 | Frontend adopts the backend's own contracts: `SkippedDayDto.ReasonCode`/`ReasonArgs`, `IEmployeeService.HasBookingsAsync` / `DeleteOrDeactivateWithOutcomeAsync` | **V4** | **Code validated 13.08.2026; UI walkthrough pending** |

---

## Validated steps

### F11 — Frontend adopts the backend contracts (code validated; walkthrough pending)
- **Validated (code):** 2026-08-13 — **the two UI flows below still need a human at V4.**

**What was wrong.**
- *Skip reasons.* The note carried forward from 12 August said these were "untranslatable English". That was **not accurate** — `BookingViewModel` already localized them and never displayed the raw English `SkippedDayDto.Reason`. The real defect was subtler: it re-derived each reason from the freshly reloaded day's `EditState`, cross-referenced by date. That is a guess about the past inferred from present state, and the two can disagree.
- *Delete vs deactivate.* `AdminEmployeesViewModel` decided which confirmation to show by calling `IReportService.GetReportAsync(id, DateOnly.MinValue, DateOnly.MaxValue)` and counting rows — materialising an employee's entire booking history, with joins, merely to ask "any?" — and then decided what had actually happened by reloading the employee list and checking whether the row had vanished or gone inactive.

**What changed.**
- `BuildSkippedDaysWithLocalizedReasons` now switches on `skipped.ReasonCode` (`BookingDateInPast` / `BookingCutOffPassed` / `MenuNotFoundForDay`) and takes the cut-off time from `ReasonArgs[0]` rather than re-reading `AppOptions`. The old `EditState` derivation survives as `BuildFallbackSkipReason`, used only when `ReasonCode` is null — a genuinely reachable path, since `FakeBookingService` supplied no codes. `FakeBookingService` was updated to emit the same codes as the real `BookingService`, so fake and real now agree; the fallback remains for safety.
- `DeleteOrDeactivateEmployeeAsync` now decides via `HasBookingsAsync` and reports the outcome from `DeleteOrDeactivateWithOutcomeAsync`'s `EmployeeDeletionOutcome`. The `stillPresent` inference block is gone. **The employee-with-no-history path no longer touches `IReportService` at all.**
- **Judgment call left in place:** `EmployeesNonDeletableConfirmTemplate` reads *"{0} has {1} bookings"*, and `HasBookingsAsync` returns only a bool. Rather than reword the dialog or extend the service contract, the report call was kept **inside the has-bookings branch only**, purely to supply that count. Dropping the count from the message would let `IReportService` leave this view model entirely — a small follow-up if wanted.

- **Files:** `src/LunchOrganizer.Web/ViewModels/BookingViewModel.cs`, `src/LunchOrganizer.Web/ViewModels/AdminEmployeesViewModel.cs`, `src/LunchOrganizer.Fakes/Services/FakeBookingService.cs`. No `.resx`, DTO, or service interface was touched — every resource key already existed.
- **Verified by:** `dotnet build` 0 errors and no new warnings; `dotnet test` **48/48**; and a live server-side prerender — logged in over HTTP and fetched `/admin`, which returned 200 with the employee list rendered, proving `AdminEmployeesViewModel` constructs and loads against the real database with the new calls in place.
- **NOT verified:** no button was ever clicked. Browser automation was unavailable, and Blazor Server's interactive handlers cannot be driven over plain HTTP. The two flows below are therefore code-correct and render-correct but have never been operated.

**V4 walkthrough script for these two flows:**
1. Booking page → pick a week containing at least one past or cut-off-locked day, enter a name, choose a menu number that is *not* offered every day, press *Appliquer le menu N à tous les jours ouverts*. The skipped list must name each day with the right reason — *Past* / *Closed at 09:00* / *No Menu N that day* — and must read correctly in **both** FR and EN.
2. Admin → Employees → remove someone **with** bookings: expect the deactivate wording with the correct count, and a *deactivated* toast. Then register a fresh employee from the booking page (this is the only way to get one with no history — every seeded employee has exactly one booking) and remove them: expect the delete wording and a *deleted* toast.

<!--
Entry template — append one block per validated step:

### F1 — Design system
- **Validated:** YYYY-MM-DD
- **Implemented:** …
- **Files:** …
- **Verified by:** …
- **Open points:** …
-->

---

## Non-negotiable constraints for this agent

0. **The user is not an engineer.** [Plan §6.6](IMPLEMENTATION_PLAN.md#66-designing-for-a-non-technical-user) governs every UI decision and overrides any convention that would be natural in a developer tool. No ids, no week numbers as labels, no status codes, no system vocabulary. Every message says what the user can do next. This is the script for the V4 review.
0b. **No user-visible literal string anywhere.** Every word a human reads — labels, buttons, validation messages, empty states, dialog text, page titles, `aria-label`s — comes from a resource key ([plan §6.7](IMPLEMENTATION_PLAN.md#67-bilingual-interface--french-default-and-english)). French is the primary language and must read as real French, not a translation of the English. Day and month names come from the culture, never a hard-coded array.
1. **Views contain no business logic.** A `.razor` file binds and displays; the ViewModel orchestrates; the service decides. No repository call from a component, ever.
2. **ViewModels are scoped**, never singleton, and never hold a `DbContext`.
3. **Every command goes through `RunGuardedAsync`** so a double-click cannot start two saves (plan §11.9).
4. **After every save, reload from the service.** Never mutate local state and assume it persisted.
5. **No flashy colours.** Desaturated blue scale and its gradients; every colour lives in `theme.css` as a custom property.
6. **Cut-off and past-day locks are re-checked server-side.** UI disabling is a courtesy, not a control.
7. **No git commands, ever.**
