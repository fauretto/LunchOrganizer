<#
.SYNOPSIS
    Registers (or updates) a Windows Task Scheduler job that runs the published
    LunchOrganizer.Mailer.exe on weekdays at a configurable local time.

.DESCRIPTION
    Creates or updates a Task Scheduler task that launches the published
    LunchOrganizer.Mailer console application, Monday through Friday, at a
    configurable local time. The task is idempotent: running this script again
    with the same -TaskName updates the existing task's action, trigger,
    principal, and settings in place rather than creating a duplicate.

    By default the task runs as the built-in SYSTEM account. Supplying
    -UserName (and, for a real user account, -Password) runs the task under
    that account instead.

.PARAMETER ExecutablePath
    Full path to the published LunchOrganizer.Mailer.exe. Mandatory.

.PARAMETER TaskName
    Name of the Task Scheduler task to create/update. Defaults to
    "LunchOrganizer Daily Summary Mailer".

.PARAMETER TimeLocal
    Local time of day (HH:mm, 24-hour) at which the task should run on
    weekdays. Defaults to "09:01".

.PARAMETER UserName
    Optional account to run the task as. If omitted, the task runs as the
    built-in SYSTEM account (LogonType ServiceAccount, no password needed).

.PARAMETER Password
    Optional secure string password for -UserName. Only meaningful when
    -UserName is supplied and is not a well-known service account (SYSTEM,
    LOCAL SERVICE, NETWORK SERVICE). Required to run the task under a real
    user account whether or not that user is logged on.

.PARAMETER WorkingDirectory
    Working directory for the task action. Defaults to the directory
    containing -ExecutablePath.

.EXAMPLE
    .\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe'

    Registers the task to run as SYSTEM, weekdays at 09:01 local time.

.EXAMPLE
    .\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe' -TimeLocal '08:30' -TaskName 'Lunch Mailer (Test)'

    Registers a differently named task at a custom time.

.EXAMPLE
    $securePassword = Read-Host -AsSecureString -Prompt 'Password'
    .\register-mailer-task.ps1 -ExecutablePath 'C:\Apps\LunchOrganizer.Mailer\LunchOrganizer.Mailer.exe' -UserName 'CONTOSO\svc-lunchmailer' -Password $securePassword

    Registers the task to run under a domain service account.

.NOTES
    Windows PowerShell 5.1 compatible. Must be run elevated (Task Scheduler
    task creation for SYSTEM / other-user principals requires administrative
    rights).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $false)]
    [string]$TaskName = "LunchOrganizer Daily Summary Mailer",

    [Parameter(Mandatory = $false)]
    [string]$TimeLocal = "09:01",

    [Parameter(Mandatory = $false)]
    [string]$UserName,

    [Parameter(Mandatory = $false)]
    [securestring]$Password,

    [Parameter(Mandatory = $false)]
    [string]$WorkingDirectory
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExecutablePath)) {
    throw "ExecutablePath '$ExecutablePath' was not found. Publish LunchOrganizer.Mailer first."
}

$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Split-Path -Path $ExecutablePath -Parent
}

$parsedTime = [DateTime]::ParseExact($TimeLocal, "HH:mm", [System.Globalization.CultureInfo]::InvariantCulture)

$daysOfWeek = "Monday", "Tuesday", "Wednesday", "Thursday", "Friday"

Write-Host "Configuring scheduled task '$TaskName'..."
Write-Host "  Executable:        $ExecutablePath"
Write-Host "  Working directory: $WorkingDirectory"
Write-Host "  Schedule:          Weekdays (Mon-Fri) at $($parsedTime.ToString('HH:mm'))"

$action = New-ScheduledTaskAction -Execute $ExecutablePath -WorkingDirectory $WorkingDirectory

$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $daysOfWeek -At $parsedTime

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -RestartCount 1 `
    -RestartInterval (New-TimeSpan -Minutes 5) `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1)

$wellKnownServiceAccounts = @("SYSTEM", "NT AUTHORITY\SYSTEM", "LOCAL SERVICE", "NT AUTHORITY\LOCAL SERVICE", "NETWORK SERVICE", "NT AUTHORITY\NETWORK SERVICE")

$runAsSystem = $true
if (-not [string]::IsNullOrWhiteSpace($UserName)) {
    $runAsSystem = $false
}

$isWellKnownAccount = $false
if (-not $runAsSystem) {
    $isWellKnownAccount = $wellKnownServiceAccounts -contains $UserName.ToUpperInvariant()
}
$useServiceAccountLogon = $runAsSystem -or $isWellKnownAccount

if ($runAsSystem) {
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
} elseif ($isWellKnownAccount) {
    $principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType ServiceAccount -RunLevel Highest
} else {
    if ($null -eq $Password) {
        throw "A -Password is required when -UserName specifies a real user account (not SYSTEM/LOCAL SERVICE/NETWORK SERVICE)."
    }
    $principal = New-ScheduledTaskPrincipal -UserId $UserName -LogonType Password -RunLevel Highest
}

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

if ($null -ne $existingTask) {
    Write-Host "Existing task '$TaskName' found. Removing it so it can be re-registered with the desired configuration..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

if ($useServiceAccountLogon) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal | Out-Null
} else {
    $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    )
    try {
        Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -User $UserName -Password $plainPassword -RunLevel Highest | Out-Null
    } finally {
        $plainPassword = $null
    }
}

Write-Host ""
Write-Host "Task '$TaskName' registered/updated successfully." -ForegroundColor Green
Write-Host ""
Write-Host "To verify the task configuration:"
Write-Host "    Get-ScheduledTask -TaskName '$TaskName' | Format-List *"
Write-Host ""
Write-Host "To trigger it immediately for testing (without waiting for the schedule):"
Write-Host "    Start-ScheduledTask -TaskName '$TaskName'"
Write-Host ""
Write-Host "To view run history (last exit code, start/end times, errors):"
Write-Host "    Open taskschd.msc -> Task Scheduler Library -> '$TaskName' -> History tab"
Write-Host "    (History must be enabled once per machine: right-click the task -> Enable All Tasks History)"
Write-Host ""
Write-Host "To test the mailer executable directly, without Task Scheduler:"
Write-Host "    & '$ExecutablePath' --dry-run"
Write-Host "    `$LASTEXITCODE   # 0=Sent, 1=Failed, 2=Skipped (no bookings), 3=AlreadyHandled"
Write-Host ""
Write-Host "To remove this task later:"
Write-Host "    Unregister-ScheduledTask -TaskName '$TaskName' -Confirm:`$false"
