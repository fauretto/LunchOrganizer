@echo off
setlocal enableextensions

rem ===========================================================================
rem  register-mailer-task.cmd
rem
rem  Wrapper around register-mailer-task.ps1 for machines where .ps1 files
rem  cannot be launched directly (corporate execution-policy restrictions).
rem
rem  It invokes Windows PowerShell with -ExecutionPolicy Bypass (scoped to this
rem  single call only, it does NOT change any machine/user policy) and forwards
rem  every argument you pass to this .cmd straight through to the .ps1 script.
rem
rem  The underlying task registration requires elevation, so this wrapper also
rem  verifies it is running as Administrator before continuing.
rem
rem  USAGE (run this .cmd "as administrator"):
rem
rem    register-mailer-task.cmd -ExecutablePath "C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe"
rem
rem    register-mailer-task.cmd -ExecutablePath "C:\Apps\...\LunchOrganizer.Mailer.exe" ^
rem                             -TimeLocal "08:30" -TaskName "Lunch Mailer (Test)"
rem
rem  Any parameters accepted by register-mailer-task.ps1 can be supplied here;
rem  they are passed along unchanged. If -ExecutablePath is omitted, PowerShell
rem  will prompt for it (it is a mandatory parameter of the script).
rem ===========================================================================

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%register-mailer-task.ps1"

if not exist "%PS_SCRIPT%" (
	echo ERROR: Could not find the PowerShell script:
	echo        "%PS_SCRIPT%"
	echo Make sure this .cmd file sits in the same folder as register-mailer-task.ps1.
	echo.
	pause
	exit /b 1
)

rem ---- Require administrative privileges (Task Scheduler registration needs them) ----
net session >nul 2>&1
if errorlevel 1 (
	echo This script must be run as Administrator.
	echo Right-click "register-mailer-task.cmd" and choose "Run as administrator".
	echo.
	pause
	exit /b 1
)

rem ---- Prefer Windows PowerShell 5.1; the .ps1 is documented as 5.1 compatible ----
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" set "PS_EXE=powershell.exe"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXITCODE=%errorlevel%"

echo.
if not "%EXITCODE%"=="0" (
	echo The PowerShell script exited with code %EXITCODE%.
) else (
	echo Done.
)

pause
endlocal & exit /b %EXITCODE%
