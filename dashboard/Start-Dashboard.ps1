<#
.SYNOPSIS
    Startet das lokale, strikt lesende AISwarmX Agent Network Dashboard (127.0.0.1).
.DESCRIPTION
    Startet die ASP.NET Core Minimal API (.NET 10) und liefert das React-Frontend lokal unter
    http://127.0.0.1:<Port> aus. Im -DevMode wird zusätzlich der Vite Dev-Server auf Port 5173 gestartet.
#>
[CmdletBinding()]
param(
    [switch]$DevMode,
    [switch]$RebuildFrontend,
    [switch]$OpenBrowser,
    [switch]$Background,
    [switch]$RegisterAutoStart,
    [switch]$UnregisterAutoStart,
    [int]$Port = 5179
)

$ErrorActionPreference = 'Stop'

$DashboardRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ScriptPath = $MyInvocation.MyCommand.Path
$AutoStartTaskName = 'AISwarmX-Dashboard-AutoStart'
$LegacyAutoStartTaskName = 'MOBAdev-Dashboard-AutoStart'
$ApiDir = Join-Path $DashboardRoot 'src\AISwarmX.Dashboard.Api'
$ApiProject = Join-Path $ApiDir 'AISwarmX.Dashboard.Api.csproj'
$ApiExe = Join-Path $ApiDir 'bin\Release\net10.0\AISwarmX.Dashboard.Api.exe'
$WebRoot = Join-Path $DashboardRoot 'src\AISwarmX.Dashboard.Web'
$WebDistIndex = Join-Path $WebRoot 'dist\index.html'
$BindUrl = "http://127.0.0.1:$Port"

if ($Port -lt 1024 -or $Port -gt 65535) {
    throw "Ungültiger Port $Port. Bitte einen Port zwischen 1024 und 65535 wählen."
}

function Test-DashboardHealth {
    param([string]$ProbeUrl)
    try {
        $res = Invoke-WebRequest -Uri $ProbeUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        return ($res.StatusCode -ge 200 -and $res.StatusCode -lt 400)
    }
    catch {
        return $false
    }
}

function Ensure-DashboardBuilt {
    if ($RebuildFrontend -or -not (Test-Path $WebDistIndex)) {
        Write-Host 'Baue React-Frontend für Produktion...' -ForegroundColor Cyan
        if (-not (Test-Path (Join-Path $WebRoot 'node_modules'))) {
            npm --prefix $WebRoot install
            if ($LASTEXITCODE -ne 0) { throw 'npm install fehlgeschlagen.' }
        }

        npm --prefix $WebRoot run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend-Build fehlgeschlagen.' }
    }

    if (-not (Test-Path $ApiExe)) {
        Write-Host 'Baue AISwarmX Dashboard API (Release)...' -ForegroundColor Cyan
        dotnet build $ApiProject --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build fehlgeschlagen.' }
    }
}

function Find-ScheduledTask {
    param([string]$TaskName)
    try {
        return (Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop)
    }
    catch {
        $isNotFound = ($_.CategoryInfo.Category -eq [System.Management.Automation.ErrorCategory]::ObjectNotFound) -or
            ($_.FullyQualifiedErrorId -like 'CmdletizationQuery_NotFound*') -or
            ($_.Exception -is [System.IO.FileNotFoundException])
        if ($isNotFound) {
            return $null
        }
        throw
    }
}

function Remove-VerifiedScheduledTask {
    param([string]$TaskName)
    if ($null -eq (Find-ScheduledTask -TaskName $TaskName)) {
        return $false
    }

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
    if ($null -ne (Find-ScheduledTask -TaskName $TaskName)) {
        throw "Scheduled Task '$TaskName' still exists after Unregister-ScheduledTask."
    }

    return $true
}

function Register-DashboardAutoStartTask {
    $hadNewTask = $null -ne (Find-ScheduledTask -TaskName $AutoStartTaskName)
    $action = New-ScheduledTaskAction -Execute $ApiExe -Argument "--urls=$BindUrl" -WorkingDirectory $ApiDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask `
        -TaskName $AutoStartTaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Description 'Starts the read-only local AISwarmX Agent Network Dashboard (127.0.0.1:5179) detached at logon.' `
        -Force `
        -ErrorAction Stop | Out-Null

    try {
        if (Remove-VerifiedScheduledTask -TaskName $LegacyAutoStartTaskName) {
            Write-Host "Migrated legacy task '$LegacyAutoStartTaskName' to '$AutoStartTaskName'." -ForegroundColor Yellow
        }
    }
    catch {
        $removeEx = $_.Exception
        if (-not $hadNewTask) {
            try {
                [void](Remove-VerifiedScheduledTask -TaskName $AutoStartTaskName)
            }
            catch {
                throw [System.AggregateException]::new(
                    "Failed to remove legacy task '$LegacyAutoStartTaskName' ($($removeEx.Message)) AND failed to roll back '$AutoStartTaskName' ($($_.Exception.Message)).",
                    @($removeEx, $_.Exception))
            }
        }
        throw
    }
}

function Migrate-LegacyAutoStartIfPresent {
    if ($Port -ne 5179) {
        return
    }

    $legacyTask = Find-ScheduledTask -TaskName $LegacyAutoStartTaskName
    if ($null -eq $legacyTask) {
        return
    }

    $newTask = Find-ScheduledTask -TaskName $AutoStartTaskName
    if ($null -eq $newTask) {
        Ensure-DashboardBuilt
        Register-DashboardAutoStartTask
    }
    elseif (Remove-VerifiedScheduledTask -TaskName $LegacyAutoStartTaskName) {
        Write-Host "Removed duplicate legacy task '$LegacyAutoStartTaskName'." -ForegroundColor Yellow
    }
}

if ($RegisterAutoStart) {
    Ensure-DashboardBuilt
    Register-DashboardAutoStartTask
    Write-Host "Registered Windows Scheduled Task '$AutoStartTaskName' (AtLogOn -> $BindUrl)." -ForegroundColor Green
    exit 0
}

if ($UnregisterAutoStart) {
    foreach ($taskName in @($AutoStartTaskName, $LegacyAutoStartTaskName)) {
        if (Remove-VerifiedScheduledTask -TaskName $taskName) {
            Write-Host "Unregistered Windows Scheduled Task '$taskName'." -ForegroundColor Yellow
        }
    }
    exit 0
}

if (-not (Test-Path $ApiProject)) {
    throw "Backend-Projekt nicht gefunden: $ApiProject"
}

function Start-BrowserWhenReady {
    param(
        [string]$ProbeUrl,
        [string]$TargetUrl
    )

    return Start-Job -ScriptBlock {
        param($Probe, $Target)
        for ($i = 0; $i -lt 90; $i++) {
            try {
                $res = Invoke-WebRequest -Uri $Probe -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
                if ($res.StatusCode -ge 200 -and $res.StatusCode -lt 400) {
                    Start-Process $Target
                    return
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
    } -ArgumentList $ProbeUrl, $TargetUrl
}

$env:Dashboard__BindUrl = $BindUrl

function Stop-LegacyDashboardForTarget {
    $legacyApiRoot = [System.IO.Path]::GetFullPath((Join-Path $DashboardRoot 'src\MobaDev.Dashboard.Api'))
    $legacyApiDirPrefix = $legacyApiRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $legacyProcs = @(Get-Process -Name 'MobaDev.Dashboard.Api' -ErrorAction SilentlyContinue)
    if ($legacyProcs.Count -eq 0) {
        return
    }

    $listenerPids = @(
        Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
    )
    foreach ($proc in $legacyProcs) {
        if ($listenerPids -notcontains $proc.Id) {
            continue
        }

        $procPath = $null
        try {
            $procPath = $proc.Path
        }
        catch {
            $procPath = $null
        }

        if ([string]::IsNullOrWhiteSpace($procPath)) {
            continue
        }

        $fullProcPath = [System.IO.Path]::GetFullPath($procPath)
        if (-not $fullProcPath.StartsWith($legacyApiDirPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Stop-Process -Id $proc.Id -Force -ErrorAction Stop
        Write-Host "Stopped verified legacy MobaDev.Dashboard.Api (PID=$($proc.Id)) on port $Port." -ForegroundColor Yellow
    }
}

if ($Background) {
    Migrate-LegacyAutoStartIfPresent
    Stop-LegacyDashboardForTarget

    $healthUrl = "$BindUrl/api/health"
    if (Test-DashboardHealth -ProbeUrl $healthUrl) {
        Write-Host "AISwarmX Dashboard already running at $BindUrl" -ForegroundColor Green
        if ($OpenBrowser) {
            Start-Process $BindUrl
        }
        exit 0
    }

    Ensure-DashboardBuilt

    $launchedVia = 'Start-Process'
    $task = if ($Port -eq 5179) {
        Find-ScheduledTask -TaskName $AutoStartTaskName
    } else {
        $null
    }

    if ($null -ne $task) {
        Start-ScheduledTask -TaskName $AutoStartTaskName
        $launchedVia = "ScheduledTask:$AutoStartTaskName"
    }
    else {
        try {
            $cimResult = Invoke-CimMethod `
                -ClassName Win32_Process `
                -MethodName Create `
                -Arguments @{ CommandLine = "`"$ApiExe`" --urls=$BindUrl"; CurrentDirectory = $ApiDir } `
                -ErrorAction Stop
            if ($cimResult.ReturnValue -eq 0) {
                $launchedVia = "Win32_Process(PID=$($cimResult.ProcessId))"
            }
            else {
                Start-Process -FilePath $ApiExe -ArgumentList "--urls=$BindUrl" -WorkingDirectory $ApiDir -WindowStyle Hidden | Out-Null
            }
        }
        catch {
            Start-Process -FilePath $ApiExe -ArgumentList "--urls=$BindUrl" -WorkingDirectory $ApiDir -WindowStyle Hidden | Out-Null
        }
    }

    $healthy = $false
    for ($i = 0; $i -lt 30; $i++) {
        if (Test-DashboardHealth -ProbeUrl $healthUrl) {
            $healthy = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $healthy) {
        throw "AISwarmX Dashboard did not become healthy at $healthUrl within 15s (via $launchedVia)."
    }

    Write-Host "AISwarmX Dashboard started detached ($launchedVia) at $BindUrl" -ForegroundColor Green
    if ($OpenBrowser) {
        Start-Process $BindUrl
    }
    exit 0
}

if ($DevMode) {
    Write-Host ''
    Write-Host '=== AISwarmX Agent Network Dashboard (Dev-Modus · Nur Lesen) ===' -ForegroundColor Cyan
    Write-Host "API:      $BindUrl"
    Write-Host 'Frontend: http://127.0.0.1:5173 (Proxy -> API)'
    Write-Host ''

    if (-not (Test-Path (Join-Path $WebRoot 'node_modules'))) {
        Write-Host 'Installiere Frontend-Abhängigkeiten (npm install)...' -ForegroundColor Yellow
        npm --prefix $WebRoot install
        if ($LASTEXITCODE -ne 0) { throw 'npm install fehlgeschlagen.' }
    }

    $viteJob = Start-Job -ScriptBlock {
        param($Path)
        npm --prefix $Path run dev
    } -ArgumentList $WebRoot

    $browserJob = $null
    try {
        if ($OpenBrowser) {
            $browserJob = Start-BrowserWhenReady -ProbeUrl "$BindUrl/api/health" -TargetUrl 'http://127.0.0.1:5173'
        }
        dotnet run --project $ApiProject --configuration Release --no-launch-profile
    }
    finally {
        if ($browserJob) {
            Stop-Job $browserJob -ErrorAction SilentlyContinue
            Remove-Job $browserJob -Force -ErrorAction SilentlyContinue
        }
        Stop-Job $viteJob -ErrorAction SilentlyContinue
        Remove-Job $viteJob -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

if ($RebuildFrontend -or -not (Test-Path $WebDistIndex)) {
    Write-Host 'Baue React-Frontend für Produktion...' -ForegroundColor Cyan
    if (-not (Test-Path (Join-Path $WebRoot 'node_modules'))) {
        npm --prefix $WebRoot install
        if ($LASTEXITCODE -ne 0) { throw 'npm install fehlgeschlagen.' }
    }

    npm --prefix $WebRoot run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend-Build fehlgeschlagen.' }
}

Write-Host ''
Write-Host '=== AISwarmX Agent Network Dashboard (Nur Lesen · Loopback) ===' -ForegroundColor Green
Write-Host "URL: $BindUrl"
Write-Host 'Warte auf Kestrel-Start; Browser öffnet automatisch sobald bereit.' -ForegroundColor DarkGray
Write-Host 'Zum Beenden Strg+C drücken.'
Write-Host ''

$browserJob = $null
try {
    if ($OpenBrowser) {
        $browserJob = Start-BrowserWhenReady -ProbeUrl "$BindUrl/api/health" -TargetUrl $BindUrl
    }
    dotnet run --project $ApiProject --configuration Release --no-launch-profile
}
finally {
    if ($browserJob) {
        Stop-Job $browserJob -ErrorAction SilentlyContinue
        Remove-Job $browserJob -Force -ErrorAction SilentlyContinue
    }
}
