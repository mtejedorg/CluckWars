# tools/ui_capture

Python capture tools. Most of them drive the Unity Editor through the MCP bridge (`bridge.py`, JSON-RPC over HTTP; the
Editor must be open). `capture_orientation.py` does not: it runs the standalone Windows dev-tools build.

## capture_orientation.py (WP8, PORTRAIT_AND_LANDSCAPE_SPEC 3.2-3.5)

The portrait / landscape layout matrix, judged by the in-game `UiProbe` (rules U1-U6).

Needs: a dev-tools Windows build in `Builds/Windows/CluckWars.exe` ("Cluck Wars/Build/Test Session"), Python 3 with
Pillow, PowerShell. No Editor, no phone.

```
python tools/ui_capture/capture_orientation.py                          # 540x960 960x540 1080x2400 2400x1080 1920x1080
python tools/ui_capture/capture_orientation.py --sizes 540x960 --safearea --rounds 2
python tools/ui_capture/capture_orientation.py --dry-run                # print the scenario-pc commands only
python tools/ui_capture/capture_orientation.py --sheet-only Captures/orient-<utc>   # redraw contact.png / summary
```

Per size it runs `tools/test-session/scenario-pc.ps1 -Mode solo -Width W -Height H -ExtraArgs ...` with

| key | what |
|---|---|
| `-cw_menushots` | before the solo flow: main, settings sheet, disconnect sheet, class pick, Gear Up, THE COOP (solo) and THE COOP (join): one `menu-<page>-s<size>.png` and one `ui offenders=N scope=menu page=<page>` scan each |
| `-cw_shots 8,15,22,29,36` | in-match screenshots of the first round, each judged by the nearest HUD scan |
| `-cw_zones` | HUD info / control zones outlined (an IMGUI overlay; the probe does not see it). `--no-zones` drops it |
| `-cw_touchhud` | phone sizes (all but 1920x1080; `--touch all|none` to change): the touch HUD instead of the desktop strip |
| `-cw_safearea l,b,r,t` | `--safearea`: punch-hole + gesture bar, portrait 0/2/0/5.5 %, landscape 5.5/2/0/0 % of the window |

1080x2400 is taller than a 2160-px monitor, so it runs as 540x1200 (same 966x2147 panel, spec 3.1); a window the OS
clamped anyway is noted from the log's `window=WxH`.

Output: `Captures/orient-<utc>/<size>/` holds the PNGs, `probe.txt` (every UiProbe line), `probe.json` (per-image
verdicts, menu offenders per page, HUD scan counts), `findings.txt`, `client1.log` and `scenario.txt`; the root holds
`summary.txt`, `summary.json` and `contact.png` (one row per size, each image framed green "UI ok" / red "UI n: U1=.."
/ grey "no UI scan").

Exit code 0 = every size reached the match HUD with 0 HUD offenders. Menu and overlay offenders are listed, not gated
(the same split as `verify.ps1`). The scenario's own verdict (`scenario exit=`) is reported separately.

The rules, scopes and the allow-list: `Assets/_Game/Scripts/Diagnostics/UiLayoutRules.cs`, `UiLayoutAllowList.cs`.
