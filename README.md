# LunchOrganizer

A Blazor Server (.NET 10) application for organizing daily lunch bookings, with an admin
panel and an automated daily summary email to the kitchen/caterer.

## Solution layout

- `src/LunchOrganizer.Web` — Blazor Server website (booking UI + admin panel)
- `src/LunchOrganizer.Mailer` — console app that sends the daily summary email
- `src/LunchOrganizer.Domain` — domain models and configuration options
- `src/LunchOrganizer.Data` — EF Core + PostgreSQL repositories and migrations
- `src/LunchOrganizer.Services` — application services
- `src/LunchOrganizer.Email` — email building, rendering, sending, and in-app scheduler
- `tests/LunchOrganizer.Tests` — xUnit test suite
- `config/` — hand-editable JSON settings (copied next to each build output)
- `Scripts/` — publishing and Task Scheduler helper scripts

## Configuration

Settings live under `config/` as base files (`app.json`, `database.json`, `email.json`,
`admin-users.json`) with optional git-ignored `*.local.json` overrides that win on matching
keys. At runtime each app reads the `config/` folder next to its own executable.

## Build & test

```powershell
dotnet build
dotnet test
```

## Deployment

To publish and install LunchOrganizer on a server:

1. **Publish** both apps (Release) with the self-contained batch script:
   ```
   Scripts\publish-production.cmd -Clean
   ```
   Output lands in `publish\Web\` and `publish\Mailer\` at the repo root.

2. **Follow the installation guide:** [`Docs/INSTALLATIONFORDUMMIES.md`](Docs/INSTALLATIONFORDUMMIES.md)
   — a step-by-step, beginner-friendly guide covering prerequisites, copying files,
   configuring `config/*.json`, hosting the website (**IIS** or **Windows Service /
   Kestrel**), and registering the daily Mailer with Windows Task Scheduler.

3. **Register the daily email job** on the server. Use the `.cmd` wrapper — it self-elevates, and
   on machines where Group Policy blocks `.ps1` execution it is the only thing that works:
   ```
   Scripts\register-mailer-task.cmd -ExecutablePath "C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe" -TimeLocal "09:01"
   ```
   Add `-UserName <domain\user> -Password <password>` if the database is only reachable by a
   specific account — the task otherwise runs as `SYSTEM`.

### Step-by-step procedures

| Task | Procedure |
|---|---|
| Release a new version to the **production server** | [`Docs/PUBLISH_TO_PRODUCTION_SERVER.md`](Docs/PUBLISH_TO_PRODUCTION_SERVER.md) |
| Run the site on **this laptop** under IIS | [`Docs/RUN_LOCALLY_UNDER_IIS.md`](Docs/RUN_LOCALLY_UNDER_IIS.md) |
| Background, permissions, troubleshooting | [`Docs/DEPLOYMENT.md`](Docs/DEPLOYMENT.md) |

Both procedures start with the same script — `Scripts\publish-production.cmd`. Note that building in
Release inside Visual Studio is **not** sufficient on its own: it produces `bin\Release\`, which has
no IIS `web.config` and is not deployable. The script performs the publish for you.
