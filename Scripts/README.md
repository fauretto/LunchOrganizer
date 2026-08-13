# Scripts

This folder contains operational scripts for LunchOrganizer that live outside the .NET solution itself.

## Database bootstrap: `create_database.sql` + `create_database.ps1`

### What they are

- **`create_database.sql`** — an idempotent SQL script that creates the LunchOrganizer schema (tables, constraints, indexes, etc.). It is generated directly from the real EF Core migration `20260812075601_InitialCreate` via:

  ```
  dotnet ef migrations script --idempotent
  ```

  It is **not** hand-written and should not be hand-edited (aside from its header comment) — regenerate it from the migration if the schema changes. It does **not** create the `lunchorganizer` database itself; it only creates objects inside whichever database `psql` is already connected to.

- **`create_database.ps1`** — a PowerShell 5.1-compatible script that locates `psql.exe`, prompts for connection details, creates the target database if it doesn't already exist, and then runs `create_database.sql` against it.

### When to use the offline route vs. the app's automatic bootstrap

LunchOrganizer's own startup path can create/migrate its database automatically the first time it runs, using the connection string and role configured for the app. Use **that** route for normal day-to-day development and for environments where the app's database role is allowed to `CREATE DATABASE` / apply migrations itself.

Use this **offline route** (`create_database.ps1` + `create_database.sql`) instead when:

- The target server is locked down and the application's own database role does **not** have permission to `CREATE DATABASE` or run DDL — e.g. a DBA-managed production/staging PostgreSQL instance where only an admin account (like `postgres`) can create databases and schema, and the app's runtime role is granted access afterward.
- You want to provision the database ahead of time, independently of the application's own startup, using an admin credential rather than the app's runtime credential.
- You want to re-apply the schema to an existing database safely (both scripts are idempotent/safe to re-run).

### How to run it

From the repo root, or from anywhere — the script resolves its own location and finds `create_database.sql` next to itself:

```powershell
./Scripts/create_database.ps1
```

(Equivalently: `C:\Projects_Git\Data\GitPerso\LunchOrganizer\Scripts\create_database.ps1` from any working directory.)

### What it prompts for

1. **`psql.exe` location** — normally found automatically (see below); only prompted for if none of the automatic lookups succeed.
2. **Host** (default: `localhost`)
3. **Port** (default: `5432`)
4. **Username** (default: `postgres`)
5. **Database name** to create (default: `lunchorganizer`)
6. **Password** — entered securely (masked input via `Read-Host -AsSecureString`), used only transiently in memory to authenticate the `psql` calls. It is never written to disk, logged, or echoed back, and the environment variable holding it is cleared before the script exits.

The script then:

- Creates the database if it doesn't already exist (tolerating a race where it gets created concurrently).
- Runs `create_database.sql` against that database with `ON_ERROR_STOP=1`.
- Prints a clear success/failure summary and exits non-zero on any failure.

### Where `psql.exe` is expected to be found

The script searches, in order, and stops at the first match:

1. `psql` on the `PATH`, if it's ever added there.
2. The known fixed path on machines set up like this one: **`D:\PostgreSQL\bin\psql.exe`**.
3. Common install locations on a fresh/typical machine, for portability: **`C:\Program Files\PostgreSQL\16\bin\psql.exe`**, **`C:\Program Files\PostgreSQL\17\bin\psql.exe`**, or any version found by globbing `C:\Program Files\PostgreSQL\*\bin\psql.exe` and `D:\PostgreSQL*\bin\psql.exe`.
4. The PostgreSQL Windows service registration in the registry (e.g. `postgresql-x64-16`, or any service matching `postgresql*`), deriving `psql.exe`'s location from the service's executable path.
5. If none of the above find it, you'll be prompted to enter the full path to `psql.exe` interactively.

In short: on machines provisioned like this one, expect **`D:\PostgreSQL\bin\psql.exe`**; on a fresh machine with a standard PostgreSQL installer, expect **`C:\Program Files\PostgreSQL\<version>\bin\psql.exe`**.

## Other scripts

- **`register-mailer-task.ps1`** — see the script itself for usage; unrelated to database provisioning.
