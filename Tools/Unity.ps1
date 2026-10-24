param(
    [Parameter(Mandatory=$true)][ValidateSet('setup','validate','test','build-windows','build-android','smoke')][string]$Action,
    [string]$EditorPath,
    [switch]$ShowWindow
)
$ErrorActionPreference = 'Stop'
$environment = & (Join-Path $PSScriptRoot 'Environment.ps1') -EditorPath $EditorPath
$projectRoot = $environment.Project
$logRoot = Join-Path $projectRoot 'Logs'
$resultsRoot = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $logRoot,$resultsRoot -Force | Out-Null
if ($Action -eq 'smoke') {
    $executable = Join-Path $projectRoot 'Builds/Windows/FitnessSimulator.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Run build-windows first.' }
    $reportRoot = Join-Path $resultsRoot 'Runtime'
    $arguments = @('-fitnessSmoke','-fitnessReportDir',"`"$reportRoot`"",'-logFile',"`"$logRoot/runtime.log`"",'-screen-width','1600','-screen-height','900','-screen-fullscreen','0')
} else {
    $executable = $environment.Editor
    $arguments = @('-batchmode','-nographics','-projectPath',"`"$projectRoot`"",'-logFile',"`"$logRoot/$Action.log`"")
    if ($Action -eq 'test') {
        $arguments += @('-runTests','-testPlatform','EditMode','-testResults',"`"$resultsRoot/editmode.xml`"")
    } else {
        $methods = @{ setup='Configure'; validate='Validate'; 'build-windows'='BuildWindows'; 'build-android'='BuildAndroid' }
        $arguments += @('-quit','-executeMethod',('FitnessSimulator.Editor.ProjectSetup.' + $methods[$Action]))
        if ($Action -eq 'build-android') { $arguments += @('-buildTarget','Android') }
        if ($Action -eq 'build-windows') { $arguments += @('-buildTarget','StandaloneWindows64') }
    }
}
$windowStyle = 'Hidden'
if ($Action -eq 'smoke' -and $ShowWindow) { $windowStyle = 'Normal' }
$startedAt = [DateTime]::UtcNow
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle $windowStyle -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "$Action failed with exit code $($process.ExitCode). Inspect Logs/$Action.log (runtime.log for smoke)." }
function Assert-FreshFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing output from this run: $Path" }
    if ((Get-Item -LiteralPath $Path).LastWriteTimeUtc -lt $startedAt) { throw "Stale output from an earlier run: $Path" }
}
if ($Action -ne 'smoke') {
    $logPath = Join-Path $logRoot "$Action.log"
    Assert-FreshFile $logPath
    if ($Action -in @('validate','setup') -and -not (Select-String -LiteralPath $logPath -SimpleMatch 'FITNESS_PROJECT_VALIDATION_OK' -Quiet)) { throw 'Project validation did not complete.' }
}
if ($Action -eq 'test') {
    Assert-FreshFile (Join-Path $resultsRoot 'editmode.xml')
    [xml]$testResult = Get-Content -LiteralPath (Join-Path $resultsRoot 'editmode.xml')
    if ($testResult.'test-run'.result -ne 'Passed' -or [int]$testResult.'test-run'.passed -lt 1) { throw 'EditMode tests did not pass or no tests ran.' }
}
if ($Action -in @('build-windows','build-android')) {
    $target = if ($Action -eq 'build-windows') { 'StandaloneWindows64' } else { 'Android' }
    $buildReport = Join-Path $resultsRoot "build-$target.json"
    Assert-FreshFile $buildReport
    $summary = Get-Content -Raw -LiteralPath $buildReport | ConvertFrom-Json
    if ($summary.result -ne 'Succeeded' -or $summary.errors -ne 0) { throw "Build report indicates failure: $buildReport" }
}
if ($Action -eq 'build-android') {
    $buildTools = Get-ChildItem (Join-Path $environment.Android 'SDK/build-tools') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $aapt = Join-Path $buildTools.FullName 'aapt.exe'
    $apk = Join-Path $projectRoot 'Builds/Android/FitnessSimulator.apk'
    $permissions = & $aapt dump permissions $apk
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect APK permissions.' }
    if ($permissions -match 'android.permission.INTERNET') { throw 'Offline APK unexpectedly requests INTERNET permission.' }
    $badging = & $aapt dump badging $apk
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect APK metadata.' }
    if (-not ($badging -match "native-code: 'arm64-v8a'")) { throw 'Expected ARM64 APK.' }
    @($permissions; $badging) | Set-Content -Encoding UTF8 (Join-Path $resultsRoot 'android-package.txt')
}
if ($Action -eq 'smoke') {
    Assert-FreshFile (Join-Path $reportRoot 'runtime-evidence.json')
    $evidence = Get-Content -Raw (Join-Path $reportRoot 'runtime-evidence.json') | ConvertFrom-Json
    if (-not ($evidence.localStorageRoundTrip -and $evidence.landscape -and $evidence.allAnimationsMoved -and $evidence.presentationsChecked -eq 8)) { throw 'Runtime verification failed.' }
    $screenshots = @($evidence.animatedExerciseIds | ForEach-Object { Join-Path $reportRoot ($_ + '.png') })
    foreach ($screenshot in $screenshots) { Assert-FreshFile $screenshot }
    $uniqueImages = @($screenshots | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash } | Select-Object -Unique)
    if ($uniqueImages.Count -ne 8) { throw 'Screenshots are not distinct. Hidden/occluded windows may not render. Re-run smoke -ShowWindow on an interactive desktop and inspect the images.' }
}
Write-Output "$Action PASSED"
