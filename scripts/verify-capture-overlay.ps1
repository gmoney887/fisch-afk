param([string]$Dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe")
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$checkPath = Join-Path $repoPath 'artifacts/capture-overlay-check'
New-Item -ItemType Directory -Path $checkPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'capture-overlay-check/Program.cs.txt') -Destination (Join-Path $checkPath 'Program.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'capture-overlay-check/CaptureCheck.csproj.template') -Destination (Join-Path $checkPath 'CaptureCheck.csproj') -Force
& $Dotnet run --project (Join-Path $checkPath 'CaptureCheck.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Windows capture/overlay integration check failed.' }
