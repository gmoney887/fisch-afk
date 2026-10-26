param([string]$Filter='', [string]$Run='focused', [switch]$Full, [string]$ResultsDirectory='', [string]$DotNet='C:/Users/garre/.dotnet/dotnet.exe')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$ResultsDirectory){$ResultsDirectory=Join-Path $root ('TestResults/'+$Run)}
$ResultsDirectory=[IO.Path]::GetFullPath($ResultsDirectory)
$runRoot=Split-Path -Parent $ResultsDirectory
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$active=@(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -match '^(testhost|vstest.*|MSBuild)\.exe$' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match '\b(test|vstest\.console\.dll|MSBuild\.dll)\b')
})
if($active.Count){ $active | Select-Object ProcessId,CommandLine | Format-List; throw 'Another native build/test is active; defer this run.' }
$collision=$ResultsDirectory+'.collision.json'
if(Test-Path -LiteralPath $collision){throw 'Preserve previous collision report and choose a unique run name.'}
$watch=Start-ThreadJob -ArgumentList $root,$collision,$ResultsDirectory -ScriptBlock {
    param($ownedRoot,$flag,$ownedResults)
    while($true){
        $hosts=@(Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe'")
        $other=@($hosts | Where-Object { $_.ExecutablePath -and !$_.ExecutablePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase) })
        if($other.Count){
            $other | Select-Object ProcessId,ExecutablePath | ConvertTo-Json | Set-Content -LiteralPath $flag
            # A collision during compilation must also cancel this invocation
            # before it can launch another host. Only our unique results path
            # identifies the owned SDK root; compilation is not shared.
            Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
                $_.CommandLine -match '\s+test\s+' -and $_.CommandLine.Contains($ownedResults,[StringComparison]::OrdinalIgnoreCase)
            } | ForEach-Object { try{[Diagnostics.Process]::GetProcessById($_.ProcessId).Kill($true)}catch{} }
            # Never clean up by executable name: only stop our exact owned host.
            $hosts | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase) } |
                ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
            return
        }
        Start-Sleep -Seconds 2
    }
}
Push-Location $root
try{
    $testArgs=@('test','FischMacroCS.slnx','-c','Release','--settings',(Join-Path $PSScriptRoot 'native-serial.runsettings'),
      '-p:UseSharedCompilation=false','--results-directory',$ResultsDirectory,'--logger',"trx;LogFileName=$Run.trx",'--logger','console;verbosity=normal')
    if(!$Full){if(!$Filter){throw 'Focused runs require a filter.'};$testArgs+=@('--filter',$Filter)}
    & $DotNet @testArgs 2>&1 | Tee-Object -FilePath ($ResultsDirectory+'.log')
    $code=$LASTEXITCODE
    if(Test-Path -LiteralPath $collision){throw 'AGY test overlap detected; only our runner was stopped. Reschedule the gate.'}
    if($code -ne 0){throw "Tests failed ($code); results retained."}
}finally{Pop-Location;Stop-Job $watch;Receive-Job $watch -ErrorAction Continue;Remove-Job $watch}
