<#
.SYNOPSIS
	Publishes LunchOrganizer.Web and LunchOrganizer.Mailer in Release mode into a
	clean, ready-to-copy deployment layout.

.DESCRIPTION
	Runs `dotnet publish -c Release` for the two deployable applications in the
	solution:

	  * LunchOrganizer.Web    - the Blazor Server website
	  * LunchOrganizer.Mailer - the daily summary email console app run by
								Windows Task Scheduler

	The class libraries (Domain, Data, Services, Email) are NOT published
	separately: their DLLs are pulled into each application's publish output
	automatically.

	Each application is published into its own subfolder under -OutputRoot, e.g.

		<OutputRoot>\Web\
		<OutputRoot>\Mailer\

	Copy the CONTENTS of each subfolder to its own folder on the production
	server (for example C:\Apps\LunchOrganizer.Web and
	C:\Apps\LunchOrganizer.Mailer). Each already contains a `config\` subfolder;
	review config\database.json and config\email.json (or their *.local.json
	overrides) for production before running.

	By default this produces a framework-dependent publish, which requires the
	ASP.NET Core Runtime for .NET 10 (Hosting Bundle) to be installed on the
	server. Pass -SelfContained to bundle the runtime into the output instead,
	so no runtime install is needed on the server.

.PARAMETER OutputRoot
	Root folder that will receive the Web\ and Mailer\ publish subfolders.
	Defaults to "publish" under the repository root.

.PARAMETER Configuration
	Build configuration to publish. Defaults to "Release".

.PARAMETER SelfContained
	Switch. When present, publishes a self-contained deployment for -Runtime
	(bundles the .NET runtime, no runtime install needed on the server).

.PARAMETER Runtime
	Runtime identifier used only when -SelfContained is specified. Defaults to
	"win-x64".

.PARAMETER Clean
	Switch. When present, deletes the existing -OutputRoot before publishing so
	stale files from a previous publish cannot linger.

.EXAMPLE
	.\publish-production.ps1

	Framework-dependent publish of both apps into <repo>\publish\Web and
	<repo>\publish\Mailer.

.EXAMPLE
	.\publish-production.ps1 -OutputRoot 'C:\publish\LunchOrganizer' -Clean

	Cleans and publishes both apps into C:\publish\LunchOrganizer\Web and
	C:\publish\LunchOrganizer\Mailer.

.EXAMPLE
	.\publish-production.ps1 -SelfContained

	Self-contained publish (bundles the .NET 10 runtime) for win-x64, so the
	server does not need the runtime installed.

.NOTES
	Windows PowerShell 5.1 compatible. Requires the .NET SDK (dotnet CLI) on the
	machine running this script. Run from anywhere; paths are resolved relative
	to the repository root inferred from this script's location.
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory = $false)]
	[string]$OutputRoot,

	[Parameter(Mandatory = $false)]
	[string]$Configuration = "Release",

	[Parameter(Mandatory = $false)]
	[switch]$SelfContained,

	[Parameter(Mandatory = $false)]
	[string]$Runtime = "win-x64",

	[Parameter(Mandatory = $false)]
	[switch]$Clean
)

$ErrorActionPreference = "Stop"

# Repository root = parent of the folder containing this script (Scripts\..).
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir "..")).ProviderPath

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
	$OutputRoot = Join-Path $repoRoot "publish"
}

# Verify the dotnet CLI is available before doing anything else.
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
	throw "The 'dotnet' CLI was not found on PATH. Install the .NET SDK first."
}

# The two deployable applications: project path (relative to repo root) -> output subfolder.
$apps = @(
	[pscustomobject]@{ Name = "Web";    Project = "src\LunchOrganizer.Web\LunchOrganizer.Web.csproj" },
	[pscustomobject]@{ Name = "Mailer"; Project = "src\LunchOrganizer.Mailer\LunchOrganizer.Mailer.csproj" }
)

if ($Clean -and (Test-Path -LiteralPath $OutputRoot)) {
	Write-Host "Cleaning existing output folder '$OutputRoot'..."
	Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host ""
Write-Host "Publishing LunchOrganizer ($Configuration)" -ForegroundColor Cyan
Write-Host "  Repository root: $repoRoot"
Write-Host "  Output root:     $OutputRoot"
if ($SelfContained) {
	Write-Host "  Deployment:      self-contained ($Runtime)"
} else {
	Write-Host "  Deployment:      framework-dependent (server needs the .NET $Configuration runtime)"
}
Write-Host ""

foreach ($app in $apps) {
	$projectPath = Join-Path $repoRoot $app.Project
	if (-not (Test-Path -LiteralPath $projectPath)) {
		throw "Project '$projectPath' was not found. Run this script from the LunchOrganizer repository."
	}

	$appOutput = Join-Path $OutputRoot $app.Name

	Write-Host "Publishing $($app.Name) -> $appOutput" -ForegroundColor Yellow

	$arguments = @(
		"publish",
		$projectPath,
		"-c", $Configuration,
		"-o", $appOutput
	)

	if ($SelfContained) {
		$arguments += @("-r", $Runtime, "--self-contained", "true")
	}

	& $dotnet.Path @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet publish failed for $($app.Name) (exit code $LASTEXITCODE)."
	}

	Write-Host "  Done: $($app.Name)" -ForegroundColor Green
	Write-Host ""
}

Write-Host "Publish complete." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Copy the CONTENTS of these folders to the server:"
Write-Host "       $(Join-Path $OutputRoot 'Web')    -> e.g. C:\Apps\LunchOrganizer.Web"
Write-Host "       $(Join-Path $OutputRoot 'Mailer') -> e.g. C:\Apps\LunchOrganizer.Mailer"
Write-Host "  2. Edit each app's config\database.json / config\email.json (or *.local.json) for production."
Write-Host "  3. Start the Web app once so it applies EF Core migrations and creates the schema."
Write-Host "  4. Register the daily mailer task, pointing at the deployed exe:"
Write-Host "       .\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe' -TimeLocal '09:01'"
