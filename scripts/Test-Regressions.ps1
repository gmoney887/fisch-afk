param(
    [ValidateSet('Fast', 'Full')][string]$Tier = 'Fast',
    [string]$DotNet = "$env:USERPROFILE/.dotnet/dotnet.exe",
    [string]$ResultsDirectory = '',
    [switch]$ScopeOnly
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $baseline = '55e4357822050939a03b9baceab5f51027b2410a'
    $allowed = @('Core/AppDataPaths.cs', 'Core/FlightRecorder.cs', 'Core/IncidentEvidenceArchive.cs',
        'Core/IncidentFrameSelection.cs', 'Core/RecordingSubmissionService.cs', 'MainWindow.xaml.cs',
        'SubmitRecordingDialog.xaml.cs', 'FischMacroCS.csproj',
        'FischMacroCS.Tests/IncidentEvidenceArchiveTests.cs', 'FischMacroCS.Tests/RecorderArchiveStatusTests.cs',
        'FischMacroCS.Tests/DiagnosticsPreservationTests.cs', 'FischMacroCS.Tests/RegressionCatalog.json',
        'scripts/Test-Regressions.ps1', 'docs/DIAGNOSTICS-637.md', 'docs/WEBWEAVER-638.md',
        'Core/FishingEngine.cs','Vision/VisionProcessor.cs','Vision/PointedControlDetector.cs',
        'FischMacroCS.Tests/WebweaverRecordingTests.cs','FischMacroCS.Tests/WebweaverEngineReplayTests.cs',
        'FischMacroCS.Tests/ReelTimingInstrumentationTests.cs','FischMacroCS.Tests/FishingEngineReplayTests.cs',
        'FischMacroCS.Tests/Fixtures/PROVENANCE.md','scripts/Invoke-NativeTests.ps1','scripts/native-serial.runsettings')
    $changed = @(& git diff --name-only $baseline --) + @(& git ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify source scope.' }
    $fixturePaths=@('regular_914.png','boat_catch_25.png','styx_6376.png') | ForEach-Object { "FischMacroCS.Tests/Fixtures/$_" }
    $unexpected = @($changed | Where-Object { $_ -and $_ -notin $allowed -and $_ -notin $fixturePaths -and $_ -notlike 'Assets/ReelShapes/*' -and $_ -notlike 'FischMacroCS.Tests/Fixtures/webweaver/*' })
    if ($unexpected.Count) { throw "Changes outside the diagnostics scope: $($unexpected -join ', ')" }
    # The capture adapter adds scalar timing only.
    $original = (& git show "${baseline}:Core/FlightRecorder.cs") -join "`n"
    $current = ([IO.File]::ReadAllText((Join-Path $root 'Core/FlightRecorder.cs')) -replace "`r`n", "`n").TrimEnd("`n")
    $current=$current -replace '    public double LastCaptureLatencyMs \{ get; private set; \}\n','' -replace '            LastCaptureLatencyMs = captureMs;\n',''
    $marker = 'public sealed class RecordingFrameSource('
    if ($original.Substring($original.IndexOf($marker)) -cne $current.Substring($current.IndexOf($marker))) {
        throw 'RecordingFrameSource differs from AGY.'
    }
    $protected=@(& git diff --name-only $baseline -- 'Core/Reel*.cs' Core/GameplayInput.cs Core/Settings.cs Capture Native)
    if($protected.Count){throw "Controller/input source changed: $($protected -join ', ')"}
    $engineOriginal=(& git show "${baseline}:Core/FishingEngine.cs") -join "`n"
    $engineCurrent=Get-Content Core/FishingEngine.cs -Raw
    $engineCurrent=[regex]::Replace($engineCurrent,'(?s)// timing-only begin.*?// timing-only end','')
    foreach($pair in @(
        @('    private void SetMouseDown(bool down)','    private void Transition('),
        @('                if (GetElapsedMs(observationTicks) > 250)','                var telem = new TelemetryData')))
    {
        function Segment($text,$begin,$end){$start=$text.IndexOf($begin);$finish=$text.IndexOf($end,$start);if($start -lt 0 -or $finish -lt 0){throw 'Controller guard markers missing.'};return $text.Substring($start,$finish-$start) -replace '\s',''}
        if((Segment $engineOriginal $pair[0] $pair[1]) -cne (Segment $engineCurrent $pair[0] $pair[1])){throw "AGY controller/guarded input differs: $($pair[0])"}
    }
    $catalog = Get-Content 'FischMacroCS.Tests/RegressionCatalog.json' -Raw | ConvertFrom-Json
    foreach ($fixture in $catalog.Fixtures | Where-Object Required) {
        $path = Join-Path $root "FischMacroCS.Tests/Fixtures/$($fixture.Path)"
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fixture.SHA256) {
            throw "Missing or changed reviewed fixture: $($fixture.Path)"
        }
    }
    foreach($asset in $catalog.ReferenceAssets){if((Get-FileHash -LiteralPath (Join-Path $root $asset.Path)).Hash -ne $asset.SHA256){throw "Reference asset changed: $($asset.Path)"}}
    Write-Output "AGY controller and input guard passed; fixture/reference hashes checked. Tier: $Tier"
    if($ScopeOnly){return}
    if (!$ResultsDirectory) { $ResultsDirectory = Join-Path $root "TestResults/$Tier" }
    $filter=''
    if ($Tier -eq 'Fast') {
        $groups = @('DiagnosticsPreservationTests', 'IncidentEvidenceArchiveTests', 'RecorderArchiveStatusTests',
            'RecordingReplayTests', 'RecordingSubmissionTests', 'IncidentFrameSelectionTests', 'SessionEventJournalTests',
            'PreservedEvidenceBufferTests', 'ReportingUiTests', 'PrivateReportTests', 'WorkflowEvidenceTests',
            'TelemetryDispatchGateTests', 'GameplayInputTests', 'HotkeyDispatchGateTests', 'FishingEngineReplayTests',
            'MissingNeedleRecoveryTests', 'ReelTrackingRegressionTests', 'ReelPulseModulatorTests', 'ReelInputMotionTests',
            'ReelEntryGuardTests', 'ReelActuationPlanTests', 'Baseline', 'Seraphic625RecordingTests',
            'CatchVerificationTests', 'ChronologicalCatchTests', 'GlorpReelTests', 'RuinousOathTests',
            'NatesBladeTests', 'OptimizationRegressionTests', 'DimReelTests', 'MultiResolutionTests',
            'WebweaverRecordingTests','ReelTimingInstrumentationTests','DoomsekkarTests')
        $filter=($groups | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    }
    & (Join-Path $PSScriptRoot 'Invoke-NativeTests.ps1') -Run $Tier.ToLowerInvariant() -ResultsDirectory $ResultsDirectory -DotNet $DotNet -Filter $filter -Full:($Tier -eq 'Full')
} finally { Pop-Location }
