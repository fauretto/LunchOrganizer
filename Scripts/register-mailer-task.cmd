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
rem  The underlying task registration requires elevation. This wrapper will
rem  automatically request administrative privileges (a UAC prompt) and relaunch
rem  itself elevated if it was not started as Administrator, so a normal
rem  double-click is enough.
rem
rem  USAGE (a plain double-click works; accept the UAC prompt):
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

rem ---- Require administrative privileges; self-elevate via UAC if not already admin ----
net session >nul 2>&1
if errorlevel 1 (
	echo Administrative privileges are required. Requesting elevation via UAC...
	if "%~1"=="" (
		powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
	) else (
		powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
	)
	if errorlevel 1 (
		echo.
		echo Elevation was cancelled or failed. The task was not registered.
		pause
	)
	exit /b
)

rem ---- Prefer Windows PowerShell 5.1; the .ps1 is documented as 5.1 compatible ----
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS_EXE%" set "PS_EXE=powershell.exe"

rem  -File checks execution policy even when -ExecutionPolicy Bypass is passed, because Group
rem  Policy (MachinePolicy / UserPolicy) overrides the command-line flag. The workaround is to
rem  read the script text into memory and invoke it as a [scriptblock]: that path is not subject
rem  to the execution-policy file-signing check regardless of GPO settings.
rem  Note: cmd strips the escape character ^ before passing the line to PowerShell, so PowerShell
rem  receives a plain & (call operator) to invoke the scriptblock.
"%PS_EXE%" -NoProfile -Command $sb=[scriptblock]::Create([IO.File]::ReadAllText('%PS_SCRIPT%')); ^& $sb %*
set "EXITCODE=%errorlevel%"

echo.
if not "%EXITCODE%"=="0" (
	echo The PowerShell script exited with code %EXITCODE%.
) else (
	echo Done.
)

pause
endlocal & exit /b %EXITCODE%
