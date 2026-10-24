param([string]$OutputRoot=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'TestResults/Lightweight/LITE-20261024'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Evidence.ps1')
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$root=Join-Path ([IO.Path]::GetTempPath()) ('fitness-evidence-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$results=[Collections.Generic.List[string]]::new()
function Assert-Case([string]$Name,[bool]$Condition) {
    if (-not $Condition) { throw "FAILED: $Name" }
    $results.Add("PASS: $Name")
}
function Expect-Failure([string]$Name,[scriptblock]$Body) {
    $failed=$false
    try { & $Body | Out-Null } catch { $failed=$true }
    Assert-Case $Name $failed
}
try {
    & git -C $root init -q
    if ($LASTEXITCODE) { throw 'git init failed' }
    Set-Content -LiteralPath (Join-Path $root '.gitignore') "evidence/`nhistorical/"
    Set-Content -LiteralPath (Join-Path $root 'source file.cs') 'class Original {}'
    & git -C $root add --all
    & git -C $root -c user.name=EvidenceTest -c user.email=evidence-test@example.invalid commit -qm fixture
    if ($LASTEXITCODE) { throw 'git fixture commit failed' }
    $before=Get-EvidenceVersion $root
    Assert-Case 'clean commit eligible; filenames with spaces hashed' (-not $before.dirty -and $before.certificationEligible -and @($before.files).Count -eq 2)
    $again=Get-EvidenceVersion $root
    Assert-Case 'identical version has no drift' ((Get-EvidenceVersionDrift $before $again) -eq 0)
    Set-Content -LiteralPath (Join-Path $root 'source file.cs') 'class Changed {}'
    Set-Content -LiteralPath (Join-Path $root 'new.cs') 'class Added {}'
    $dirty=Get-EvidenceVersion $root
    Assert-Case 'dirty worktree is self-test only and identifies untracked files' ($dirty.dirty -and -not $dirty.certificationEligible -and @($dirty.files | Where-Object path -eq 'new.cs').Count -eq 1)
    Assert-Case 'dirty modification causes drift' ((Get-EvidenceVersionDrift $before $dirty) -eq 1)
    Remove-Item -LiteralPath (Join-Path $root 'source file.cs')
    $deleted=Get-EvidenceVersion $root
    Assert-Case 'deleted tracked file retained as missing' (@($deleted.files | Where-Object { $_.path -eq 'source file.cs' -and $null -eq $_.sha256 }).Count -eq 1)
    $run=Join-Path $root 'evidence/run'
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $payload=Join-Path $run 'trace.jsonl'
    [IO.File]::WriteAllBytes($payload,[byte[]]::new(16))
    $manifest=Join-Path $root 'evidence/reference.json'
    $null=New-EvidenceReference $root @($payload) $manifest 16
    Assert-Case 'reference creation and verification without payload copy' ((Test-EvidenceReference $root $manifest) -and @(Get-ChildItem -LiteralPath $run -File).Count -eq 1)
    Expect-Failure 'reference refuses overwrite' { New-EvidenceReference $root @($payload) $manifest }
    Expect-Failure 'reference budget overflow rejects' { New-EvidenceReference $root @($payload) (Join-Path $root 'evidence/overflow.json') 15 }
    Assert-Case 'exact run budget accepted' ((Assert-EvidenceBudget $run -RunMaxBytes 16) -eq 16)
    Expect-Failure 'run budget overflow rejects without truncation' { Assert-EvidenceBudget $run -RunMaxBytes 15 }
    Assert-Case 'overflow preserves payload' ((Get-Item -LiteralPath $payload).Length -eq 16)
    $history=Join-Path $root 'historical'
    New-Item -ItemType Directory -Path $history | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $history 'old.log'),[byte[]]::new(4096))
    Assert-Case 'historical evidence excluded from current budget' ((Assert-EvidenceBudget $run -RunMaxBytes 16) -eq 16)
    $log=Join-Path $root 'evidence/run.log'
    [IO.File]::WriteAllBytes($log,[byte[]]::new(8))
    Assert-Case 'external current log included once at exact total' ((Assert-EvidenceBudget $run $log 24 8) -eq 24)
    Expect-Failure 'log overflow rejects' { Assert-EvidenceBudget $run $log 128 7 }
    Expect-Failure 'combined total overflow rejects' { Assert-EvidenceBudget $run $log 23 8 }
    [IO.File]::WriteAllBytes($payload,[byte[]]::new(17))
    Expect-Failure 'reference detects tamper' { Test-EvidenceReference $root $manifest }
    Expect-Failure 'reference path traversal rejects' { Resolve-EvidenceReference $root '../outside.txt' }
    $parseTargets=@((Join-Path (Split-Path $PSScriptRoot -Parent) 'Game.ps1'),(Join-Path (Split-Path $PSScriptRoot -Parent) 'Automation/Run.ps1')) + @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Where-Object Name -ne 'Maintain-Evidence.ps1' | ForEach-Object FullName)
    foreach($file in $parseTargets) {
        $tokens=$null; $errors=$null
        $null=[Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
        Assert-Case ('PowerShell parse '+(Split-Path $file -Leaf)) (-not $errors)
    }
    $results | Set-Content -LiteralPath (Join-Path $OutputRoot 'tools-tests.txt') -Encoding UTF8
    [ordered]@{result='Passed';cases=$results.Count;fixture='Temporary minimal Git repository; deleted after test';unityStarted=$false;createdUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'tools-result.json') -Encoding UTF8
    "Passed $($results.Count) cases; evidence: $OutputRoot/tools-result.json"
} finally {
    # Only this test's fresh GUID directory may be deleted.
    $resolved=[IO.Path]::GetFullPath($root)
    $allowed=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notmatch '^fitness-evidence-test-[0-9a-f]{32}$') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Force -Recurse
}
