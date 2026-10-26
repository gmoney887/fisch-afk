param(
    [ValidateSet('Fast', 'Full')][string]$Tier = 'Fast',
    [string]$DotNet = "$env:USERPROFILE/.dotnet/dotnet.exe",
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $baseline = '4f96f456b4f9a8f9f349780f2e6dc4936d5d4912'
    $allowed = @('Core/AppDataPaths.cs', 'Core/FlightRecorder.cs', 'Core/IncidentEvidenceArchive.cs',
        'Core/IncidentFrameSelection.cs', 'Core/RecordingSubmissionService.cs', 'MainWindow.xaml.cs',
        'SubmitRecordingDialog.xaml.cs', 'FischMacroCS.csproj',
        'FischMacroCS.Tests/IncidentEvidenceArchiveTests.cs', 'FischMacroCS.Tests/RecorderArchiveStatusTests.cs',
        'FischMacroCS.Tests/DiagnosticsPreservationTests.cs', 'FischMacroCS.Tests/RegressionCatalog.json',
        'scripts/Test-Regressions.ps1', 'docs/DIAGNOSTICS-637.md')
    $changed = @(& git diff --name-only $baseline --) + @(& git ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify source scope.' }
    $unexpected = @($changed | Where-Object { $_ -and $_ -notin $allowed })
    if ($unexpected.Count) { throw "Changes outside the diagnostics scope: $($unexpected -join ', ')" }
    # This capture adapter shares the recorder file but must remain exact AGY.
    $original = (& git show "${baseline}:Core/FlightRecorder.cs") -join "`n"
    $current = ([IO.File]::ReadAllText((Join-Path $root 'Core/FlightRecorder.cs')) -replace "`r`n", "`n").TrimEnd("`n")
    $marker = 'public sealed class RecordingFrameSource('
    if ($original.Substring($original.IndexOf($marker)) -cne $current.Substring($current.IndexOf($marker))) {
        throw 'RecordingFrameSource differs from AGY.'
    }
    $catalog = Get-Content 'FischMacroCS.Tests/RegressionCatalog.json' -Raw | ConvertFrom-Json
    foreach ($fixture in $catalog.Fixtures | Where-Object Required) {
        $path = Join-Path $root "FischMacroCS.Tests/Fixtures/$($fixture.Path)"
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fixture.SHA256) {
            throw "Missing or changed reviewed fixture: $($fixture.Path)"
        }
    }
    Write-Output "AGY scope guard passed; reeling, vision, capture adapter and gameplay input unchanged. Tier: $Tier"
    if (!$ResultsDirectory) { $ResultsDirectory = Join-Path $root "TestResults/$Tier" }
    $testArgs = @('test', 'FischMacroCS.slnx', '-c', 'Release', '--results-directory', $ResultsDirectory,
        '--logger', "trx;LogFileName=$($Tier.ToLowerInvariant()).trx", '--logger', 'console;verbosity=normal')
    if ($Tier -eq 'Fast') {
        $groups = @('DiagnosticsPreservationTests', 'IncidentEvidenceArchiveTests', 'RecorderArchiveStatusTests',
            'RecordingReplayTests', 'RecordingSubmissionTests', 'IncidentFrameSelectionTests', 'SessionEventJournalTests',
            'PreservedEvidenceBufferTests', 'ReportingUiTests', 'PrivateReportTests', 'WorkflowEvidenceTests',
            'TelemetryDispatchGateTests', 'GameplayInputTests', 'HotkeyDispatchGateTests', 'FishingEngineReplayTests',
            'MissingNeedleRecoveryTests', 'ReelTrackingRegressionTests', 'ReelPulseModulatorTests', 'ReelInputMotionTests',
            'ReelEntryGuardTests', 'ReelActuationPlanTests', 'Baseline', 'Seraphic625RecordingTests',
            'CatchVerificationTests', 'ChronologicalCatchTests', 'GlorpReelTests', 'RuinousOathTests',
            'NatesBladeTests', 'OptimizationRegressionTests', 'DimReelTests', 'MultiResolutionTests')
        $testArgs += @('--filter', (($groups | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'))
    }
    & $DotNet @testArgs
    if ($LASTEXITCODE -ne 0) { throw "$Tier regression gate failed ($LASTEXITCODE)." }
} finally { Pop-Location }
