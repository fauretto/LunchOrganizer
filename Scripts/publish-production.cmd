@echo off
setlocal EnableDelayedExpansion
REM ---------------------------------------------------------------------------
REM  publish-production.cmd
REM
REM  Self-contained publisher for LunchOrganizer. Runs `dotnet publish -c
REM  Release` directly for the two deployable applications:
REM
REM      * LunchOrganizer.Web    - the Blazor Server website
REM      * LunchOrganizer.Mailer - the daily summary email console app
REM
REM  This batch file calls the dotnet CLI directly (no PowerShell script), so it
REM  works even when the machine's PowerShell execution policy is locked down by
REM  Group Policy (e.g. AllSigned), which -ExecutionPolicy Bypass cannot override.
REM
REM  Each app is published into its own subfolder under the output root:
REM      <OutputRoot>\Web\
REM      <OutputRoot>\Mailer\
REM
REM  Options (all optional):
REM      -OutputRoot <path>    Root for the Web\ and Mailer\ subfolders.
REM                            Default: <repo>\publish
REM      -Configuration <cfg>  Build configuration. Default: Release
REM      -SelfContained        Bundle the .NET runtime (no runtime install
REM                            needed on the server).
REM      -Runtime <rid>        RID for -SelfContained. Default: win-x64
REM      -Clean                Delete the output root before publishing.
REM
REM  Examples:
REM      publish-production.cmd
REM      publish-production.cmd -Clean
REM      publish-production.cmd -OutputRoot C:\publish\LunchOrganizer -Clean
REM      publish-production.cmd -SelfContained
REM ---------------------------------------------------------------------------

REM ---- Resolve repository root (parent of this script's Scripts folder) ----
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"

REM ---- Defaults ----
set "OUTPUT_ROOT="
set "CONFIGURATION=Release"
set "SELF_CONTAINED=0"
set "RUNTIME=win-x64"
set "CLEAN=0"

REM ---- Parse arguments ----
:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="-OutputRoot" (
    set "OUTPUT_ROOT=%~2"
    shift
    shift
    goto parse_args
)
if /I "%~1"=="-Configuration" (
    set "CONFIGURATION=%~2"
    shift
    shift
    goto parse_args
)
if /I "%~1"=="-Runtime" (
    set "RUNTIME=%~2"
    shift
    shift
    goto parse_args
)
if /I "%~1"=="-SelfContained" (
    set "SELF_CONTAINED=1"
    shift
    goto parse_args
)
if /I "%~1"=="-Clean" (
    set "CLEAN=1"
    shift
    goto parse_args
)
echo ERROR: Unknown argument '%~1'.
echo Valid options: -OutputRoot ^<path^> -Configuration ^<cfg^> -SelfContained -Runtime ^<rid^> -Clean
exit /b 1

:args_done
if not defined OUTPUT_ROOT set "OUTPUT_ROOT=%REPO_ROOT%\publish"

REM ---- Verify dotnet CLI is available ----
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: The 'dotnet' CLI was not found on PATH. Install the .NET SDK first.
    exit /b 1
)

set "WEB_PROJECT=%REPO_ROOT%\src\LunchOrganizer.Web\LunchOrganizer.Web.csproj"
set "MAILER_PROJECT=%REPO_ROOT%\src\LunchOrganizer.Mailer\LunchOrganizer.Mailer.csproj"

if not exist "%WEB_PROJECT%" (
    echo ERROR: Project '%WEB_PROJECT%' was not found. Run this script from the LunchOrganizer repository.
    exit /b 1
)
if not exist "%MAILER_PROJECT%" (
    echo ERROR: Project '%MAILER_PROJECT%' was not found. Run this script from the LunchOrganizer repository.
    exit /b 1
)

REM ---- Optional clean ----
if "%CLEAN%"=="1" (
    if exist "%OUTPUT_ROOT%" (
        echo Cleaning existing output folder "%OUTPUT_ROOT%"...
        rmdir /s /q "%OUTPUT_ROOT%"
    )
)

echo.
echo Publishing LunchOrganizer (%CONFIGURATION%)
echo   Repository root: %REPO_ROOT%
echo   Output root:     %OUTPUT_ROOT%
if "%SELF_CONTAINED%"=="1" (
    echo   Deployment:      self-contained ^(%RUNTIME%^)
) else (
    echo   Deployment:      framework-dependent ^(server needs the .NET runtime^)
)
echo.

REM ---- Build the shared self-contained arguments once ----
set "SC_ARGS="
if "%SELF_CONTAINED%"=="1" set "SC_ARGS=-r %RUNTIME% --self-contained true"

REM ---- Publish Web ----
echo Publishing Web -^> %OUTPUT_ROOT%\Web
dotnet publish "%WEB_PROJECT%" -c %CONFIGURATION% -o "%OUTPUT_ROOT%\Web" %SC_ARGS%
if errorlevel 1 (
    echo ERROR: dotnet publish failed for Web.
    exit /b 1
)
echo   Done: Web
echo.

REM ---- Publish Mailer ----
echo Publishing Mailer -^> %OUTPUT_ROOT%\Mailer
dotnet publish "%MAILER_PROJECT%" -c %CONFIGURATION% -o "%OUTPUT_ROOT%\Mailer" %SC_ARGS%
if errorlevel 1 (
    echo ERROR: dotnet publish failed for Mailer.
    exit /b 1
)
echo   Done: Mailer
echo.

echo Publish complete.
echo.
echo Next steps:
echo   1. Copy the CONTENTS of these folders to the server:
echo        %OUTPUT_ROOT%\Web     -^> e.g. C:\Apps\LunchOrganizer.Web
echo        %OUTPUT_ROOT%\Mailer  -^> e.g. C:\Apps\LunchOrganizer.Mailer
echo   2. Edit each app's config\database.json / config\email.json (or *.local.json) for production.
echo   3. Start the Web app once so it applies EF Core migrations and creates the schema.
echo   4. Register the daily mailer task, pointing at the deployed exe.

endlocal
exit /b 0
