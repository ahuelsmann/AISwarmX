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
    [int]$Port = 5179
)

$ErrorActionPreference = 'Stop'

$DashboardRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApiProject = Join-Path $DashboardRoot 'src\MobaDev.Dashboard.Api\MobaDev.Dashboard.Api.csproj'
$WebRoot = Join-Path $DashboardRoot 'src\MobaDev.Dashboard.Web'
$WebDistIndex = Join-Path $WebRoot 'dist\index.html'
$BindUrl = "http://127.0.0.1:$Port"

if ($Port -lt 1024 -or $Port -gt 65535) {
    throw "Ungültiger Port $Port. Bitte einen Port zwischen 1024 und 65535 wählen."
}

if (-not (Test-Path $ApiProject)) {
    throw "Backend-Projekt nicht gefunden: $ApiProject"
}

$env:Dashboard__BindUrl = $BindUrl

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

    try {
        if ($OpenBrowser) {
            Start-Process 'http://127.0.0.1:5173'
        }
        dotnet run --project $ApiProject --configuration Release --no-launch-profile
    }
    finally {
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
Write-Host 'Zum Beenden Strg+C drücken.'
Write-Host ''

if ($OpenBrowser) {
    Start-Process $BindUrl
}

dotnet run --project $ApiProject --configuration Release --no-launch-profile
