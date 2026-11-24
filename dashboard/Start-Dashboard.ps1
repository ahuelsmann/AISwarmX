<#
.SYNOPSIS
    Startet das lokale, strikt lesende MOBAdev Agent Network Dashboard (127.0.0.1).
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
$AutoStartTaskName = 'MOBAdev-Dashboard-AutoStart'
$ApiDir = Join-Path $DashboardRoot 'src\MobaDev.Dashboard.Api'
$ApiProject = Join-Path $ApiDir 'MobaDev.Dashboard.Api.csproj'
$ApiExe = Join-Path $ApiDir 'bin\Release\net10.0\MobaDev.Dashboard.Api.exe'
$WebRoot = Join-Path $DashboardRoot 'src\MobaDev.Dashboard.Web'
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

if ($RegisterAutoStart) {
    $argList = "-NoProfile -WindowStyle Hidden -File `"$ScriptPath`" -Background -Port $Port"
    $action = New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument $argList -WorkingDirectory $DashboardRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask `
        -TaskName $AutoStartTaskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Description 'Starts the read-only local MOBAdev Agent Swarm Gen#3 Dashboard (127.0.0.1:5179) detached at logon.' `
        -Force | Out-Null
    Write-Host "Registered Windows Scheduled Task '$AutoStartTaskName' (AtLogOn -> $BindUrl)." -ForegroundColor Green
    exit 0
}

if ($UnregisterAutoStart) {
    Unregister-ScheduledTask -TaskName $AutoStartTaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Unregistered Windows Scheduled Task '$AutoStartTaskName'." -ForegroundColor Yellow
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

if ($Background) {
    $healthUrl = "$BindUrl/api/health"
    if (Test-DashboardHealth -ProbeUrl $healthUrl) {
        Write-Host "MOBAdev Dashboard already running at $BindUrl" -ForegroundColor Green
        if ($OpenBrowser) {
            Start-Process $BindUrl
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

    if (-not (Test-Path $ApiExe)) {
        Write-Host 'Baue MOBAdev Dashboard API (Release)...' -ForegroundColor Cyan
        dotnet build $ApiProject --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build fehlgeschlagen.' }
    }

    $proc = Start-Process -FilePath $ApiExe -WorkingDirectory $ApiDir -WindowStyle Hidden -PassThru
    $healthy = $false
    for ($i = 0; $i -lt 30; $i++) {
        if (Test-DashboardHealth -ProbeUrl $healthUrl) {
            $healthy = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $healthy) {
        throw "MOBAdev Dashboard did not become healthy at $healthUrl within 15s (PID $($proc.Id))."
    }

    Write-Host "MOBAdev Dashboard started detached (PID $($proc.Id)) at $BindUrl" -ForegroundColor Green
    if ($OpenBrowser) {
        Start-Process $BindUrl
    }
    exit 0
}

if ($DevMode) {
    Write-Host ''
    Write-Host '=== MOBAdev Agent Network Dashboard (Dev-Modus · Nur Lesen) ===' -ForegroundColor Cyan
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
Write-Host '=== MOBAdev Agent Network Dashboard (Nur Lesen · Loopback) ===' -ForegroundColor Green
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
