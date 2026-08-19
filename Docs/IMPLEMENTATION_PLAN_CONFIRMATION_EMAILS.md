# Implementation Plan — Per-employee booking confirmation emails

**Status:** planned 2026-08-18. Implementation delegated; verification pending (§7).
**Related:** `IMPLEMENTATION_PLAN_SQLSERVER.md` — this feature lands *between* step 10 and that plan's
§9 verification gate, so both must be verified in the same sitting.

---

## 1. The requirement

When the mailer sends the daily list of booked lunches to the kitchen/caterer, it must **also** send
each employee who booked a lunch that day a personal confirmation message containing the details of
*their own* booking.

**A confirmation is sent only to employees who have an email address recorded in the database.** An
employee with no address is silently skipped — that is not an error, and it must not affect anyone
else's confirmation or the summary itself.

The request is unambiguous. What follows are the decisions it does not specify; each is called out so
it can be reversed cheaply if a different behaviour was intended.

---

## 2. Decisions taken (none of these were specified; all are cheap to flip)

| # | Decision | Reasoning |
|---|---|---|
| **D1** | **The summary is sent first. Confirmations are sent only if the summary succeeded.** | The kitchen email is the function that matters — someone doesn't get fed if it fails. Confirmations are a courtesy. Never risk the primary job for the secondary one. |
| **D2** | **A failed confirmation never fails the run.** Individual failures are logged and counted; the run's status and process exit code are decided by the summary alone. | The mailer runs under Task Scheduler. A non-zero exit because one employee's mailbox was full would look like the kitchen never got its list. `email_log` would also already say `Sent`, so the states would contradict each other. |
| **D3** | **Confirmations respect `--dry-run`**: written to the pickup directory alongside the summary preview, nothing sent, `email_log` untouched. | Gives a safe way to inspect the new emails before they reach real people. Without this, the only way to see one would be to send it. |
| **D4** | **New toggle `Email:SendEmployeeConfirmations`, default `true`.** | Default `true` because this is the requested behaviour. A toggle exists so it can be switched off without redeploying if the confirmations turn out to be unwelcome. |
| **D5** | **Employees with a blank *or malformed* address are skipped**, not just those with `NULL`. | The requirement says "only if the employee has an email address". An unparseable address is functionally the same as not having one, and letting it through would throw inside the sender and abort the loop for everyone after them. |
| **D6** | **One language for all confirmations: the existing `Email:Language`.** | There is no per-employee language column, and inventing one is out of scope. Flagged in §8 as a known limitation. |
| **D7** | **Confirmation content: date, menu number, menu description, price, and the employee's name.** | "Details of the lunch booked by the employee itself." Price comes from `Booking.PriceSnapshot`, the frozen value — not a recomputed one. |
| **D8** | **No retry, and no second attempt on a later run.** | The day is reserved in `email_log` before the summary is sent, so a re-run is refused. Retrying confirmations would mean either duplicate emails or a second reservation concept. A missed courtesy email is not worth that complexity. Recorded as accepted behaviour in §8. |

---

## 3. Why this is mostly additive

The existing pipeline is well-factored and needs no restructuring:

- `IBookingRepository.GetForDateAsync` **already eager-loads `Booking.Employee` and `Booking.Menu`**,
  so `Employee.Email`, `Menu.MenuNumber`, `Menu.Description` and `Booking.PriceSnapshot` are all
  present in the single query the summary already performs.
- `IEmailSender.SendAsync(EmailMessage)` already takes an arbitrary recipient list, so sending to one
  employee needs no new sender.
- `PickupDirectoryEmailSender` is already injected into `DailySummaryMailService` as `previewSender`
  for dry runs.

**Crucially, no extra database query is needed.** Plan §11.8 requires the daily summary to be read in
exactly one query; that constraint is preserved by carrying the per-employee rows out of the query the
builder already runs.

**One booking per employee per day is guaranteed by the `UNIQUE (employee_id, booking_date)`
constraint**, so no de-duplication is required — an employee cannot receive two confirmations.

---

## 4. Design

### 4.1 New DTO — `EmployeeBookingConfirmationDto`

`src/LunchOrganizer.Services/Dtos/EmployeeBookingConfirmationDto.cs`:

```csharp
public sealed record EmployeeBookingConfirmationDto(
    int EmployeeId,
    string FullName,
    string? Email,
    int MenuNumber,
    string? MenuDescription,
    decimal PriceSnapshot);
```

### 4.2 `DailySummaryDto` gains one optional member

```csharp
public sealed record DailySummaryDto(
    DateOnly Date,
    int TotalBookingCount,
    IReadOnlyList<DailySummaryMenuGroupDto> MenuGroups,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<EmployeeBookingConfirmationDto>? EmployeeBookings = null);
```

**Why optional.** There are eleven construction sites, nine of them in `DailySummaryBodyRendererTests`
which have nothing to do with this feature. A required parameter would force `Array.Empty<…>()` noise
into all nine. The default keeps those tests untouched. The real builder always populates it, and the
consumer treats `null` as empty.

### 4.3 `DailySummaryBuilder` populates it from the same query

Built from the `bookings` list it already has, filtered the same way as the menu grouping
(`b.Menu is not null && b.Employee is not null`), ordered by employee name using the same
culture-aware comparer the class already constructs.

### 4.4 New renderer — `IEmployeeConfirmationBodyRenderer`

`src/LunchOrganizer.Email/Rendering/EmployeeConfirmationBodyRenderer.cs`, mirroring
`DailySummaryBodyRenderer`: stateless, produces subject + HTML + plain text, same 600px centred
Outlook-safe table, same `#1B4F87` header, same footer. Every interpolated value HTML-encoded via
`WebUtility.HtmlEncode` — employee names and menu descriptions are user-entered and reach an HTML
email body, so this is not optional.

### 4.5 New strings in `EmailBodyText`

French default / English, matching the existing `IsFrench(language)` pattern:

| Purpose | French | English |
|---|---|---|
| Header / subject stem | `Confirmation de votre repas` | `Your lunch booking confirmation` |
| Greeting | `Bonjour {name},` | `Hello {name},` |
| Intro | `Votre repas est confirmé pour le {date} :` | `Your lunch is confirmed for {date}:` |
| Menu label | `Menu {n}` | `Menu {n}` |
| Price label | `Prix : {price} {currency}` | `Price: {price} {currency}` |
| No description | *(reuses the existing `NoDescription`)* | |
| Closing | `Bon appétit !` | `Enjoy your meal!` |
| Footer | *(reuses the existing `Footer`)* | |

The currency symbol comes from `AppOptions.Currency` (`CHF`), which means
`EmployeeConfirmationBodyRenderer` needs `IOptionsMonitor<AppOptions>` — the only new dependency in
the feature.

### 4.6 New service — `IEmployeeConfirmationSender`

`src/LunchOrganizer.Email/Services/EmployeeConfirmationSender.cs`. Kept separate from
`DailySummaryMailService` so that method stays readable and this logic is independently testable.

```csharp
public sealed record ConfirmationSendOutcome(int Sent, int SkippedNoEmail, int Failed);

public interface IEmployeeConfirmationSender
{
    Task<ConfirmationSendOutcome> SendAllAsync(
        DailySummaryDto summary, bool dryRun, CancellationToken ct = default);
}
```

Loop per employee booking:
1. Skip when `Email` is null/whitespace → `SkippedNoEmail++`.
2. Skip when `MailboxAddress.TryParse` rejects it → `SkippedNoEmail++`, log a warning naming the
   employee (not the malformed address, to keep it out of logs).
3. Render, then send via `previewSender` when `dryRun`, otherwise `emailSender`.
4. On failure or exception → `Failed++`, log a warning, **continue the loop**. One bad mailbox must
   never stop the others.

### 4.7 `DailySummaryMailService` — the only change to existing orchestration

In the success branch only, immediately after `emailLogRepository.CompleteAsync(… Sent …)`, and in the
dry-run branch after the preview is written:

```csharp
if (options.SendEmployeeConfirmations)
{
    var outcome = await confirmationSender.SendAllAsync(summary, dryRun, ct);
    // appended to the returned message, e.g.
    // "Sent to 2 recipient(s). Confirmations: 5 sent, 2 skipped (no email), 1 failed."
}
```

The run's `EmailSendStatus`, `BookingCount` and `SuggestedExitCode` are **unchanged** by the outcome
(D2). The whole call is wrapped so that an unexpected exception inside confirmation sending cannot
turn a successful summary run into a failure.

### 4.8 Configuration

`config/email.json` gains two keys, and `EmailOptions` two properties:

```jsonc
"SendEmployeeConfirmations": true,
"ConfirmationSubjectPrefix": "Confirmation - "
```

The subject becomes `ConfirmationSubjectPrefix` + the date formatted with the existing
`SubjectDateFormat`, mirroring how the summary subject is built.

### 4.9 DI registration

`IEmployeeConfirmationBodyRenderer` → singleton (stateless, like the summary renderer).
`IEmployeeConfirmationSender` → scoped (it consumes nothing scoped today, but it sits alongside the
scoped `IDailySummaryMailService` and this keeps the lifetimes consistent; the Mailer runs with
`ValidateScopes = true`, which would catch a mistake at startup either way).

Registered in **both** `src/LunchOrganizer.Web/Program.cs` and `src/LunchOrganizer.Mailer/Program.cs`,
which currently mirror each other's email registrations.

---

## 5. Files

**New (5):**
- `src/LunchOrganizer.Services/Dtos/EmployeeBookingConfirmationDto.cs`
- `src/LunchOrganizer.Email/Rendering/IEmployeeConfirmationBodyRenderer.cs`
- `src/LunchOrganizer.Email/Rendering/EmployeeConfirmationBodyRenderer.cs`
- `src/LunchOrganizer.Email/Abstractions/IEmployeeConfirmationSender.cs`
- `src/LunchOrganizer.Email/Services/EmployeeConfirmationSender.cs`

**Modified (8):**
- `src/LunchOrganizer.Services/Dtos/DailySummaryDto.cs`
- `src/LunchOrganizer.Domain/Configuration/EmailOptions.cs`
- `src/LunchOrganizer.Email/Localization/EmailBodyText.cs`
- `src/LunchOrganizer.Email/Services/DailySummaryBuilder.cs`
- `src/LunchOrganizer.Email/DailySummaryMailService.cs`
- `src/LunchOrganizer.Web/Program.cs`
- `src/LunchOrganizer.Mailer/Program.cs`
- `config/email.json`

**Also:** `src/LunchOrganizer.Fakes/Services/FakeDailySummaryBuilder.cs` should populate the new list
so the Web project's fake path stays representative.

---

## 6. Tests to add

In `tests/LunchOrganizer.Tests/Email/`, following the existing style (xUnit + FluentAssertions,
`TestData` helpers, `TestOptionsMonitor`):

**`EmployeeConfirmationSenderTests`** — the requirement's core rules:
1. An employee **with** an email receives exactly one confirmation.
2. An employee **with `null`** email receives none, and is counted in `SkippedNoEmail`.
3. An employee with a **whitespace** email is likewise skipped.
4. An employee with a **malformed** email is skipped, not attempted.
5. A mix of the above: the ones with valid addresses still get theirs — **the skip must not
   short-circuit the loop.**
6. A send failure for one employee does not prevent later employees from being sent.
7. `dryRun: true` routes to the pickup directory and sends nothing.

**`EmployeeConfirmationBodyRendererTests`**:
8. Body contains the employee's own name, menu number, description and price.
9. Body does **not** contain any other employee's name — a confirmation must never leak the full list.
10. French and English variants each produce their own strings.
11. A name containing HTML (`<script>`) is encoded in the HTML body.

**`DailySummaryBuilderTests`** (extend): the builder populates `EmployeeBookings` with one row per
booking, carrying the correct email, menu and frozen price.

**`DailySummaryMailServiceIdempotencyTests`** (extend): a confirmation failure leaves the run's status
`Sent` and exit code `0` (D2).

---

## 7. Verification

Compile-time and unit tests can be run immediately. The end-to-end checks below belong with the
`IMPLEMENTATION_PLAN_SQLSERVER.md` §9 gate, since both are pending:

- `dotnet build` clean; full `dotnet test` green.
- Mailer `--dry-run` for a seeded date: pickup directory contains the summary **plus** one file per
  employee with an address, and none for employees without.
- Inspect one rendered confirmation by eye: correct employee, correct menu, correct price, correct
  language, no other employee's name present.
- Real send (pickup-directory mode is sufficient): confirmations appear, `email_log` still shows one
  row for the day with status `Sent`, and the process exit code is `0`.
- Set `SendEmployeeConfirmations: false` and confirm the summary still sends and no confirmations do.

**Seeded data note:** `DevelopmentSeeder` gives every employee an address, so it exercises the happy
path only. To test the skip rule, clear one employee's email first:
`UPDATE employees SET email = NULL WHERE full_name = 'Alice Martin';`

---

## 8. Known limitations, accepted

1. **One language for everyone** (D6). There is no per-employee language preference in the schema.
2. **No retry** (D8). A confirmation that fails is not re-attempted, because the day is already
   reserved in `email_log` and a re-run is refused. Deliberate: a duplicate email is worse than a
   missing courtesy note.
3. **Employee email addresses are not validated at entry.** The admin UI accepts any string in the
   email field, so malformed addresses are only detected here, at send time (D5). Validating on input
   would be a better fix and is out of scope for this change.
4. **Volume.** Confirmations are sent one at a time on a single SMTP connection per message. At the
   current scale (5 employees) this is irrelevant. If the company grows to hundreds, batching or a
   reused connection would be worth revisiting.
