# LunchOrganizer — Installation Guide

How to install LunchOrganizer on a machine that has never seen it, and how to configure it.

Written for whoever installs the application — not necessarily a developer. Every configuration file is plain JSON, editable in Notepad++.

Once it is installed, hand the people who will use it [`USER_GUIDE.fr.md`](USER_GUIDE.fr.md) — or [`USER_GUIDE.md`](USER_GUIDE.md) for the English version.

> **Status note.** `dotnet publish` was executed and its output inspected on 12 August 2026, so §4 is verified. Hosting as a Windows Service or under IIS (§4.4) and the Task Scheduler registration (§7) are written from the scripts and their documented behaviour but have **not** been performed end-to-end on a target machine. The daily email **is** connected to the real database as of 13 August 2026; the one thing still missing before §7 is production-ready is the SMTP configuration — see §9.

---

## 1. What the target machine needs

| Requirement | Detail |
|---|---|
| **Operating system** | Windows 10/11 or Windows Server 2019+. The application itself is cross-platform; only the Task Scheduler script and the PowerShell helpers are Windows-specific. |
| **.NET** | **ASP.NET Core Runtime 10.0** (LTS). The full SDK is only needed if you build on the target machine. Download: <https://dotnet.microsoft.com/download/dotnet/10.0> — pick *ASP.NET Core Runtime*, **Hosting Bundle** if you intend to use IIS. |
| **PostgreSQL** | **Version 16 or later**, reachable from the application. It may be on the same machine or elsewhere. |
| **PostgreSQL extensions** | `citext` (**required**) and `unaccent` (optional, improves name search). Both ship with the standard PostgreSQL installer; the application installs them itself if its role is allowed to. |
| **Database privileges** | A login role. It needs `CREATEDB` **only** if you use automatic creation (§3.1); the offline route (§3.2) avoids that. |
| **Network** | The web port must be reachable by users (default `5000`/`5001` for a published app, or whatever you configure). Outbound access to the SMTP relay if email is enabled. |
| **Account for the scheduled email** | A Windows account able to run a scheduled task. `SYSTEM` is the default and is usually sufficient. |
| **Disk** | ~200 MB for the published application; the database grows by roughly one small row per lunch booked. |

Verify .NET after installing:

```powershell
dotnet --list-runtimes
```

You need a line beginning `Microsoft.AspNetCore.App 10.0.`. `Microsoft.NETCore.App` alone is **not** enough — the web application will not start.

---

## 2. Getting the application onto the machine

Either build it yourself:

```powershell
git clone <repository-url> LunchOrganizer
cd LunchOrganizer
dotnet publish src/LunchOrganizer.Web    -c Release -o C:\Apps\LunchOrganizer\web
dotnet publish src/LunchOrganizer.Mailer -c Release -o C:\Apps\LunchOrganizer\mailer
```

or copy a published folder produced elsewhere.

> ⚠️ **Before copying a published folder, delete every `config\*.local.json` file inside it.**
> The project deliberately copies `config/*.json` next to the executable, and the wildcard picks up the local override files too. Those are git-ignored precisely because they hold **real passwords**, and a publish made on a developer machine will contain that developer's database password. Check `C:\Apps\LunchOrganizer\web\config\` and delete any `*.local.json` you did not intend to ship.

---

## 3. Setting up the database

Pick **one** of the two routes.

### 3.1 Automatic (simplest)

The application creates the database, applies the schema and installs the extensions the first time it runs. It needs a role with **`CREATEDB`**.

Set `"AutoCreateDatabase": true` in `config\database.json` (the default) and start the application. On success the log reads:

```
Database bootstrap complete: database created, 1 migration(s) applied.
```

Two instances starting simultaneously are safe: creation tolerates "database already exists", and migrations run inside a PostgreSQL advisory lock.

### 3.2 Offline script (for a locked-down server)

Use this when a DBA owns the server and the application's role may not create databases or run DDL.

```powershell
.\Scripts\create_database.ps1
```

It locates `psql.exe` automatically (PATH, then `D:\PostgreSQL\bin`, then `C:\Program Files\PostgreSQL\<version>\bin`, then the Windows service registration; it prompts if all of those fail), then asks for host, port, username, database name and password. The password is entered masked and is never written to disk.

It creates the database if absent and applies `Scripts\create_database.sql`, which is generated from the application's own migration — do not hand-edit it. Both are idempotent and safe to re-run.

Afterwards, grant the application's runtime role access to that database and set `"AutoCreateDatabase": false` in `config\database.json`.

---

## 4. Running the web application

### 4.1 Console (simplest, good for a first check)

```powershell
cd C:\Apps\LunchOrganizer\web
.\LunchOrganizer.Web.exe
```

It prints the URL it is listening on. Closing the window stops the site — fine for testing, not for production.

### 4.2 Choosing the address and port

```powershell
$env:ASPNETCORE_URLS = 'http://0.0.0.0:8080'
.\LunchOrganizer.Web.exe
```

`0.0.0.0` accepts connections from other machines; `localhost` accepts only local ones. Open the port in Windows Firewall if users connect from elsewhere.

### 4.3 HTTPS

Recommended, since admin passwords are submitted through a login form. Either put the site behind IIS or a reverse proxy holding the certificate (usual choice in a company network), or configure Kestrel directly with a certificate.

### 4.4 Running permanently

Two options:

**Windows Service** — using `sc.exe` or NSSM, pointing at `LunchOrganizer.Web.exe` with the working directory set to its folder.

**IIS** — install the **.NET Hosting Bundle**, create a site pointing at the published folder, and set its application pool to *No Managed Code*. The ASP.NET Core Module starts the application. Give the application pool identity read access to the folder and to `config\`.

Either way the site restarts automatically after a reboot, which the console option does not.

---

## 5. Configuration files

All live in the `config\` folder **next to the executable**. Plain JSON; edit in Notepad++. The web application reloads them while running — no restart needed for most settings.

Any file may be shadowed by a `*.local.json` twin (`database.local.json`, `email.local.json`, …) which overrides individual values and is git-ignored. On a server, it is simpler to edit the base files directly.

> **These files contain passwords in clear text.** That is a deliberate, accepted trade-off for editability. Protect them with **NTFS permissions** — read access limited to the account running the application and to administrators. Do not place the application in a shared folder that everyone can browse.

### 5.1 `database.json` — connection

| Setting | Meaning | Change on a new machine? |
|---|---|---|
| `Host` | PostgreSQL server. `localhost` if on the same machine. | **Yes, if remote** |
| `Port` | Default `5432`. | Rarely |
| `Database` | Database name. Default `lunchorganizer`. | Rarely |
| `Username` | Login role. | **Yes** |
| `Password` | Its password. Ships as `changeme`. | **Yes — always** |
| `MaintenanceDatabase` | Database connected to in order to create the main one. Leave `postgres`. | No |
| `AutoCreateDatabase` | `true` = create on first run (needs `CREATEDB`); `false` = expect it to exist (§3.2). | Depends on §3 |
| `Seed` | `true` inserts demo employees and menus. **Leave `false` in production.** | No |
| `MaxPoolSize` | Maximum simultaneous connections. `50` suits a few hundred users. | Rarely |
| `CommandTimeoutSeconds` | Query timeout. | Rarely |

### 5.2 `app.json` — behaviour and appearance

| Setting | Meaning | Change on a new machine? |
|---|---|---|
| `DefaultLunchPrice` | Price used for a day with no explicit price. Decimal point, e.g. `12.50`. | **Probably** |
| `Currency` | `CHF`. | If not Switzerland |
| `DefaultCulture` | Language on first visit: `fr-CH` or `en-CH`. | Optional |
| `SupportedCultures` | Languages offered by the `FR · EN` switch. | Rarely |
| `BookingCutOffLocalTime` | After this **server local time**, today's lunch can no longer be booked or changed. `09:00:00`. | If the kitchen's deadline differs |
| `AutocompleteMinChars` | Characters typed before name suggestions appear. | Rarely |
| `AutocompleteMaxResults` | Maximum suggestions shown. | Rarely |
| `MaxMenusPerDay` | Upper limit on menus for one day. | Rarely |

The cut-off uses the **server's clock and time zone**. If the server sits in another zone than the office, set the server's time zone correctly — do not compensate by shifting this value.

### 5.3 `admin-users.json` — who may open the admin page

```json
{
  "Users": [
    { "Username": "massimo", "DisplayName": "Massimo Fauro", "Password": "changeme" }
  ]
}
```

> **Mandatory first step on any new installation: change this.** It ships with `massimo` / `changeme`, and anyone who reaches the site could otherwise open the admin page, edit menus and read who ate what.

A password may be written in plain text, or hashed as `"sha256:<hex>"` — both are accepted, so an account can be hardened without changing any code. Produce a hash with:

```powershell
$bytes = [System.Text.Encoding]::UTF8.GetBytes('your-new-password')
'sha256:' + [BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($bytes)).Replace('-','').ToLower()
```

Add as many accounts as you need. **Editing this file takes effect immediately, including revoking anyone currently signed in** — removing a user ends their session rather than merely blocking their next login.

The booking page itself needs no login, by design: people identify themselves by typing their name, exactly as they used to write it on the paper sheet.

### 5.4 `email.json` — the daily summary

| Setting | Meaning | Change on a new machine? |
|---|---|---|
| `Mode` | `PickupDirectory` writes `.eml` files and sends nothing (safe for testing). `Smtp` really sends. | **Yes, to `Smtp`** |
| `SmtpHost` | Mail server. | **Yes** |
| `SmtpPort` | `25` for an internal relay, `587` for authenticated submission. | **Yes** |
| `UseStartTls` | `true` for port 587. | **Yes** |
| `Username` / `Password` | Leave empty for an anonymous internal relay. | Depends |
| `SenderName` / `SenderAddress` | The From shown to recipients. | **Yes** |
| `Recipients` | Who receives the summary — a JSON list, e.g. `["cantine@cohu.com"]`. **Ships empty.** | **Yes — always** |
| `SubjectPrefix` | `"COHU booked lunch for "`. The date is appended. | Rarely |
| `SubjectDateFormat` | `dd.MM.yyyy`. | Rarely |
| `Language` | `fr` or `en` — the language of the email body, independent of the website's switch, because the recipient is the kitchen. | Optional |
| `SendTimeLocal` | `09:01`. One minute after the cut-off so that a booking committing at 08:59:59 cannot be missed. | Rarely |
| `WorkingDays` | Monday–Friday. | If the canteen opens other days |
| `SkipWhenNoBookings` | `true` = send nothing on an empty day. | No |
| `PickupDirectory` | Where `.eml` files go in `PickupDirectory` mode. Relative to the executable. | No |
| `EnableInAppScheduler` | `true` makes the **website** send the email — only if it is running at that moment. Leave `false` and use the scheduled task (§7). | No |

**Test with `PickupDirectory` before switching to `Smtp`.** You will see exactly what recipients would receive, without the risk of sending a wrong or empty summary to the whole canteen.

---

## 6. First-run checklist

1. `dotnet --list-runtimes` lists `Microsoft.AspNetCore.App 10.0.*`.
2. PostgreSQL is reachable and the database exists, or the role can create it.
3. `config\database.json` has real credentials; **no leftover `*.local.json`** from a developer machine.
4. `config\admin-users.json` no longer contains `changeme`.
5. `config\email.json` has a real `Recipients` list.
6. Start the application; the log shows the bootstrap line and no error.
7. Open the site — the booking page appears in French, headed *"Semaine du lundi …"*.
8. Type a name, choose a menu, save. Confirm it persisted:
   ```powershell
   & 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -h localhost -U postgres -d lunchorganizer -c "select * from bookings;"
   ```
9. Open `/admin`, sign in with the new password, check the three tabs.
10. Switch to **EN** and back; the page must keep your week and your selection.
11. Run the mailer once with `--dry-run` and open the resulting `.eml`. It reads the real database, so the names in it must match the bookings you just made — **but see §9 about `Recipients` before doing a non-dry run**.

---

## 7. The daily email as a scheduled task

Runs the mailer every weekday, independently of whether the website happens to be running.

```powershell
.\Scripts\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer\mailer\LunchOrganizer.Mailer.exe'
```

| Parameter | Default | Purpose |
|---|---|---|
| `-ExecutablePath` | *(required)* | Full path to the published `LunchOrganizer.Mailer.exe`. |
| `-TaskName` | `LunchOrganizer Daily Summary Mailer` | Name in Task Scheduler. |
| `-TimeLocal` | `09:01` | Local time, weekdays. Keep it consistent with `SendTimeLocal`. |
| `-UserName` / `-Password` | *(SYSTEM)* | Run under a specific account instead. |
| `-WorkingDirectory` | folder of the executable | Must be where `config\` lives. |

The script is idempotent — running it again updates the existing task rather than creating a duplicate.

**Verify:** open Task Scheduler, find the task, use *Run* and check *Last Run Result*.

**Test by hand first:**

```powershell
cd C:\Apps\LunchOrganizer\mailer
.\LunchOrganizer.Mailer.exe --date 2026-08-17 --dry-run
```

| Exit code | Meaning |
|---|---|
| `0` | Sent |
| `1` | Failed — check the console output |
| `2` | Skipped — nobody booked that day |
| `3` | Already handled — another run had the day |

`3` is not an error. It is the guard that makes a double send impossible.

**Remove the task:**

```powershell
Unregister-ScheduledTask -TaskName 'LunchOrganizer Daily Summary Mailer' -Confirm:$false
```

---

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| *"The application to execute does not exist"* or an immediate exit | ASP.NET Core Runtime 10 missing | Install the runtime (§1); `Microsoft.NETCore.App` alone is insufficient |
| `FileNotFoundException: app.json` | `config\` not next to the executable | Copy the whole `config\` folder into the application folder |
| `password authentication failed` | Wrong credentials, or a stale `database.local.json` overriding them | Fix `database.json`; delete leftover `*.local.json` |
| `permission denied to create database` | Role lacks `CREATEDB` | Grant it, or use §3.2 and set `AutoCreateDatabase: false` |
| `Npgsql.NpgsqlException: No connection could be made` | PostgreSQL stopped, wrong host/port, or firewall | Check the service and `pg_hba.conf` |
| `extension "citext" is not available` | `citext` not installed on the server | Install the PostgreSQL contrib package |
| `Address already in use` | Port taken | Change `ASPNETCORE_URLS`, or free the port |
| Site loads but nothing is clickable | Missing `_framework/blazor.web.js`, or a proxy blocking WebSockets | Allow WebSocket upgrades through the reverse proxy |
| Everyone is locked out of today's booking | Server clock or time zone wrong | Fix the server's time zone; do not compensate via `BookingCutOffLocalTime` |
| No email arrives | `Recipients` empty, still in `PickupDirectory` mode, task not firing, or nobody booked (exit `2`) | Check in that order |
| Email sent twice | Both `EnableInAppScheduler` and the scheduled task are active | Set `EnableInAppScheduler: false`. The `email_log` guard should prevent it regardless |
| Accented names not found in search | `unaccent` unavailable | Install it, or accept exact-accent matching |

---

## 9. Before this is production-ready

One thing must be finished first. It is recorded in [`DEBUGGING.md`](DEBUGGING.md) §10.

1. **No SMTP details are configured.** `config/email.json` has `Mode: "PickupDirectory"` and an empty `Recipients` list, so the daily email is written as an `.eml` file rather than sent. Set `Mode: "Smtp"`, the SMTP host, and at least one recipient before scheduling it for real use. This is a configuration change only — no rebuild.

Both the **website and the mailer are fully connected** to the real database and read the same `config/*.json` + `*.local.json` files. (The two mailer gaps listed here on 12 August 2026 — in-memory demo data, and `*.local.json` overrides not being read — were closed on 13 August 2026.)

---

## 10. Upgrading, backing up, uninstalling

**Upgrade**

1. Stop the site (and disable the scheduled task).
2. **Back up the database first** (below).
3. Replace the application files, **keeping your `config\` folder** — or copy your settings across.
4. Start the site. Any new schema migration applies automatically on startup.

**Back up**

```powershell
& 'C:\Program Files\PostgreSQL\16\bin\pg_dump.exe' -h localhost -U postgres -d lunchorganizer -F c -f C:\Backups\lunchorganizer_2026-08-12.dump
```

Restore with `pg_restore`. Worth scheduling: the booking history is also the billing record.

**Uninstall**

1. `Unregister-ScheduledTask -TaskName 'LunchOrganizer Daily Summary Mailer' -Confirm:$false`
2. Stop and remove the site (service or IIS entry).
3. Delete the application folder.
4. Only once you are certain the history is no longer needed — and after a final backup:
   ```sql
   DROP DATABASE lunchorganizer;
   ```
