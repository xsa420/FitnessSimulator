# Shared, side-effect-free helpers for lightweight verification.
function Get-EvidenceVersion {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    $head = & git -C $root rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Version evidence requires an existing Git commit.' }
    $status = & git -C $root -c core.quotepath=false status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git working tree status.' }
    $names = & git -C $root ls-files --cached --others --exclude-standard -z
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate versioned files.' }
    $files = @((($names -join "`n") -split "`0") | Where-Object { $_ } | Sort-Object -Unique | ForEach-Object {
        $relative = $_
        $path = Join-Path $root $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [ordered]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        } else { [ordered]@{ path=$relative; sha256=$null } }
    })
    [ordered]@{
        schemaVersion=1; gitCommit="$head"; dirty=(@($status).Count -gt 0)
        certificationEligible=(@($status).Count -eq 0)
        reconstruction=if (@($status).Count) { 'SelfTestOnly: hashes identify working files; Git commit does not reconstruct uncommitted content.' } else { 'GitCommit' }
        status=@($status); files=$files
    }
}

function Get-EvidenceVersionDrift {
    param($Before, $After)
    $left = $Before | ConvertTo-Json -Depth 8 -Compress
    $right = $After | ConvertTo-Json -Depth 8 -Compress
    return [int]($left -cne $right)
}

function Assert-EvidenceBudget {
    param([Parameter(Mandatory)][string]$RunRoot, [string]$LogPath,
        [long]$RunMaxBytes=134217728, [long]$LogMaxBytes=16777216)
    if ($RunMaxBytes -le 0 -or $LogMaxBytes -le 0) { throw 'Evidence budgets must be positive.' }
    [long]$logBytes=0
    if ($LogPath -and (Test-Path -LiteralPath $LogPath -PathType Leaf)) { $logBytes=(Get-Item -LiteralPath $LogPath).Length }
    if ($logBytes -gt $LogMaxBytes) { throw "Evidence budget exceeded: run log $logBytes > $LogMaxBytes bytes. Evidence was not truncated." }
    [long]$total=0
    if (Test-Path -LiteralPath $RunRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $RunRoot -Recurse -File) {
            $total += $file.Length
            if ($total -gt $RunMaxBytes) { throw "Evidence budget exceeded: run evidence $total > $RunMaxBytes bytes. Evidence was not truncated." }
        }
    }
    if ($LogPath -and -not ([IO.Path]::GetFullPath($LogPath).StartsWith([IO.Path]::GetFullPath($RunRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) { $total += $logBytes }
    if ($total -gt $RunMaxBytes) { throw "Evidence budget exceeded: run total $total > $RunMaxBytes bytes. Evidence was not truncated." }
    return $total
}

function Resolve-EvidenceReference {
    param([string]$Root, [string]$RelativePath)
    $absoluteRoot=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $resolved=[IO.Path]::GetFullPath((Join-Path $absoluteRoot $RelativePath))
    if (-not $resolved.StartsWith($absoluteRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Evidence reference escapes root: $RelativePath" }
    # References must not escape through a filesystem junction/symlink either.
    $cursor=$resolved
    while ($cursor -ne $absoluteRoot) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Evidence reference uses a link: $RelativePath" }
        $cursor=Split-Path $cursor -Parent
    }
    return $resolved
}

function New-EvidenceReference {
    param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string[]]$Paths,
        [Parameter(Mandatory)][string]$OutputPath, [long]$MaxBytes=134217728)
    if ($MaxBytes -le 0) { throw 'Reference budget must be positive.' }
    $root=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
    $destination=[IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $destination) { throw 'Reference manifest already exists; refusing to overwrite.' }
    [long]$total=0
    $entries=@(foreach ($inputPath in ($Paths | Sort-Object -Unique)) {
        $absolute=if ([IO.Path]::IsPathRooted($inputPath)) { [IO.Path]::GetFullPath($inputPath) } else { Join-Path $root $inputPath }
        if (-not $absolute.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence must be inside project root.' }
        $relative=$absolute.Substring($root.Length+1).Replace('\','/')
        $absolute=Resolve-EvidenceReference $root $relative
        if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { throw "Evidence file missing: $relative" }
        if ($absolute -eq $destination) { throw 'Reference cannot include itself.' }
        $file=Get-Item -LiteralPath $absolute
        $total+=$file.Length
        if ($total -gt $MaxBytes) { throw "Referenced evidence budget exceeded: $total > $MaxBytes bytes." }
        [ordered]@{ path=$relative; bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
    })
    if (-not $entries.Count) { throw 'No evidence files selected.' }
    $manifest=[ordered]@{ schemaVersion=1; kind='reference-only'; createdUtc=[DateTime]::UtcNow.ToString('o'); bytes=$total; evidence=$entries }
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $destination -Encoding UTF8
    return $manifest
}

function Test-EvidenceReference {
    param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string]$ManifestPath)
    $manifest=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.kind -ne 'reference-only' -or -not @($manifest.evidence).Count) { throw 'Invalid reference manifest.' }
    [long]$total=0
    foreach ($entry in $manifest.evidence) {
        $absolute=Resolve-EvidenceReference $ProjectRoot $entry.path
        if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { throw "Referenced evidence missing: $($entry.path)" }
        $file=Get-Item -LiteralPath $absolute
        if ($file.Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Referenced evidence changed: $($entry.path)" }
        $total+=$file.Length
    }
    if ($total -ne $manifest.bytes) { throw 'Reference manifest byte total mismatch.' }
    return $true
}
