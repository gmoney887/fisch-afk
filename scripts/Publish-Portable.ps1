$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootPrefix = $projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$stage = [IO.Path]::GetFullPath((Join-Path $projectRoot "publish-staging-$stamp"))
$current = [IO.Path]::GetFullPath((Join-Path $projectRoot 'publish-singlefile'))
$rollback = [IO.Path]::GetFullPath((Join-Path $projectRoot "publish-rollback-$stamp"))
foreach ($target in @($stage, $current, $rollback)) {
    if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish target escaped the project directory.' }
    if ((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Publish target is a filesystem link: $target"
    }
}
& dotnet publish (Join-Path $projectRoot 'FischMacroCS.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. The previous portable build is untouched.' }
if (-not (Test-Path -LiteralPath (Join-Path $stage 'FischMacroCS.exe'))) { throw 'Portable executable is missing.' }
if (Test-Path -LiteralPath $current) { Move-Item -LiteralPath $current -Destination $rollback }
try { Move-Item -LiteralPath $stage -Destination $current }
catch {
    if ((Test-Path -LiteralPath $rollback) -and -not (Test-Path -LiteralPath $current)) { Move-Item -LiteralPath $rollback -Destination $current }
    throw
}
Write-Output "Portable build: $current"
if (Test-Path -LiteralPath $rollback) { Write-Output "Previous build preserved: $rollback" }
Write-Output 'Local packaging only. No GitHub release was created.'
