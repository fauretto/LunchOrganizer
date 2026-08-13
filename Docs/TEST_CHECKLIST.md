# LunchOrganizer — Manual test checklist

**Written 13 August 2026, for Massimo's own testing.**

This is everything that has **not** been exercised by a human. It excludes anything already proven by the automated suite or by direct verification — no point re-testing what 48 passing tests and live SQL already cover.

Two honest notes before you start:

- **No button in this application has ever been clicked by anyone.** Everything to date was verified by build, tests, SQL, HTTP, and log inspection. §A and §B below are therefore the highest-value tests in this document, not formalities.
- Where a test needs a specific data condition (a locked day, an employee with no history), the setup step is written out. Several of these are impossible with the seeded data as-is.

## How to run

```powershell
cd C:\Projects_Git\Data\GitPerso\LunchOrganizer
dotnet run --project src/LunchOrganizer.Web
```

Then open the address printed in the console. Admin login is `massimo` / `changeme`.

Useful throughout — see what the database really holds:

```powershell
$env:PGPASSWORD='P0stgr3sP0stgr3s'
& 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d lunchorganizer -c `
  "select b.booking_date, m.menu_number, e.full_name, b.price_snapshot from bookings b
   join employees e on e.id=b.employee_id join menus m on m.id=b.menu_id order by 1,2;"
```

---

## A. The two flows rewritten on 13 August — highest priority

These were changed today to use the backend's own contracts. They compile, they render, they are covered by no test.

### A1 · Whole-week apply, and its skip reasons

**Setup:** pick a week that contains at least one *past* day and, ideally, test after 09:00 so today is cut-off-locked. Choose a menu number that is **not** offered on every day of that week (check the Menus tab, or the SQL above).

- [ ] Enter your name, pick a menu number, press *Appliquer le menu N à tous les jours ouverts*.
- [ ] The **applied** list names exactly the days that were open.
- [ ] The **skipped** list names every other day, each with the right reason:
  - past day → *Passé* / *Past*
  - today after 09:00 → *Fermé à 09:00* / *Closed at 09:00*
  - day without that menu → *Pas de menu N ce jour-là* / *No Menu N that day*
- [ ] **Switch to EN and repeat.** Every skip reason must be in English — no French leaking through, no raw developer text like *"Day is in the past"*. Seeing that English sentence verbatim would mean the fallback path is being hit when it should not be.
- [ ] Save, then confirm with SQL that exactly the applied days were written.

### A2 · Delete versus deactivate

**Setup for the deactivate case:** any seeded employee — they all have exactly one booking.

- [ ] Admin → Employees → remove an employee **with** bookings.
- [ ] The dialog offers **Désactiver**, not delete, and states the correct booking **count**.
- [ ] Confirm → a *deactivated* toast appears, the row stays in the list marked inactive.
- [ ] That name no longer appears in the booking page's autocomplete.
- [ ] SQL confirms the row still exists with `is_active = false` and its booking intact.

**Setup for the delete case:** no seeded employee qualifies — **register a brand-new employee from the booking page** and do not book anything for them.

- [ ] Admin → Employees → remove that new employee.
- [ ] The dialog offers **Supprimer** and says nothing will be lost.
- [ ] Confirm → a *deleted* toast appears and the row disappears.
- [ ] SQL confirms the row is gone.

---

## B. V4 — first-time non-technical user walkthrough

The formal checkpoint from plan §7, governed by §6.6. Walk it as somebody who has never seen it.

### B1 · Booking page, French (default)

- [ ] The page opens in French, headed *"Semaine du lundi …"* — not a week number.
- [ ] **This week** / **Semaine prochaine** both work; no "previous" arrow on the current week.
- [ ] Autocomplete appears after 2 characters; arrow keys + Enter select.
- [ ] Accent-insensitive: typing `chloe` finds *Chloé Bernard*.
- [ ] An unknown name offers to register; similar existing names are shown first.
- [ ] Today's status banner is correct, and its remaining-time text is right.
- [ ] Locked cells are visibly disabled and explain why on hover.
- [ ] Each day column shows its own price.
- [ ] Selecting a second menu on the same day **replaces** the first.
- [ ] Clicking a selected cell clears that day.
- [ ] Save → toast, and the grid reloads showing persisted state.
- [ ] **Open question to decide while here:** the menu grid only appears once a name is entered. Should the week's menus be visible before identifying yourself?

### B2 · Bilingual

- [ ] **FR/EN** switch changes every visible string — headings, buttons, banners, dialogs, toasts, error messages.
- [ ] Switching language preserves the selected week and selection (re-entering the name is expected).
- [ ] Dates and prices stay Swiss in both: `dd.MM.yyyy`, `CHF 12.50`.

### B3 · Admin

- [ ] Wrong password is rejected; six rapid attempts trigger the rate limiter, then it recovers.
- [ ] **Menus & Prices:** add / edit / delete menus; *Copier cette description sur toute la semaine*; day price; *Appliquer ce prix à toute la semaine*.
- [ ] Deleting a menu that has bookings is **refused**, naming the count.
- [ ] **Price history:** book a day, change that day's price, then check the report — the existing booking must keep its original price. *(This is the single most important business rule in the app.)*
- [ ] **Report:** one employee and *All employees*; date range; period total is arithmetically right.
- [ ] **CSV export** opens in Excel with accents intact (é, not `Ã©`) and the total reconciles.

### B4 · Responsive, accessibility, print

- [ ] Below ~900 px the grid stacks into day-by-day; usable on a phone.
- [ ] Whole booking flow by keyboard only; focus rings always visible.
- [ ] Booking page and report both print cleanly, without navigation.

---

## C. End-to-end — V6

- [ ] Book several people across several days in the UI.
- [ ] Verify in SQL that the rows match exactly what you clicked.
- [ ] `dotnet run --project src/LunchOrganizer.Mailer -- --date <that date> --dry-run`
- [ ] Open the `.eml` — it lists **exactly** those people, grouped by menu, alphabetical, with a correct total. Compare name by name against the SQL, not just by eye.
- [ ] A date with no bookings exits `2` and writes no email.
- [ ] Optional: the in-app scheduler path, per `DEBUGGING.md` §6.1.

---

## D. Once SMTP details exist

Blocked until the relay host and recipient list are known. Nothing here needs a code change.

- [ ] Set `Mode: "Smtp"`, `SmtpHost`, and a **real** `Recipients` list in `config/email.json`.
- [ ] Send yourself one real summary first; check it renders correctly **in Outlook**, not just in a browser.
- [ ] Confirm the `email_log` row reads `Sent` with the recipient list recorded.
- [ ] Re-run the same date → exits `3`, sends nothing.
- [ ] Register the scheduled task and let it fire on its own at 09:01 the next working day.

---

## E. Deployment — never done on a real target machine

- [ ] `dotnet publish` both apps onto a machine that has never seen the project.
- [ ] **Delete `config/database.local.json` from the publish output before copying it anywhere** — the wildcard copy includes it, real password and all.
- [ ] Change `admin-users.json` away from `changeme`.
- [ ] Work through `INSTALLATION.md` §6 exactly as written and note anything that does not match reality.
- [ ] Run as a Windows Service or under IIS (§4.4) — written from the scripts, never actually performed.
- [ ] Register the mailer task with `Scripts/register-mailer-task.ps1` — same caveat.

---

## F. Known small gaps — confirm you can live with them

- [ ] **The cut-off countdown does not tick.** It is computed per page load, so it only refreshes when the page does. Decide whether that matters.
- [ ] **No toast on new-employee registration** — the panel disappearing is the only confirmation.
- [ ] **The `unaccent` fallback path is untested.** `unaccent` is installed here, so only the primary path runs. It would only matter on a server without the extension.
- [ ] **No machine-verified accessibility or contrast check.** AA was designed for, never measured.
- [ ] **Nine `MSB3277` EF-version warnings** on every build. Benign; suppressible if they annoy you.

---

## Reporting back

For anything that fails, the most useful things to capture are: what you clicked, what you expected, what happened, the language you were in, and — if the database is involved — the output of the SQL query above at that moment.
