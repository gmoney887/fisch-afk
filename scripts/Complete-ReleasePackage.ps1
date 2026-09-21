param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$') { throw 'Invalid release version.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$published = Join-Path $root "publish-release-$Version"
$staging = Join-Path $root "dist/FischMacroCS-v$Version-win-x64"
$archive = "$staging.zip"
if (!(Test-Path -LiteralPath "$staging/FischMacroCS.exe")) { throw 'Run the release helper with -SkipPush first.' }
# The legacy helper stages only shake_template.png. Preserve every published
# recognition asset, including the aquarium workflow templates.
Copy-Item -Path "$published/Assets/*" -Destination "$staging/Assets" -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "docs/RELEASE-v$Version.md") -Destination "$staging/README.md" -Force
Compress-Archive -Path "$staging/*" -DestinationPath $archive -CompressionLevel Optimal -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expected = @('FischMacroCS.exe', 'README.md', 'Assets/shake_template.png')
    $expected += Get-ChildItem -LiteralPath "$root/Assets/Workflows" -Filter '*.png' | ForEach-Object { "Assets/Workflows/$($_.Name)" }
    foreach ($name in $expected) {
        $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq $name })
        if ($entry.Count -ne 1 -or $entry[0].Length -eq 0) { throw "Missing or empty package entry: $name" }
        $inputStream = $entry[0].Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($inputStream)).Replace('-','') }
        finally { $inputStream.Dispose(); $sha.Dispose() }
        $source = if ($name -eq 'README.md') { Join-Path $root "docs/RELEASE-v$Version.md" } else { Join-Path $published $name }
        if ($actual -ne (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) { throw "Package hash mismatch: $name" }
    }
    $zip.Entries | Select-Object FullName,Length
} finally { $zip.Dispose() }
$versionInfo = (Get-Item -LiteralPath "$staging/FischMacroCS.exe").VersionInfo
if ($versionInfo.ProductVersion -notlike "$Version*") { throw 'Executable version does not match release.' }
Get-Item -LiteralPath $archive | Select-Object FullName,Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256
