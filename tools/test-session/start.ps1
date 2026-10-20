<#
.SYNOPSIS
  Start a multi-device Cluck Wars test session: logcat + rolling screenrecord + (optional) EXE clients
  and phone app launch. Everything lands in Captures\session-<UTC yyyyMMdd-HHmmss>\.

.DESCRIPTION
  Decisions (also see README.md):
   * Session folder name and every timestamp are UTC so they line up with the game's UTC log stamps.
   * Device = first `adb devices` entry in state 'device' that is not emulator-* (override: -Serial).
   * Phone app is only (re)started with -LaunchPhone (force-stop first, so the autotest extras are
     read by a fresh process). Without it the script just records whatever the app does.
   * `adb logcat -c` always runs before recording (except in -DryRun) so phone.log contains only this session.
   * Editor.log is not copied now; its path + byte length at start go in session.json and stop.ps1
     slices everything written after that point.
   * -DryRun / -WhatIf prints every command and touches nothing (read-only adb queries still run).

.PARAMETER ExeClients     Number of windowed Windows EXE clients to launch (log: client<i>.log in the session dir).
.PARAMETER ExeArgs        Extra args appended to EVERY client, e.g. '-cw_devmode','1'.
.PARAMETER ExeClientArgs  One string per client with its own args (space separated), e.g. '-cw_autohost 1','-cw_autojoin ABC123'.
.PARAMETER PhoneExtras    Intent extras for the phone: hashtable or 'cw_autojoin=ABC123 cw_class=Warrior cw_autoplay=1'.
.PARAMETER WaitCode       After launching, block until an AutoTest code shows in any log and print it.

.EXAMPLE
  # Editor hosts (you press Play + HOST), phone auto-joins, 2 EXE clients auto-join
  .\tools\test-session\start.ps1 -LaunchPhone -PhoneExtras 'cw_autojoin=ABC123 cw_class=Warrior cw_autoplay=1' `
      -ExeClients 2 -ExeClientArgs '-cw_autojoin ABC123','-cw_autojoin ABC123'

.EXAMPLE
  # Phone auto-hosts; read the code from the log; EXE joins
  .\tools\test-session\start.ps1 -LaunchPhone -PhoneExtras 'cw_autohost=1 cw_class=Warrior' -WaitCode
  .\tools\test-session\get-code.ps1                      # (or use the code printed by -WaitCode)
  .\tools\test-session\start.ps1 -ExeClients 1 -ExeClientArgs '-cw_autojoin XYZ789'   # new session folder
#>
param(
    [string]$Serial,
    [int]$ExeClients = 0,
    [string[]]$ExeArgs = @(),
    [string[]]$ExeClientArgs = @(),
    $PhoneExtras,
    [switch]$LaunchPhone,
    [switch]$InstallApk,
    [int]$RecordChunkSeconds = 180,
    [switch]$NoRecord,
    [switch]$WaitCode,
    [int]$WaitCodeSec = 180,
    [string]$ExePath,
    [int]$Width = 960,
    [int]$Height = 540,
    [Alias('WhatIf')][switch]$DryRun
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"

# `powershell -File` passes 'a','b' as ONE string 'a,b'; split so both call styles work.
$ExeArgs = @($ExeArgs | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$ExeClientArgs = @($ExeClientArgs | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

$root = $script:RepoRoot
if (-not $ExePath) { $ExePath = Join-Path $root 'Builds\Windows\CluckWars.exe' }
$adb = Get-AdbPath
$serial = Get-PhoneSerial -Adb $adb -Override $Serial
$tag = if ($DryRun) { '[dry-run] ' } else { '' }

function Show([string]$s) { Write-Host "$tag$s" }
function QuoteArg([string]$a) { if ($a -match '\s') { return '"' + $a + '"' } return $a }

# ---- device -------------------------------------------------------------------------------------
$needPhone = (-not $NoRecord) -or $LaunchPhone -or $InstallApk -or $PhoneExtras
$model = ''
if ($serial) {
    $model = ((& $adb -s $serial shell getprop ro.product.model) -join '').Trim()
    Show "device: $serial ($model)"
} elseif ($needPhone) {
    Write-Warning 'No physical adb device in state "device" found. Phone logging/recording is skipped (EXE clients still start).'
    $NoRecord = $true
    if ($LaunchPhone -or $InstallApk) { throw 'LaunchPhone/InstallApk requested but no device is connected.' }
}

# ---- session dir --------------------------------------------------------------------------------
$startUtc = [DateTime]::UtcNow
$session = Join-Path $root ('Captures\session-{0:yyyyMMdd-HHmmss}' -f $startUtc)
Show "session dir: $session"
if (-not $DryRun) { New-Item -ItemType Directory -Force $session | Out-Null }

$editorLogPath = Join-Path $env:LOCALAPPDATA 'Unity\Editor\Editor.log'
$editorLen = 0
if (Test-Path $editorLogPath) { $editorLen = (Get-Item $editorLogPath).Length }
Show "Editor.log: $editorLogPath  (offset at start = $editorLen bytes)"

$sj = [ordered]@{
    startUtc      = $startUtc.ToString('o')
    dryRun        = [bool]$DryRun
    adb           = $adb
    deviceSerial  = $serial
    deviceModel   = $model
    package       = $script:AppPackage
    editorLog     = [ordered]@{ path = $editorLogPath; startOffset = $editorLen }
    phoneLog      = $null
    logcatPid     = $null
    recLoopPid    = $null
    recordChunkSeconds = $RecordChunkSeconds
    phoneExtras   = $null
    exeClients    = @()
}
function Save-Session {
    if ($DryRun) { return }
    Write-Utf8 (Join-Path $session 'session.json') ($sj | ConvertTo-Json -Depth 6)
}

# ---- APK install --------------------------------------------------------------------------------
if ($InstallApk) {
    $apk = Get-ChildItem (Join-Path $root 'Builds\Android\CluckWars-*.apk') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $apk) { throw 'No Builds\Android\CluckWars-*.apk found.' }
    Show ("adb -s $serial install -r `"$($apk.FullName)`"   (APK built {0:u})" -f $apk.LastWriteTimeUtc)
    if (-not $DryRun) {
        $out = & $adb -s $serial install -r $apk.FullName 2>&1
        $out | ForEach-Object { Write-Host $_ }
        if (($out -join ' ') -notmatch 'Success') { throw 'adb install failed (see output above).' }
    }
    $sj.apk = $apk.FullName
}

# ---- logcat + screenrecord ----------------------------------------------------------------------
if ($serial -and -not $NoRecord) {
    $phoneLog = Join-Path $session 'phone.log'
    $sj.phoneLog = $phoneLog
    Show "adb -s $serial logcat -c"
    Show "background: adb -s $serial logcat -v threadtime  > $phoneLog   (stderr -> phone.err.log)"
    Show "background: rec-loop.ps1 -ChunkSeconds $RecordChunkSeconds  (adb shell screenrecord --time-limit N /sdcard/cw_rec_<k>.mp4 -> screenrec\)"
    if (-not $DryRun) {
        & $adb -s $serial logcat -c
        $lc = Start-Process -FilePath $adb -ArgumentList @('-s', $serial, 'logcat', '-v', 'threadtime') `
            -RedirectStandardOutput $phoneLog -RedirectStandardError (Join-Path $session 'phone.err.log') `
            -WindowStyle Hidden -PassThru
        $sj.logcatPid = $lc.Id
        $psExe = (Get-Process -Id $PID).Path
        $rec = Start-Process -FilePath $psExe -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (QuoteArg (Join-Path $PSScriptRoot 'rec-loop.ps1')),
            '-Adb', (QuoteArg $adb), '-Serial', $serial, '-SessionDir', (QuoteArg $session), '-ChunkSeconds', $RecordChunkSeconds)
        $sj.recLoopPid = $rec.Id
    }
} elseif ($serial) {
    Show 'recording skipped (-NoRecord)'
}
Save-Session

# ---- EXE clients (windowed, reuse run-clients.ps1 conventions; 700 ms stagger) --------------------
if ($ExeClients -gt 0 -and -not (Test-Path $ExePath)) {
    if ($DryRun) { Write-Warning "EXE not found at $ExePath (dry-run continues)" }
    else { throw "EXE not found at $ExePath - build it first (Ctrl+Shift+W)." }
}
for ($i = 1; $i -le $ExeClients; $i++) {
    $log = Join-Path $session "client$i.log"
    $exeArgList = @('-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height",
              '-logFile', (QuoteArg $log))
    if ($ExeClientArgs.Count -ge $i -and $ExeClientArgs[$i - 1]) { $exeArgList += @($ExeClientArgs[$i - 1] -split '\s+' | Where-Object { $_ }) }
    $exeArgList += $ExeArgs
    Show ("Start-Process `"$ExePath`" " + ($exeArgList -join ' '))
    $entry = [ordered]@{ index = $i; log = $log; pid = $null; args = ($exeArgList -join ' ') }
    if (-not $DryRun) {
        $p = Start-Process -FilePath $ExePath -ArgumentList $exeArgList -PassThru
        $entry.pid = $p.Id
        Start-Sleep -Milliseconds 700
    }
    $sj.exeClients += $entry
}
Save-Session

# ---- phone app launch ---------------------------------------------------------------------------
if ($LaunchPhone) {
    $extras = Convert-Extras $PhoneExtras
    $sj.phoneExtras = $extras
    $amArgs = @('-s', $serial, 'shell', 'am', 'start', '-n', "$($script:AppPackage)/$($script:AppActivity)")
    foreach ($k in $extras.Keys) { $amArgs += @('-e', $k, $extras[$k]) }
    Show "adb -s $serial shell am force-stop $($script:AppPackage)"
    Show ("adb " + ($amArgs -join ' '))
    if (-not $DryRun) {
        & $adb -s $serial shell am force-stop $script:AppPackage
        Start-Sleep -Milliseconds 800
        & $adb @amArgs
    }
}
Save-Session

# ---- join code ----------------------------------------------------------------------------------
if ($WaitCode) {
    if ($DryRun) { Show "would wait up to ${WaitCodeSec}s for 'AutoTest code=XXXXXX' in phone.log / client*.log / Editor.log" }
    else {
        Write-Host "Waiting up to ${WaitCodeSec}s for the AutoTest join code..."
        $code = Wait-AutoTestCode -Session $session -TimeoutSec $WaitCodeSec
        if ($code) { Write-Host "JOIN CODE: $code" } else { Write-Warning 'No AutoTest code seen (host not started yet? run get-code.ps1 later).' }
    }
}

Write-Host ''
Show "Stop + merge:  .\tools\test-session\stop.ps1 -Session `"$session`""
Show "Join code:     .\tools\test-session\get-code.ps1 -Session `"$session`""
Show "Screenshots:   .\tools\test-session\shot.ps1 -All -Session `"$session`""
