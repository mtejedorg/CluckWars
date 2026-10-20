<#
.SYNOPSIS
  Stop a test session started by start.ps1: finalize the last screenrecord chunk, stop logcat,
  slice Editor.log, then run merge.ps1 (merged.log + findings.txt).

.DESCRIPTION
  Order matters: stop.flag (rec loop will not start a new chunk) -> `adb shell pkill -INT screenrecord`
  (finalizes the in-flight mp4) -> wait for the rec loop to pull it -> kill logcat -> pull any leftover
  /sdcard/cw_rec_*.mp4 -> copy the Editor.log bytes written since start (offset in session.json; if the
  file is now shorter the Editor restarted and the whole file is taken) -> merge.

.PARAMETER StopApp      also `am force-stop` the game on the phone
.PARAMETER StopClients  also close the EXE clients launched by start.ps1
.PARAMETER SkipMerge    do not run merge.ps1
#>
param(
    [string]$Session,
    [switch]$StopApp,
    [switch]$StopClients,
    [switch]$SkipMerge,
    [Alias('WhatIf')][switch]$DryRun
)
$ErrorActionPreference = 'Continue'
. "$PSScriptRoot\lib.ps1"
if (-not $Session) { $Session = Get-LatestSessionDir }
if (-not $Session -or -not (Test-Path $Session)) { throw 'No session dir (pass -Session).' }
$Session = (Resolve-Path $Session).Path
$sj = Read-SessionJson $Session
if (-not $sj) { throw "No session.json in $Session" }
$adb = $sj.adb; if (-not $adb) { $adb = Get-AdbPath }
$serial = $sj.deviceSerial
$tag = if ($DryRun) { '[dry-run] ' } else { '' }
function Show([string]$s) { Write-Host "$tag$s" }

function Stop-Tracked([int]$ProcId, [string[]]$NameLike) {
    if (-not $ProcId) { return }
    $p = Get-Process -Id $ProcId -ErrorAction SilentlyContinue
    if (-not $p) { return }
    $ok = $false
    foreach ($n in $NameLike) { if ($p.ProcessName -like $n) { $ok = $true } }
    if (-not $ok) { Show "pid $ProcId is now '$($p.ProcessName)' (PID reused) - not killing"; return }
    Show "stopping pid $ProcId ($($p.ProcessName))"
    if (-not $DryRun) { Stop-Process -Id $ProcId -Force -ErrorAction SilentlyContinue }
}

# ---- screenrecord -------------------------------------------------------------------------------
if ($sj.recLoopPid) {
    Show 'create stop.flag; adb shell pkill -INT screenrecord'
    if (-not $DryRun) {
        New-Item -ItemType File -Force (Join-Path $Session 'stop.flag') | Out-Null
        if ($serial) { & $adb -s $serial shell pkill -INT screenrecord 2>&1 | Out-Null }
        $deadline = (Get-Date).AddSeconds(25)
        while ((Get-Date) -lt $deadline -and (Get-Process -Id ([int]$sj.recLoopPid) -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 400 }
    }
    Stop-Tracked ([int]$sj.recLoopPid) @('powershell', 'pwsh')
}

# ---- logcat -------------------------------------------------------------------------------------
if ($sj.logcatPid) {
    if (-not $DryRun) { Start-Sleep -Seconds 1 }   # let the last lines flush
    Stop-Tracked ([int]$sj.logcatPid) @('adb')
}

# ---- leftover chunks ----------------------------------------------------------------------------
if ($serial -and $sj.recLoopPid) {
    $recDir = Join-Path $Session 'screenrec'
    $left = @(& $adb -s $serial shell 'ls /sdcard/cw_rec_*.mp4 2>/dev/null') | ForEach-Object { "$_".Trim() } | Where-Object { $_ -like '/sdcard/cw_rec_*.mp4' }
    foreach ($r in $left) {
        $dest = Join-Path $recDir ('leftover_' + [IO.Path]::GetFileName($r))
        Show "adb pull $r -> $dest"
        if (-not $DryRun) { & $adb -s $serial pull $r $dest 2>&1 | Out-Null; & $adb -s $serial shell rm -f $r | Out-Null }
    }
}

# ---- optional app / client shutdown -------------------------------------------------------------
if ($StopApp -and $serial) { Show "adb shell am force-stop $($sj.package)"; if (-not $DryRun) { & $adb -s $serial shell am force-stop $sj.package } }
if ($StopClients) {
    foreach ($c in $sj.exeClients) {
        if ($c.pid) {
            $p = Get-Process -Id ([int]$c.pid) -ErrorAction SilentlyContinue
            if ($p -and $p.ProcessName -like 'CluckWars*') { Show "closing client $($c.index) (pid $($c.pid))"; if (-not $DryRun) { [void]$p.CloseMainWindow(); Start-Sleep -Milliseconds 500; if (-not $p.HasExited) { $p.Kill() } } }
        }
    }
}

# ---- Editor.log slice ---------------------------------------------------------------------------
$el = $sj.editorLog
if ($el -and $el.path -and (Test-Path $el.path)) {
    $dest = Join-Path $Session 'editor.log'
    $off = [long]$el.startOffset
    Show "slice Editor.log from byte $off -> $dest"
    if (-not $DryRun) {
        $in = New-Object IO.FileStream($el.path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            if ($off -gt $in.Length) { Write-Warning 'Editor.log is shorter than at start (Editor restarted?) - taking the whole file.'; $off = 0 }
            [void]$in.Seek($off, [IO.SeekOrigin]::Begin)
            $o = [IO.File]::Create($dest)
            try { $in.CopyTo($o) } finally { $o.Dispose() }
        } finally { $in.Dispose() }
        if ((Get-Item $dest).Length -eq 0) { Write-Warning 'Editor.log slice is empty (Editor not running / not logging?).' }
    }
} else { Write-Warning 'Editor.log not found - no editor source in the merge.' }

if (-not $DryRun) {
    $sj | Add-Member -NotePropertyName endUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
    Write-Utf8 (Join-Path $Session 'session.json') ($sj | ConvertTo-Json -Depth 6)
}

if ($SkipMerge) { Show 'merge skipped'; return }
Show "merge.ps1 -Session $Session"
if (-not $DryRun) { & (Join-Path $PSScriptRoot 'merge.ps1') -Session $Session }
