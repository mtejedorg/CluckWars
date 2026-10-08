import os, glob, sys
import numpy as np
from scipy.io import wavfile
from scipy import signal
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(__file__))
from synth import OUT, max_loud_100ms, METER

SCR = os.path.dirname(__file__)
files = sorted(glob.glob(os.path.join(OUT, "*.wav")))
sel = sys.argv[1:]
if sel:
    files = [f for f in files if os.path.basename(f) in sel]
rows = []
fig, axes = plt.subplots(len(files), 2, figsize=(14, 2.1 * len(files)))
axes = np.atleast_2d(axes)
for i, f in enumerate(files):
    sr, d = wavfile.read(f)
    x = d.astype(float) / 32768
    mono = x.mean(axis=1) if x.ndim == 2 else x
    pk = 20 * np.log10(np.abs(x).max())
    rms = 20 * np.log10(np.sqrt(np.mean(mono ** 2)))
    S = np.abs(np.fft.rfft(mono * np.hanning(len(mono))))
    fr = np.fft.rfftfreq(len(mono), 1 / sr)
    cen = (S * fr).sum() / S.sum()
    l100 = max_loud_100ms(mono)
    lufs = METER.integrated_loudness(x) if len(mono) > sr * 0.5 else float("nan")
    rows.append((os.path.basename(f), len(mono) / sr * 1000, x.ndim, pk, rms, l100, lufs, cen, abs(mono[0]), abs(mono[-1])))
    a = axes[i, 0]; a.plot(np.arange(len(mono)) / sr, mono, lw=0.4); a.set_title(os.path.basename(f), fontsize=8); a.set_ylim(-1, 1)
    a = axes[i, 1]
    nps = 1024 if len(mono) > 20000 else 512
    fq, tm, Sx = signal.spectrogram(mono, sr, nperseg=nps, noverlap=nps * 7 // 8)
    a.pcolormesh(tm, fq, 10 * np.log10(Sx + 1e-12), shading="auto", vmin=-120, vmax=-40); a.set_ylim(0, 8000)
plt.tight_layout(); plt.savefig(os.path.join(SCR, "analysis.png"), dpi=70)
print(f"{'file':22s} {'ms':>7s} ch {'peak':>6s} {'rms':>6s} {'L100':>6s} {'LUFS':>6s} {'cent':>6s} first   last")
for r in rows:
    print(f"{r[0]:22s} {r[1]:7.0f} {r[2]}  {r[3]:6.1f} {r[4]:6.1f} {r[5]:6.1f} {r[6]:6.1f} {r[7]:6.0f} {r[8]:.4f} {r[9]:.4f}")
