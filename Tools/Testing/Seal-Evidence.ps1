param([ValidateSet('Create','Verify')][string]$Action='Verify',
    [string]$ProjectRoot=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [Parameter(Mandatory)][string]$ManifestPath, [string[]]$Paths, [long]$MaxBytes=134217728)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Evidence.ps1')
if ($Action -eq 'Create') {
    if (-not $Paths.Count) { throw 'Create requires explicit evidence file paths; directories are not copied or expanded.' }
    $manifest=New-EvidenceReference -ProjectRoot $ProjectRoot -Paths $Paths -OutputPath $ManifestPath -MaxBytes $MaxBytes
    "Reference created: $ManifestPath ($($manifest.evidence.Count) files, $($manifest.bytes) bytes; no source or evidence copies)."
} else {
    $null=Test-EvidenceReference -ProjectRoot $ProjectRoot -ManifestPath $ManifestPath
    "Reference verified: $ManifestPath"
}
