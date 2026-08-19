# Session handovers

One file per working session, newest first. Each is written to be read cold by a future session with no
memory of the previous one: current state, decisions and their reasons, and an explicit split between what
is **proven** and what is merely **written**.

| Date | Session | State it left behind |
|---|---|---|
| 2026-08-19 | [Production deployment · LocalDB → SQL Server Express · Mailer](2026-08-19-production-deployment-sqlexpress-and-mailer.md) | Site live under IIS, mailer working end-to-end. **LocalDB abandoned — unusable under IIS.** `Failed: 1, Passed: 80, Total: 81`. **D1 still open.** No booking ever made through the UI. |
| 2026-08-18 | [SQL Server migration · confirmation emails · registration email](2026-08-18-sqlserver-migration-and-registration-email.md) | Builds clean. Migration steps 1–10 done, 11–13 blocked. **Defect D1 open.** Nothing manually validated. ⚠ Its §6 (Environment) is superseded by the 2026-08-19 entry. |

## Conventions

- Filename: `YYYY-MM-DD-short-topic.md`
- Always state what was **verified** versus what was only **written and reviewed**. That distinction is the
  main reason these files exist — code that compiles is not code that works.
- Record *why* a decision was taken, not just what was decided. The reasoning is what does not survive in
  the diff.
- Carry forward open defects and standing constraints explicitly; do not assume the next session will infer
  them from the code.
