"""menu_loop.wav: 120 BPM, 16 bars of G major boom-chick, rendered into a circular
buffer so every note/reverb tail that runs past the end wraps onto the start.
The seam is therefore just another pair of adjacent samples."""
import os, sys
import numpy as np
from scipy import signal
from scipy.io import wavfile
import pyloudnorm as pyln
sys.path.insert(0, os.path.dirname(__file__))
from synth import SR, OUT, lp, hp, bp, tt

BPM = 120
BEAT = 60 / BPM
BAR = 4 * BEAT
NBARS = 16
L = int(round(NBARS * BAR * SR))
buf = np.zeros((2, L))
rng = np.random.default_rng(77)


def mtof(m):
    return 440 * 2 ** ((m - 69) / 12)


def add(sig, start_s, pan=0.0, gain=1.0):
    """constant-power pan, circular placement."""
    i0 = int(round(start_s * SR)) % L
    gl, gr = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    idx = (i0 + np.arange(len(sig))) % L
    np.add.at(buf[0], idx, sig * gl * gain)
    np.add.at(buf[1], idx, sig * gr * gain)


def pluck(m, d, bright=1.0, twang=0.0006):
    """additive plucked string: higher partials decay faster, slight inharmonicity."""
    t = tt(d)
    f = mtof(m)
    y = np.zeros_like(t)
    for k in range(1, 16):
        fk = f * k * np.sqrt(1 + twang * k * k)
        if fk > 12000:
            break
        y += (bright ** (k - 1)) / k * np.sin(2 * np.pi * fk * t) * np.exp(-t * (3.0 + 2.2 * k))
    y += bp(rng.standard_normal(len(t)), 2000, 7000) * np.exp(-t / 0.002) * 0.15
    y *= np.minimum(1, t / 0.002)
    r = int(0.015 * SR)
    y[-r:] *= np.linspace(1, 0, r)
    return y


def bass(m, d):
    t = tt(d)
    f = mtof(m)
    y = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t)
    env = np.minimum(1, t / 0.004) * np.exp(-t / 0.28)
    r = int(0.02 * SR)
    env[-r:] *= np.linspace(1, 0, r)
    return y * env


def whistle(m, d):
    t = tt(d)
    f0 = mtof(m)
    vib = 1 + 0.008 * np.sin(2 * np.pi * 5.6 * t) * np.clip((t - 0.12) / 0.15, 0, 1)
    scoop = 1 - 0.03 * np.exp(-t / 0.03)
    ph = 2 * np.pi * np.cumsum(f0 * vib * scoop) / SR
    y = np.sin(ph) + 0.06 * np.sin(2 * ph)
    breath = bp(rng.standard_normal(len(t)), f0 * 0.8, min(f0 * 3, 15000)) * 0.08
    env = np.minimum(1, t / 0.025)
    r = int(min(0.08, d * 0.3) * SR)
    env[-r:] *= np.linspace(1, 0, r) ** 1.5
    return (y + breath) * env


def kick():
    t = tt(0.16)
    f = 50 + 70 * np.exp(-t / 0.03)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.07) * np.minimum(1, t / 0.001)


def shaker(acc):
    t = tt(0.06)
    e = np.minimum(1, t / 0.008) * np.exp(-t / 0.018)
    return bp(rng.standard_normal(len(t)), 5000, 11000) * e * acc


def woodblock(f=1250):
    t = tt(0.08)
    return (np.sin(2 * np.pi * f * t) + 0.4 * np.sin(2 * np.pi * f * 2.3 * t)) * np.exp(-t / 0.018) * np.minimum(1, t / 0.0008)


CH = {"G": [59, 62, 67], "C": [60, 64, 67], "D": [57, 62, 66], "D7": [57, 60, 66], "Am": [57, 60, 64]}
ROOT = {"G": (43, 50), "C": (48, 43), "D": (50, 45), "D7": (50, 45), "Am": (45, 40)}
CHORDS = ["G", "C", "D", "G", "G", "C", "D7", "G", "C", "G", "Am", "D", "C", "G", "D7", "G"]
N = None
MEL = [  # (midi or None, eighths)
    [(74, 1), (71, 1), (74, 1), (79, 2), (78, 1), (79, 2)],
    [(76, 1), (72, 1), (76, 1), (79, 1), (76, 2), (72, 2)],
    [(74, 1), (78, 1), (81, 1), (78, 1), (74, 1), (72, 1), (71, 1), (69, 1)],
    [(71, 2), (67, 2), (N, 1), (67, 1), (69, 1), (71, 1)],
    [(74, 1), (71, 1), (74, 1), (79, 2), (81, 1), (83, 2)],
    [(84, 1), (83, 1), (81, 1), (79, 1), (76, 2), (79, 2)],
    [(78, 1), (81, 1), (78, 1), (74, 1), (72, 2), (78, 2)],
    [(79, 3), (74, 1), (79, 2), (N, 2)],
    [(76, 3), (79, 1), (84, 4)],
    [(83, 2), (79, 2), (74, 4)],
    [(76, 2), (72, 2), (81, 4)],
    [(78, 2), (81, 2), (74, 4)],
    [(76, 1), (79, 1), (84, 2), (83, 1), (81, 1), (79, 2)],
    [(79, 2), (74, 2), (71, 2), (74, 2)],
    [(81, 2), (78, 2), (74, 2), (78, 2)],
    [(79, 6), (N, 2)],
]
E8 = BEAT / 2
for b, ch in enumerate(CHORDS):
    t0 = b * BAR
    root, fifth = ROOT[ch]
    # bass boom on 1 and 3, walk-up into the next bar every 4th bar
    add(bass(root, BEAT * 1.1), t0, 0, 0.5)
    if b % 4 == 3:
        nxt = ROOT[CHORDS[(b + 1) % NBARS]][0]
        add(bass(fifth, BEAT * 0.9), t0 + 2 * BEAT, 0, 0.45)
        add(bass(nxt - 3, E8 * 0.95), t0 + 3 * BEAT, 0, 0.4)
        add(bass(nxt - 1, E8 * 0.95), t0 + 3.5 * BEAT, 0, 0.4)
    else:
        add(bass(fifth, BEAT * 1.1), t0 + 2 * BEAT, 0, 0.45)
    # chick: short banjo-ish chord stabs on 2 and 4
    for beat in (1, 3):
        for k, m in enumerate(CH[ch]):
            add(pluck(m, 0.22, bright=0.85, twang=0.0012), t0 + beat * BEAT + k * 0.006, 0.35, 0.3)
    # drums
    add(kick(), t0, 0, 0.45)
    add(kick(), t0 + 2 * BEAT, 0, 0.35)
    for e in range(8):
        add(shaker(1.0 if e % 2 else 0.55), t0 + e * E8, -0.3, 0.3)
    if b % 2 == 1:
        add(woodblock(), t0 + 3.5 * BEAT, 0.5, 0.18)
    if b % 4 == 3:
        add(woodblock(1500), t0 + 3.75 * BEAT, 0.5, 0.14)
    # lead
    pos = t0
    for m, n8 in MEL[b]:
        d = n8 * E8
        if m is not None:
            if b < 8:
                add(pluck(m, max(0.3, d * 1.2), bright=0.8), pos, -0.35, 0.6)
            else:
                add(whistle(m, d * 0.95), pos, -0.2, 0.33)
        pos += d
    # B section: soft pluck arpeggio counter-line on offbeat eighths
    if b >= 8:
        arp = CH[ch] + [CH[ch][0] + 12]
        for e in (1, 3, 5, 7):
            add(pluck(arp[(e // 2) % 4] + 12, 0.25, bright=0.6), t0 + e * E8, 0.45, 0.16)

assert np.sum([len(x) for x in MEL]) and all(abs(sum(n for _, n in x) - 8) < 1e-9 for x in MEL)

# circular stereo reverb (FFT convolution over the loop period => seamless tails)
irl = int(1.1 * SR)
ti = np.arange(irl) / SR
wet = np.zeros_like(buf)
for c in range(2):
    ir = lp(np.random.default_rng(90 + c).standard_normal(irl), 5000) * np.exp(-ti / 0.28)
    ir[: int(0.012 * SR)] = 0  # pre-delay
    ir /= np.sqrt(np.sum(ir ** 2))
    irp = np.zeros(L); irp[:irl] = ir
    wet[c] = np.real(np.fft.ifft(np.fft.fft(buf[c]) * np.fft.fft(irp)))
mix = buf + 0.22 * wet


def circ_filter(x, sos):
    # run filter over 3 periods and keep the middle one -> steady state, circular
    y = signal.sosfilt(sos, np.tile(x, 3))
    return y[L:2 * L]


sos_hp = signal.butter(2, 35, btype="high", fs=SR, output="sos")
mix = np.stack([circ_filter(c, sos_hp) for c in mix])

# loudness to -16 LUFS, then circular look-ahead limiter to <= -3.2 dBFS
meter = pyln.Meter(SR)
lufs = meter.integrated_loudness(mix.T)
mix *= 10 ** ((-16 - lufs) / 20)
thr = 10 ** (-3.2 / 20)
peak = np.max(np.abs(mix), axis=0)
g = np.minimum(1, thr / np.maximum(peak, 1e-9))
la = int(0.003 * SR)
gp = np.concatenate([g[-la:], g, g[:la]])
gmin = -np.lib.stride_tricks.sliding_window_view(-gp, 2 * la + 1).max(axis=1)  # min over +-3ms
rel = int(0.05 * SR)
k = np.hanning(2 * la + 1); k /= k.sum()
gs = np.real(np.fft.ifft(np.fft.fft(gmin) * np.fft.fft(np.roll(np.pad(k, (0, L - len(k))), -la))))
gs = np.minimum(gs, gmin)  # never exceed the required reduction
mix *= gs
lufs2 = meter.integrated_loudness(mix.T)

# rotate (<= 4 ms, before the downbeat) so the file starts on a near-zero sample in both
# channels: a cold first play then has no start click. The circular content is unchanged.
w = int(0.004 * SR)
cand = np.arange(L - w, L)
best = cand[np.argmin(np.abs(mix[0, cand]) + np.abs(mix[1, cand]))]
mix = np.roll(mix, L - best, axis=1)
print("rotated by", (L - best) / SR * 1000, "ms")
pcm = np.round(np.clip(mix, -1, 1) * 32767).astype(np.int16).T
wavfile.write(os.path.join(OUT, "menu_loop.wav"), SR, pcm)

# seam verification on the written int16 data
x = pcm.astype(float) / 32768
d = np.abs(np.diff(x, axis=0))
seam = np.abs(x[0] - x[-1])
print(f"len {L/SR:.2f}s  LUFS {lufs2:.2f}  peak {20*np.log10(np.abs(x).max()):.2f} dBFS  limiter min gain {20*np.log10(gs.min()):.2f} dB")
print(f"seam jump L/R {seam}  median step {np.median(d, axis=0)}  99.9pct step {np.percentile(d, 99.9, axis=0)}  max step {d.max(axis=0)}")
print(f"first sample {x[0]}  last {x[-1]}")
# second-difference continuity at seam vs typical
dd = np.abs(np.diff(np.concatenate([x[-3:], x[:3]]), n=2, axis=0))
print("2nd diff around seam", dd.max(axis=0), " typical 99.9pct", np.percentile(np.abs(np.diff(x, n=2, axis=0)), 99.9, axis=0))
