param([string]$OutputRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'TestResults/Lightweight/LITE-20261024/independent-maintenance'),
    [string]$MaintenanceShell = (Get-Process -Id $PID).Path)
$ErrorActionPreference = 'Stop'
$implementation = Join-Path $PSScriptRoot 'Maintain-Evidence.ps1'
$runRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$cases = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed) {
    $cases.Add([ordered]@{ name=$Name; passed=$Passed })
}
function Write-FixtureFile([string]$Path, [string]$Value) {
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Value)
}
function New-Fixture([string]$Name) {
    $fixture = Join-Path $runRoot $Name
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/a-source-copy/one.cs') 'same historical contents'
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/b-source-copy/one.cs') 'same historical contents'
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/b-source-copy/variant.cs') 'different historical version'
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/a-source-copy/empty.cs') ''
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/b-source-copy/empty.cs') ''
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/a-source-copy/state.bin') 'same historical contents'
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/seal-manifest.json') '{"historical":"preserve exact bytes"}'
    Write-FixtureFile (Join-Path $fixture 'TestResults/Old/history.lock') 'historical lock must be excluded'
    Write-FixtureFile (Join-Path $fixture 'TestResults/IsolatedProjects/Old/Library/cache') 'cache'
    Write-FixtureFile (Join-Path $fixture 'TestResults/IsolatedProjects/Old/Library/cache.lock') 'cache lock must be inventoried'
    Write-FixtureFile (Join-Path $fixture 'TestResults/IsolatedProjects/Old/UpmCache/cache') 'cache'
    Write-FixtureFile (Join-Path $fixture 'TestResults/IsolatedProjects/Old/IsolatedTemp/save/state.bin') 'actual route evidence'
    return $fixture
}
function Invoke-Maintenance([string]$Fixture, [string]$Action, [string]$Receipt='TestResults/Lightweight/receipt') {
    $log = Join-Path $runRoot ((Split-Path $Fixture -Leaf) + '-' + $Action + '-' + [Guid]::NewGuid().ToString('N') + '.log')
    $previousPreference=$ErrorActionPreference
    try {
        # Windows PowerShell converts native stderr into error records; expected
        # child failures must be assessed by exit code rather than terminating the harness.
        $ErrorActionPreference='Continue'
        & $MaintenanceShell -NoProfile -File $implementation -ProjectRoot $Fixture -Action $Action -ReceiptDirectory $Receipt *> $log
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$previousPreference }
    return $code
}
function Read-Plan([string]$Fixture) {
    Get-Content -LiteralPath (Join-Path $Fixture 'TestResults/Lightweight/receipt/plan.json') -Raw | ConvertFrom-Json
}
function Save-Plan([string]$Fixture, $Plan) {
    $Plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Fixture 'TestResults/Lightweight/receipt/plan.json') -Encoding utf8
}
function Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
try {
    $basic=New-Fixture 'basic'
    Check 'Plan succeeds' ((Invoke-Maintenance $basic 'Plan') -eq 0)
    $plan=Read-Plan $basic
    Check 'Plan selects exactly two isolated cache roots' (@($plan.cacheDirectories).Count -eq 2)
    Check 'Plan inventories cache lock files for deletion preflight' (@($plan.cacheFiles | Where-Object path -eq 'TestResults/IsolatedProjects/Old/Library/cache.lock').Count -eq 1)
    Check 'Historical non-cache lock is excluded from retained hashes' (@($plan.retainedFiles | Where-Object path -eq 'TestResults/Old/history.lock').Count -eq 0)
    Check 'Plan links only equal nonempty historical sources' (@($plan.hardLinks).Count -eq 1 -and $plan.hardLinks[0].path -like '*/b-source-copy/one.cs')
    $originalManifest=Digest (Join-Path $basic 'TestResults/Old/seal-manifest.json')
    Check 'Apply succeeds' ((Invoke-Maintenance $basic 'Apply') -eq 0)
    Check 'Verify succeeds' ((Invoke-Maintenance $basic 'Verify') -eq 0)
    Check 'Caches removed and actual route preserved' (-not (Test-Path -LiteralPath (Join-Path $basic 'TestResults/IsolatedProjects/Old/Library')) -and -not (Test-Path -LiteralPath (Join-Path $basic 'TestResults/IsolatedProjects/Old/UpmCache')) -and (Test-Path -LiteralPath (Join-Path $basic 'TestResults/IsolatedProjects/Old/IsolatedTemp/save/state.bin')))
    $first=Join-Path $basic 'TestResults/Old/a-source-copy/one.cs'
    $second=Join-Path $basic 'TestResults/Old/b-source-copy/one.cs'
    Check 'Both historical paths retain identical content and hardlink identity' ((Digest $first) -eq (Digest $second) -and (Get-Item -LiteralPath $first).LinkType -eq 'HardLink' -and (Get-Item -LiteralPath $second).LinkType -eq 'HardLink')
    Check 'Original manifest unchanged' ((Digest (Join-Path $basic 'TestResults/Old/seal-manifest.json')) -eq $originalManifest)
    Check 'Binary save and distinct version stay independently allocated' ((Get-Item -LiteralPath (Join-Path $basic 'TestResults/Old/a-source-copy/state.bin')).LinkType -ne 'HardLink' -and (Get-Item -LiteralPath (Join-Path $basic 'TestResults/Old/b-source-copy/variant.cs')).LinkType -ne 'HardLink')
    Check 'Repeated Apply is safe' ((Invoke-Maintenance $basic 'Apply') -eq 0)
    $changed=New-Fixture 'changed'
    $null=Invoke-Maintenance $changed 'Plan'
    Write-FixtureFile (Join-Path $changed 'TestResults/Old/b-source-copy/one.cs') 'modified after plan'
    Check 'Changed retained file rejects Apply before cache deletion' ((Invoke-Maintenance $changed 'Apply') -ne 0 -and (Test-Path -LiteralPath (Join-Path $changed 'TestResults/IsolatedProjects/Old/Library/cache')))
    $escape=New-Fixture 'escape'
    Check 'Receipt traversal is rejected' ((Invoke-Maintenance $escape 'Plan' '../outside-receipt') -ne 0)
    $null=Invoke-Maintenance $escape 'Plan'
    $escapePlan=Read-Plan $escape
    $escapePlan.hardLinks[0].canonical='../outside.cs'
    Save-Plan $escape $escapePlan
    Check 'Plan canonical traversal is rejected before mutation' ((Invoke-Maintenance $escape 'Apply') -ne 0 -and (Test-Path -LiteralPath (Join-Path $escape 'TestResults/IsolatedProjects/Old/Library/cache')))
    $junction=New-Fixture 'junction'
    $outside=Join-Path $junction 'outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $junction 'TestResults/Old/linked-source-copy') -Target $outside | Out-Null
    Check 'Empty reparse-point directory is rejected during Plan' ((Invoke-Maintenance $junction 'Plan') -ne 0)
    $postJunction=New-Fixture 'junction-after-plan'
    $null=Invoke-Maintenance $postJunction 'Plan'
    $outside2=Join-Path $postJunction 'outside'
    New-Item -ItemType Directory -Path $outside2 | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $postJunction 'TestResults/IsolatedProjects/Old/Library/linked') -Target $outside2 | Out-Null
    Check 'New empty cache junction rejects Apply before mutation' ((Invoke-Maintenance $postJunction 'Apply') -ne 0 -and (Test-Path -LiteralPath (Join-Path $postJunction 'TestResults/IsolatedProjects/Old/Library/cache')))
    $scope=New-Fixture 'scope'
    $null=Invoke-Maintenance $scope 'Plan'
    $scopePlan=Read-Plan $scope
    $scopePlan.hardLinks[0].path='TestResults/Old/a-source-copy/state.bin'
    Save-Plan $scope $scopePlan
    Check 'Forged hardlink targeting excluded bin is rejected before mutation' ((Invoke-Maintenance $scope 'Apply') -ne 0 -and (Test-Path -LiteralPath (Join-Path $scope 'TestResults/IsolatedProjects/Old/Library/cache')))
    $preserve=New-Fixture 'failure-preserves-original'
    $null=Invoke-Maintenance $preserve 'Plan'
    $preservePlan=Read-Plan $preserve
    $preservePlan.hardLinks[0].canonical='TestResults/Old/missing-source-copy/no.cs'
    Save-Plan $preserve $preservePlan
    $beforeHash=Digest (Join-Path $preserve 'TestResults/Old/b-source-copy/one.cs')
    $failureCode=Invoke-Maintenance $preserve 'Apply'
    Check 'Failed link preparation preserves original historical file' ($failureCode -ne 0 -and (Digest (Join-Path $preserve 'TestResults/Old/b-source-copy/one.cs')) -eq $beforeHash)
} catch {
    $cases.Add([ordered]@{name='Unhandled test failure';passed=$false;details=$_.Exception.Message})
} finally {
    $failed=@($cases | Where-Object { -not $_.passed })
    [ordered]@{result=if($failed.Count){'Failed'}else{'Passed'};cases=$cases.Count;failures=$failed.Count;checks=$cases;fixtureRoot=$runRoot;shell=$MaintenanceShell;unityStarted=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot 'result.json') -Encoding utf8
    "Cases=$($cases.Count); failures=$($failed.Count); evidence=$OutputRoot/result.json"
    $failed | ForEach-Object { "FAIL: $($_.name) $($_.details)" }
}
if (@($cases | Where-Object { -not $_.passed }).Count) { exit 1 }
