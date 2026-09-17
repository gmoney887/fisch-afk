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

# 1. Locate dotnet SDK
$dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $userDotnet = "$HOME\.dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnet = $userDotnet
    } else {
        throw "Could not find dotnet CLI. Please ensure .NET SDK is installed."
    }
}

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

Write-Host "[1/6] Target Version: v$targetVer" -ForegroundColor Green

if ($targetVer -ne $currentVerStr) {
    Write-Host "      Updating FischMacroCS.csproj to $targetVer..." -ForegroundColor Yellow
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = [regex]::Replace($csprojContent, "<Version>.*?</Version>", "<Version>$targetVer</Version>")
    $csprojContent = [regex]::Replace($csprojContent, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$targetVer.0</AssemblyVersion>")
    $csprojContent = [regex]::Replace($csprojContent, "<FileVersion>.*?</FileVersion>", "<FileVersion>$targetVer.0</FileVersion>")
    Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
}

# 3. Publish Single-File Standalone Portable Executable
Write-Host "[2/6] Compiling Standalone Portable Executable..." -ForegroundColor Green
$publishDir = Join-Path $repoRoot "publish-singlefile"

& $dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 4. Create Dist Staging & Zip Package
Write-Host "[3/6] Packaging Standalone Zero-Dependency ZIP..." -ForegroundColor Green
$distDir = Join-Path $repoRoot "dist"
$pkgName = "FischMacroCS-v$targetVer-win-x64"
$stagingDir = Join-Path $distDir $pkgName
$zipFile = Join-Path $distDir "$pkgName.zip"

if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
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

# 5. Git Commit & Tag
Write-Host "[4/6] Committing & Tagging in Git..." -ForegroundColor Green
$tag = "v$targetVer"

# Check if working copy has changes
$status = git status --porcelain
if ($status) {
    git add -A
    git commit -m "Release ${tag}: Automated release build"
}

# Update or create annotated tag
git tag -f -a $tag -m "Fat Dad's Fisch AFK Pro $tag"

# 6. Push & GitHub Release
if (-not $SkipPush) {
    Write-Host "[5/6] Pushing to GitHub (main branch & tags)..." -ForegroundColor Green
    git push origin main --tags -f
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Git push returned a non-zero exit code. Please check your GitHub remote credentials."
    }

    Write-Host "[6/6] Publishing Release on GitHub..." -ForegroundColor Green
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $notesArg = if ($ReleaseNotes) { @("--notes", $ReleaseNotes) } else { @("--generate-notes") }
        
        # Check if release already exists
        $existing = (& gh release list --limit 50 | Select-String -Pattern "^\s*$tag\b")
        if ($existing) {
            Write-Host "      Updating existing release $tag..." -ForegroundColor Yellow
            & gh release upload $tag "$zipFile#FischMacroCS-v$targetVer-win-x64.zip" --clobber
        } else {
            Write-Host "      Creating new release $tag..." -ForegroundColor Green
            & gh release create $tag $zipFile --title "Fat Dad's Fisch AFK Pro $tag" @notesArg
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
    Write-Host "[5/6] SkipPush specified: Skipping git push & GitHub release." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Local release complete: $zipFile" -ForegroundColor Green
