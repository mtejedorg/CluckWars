"""Portrait / landscape capture matrix (WP8, PORTRAIT_AND_LANDSCAPE_SPEC 3.2-3.5): standalone, no Unity Editor.

For every window size it runs one solo scenario (tools/test-session/scenario-pc.ps1, a dev-tools Windows build in
Builds/Windows) with
    -cw_menushots      the menu pages first (main, settings, disconnect, class, gear, coop, coopjoin): menu-<page>.png
                       plus one UiProbe scan per page, then the solo flow starts
    -cw_shots ...      in-match screenshots at fixed seconds of the first round (default 8,15,22,29,36)
    -cw_zones          the HUD info / control zones outlined (IMGUI overlay, not a UIDocument: the probe never sees it)
    -cw_touchhud       phone sizes: the touch HUD (joystick + thumb-arc hexes) instead of the desktop strip
    -cw_safearea       optional (--safearea): a punch-hole + gesture-bar inset for the orientation
and collects into Captures/orient-<utc>/<size>/: the PNGs, the UiProbe lines (probe.txt, probe.json), findings.txt and
the scenario verdict. Then it builds contact.png: one row per size, every image with its probe verdict overlaid
(green "UI ok", red "UI n: U1=.. U3=.."), the menu shots judged by their own page scan, the in-match shots by the
nearest HUD scan.

The UI panel depends on the aspect only (PanelSettings ScaleWithScreenSize, match 0.5, which UI Toolkit blends LINEARLY:
scale = (W/1920 + H/1080) / 2), so a half-size window has exactly the phone's panel: 1080x2400 runs as 540x1200 and
1080x2410 (Pixel 10 Pro) as 540x1205, both 776x1724 / 773x1725 panel px (spec 0, WP8 correction; NOT the 966x2147 an
area-scaled panel would give). Every row prints the panel the probe measured next to the one this formula expects, and
a mismatch is noted, as is a window the OS clamped (from the log's window=WxH).

Usage:
    python tools/ui_capture/capture_orientation.py                         # the five sizes, 2 rounds each
    python tools/ui_capture/capture_orientation.py --sizes 540x960,960x540 --safearea
    python tools/ui_capture/capture_orientation.py --dry-run               # print the scenario commands only
    python tools/ui_capture/capture_orientation.py --sheet-only Captures/orient-20261023-101500   # rebuild the sheet

Exit code: 0 when every size reached the match HUD (>= 1 scope=hud scan) with 0 HUD offenders, else 1. Menu and
overlay offenders are reported, not gated (the same split verify.ps1 makes). Needs Pillow for the contact sheet.
"""
import argparse
import datetime
import glob
import json
import os
import re
import shutil
import subprocess
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SCENARIO = os.path.join(REPO, "tools", "test-session", "scenario-pc.ps1")
DEFAULT_SIZES = ["540x960", "960x540", "1080x2400", "2400x1080", "1920x1080"]
# Sizes taller than a 3840x2160 monitor's work area: run a same-aspect stand-in (identical panel, spec 3.1).
STAND_INS = {"1080x2400": (540, 1200), "1080x2410": (540, 1205)}
DESKTOP_SIZES = {"1920x1080"}          # the PC baseline keeps the desktop HUD; every other size is a phone
SAFE_PORTRAIT = (0.0, 0.02, 0.0, 0.055)   # l, b, r, t as fractions of the window (spec 3.4: punch-hole top, gesture bar)
SAFE_LANDSCAPE = (0.055, 0.02, 0.0, 0.0)  # the cutout moves to the left edge

RX_STAMP = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\]\[[^\]]*\]\[(\w+)\]\[([^\]]+)\] ?(.*)$")
RX_SUMMARY = re.compile(r"^ui offenders=(\d+) scope=(\w+) page=(\S+) reason=(\S+) scan=(\d+) U1=(\d+) U2=(\d+) U3=(\d+) U4=(\d+) U5=(\d+) U6=(\d+) allowed=(\d+).*?\bpanel=(\d+)x(\d+)")
RX_SHOT = re.compile(r"^AutoTest shot t=(\d+)s window=(\d+)x(\d+) -> (.+)$")
RX_MENUSHOT = re.compile(r"^AutoTest menushot page=(\w+) window=(\d+)x(\d+) -> (.+)$")


def expected_panel(w, h, ref_w=1920.0, ref_h=1080.0, match=0.5):
    """UI Toolkit's ScaleWithScreenSize + MatchWidthOrHeight: a linear blend of the two ratios (HudControlZones.PanelSize)."""
    scale = (1.0 - match) * w / ref_w + match * h / ref_h
    return round(w / scale), round(h / scale)


def parse_size(s):
    m = re.match(r"^(\d+)x(\d+)$", s.strip())
    if not m:
        raise SystemExit(f"bad size '{s}' (want WxH)")
    return int(m.group(1)), int(m.group(2))


def safe_arg(w, h):
    l, b, r, t = SAFE_PORTRAIT if w < h else SAFE_LANDSCAPE
    return "-cw_safearea {:.0f},{:.0f},{:.0f},{:.0f}".format(l * w, b * h, r * w, t * h)


def extra_args(label, run_w, run_h, args):
    parts = []
    if args.shots:
        parts.append("-cw_shots " + args.shots)
    if not args.no_zones:
        parts.append("-cw_zones")
    parts.append("-cw_shottag " + "s" + label)
    if args.menushots:
        parts.append("-cw_menushots" + ("" if args.menushots == "all" else " " + args.menushots))
    if args.touch == "all" or (args.touch == "phone" and label not in DESKTOP_SIZES):
        parts.append("-cw_touchhud")
    if args.safearea:
        parts.append(safe_arg(run_w, run_h))
    return " ".join(parts)


def run_scenario(label, run_w, run_h, args):
    cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", SCENARIO, "-Mode", "solo",
           "-Rounds", str(args.rounds), "-Class", args.cls, "-Width", str(run_w), "-Height", str(run_h),
           "-ExtraArgs", extra_args(label, run_w, run_h, args)]
    print(f"[{label}] " + " ".join(f'"{c}"' if " " in c else c for c in cmd), flush=True)
    if args.dry_run:
        return None, None, ""
    p = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = (p.stdout or "") + (p.stderr or "")
    m = re.search(r"^session: (.+?)\s{2,}mode:", out, re.M)
    session = m.group(1).strip() if m else None
    if session is None:   # fall back to the newest session folder
        cands = sorted(glob.glob(os.path.join(REPO, "Captures", "session-*")))
        session = cands[-1] if cands else None
    return p.returncode, session, out


def seconds(m):
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + int(m.group(4)) / 1000.0


def parse_log(path):
    """UiProbe summaries, offender lines and the shot lines of one client log."""
    res = {"scans": [], "offenders": [], "shots": [], "menushots": [], "lines": []}
    if not path or not os.path.exists(path):
        return res
    with open(path, encoding="utf-8", errors="replace") as f:
        for raw in f:
            m = RX_STAMP.match(raw.rstrip("\n"))
            if not m:
                continue
            t, level, src, msg = seconds(m), m.group(5), m.group(6), m.group(7)
            if src == "UiProbe":
                res["lines"].append(raw.rstrip("\n"))
                s = RX_SUMMARY.match(msg)
                if s:
                    res["scans"].append({"t": t, "offenders": int(s.group(1)), "scope": s.group(2), "page": s.group(3),
                                         "reason": s.group(4), "scan": int(s.group(5)),
                                         "rules": {f"U{i}": int(s.group(5 + i)) for i in range(1, 7)},
                                         "allowed": int(s.group(12)), "panel": [int(s.group(13)), int(s.group(14))]})
                elif msg.startswith("ui offender "):
                    res["offenders"].append({"t": t, "level": level, "text": msg})
            elif src == "AutoTest":
                a = RX_SHOT.match(msg)
                if a:
                    res["shots"].append({"t": t, "second": int(a.group(1)), "window": [int(a.group(2)), int(a.group(3))], "path": a.group(4).strip()})
                b = RX_MENUSHOT.match(msg)
                if b:
                    res["menushots"].append({"t": t, "page": b.group(1), "window": [int(b.group(2)), int(b.group(3))], "path": b.group(4).strip()})
    return res


def verdict_text(scan):
    if scan is None:
        return "no UI scan", None
    if scan["offenders"] == 0:
        return "UI ok" + (f" ({scan['allowed']} allowed)" if scan["allowed"] else ""), True
    rules = " ".join(f"{k}={v}" for k, v in scan["rules"].items() if v)
    return f"UI {scan['offenders']}: {rules}", False


def judge_images(data, size_dir):
    """Every PNG in the size folder with the scan that judges it."""
    images = []
    by_page = {}
    for s in data["scans"]:
        if s["scope"] == "menu" and s["page"] != "-":
            by_page[s["page"]] = s
    shot_t = {os.path.basename(s["path"]): s for s in data["shots"]}
    for png in sorted(glob.glob(os.path.join(size_dir, "*.png"))):
        name = os.path.basename(png)
        if name == "contact.png":
            continue
        scan = None
        m = re.match(r"^menu-(\w+?)(?:-.*)?\.png$", name)
        if m:
            scan = by_page.get(m.group(1))
        elif name in shot_t:
            t = shot_t[name]["t"]
            near = [s for s in data["scans"] if s["scope"] in ("hud", "overlay") and abs(s["t"] - t) <= 3.0]
            scan = min(near, key=lambda s: abs(s["t"] - t)) if near else None
        text, ok = verdict_text(scan)
        images.append({"file": name, "verdict": text, "ok": ok, "scope": scan["scope"] if scan else None})
    return images


def collect(label, req, run, code, session, out_root, scenario_out):
    size_dir = os.path.join(out_root, label)
    os.makedirs(size_dir, exist_ok=True)
    with open(os.path.join(size_dir, "scenario.txt"), "w", encoding="utf-8") as f:
        f.write(scenario_out)
    log = os.path.join(session, "client1.log") if session else None
    if session:
        for png in glob.glob(os.path.join(session, "shots", "*.png")):
            shutil.copy2(png, size_dir)
        for name in ("findings.txt", "client1.log"):
            src = os.path.join(session, name)
            if os.path.exists(src):
                shutil.copy2(src, size_dir)
    data = parse_log(log)
    with open(os.path.join(size_dir, "probe.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(data["lines"]) + "\n")
    hud = [s for s in data["scans"] if s["scope"] == "hud"]
    windows = {tuple(s["window"]) for s in data["shots"] + data["menushots"]}
    notes = []
    if req != run:
        notes.append(f"{req[0]}x{req[1]} runs as {run[0]}x{run[1]} (same panel, spec 3.1)")
    for w in windows:
        if w != tuple(run):
            notes.append(f"window came up {w[0]}x{w[1]}, not {run[0]}x{run[1]} (clamped by the OS?)")
    summary = {
        "size": label, "requested": list(req), "ran": list(run), "session": session, "scenario_exit": code,
        "hud_scans": len(hud), "hud_bad": sum(1 for s in hud if s["offenders"] > 0),
        "hud_worst": max((s["offenders"] for s in hud), default=0),
        "menu": {s["page"]: s["offenders"] for s in data["scans"] if s["scope"] == "menu" and s["page"] != "-"},
        "overlay_bad": sum(1 for s in data["scans"] if s["scope"] == "overlay" and s["offenders"] > 0),
        "offender_lines": [o["text"] for o in data["offenders"]][:200],
        "notes": notes,
    }
    panels = sorted({tuple(s["panel"]) for s in data["scans"]})
    summary["panel_measured"] = [list(p) for p in panels]
    summary["panel_expected"] = list(expected_panel(*run))
    for p in panels:
        if abs(p[0] - summary["panel_expected"][0]) > 2 or abs(p[1] - summary["panel_expected"][1]) > 2:
            notes.append(f"probe panel {p[0]}x{p[1]} != expected {summary['panel_expected'][0]}x{summary['panel_expected'][1]}")
    summary["images"] = judge_images(data, size_dir)
    summary["ui_pass"] = summary["hud_scans"] > 0 and summary["hud_bad"] == 0
    with open(os.path.join(size_dir, "probe.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
    return summary


def contact_sheet(out_root, summaries, thumb=300):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print("Pillow is not installed (pip install pillow): no contact sheet.", file=sys.stderr)
        return None
    try:
        font = ImageFont.truetype("arial.ttf", 15)
        big = ImageFont.truetype("arialbd.ttf", 18)
    except OSError:
        font = big = ImageFont.load_default()
    pad, label_h, head_h = 10, 44, 52
    cols = max((len(s["images"]) for s in summaries), default=1) or 1
    row_h = head_h + thumb + label_h + pad
    sheet = Image.new("RGB", (max(pad + cols * (thumb + pad), 1400), pad + len(summaries) * row_h), (24, 18, 12))
    d = ImageDraw.Draw(sheet)
    for r, s in enumerate(summaries):
        y0 = pad + r * row_h
        hud = "HUD ok" if s["ui_pass"] else ("HUD: no scan" if s["hud_scans"] == 0 else f"HUD {s['hud_bad']}/{s['hud_scans']} scans bad (worst {s['hud_worst']})")
        menu_bad = sum(1 for v in s["menu"].values() if v)
        pm = s.get("panel_measured") or []
        head = f"{s['size']}  ran {s['ran'][0]}x{s['ran'][1]}  panel {'/'.join(f'{p[0]}x{p[1]}' for p in pm) or '?'}   {hud}"
        sub = f"menu pages with offenders: {menu_bad}/{len(s['menu'])}   overlay scans with offenders: {s['overlay_bad']}   scenario exit={s['scenario_exit']}"
        if s["notes"]:
            sub += "   [" + "; ".join(s["notes"]) + "]"
        d.text((pad, y0 + 4), head, fill=(120, 230, 120) if s["ui_pass"] else (255, 120, 110), font=big)
        d.text((pad, y0 + 27), sub, fill=(220, 210, 190), font=font)
        for c, img in enumerate(s["images"]):
            x = pad + c * (thumb + pad)
            try:
                im = Image.open(os.path.join(out_root, s["size"], img["file"])).convert("RGB")
                im.thumbnail((thumb, thumb))
                sheet.paste(im, (x + (thumb - im.width) // 2, y0 + head_h + (thumb - im.height) // 2))
            except OSError as e:
                d.text((x, y0 + head_h + 10), f"unreadable: {e}", fill=(255, 120, 110), font=font)
            colour = (120, 230, 120) if img["ok"] else (200, 200, 200) if img["ok"] is None else (255, 120, 110)
            d.rectangle([x, y0 + head_h, x + thumb - 1, y0 + head_h + thumb - 1], outline=colour, width=3)
            d.text((x, y0 + head_h + thumb + 4), img["file"], fill=(254, 245, 224), font=font)
            d.text((x, y0 + head_h + thumb + 22), img["verdict"], fill=colour, font=font)
    path = os.path.join(out_root, "contact.png")
    sheet.save(path)
    return path


def write_summary(out_root, summaries):
    with open(os.path.join(out_root, "summary.json"), "w", encoding="utf-8") as f:
        json.dump(summaries, f, indent=2)
    lines = []
    for s in summaries:
        pm = "/".join(f"{p[0]}x{p[1]}" for p in s.get("panel_measured") or []) or "?"
        lines.append(f"{s['size']:>10}  ran {s['ran'][0]}x{s['ran'][1]:<5} panel {pm:<9} hud scans={s['hud_scans']:<3} bad={s['hud_bad']:<3} "
                     f"worst={s['hud_worst']:<3} {'PASS' if s['ui_pass'] else 'FAIL'}  menu={s['menu']}  scenario exit={s['scenario_exit']}"
                     + (f"  notes: {'; '.join(s['notes'])}" if s["notes"] else ""))
    with open(os.path.join(out_root, "summary.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sizes", default=",".join(DEFAULT_SIZES), help="comma-separated WxH list")
    ap.add_argument("--rounds", type=int, default=2, help="rounds per size (verify.ps1 wants >= 2 matches)")
    ap.add_argument("--class", dest="cls", default="Warrior")
    ap.add_argument("--shots", default="8,15,22,29,36", help="cw_shots seconds ('' = none)")
    ap.add_argument("--menushots", default="all", help="cw_menushots page list, 'all', or '' for none")
    ap.add_argument("--touch", choices=["phone", "all", "none"], default="phone", help="which sizes get -cw_touchhud")
    ap.add_argument("--safearea", action="store_true", help="simulate the punch-hole / gesture-bar insets")
    ap.add_argument("--no-zones", action="store_true", help="no -cw_zones outline overlay in the PNGs")
    ap.add_argument("--out", default=None, help="output folder (default Captures/orient-<utc>)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--sheet-only", default=None, help="rebuild contact.png / summary from an existing orient folder")
    args = ap.parse_args()

    if args.sheet_only:
        root = os.path.abspath(args.sheet_only)
        summaries = []
        for pj in sorted(glob.glob(os.path.join(root, "*", "probe.json"))):
            with open(pj, encoding="utf-8") as f:
                summaries.append(json.load(f))
        write_summary(root, summaries)
        print("contact sheet:", contact_sheet(root, summaries))
        return 0 if summaries and all(s["ui_pass"] for s in summaries) else 1

    if not os.path.exists(os.path.join(REPO, "Builds", "Windows", "CluckWars.exe")) and not args.dry_run:
        raise SystemExit("no Builds/Windows/CluckWars.exe: build the dev-tools Test Session player first")
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d-%H%M%S")
    out_root = os.path.abspath(args.out or os.path.join(REPO, "Captures", f"orient-{stamp}"))
    if not args.dry_run:
        os.makedirs(out_root, exist_ok=True)

    summaries = []
    for label in [s.strip() for s in args.sizes.split(",") if s.strip()]:
        req = parse_size(label)
        run = STAND_INS.get(label, req)
        code, session, out = run_scenario(label, run[0], run[1], args)
        if args.dry_run:
            continue
        s = collect(label, req, run, code, session, out_root, out)
        summaries.append(s)
        print(f"[{label}] hud scans={s['hud_scans']} bad={s['hud_bad']} -> {'PASS' if s['ui_pass'] else 'FAIL'}; {len(s['images'])} image(s)", flush=True)
    if args.dry_run:
        return 0
    write_summary(out_root, summaries)
    print("contact sheet:", contact_sheet(out_root, summaries))
    print("output:", out_root)
    return 0 if summaries and all(s["ui_pass"] for s in summaries) else 1


if __name__ == "__main__":
    sys.exit(main())
