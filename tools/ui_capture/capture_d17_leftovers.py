"""Phase 8 D17 (leftovers): the straw pad (paths must not draw through it) and the fence-hedge gap (no lime strip), seen from the
owner's seats 0 and 2, MATCH camera, both tiers, Performance Mode OFF (capture_common.perf_mode).

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d17_leftovers.py <output_dir>

Same staging as capture_d13_rimpath.py (spots `spawn` = on the base pad, `rim` = beside the E fence gap; seats 0 and 2), so the files are
<tier>_seat<k>_<spot>.png (+ _nopath) and compare 1:1 with Captures/env3d_after/D/D13.
"""
import sys, os
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge
import capture_common
import capture_d13_rimpath as d13

_ds = d13.ds


def _ds_with_nopath(method, *args):
    """Every frame is also rendered with every DirtPaths_Base* ShadowsOnly (<prefix>_nopath.png): whatever still crosses the pad there is not a path."""
    if method == "Shots" and args[0] == "all":
        args = ("all,nopath",) + args[1:]
    return _ds(method, *args)


d13.ds = _ds_with_nopath

if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        d13.main()
