<#
.SYNOPSIS
  Runs merge.ps1 on the synthetic fixture (fixtures\session-fixture) and asserts the expected output.
  Exit code 0 = all assertions passed.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\lib.ps1"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('cw-selftest-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item (Join-Path $PSScriptRoot 'fixtures\session-fixture') $tmp -Recurse
& (Join-Path $PSScriptRoot 'merge.ps1') -Session $tmp | Out-Host

$merged = Get-Content (Join-Path $tmp 'merged.log') -Raw
$mlines = @(Get-Content (Join-Path $tmp 'merged.log'))
$find = Get-Content (Join-Path $tmp 'findings.txt') -Raw

$script:fail = 0
function Assert([bool]$cond, [string]$what) {
    if ($cond) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what" -ForegroundColor Red; $script:fail++ }
}
function Count([string]$text, [string]$pattern) { return ([regex]::Matches($text, $pattern)).Count }

Write-Host "`nmerged.log"
$stamps = @($mlines | Where-Object { $_ -match '^\d{4}-\d\d-\d\d ' } | ForEach-Object { $_.Substring(0, 24) })
Assert ($stamps.Count -gt 20) "has timeline entries ($($stamps.Count))"
Assert (($stamps -join "`n") -eq (($stamps | Sort-Object) -join "`n")) 'entries are in chronological order'
Assert ($merged -match '2026-10-20 23:59:59\.000Z \[phone  \] \[Debug\]\[Perf\]') 'phone line dated 2026-10-20 before midnight'
Assert ($merged -match '2026-10-21 00:00:00\.000Z \[phone  \] \[Debug\]\[Sync\]') 'midnight rollover -> 2026-10-21 (phone, Session date)'
Assert ($merged -match '2026-10-20 23:59:59\.500Z \[client2\]') 'client2 uses session.json start date as fallback'
Assert ($merged -match '2026-10-21 00:00:01\.000Z \[client2\]') 'client2 rollover with fallback date'
Assert ($merged -notmatch 'ActivityManager') 'non-Unity logcat noise dropped'
Assert ($merged -notmatch 'Refresh completed') 'unstamped engine noise before the first stamped line dropped'
Assert ($merged -notmatch 'Boot complete\r?\n\s+\| UnityEngine\.Debug:Log') 'stack boilerplate dropped under an Info parent'
Assert ($merged -match 'Slow RTT=180ms\r?\n\s+\| UnityEngine\.Logger:Log') 'stack trace kept under a Warn parent'
Assert ($merged -match 'Ability failed: NullReferenceException\r?\n\s+\| NullReferenceException: Object reference') 'exception detail kept under an Error parent'
Assert ($merged -match '\[Error\]\[Unity\] InvalidOperationException: Sequence contains no elements') 'uncaught exception after an Info line becomes its own entry'
Assert ($merged -match '\[Error\]\[Android\] \[AndroidRuntime\] FATAL EXCEPTION') 'Android crash line surfaced'
$ia = $merged.IndexOf('[phone  ] [Debug][Sync]'); $ib = $merged.IndexOf('[editor ] [Debug][Sync]')
Assert ($ia -ge 0 -and $ib -gt $ia) 'phone sorts before editor at equal-ish times (stable per source order)'

Write-Host "`nfindings.txt"
Assert ($find -match 'ERROR=\d+') 'summary header present'
Assert ($find -match 'SYNC=2 GHOST=1' -or $find -match 'SYNC=2\s+GHOST=1') 'exactly 2 SYNC (phone/editor + phone/client1 hp) and 1 GHOST'
Assert ((Count $find '\[SYNC\] t=300\.\.400 phone vs editor: chicken 2 hp phone=80 editor=100') -eq 1) 'hp mismatch persisting 2 heartbeats reported (phone vs editor)'
Assert ((Count $find '\[SYNC\] t=300\.\.400 phone vs client1: chicken 2 hp') -eq 1) 'same mismatch reported for phone vs client1'
Assert ($find -notmatch 'editor vs client1') 'editor vs client1 agree -> nothing reported'
Assert ($find -notmatch 'chicken 1 cg') 'single-heartbeat cg mismatch (t=500) NOT reported'
Assert ($find -notmatch 'chicken 2 x') 'single-heartbeat x mismatch (t=500) NOT reported'
Assert ($find -notmatch 'chicken 1 x') 'x within 1.5 m tolerance NOT reported'
Assert ($find -notmatch 'time remaining') 'rem within 0.5 s tolerance NOT reported'
Assert ((Count $find '\[GHOST\] t=600\.\.700 phone vs editor: chicken 4 phone=present editor=missing') -eq 1) 'ghost chicken reported'
Assert ($find -match 'PERF\]? .*Hitch frame=87\.3ms') 'perf hitch reported'
Assert ($find -match 'p99=65\.2') 'perf summary with p99 > 50 reported'
Assert ($find -notmatch 'p99=20\.1') 'perf summary with p99 <= 50 NOT reported'
Assert ($find -match 'Magenta material') 'visual warn reported'
Assert ($find -match 'offenders=3') 'visual summary with nonzero offenders reported'
Assert ($find -notmatch 'offenders=0') 'visual summary with zero offenders NOT reported'
Assert ($find -match '\[ERROR\]\[EXCEPTION\].*Ability failed') 'error + exception line reported once with both tags'
Assert ($find -match '\[WARN\].*client1.*x3 engine warning \(expected 0\): Non-Legacy animations cannot be sampled') 'engine warning (CW-207) flagged with its count'
Assert ($find -match 'SHA MISMATCH') 'build SHA mismatch flagged (aaa111 vs bbb222)'
Assert ($find -match 'phone\s+sha=aaa111' -and $find -match 'client1\s+sha=bbb222') 'SHAs listed per source'

Write-Host "`nlib helpers"
$ex = Convert-Extras 'cw_autojoin=ABC123 cw_class=Warrior  cw_autoplay=1'
Assert ($ex.Count -eq 3 -and $ex['cw_class'] -eq 'Warrior') 'Convert-Extras parses string'
$ex2 = Convert-Extras @{ a = 1 }
Assert ($ex2['a'] -eq '1') 'Convert-Extras parses hashtable'
$code = Get-AutoTestCode -Session $tmp
Assert ($code -eq 'ABC123') 'Get-AutoTestCode finds AutoTest code in editor.log'

Remove-Item $tmp -Recurse -Force

# ---- Round-start invariants (CW-221 regression): synthetic 2-round log, round 2 is wrong in 5 ways ----
$tmp2 = Join-Path ([IO.Path]::GetTempPath()) ('cw-selftest-inv-' + [Guid]::NewGuid().ToString('N'))
Copy-Item (Join-Path $PSScriptRoot 'fixtures\invariant-fixture') $tmp2 -Recurse
& (Join-Path $PSScriptRoot 'merge.ps1') -Session $tmp2 | Out-Null
$f2 = Get-Content (Join-Path $tmp2 'findings.txt') -Raw
Assert ($f2 -notmatch 'round 1 start') 'invariants: a correct round 1 start is not flagged'
Assert ($f2 -match 'round 2 start on client1 \(t=400\): chicken P1 is NOT at a corner') 'invariants: chicken left where it ended is flagged (CW-221)'
Assert ($f2 -match 'chicken P1 carries cg=4') 'invariants: cargo carried into a new round is flagged'
Assert ($f2 -match 'base 0 food=7') 'invariants: base food not reset is flagged'
Assert ($f2 -match 'piles differ from round 1') 'invariants: piles not reset are flagged'
Assert ($f2 -match 'share corner quadrant') 'invariants: two chickens in one corner are flagged'
Assert ($f2 -match 'world static hash differs across peers from t=1000') 'invariants: persistent static-world hash disagreement between peers is flagged (CW-216/217 class)'
Remove-Item $tmp2 -Recurse -Force

# ---- lag vs stuck (CW-226): a mismatch counts only while one side stays constant; both sides changing is lag ----
$tmp3 = Join-Path ([IO.Path]::GetTempPath()) ('cw-selftest-cs-' + [Guid]::NewGuid().ToString('N'))
Copy-Item (Join-Path $PSScriptRoot 'fixtures\cs-fixture') $tmp3 -Recurse
& (Join-Path $PSScriptRoot 'merge.ps1') -Session $tmp3 | Out-Null
$f3 = Get-Content (Join-Path $tmp3 'findings.txt') -Raw
Assert ($f3 -notmatch 'chicken P2 cs') 'lag: owner Slowed/Free vs remote Free/Slowed (swapped pairs, session 150533) NOT reported'
Assert ($f3 -notmatch 'pile\[1\]') 'lag: pile 5.0/0.0 then 0.0/2.0, both sides change (refill seen a beat late, session 153100) NOT reported'
Assert ((Count $f3 '\[SYNC\] t=292\.\.356 client1 vs client2: chicken P3 cs client1=Free client2=Slowed') -eq 1) 'stuck: the same pair on 2 heartbeats IS reported'
Assert ($f3 -match 'chicken P3 cs .*\[owner=client2 src=Collision fl client1=0 client2=1\]') 'stuck: cs finding names the owner, its slow sources and each side''s flag changes'
Assert ((Count $f3 '\[SYNC\] t=100\.\.228 client1 vs client2: chicken P4 cs client1=Slowed client2=Free') -eq 1) 'stuck: remote constant Slowed while the owner flickers Free/Rooted/Free IS reported (pairs differ every beat)'
Assert ((Count $f3 '\[SYNC\] t=292\.\.420 client1 vs client2: chicken P5 cs client1=Slowed client2=Free  \(3 consecutive') -eq 1) 'stuck: 3-beat owner-Slowed / remotes-Free (session 225001 shape) IS reported'
Assert ($f3 -match 'SYNC=3\s') 'lag/stuck: exactly three SYNC findings'
Remove-Item $tmp3 -Recurse -Force
Write-Host ''
if ($script:fail -eq 0) { Write-Host 'SELFTEST OK' -ForegroundColor Green; exit 0 }
Write-Host "SELFTEST FAILED ($($script:fail) assertion(s))" -ForegroundColor Red
exit 1
