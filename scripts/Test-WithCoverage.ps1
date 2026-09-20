param(
    [string]$DotNet = 'dotnet',
    [string]$CoverageFile,
    [string]$ResultsDirectory = 'artifacts/coverage',
    [double]$MinimumLinePercent = 40,
    [double]$MinimumBranchPercent = 34
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    if (!$CoverageFile) {
        $run = Join-Path $ResultsDirectory ([Guid]::NewGuid().ToString('N'))
        & $DotNet test FischMacroCS.slnx -c Release '--collect:XPlat Code Coverage' --logger 'trx;LogFileName=tests.trx' --results-directory $run
        if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
        # TRX may copy the same attachment under In/<host>; deduplicate identical content.
        $reports = @(Get-ChildItem -LiteralPath $run -Filter coverage.cobertura.xml -Recurse |
            Get-FileHash | Group-Object Hash | ForEach-Object { Get-Item -LiteralPath $_.Group[0].Path })
        if ($reports.Count -ne 1) { throw "Expected one production coverage report; found $($reports.Count)." }
        $CoverageFile = $reports[0].FullName
    }
    [xml]$report = Get-Content -LiteralPath $CoverageFile -Raw
    $line = 100.0 * [double]$report.coverage.'line-rate'
    $branch = 100.0 * [double]$report.coverage.'branch-rate'
    # Group ALL classes by filename, including compiler-generated async state machines.
    # Merge duplicate line entries rather than reporting only the outer class.
    $files = foreach ($group in ($report.coverage.packages.package.classes.class | Group-Object filename)) {
        $covered = 0; $total = 0; $branches = 0; $hitBranches = 0
        foreach ($sourceLine in ($group.Group.lines.line | Group-Object number)) {
            $total++
            if (@($sourceLine.Group | Where-Object { [int]$_.hits -gt 0 }).Count -gt 0) { $covered++ }
            $branchRows = @($sourceLine.Group | Where-Object { $_.branch -eq 'true' })
            $bestTotal = 0; $bestHit = 0
            foreach ($row in $branchRows) {
                if ($row.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                    $bestHit = [Math]::Max($bestHit, [int]$Matches[1])
                    $bestTotal = [Math]::Max($bestTotal, [int]$Matches[2])
                }
            }
            $branches += $bestTotal; $hitBranches += $bestHit
        }
        [pscustomobject]@{ File = $group.Name; LinesCovered = $covered; Lines = $total;
            LinePercent = if ($total) { [Math]::Round(100.0*$covered/$total, 2) } else { 0 };
            BranchesCovered = $hitBranches; Branches = $branches;
            BranchPercent = if ($branches) { [Math]::Round(100.0*$hitBranches/$branches, 2) } else { $null } }
    }
    $reportPath = (Resolve-Path -LiteralPath $CoverageFile).Path
    $reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash
    $generatedUtc = [DateTime]::UtcNow.ToString('o')
    $relativeReport = [IO.Path]::GetRelativePath($repo, $reportPath).Replace('\', '/')
    $summary = [pscustomobject]@{ GeneratedUtc = $generatedUtc; CoverageReport = $relativeReport;
        CoverageReportSha256 = $reportHash;
        LinePercent = [Math]::Round($line, 2); BranchPercent = [Math]::Round($branch, 2);
        LinesCovered = [int]$report.coverage.'lines-covered'; Lines = [int]$report.coverage.'lines-valid';
        BranchesCovered = [int]$report.coverage.'branches-covered'; Branches = [int]$report.coverage.'branches-valid';
        Files = @($files | Sort-Object File) }
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.json') -Encoding utf8
    $markdown = @('# Automated coverage', '', ('Overall: {0:N2}% lines, {1:N2}% branches.' -f $line, $branch),
        '', "Summary generated UTC: $generatedUtc", "Source report (repository-relative): $relativeReport",
        "Source report SHA256: $reportHash",
        '', 'These are regression floors, not AFK acceptance. Live input, focus, server updates and idle behavior require separate evidence.',
        '', '| Source file (async code included) | Lines | Branches |', '|---|---:|---:|')
    $markdown += $files | Sort-Object File | ForEach-Object {
        $branchText = if ($null -eq $_.BranchPercent) { 'n/a' } else { "$($_.BranchPercent)%" }
        "| $($_.File) | $($_.LinePercent)% | $branchText |"
    }
    $markdown | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.md') -Encoding utf8
    Write-Output ('Coverage: {0:N2}% lines, {1:N2}% branches' -f $line, $branch)
    if ($line -lt $MinimumLinePercent -or $branch -lt $MinimumBranchPercent) {
        throw "Coverage fell below $MinimumLinePercent% line / $MinimumBranchPercent% branch regression floors."
    }
    $engine = @($files | Where-Object { ($_.File -replace '\\', '/') -match '(^|/)Core/FishingEngine.cs$' })
    if ($engine.Count -ne 1 -or $engine[0].LinePercent -lt 45 -or $engine[0].BranchPercent -lt 35) {
        throw 'FishingEngine must retain at least 45% line / 35% branch coverage from worker replay scenarios.'
    }
} finally { Pop-Location }
