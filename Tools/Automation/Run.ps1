param([uint[]]$Seeds=@(1,7,42),[ValidateRange(1,420)][int]$Days=3,[ValidateSet('replay','trusted')][string]$Source='replay',[ValidateSet('baseline','paid-rest-auto')][string]$Strategy='baseline',[ValidateRange(180,86400)][int]$SecondsPerSeed=300,[string]$EditorPath,[ValidateNotNullOrEmpty()][string]$TestFilter='FitnessSimulator.Game.Tests.Automation.OfflineM1Tests',
 [switch]$RequireCleanSource,
 [ValidateRange(1,[long]::MaxValue)][long]$TraceMaxBytes=16777216,
 [ValidateRange(1,[long]::MaxValue)][long]$SnapshotMaxBytes=8388608,
 [ValidateRange(1,[long]::MaxValue)][long]$SnapshotSetMaxBytes=67108864,
 [ValidateRange(1,[long]::MaxValue)][long]$RunLogMaxBytes=16777216,
 [ValidateRange(1,[long]::MaxValue)][long]$RunMaxBytes=134217728)
$ErrorActionPreference='Stop'
if (!$Seeds.Count -or ($Seeds | Where-Object {$_ -eq 0})) { throw 'Seeds must be nonzero.' }
$names=@('FITNESS_OFFLINE_DAYS','FITNESS_OFFLINE_SECONDS','FITNESS_OFFLINE_OUTPUT','FITNESS_OFFLINE_BATCH','FITNESS_OFFLINE_SEEDS','FITNESS_OFFLINE_SOURCE','FITNESS_OFFLINE_STRATEGY')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name,$null)}
try {
 $env:FITNESS_OFFLINE_DAYS="$Days"
 $env:FITNESS_OFFLINE_SECONDS="$SecondsPerSeed"
 $env:FITNESS_OFFLINE_SEEDS=$Seeds -join ','
 $env:FITNESS_OFFLINE_SOURCE=$Source
 $env:FITNESS_OFFLINE_STRATEGY=$Strategy
 & (Join-Path (Split-Path $PSScriptRoot -Parent) 'Game.ps1') -Action test -EditorPath $EditorPath -TestFilter $TestFilter -RequireCleanSource:$RequireCleanSource -TraceMaxBytes $TraceMaxBytes -SnapshotMaxBytes $SnapshotMaxBytes -SnapshotSetMaxBytes $SnapshotSetMaxBytes -RunLogMaxBytes $RunLogMaxBytes -RunMaxBytes $RunMaxBytes
} finally { foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])} }

