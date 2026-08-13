<#
.SYNOPSIS
    Creates the LunchOrganizer PostgreSQL database (if needed) and applies the
    schema from create_database.sql.

.DESCRIPTION
    This script:
      1. Locates psql.exe on this machine (PATH, known fixed paths, common
         install locations, or the PostgreSQL Windows service registration).
      2. Prompts for connection details (host, port, username, database name,
         password).
      3. Creates the target database if it does not already exist.
      4. Runs Scripts\create_database.sql (an idempotent EF Core migration
         script) against that database.

    Compatible with Windows PowerShell 5.1 (no ??, no ternary ?:, no &&/||).
#>

# ------------------------------------------------------------------------
# Step 1: Locate psql.exe
# ------------------------------------------------------------------------
# We try several strategies in order and stop at the first one that finds
# a real file on disk. $psqlPath and $psqlSource together describe what we
# used and how we found it, for the informational message printed later.

$psqlPath = $null
$psqlSource = $null

# (a) Is psql already on PATH?
if ($null -eq $psqlPath) {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        $psqlPath = $cmd.Source
        $psqlSource = "found on PATH"
    }
}

# (b) Known fixed path on this machine.
if ($null -eq $psqlPath) {
    $knownFixedPath = "D:\PostgreSQL\bin\psql.exe"
    if (Test-Path -Path $knownFixedPath -PathType Leaf) {
        $psqlPath = $knownFixedPath
        $psqlSource = "found at known fixed path ($knownFixedPath)"
    }
}

# (c) Other common install locations, including wildcard globs.
if ($null -eq $psqlPath) {
    $candidatePaths = @(
        "C:\Program Files\PostgreSQL\16\bin\psql.exe",
        "C:\Program Files\PostgreSQL\17\bin\psql.exe"
    )

    foreach ($candidate in $candidatePaths) {
        if ($null -eq $psqlPath) {
            if (Test-Path -Path $candidate -PathType Leaf) {
                $psqlPath = $candidate
                $psqlSource = "found at common install location ($candidate)"
            }
        }
    }
}

# (c continued) Wildcard globs across possible PostgreSQL versions/drives.
if ($null -eq $psqlPath) {
    $globPatterns = @(
        "C:\Program Files\PostgreSQL\*\bin\psql.exe",
        "D:\PostgreSQL*\bin\psql.exe"
    )

    $globMatches = @()
    foreach ($pattern in $globPatterns) {
        $found = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
        if ($null -ne $found) {
            $globMatches += $found
        }
    }

    if ($globMatches.Count -gt 0) {
        # Prefer a higher version number when multiple matches exist, but
        # simple descending sort by full path is sufficient here.
        $sorted = $globMatches | Sort-Object -Property FullName -Descending
        $chosen = $sorted[0]
        $psqlPath = $chosen.FullName
        $psqlSource = "found via wildcard search ($($chosen.FullName))"
    }
}

# (d) Read the PostgreSQL Windows service's ImagePath from the registry.
if ($null -eq $psqlPath) {

    function Get-PsqlPathFromServiceImagePath {
        param([string]$ImagePath)

        if ([string]::IsNullOrWhiteSpace($ImagePath)) {
            return $null
        }

        # ImagePath may look like:
        #   "C:\Program Files\PostgreSQL\16\bin\pg_ctl.exe" runservice -N ...
        # or (less commonly) an unquoted path. Extract the executable path.
        $exePath = $null
        if ($ImagePath.StartsWith('"')) {
            $endQuoteIndex = $ImagePath.IndexOf('"', 1)
            if ($endQuoteIndex -gt 0) {
                $exePath = $ImagePath.Substring(1, $endQuoteIndex - 1)
            }
        }
        else {
            # Unquoted: take everything up to the first space as a best effort.
            $spaceIndex = $ImagePath.IndexOf(' ')
            if ($spaceIndex -gt 0) {
                $exePath = $ImagePath.Substring(0, $spaceIndex)
            }
            else {
                $exePath = $ImagePath
            }
        }

        if ([string]::IsNullOrWhiteSpace($exePath)) {
            return $null
        }

        $binDirectory = Split-Path -Path $exePath -Parent
        if ([string]::IsNullOrWhiteSpace($binDirectory)) {
            return $null
        }

        $candidatePsql = Join-Path -Path $binDirectory -ChildPath "psql.exe"
        if (Test-Path -Path $candidatePsql -PathType Leaf) {
            return $candidatePsql
        }

        return $null
    }

    $registryBase = "HKLM:\SYSTEM\CurrentControlSet\Services"
    $defaultServiceKey = Join-Path -Path $registryBase -ChildPath "postgresql-x64-16"

    $serviceImagePath = $null

    if (Test-Path -Path $defaultServiceKey) {
        $svc = Get-ItemProperty -Path $defaultServiceKey -Name "ImagePath" -ErrorAction SilentlyContinue
        if ($null -ne $svc) {
            $serviceImagePath = $svc.ImagePath
        }
    }

    if ([string]::IsNullOrWhiteSpace($serviceImagePath)) {
        # Scan all services for anything matching postgresql*
        $allServiceKeys = Get-ChildItem -Path $registryBase -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like "postgresql*" }

        foreach ($serviceKey in $allServiceKeys) {
            if ([string]::IsNullOrWhiteSpace($serviceImagePath)) {
                $svc = Get-ItemProperty -Path $serviceKey.PSPath -Name "ImagePath" -ErrorAction SilentlyContinue
                if ($null -ne $svc) {
                    $serviceImagePath = $svc.ImagePath
                }
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($serviceImagePath)) {
        $derivedPsqlPath = Get-PsqlPathFromServiceImagePath -ImagePath $serviceImagePath
        if ($null -ne $derivedPsqlPath) {
            $psqlPath = $derivedPsqlPath
            $psqlSource = "derived from PostgreSQL service registry ImagePath"
        }
    }
}

# (e) Last resort: prompt the user interactively.
if ($null -eq $psqlPath) {
    $attempt = 0
    $maxAttempts = 3

    while ($null -eq $psqlPath -and $attempt -lt $maxAttempts) {
        $attempt = $attempt + 1
        $userProvidedPath = Read-Host -Prompt "Full path to psql.exe"

        if (-not [string]::IsNullOrWhiteSpace($userProvidedPath) -and (Test-Path -Path $userProvidedPath -PathType Leaf)) {
            $psqlPath = $userProvidedPath
            $psqlSource = "provided interactively by user"
        }
        else {
            Write-Host "That path does not exist or is not a file. Please try again."
        }
    }

    if ($null -eq $psqlPath) {
        Write-Host "ERROR: Could not locate psql.exe after $maxAttempts attempts. Aborting." -ForegroundColor Red
        exit 1
    }
}

# Report which psql we're using (not sensitive information).
Write-Host "Using psql.exe: $psqlPath"
Write-Host "  ($psqlSource)"
Write-Host ""

# ------------------------------------------------------------------------
# Step 2 onward: prompt for connection details, create DB, run schema script.
# ------------------------------------------------------------------------
# Everything from here through the psql invocations is wrapped in try/finally
# so that PGPASSWORD is always cleared afterwards, even if something throws.

$overallSuccess = $true
$stepResults = @()

try {
    # --- Prompt for connection details ---

    $HostName = Read-Host -Prompt "Host (default: localhost)"
    if ([string]::IsNullOrWhiteSpace($HostName)) {
        $HostName = "localhost"
    }

    $Port = Read-Host -Prompt "Port (default: 5432)"
    if ([string]::IsNullOrWhiteSpace($Port)) {
        $Port = "5432"
    }

    $Username = Read-Host -Prompt "Username (default: postgres)"
    if ([string]::IsNullOrWhiteSpace($Username)) {
        $Username = "postgres"
    }

    $Database = Read-Host -Prompt "Database name (default: lunchorganizer)"
    if ([string]::IsNullOrWhiteSpace($Database)) {
        $Database = "lunchorganizer"
    }

    $securePassword = Read-Host -AsSecureString -Prompt "Password"

    # Convert the SecureString to plain text only transiently, to set
    # PGPASSWORD for the psql child processes. Never written to disk/log.
    $plainPassword = [System.Net.NetworkCredential]::new('', $securePassword).Password
    $env:PGPASSWORD = $plainPassword

    # --- Step: check whether the database already exists ---

    Write-Host ""
    Write-Host "Checking whether database '$Database' already exists..."

    $checkOutput = & $psqlPath -h $HostName -p $Port -U $Username -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'" 2>&1
    $checkExitCode = $LASTEXITCODE
    $databaseExists = $false

    if ($checkExitCode -eq 0) {
        $trimmedOutput = ($checkOutput | Out-String).Trim()
        if ($trimmedOutput -eq "1") {
            $databaseExists = $true
        }
    }
    else {
        Write-Host "WARNING: Could not query pg_database (exit code $checkExitCode). Output:" -ForegroundColor Yellow
        Write-Host $checkOutput
        $overallSuccess = $false
        $stepResults += "FAILED: Check whether database exists"
    }

    # --- Step: create the database if it doesn't exist ---

    if ($databaseExists) {
        Write-Host "Database '$Database' already exists. Skipping creation."
        $stepResults += "SKIPPED (already existed): Create database '$Database'"
    }
    else {
        Write-Host "Database '$Database' does not exist. Creating it..."

        $createOutput = & $psqlPath -h $HostName -p $Port -U $Username -d postgres -c ('CREATE DATABASE "' + $Database + '"') 2>&1
        $createExitCode = $LASTEXITCODE

        if ($createExitCode -eq 0) {
            Write-Host "Database '$Database' created successfully."
            $stepResults += "SUCCESS: Create database '$Database'"
        }
        else {
            # The CREATE DATABASE call failed. Before treating this as a real
            # failure, re-check whether the database exists now -- it may
            # have been created concurrently by another process (a race),
            # in which case this is not an actual failure.
            Write-Host "CREATE DATABASE reported a non-zero exit code. Re-checking existence to rule out a race..." -ForegroundColor Yellow

            $recheckOutput = & $psqlPath -h $HostName -p $Port -U $Username -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'" 2>&1
            $recheckExitCode = $LASTEXITCODE
            $recheckTrimmed = ($recheckOutput | Out-String).Trim()

            if ($recheckExitCode -eq 0 -and $recheckTrimmed -eq "1") {
                Write-Host "Database '$Database' exists after all (likely created concurrently). Treating as success."
                $stepResults += "SUCCESS (race tolerated): Create database '$Database'"
            }
            else {
                Write-Host "ERROR: Failed to create database '$Database'. Output:" -ForegroundColor Red
                Write-Host $createOutput
                $overallSuccess = $false
                $stepResults += "FAILED: Create database '$Database'"
            }
        }
    }

    # --- Step: run create_database.sql against the target database ---

    $sqlScriptPath = Join-Path -Path $PSScriptRoot -ChildPath "create_database.sql"

    if (-not (Test-Path -Path $sqlScriptPath -PathType Leaf)) {
        Write-Host "ERROR: Could not find schema script at '$sqlScriptPath'." -ForegroundColor Red
        $overallSuccess = $false
        $stepResults += "FAILED: Locate create_database.sql"
    }
    else {
        Write-Host ""
        Write-Host "Applying schema from '$sqlScriptPath' to database '$Database'..."

        & $psqlPath -h $HostName -p $Port -U $Username -d $Database -v ON_ERROR_STOP=1 -f "$sqlScriptPath"
        $schemaExitCode = $LASTEXITCODE

        if ($schemaExitCode -eq 0) {
            Write-Host "Schema applied successfully."
            $stepResults += "SUCCESS: Apply create_database.sql"
        }
        else {
            Write-Host "ERROR: Applying schema failed (exit code $schemaExitCode)." -ForegroundColor Red
            $overallSuccess = $false
            $stepResults += "FAILED: Apply create_database.sql"
        }
    }
}
finally {
    # Always clear PGPASSWORD so it doesn't linger in the process environment.
    if (Test-Path Env:\PGPASSWORD) {
        Remove-Item Env:\PGPASSWORD
    }
    $plainPassword = $null
}

# ------------------------------------------------------------------------
# Final summary
# ------------------------------------------------------------------------

Write-Host ""
Write-Host "=========================================="
Write-Host "Summary:"
foreach ($result in $stepResults) {
    Write-Host "  - $result"
}
Write-Host "=========================================="

if ($overallSuccess) {
    Write-Host "All steps completed successfully." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "One or more steps failed. See details above." -ForegroundColor Red
    exit 1
}
