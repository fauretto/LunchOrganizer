# LunchOrganizer — User guide

This guide is for the people who use LunchOrganizer every day. It assumes no technical knowledge.

The application opens in French by default. Every button named below is given as **French *(English)*** so you can follow along whichever language you are using — the **FR / EN** switch in the top-right corner changes it at any time, and your choice is remembered on that computer.

- **Part 1 — Booking your lunch** is for everyone.
- **Part 2 — Administration** is for whoever manages menus, prices and employees.

> Installing or moving the application is covered in [`INSTALLATION.md`](INSTALLATION.md), not here.
>
> *Une version française de ce guide est disponible : [`USER_GUIDE.fr.md`](USER_GUIDE.fr.md).*

---

## The one rule that matters

> ### Lunch for today can be booked or changed until **09:00**.
>
> After 09:00 today's box locks and cannot be changed — the kitchen has already received the list. Tomorrow and the rest of the week stay open as normal.

Everything else in this guide is detail. This is the part worth remembering.

---

# Part 1 — Booking your lunch

## Opening the application

Type the address your administrator gave you into any browser. You do **not** need a password and you do **not** need to install anything. A phone or tablet works — below a certain width the weekly grid stacks into one day after another.

## Step 1 — Pick the week

Two large buttons sit at the top:

| Button | Shows |
|---|---|
| **Cette semaine** *(This week)* | The current Monday–Friday |
| **Semaine prochaine** *(Next week)* | The following Monday–Friday |

The heading spells the week out in full — *"Semaine du lundi 17 août"* — rather than a week number. A small arrow beside the buttons reaches further weeks. On the current week there is no "previous" arrow, because past weeks cannot be booked.

Only Monday to Friday appear. There are no weekend lunches.

## Step 2 — Type your name

In **Votre nom** *(Your name)*, start typing. After **two letters** a list of matching names appears; use the mouse or the arrow keys and Enter to pick yours.

Accents do not matter — typing `chloe` finds *Chloé Bernard*, and capitals are ignored too.

**If your name is not in the list**, the application offers *"Register «your name» as a new employee"*. Confirm once and you are added — no administrator needed. If similar names already exist they are shown first, so please check before confirming: **Jean Dupont** and **Jean Dupond** are two different people, and the kitchen will cook for both.

## Step 3 — Read today's status

As soon as the application knows who you are, a banner summarises today, for example:

> **Today (Mon 17 Aug): Menu 2 booked** · editable for another 1 h 12 min

or

> **No lunch booked for today** · you can still book for another 1 h 12 min

Once 09:00 has passed the banner says so instead, and today's column is locked.

## Step 4 — Choose your menus

The grid is the paper sheet, on screen: **one column per day, one row per menu.** Each day shows its own menus, its own descriptions, and its own price — Monday's *Menu 2* and Tuesday's *Menu 2* are different meals.

- Click a box to book that menu on that day.
- Click your selected box again to remove that day's lunch.
- **One lunch per person per day.** Choosing a second menu on the same day replaces the first rather than adding to it.
- Greyed-out boxes cannot be used. Hover over one and it explains why — the day is in the past, or 09:00 has passed.
- A day with no menus published yet simply shows nothing to choose.

### Booking the whole week at once

Below the grid, pick a menu number and press **Appliquer le menu N à tous les jours ouverts** *(Apply Menu N to every open day)*. It fills every day that is still open and then tells you exactly what it did — which days it applied, and which it skipped and why (already past, past the cut-off, or that menu not offered that day). Nothing is hidden.

## Step 5 — Save

Nothing is booked until you press **Réserver mes repas** *(Book my lunches)*. One press saves the whole week.

A green confirmation appears, and the grid reloads from the database — so what you see afterwards is always what is really stored, never just what you clicked.

## Changing or cancelling

Come back at any time, select your name, and change the boxes as before. The rules are the same: today until 09:00, later days freely, past days never.

**To cancel a lunch**, click the selected box to clear it, then save. Not saving changes nothing.

## Printing

The booking page and the administration report both print cleanly — use your browser's normal print command. Navigation and buttons are removed automatically, and the heading carries the week and the date it was printed.

---

# Part 2 — Administration

## Signing in

Open **Administration** in the top navigation. Administrator accounts are held in a small file (`config/admin-users.json`) that an administrator can edit in Notepad++; there is no account-creation screen. Sessions last 8 hours.

After five failed attempts in a minute the login pauses briefly. Wait a moment and try again.

The administration area has three tabs.

## Menus & Prices tab

This is the tab used most often, usually once a week.

**Menus.** Pick a week, then for each day add menus with **Ajouter un menu** *(Add a menu)* and write a description — *"Poulet au curry, riz basmati"*. Menus are numbered automatically from 1. A menu belongs to **one specific date**: setting up Monday says nothing about Tuesday. **Copier cette description sur toute la semaine** *(Copy this description across the whole week)* saves retyping when the same dish runs several days. Menus can also be imported in bulk from a Word document with **Importer des menus** *(Import menus)*; the newer monthly layout — day cells with no header row, each stating its own day and year — is supported, but the import is refused if the year written in the document doesn't match the year selected in the import dialog.

**Prices.** Each day has one price shared by all of that day's menus. Set it per day, or use **Appliquer ce prix à toute la semaine** *(Apply this price to the whole week)*. New days start from the default price in `config/app.json` (currently **CHF 12.50**). A menu can also have its own price, set in a small field next to its description — leave it blank to use the day's price instead — and this price can also arrive automatically from an imported document when the document states one.

> **Changing a price never rewrites the past.** Each booking stores the price at the moment it was made. If Tuesday's price changes after ten people have booked, those ten keep the price they booked at and the report stays correct. Prices only ever apply going forward.

**Deleting a menu** is refused while anyone has booked it — you will be told how many bookings exist. Remove or move those bookings first. This is deliberate: it makes it impossible to leave someone holding a lunch that no longer exists.

## Employees tab

A searchable list of everyone registered. You can add people manually, and edit a name or e-mail address inline.

**Delete versus deactivate.** Someone who has never booked can be deleted outright. Someone with booking history **cannot** — deleting them would corrupt past reports. The application offers **Désactiver** *(Deactivate)* instead: they stop appearing in the booking autocomplete, while their history stays intact for reporting. This is the right choice for someone who has left the company.

## Report tab

Choose one employee or *Tous les employés* *(All employees)*, set a start and end date, and the table lists every booking — **Date · Day · Menu · Description · Price** — with the **period total** in a summary card. This is what you use to bill or reconcile.

**Exporter en CSV** *(Export to CSV)* downloads the same table for Excel. The file is UTF-8 with a byte-order mark, so accented names open correctly in Excel without any import step.

---

# The daily e-mail

Every working day at **09:01**, one minute after the cut-off, LunchOrganizer sends the kitchen a summary of that day's bookings.

The e-mail is grouped by menu, each with its description and the alphabetical list of people who chose it, and a total at the top. Menus nobody chose are left out. It is sent in French by default, independently of whichever language you personally use on the website.

**If nobody booked, nothing is sent.** The kitchen never receives an empty list.

A few things worth knowing:

- The e-mail is sent by a small scheduled program, **not** by the website. It still goes out if the website has been restarted or is not running.
- The summary **can never be sent twice** for the same day, even if two copies of the scheduler run at once. The first one to claim the day wins.
- Sending at 09:01 rather than 09:00 is intentional: it guarantees a booking saved at 08:59:59 is in the e-mail.

---

# Common questions

**I booked but I'm not in the e-mail.**
Almost always the booking was saved after 09:00, so it counted for a later day rather than today. Open the week and check which box is selected.

**Someone registered my name twice, slightly differently.**
An administrator can deactivate the wrong one on the Employees tab. The application already blocks exact duplicates — including differences of capitals or accents, so `ALICE MARTIN` cannot be added next to `Alice Martin` — but it cannot know that *Jean Dupond* was meant to be *Jean Dupont*.

**I forgot to book and it's 09:30.**
The application cannot help — the kitchen list has gone. Speak to them directly.

**Can I book for a colleague?**
Yes. Type their name instead of yours. There is no password on the booking page. It relies on trust, exactly like the paper sheet it replaces.

**Can I book several weeks ahead?**
Yes, as far ahead as menus have been published. Days with no menus yet cannot be booked.

**The page says it is reconnecting.**
The website keeps a live connection to the server. If the network hiccups it reconnects on its own and offers a retry button if it cannot. Your saved bookings are never affected — only unsaved clicks would be lost.

**I changed the language and my bookings vanished.**
They have not. Switching language reloads the page; re-enter your name and the grid returns exactly as it was.

---

*Questions this guide does not answer are probably in [`DEBUGGING.md`](DEBUGGING.md) (running the application) or [`INSTALLATION.md`](INSTALLATION.md) (installing and configuring it).*
