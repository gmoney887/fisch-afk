param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$SessionDirectory,
    [string]$OutputFile = 'artifacts/live-review/regression-trial.jsonl'
)
$ErrorActionPreference = 'Stop'
$process = Get-Process -Id $ProcessId
$manifest = Get-Content -LiteralPath (Join-Path $SessionDirectory 'manifest.json') -Raw | ConvertFrom-Json
$rows = @(Get-ChildItem -LiteralPath $SessionDirectory -Filter 'events*.jsonl' | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Tail 2000 | ForEach-Object {
        # The writer can still be appending the final line. Only complete JSON rows are evidence.
        try { $_ | ConvertFrom-Json -ErrorAction Stop } catch { }
    }
} | Where-Object { $null -ne $_.Timestamp } | Sort-Object Timestamp)
$decision = $rows | Where-Object Kind -eq 'decision' | Select-Object -Last 1
$outcome = $rows | Where-Object Kind -eq 'outcome' | Select-Object -Last 1
$latencies = @($rows | Where-Object Kind -eq 'decision' | ForEach-Object { [double]$_.Data.LoopLatencyMs } | Sort-Object)
$files = @(Get-ChildItem -LiteralPath $SessionDirectory -File)
$sample = [ordered]@{
    ObservedUtc = [DateTime]::UtcNow.ToString('o')
    ProcessId = $process.Id
    ProcessStartedUtc = $process.StartTime.ToUniversalTime().ToString('o')
    Executable = $process.Path
    SessionId = $manifest.SessionId
    SessionStartedUtc = $manifest.StartedUtc
    Completed = Test-Path -LiteralPath (Join-Path $SessionDirectory 'completed.json')
    PrivateBytes = $process.PrivateMemorySize64
    WorkingSetBytes = $process.WorkingSet64
    CpuSeconds = $process.TotalProcessorTime.TotalSeconds
    StoredBytes = ($files | Measure-Object Length -Sum).Sum
    RetainedImages = @($files | Where-Object Extension -eq '.png').Count
    LatestEventSeconds = if ($rows.Count) { ([double]$rows[-1].Timestamp - [double]$manifest.MonotonicStart) / [double]$manifest.TimestampFrequency } else { $null }
    LatestDecision = $decision.Data
    LatestOutcome = $outcome.Data
    DecisionSampleCount = $latencies.Count
    SampledLoopP95Ms = if ($latencies.Count) { $latencies[[Math]::Max(0, [Math]::Ceiling($latencies.Count * .95) - 1)] } else { $null }
    SamplingNote = 'Up to 2000 complete rows per retained journal segment; latency is a recent sample, not whole-session acceptance. Outcomes are application counters, not independent catch labels.'
}
$outputPath = [IO.Path]::GetFullPath($OutputFile)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$sample | ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath $outputPath
$sample | ConvertTo-Json -Depth 8
