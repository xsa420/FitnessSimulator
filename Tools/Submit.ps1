param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$Message,
    [string]$ProxyUrl,
    [switch]$AskProxy,
    [switch]$NonInteractive,
    [switch]$Pause
)
$ErrorActionPreference = 'Stop'
$result = 0
$proxyArguments = @()
function Invoke-ProjectGit([string[]]$Arguments) {
    $savedPreference = $ErrorActionPreference
    try {
        # Native progress on stderr is not a PowerShell failure (Windows PS 5.1).
        $ErrorActionPreference = 'Continue'
        & git -C $ProjectRoot @proxyArguments @Arguments
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($code -ne 0) { throw "Git command failed (exit $code): $($Arguments[0]). Local changes/commits are retained." }
}
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Install Git for Windows first, then reopen this shortcut.' }
    $ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
    $actualRoot = (Invoke-ProjectGit -Arguments @('rev-parse','--show-toplevel')).Trim().Replace('/','\')
    if ($actualRoot -ne $ProjectRoot.Replace('/','\')) { throw 'Shortcut must target the root of its own Git clone.' }
    $branch = (Invoke-ProjectGit -Arguments @('symbolic-ref','--quiet','--short','HEAD')).Trim()
    $remote = (Invoke-ProjectGit -Arguments @('remote','get-url','--push','origin')).Trim()
    if (Invoke-ProjectGit -Arguments @('ls-files','--unmerged')) { throw 'Resolve existing merge conflicts before submitting.' }
    foreach ($state in @('MERGE_HEAD','CHERRY_PICK_HEAD','REVERT_HEAD','rebase-merge','rebase-apply')) {
        $statePath = (Invoke-ProjectGit -Arguments @('rev-parse','--git-path',$state)).Trim()
        if (-not [IO.Path]::IsPathRooted($statePath)) { $statePath = Join-Path $ProjectRoot $statePath }
        if (Test-Path -LiteralPath $statePath) { throw "Finish the existing Git operation first: $state" }
    }
    if ($AskProxy -and -not $NonInteractive -and -not $ProxyUrl) {
        $ProxyUrl = Read-Host 'Proxy URL for this run (example http://127.0.0.1:7890; Enter = existing Git settings)'
    }
    if ($ProxyUrl) {
        $uri = $null
        if (-not [Uri]::TryCreate($ProxyUrl,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -notin @('http','https','socks5','socks5h') -or -not $uri.Host) { throw 'Invalid proxy URL.' }
        if ($remote -notmatch '^https://') { throw 'This proxy shortcut requires an HTTPS origin. Clone using the HTTPS repository URL; SSH remotes need separate SSH proxy settings.' }
        $proxyArguments = @('-c',"http.proxy=$ProxyUrl")
    }
    Write-Host "Repository: $ProjectRoot"
    Write-Host "Branch: $branch"
    Write-Host "Push destination: $remote"
    Write-Host 'All changed/new/deleted files except Git-ignored files will be included.'
    $status = @(Invoke-ProjectGit -Arguments @('status','--short'))
    if ($status.Count) {
        $status | ForEach-Object { Write-Host $_ }
        foreach ($field in @('name','email')) {
            $value = Invoke-ProjectGit -Arguments @('config','--get-regexp',"^user\.$field$|^core\.repositoryformatversion$")
            if (-not ($value | Where-Object { $_ -match "^user\.$field " })) {
                if ($NonInteractive) { throw "Set git config user.$field before submitting." }
                $identity = Read-Host "Git author $field (saved only in this clone)"
                if ([string]::IsNullOrWhiteSpace($identity)) { throw 'Author identity cannot be empty.' }
                Invoke-ProjectGit -Arguments @('config','--local',"user.$field",$identity)
            }
        }
        if ([string]::IsNullOrWhiteSpace($Message)) {
            if ($NonInteractive) { throw 'A nonempty -Message is required when files changed.' }
            $Message = Read-Host 'Commit message (Enter without text = cancel)'
        }
        if ([string]::IsNullOrWhiteSpace($Message)) { throw 'Cancelled before staging or committing.' }
        Invoke-ProjectGit -Arguments @('add','--all')
        $staged = @(Invoke-ProjectGit -Arguments @('diff','--cached','--name-only'))
        if ($staged.Count) { Invoke-ProjectGit -Arguments @('commit','-m',$Message) }
    } else { Write-Host 'No working-file changes. Pushing existing local commits, if any.' }
    # No force, pull, merge, reset, branch switch or automatic build.
    Invoke-ProjectGit -Arguments @('push','--set-upstream','origin',"${branch}:refs/heads/$branch")
    Write-Host 'SUCCESS: branch pushed.' -ForegroundColor Green
} catch {
    $result = 1
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'If the remote has newer commits, integrate them and resolve conflicts before retrying. For authentication errors, sign in to the repository account or configure your SSH key.'
} finally {
    if ($Pause -and -not $NonInteractive) { $null = Read-Host 'Press Enter to close' }
}
exit $result
