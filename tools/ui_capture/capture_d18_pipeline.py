"""Phase 8 D18 (PC_RPAsset light layers / reflection-probe extras off): PC-tier MATCH-camera frames, seat pinned to 0 so before / after compare.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d18_pipeline.py <output_dir>

Reuses the capture_d2_tones.py plumbing (solo START, bots + animators frozen, UI hidden, local chicken pinned to SEAT 0, match camera yaw 225).
Spots: corner (local at the arena corner), pile (beside the centre pile); one frame each, as shipped (`pc_<spot>.png`). PC tier only (the Editor's default
quality level); Performance Mode OFF via capture_common.perf_mode (set, logged, restored). Run it once per pipeline-asset state and compare the PNGs.
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "d18")
os.makedirs(OUT, exist_ok=True)
import bridge
import capture_common
import capture_d2_tones as d2
d2.OUT = OUT


def main():
    try:
        print(d2.cs("Quality", "capture"))
        d2.start(); time.sleep(2.0)
        print(d2.cs("FreezeBots")); print(d2.cs("HideUi", "1")); print(d2.cs("PinSeat0")); time.sleep(2.0)
        print("PASS PC: " + d2.cs("Info"))
        for spot in ("corner", "pile"):
            print(d2.cs("FillPiles"))
            print(d2.cs("FreezeAnim", "0")); print(d2.cs("Stage", spot)); time.sleep(1.8); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
            print(d2.cs("Shot", "all", "", d2.P(f"pc_{spot}.png")))
    finally:
        for label, step in (("quality", lambda: print(d2.cs("Quality", "restore", strict=False))), ("ui", lambda: d2.cs("HideUi", "0", strict=False)),
                            ("play off", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as e:
                print(f"CLEANUP FAILED ({label}): {e}")
        time.sleep(4)


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
