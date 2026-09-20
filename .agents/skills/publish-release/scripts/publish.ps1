param (
    [string]$Version = "",
    [ValidateSet("None", "Patch", "Minor", "Major")]
    [string]$Bump = "None",
    [string]$ReleaseNotes = "",
    [switch]$SkipPush
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..\..\..").Path
Set-Location $repoRoot

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  Fat Dad's Fisch AFK Pro - Automated Release Engine   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Locate dotnet SDK (prefer user-installed SDK to avoid broken PATH shims)
$dotnet = ""
$userDotnet = "$HOME\.dotnet\dotnet.exe"
if (Test-Path $userDotnet) {
    $dotnet = $userDotnet
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnet = "dotnet"
} else {
    throw "Could not find dotnet CLI. Please ensure .NET SDK is installed."
}
Write-Host "      Using dotnet: $dotnet" -ForegroundColor DarkGray

# 2. Parse / Update Version in FischMacroCS.csproj
$csprojPath = Join-Path $repoRoot "FischMacroCS.csproj"
[xml]$csprojXml = Get-Content $csprojPath

$currentVerStr = $csprojXml.Project.PropertyGroup.Version
if (-not $currentVerStr) { $currentVerStr = "1.0.0" }

$targetVer = $currentVerStr

if ($Version -ne "") {
    $targetVer = $Version.TrimStart('v', 'V')
} elseif ($Bump -ne "None") {
    $parts = $currentVerStr.Split('.')
    [int]$major = if ($parts.Length -gt 0) { [int]$parts[0] } else { 1 }
    [int]$minor = if ($parts.Length -gt 1) { [int]$parts[1] } else { 0 }
    [int]$patch = if ($parts.Length -gt 2) { [int]$parts[2] } else { 0 }

    switch ($Bump) {
        "Major" { $major++; $minor = 0; $patch = 0 }
        "Minor" { $minor++; $patch = 0 }
        "Patch" { $patch++ }
    }
    $targetVer = "$major.$minor.$patch"
}

Write-Host "[1/7] Target Version: v$targetVer" -ForegroundColor Green

if ($targetVer -ne $currentVerStr) {
    Write-Host "      Updating FischMacroCS.csproj to $targetVer..." -ForegroundColor Yellow
    $assemblyVer = ($targetVer -split '-')[0] + '.0'
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$targetVer</Version>")
    $csprojContent = [regex]::Replace($csprojContent, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$assemblyVer</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, "<FileVersion>.*?</FileVersion>", "<FileVersion>$assemblyVer</FileVersion>")
    Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
}

# 3. Mandatory Pre-Flight Automated Regression Tests Gate
Write-Host "[2/7] Running Automated Regression & Vision Tests..." -ForegroundColor Green
& $dotnet test FischMacroCS.slnx -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Automated tests failed! Release aborted to prevent regressions from reaching users."
}
Write-Host "      All vision, geometry, and safety tests passed!" -ForegroundColor Cyan

# 4. Publish Single-File Standalone Portable Executable
Write-Host "[3/7] Compiling Standalone Portable Executable..." -ForegroundColor Green
$publishDir = Join-Path $repoRoot "publish-release-$targetVer"

# Publish into a version-specific directory without interrupting other running builds.
& $dotnet publish FischMacroCS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 5. Create Dist Staging & Zip Package
Write-Host "[4/7] Packaging Standalone Zero-Dependency ZIP..." -ForegroundColor Green
$distDir = Join-Path $repoRoot "dist"
$pkgName = "FischMacroCS-v$targetVer-win-x64"
$stagingDir = Join-Path $distDir $pkgName
$zipFile = Join-Path $distDir "$pkgName.zip"

$resolvedStage = [IO.Path]::GetFullPath($stagingDir)
$distPrefix = [IO.Path]::GetFullPath($distDir).TrimEnd('\') + '\'
if (!$resolvedStage.StartsWith($distPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid package staging path." }
if (Test-Path -LiteralPath $stagingDir) { throw "Package staging already exists; choose a new version or review it before retrying." }
New-Item -ItemType Directory -Force -Path "$stagingDir\Assets" | Out-Null

Copy-Item "$publishDir\FischMacroCS.exe" "$stagingDir\" -Force
if (Test-Path "$publishDir\Assets\shake_template.png") {
    Copy-Item "$publishDir\Assets\shake_template.png" "$stagingDir\Assets\" -Force
}

if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipFile -CompressionLevel Optimal

$zipInfo = Get-Item $zipFile
$zipSizeMb = [math]::Round($zipInfo.Length / 1MB, 1)
Write-Host "      Created package: $zipFile ($zipSizeMb MB)" -ForegroundColor Cyan

if ($SkipPush) { Write-Output "Verified local package: $zipFile"; return }

# 6. Git Commit & Tag
Write-Host "[5/7] Committing & Tagging in Git..." -ForegroundColor Green
$tag = "v$targetVer"

# Check if working copy has changes
$status = git status --porcelain
if ($status) {
    git add -A
    git commit -m "Release ${tag}: Automated release build"
}

# Update or create annotated tag
git tag -a $tag -m "Fat Dad's Fisch AFK Pro $tag"

# 7. Push & GitHub Release
if (-not $SkipPush) {
    Write-Host "[6/7] Pushing to GitHub (main branch & tags)..." -ForegroundColor Green
    git push origin main "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) {
        throw "Git push failed; release creation aborted."
    }

    Write-Host "[7/7] Publishing Release on GitHub..." -ForegroundColor Green
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $notesArg = if ($ReleaseNotes) { @("--notes", $ReleaseNotes) } else { @("--generate-notes") }
        
        # Check if release already exists
        $existing = (& gh release list --limit 50 | Select-String -Pattern "^\s*$tag\b")
        if ($existing) {
            Write-Host "      Updating existing release $tag..." -ForegroundColor Yellow
            & gh release upload $tag "$zipFile#FischMacroCS-v$targetVer-win-x64.zip" --clobber
        } else {
            Write-Host "      Creating new release $tag..." -ForegroundColor Green
            if ($ReleaseNotes) {
                & gh release create $tag $zipFile --title "Fat Dad's Fisch AFK Pro $tag" --notes $ReleaseNotes
            } else {
                & gh release create $tag $zipFile --title "Fat Dad's Fisch AFK Pro $tag" --generate-notes
            }
        }
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "========================================================" -ForegroundColor Green
            Write-Host "  SUCCESS! Release published and downloadable by friend:" -ForegroundColor Green
            Write-Host "  https://github.com/gmoney887/fisch-afk/releases/tag/$tag" -ForegroundColor Cyan
            Write-Host "========================================================" -ForegroundColor Green
            exit 0
        }
    }
    
    Write-Warning "gh CLI is not authenticated or encountered an error. Release zip is ready in dist/ for manual upload."
} else {
    Write-Host "[6/7] SkipPush specified: Skipping git push & GitHub release." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Local release complete: $zipFile" -ForegroundColor Green
