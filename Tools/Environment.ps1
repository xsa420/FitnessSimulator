param([string]$EditorPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$versionLine = Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' }
$editorVersion = ($versionLine -split ':', 2)[1].Trim()
if (-not $EditorPath) { $EditorPath = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
if (-not (Test-Path -LiteralPath $EditorPath)) { throw "Install Unity $editorVersion with Android Build Support, SDK/NDK and OpenJDK through Unity Hub, or pass -EditorPath." }
$installedVersion = (Get-Item -LiteralPath $EditorPath).VersionInfo.ProductVersion
if (($installedVersion -split '_', 2)[0] -ne $editorVersion) { throw "Editor version mismatch. Project requires $editorVersion; selected editor is $installedVersion." }
$androidRoot = Join-Path (Split-Path $EditorPath -Parent) 'Data/PlaybackEngines/AndroidPlayer'
$checks = [ordered]@{
    Editor = $EditorPath
    Java = Join-Path $androidRoot 'OpenJDK/bin/java.exe'
    Adb = Join-Path $androidRoot 'SDK/platform-tools/adb.exe'
    Ndk = Join-Path $androidRoot 'NDK/source.properties'
    WindowsSupport = Join-Path (Split-Path $EditorPath -Parent) 'Data/PlaybackEngines/windowsstandalonesupport'
}
foreach ($entry in $checks.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value)) { throw "Missing $($entry.Key): $($entry.Value)" }
}
$buildTools = Get-ChildItem (Join-Path $androidRoot 'SDK/build-tools') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $buildTools -or -not (Test-Path -LiteralPath (Join-Path $buildTools.FullName 'aapt.exe'))) { throw 'Android SDK build-tools with aapt.exe are required.' }
[pscustomobject]@{ Project = $projectRoot; Version = $editorVersion; Editor = $EditorPath; Android = $androidRoot; Checks = 'PASS' }
