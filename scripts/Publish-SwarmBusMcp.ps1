[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path $env:LOCALAPPDATA "MOBAdev\bin\AISwarmX.SwarmBus.Mcp")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "dashboard\src\AISwarmX.SwarmBus.Mcp\AISwarmX.SwarmBus.Mcp.csproj"
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "AISwarmX.SwarmBus.Mcp project not found at '$projectPath'."
}

$resolvedOut = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Path $resolvedOut -Force | Out-Null

Write-Host "Publishing AISwarmX.SwarmBus.Mcp ($Configuration) -> $resolvedOut"
& dotnet publish $projectPath -c $Configuration -o $resolvedOut --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $resolvedOut "AISwarmX.SwarmBus.Mcp.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Expected published executable missing at '$exePath'."
}

Write-Host "Published ready: $exePath"
