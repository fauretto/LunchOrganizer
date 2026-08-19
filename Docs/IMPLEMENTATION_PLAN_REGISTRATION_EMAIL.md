# Implementation plan — require an email address when self-registering from the booking page

**Written 18 August 2026. Status: ✅ implemented 18 August 2026. Builds clean (0 errors, 0 warnings).
Automated tests written but NOT YET RUN. Manual validation pending — see
[`TEST_PLAN_SQLSERVER_MIGRATION.md`](TEST_PLAN_SQLSERVER_MIGRATION.md) §4b.**

Implementation notes, where reality differed from the plan below:

- `MimeKit` needed no new package reference — it reaches the Web project transitively through the
  existing `LunchOrganizer.Email` project reference.
- Validation went into a new shared `LunchOrganizer.Email.Validation.EmailAddressValidation`, and
  `EmployeeConfirmationSender` was switched to call it too, so there is exactly one implementation
  rather than two that could drift apart.
- `MailboxAddress.TryParse` alone proved too lenient for a UI gate (MimeKit accepts a bare local-part
  with no domain), so the shared validator adds a `Contains('@')` check. The invariant preserved is
  one-directional: the UI may be **stricter** than the sender, never looser.
- §4.3's reactivation fix also applies to the **admin** add-employee path, since both share
  `RegisterAsync`. An admin adding a name matching a deactivated employee now reactivates that row
  instead of silently returning it inactive. This is a behaviour change beyond the booking page, and a
  desirable one, but it was not explicitly requested — flagged here rather than buried.

Companion to [`IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md`](IMPLEMENTATION_PLAN_CONFIRMATION_EMAILS.md), which
added per-employee confirmation emails. This plan closes the gap that made that feature unreachable for
self-registered employees.

---

## 1. The requirement

On the booking page, when someone types a name that is not in the database, the register panel must also
ask for an **email address**, and must not let them register without one. The address is stored on the new
`employees` row so the mailer can send that person their own lunch confirmation.

## 2. Why this is needed, not just nice

`EmployeeService.RegisterAsync(string fullName)` (`src/LunchOrganizer.Services/Services/EmployeeService.cs:32`)
constructs `new Employee { FullName = fullName.Trim(), IsActive = true }`. `Email` is never set, so it is
`NULL`.

`EmployeeConfirmationSender.SendAllAsync` skips any booking whose `Email` is null or whitespace
(`src/LunchOrganizer.Email/Services/EmployeeConfirmationSender.cs:40-44`).

Therefore **every employee who has ever self-registered from the booking page is permanently excluded from
confirmation emails**, silently, with only a counter in the outcome to show for it. The seeded employees have
addresses; real self-registered ones never will. This is the single highest-value follow-up to the
confirmation-email feature.

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| **D1** | The email is **mandatory** on the booking-page registration path. The confirm button stays disabled until the field holds a valid address. | Massimo's word was "force him". A skippable field would recreate the exact hole this plan closes. |
| **D2** | The **admin** "add employee" path (`AdminEmployeesViewModel.cs:193`) is **left unchanged** — email stays optional there. | Not part of the request. An admin creating a placeholder row for someone who has not started yet is a legitimate case, and the admin edit screen can already set an email afterwards. |
| **D3** | `IEmployeeService.RegisterAsync` gains a trailing **optional** parameter: `string? email = null`. | Keeps D2's call site compiling untouched, and matches the precedent set by `DailySummaryDto.EmployeeBookings` in the previous feature. |
| **D4** | Validation uses **`MailboxAddress.TryParse`** — the same check the sender applies. | If the UI accepted an address the sender later rejects, the user would be told they are registered for confirmations and then silently not receive any. The two validators must not disagree. |
| **D5** | **No unique constraint on `email`.** Duplicates are accepted. | Shared team mailboxes are legitimate, and there is no such constraint today. Adding one would be a schema change beyond the request that could reject valid data. |
| **D6** | Existing employees with `NULL` email are **not** back-filled or prompted. | Out of scope. See §7 — worth raising separately. |
| **D7** | The email is trimmed before storage, but **not** lower-cased. | Local-parts are technically case-sensitive; nothing in the app compares addresses for equality. |
| **D8** | **Fix the reactivation hole found while planning** — see §4.3. | Without it, D1 can be defeated in a way that leaves the user believing they registered. |

## 4. Design

### 4.1 Service layer

`src/LunchOrganizer.Services/Abstractions/IEmployeeService.cs`

```csharp
Task<OperationResult<EmployeeDto>> RegisterAsync(string fullName, string? email = null, CancellationToken ct = default);
```

`src/LunchOrganizer.Services/Services/EmployeeService.cs` — set `Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim()`
on the new `Employee`. Whitespace collapses to `NULL` rather than an empty string, so the sender's
`IsNullOrWhiteSpace` check and the database agree on what "no address" means.

The service does **not** enforce D1. Mandatory-ness is a booking-page rule (D2), so it belongs in the
booking view model, not in a service shared with the admin screen.

### 4.2 Booking view model

`src/LunchOrganizer.Web/ViewModels/BookingViewModel.cs`

- New bindable property `NewEmployeeEmail` (with `SetProperty`, like `EmployeeQuery`).
- New computed `bool IsNewEmployeeEmailValid` → `MailboxAddress.TryParse(NewEmployeeEmail, out _)`.
  This requires a `MimeKit` reference in the Web project; if that is unwanted, use the same call behind a
  tiny `IEmailAddressValidator` in `LunchOrganizer.Domain` — but **do not** hand-roll a regex, per D4.
- `RegisterNewEmployeeAsync(string fullName)` → `RegisterNewEmployeeAsync(string fullName, string email)`,
  which returns `false` without calling the service when the address is invalid.
- `NewEmployeeEmail` must be cleared in `SelectEmployeeInternalAsync` and on the cancel path, alongside the
  existing `ShowRegisterPrompt = false`, so a stale address cannot leak into the next registration.

### 4.3 The reactivation hole (D8)

Found while reading the code; it is pre-existing but this feature makes it materially worse.

`OnEmployeeQueryChangedAsync` searches `GetAllAsync(includeInactive: false)`, so a **deactivated** employee's
name produces no match and the register panel appears. `EmployeeRepository.AddAsync` is a get-or-create:
its `INSERT … WHERE NOT EXISTS` finds the existing row, skips the insert, and returns the existing employee —
still inactive, still with whatever email it had, **discarding the address just typed**.

The user is shown the booking grid and believes they registered with their email. They did not. They will not
receive confirmations, and they remain inactive.

Fix, in `EmployeeService.RegisterAsync`, after `AddAsync` returns: if the returned employee is inactive, or
has no email while one was supplied, reactivate it and store the address via `repo.UpdateAsync`. This makes
"register" mean the same thing to the user whether or not a dormant row happened to exist.

### 4.4 Booking page UI

`src/LunchOrganizer.Web/Components/Pages/Booking.razor`, inside the existing `register-prompt` div, between
the similar-names list and the actions div:

- A `<label>` + `<input type="email">` bound to `Vm.NewEmployeeEmail`, with `@bind:event="oninput"` so the
  confirm button enables as the user types rather than on blur.
- A short explanatory line — the user should know *why* they are being asked, i.e. to receive their lunch
  confirmation. Without it, a mandatory field on a lunch form reads as data collection for its own sake.
- `disabled="@(!Vm.IsNewEmployeeEmailValid)"` on `register-prompt-confirm-btn`.
- A validation hint shown only once the field is non-empty and invalid — never on an untouched field.

`ConfirmRegisterAsync` passes `Vm.NewEmployeeEmail`.

`Booking.razor.css` — style the new field consistently with the existing prompt; keep the disabled button
visually distinct, and rely on more than colour alone for the disabled state.

### 4.5 Localization

Four new keys in **both** `Resources/Booking.resx` (EN) and `Resources/Booking.fr.resx` (FR):

| Key | EN | FR |
|---|---|---|
| `EmployeeRegisterEmailLabel` | Email address | Adresse e-mail |
| `EmployeeRegisterEmailPlaceholder` | name@company.com | nom@entreprise.com |
| `EmployeeRegisterEmailWhy` | We will send your lunch booking confirmation to this address. | Nous enverrons la confirmation de votre réservation à cette adresse. |
| `EmployeeRegisterEmailInvalid` | Please enter a valid email address. | Veuillez saisir une adresse e-mail valide. |

Per `TEST_CHECKLIST.md` §B2, every visible string must switch language. A missing FR entry falls back to
English silently, so both files must be edited in the same change.

### 4.6 Fakes

`src/LunchOrganizer.Fakes/Services/FakeEmployeeService.cs:40` must match the new signature and must actually
store the email — otherwise the fake cannot reproduce the bug this plan fixes and tests against it prove
nothing.

## 5. Files

| File | Change |
|---|---|
| `src/LunchOrganizer.Services/Abstractions/IEmployeeService.cs` | signature |
| `src/LunchOrganizer.Services/Services/EmployeeService.cs` | store email; §4.3 reactivation |
| `src/LunchOrganizer.Web/ViewModels/BookingViewModel.cs` | property, validation, signature, reset paths |
| `src/LunchOrganizer.Web/Components/Pages/Booking.razor` | input, hint, disabled button |
| `src/LunchOrganizer.Web/Components/Pages/Booking.razor.css` | styling |
| `src/LunchOrganizer.Web/Resources/Booking.resx` | 4 keys |
| `src/LunchOrganizer.Web/Resources/Booking.fr.resx` | 4 keys |
| `src/LunchOrganizer.Fakes/Services/FakeEmployeeService.cs` | signature + store |
| `tests/…/BookingViewModelTests.cs` | new cases |

**No database migration.** `employees.email` already exists, nullable, and `AddAsync` already writes it.

## 6. Tests

1. Registering with a valid address stores it on the new row.
2. Registering with a blank address is rejected by the view model without reaching the service.
3. Registering with a malformed address (`"bob"`, `"a@"`, `"a b@c.com"`) is rejected.
4. A valid address is trimmed before storage.
5. `NewEmployeeEmail` is cleared after a successful registration.
6. `NewEmployeeEmail` is cleared when an existing employee is selected instead.
7. **§4.3:** registering a name matching a *deactivated* employee reactivates them and stores the new address.
8. **End-to-end intent:** an employee registered this way, who then books a lunch, appears in
   `DailySummaryDto.EmployeeBookings` **with** an email and is counted in `sent`, not `skippedNoEmail`.
   This is the test that proves the feature achieved its purpose; the rest only prove it was wired up.
9. The admin add-employee path still compiles and still permits a null email (D2 regression guard).

## 7. Out of scope — worth raising separately

- **Existing employees with `NULL` email get nothing.** This plan only affects new registrations. Anyone
  already in the database without an address stays silently excluded. A follow-up could prompt a known
  employee for a missing address when they select their name.
  Note `TEST_PLAN_SQLSERVER_MIGRATION.md` deliberately nulls Alice Martin's email to exercise the skip rule —
  that row is expected to stay null.
- **No unique constraint on email** (D5) — two employees may share an address; both get their own message.
- **Still no success toast** on registration (`TEST_CHECKLIST.md` §F). Adding a mandatory field makes the
  silent success more jarring, but it is a separate, pre-existing gap.
