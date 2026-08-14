# LunchOrganizer — Server Installation Guide (for Dummies)

A complete, beginner-friendly, step-by-step guide to installing LunchOrganizer on a
Windows server: the **website** (browsed by users) and the **daily email Mailer**.

---

## Important concept first (read this)

A published ASP.NET Core app is **not** like old static HTML that you drop into a web
root folder. It is a self-contained application that must be **hosted** by a process:

- **Option A (recommended): IIS** points at the app's folder and runs it continuously.
- **Option B: Windows Service** runs the app directly with its built-in Kestrel web
  server (no IIS).

Either way, **the app folder can live anywhere** (e.g. `C:\Apps\LunchOrganizer.Web`).
What matters is that a host process runs it. Do **not** put it in a drive root and expect
a browser to serve it.

---

## Overview: what you're installing

| App | What it is | How it runs |
|-----|-----------|-------------|
| **Web** | The website users open in a browser | IIS (Option A) or Windows Service (Option B) — runs continuously |
| **Mailer** | The daily summary email job | Launched once a day by **Task Scheduler** |

They share the same PostgreSQL database but are otherwise independent.

---

## PART 0 — Prerequisites (install on the server first)

1. **PostgreSQL** installed and reachable (on this server or another). You need: host,
   port, database username, and password.

2. **.NET 10 Hosting Bundle** (required for the website):
   - Download "**.NET 10.0 Hosting Bundle**" from the official .NET download page.
   - Run the installer. It installs the .NET runtime **and** the **ASP.NET Core Module
	 (ANCM)** that lets IIS run .NET apps.
   - After installing, **restart IIS** from an **elevated** Command Prompt:
	 ```
	 net stop was /y
	 net start w3svc
	 ```
   > The Web app uses `hostingModel="inprocess"` (see its `web.config`), which needs this
   > bundle. If you use the **Windows Service** option instead of IIS, you can install the
   > plain **ASP.NET Core Runtime** rather than the full Hosting Bundle — but the Hosting
   > Bundle works for both, so it is the safe choice.

3. **IIS** enabled (only needed for Option A). On Windows Server, add the
   **Web Server (IIS)** role; on Windows client, enable the "Internet Information
   Services" Windows feature.

---

## PART 1 — Copy the files to the server

On your build machine you published to:
```
D:\Data\GitPerso\LunchOrganizer\publish\Web\
D:\Data\GitPerso\LunchOrganizer\publish\Mailer\
```

Create two folders on the server and copy the **entire contents** of each publish folder
into them. This guide uses these paths (you may choose others):

```
C:\Apps\LunchOrganizer.Web       <- copy everything from publish\Web\ here
C:\Apps\LunchOrganizer.Mailer    <- copy everything from publish\Mailer\ here
```

After copying, `C:\Apps\LunchOrganizer.Web` should directly contain
`LunchOrganizer.Web.dll`, `web.config`, a `config\` subfolder, a `runtimes\` subfolder,
and many `.dll` files. **Do not** nest them inside an extra subfolder — `web.config` must
sit at the top level of the site folder.

---

## PART 2 — Configure the app settings for production

Edit the files in **`C:\Apps\LunchOrganizer.Web\config\`**:

**`database.json`** — point at your production PostgreSQL:
```json
{
  "Database": {
	"Host": "your-db-host",
	"Port": 5432,
	"Database": "lunchorganizer",
	"Username": "your-db-user",
	"Password": "your-db-password",
	"AutoCreateDatabase": true
  }
}
```
> For the **Web** app leave `AutoCreateDatabase: true` — on first start it creates the
> database and applies all EF Core migrations before serving requests.

**`email.json`** — set delivery mode and recipients (SMTP for real sending):
```json
{
  "Email": {
	"Mode": "Smtp",
	"SmtpHost": "your-smtp-relay",
	"SmtpPort": 587,
	"UseStartTls": true,
	"Recipients": [ "kitchen@yourcompany.com" ]
  }
}
```

**`admin-users.json`** — set the admin login accounts used at `/admin/login`.

Then apply the **same `database.json` and `email.json`** edits in
**`C:\Apps\LunchOrganizer.Mailer\config\`**, but for the Mailer set
`"AutoCreateDatabase": false` (it must never create schema).

> Tip: instead of editing the base files, you can create `database.local.json` /
> `email.local.json` next to them containing only the overrides. Both approaches work;
> editing the base files is simpler for a first deployment.

---

# OPTION A — Host the website in IIS (recommended)

## A1. Create an Application Pool
1. Open **IIS Manager** (`inetmgr`).
2. Right-click **Application Pools → Add Application Pool**.
3. Name: `LunchOrganizer`.
4. **.NET CLR version: "No Managed Code"** (ASP.NET Core runs out-of-CLR; IIS only forwards
   requests).
5. Click OK.

## A2. Create the Website
1. Right-click **Sites → Add Website**.
2. **Site name:** `LunchOrganizer`.
3. **Application pool:** select `LunchOrganizer`.
4. **Physical path:** `C:\Apps\LunchOrganizer.Web`.
5. **Binding:**
   - Type `http`, port `80` (or another), optional **Host name** like
	 `lunch.yourcompany.com`.
   - For HTTPS, add a second binding of type `https` on port `443` and select a TLS
	 certificate.
6. Click OK.

## A3. Grant folder permissions
The app pool identity (`IIS AppPool\LunchOrganizer`) needs access:
1. In File Explorer, right-click `C:\Apps\LunchOrganizer.Web` →
   **Properties → Security → Edit → Add**.
2. Add `IIS AppPool\LunchOrganizer`, grant **Read & execute** (and **Modify** if it must
   write logs).

## A4. HTTPS is enforced by the app
`Program.cs` calls `app.UseHttpsRedirection()`, so the app redirects HTTP → HTTPS:
- **Best:** configure an **https binding with a valid certificate** (company CA or Let's
  Encrypt). Users then browse `https://lunch.yourcompany.com`.
- For a quick internal test over HTTP only, the redirect may bounce browsers to `https://`
  and fail — so set up a certificate for the real hostname.

## A5. Start and test
1. In IIS Manager select the site → **Browse**, or open a browser to your URL.
2. The first request connects to PostgreSQL, **creates the schema and runs migrations**,
   then serves the page (first hit may take a few seconds).
3. Admin panel: `/admin/login`, sign in with an account from `admin-users.json`.

### Troubleshooting (IIS)
- Startup errors go to **Event Viewer → Windows Logs → Application**.
- For detailed logs: in `web.config` set `stdoutLogEnabled="true"`, create a `logs`
  subfolder, reproduce, then read `logs\stdout*.log`. Turn logging back off afterward.
- Common causes: wrong DB settings in `config\database.json`; Hosting Bundle not installed;
  IIS not restarted after installing it.

---

# OPTION B — Run the website as a Windows Service (Kestrel, no IIS)

Use this if you prefer not to install/manage IIS. The app runs itself using its built-in
Kestrel web server, and Windows keeps it alive as a service.

> Note: the published app already supports running as a service via the .NET generic host.
> You do not need IIS or the ASP.NET Core Module for this path — the plain **ASP.NET Core
> Runtime for .NET 10** is enough (the Hosting Bundle also works).

## B1. Decide the listening URL/port
Kestrel needs to know which address/port to listen on. The easiest way is an environment
variable set for the service. For example, to listen on port 8080 (HTTP):
```
ASPNETCORE_URLS = http://0.0.0.0:8080
```
For HTTPS with a certificate, you can instead bind HTTPS and provide a certificate via
`ASPNETCORE_URLS=https://0.0.0.0:8443` plus Kestrel certificate settings. For a simple
internal deployment, HTTP on a chosen port is the least fiddly starting point.

> Reminder: the app calls `UseHttpsRedirection()`. If you serve **HTTP only** behind a
> reverse proxy or on a trusted internal network, that redirect can interfere. Options:
> terminate TLS at a front-end proxy, serve HTTPS directly from Kestrel with a real
> certificate, or place the service behind IIS/another proxy. For a pure internal HTTP
> test, be aware the HTTPS redirect exists.

## B2. Create the Windows Service
Open an **elevated** Command Prompt (or PowerShell) and create a service that runs the
app's DLL via `dotnet`. Use `sc.exe`:

```
sc.exe create LunchOrganizerWeb binPath= "\"C:\Program Files\dotnet\dotnet.exe\" \"C:\Apps\LunchOrganizer.Web\LunchOrganizer.Web.dll\"" start= auto DisplayName= "LunchOrganizer Web"
```

Notes on the exact syntax (important for `sc.exe`):
- There **must** be a space after each `=` (e.g. `start= auto`, not `start=auto`).
- The inner quotes around the two paths are escaped with `\"` so the whole `binPath` is one
  quoted string.
- If you published **self-contained** (`-SelfContained`), point `binPath` directly at the
  EXE instead:
  ```
  sc.exe create LunchOrganizerWeb binPath= "\"C:\Apps\LunchOrganizer.Web\LunchOrganizer.Web.exe\"" start= auto DisplayName= "LunchOrganizer Web"
  ```

Set a description (optional):
```
sc.exe description LunchOrganizerWeb "LunchOrganizer Blazor Server website (Kestrel)"
```

## B3. Set the environment (listening URL and production mode)
Set the URL and environment for the service. The simplest robust way is to set them as
**machine-level** environment variables, then have the service inherit them, OR set them on
the service key. A straightforward approach with PowerShell (machine scope):
```powershell
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', 'http://0.0.0.0:8080', 'Machine')
[Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Machine')
```
Then restart the service (below) so it picks them up. (Machine-scope variables apply to all
services/processes; if you need per-service isolation, set the multi-string
`Environment` value on
`HKLM\SYSTEM\CurrentControlSet\Services\LunchOrganizerWeb` instead.)

## B4. Grant the service account access
By default the service runs as **LocalSystem**. That can read `C:\Apps\...` fine. For a
tighter setup, create a dedicated service account and:
- Grant it **Read & execute** on `C:\Apps\LunchOrganizer.Web`.
- Configure the service to log on as that account (Services console → the service →
  Properties → Log On).

## B5. Start and test the service
```powershell
Start-Service LunchOrganizerWeb
Get-Service LunchOrganizerWeb
```
Then browse to:
```
http://<server-name-or-ip>:8080
```
(or your chosen port). On first request the app creates/migrates the database, then serves
pages. `/admin/login` uses the accounts from `admin-users.json`.

### Managing the service
```powershell
Stop-Service LunchOrganizerWeb
Start-Service LunchOrganizerWeb
# Remove it (if needed):
sc.exe delete LunchOrganizerWeb
```

### Troubleshooting (Windows Service)
- If the service starts then immediately stops, the app likely threw at startup (bad DB
  settings, port already in use, missing runtime).
- Check **Event Viewer → Windows Logs → Application** for the .NET error.
- Verify the port is free: `netstat -ano | findstr :8080`.
- Confirm the runtime is installed: `dotnet --info`.
- Make sure `ASPNETCORE_URLS` is actually visible to the service (restart the service
  after setting machine-level variables).

### Firewall
If users connect from other machines, open the chosen port in Windows Firewall:
```powershell
New-NetFirewallRule -DisplayName "LunchOrganizer Web 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

---

## PART 5 — Set up the daily Mailer (Task Scheduler) — both options

The website does **not** send the daily email in production — the Mailer does, launched by
Windows Task Scheduler. This is identical whether you chose IIS or the Windows Service.

1. Test the Mailer manually from an **elevated** PowerShell on the server:
   ```powershell
   & 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe' --dry-run
   $LASTEXITCODE   # 0=Sent, 1=Failed, 2=Skipped (no bookings), 3=AlreadyHandled
   ```
   This verifies DB connectivity and config without sending anything.

2. Register the scheduled task (elevated PowerShell):
   ```powershell
   .\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe' -TimeLocal '09:01'
   ```
   Creates a task that runs the exe **weekdays at 09:01 local time**.
   > If Group Policy blocks unsigned `.ps1` scripts, either sign the script or run the
   > equivalent `Register-ScheduledTask` / `schtasks` command directly.

3. Test the task immediately without waiting:
   ```powershell
   Start-ScheduledTask -TaskName 'LunchOrganizer Daily Summary Mailer'
   ```

---

## PART 6 — Final checklist

- [ ] PostgreSQL reachable; .NET 10 runtime/Hosting Bundle installed.
- [ ] `C:\Apps\LunchOrganizer.Web` and `C:\Apps\LunchOrganizer.Mailer` populated with the
	  publish contents.
- [ ] `config\database.json` points to production DB (Web: `AutoCreateDatabase=true`,
	  Mailer: `false`).
- [ ] `config\email.json` has real `Mode`/`Recipients`/SMTP.
- [ ] `admin-users.json` has real admin credentials.
- [ ] **IIS path:** App Pool = "No Managed Code"; site physical path = the Web folder;
	  folder permissions granted; HTTPS binding with a valid certificate.
- [ ] **Windows Service path:** service created; `ASPNETCORE_URLS` set; service starts;
	  firewall port opened if needed.
- [ ] Website loads in a browser; `/admin/login` works.
- [ ] Mailer `--dry-run` returns a sensible exit code; scheduled task registered.

---

## Quick answers to common questions

- **"Does the Web content go in a server root folder?"** No. Put it in its **own folder**
  (e.g. `C:\Apps\LunchOrganizer.Web`) and either point an **IIS website** at that folder or
  run it as a **Windows Service**. The folder location itself is arbitrary; a host process
  is what makes it reachable from a browser.
- **"Where does the Mailer folder go?"** Anywhere (e.g. `C:\Apps\LunchOrganizer.Mailer`).
  It is never browsed to — Task Scheduler just launches its exe once a day.
- **"IIS or Windows Service?"** IIS is the most common and gives easy HTTPS, host headers,
  and process management. The Windows Service (Kestrel) avoids installing IIS and is
  simpler for a small internal deployment, but you manage the URL/port/cert yourself.

---

## Reference: relevant app facts (from the codebase)

- Hosting model in `publish\Web\web.config`: `inprocess` via `AspNetCoreModuleV2` (IIS
  path).
- `Program.cs` forces HTTPS (`UseHttpsRedirection`) and bootstraps the database on startup
  (creates/migrates before serving).
- Default app settings (`config/app.json`): currency `CHF`, default culture `fr-CH`,
  booking cut-off `09:00`.
- Admin cookie auth; login at `/admin/login`; accounts from `config/admin-users.json`.
- Mailer exit codes: `0` Sent, `1` Failed, `2` Skipped (no bookings), `3` AlreadyHandled.
