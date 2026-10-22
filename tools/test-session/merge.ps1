<#
.SYNOPSIS
  Merge phone.log (logcat), editor.log and client*.log of a test session into one UTC timeline
  (merged.log) and extract findings.txt (warnings/errors, exceptions, perf hitches, visual-integrity
  problems, cross-peer Sync mismatches, build-SHA mismatches).

.DESCRIPTION
  Game line format:  [HH:mm:ss.fff][mm:ss.fff][Level][Source] message     (1st bracket = UTC wall clock)
  Decisions:
   * Phone: only logcat lines with tag "Unity" are timeline entries; plus crash lines of other tags
     (AndroidRuntime/DEBUG FATAL, "Fatal signal", ANR) as Source "Android", dated relative to the
     nearest preceding Unity line (logcat time is phone-local, not UTC).
   * Unstamped lines (stack traces, engine noise) belong to the previous stamped line. Pure stack-trace
     boilerplate and blank lines are dropped unless the parent is Warn/Error/Exception. At most 40
     continuation lines are kept per entry. An unstamped "...Exception:" line after a non-Warn/Error
     parent becomes its own synthetic Error entry (source "Unity") so uncaught exceptions are never lost.
   * Date comes from the "[Session] ... date=yyyy-MM-dd" line (resets per process), else the date of
     session.json startUtc. Midnight rollover = time-of-day jumping backwards by more than 6 h.
   * Sort key: (UTC ticks, global sequence) so equal timestamps keep per-source line order.
   * Sync mismatch rules (per source pair): heartbeats pair by tick t (nearest within -TickTolerance,
     default 3, and within 15 s wall clock); compare st exact, rem +-0.5 s, per chicken hp/cg/cs exact,
     x/z +-1.5 m, bases and piles exact; a field must differ on 2 consecutive paired heartbeats
     (-Persist) to be reported; chicken ids present on one peer only are reported as GHOST on the same rule.
     Lag vs stuck (CW-226, every field): the owner prints its newest tick, the others their newest snapshot, so a
     value that changes near a heartbeat differs for a beat or two while BOTH sides keep changing (swapped cs pairs,
     a pile refill the clients see one beat later). A mismatch only counts when, over -Persist consecutive beats, at
     least one side's value stays constant (a remote stuck / an update that never arrives; a repeated pair is a case
     of this). For cs a side is only constant if it also saw no flag change between the beats (fl absent/0).
     cs findings append the owner (own=1), each side's flag changes (fl=) and the owner's slow sources (src=).

.EXAMPLE
  .\tools\test-session\merge.ps1 -Session .\Captures\session-20261020-173000
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Session,
    [double]$RemTolerance = 0.5,
    [double]$PosTolerance = 3.0,   # owner leads remote peers by interpolation delay (~0.25 s x 12 m/s); real divergence is caught by World hash + invariants
    [int]$TickTolerance = 3,
    [double]$TimeGuardSec = 15,
    [int]$Persist = 2,
    [double]$MidnightJumpHours = 6,
    [double]$P99ThresholdMs = 50
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"
$Session = (Resolve-Path $Session).Path
$inv = [Globalization.CultureInfo]::InvariantCulture

# ---------------------------------------------------------------------------------------------
# regexes
# ---------------------------------------------------------------------------------------------
$rxStamp = New-Object Text.RegularExpressions.Regex('^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\]\[[\d:]+\.\d{3}\]\[(Verbose|Debug|Info|Warn|Error)\]\[([^\]]+)\] ?(.*)$')
$rxLogcat = New-Object Text.RegularExpressions.Regex('^\d\d-\d\d (\d\d):(\d\d):(\d\d)\.(\d{3})\s+\d+\s+\d+\s+([VDIWEFA])\s+(.+?)\s*:\s?(.*)$')
$rxDate = New-Object Text.RegularExpressions.Regex('date=(\d{4}-\d\d-\d\d)')
$rxSha = New-Object Text.RegularExpressions.Regex('sha=(\S+)')
$rxBoiler = New-Object Text.RegularExpressions.Regex('^\s*$|^\s*at\s|^\(Filename:|\(at [^)]+:\d+\)\s*$|^\s*(UnityEngine|UnityEditor|System|Zenject|Fusion|Cysharp)\.[\w\.<>`+]*:[\w<>\.\[\]]+\s*\(')
$rxExcLine = New-Object Text.RegularExpressions.Regex('^[\w\.]*Exception\b')
$rxAndroidCrash = New-Object Text.RegularExpressions.Regex('FATAL EXCEPTION|Fatal signal|\bANR in\b|\*\*\* \*\*\* \*\*\*')
$maxCont = 40
# Raw engine warnings (not ILogService lines, so they arrive unstamped) that must never silently return.
# CW-207: a non-legacy clip sampled without an Animator in a player prints this per call (thousands per run).
$rxEngineWarn = New-Object Text.RegularExpressions.Regex('Non-Legacy animations cannot be sampled')
$engineWarn = @{}      # src -> @{ Count; Ticks; Text }

# ---------------------------------------------------------------------------------------------
# session info
# ---------------------------------------------------------------------------------------------
$sj = Read-SessionJson $Session
$fallbackDate = [DateTime]::UtcNow.Date
if ($sj -and $sj.startUtc) { $fallbackDate = ([DateTime]::Parse([string]$sj.startUtc, $inv, [Globalization.DateTimeStyles]::RoundtripKind)).ToUniversalTime().Date }

$sourceFiles = New-Object System.Collections.Generic.List[object]
if (Test-Path (Join-Path $Session 'phone.log')) { $sourceFiles.Add(@{ Name = 'phone'; Path = (Join-Path $Session 'phone.log'); Kind = 'logcat' }) }
if (Test-Path (Join-Path $Session 'editor.log')) { $sourceFiles.Add(@{ Name = 'editor'; Path = (Join-Path $Session 'editor.log'); Kind = 'unity' }) }
foreach ($f in (Get-ChildItem $Session -Filter 'client*.log' | Sort-Object { [regex]::Replace($_.Name, '\d+', { param($m) $m.Value.PadLeft(6, '0') }) })) {
    $sourceFiles.Add(@{ Name = [IO.Path]::GetFileNameWithoutExtension($f.Name); Path = $f.FullName; Kind = 'unity' })
}
if ($sourceFiles.Count -eq 0) { throw "No phone.log / editor.log / client*.log in $Session" }

# ---------------------------------------------------------------------------------------------
# parse
# ---------------------------------------------------------------------------------------------
$all = New-Object System.Collections.Generic.List[object]
$sessionInfo = New-Object System.Collections.Generic.List[object]   # {Src, Sha, Line}
$stats = @{}
$script:seq = 0

function New-Entry($src, $ticks, $level, $source, $msg) {
    $script:seq++
    return [pscustomobject]@{
        Src = $src; Ticks = $ticks; Level = $level; Source = $source; Message = $msg
        Cont = (New-Object System.Collections.Generic.List[string]); ContDropped = 0
        Seq = $script:seq; LcTod = -1.0
    }
}

foreach ($sf in $sourceFiles) {
    $src = $sf.Name
    $lines = Read-SharedLines $sf.Path
    $date = $fallbackDate
    $prevTod = $null
    $cur = $null
    $anchor = $null        # last Unity-stamped entry (phone) for dating Android crash lines
    $st = @{ Lines = $lines.Count; Entries = 0; Dropped = 0 }

    foreach ($line in $lines) {
        $msg = $line
        $lcTod = -1.0
        if ($sf.Kind -eq 'logcat') {
            $lm = $rxLogcat.Match($line)
            if (-not $lm.Success) { continue }          # "--------- beginning of ..." etc.
            $lcTod = [int]$lm.Groups[1].Value * 3600 + [int]$lm.Groups[2].Value * 60 + [int]$lm.Groups[3].Value + [int]$lm.Groups[4].Value / 1000.0
            $tag = $lm.Groups[6].Value.Trim()
            $msg = $lm.Groups[7].Value
            if ($tag -ne 'Unity') {
                if ($anchor -and $rxAndroidCrash.IsMatch($msg) -and ($tag -in 'AndroidRuntime', 'DEBUG', 'ActivityManager', 'libc')) {
                    $d = $lcTod - $anchor.LcTod
                    if ($d -lt -43200) { $d += 86400 } elseif ($d -gt 43200) { $d -= 86400 }
                    $e = New-Entry $src ($anchor.Ticks + [long]($d * 1e7)) 'Error' 'Android' "[$tag] $msg"
                    $all.Add($e); $st.Entries++
                    $cur = $e
                }
                continue
            }
        }

        if ($rxEngineWarn.IsMatch($msg)) {
            if (-not $engineWarn.ContainsKey($src)) { $engineWarn[$src] = @{ Count = 0; Ticks = $(if ($cur) { $cur.Ticks } else { $date.Ticks }); Text = $msg.Trim() } }
            $engineWarn[$src].Count++
        }

        $m = $rxStamp.Match($msg)
        if ($m.Success) {
            $tod = [int]$m.Groups[1].Value * 3600 + [int]$m.Groups[2].Value * 60 + [int]$m.Groups[3].Value + [int]$m.Groups[4].Value / 1000.0
            $level = $m.Groups[5].Value
            $source = $m.Groups[6].Value
            $text = $m.Groups[7].Value
            if ($source -eq 'Session') {
                $dm = $rxDate.Match($text)
                if ($dm.Success) { $date = [DateTime]::ParseExact($dm.Groups[1].Value, 'yyyy-MM-dd', $inv); $prevTod = $null }
                $sm = $rxSha.Match($text)
                $sessionInfo.Add([pscustomobject]@{ Src = $src; Sha = $(if ($sm.Success) { $sm.Groups[1].Value } else { '?' }); Line = $text })
            }
            if ($null -ne $prevTod -and $tod -lt ($prevTod - $MidnightJumpHours * 3600)) { $date = $date.AddDays(1) }
            $prevTod = $tod
            $ticks = $date.Ticks + [long]($tod * 1e7)
            $cur = New-Entry $src $ticks $level $source $text
            $cur.LcTod = $lcTod
            $all.Add($cur); $st.Entries++
            if ($sf.Kind -eq 'logcat') { $anchor = $cur }
            continue
        }

        # ---- unstamped line -------------------------------------------------------------------
        if ($null -eq $cur) { $st.Dropped++; continue }
        $parentLoud = ($cur.Level -eq 'Warn' -or $cur.Level -eq 'Error' -or $cur.Message -match 'Exception')
        if ($rxExcLine.IsMatch($msg) -and -not ($cur.Level -eq 'Warn' -or $cur.Level -eq 'Error')) {
            $e = New-Entry $src $cur.Ticks 'Error' 'Unity' $msg
            $e.LcTod = $cur.LcTod
            $all.Add($e); $st.Entries++
            $cur = $e
            continue
        }
        if ($msg -match '^\s*$') { $st.Dropped++; continue }
        if ($rxBoiler.IsMatch($msg) -and -not $parentLoud -and $msg -notmatch 'Exception') { $st.Dropped++; continue }
        if ($cur.Cont.Count -ge $maxCont) { $cur.ContDropped++; $st.Dropped++; continue }
        $cur.Cont.Add($msg.TrimEnd())
    }
    $stats[$src] = $st
}

$sorted = $all | Sort-Object -Property Ticks, Seq

# ---------------------------------------------------------------------------------------------
# merged.log
# ---------------------------------------------------------------------------------------------
function Format-Time($ticks) { return ([DateTime]::new([long]$ticks, [DateTimeKind]::Utc)).ToString('yyyy-MM-dd HH:mm:ss.fff', $inv) + 'Z' }
function Format-Entry($e) { return ('{0} [{1,-7}] [{2,-5}][{3}] {4}' -f (Format-Time $e.Ticks), $e.Src, $e.Level, $e.Source, $e.Message) }

$out = New-Object System.Collections.Generic.List[string]
foreach ($e in $sorted) {
    $out.Add((Format-Entry $e))
    foreach ($c in $e.Cont) { $out.Add('    | ' + $c) }
    if ($e.ContDropped -gt 0) { $out.Add("    | ... +$($e.ContDropped) more lines") }
}
Write-Utf8 (Join-Path $Session 'merged.log') (($out -join "`r`n") + "`r`n")

# ---------------------------------------------------------------------------------------------
# findings
# ---------------------------------------------------------------------------------------------
$findings = New-Object System.Collections.Generic.List[object]   # {Cats, Ticks, Seq, Src, Text}
function Add-Finding($cats, $e, $text) {
    $findings.Add([pscustomobject]@{ Cats = $cats; Ticks = $e.Ticks; Seq = $e.Seq; Src = $e.Src; Entry = $e; Text = $text })
}

foreach ($e in $sorted) {
    $cats = New-Object System.Collections.Generic.List[string]
    $contText = $e.Cont -join "`n"
    if ($e.Level -eq 'Error') { $cats.Add('ERROR') } elseif ($e.Level -eq 'Warn') { $cats.Add('WARN') }
    if (($e.Message -match 'Exception') -or ($contText -match 'Exception')) { $cats.Add('EXCEPTION') }

    if ($e.Source -eq 'Perf') {
        $isSummary = $e.Message -match '\bp99\b'
        if ($isSummary) {
            $pm = [regex]::Match($e.Message, 'p99\D{0,4}([\d.]+)')
            if ($pm.Success -and [double]::Parse($pm.Groups[1].Value, $inv) -gt $P99ThresholdMs) { $cats.Add('PERF') }
        } elseif ($e.Level -in 'Warn', 'Error' -or $e.Message -match 'hitch|spike|stall|slow frame|frame\s*[=:]?\s*[\d.]+\s*ms') {
            $cats.Add('PERF')
        }
    }
    elseif ($e.Source -eq 'Visual') {
        if ($e.Level -ne 'Warn' -and $e.Level -ne 'Error') {
            foreach ($kv in [regex]::Matches($e.Message, '(\w+)\s*[=:]\s*(\d+)')) {
                if ($kv.Groups[1].Value -match 'offender|null|error|magenta|pink|nan|inf|outside|unsupported|missing|invalid|bad' -and [int]$kv.Groups[2].Value -gt 0) { $cats.Add('VISUAL'); break }
            }
        } else { $cats.Add('VISUAL') }
    }
    if ($cats.Count -gt 0) { Add-Finding $cats.ToArray() $e (Format-Entry $e) }
}

# ---- Engine warnings that must stay at zero (CW-207) ---------------------------------------------
foreach ($src in @($engineWarn.Keys)) {
    $w = $engineWarn[$src]
    $we = New-Entry $src $w.Ticks 'Warn' 'Unity' $w.Text
    Add-Finding @('WARN') $we ("$(Format-Time $w.Ticks) [$($src.PadRight(7))] [Warn ][Unity] x$($w.Count) engine warning (expected 0): $($w.Text)")
}

# ---- Sync heartbeats ----------------------------------------------------------------------------
$rxSync = New-Object Text.RegularExpressions.Regex('(?:^|\s)t=(\d+)\s+st=(\S+)\s+rem=(-?[\d.]+)\s*\|(.*?)\|\s*bases=(.*?)\s*\|\s*piles=(.*)$')
$beatsBySrc = @{}
foreach ($e in $sorted) {
    if ($e.Source -ne 'Sync') { continue }
    $m = $rxSync.Match($e.Message)
    if (-not $m.Success) { continue }
    $ch = @{}
    foreach ($seg in ($m.Groups[4].Value -split ';')) {
        $seg = $seg.Trim(); if (-not $seg) { continue }
        $ci = $seg.IndexOf(':'); if ($ci -lt 1) { continue }
        $id = $seg.Substring(0, $ci).Trim()
        $f = @{}
        foreach ($p in ($seg.Substring($ci + 1) -split ',')) {
            $pi = $p.IndexOf('=')
            if ($pi -gt 0) { $f[$p.Substring(0, $pi).Trim()] = $p.Substring($pi + 1).Trim() }
        }
        $ch[$id] = $f
    }
    $bases = @{}
    foreach ($b in ($m.Groups[5].Value -split ',')) { $bi = $b.IndexOf('='); if ($bi -gt 0) { $bases[$b.Substring(0, $bi).Trim()] = $b.Substring($bi + 1).Trim() } }
    $piles = @($m.Groups[6].Value.Trim() -split ',' | Where-Object { $_ -ne '' } | ForEach-Object { $_.Trim() })
    $beat = [pscustomobject]@{ Src = $e.Src; T = [long]$m.Groups[1].Value; St = $m.Groups[2].Value; Rem = [double]::Parse($m.Groups[3].Value, $inv); Ch = $ch; Bases = $bases; Piles = $piles; Entry = $e }
    if (-not $beatsBySrc.ContainsKey($e.Src)) { $beatsBySrc[$e.Src] = New-Object System.Collections.Generic.List[object] }
    $beatsBySrc[$e.Src].Add($beat)
}


# ---- Round-start invariants (from Sync heartbeats) ------------------------------------------------
# Lesson of 2026-10-20 (CW-221): a log line saying "Teleported" is not proof the chicken moved. These rules assert the
# STATE of the world at the first Active heartbeat of every round (rem >= 44.99, intro frozen), on every source:
#   * every chicken sits in a corner quadrant (|x| and |z| >= $CornerMin) and no two chickens share a quadrant
#   * cargo == 0, every base food == 0
#   * the pile amounts equal that source's round-1 snapshot
# Violations are filed under the INVARIANT category.
$CornerMin = 17.5
foreach ($src in @($beatsBySrc.Keys)) {
    $round = 0; $prevSt = ''; $firstPiles = $null
    foreach ($b in $beatsBySrc[$src]) {
        $isStart = ($b.St -eq 'Active' -and $b.Rem -ge 44.99 -and $prevSt -ne 'Active')
        $prevSt = $b.St
        if (-not $isStart) { if ($b.St -ne 'Active') { } ; continue }
        $round++
        $quads = @{}
        foreach ($id in $b.Ch.Keys) {
            $c = $b.Ch[$id]
            if ($c['hp'] -eq '0') { continue }
            $x = 0.0; $z = 0.0
            [void][double]::TryParse($c['x'], [Globalization.NumberStyles]::Float, $inv, [ref]$x)
            [void][double]::TryParse($c['z'], [Globalization.NumberStyles]::Float, $inv, [ref]$z)
            if ([math]::Abs($x) -lt $CornerMin -or [math]::Abs($z) -lt $CornerMin) {
                Add-Finding @('INVARIANT') $b.Entry ("round $round start on $src (t=$($b.T)): chicken $id is NOT at a corner: x=$($x.ToString('0.0',$inv)) z=$($z.ToString('0.0',$inv)) (expected |x|,|z| >= $CornerMin)")
            } else {
                $q = "$([math]::Sign($x)),$([math]::Sign($z))"
                if ($quads.ContainsKey($q)) { Add-Finding @('INVARIANT') $b.Entry ("round $round start on $src (t=$($b.T)): chickens $($quads[$q]) and $id share corner quadrant $q") }
                else { $quads[$q] = $id }
            }
            if ($c.ContainsKey('cg') -and $c['cg'] -ne '0') { Add-Finding @('INVARIANT') $b.Entry ("round $round start on $src (t=$($b.T)): chicken $id carries cg=$($c['cg']) at round start (expected 0)") }
        }
        foreach ($k in $b.Bases.Keys) {
            $v = 0.0; [void][double]::TryParse($b.Bases[$k], [Globalization.NumberStyles]::Float, $inv, [ref]$v)
            if ($v -ne 0) { Add-Finding @('INVARIANT') $b.Entry ("round $round start on $src (t=$($b.T)): base $k food=$($b.Bases[$k]) at round start (expected 0)") }
        }
        if ($round -eq 1) { $firstPiles = @($b.Piles) }
        elseif ($firstPiles) {
            $now = @($b.Piles)
            if (($now -join ',') -ne ($firstPiles -join ',')) { Add-Finding @('INVARIANT') $b.Entry ("round $round start on $src (t=$($b.T)): piles differ from round 1: [$($now -join ',')] vs [$($firstPiles -join ',')]") }
        }
    }
}

# ---- Home-corner identity (CW-233): no two real players share a HomeCornerIndex ---------------------------------
# Every peer prints `Respawn check chicken=<id> home=<HomeCornerIndex> ... round=<n>` once per round for every chicken
# (Source Reset; P<PlayerId> = a real player, B<corner> = a bot). Two P ids with the same home on one peer in one round
# is the CW-233 spawn race: both chickens stand on one corner, one of them has no base and the master warns "already
# claimed" every tick. The quadrant rule above sees the same thing by position; this one names the stamped identity.
$rxRespawn = New-Object Text.RegularExpressions.Regex('^Respawn check chicken=(P\d+) home=(-?\d+) .*\bround=(\d+)')
$homeSeen = @{}     # "src|round|home" -> first player id
$homeFiled = @{}
foreach ($e in $sorted) {
    if ($e.Source -ne 'Reset') { continue }
    $m = $rxRespawn.Match($e.Message); if (-not $m.Success) { continue }
    $id = $m.Groups[1].Value; $hc = $m.Groups[2].Value; $rd = $m.Groups[3].Value
    if ([int]$hc -lt 0) { continue }   # unstamped: the corner-quadrant / spawn rules own that case
    $key = "$($e.Src)|$rd|$hc"
    if (-not $homeSeen.ContainsKey($key)) { $homeSeen[$key] = $id; continue }
    if ($homeSeen[$key] -eq $id -or $homeFiled.ContainsKey($key)) { continue }
    $homeFiled[$key] = 1
    Add-Finding @('INVARIANT') $e ("round $rd on $($e.Src): real players $($homeSeen[$key]) and $id share HomeCornerIndex $hc (CW-233 spawn-corner race: one of them has no base)")
}

# ---- Round-start leftovers (B10): nothing the last round placed survives the reset -------------------------------
# (a) Builds with the B10 probe print " zn=<zones> au=<auras> dc=<decoys>" on every round-start Invariant ok|FAIL line
#     (0.5 s after Active, inside the intro: nobody can cast). Any non-zero count is a finding, once per source and round.
# (b) Any build (also the ones before B10): the 2 s World line's zones=<hash>/<n>. From $LeftoverGraceSec after a round
#     leaves Ended (PLAY AGAIN; the despawn is one tick + replication away) until $IntroGraceSec after the next Active
#     (the intro is 3 s: still nobody can cast), n must be 0. WP3 builds also print auras=<hash>/<n> (Caster-anchored
#     zones); they are added to the count.
$LeftoverGraceSec = 1.0
$IntroGraceSec = 2.5
$rxStartCounts = New-Object Text.RegularExpressions.Regex('^Invariant (?:ok|FAIL)\b(?=.*\bphase=start\b)(?=.*\bround=(\d+)\b).*\bzn=(\d+) au=(\d+) dc=(\d+)')
$rxStateLine = New-Object Text.RegularExpressions.Regex('^Invariant state (\w+)->(\w+) round=(\d+)')
$rxWorldZones = New-Object Text.RegularExpressions.Regex('^World t=(\d+) .*\bzones=\w+/(\d+)(?:.*\bauras=\w+/(\d+))?')
$leftoverSeen = @{}
$leftWin = @{}   # src -> @{ From; Until; Round }
foreach ($e in $sorted) {
    if ($e.Source -ne 'Invariant') { continue }
    $m = $rxStartCounts.Match($e.Message)
    if ($m.Success) {
        $rd = $m.Groups[1].Value; $zn = [int]$m.Groups[2].Value; $au = [int]$m.Groups[3].Value; $dc = [int]$m.Groups[4].Value
        $key = "start|$($e.Src)|$rd"
        if (($zn + $au + $dc) -gt 0 -and -not $leftoverSeen.ContainsKey($key)) {
            $leftoverSeen[$key] = 1
            Add-Finding @('INVARIANT') $e ("round $rd start on $($e.Src): zn=$zn au=$au dc=$dc - zones/auras/decoys of the last round survived the reset (expected 0, B10)")
        }
        continue
    }
    $m = $rxStateLine.Match($e.Message)
    if ($m.Success) {
        $from = $m.Groups[1].Value; $to = $m.Groups[2].Value
        if ($from -eq 'Ended' -and $to -ne 'Ended') { $leftWin[$e.Src] = @{ From = $e.Ticks + [long]($LeftoverGraceSec * 1e7); Until = [long]::MaxValue; Round = [int]$m.Groups[3].Value } }
        elseif ($to -eq 'Active' -and $leftWin.ContainsKey($e.Src) -and $leftWin[$e.Src].Until -eq [long]::MaxValue) { $leftWin[$e.Src].Until = $e.Ticks + [long]($IntroGraceSec * 1e7) }
        elseif ($to -eq 'Ended') { $leftWin.Remove($e.Src) }
        continue
    }
    $m = $rxWorldZones.Match($e.Message)
    $liveZones = 0
    if ($m.Success) { $liveZones = [int]$m.Groups[2].Value + $(if ($m.Groups[3].Success) { [int]$m.Groups[3].Value } else { 0 }) }
    if ($m.Success -and $liveZones -gt 0 -and $leftWin.ContainsKey($e.Src)) {
        $w = $leftWin[$e.Src]
        if ($e.Ticks -lt $w.From -or $e.Ticks -gt $w.Until) { continue }
        $key = "world|$($e.Src)|$($w.Round)"
        if ($leftoverSeen.ContainsKey($key)) { continue }
        $leftoverSeen[$key] = 1
        Add-Finding @('INVARIANT') $e ("round reset after round $($w.Round) on $($e.Src) (World t=$($m.Groups[1].Value)): $liveZones ability zone(s) still alive after PLAY AGAIN, before anyone could cast (expected 0, B10)")
    }
}

# ---- Control RPC rejections (F4 / WP1): honest clients never trip the receiver-side control check ---------------
# ChickenController refuses an RPC_ApplyStun / RPC_ApplyRoot / RPC_ApplyAbilitySlow it cannot attribute or bound with a
# rate-limited Warn "<chicken>: rejected RPC_Apply<Kind>(...) from <player> (<Verdict>) - <reason>". With honest clients
# the count is 0 (TEST_STRATEGY: grep `rejected RPC_Apply` = 0): any line is a validation bound that no longer covers a
# real ability, or a caller that stopped naming its caster. One INVARIANT finding per source, with the count and the
# first line (a line may carry "(+N similar suppressed)", which is added to the count).
$rxCtlReject = New-Object Text.RegularExpressions.Regex('\brejected (RPC_Apply(?:Stun|Root|AbilitySlow))\(.*\) from \S+ \((\w+)\) - (?:.*\(\+(\d+) similar suppressed\))?')
$ctlRejects = [ordered]@{}   # src -> @{ Count; First; Kinds }
foreach ($e in $sorted) {
    if ($e.Message -notmatch '\brejected RPC_Apply') { continue }   # the stable prefix decides; the details are best effort
    if (-not $ctlRejects.Contains($e.Src)) { $ctlRejects[$e.Src] = @{ Count = 0; First = $e; Kinds = @{} } }
    $r = $ctlRejects[$e.Src]
    $m = $rxCtlReject.Match($e.Message)
    $r.Count += 1 + $(if ($m.Success -and $m.Groups[3].Success) { [int]$m.Groups[3].Value } else { 0 })
    $r.Kinds[$(if ($m.Success) { "$($m.Groups[1].Value)/$($m.Groups[2].Value)" } else { 'unparsed' })] = 1
}
foreach ($src in $ctlRejects.Keys) {
    $r = $ctlRejects[$src]
    Add-Finding @('INVARIANT') $r.First ("control RPC rejections on ${src}: $($r.Count) [$((@($r.Kinds.Keys) | Sort-Object) -join ', ')] (expected 0 with honest clients, F4) - first: $($r.First.Message)")
}

# ---- Remote aura victim (WP4, the CW-217 lesson: state on a REMOTE peer, not logs) ---------------------------------
# Every peer's Invariant probe samples each rival deep inside a live Slow aura for its replicated ControlVfx.Snared and,
# when the aura is gone, logs `Invariant aura owner=<id> victim=<id> remote=<0|1> deep=<n> snared=<m> verdict=<ok|too-few|
# not-snared> round=<r>` (remote=1: the victim's state authority is not the aura's). (a) Any not-snared verdict is an
# INVARIANT finding, once per source. (b) Once a `cw_autoaura` peer logged `AutoTest autoaura: cast ...`, the session must
# hold at least one remote=1 verdict=ok line from some peer: an aura cast into a rival that no peer saw slow is the bug.
$rxAuraVerdict = New-Object Text.RegularExpressions.Regex('^Invariant aura owner=(\S+) victim=(\S+) remote=([01]) deep=(\d+) snared=(\d+) verdict=(\S+)')
$auraCasts = New-Object System.Collections.Generic.List[object]
$auraRemoteOk = 0
$auraNotSnared = [ordered]@{}   # src -> @{ Count; First }
foreach ($e in $sorted) {
    if ($e.Source -eq 'AutoTest' -and $e.Message -match '^AutoTest autoaura: cast ') { $auraCasts.Add($e); continue }
    if ($e.Source -ne 'Invariant') { continue }
    $m = $rxAuraVerdict.Match($e.Message); if (-not $m.Success) { continue }
    if ($m.Groups[6].Value -eq 'ok' -and $m.Groups[3].Value -eq '1') { $auraRemoteOk++ }
    if ($m.Groups[6].Value -eq 'not-snared') {
        if (-not $auraNotSnared.Contains($e.Src)) { $auraNotSnared[$e.Src] = @{ Count = 0; First = $e } }
        $auraNotSnared[$e.Src].Count++
    }
}
foreach ($src in $auraNotSnared.Keys) {
    $r = $auraNotSnared[$src]
    Add-Finding @('INVARIANT') $r.First ("aura victims not slowed on ${src}: $($r.Count) (a rival deep inside a Slow aura without its replicated Snared flag, WP4) - first: $($r.First.Message)")
}
if ($auraCasts.Count -gt 0 -and $auraRemoteOk -eq 0) {
    Add-Finding @('INVARIANT') $auraCasts[0] ("autoaura cast $($auraCasts.Count) aura(s) but no peer proved a remote victim slowed (expected >= 1 'Invariant aura ... remote=1 ... verdict=ok', WP4/CW-217) - first: $($auraCasts[0].Message)")
}

# ---- World-hash parity (CW-216/217 class: spawned objects positioned differently per peer) ---------------------
# The game logs `World t=<tick> static=<hash> n=<count> ... auras=<hash>/<n> ...` on every peer every 2 s (Source
# Invariant). Peers log on the same ticks, so for each tick every reporting peer must have the same `static` hash (bases,
# piles, pickups, World-anchored zones at 0.5 m resolution) and the same `auras` hash (WP3: Caster-anchored zones, keyed by
# owner id, radius bucket, effect and channels - never by position, which follows a moving chicken). A mismatch that lasts
# >= $WorldPersist consecutive shared ticks is an INVARIANT finding. Builds before WP3 print no auras= and are skipped there.
$WorldPersist = 2
function Test-WorldParity([string]$field, [Text.RegularExpressions.Regex]$rx, [string]$what) {
    $byT = @{}
    foreach ($e in $sorted) {
        if ($e.Source -ne 'Invariant') { continue }
        $m = $rx.Match($e.Message); if (-not $m.Success) { continue }
        $t = [long]$m.Groups[1].Value
        if (-not $byT.ContainsKey($t)) { $byT[$t] = @{ Entry = $e; Peers = @{} } }
        $byT[$t].Peers[$e.Src] = @{ H = $m.Groups[2].Value; N = $m.Groups[3].Value }
    }
    # Like-with-like persistence (same idea as Find-StuckWindow for Sync fields): right after a master migration objects keep
    # spawning and each peer's hash is only a beat behind the other's, so BOTH sides change between mismatching beats (lag).
    # A mismatch run counts only when, over its consecutive beats, some peer's hash stays CONSTANT (a missing / misplaced
    # object: CW-216/217) - which also covers the same mismatching pair repeating.
    $run = @(); $runFlagged = $false
    foreach ($t in ($byT.Keys | Sort-Object)) {
        $w = $byT[$t]
        if ($w.Peers.Count -lt 2) { continue }
        $distinct = @($w.Peers.Values | ForEach-Object { $_.H } | Sort-Object -Unique)
        if ($distinct.Count -le 1) { $run = @(); $runFlagged = $false; continue }
        $run += , @{ T = $t; W = $w }
        if ($run.Count -lt $WorldPersist -or $runFlagged) { continue }
        $stuckPeer = $null
        foreach ($peer in $w.Peers.Keys) {
            $hashes = @($run | ForEach-Object { if ($_.W.Peers.ContainsKey($peer)) { $_.W.Peers[$peer].H } else { '' } } | Sort-Object -Unique)
            if ($hashes.Count -eq 1 -and $hashes[0] -ne '') { $stuckPeer = $peer; break }
        }
        if ($null -eq $stuckPeer) { continue }   # every side kept changing: replication lag, not a stuck difference
        $runFlagged = $true
        $first = $run[0]
        $desc = ($first.W.Peers.Keys | Sort-Object | ForEach-Object { "$_=$($first.W.Peers[$_].H)(n=$($first.W.Peers[$_].N))" }) -join ' '
        Add-Finding @('INVARIANT') $first.W.Entry ("world $field hash differs across peers from t=$($first.T) ($WorldPersist+ consecutive heartbeats, $stuckPeer constant at $($w.Peers[$stuckPeer].H)): $desc - $what")
    }
}
Test-WorldParity 'static' (New-Object Text.RegularExpressions.Regex('^World t=(\d+) static=(\w+) n=(\d+)')) 'a spawned object (base/pile/pickup/zone) is positioned differently on some peers'
Test-WorldParity 'auras' (New-Object Text.RegularExpressions.Regex('^World t=(\d+) .*\bauras=(\w+)/(\d+)')) 'an aura (caster-anchored zone) is missing on some peer, or has another owner, radius, effect or channels there'

function Test-Num($a, $b, $tol) {
    $x = 0.0; $y = 0.0
    if ([double]::TryParse($a, [Globalization.NumberStyles]::Float, $inv, [ref]$x) -and [double]::TryParse($b, [Globalization.NumberStyles]::Float, $inv, [ref]$y)) { return ([math]::Abs($x - $y) -le $tol) }
    return ($a -eq $b)
}

# returns hashtable key -> @{Cat; Desc; A; B}
function Compare-Beats($a, $b) {
    $d = @{}
    if ($a.St -ne $b.St) { $d['st'] = @{ Cat = 'SYNC'; Desc = 'state'; A = $a.St; B = $b.St } }
    if ([math]::Abs($a.Rem - $b.Rem) -gt $RemTolerance) { $d['rem'] = @{ Cat = 'SYNC'; Desc = 'time remaining'; A = $a.Rem.ToString($inv); B = $b.Rem.ToString($inv) } }
    $ids = @($a.Ch.Keys) + @($b.Ch.Keys) | Sort-Object -Unique
    foreach ($id in $ids) {
        $ca = $a.Ch[$id]; $cb = $b.Ch[$id]
        if ($null -eq $ca -or $null -eq $cb) {
            $d["ghost:$id"] = @{ Cat = 'GHOST'; Desc = "chicken $id"; A = $(if ($ca) { 'present' } else { 'missing' }); B = $(if ($cb) { 'present' } else { 'missing' }) }
            continue
        }
        foreach ($fld in 'hp', 'cg') {
            $va = $ca[$fld]; $vb = $cb[$fld]
            if ("$va" -ne "$vb") { $d["c$id.$fld"] = @{ Cat = 'SYNC'; Desc = "chicken $id $fld"; A = "$va"; B = "$vb" } }
        }
        # cs carries the owner diagnostics (own/fl/src, CW-226) for the finding text
        $va = $ca['cs']; $vb = $cb['cs']
        if ("$va" -ne "$vb") {
            $d["c$id.cs"] = @{ Cat = 'SYNC'; Desc = "chicken $id cs"; A = "$va"; B = "$vb"
                OwnA = ($ca['own'] -eq '1'); OwnB = ($cb['own'] -eq '1'); FlA = $ca['fl']; FlB = $cb['fl']; SrcA = $ca['src']; SrcB = $cb['src'] }
        }
        foreach ($fld in 'x', 'z') {
            $va = $ca[$fld]; $vb = $cb[$fld]
            if (-not (Test-Num $va $vb $PosTolerance)) { $d["c$id.$fld"] = @{ Cat = 'SYNC'; Desc = "chicken $id $fld"; A = "$va"; B = "$vb" } }
        }
    }
    $bids = @($a.Bases.Keys) + @($b.Bases.Keys) | Sort-Object -Unique
    foreach ($id in $bids) {
        $va = $a.Bases[$id]; $vb = $b.Bases[$id]
        if ("$va" -ne "$vb") { $d["base$id"] = @{ Cat = 'SYNC'; Desc = "base $id food"; A = $(if ($null -eq $va) { '<absent>' } else { $va }); B = $(if ($null -eq $vb) { '<absent>' } else { $vb }) } }
    }
    $n = [math]::Max($a.Piles.Count, $b.Piles.Count)
    for ($i = 0; $i -lt $n; $i++) {
        $va = $(if ($i -lt $a.Piles.Count) { $a.Piles[$i] } else { '<absent>' })
        $vb = $(if ($i -lt $b.Piles.Count) { $b.Piles[$i] } else { '<absent>' })
        if ($va -ne $vb) { $d["pile$i"] = @{ Cat = 'SYNC'; Desc = "pile[$i]"; A = $va; B = $vb } }
    }
    return $d
}

# Longest stretch of a run's beats (all mismatching) over which at least one side's value stays constant: the stuck
# side. A stretch where both sides change every beat is lag (see header) and never qualifies. $null for an empty run.
# For cs a side is constant only if it also saw NO flag change since the previous beat (fl absent/0): equal values at
# the two sample instants with fl>0 in between are a toggling flag sampled in phase, not a stuck one (CW-226, sessions
# 163938 / 164644: owner Slowed at both beats with fl=2, remotes Free at both with fl=2).
function Find-StuckWindow($beats) {
    $best = $null
    foreach ($side in 'A', 'B') {
        $start = 0
        for ($i = 1; $i -le $beats.Count; $i++) {
            $fl = $(if ($i -lt $beats.Count) { $beats[$i].Info["Fl$side"] } else { $null })
            if ($i -lt $beats.Count -and "$($beats[$i].Info[$side])" -eq "$($beats[$i - 1].Info[$side])" -and (-not $fl -or $fl -eq '0')) { continue }
            $n = $i - $start
            if ($null -eq $best -or $n -gt $best.N) { $best = @{ N = $n; First = $beats[$start]; Last = $beats[$i - 1] } }
            $start = $i
        }
    }
    return $best
}

$pairStats = New-Object System.Collections.Generic.List[string]
$srcNames = @($sourceFiles | ForEach-Object { $_.Name } | Where-Object { $beatsBySrc.ContainsKey($_) })
for ($ia = 0; $ia -lt $srcNames.Count; $ia++) {
    for ($ib = $ia + 1; $ib -lt $srcNames.Count; $ib++) {
        $sa = $srcNames[$ia]; $sb = $srcNames[$ib]
        $A = $beatsBySrc[$sa].ToArray(); $B = $beatsBySrc[$sb].ToArray()
        $paired = New-Object System.Collections.Generic.List[object]
        $byT = @{}
        foreach ($bb in $B) { if (-not $byT.ContainsKey($bb.T)) { $byT[$bb.T] = New-Object System.Collections.Generic.List[object] }; $byT[$bb.T].Add($bb) }
        foreach ($ba in $A) {
            $best = $null
            for ($dt = 0; $dt -le $TickTolerance -and -not $best; $dt++) {
                foreach ($t in @(($ba.T - $dt), ($ba.T + $dt) | Select-Object -Unique)) {
                    if (-not $byT.ContainsKey([long]$t)) { continue }
                    foreach ($bb in $byT[[long]$t]) {
                        if (([math]::Abs($bb.Entry.Ticks - $ba.Entry.Ticks) / 1e7) -le $TimeGuardSec) { $best = $bb; break }
                    }
                    if ($best) { break }
                }
            }
            if ($best) { $paired.Add(@($ba, $best)) }
        }
        $pairStats.Add("$sa <-> $sb : $($paired.Count) paired heartbeats (of $($A.Count) / $($B.Count))")

        $runs = @{}        # key -> run
        $done = New-Object System.Collections.Generic.List[object]
        foreach ($pr in $paired) {
            $diff = Compare-Beats $pr[0] $pr[1]
            foreach ($k in @($runs.Keys)) {
                if (-not $diff.ContainsKey($k)) { $done.Add($runs[$k]); $runs.Remove($k) }
            }
            foreach ($k in $diff.Keys) {
                $beat = @{ T = $pr[0].T; Entry = $pr[0].Entry; Info = $diff[$k] }
                if ($runs.ContainsKey($k)) { $runs[$k].Beats.Add($beat) }
                else { $l = New-Object System.Collections.Generic.List[object]; $l.Add($beat); $runs[$k] = @{ Key = $k; Beats = $l } }
            }
        }
        foreach ($k in $runs.Keys) { $done.Add($runs[$k]) }
        foreach ($r in $done) {
            # cargo changes every few seconds while foraging/depositing: a 2-beat mismatch is sampling skew at a flush, so it
            # needs 2 more consecutive beats to count as a real divergence
            $need = if ($r.Key -like 'c*.cg') { $Persist + 2 } else { $Persist }
            $w = Find-StuckWindow $r.Beats
            if ($null -eq $w -or $w.N -lt $need) { continue }
            $inf = $w.First.Info
            $text = 't={1}..{2} {3} vs {4}: {5} {6}={7} {8}={9}  ({10} consecutive heartbeats, from {11})' -f $inf.Cat, $w.First.T, $w.Last.T, $sa, $sb, $inf.Desc, $sa, $inf.A, $sb, $inf.B, $w.N, (Format-Time $w.First.Entry.Ticks)
            if ($inf.ContainsKey('OwnA')) {
                # first heartbeat of the run: who simulates the bird, how often each side saw the flags change, what slows it
                $own = $(if ($inf.OwnA) { "$sa src=$(if ($inf.SrcA) { $inf.SrcA } else { 'none' })" } elseif ($inf.OwnB) { "$sb src=$(if ($inf.SrcB) { $inf.SrcB } else { 'none' })" } else { 'neither' })
                $text += ('  [owner={0} fl {1}={2} {3}={4}]' -f $own, $sa, $(if ($inf.FlA) { $inf.FlA } else { 0 }), $sb, $(if ($inf.FlB) { $inf.FlB } else { 0 }))
            }
            Add-Finding @($inf.Cat) $w.First.Entry $text
        }
    }
}

# ---------------------------------------------------------------------------------------------
# SHA comparison
# ---------------------------------------------------------------------------------------------
$shaLines = New-Object System.Collections.Generic.List[string]
$distinct = @($sessionInfo | ForEach-Object { $_.Sha } | Sort-Object -Unique)
foreach ($s in $sessionInfo) { $shaLines.Add(("  {0,-8} sha={1}" -f $s.Src, $s.Sha)) }
$shaMismatch = ($distinct.Count -gt 1)

# ---------------------------------------------------------------------------------------------
# findings.txt
# ---------------------------------------------------------------------------------------------
$sortedFindings = $findings | Sort-Object -Property Ticks, Seq
$catCounts = [ordered]@{ ERROR = 0; WARN = 0; EXCEPTION = 0; PERF = 0; VISUAL = 0; SYNC = 0; GHOST = 0; INVARIANT = 0 }
$srcCounts = @{}
foreach ($f in $findings) {
    foreach ($c in $f.Cats) { $catCounts[$c]++ }
    if (-not $srcCounts.ContainsKey($f.Src)) { $srcCounts[$f.Src] = 0 }
    $srcCounts[$f.Src]++
}

$r = New-Object System.Collections.Generic.List[string]
$r.Add("Cluck Wars test session findings  (generated $((Get-Date).ToUniversalTime().ToString('u')))")
$r.Add("Session: $Session")
if ($sj -and $sj.deviceModel) { $r.Add("Device: $($sj.deviceModel) [$($sj.deviceSerial)]") }
$r.Add('')
$r.Add('SUMMARY')
$r.Add('  per category : ' + (($catCounts.Keys | ForEach-Object { "$_=$($catCounts[$_])" }) -join '  '))
$r.Add('  per source   : ' + (($sourceFiles | ForEach-Object { "$($_.Name)=$($(if ($srcCounts.ContainsKey($_.Name)) { $srcCounts[$_.Name] } else { 0 })) (entries $($stats[$_.Name].Entries))" }) -join '  '))
$r.Add('  Sync pairing :' + $(if ($pairStats.Count) { '' } else { ' no Sync heartbeats found / fewer than 2 sources with Sync' }))
foreach ($p in $pairStats) { $r.Add("    $p") }
$r.Add('  session SHAs :')
if ($shaLines.Count -eq 0) { $r.Add('    (no [Session] lines found)') } else { foreach ($l in $shaLines) { $r.Add("  $l") } }
if ($shaMismatch) { $r.Add("  !! SHA MISMATCH between sources: " + ($distinct -join ' vs ')) }
$r.Add('')

foreach ($cat in $catCounts.Keys) {
    $items = @($sortedFindings | Where-Object { $_.Cats -contains $cat })
    $r.Add("== $cat ($($items.Count)) ==")
    foreach ($f in $items) {
        if ($cat -in 'SYNC', 'GHOST', 'INVARIANT') { $r.Add("[$cat] $($f.Text)"); continue }
        $tags = ($f.Cats | ForEach-Object { "[$_]" }) -join ''
        $r.Add("$tags $($f.Text)")
        $n = 0
        foreach ($c in $f.Entry.Cont) { if ($n -ge 6) { $r.Add('        ...'); break }; $r.Add('        | ' + $c); $n++ }
    }
    $r.Add('')
}
if ($shaMismatch) { $r.Add('== SHA ==' ); $r.Add("[SHA] build SHAs differ across sources: " + ($distinct -join ', ')); $r.Add('') }

Write-Utf8 (Join-Path $Session 'findings.txt') (($r -join "`r`n") + "`r`n")

Write-Host "merged.log : $($out.Count) lines ($($all.Count) entries) -> $(Join-Path $Session 'merged.log')"
Write-Host "findings   : $(($catCounts.Keys | ForEach-Object { "$_=$($catCounts[$_])" }) -join ' ')$(if ($shaMismatch) { '  SHA-MISMATCH' }) -> $(Join-Path $Session 'findings.txt')"
