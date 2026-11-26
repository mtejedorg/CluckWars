<#
.SYNOPSIS
  One-button, repeatable build of Cluck Wars: runs the Unity batch build with the environment that keeps this PC's
  il2cpp tools from crashing, retries a tool crash, and decides pass or fail from the log (Unity exits 0 even when
  a build fails, so the exit code alone proves nothing).

.EXAMPLE
  tools\build\build.bat                      # Windows + Android, production
  tools\build\build.bat -Target Windows
  tools\build\build.bat -Target PlayStore    # the Google Play .aab
  tools\build\build.bat -Target TestSession  # dev-tools Windows + Android, for the multi-client sessions

.NOTES
  Exit codes: 0 every requested target built, 1 a target failed after the retries, 2 could not start
  (Unity or a game client is already running, Unity not found).
  It replaces Builds/Windows and Builds/Android. The build number is stamped by the build menu itself.
  The environment variables are inherited by UnityLinker and il2cpp. On 2026-11-26 their coreclr runtime crashed with
  0xC0000005 (exit -1073741819) at a different place on every run; with these set the full Android build passed.
  Which variable matters was not isolated.
#>
param(
    [ValidateSet('All', 'Windows', 'Android', 'PlayStore', 'TestSession')][string]$Target = 'All',
    [int]$Retries = 2,
    [string]$UnityExe = $env:UNITY_EXE,
    [string]$LogDir = ''
)
$ErrorActionPreference = 'Continue'
$repo = (git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $repo -or -not (Test-Path (Join-Path $repo 'ProjectSettings\ProjectVersion.txt'))) { Write-Error 'Run from inside the Cluck Wars repo.'; exit 2 }
$repo = (Resolve-Path $repo).Path
Set-Location $repo

if (-not $UnityExe) {
    $ver = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Select-String 'm_EditorVersion:').ToString() -split ':')[1].Trim()
    $UnityExe = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
}
if (-not (Test-Path $UnityExe)) { Write-Error "Unity not found at $UnityExe (set UNITY_EXE)."; exit 2 }
if (Get-Process -Name Unity -ErrorAction SilentlyContinue) { Write-Error 'A Unity process is open: close the Editor first (the project lock).'; exit 2 }
if (Get-Process -Name CluckWars -ErrorAction SilentlyContinue) { Write-Error 'A game client is running: close it first.'; exit 2 }

$method = switch ($Target) {
    'All'         { 'BuildAll' }
    'Windows'     { 'BuildWindows' }
    'Android'     { 'BuildAndroid' }
    'PlayStore'   { 'BuildPlayStore' }
    'TestSession' { 'BuildTestSession' }
}
# What a pass looks like in the log, per target. A line "[Build] <label> FAILED" or a missing OK line is a failure.
$expect = switch ($Target) {
    'All'         { @('StandaloneWindows64', 'Android') }
    'Windows'     { @('StandaloneWindows64') }
    'Android'     { @('Android') }
    'PlayStore'   { @('Android') }
    'TestSession' { @('StandaloneWindows64', 'Android') }
}

$env:DOTNET_gcServer = '0'
$env:DOTNET_gcConcurrent = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_PROCESSOR_COUNT = '4'

if (-not $LogDir) { $LogDir = Join-Path $repo 'Captures\build' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$crash = '-1073741819'   # 0xC0000005 from a Unity build tool: the known flaky case, worth a retry

function Get-Verdict([string]$log) {
    $text = Get-Content $log -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return @{ ok = $false; crash = $false; why = 'no log' } }
    $bad = @(); $okAll = $true
    foreach ($t in $expect) {
        if ($text -notmatch "\[Build\] $t OK") { $okAll = $false; $bad += $t }
    }
    $crashed = ($text -match "##### ExitCode\s+$crash")
    @{ ok = $okAll; crash = $crashed; why = ($bad -join ', ') }
}

$attempt = 0; $verdict = $null; $log = ''
do {
    $attempt++
    $log = Join-Path $LogDir "build-$Target-$stamp-try$attempt.log"
    Write-Host "[build] $Target, attempt $attempt of $($Retries + 1), log $log"
    $p = Start-Process -FilePath $UnityExe -ArgumentList @('-batchmode', '-quit', '-projectPath', "`"$repo`"", '-executeMethod',
        "CluckWars.EditorTools.CluckWarsBuildMenu.$method", '-logFile', "`"$log`"") -Wait -PassThru -NoNewWindow
    $verdict = Get-Verdict $log
    if ($verdict.ok) { break }
    if (-not $verdict.crash) { break }   # a real build error: retrying will not help
    Write-Host "[build] a build tool crashed (exit $crash); retrying"
} while ($attempt -le $Retries)

$lines = Select-String -Path $log -Pattern '\[Build\] (StandaloneWindows64|Android) (OK|FAILED)|\[Build\] Build number' | ForEach-Object { $_.Line.Trim() }
$lines | ForEach-Object { Write-Host $_ }

# The build rewrites this URP asset (drops two renderer-feature entries). It is never an intended change.
$dirty = git status --porcelain -- 'Assets/UniversalRenderPipelineGlobalSettings.asset'
if ($dirty) { Write-Host '[build] NOTE: Assets/UniversalRenderPipelineGlobalSettings.asset was modified by the build; restore it with: git checkout -- Assets/UniversalRenderPipelineGlobalSettings.asset' }

if ($verdict.ok) { Write-Host "[build] PASS ($Target)"; exit 0 }
Write-Host "[build] FAIL ($Target): $($verdict.why); first errors:"
Select-String -Path $log -Pattern 'error CS\d+|Build failed|Tundra build failed|BuildFailedException' | Select-Object -First 8 | ForEach-Object { Write-Host ('  ' + $_.Line.Trim()) }
exit 1
