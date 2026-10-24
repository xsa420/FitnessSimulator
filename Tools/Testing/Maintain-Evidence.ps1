param(
    [ValidateSet('Plan','Apply','Verify')][string]$Action = 'Plan',
    [string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$ReceiptDirectory = 'TestResults/Lightweight/LITE-20261024/maintenance'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
$evidenceRoot = Join-Path $root 'TestResults'
$receiptRoot = [IO.Path]::GetFullPath((Join-Path $root $ReceiptDirectory))
$checkedDirectories = @{}
function Assert-InEvidence([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($evidenceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Outside TestResults: $full" }
    $cursor = $full
    while ($cursor -and $cursor.Length -ge $root.Length) {
        if ($checkedDirectories.ContainsKey($cursor)) { break }
        if ([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)) {
            $attributes = [IO.File]::GetAttributes($cursor)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point is not allowed: $cursor" }
            if ($attributes -band [IO.FileAttributes]::Directory) { $checkedDirectories[$cursor] = $true }
        }
        $cursor = Split-Path $cursor -Parent
    }
    return $full
}
$null = Assert-InEvidence $receiptRoot
New-Item -ItemType Directory -Path $receiptRoot -Force | Out-Null
$planPath = Join-Path $receiptRoot 'plan.json'
function Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
function Relative([string]$Path) { $Path.Substring($root.Length + 1).Replace('\','/') }
function Resolve-Entry([string]$RelativePath) { Assert-InEvidence ([IO.Path]::GetFullPath((Join-Path $root $RelativePath))) }
function Assert-File($Entry) {
    $path = Resolve-Entry $Entry.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing retained evidence: $path" }
    if ((Get-Item -LiteralPath $path).Length -ne $Entry.bytes -or (Hash $path) -ne $Entry.sha256) { throw "Evidence changed: $path" }
    return $path
}
function Is-Excluded([string]$Path) {
    return $Path.StartsWith((Join-Path $evidenceRoot 'Lightweight') + '\', [StringComparison]::OrdinalIgnoreCase) -or $Path.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase) -or $Path.EndsWith('.writer-lock', [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoEditor {
    $running = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($running.Count) { throw 'An editor is using this project or a nested isolated project; no maintenance performed.' }
}
Assert-NoEditor
$lockRoot = Join-Path $evidenceRoot 'M1'
New-Item -ItemType Directory -Path $lockRoot -Force | Out-Null
$lock = [IO.File]::Open((Join-Path $lockRoot 'unity-batch.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    if ($Action -eq 'Plan') {
        $cacheRoots = @()
        $isolated = Join-Path $evidenceRoot 'IsolatedProjects'
        if (Test-Path -LiteralPath $isolated) {
            foreach ($project in Get-ChildItem -LiteralPath $isolated -Directory) {
                foreach ($name in @('Library','UpmCache')) {
                    $candidate = Join-Path $project.FullName $name
                    if (Test-Path -LiteralPath $candidate) { $cacheRoots += (Assert-InEvidence $candidate) }
                }
            }
        }
        $retained = [Collections.Generic.List[object]]::new()
        $cacheFiles = [Collections.Generic.List[object]]::new()
        $duplicates = [Collections.Generic.List[object]]::new()
        $seen = @{}
        foreach ($directory in Get-ChildItem -LiteralPath $evidenceRoot -Recurse -Directory -Force) {
            if (Is-Excluded ($directory.FullName + '\')) { continue }
            if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Historical evidence contains a reparse point.' }
        }
        foreach ($file in Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Force | Sort-Object FullName) {
            if (Is-Excluded $file.FullName) { continue }
            $null = Assert-InEvidence $file.FullName
            $inCache = $false
            foreach ($cache in $cacheRoots) { if ($file.FullName.StartsWith($cache + '\', [StringComparison]::OrdinalIgnoreCase)) { $inCache = $true; break } }
            if ($inCache) {
                $cacheFiles.Add([ordered]@{ path=(Relative $file.FullName); bytes=$file.Length })
                continue
            }
            $hash = Hash $file.FullName
            $entry = [ordered]@{ path=(Relative $file.FullName); bytes=$file.Length; sha256=$hash }
            $retained.Add($entry)
            # Only historical payloads are coalesced. Keep mutable metadata, reports,
            # manifests, locks and empty files independently allocated.
            $eligible = $file.FullName -match '[\\/][^\\/]+-source-copy[\\/]' -and $file.Extension -ne '.bin'
            if (-not $eligible -or $file.Length -eq 0 -or $file.LinkType -eq 'HardLink') { continue }
            $key = "$($file.Length):$hash"
            if ($seen.ContainsKey($key)) {
                $duplicates.Add([ordered]@{ path=$entry.path; canonical=$seen[$key]; bytes=$file.Length; sha256=$hash })
            } else { $seen[$key] = $entry.path }
        }
        [long]$cacheBytes = 0; foreach ($entry in $cacheFiles) { $cacheBytes += $entry.bytes }
        [long]$duplicateBytes = 0; foreach ($entry in $duplicates) { $duplicateBytes += $entry.bytes }
        $plan = [ordered]@{
            schema=1; projectRoot=$root; createdUtc=[DateTime]::UtcNow.ToString('o')
            cacheDirectories=@($cacheRoots | ForEach-Object { Relative $_ }); cacheFiles=$cacheFiles.ToArray()
            retainedFiles=$retained.ToArray(); hardLinks=$duplicates.ToArray()
            cacheBytes=$cacheBytes; duplicateBytes=$duplicateBytes
        }
        $plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $planPath -Encoding UTF8
        [ordered]@{ action='Plan'; cacheBytes=$plan.cacheBytes; duplicateBytes=$plan.duplicateBytes; retainedFiles=$retained.Count; hardLinks=$duplicates.Count; plan=$planPath } | ConvertTo-Json -Compress
        exit 0
    }
    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    if ($plan.schema -ne 1 -or $plan.projectRoot -ne $root) { throw 'Plan belongs to another workspace or schema.' }
    # Validate every retained byte before and after mutation, not merely the duplicates.
    foreach ($entry in $plan.retainedFiles) { $null = Assert-File $entry }
    if ($Action -eq 'Apply') {
        # Preflight the complete mutation list before removing even one cache directory.
        foreach ($entry in $plan.hardLinks) {
            foreach ($relative in @($entry.path, $entry.canonical)) {
                if ($relative -notmatch '^TestResults/(?:[^/]+/)*[^/]+-source-copy/.+' -or [IO.Path]::GetExtension($relative) -eq '.bin') { throw 'Hard-link target is outside historical source-copy scope.' }
                $candidate = Resolve-Entry $relative
                if ((Get-Item -LiteralPath $candidate).Length -ne $entry.bytes -or (Hash $candidate) -ne $entry.sha256) { throw 'Hard-link preflight content mismatch.' }
            }
        }
        foreach ($relative in $plan.cacheDirectories) {
            $checkedDirectories.Clear()
            $cache = Resolve-Entry $relative
            if ($relative -notmatch '^TestResults/IsolatedProjects/[^/]+/(Library|UpmCache)$') { throw 'Cache target is not in the explicit allowlist.' }
            if (Test-Path -LiteralPath $cache) {
                foreach ($directory in Get-ChildItem -LiteralPath $cache -Recurse -Directory -Force) {
                    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Cache contains a reparse point; refusing recursive deletion.' }
                }
                $actual = @(Get-ChildItem -LiteralPath $cache -Recurse -File -Force)
                $expected = @($plan.cacheFiles | Where-Object { $_.path.StartsWith($relative + '/', [StringComparison]::OrdinalIgnoreCase) })
                if ($actual.Count -ne $expected.Count) { throw 'Cache changed since planning; create and review a new plan.' }
                $lengths = @{}; foreach ($item in $expected) { $lengths[$item.path] = $item.bytes }
                foreach ($file in $actual) {
                    $null = Assert-InEvidence $file.FullName
                    if (-not $lengths.ContainsKey((Relative $file.FullName)) -or $lengths[(Relative $file.FullName)] -ne $file.Length) { throw 'Cache changed since planning.' }
                }
            }
        }
        foreach ($relative in $plan.cacheDirectories) {
            $checkedDirectories.Clear()
            $cache = Resolve-Entry $relative
            if (Test-Path -LiteralPath $cache) {
                # Native PowerShell, resolved allowlisted absolute path; no cross-shell deletion.
                Remove-Item -LiteralPath $cache -Recurse -Force
            }
        }
        $linked = 0
        foreach ($entry in $plan.hardLinks) {
            $target = Assert-File $entry
            $canonical = Resolve-Entry $entry.canonical
            if ((Hash $canonical) -ne $entry.sha256) { throw 'Canonical evidence changed.' }
            if ((Get-Item -LiteralPath $target).LinkType -eq 'HardLink') { continue }
            $temporary = $target + '.dedup-' + [Guid]::NewGuid().ToString('N')
            $null = Assert-InEvidence $temporary
            try {
                New-Item -ItemType HardLink -Path $temporary -Target $canonical | Out-Null
                if ((Hash $temporary) -ne $entry.sha256) { throw 'Hard-link verification failed.' }
                # Atomic replacement on the same volume; the original path always exists.
                [IO.File]::Replace($temporary, $target, [NullString]::Value)
                $linked++
            } finally {
                if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
            }
        }
        foreach ($entry in $plan.retainedFiles) { $null = Assert-File $entry }
        [ordered]@{ action='Apply'; completedUtc=[DateTime]::UtcNow.ToString('o'); verifiedRetainedFiles=$plan.retainedFiles.Count; linksCreated=$linked; plannedCacheBytes=$plan.cacheBytes; plannedDuplicateBytes=$plan.duplicateBytes; planSha256=(Hash $planPath) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $receiptRoot 'applied.json') -Encoding UTF8
    }
    [ordered]@{ action=$Action; retainedFiles=$plan.retainedFiles.Count; verified=$true; planSha256=(Hash $planPath) } | ConvertTo-Json -Compress
} finally { $lock.Dispose() }
