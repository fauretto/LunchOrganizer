# Session summary — 25 August 2026

**Project:** LunchOrganizer · `C:\Projects_Git\Data\GitPerso\LunchOrganizer`
**Session outcome:** the session opened with a **security regression**: after Windows Authentication was enabled in IIS on 24 August, the Administration page had become reachable by *every* domain user with no login at all. The cause was found (it is a consequence of the 24 August work, not a coincidence), the authorization gate was rebuilt around a marker claim, and admin passwords were moved from plaintext to PBKDF2 with a generator CLI. **Nothing has been exercised in a browser.** The project now waits on Massimo to refresh the deployed config and run the four browser checks in §7.

Written to be read **cold**. Previous summary: [`2026-08-24-session-summary.md`](2026-08-24-session-summary.md) — its §2.1 delegation incident, §2.3 config-divergence rule and §8 environment facts are still accurate and still worth reading. Its §4 conclusion about `UseAuthentication()` and its §2.5 "two copies of this repository" are both corrected below (§3).

**This document deliberately contains no password values.** The live admin password is Massimo's own choice and appears nowhere here; the stored token lives only in `config\admin-users.json`.

---

## 1. What changed today

| # | Change | Why it mattered |
|---|---|---|
| 1 | **`src/LunchOrganizer.Web/Security/AdminAuthorization.cs`** (new) — `PolicyName`, `AdminClaimType` (`lunchorganizer:admin`), `AdminClaimValue` | One place to hold the constant that now separates "an admin who signed in" from "anyone with a Windows account". Its XML doc records the IIS mechanism so the next reader does not have to re-derive §2.1 |
| 2 | **`AdminAuthEndpoints.cs`** — cookie sign-in now also issues the marker claim | The claim is the only thing a Windows principal can never have. Without it, "authenticated" was satisfiable by simply browsing to the site |
| 3 | **`Program.cs`** — `AddAuthorization()` now builds a policy scoped to the **cookie scheme** *and* requiring the **marker claim**, registered by name and installed as `options.DefaultPolicy` | Making it the *default* policy closed both exposed surfaces (`Admin.razor`'s bare `[Authorize]` and `/admin/report/export.csv`'s bare `.RequireAuthorization()`) without editing either file. `FallbackPolicy` left null so the booking page and `/admin/login` stay public |
| 4 | **`Program.cs`** — `OnValidatePrincipal` now also rejects a cookie lacking the marker claim | Retires cookies issued before today instead of leaving a holder authenticated-but-never-authorized |
| 5 | **`src/LunchOrganizer.Domain/Security/AdminPasswordHasher.cs`** (new) — PBKDF2/HMACSHA256, 210 000 iterations, 16-byte per-password random salt, 32-byte key; token `pbkdf2-sha256:<iterations>:<saltB64>:<hashB64>` | Placed in **Domain**, not Web, so the web app and the CLI share one implementation and cannot drift. The iteration count travels inside each token, so `DefaultIterations` can be raised later without invalidating stored entries |
| 6 | **`src/LunchOrganizer.Web/Security/AdminPasswordVerifier.cs`** (deleted) | Its two branches — plaintext comparison and unsalted `sha256:<hex>` — are gone. Per Massimo's decision there is **no weaker fallback path** left; a plaintext value in the config file now simply fails to authenticate |
| 7 | **`src/LunchOrganizer.AdminHash/`** (new project, added to the solution) + `README.md` | Generates a token from a password (positional argument, or a masked interactive prompt). Prints only; never writes `admin-users.json` |
| 8 | **`AdminAuthEndpoints.cs`** — warns when a stored value is not a valid `pbkdf2-sha256:` token, and warns when `admin-users.json` holds **duplicate entries for one username** | Both are diagnostics for failure modes that hit today (§2.3, §2.4) and previously produced only a generic "invalid" |
| 9 | **`config/admin-users.json`** — single `massimo` entry holding a PBKDF2 token | Ends the plaintext era for this file |
| 10 | **`tests/LunchOrganizer.Tests/Unit/AdminPasswordHasherTests.cs`** (new) — 20 facts | Round-trip, wrong password, salt uniqueness, and rejection of plaintext / `sha256:` / malformed tokens |

**Build and test status, run by the orchestrator on 25 August 2026, not taken from an agent's report:**

```
dotnet build LunchOrganizer.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
dotnet test LunchOrganizer.sln --filter "FullyQualifiedName~AdminPasswordHasherTests" --no-build
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 269 ms
```

Note: the incremental build above reported 0 warnings. Both agents, building with `--no-incremental`, reported exactly one pre-existing `CS9113` in `DailySummaryMailService.cs`, unrelated to this work. The agents also reported the solution-wide `dotnet test` as 100/101 with the single failure being the known SQL-Server race `ConcurrencyTests.TenParallelAddMenu_SameDay_ResultInTenSequentialMenuNumbers`; **that full run was not repeated by the orchestrator today.**

Everything above is **uncommitted**. `git status` at end of session:

```
 M LunchOrganizer.sln
 M config/admin-users.json
 M src/LunchOrganizer.Web/Endpoints/AdminAuthEndpoints.cs
 M src/LunchOrganizer.Web/Program.cs
 D src/LunchOrganizer.Web/Security/AdminPasswordVerifier.cs
?? Backups/lunchorganizer
?? src/LunchOrganizer.AdminHash/
?? src/LunchOrganizer.Domain/Security/
?? src/LunchOrganizer.Web/Security/AdminAuthorization.cs
?? tests/LunchOrganizer.Tests/Unit/AdminPasswordHasherTests.cs
```

Four of those paths are **untracked**, including the entire new project and the new Domain folder — a lost working tree loses them outright.

---

## 2. Problems found that were not on anyone's list

### 2.1 Enabling Windows Authentication silently switched the admin login off

This is the session's headline finding, and it is a direct consequence of the 24 August feature.

The mechanism, in order:

1. The site is hosted `hostingModel="inprocess"` (`publish\Web\web.config`). With Windows Authentication on and `IISServerOptions.AutomaticAuthentication` at its **default of `true`**, the IIS server sets `HttpContext.User` to the caller's Windows principal *before* any app middleware runs.
2. `AuthenticationMiddleware` overwrites `HttpContext.User` **only when the default scheme actually authenticates the request** — the shape is `if (await context.AuthenticateAsync(scheme) is { Principal: not null } result) context.User = result.Principal;`.
3. With no admin cookie, the cookie handler returns no result, so **the Windows principal survives untouched** — and its `Identity.IsAuthenticated` is `true`.
4. The admin gate was a bare `@attribute [Authorize]` (`Admin.razor:2`) and a bare `.RequireAuthorization()` (`AdminReportEndpoints.cs:20`), both resolving to a default policy of nothing more than `RequireAuthenticatedUser()`.

Result: every domain user who browsed to `/admin` passed the gate. Under Anonymous Authentication nobody had set `HttpContext.User`, so the identical code was correct — which is why this had never been seen before.

> **Carry-forward rule.** Once an *ambient* authentication source exists (IIS Windows auth, a reverse proxy, client certificates), `IsAuthenticated` no longer means "this user went through our sign-in". Never let `RequireAuthenticatedUser()` alone guard anything privileged. Require something only your own sign-in path can produce — a claim, a scheme, or both.

> **Second-order rule.** Turning on an authentication mechanism can *weaken* authorization. Changing authN and leaving authZ untouched is not a null change; the two must be reviewed together.

### 2.2 A component-level `[Authorize]` ignores a policy's authentication-scheme list

The fix could not rely on scheme scoping alone. `AuthorizationMiddleware` honours a policy's scheme list (it authenticates each listed scheme explicitly), but a Blazor **component**-level `[Authorize]` is evaluated by `AuthorizeRouteView` against the already-cascaded `ClaimsPrincipal` and never consults that list. A scheme-only policy would therefore have secured `/admin/report/export.csv` (an endpoint) while leaving `/admin` (a component) open — the more visible of the two.

Hence the policy carries **both** requirements: the scheme list for the endpoint path, the claim for the component path. The rationale is recorded in a comment in `Program.cs` because it is invisible from the code alone.

### 2.3 A duplicate username silently shadowed the new password, and it looked exactly like a broken hash

Massimo generated a token for a new password, pasted it into `admin-users.json`, and could not log in. The reported symptom — "the hash does not work" — pointed at the new crypto. It was not the crypto.

The file had been *appended to* rather than edited, leaving **two entries with `Username: "massimo"`**. The lookup was `FirstOrDefault(u => ...)`, so it always matched the first entry (the old placeholder's hash) and the new token was never read.

The check that settled it was calling `AdminPasswordHasher.Verify` directly against the second token: it returned `True`, which eliminated the hasher, the CLI and the paste in one step and left only the lookup. **A discriminating check: had it returned `False`, the diagnosis would have been the opposite one.** It was also run *before* any file edit, precisely so that a `False` result would not have led to deleting the only working entry and locking the account out.

> **Carry-forward rule.** When a config-driven credential "does not work", check for a duplicate key shadowing your edit before suspecting the algorithm. A JSON array of objects has no uniqueness guarantee, and `FirstOrDefault` fails silently.

`HandleLoginAsync` now logs a warning naming the duplicated username, so this specific hour is not repeatable.

### 2.4 The deployed config was stale again — the same trap as 24 August §2.3

`publish\Web\config\admin-users.json` is still dated **09:20** and still holds only the old single entry, while the source file was edited at 09:38 and again afterwards. If IIS serves from `publish\Web`, the running site has **never seen** the new token — so even a perfectly correct source file would still have failed the login. This is the second consecutive session in which stale deployed config produced a "the feature is broken" report; the 24 August carry-forward rule stands and was insufficient to prevent a recurrence.

Also still true, and still on the list from 24 August: `PcUser.Diagnostics` is `true` in **both** `config\app.json` and `publish\Web\config\app.json`, so `GET /whoami` is live.

---

## 3. Corrections to the previous summary

| The 24 August 2026 summary said | Actually true on 25 August 2026 |
|---|---|
| §4: "the `Windows` scheme *is* registered but the **default is `Cookies`**, so `UseAuthentication()` would have overwritten `HttpContext.User`" | Half right, and the missing half was a security hole. `UseAuthentication()` overwrites `HttpContext.User` **only when the cookie scheme succeeds** — i.e. only for an already-signed-in admin. On every *unauthenticated* request the IIS Windows principal survives, which is exactly what opened `/admin` to everyone (§2.1). The design conclusion drawn there — capture the PC user *before* `UseAuthentication()` — remains **correct and unchanged**; what was missed is the consequence for authorization |
| §2.5: "There are two copies of this repository … `D:\Data\GitPerso\LunchOrganizer` also exists and is **stale**" | **There is one tree with two paths.** `D:\Data` is a **`SymbolicLink` whose target is `C:\Projects_Git\Data`** (`Get-Item 'D:\Data'` → `LinkType : SymbolicLink`), and `C:\Projects_Git\Data\GitPerso\LunchOrganizer\config` and `D:\Data\GitPerso\LunchOrganizer\config` return the **same inode**. Editing "the D: file" edits the C: file. Whether the link post-dates 24 August was not established — but any advice to "build from the C: copy, not the stale D: one" is now meaningless |
| §3: "HEAD is `19cec11`" / "7 commits" | HEAD is **`97db7e8`** *"Recorded the Windows user of the PC that made each booking"*, **8 commits**. The 24 August work was committed after that summary was written |
| §1 item 1 and §6 W1 reference the admin **cookie** as the app's default scheme | Still true, but `AdminPasswordVerifier` (the class that checked admin passwords) **no longer exists** — it was deleted today. Anything referring to it, or to plaintext/`sha256:` passwords in `admin-users.json`, is out of date |

The 20 August §5 statement that no UI element of the *menu import* feature has ever been rendered in a browser was **not** revisited today and should still be treated as true.

---

## 4. Verification actually performed

**Static analysis of the vulnerability (§2.1).** Read end to end: `Program.cs` pipeline order, the two `[Authorize]`/`.RequireAuthorization()` call sites, `web.config`'s `hostingModel`, and `PcUserCaptureMiddleware`. Corroborated by two independent signals — the existing comment at `Program.cs:263-265` describes the shared-`HttpContext.User` hazard in the app's own words, and the 24 August `/whoami` capture (that summary §4) records `Identity.AuthenticationType: Negotiate` with both `Cookies` and `Windows` schemes registered. **This is reasoning plus evidence from a prior session's capture, not a live reproduction** — see §5.

**The password round-trip (agent-run, reported literally, throwaway project deleted afterwards).** Chosen to discriminate between the four candidate causes of the failed login — hasher, CLI, paste, lookup:

- `Verify(<new password>, <token in the second entry>)` → **True** — clears hasher, CLI and paste; leaves only the lookup
- `Verify(<old placeholder>, <token in the first entry>)` → **True** — confirms the first entry was the one being consulted
- `Verify(<password>, "changeme")` → **False**, `Verify(<password>, "sha256:ABC")` → **False** — confirms the weak formats are genuinely gone, not merely unused

**Orchestrator-run, today** (quoted verbatim in §1): solution build — `Build succeeded. 0 Warning(s) 0 Error(s)`; the 20 hasher tests — `Passed! - Failed: 0, Passed: 20`.

**Independent review of every agent diff.** Each of the three agents' changes was re-read from the working tree (`git diff` plus direct file reads), not accepted from its report. One discrepancy was caught this way and is recorded in §3: an agent stated it had not touched `D:\...\admin-users.json`, yet that file changed — resolved by discovering the symlink, which means the statement was true and the "two files" premise was false.

---

## 5. The limit of that verification — read this before trusting today's work

**Nothing was run in a browser, and no HTTP request was made against the app at any point today.** The entire authorization fix is verified by *compilation and reading* only. Specifically unproven at runtime:

- that an unauthenticated domain user is now actually redirected away from `/admin` — **the very bug this session exists to fix has not been observed as fixed**;
- that `massimo` can still sign in at all. If the marker claim, the policy, or the cookie interaction is wrong in some way the compiler cannot see, the result is a **locked-out administrator**, and that failure mode has not been ruled out;
- that `/admin/report/export.csv` refuses an unauthenticated request;
- that the `OnValidatePrincipal` claim check behaves as intended (it should sign out any pre-existing cookie; if it misfires it produces a sign-in loop);
- that PBKDF2 verification works *through the web app's own login form*. It is proven only via a direct call to `AdminPasswordHasher.Verify` from a console program.

**There is no automated test of admin authorization anywhere in the solution** — not before today and not after. The 20 new tests cover the hasher in isolation and say nothing about who can reach `/admin`. A green test run must not be read as evidence that the security fix works.

Also untested: PBKDF2's ~210 000 iterations add measurable CPU work per login attempt. That is deliberate and correct, but the actual latency on the production server was never measured, and the login endpoint's existing rate limit (5/minute/IP) has not been re-examined in that light.

Everything in the 24 August §5 remains true and unaddressed — in particular, Windows Authentication is still proven for exactly one user on the IIS host itself, never from a colleague's PC by hostname.

---

## 6. Decisions and open judgment calls

| # | Decision | Reasoning |
|---|---|---|
| D1 | Marker claim + tightened `DefaultPolicy` (option A), over a middleware that rewrites `HttpContext.User` (B) or `AutomaticAuthentication = false` (C) | A fixes both exposed surfaces at once, leaves `HttpContext.User` semantics untouched so the 24 August PC-user capture keeps working unmodified, and fails closed. C is arguably the most correct design but would have required reworking `PcUserCaptureMiddleware` — the newest, least-proven code in the repo |
| D2 | Claim **and** scheme in the policy, not either alone | §2.2 — the two authorization paths honour different halves |
| D3 | Marker claim uses a custom type `lunchorganizer:admin`, **not** `ClaimTypes.Role` | A Windows principal already carries role claims (group SIDs), which would make any role-based reasoning here confusing and easy to get subtly wrong |
| D4 | PBKDF2 with per-password salt, over the existing unsalted `sha256:`, `PasswordHasher<T>` from ASP.NET Identity, or DPAPI | No new package; salted and slow; self-describing token. `PasswordHasher<T>` means an Identity package reference for one class; DPAPI is reversible encryption, which is the wrong primitive |
| D5 | **Massimo's call:** hashed values only — the plaintext *and* `sha256:` branches removed outright, no backward compatibility | A silent plaintext fallback means a plaintext password keeps working and nobody notices. The cost is that the config file must be migrated before the app is usable, which is now visible as a logged warning rather than a mystery |
| D6 | The repo config was first seeded with a hash of the **old placeholder password** rather than left as plaintext | Avoided handing over a locked-out app while keeping the security posture exactly as it already was. Superseded the same day when Massimo set his own password |
| D7 | A console project for hash generation, over a PowerShell script | The tool shares `AdminPasswordHasher` with the web app, so generation and verification cannot diverge. A script would reimplement the KDF |
| D8 | The generator prints only; it never writes `admin-users.json` | Keeps a tool that handles passwords out of the business of editing production config |

**Burned credential, for the record:** the placeholder value that this file shipped with until today (the literal string `changeme`, present in git history) must be treated as public. It is no longer valid — the current entry is a token for a password Massimo chose on 25 August 2026.

**Open:** whether `/admin` should be restricted further than "any account in `admin-users.json`" — e.g. to specific AD groups — was never discussed. Windows Authentication now makes that possible; it is not implemented and nobody has asked for it.

---

## 7. What remains

Ordered by what blocks what. Items 1-2 block any claim that this session's work functions.

| # | Item | Owner |
|---|---|---|
| 1 | **Refresh the deployed config** — re-run `Scripts\publish-production.cmd`, or copy `config\admin-users.json` over `publish\Web\config\admin-users.json`. Until this is done the running site still holds the 09:20 file with the old entry and the new password *cannot* work (§2.4) | **Massimo** |
| 2 | **Run the four browser checks**, in this order: (a) an unauthenticated domain user hitting `/admin` is redirected to `/admin/login`; (b) `massimo` can sign in and reach `/admin`; (c) `/admin/report/export.csv` does not return a CSV while signed out; (d) booking still records the PC user, i.e. today's change did not disturb the 24 August feature. **(b) is the lock-out check — run it early** | **Massimo** |
| 3 | If a login fails after item 1: the log now discriminates. A **warning** about a non-`pbkdf2-sha256:` value means the deployed file is still stale; a warning about **duplicate entries** means the file was appended to rather than edited; only the bare `Admin login failed for username 'massimo'` means the password itself did not match | **Massimo** |
| 4 | **Commit.** Everything is uncommitted and four paths are untracked (§1). `/git_commit_message` prepares the message; the commit is Massimo's to run | **Massimo** |
| 5 | **Add automated coverage for admin authorization** — there is none (§5). A `WebApplicationFactory` test asserting that an authenticated-but-claimless principal is refused `/admin` would have caught §2.1 the day it was introduced | code |
| 6 | **Remove `WhoAmIDiagnosticsMiddleware`** and its `Program.cs` line; set `PcUser.Diagnostics` to `false` in **both** `config\app.json` and `publish\Web\config\app.json` — still `true` in both, second session running | code + **Massimo** |
| 7 | **Check the ACLs on the deployed `config\` folder.** It now holds a password hash alongside the database and SMTP credentials in `database.json` / `email.json` | **Massimo** |
| 8 | Document the new password workflow (generate with `LunchOrganizer.AdminHash`, paste the token, one entry per username) in `DEPLOYMENT.md` / `INSTALLATIONFORDUMMIES.md`, and remove any instruction that says to type a plaintext password into `admin-users.json` | code |
| 9 | Decide whether `LunchOrganizer.AdminHash` should be excluded from the production publish — it is an admin utility with no runtime role | **Massimo** → then code |
| 10 | Everything still open from the 24 August summary §7, in particular item 5 (**test from a colleague's PC by hostname** — still the single most important untested thing in the PC-user feature) and items 2-4 (deployed `BookingCutOffLocalTime`, `Database.Seed`, `DefaultLunchPrice`) | **Massimo** |

---

## 8. Environment facts worth not rediscovering

- **`D:\Data` is a symbolic link to `C:\Projects_Git\Data`.** There is one repository, reachable by two paths. Confirmed by `Get-Item 'D:\Data'` → `LinkType : SymbolicLink`, `Target : C:\Projects_Git\Data`, and by identical inode numbers on the `config` directories. This supersedes 24 August §2.5.
- **`publish\Web\config\` diverges from `config\` and does not reliably refresh on publish.** Checked twice today; still stale both times. Diff it before diagnosing any config-driven behaviour.
- **The runtime config directory is `AppContext.BaseDirectory\config`** (`Program.cs:31`) — i.e. whatever `config\` sits beside the deployed `LunchOrganizer.Web.dll`. All eight JSON files are registered with `reloadOnChange: true`, so a config edit takes effect on the next request with no restart. `admin-users.json` is bound **section-less** — the whole file *is* the options object.
- **Changing a password does not invalidate a live session.** `OnValidatePrincipal` checks that the *username* still exists in the file (and now that the marker claim is present) — never the password. Sign out to test a new password.
- **`LunchOrganizer.AdminHash` usage:** `dotnet run --project src\LunchOrganizer.AdminHash -- "<password>"`, or no argument for a masked prompt. **Prefer the prompt** — an argument lands in PowerShell history and in the process command line. Each run yields a *different* token for the same password (fresh salt); all of them verify.

---

## 9. Working rules confirmed or established

- **All source-code generation and editing goes through a Sonnet agent** (Massimo's standing global directive), with the orchestrator writing precise file-by-file instructions and then reviewing the diff. Followed for all three of today's changes. Non-code artefacts — this summary included — are written directly.
- **Never run `git commit`, `git add` or `git push`.** Every agent prompt today restated this explicitly; the working tree is Massimo's to commit.
- **Verify an agent's report against the working tree.** Every diff was re-read today; one apparent contradiction in an agent's report turned out to reveal the symlink (§4).
- **Verify before destroying.** The duplicate-entry fix was gated on a prior `Verify` call precisely so that a negative result would leave the working credential in place (§2.3). State the branch in the agent's instructions, do not leave it to judgement.
- **Prefer a discriminating test over a confirming one** (established 13 August; earned its keep again today — §2.3, §4).
- **Massimo runs the validation tests himself.** Agents and the orchestrator stop at build-and-unit-test; browser and IIS verification is his.

---

## 10. Document index — where today's work is described

| File | What it holds |
|---|---|
| `src/LunchOrganizer.AdminHash/README.md` | How to generate a password token and what to paste into `admin-users.json` |
| `src/LunchOrganizer.Domain/Security/AdminPasswordHasher.cs` | XML docs on the token format and why unsalted SHA-256 and plaintext were dropped |
| `src/LunchOrganizer.Web/Security/AdminAuthorization.cs` | XML docs on why a marker claim is necessary under IIS Windows Authentication — the §2.1 mechanism, recorded next to the code |
| `Docs/sessions/2026-08-24-session-summary.md` | The PC-user capture feature, the delegation incident, and the config-divergence rule. Read §5 and §7 before touching the booking path |
