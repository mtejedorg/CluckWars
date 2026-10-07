"""match_loop.wav (+ optional match_loop_intense.wav): in-match music for Cluck Wars.

Same family as Assets/_Game/Audio/UI/Source~/music.py (menu_loop): procedural, no samples, rendered into a circular
buffer so every note, reverb tail and filter state that runs past the end wraps onto the
start. The seam is therefore just another pair of adjacent samples.

128 BPM, A major, 16 bars = exactly 30.000 s = 1,440,000 frames at 48 kHz.
Four-on-the-floor kick, clap on 2 and 4, 16th shaker, bouncing octave bass, banjo
offbeat "chick" stabs, marimba lead (bars 1-8), banjo lead + marimba arps (bars 9-16).
Mastered like menu_loop (-16 LUFS integrated, peak <= -3.2 dBFS) with a master dip at
1-4 kHz so it sits under the SFX.

The intense variant is the same grid, same length, same rotation, with extra layers
(tambourine, 16th hats, octave-doubled lead, kick pickup, tom fills). It uses the base
mix's gain stage, so it is naturally a little louder. Because it is sample-aligned with
the base loop, a runtime can crossfade between them at the same playback position.

Run: python tools/audio/match_music.py [--no-intense]   (writes into Assets/_Game/Audio/UI)
Needs numpy, scipy, pyloudnorm.
"""
import os, sys
import numpy as np
from scipy import signal
from scipy.io import wavfile
import pyloudnorm as pyln
# Self-contained (the menu generators in Assets/_Game/Audio/UI/Source~ are git-ignored by the
# repo's `*~` rule); these helpers are copied from Source~/synth.py.
SR = 48000
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Game", "Audio", "UI"))


def tt(d):
    return np.arange(int(round(d * SR))) / SR


def bp(x, lo, hi, order=2):
    return signal.sosfilt(signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, btype="low", fs=SR, output="sos"), x)

BPM = 128
BEAT = 60 / BPM
BAR = 4 * BEAT
E8 = BEAT / 2
E16 = BEAT / 4
NBARS = 16
L = int(round(NBARS * BAR * SR))
assert L == 1_440_000
rng = np.random.default_rng(128)


def mtof(m):
    return 440 * 2 ** ((m - 69) / 12)


class Bus:
    def __init__(self):
        self.buf = np.zeros((2, L))

    def add(self, sig, start_s, pan=0.0, gain=1.0):
        """constant-power pan, circular placement."""
        i0 = int(round(start_s * SR)) % L
        gl, gr = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
        idx = (i0 + np.arange(len(sig))) % L
        np.add.at(self.buf[0], idx, sig * gl * gain)
        np.add.at(self.buf[1], idx, sig * gr * gain)


def tail(y, sec=0.015):
    r = min(len(y), int(sec * SR))
    y[-r:] *= np.linspace(1, 0, r)
    return y


# ---------------------------------------------------------------- instruments
def marimba(m, d=0.6):
    """modal bar: partials ~1 : 3.93 : 9.2, upper ones die fast; soft mallet click."""
    t = tt(d)
    f = mtof(m)
    tau1 = 0.42 * (440 / f) ** 0.35
    y = (np.sin(2 * np.pi * f * t) * np.exp(-t / tau1)
         + 0.16 * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / 0.05)
         + 0.04 * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / 0.015))
    y += lp(rng.standard_normal(len(t)), 1200) * np.exp(-t / 0.003) * 0.25
    y *= np.minimum(1, t / 0.0015)
    return tail(y)


def banjo(m, d, bright=0.85, twang=0.0014):
    """additive plucked string (as menu pluck), brighter decay, a touch more twang."""
    t = tt(d)
    f = mtof(m)
    y = np.zeros_like(t)
    for k in range(1, 16):
        fk = f * k * np.sqrt(1 + twang * k * k)
        if fk > 12000:
            break
        y += (bright ** (k - 1)) / k * np.sin(2 * np.pi * fk * t) * np.exp(-t * (4.0 + 2.6 * k))
    y += bp(rng.standard_normal(len(t)), 2500, 8000) * np.exp(-t / 0.0015) * 0.12
    y *= np.minimum(1, t / 0.0015)
    return tail(y)


def bass(m, d, punch=1.0):
    t = tt(d)
    f = mtof(m)
    y = np.sin(2 * np.pi * f * t) + 0.4 * np.sin(4 * np.pi * f * t) + 0.15 * np.sin(6 * np.pi * f * t)
    env = np.minimum(1, t / 0.003) * np.exp(-t / (0.16 * punch))
    return tail(y * env, 0.012)


def kick():
    t = tt(0.18)
    f = 48 + 90 * np.exp(-t / 0.025)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.075) * np.minimum(1, t / 0.0008)
    y += lp(rng.standard_normal(len(t)), 600) * np.exp(-t / 0.004) * 0.3  # beater
    return tail(y, 0.01)


def clap():
    """body tone + three-burst noise; the noise skips 1-4 kHz (low band + air band)."""
    t = tt(0.2)
    n = rng.standard_normal(len(t))
    bursts = sum(np.exp(-np.maximum(t - o, 0) / 0.004) * (t >= o) for o in (0, 0.009, 0.018))
    env = bursts * 0.5 + np.exp(-np.maximum(t - 0.018, 0) / 0.05) * (t >= 0.018)
    noise = bp(n, 400, 950) * 0.9 + bp(n, 5000, 11000) * 1.1
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.03) * 0.5
    return tail(noise * env + body, 0.02)


def shaker(acc):
    t = tt(0.05)
    e = np.minimum(1, t / 0.006) * np.exp(-t / 0.014)
    return bp(rng.standard_normal(len(t)), 6000, 12000) * e * acc


def tambourine():
    t = tt(0.16)
    jing = sum(np.sin(2 * np.pi * f * t + rng.uniform(0, 6)) for f in (7100, 8300, 9650, 10900))
    y = (bp(rng.standard_normal(len(t)), 7000, 13000) + 0.08 * jing) * np.exp(-t / 0.045)
    return tail(y * np.minimum(1, t / 0.001), 0.02)


def woodblock(f=880):
    t = tt(0.08)
    return (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * f * 2.3 * t)) * np.exp(-t / 0.02) * np.minimum(1, t / 0.0008)


def tom(f):
    t = tt(0.22)
    fr = f * (1 + 0.5 * np.exp(-t / 0.02))
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.09) * np.minimum(1, t / 0.001)
    return tail(y, 0.02)


# ---------------------------------------------------------------- score
CH = {"A": [61, 64, 69], "D": [62, 66, 69], "E": [59, 64, 68], "E7": [59, 62, 68], "F#m": [61, 66, 69]}
ROOT = {"A": (45, 52), "D": (38, 45), "E": (40, 47), "E7": (40, 47), "F#m": (42, 49)}
CHORDS = ["A", "D", "E", "A", "A", "D", "E", "E7",
          "F#m", "D", "A", "E", "F#m", "D", "E7", "A"]
N = None
MEL = [  # (midi or None, eighths)
    [(76, 1), (73, 1), (69, 1), (73, 1), (76, 2), (81, 2)],
    [(78, 1), (74, 1), (78, 1), (81, 1), (78, 2), (74, 2)],
    [(76, 1), (80, 1), (83, 1), (80, 1), (76, 1), (74, 1), (73, 1), (71, 1)],
    [(69, 2), (73, 1), (76, 1), (81, 3), (N, 1)],
    [(76, 1), (73, 1), (69, 1), (73, 1), (76, 2), (78, 1), (80, 1)],
    [(81, 1), (N, 1), (81, 1), (78, 1), (74, 2), (78, 2)],
    [(80, 1), (78, 1), (76, 1), (74, 1), (71, 2), (74, 2)],
    [(76, 2), (N, 1), (76, 1), (80, 1), (83, 1), (80, 1), (76, 1)],
    [(66, 1), (69, 1), (73, 1), (78, 2), (76, 1), (73, 2)],
    [(74, 1), (N, 1), (74, 1), (73, 1), (74, 1), (78, 1), (81, 2)],
    [(76, 1), (73, 1), (69, 1), (73, 1), (76, 1), (81, 1), (80, 1), (78, 1)],
    [(76, 3), (71, 1), (68, 2), (71, 2)],
    [(78, 1), (76, 1), (73, 1), (78, 1), (81, 2), (78, 2)],
    [(74, 1), (78, 1), (81, 1), (86, 1), (85, 2), (81, 2)],
    [(83, 1), (81, 1), (80, 1), (78, 1), (76, 1), (74, 1), (73, 1), (71, 1)],
    [(69, 3), (76, 1), (81, 2), (N, 2)],
]
assert len(MEL) == NBARS and all(sum(n for _, n in bar) == 8 for bar in MEL)


def render(intense):
    bus = Bus()
    add = bus.add
    for b, ch in enumerate(CHORDS):
        t0 = b * BAR
        root, fifth = ROOT[ch]
        fill_bar = b % 8 == 7

        # bouncing octave bass: R  R'  F  R'  R  R'  F  F'   (offbeats ghosted)
        pat = [root, root + 12, fifth, root + 12, root, root + 12, fifth, fifth + 12]
        if b % 4 == 3:  # walk into the next bar's root
            nxt = ROOT[CHORDS[(b + 1) % NBARS]][0]
            pat[6], pat[7] = nxt - 2, nxt - 1
        for e, m in enumerate(pat):
            add(bass(m, E8 * 0.9, 1.0 if e % 2 == 0 else 0.6), t0 + e * E8, 0, 0.5 if e % 2 == 0 else 0.3)

        # four-on-the-floor kick, clap on 2 and 4
        for beat in range(4):
            add(kick(), t0 + beat * BEAT, 0, 0.55)
        for beat in (1, 3):
            add(clap(), t0 + beat * BEAT, 0.05, 0.28)

        # 16th shaker, accented offbeats
        for s in range(16):
            acc = 1.0 if s % 4 == 2 else (0.6 if s % 2 == 0 else 0.4)
            if intense:
                acc *= 1.35
            add(shaker(acc), t0 + s * E16, -0.35, 0.22)

        # banjo "chick" on every offbeat eighth, low-passed to keep 1-4 kHz clear
        for e in (1, 3, 5, 7):
            for k, m in enumerate(CH[ch]):
                stab = lp(banjo(m, 0.14, bright=0.8), 3000)
                add(stab, t0 + e * E8 + k * 0.005, 0.4, 0.2)

        # bar-end woodblock pickup (A section), tom fill at the 8-bar turns
        if fill_bar:
            for k, f in enumerate((210, 180, 150, 120)):
                add(tom(f), t0 + 3 * BEAT + k * E16, (-0.3 + 0.2 * k), 0.45)
        elif b % 2 == 1:
            add(woodblock(), t0 + 3.5 * BEAT, 0.5, 0.12)
            add(woodblock(990), t0 + 3.75 * BEAT, 0.5, 0.09)

        # lead
        pos = t0
        for m, n8 in MEL[b]:
            d = n8 * E8
            if m is not None:
                if b < 8:
                    add(marimba(m, max(0.45, d * 1.5)), pos, -0.3, 0.5)
                    if intense:
                        add(lp(banjo(m - 12, max(0.3, d * 1.1), bright=0.75), 3200), pos, 0.3, 0.3)
                else:
                    add(lp(banjo(m - 12, max(0.3, d * 1.2), bright=0.85), 3500), pos, -0.25, 0.55)
                    if intense:
                        add(marimba(m, max(0.45, d * 1.5)), pos, 0.25, 0.3)
            pos += d

        # B section: soft marimba arpeggio on offbeat eighths (an octave up, quiet)
        if b >= 8:
            arp = CH[ch] + [CH[ch][0] + 12]
            for e in (1, 3, 5, 7):
                add(marimba(arp[(e // 2) % 4] + 12, 0.35), t0 + e * E8, 0.5, 0.13)

        if intense:
            for e in (1, 3, 5, 7):
                add(tambourine(), t0 + e * E8, 0.55, 0.22)
            add(kick(), t0 + 3.5 * BEAT, 0, 0.35)       # pickup kick on 4&
            add(clap(), t0 + 3.75 * BEAT, 0.05, 0.12)   # ghost clap
    return bus.buf


# ---------------------------------------------------------------- mix / master (circular)
def circ_filter(x, sos):
    """run over 3 periods, keep the middle one: steady-state and circular."""
    return signal.sosfilt(sos, np.tile(x, 3))[L:2 * L]


def peaking_sos(f0, gain_db, q):
    a = 10 ** (gain_db / 40)
    w0 = 2 * np.pi * f0 / SR
    al = np.sin(w0) / (2 * q)
    b = [1 + al * a, -2 * np.cos(w0), 1 - al * a]
    aa = [1 + al / a, -2 * np.cos(w0), 1 - al / a]
    return signal.tf2sos(np.array(b) / aa[0], np.array(aa) / aa[0])


IRS = []
irl = int(0.9 * SR)
ti = np.arange(irl) / SR
for c in range(2):
    ir = lp(np.random.default_rng(190 + c).standard_normal(irl), 4500) * np.exp(-ti / 0.22)
    ir[: int(0.01 * SR)] = 0
    ir /= np.sqrt(np.sum(ir ** 2))
    irp = np.zeros(L); irp[:irl] = ir
    IRS.append(np.fft.fft(irp))

SOS_HP = signal.butter(2, 32, btype="high", fs=SR, output="sos")
SOS_DIP = peaking_sos(2500, -4.0, 0.8)  # make room for SFX in 1-4 kHz


def premaster(buf):
    wet = np.stack([np.real(np.fft.ifft(np.fft.fft(buf[c]) * IRS[c])) for c in range(2)])
    mix = buf + 0.16 * wet  # drier than the menu bed: tighter, more driving
    return np.stack([circ_filter(circ_filter(c, SOS_HP), SOS_DIP) for c in mix])


def limit(mix, ceiling_db=-3.2):
    """circular look-ahead peak limiter (same as music.py)."""
    thr = 10 ** (ceiling_db / 20)
    peak = np.max(np.abs(mix), axis=0)
    g = np.minimum(1, thr / np.maximum(peak, 1e-9))
    la = int(0.003 * SR)
    gp = np.concatenate([g[-la:], g, g[:la]])
    gmin = -np.lib.stride_tricks.sliding_window_view(-gp, 2 * la + 1).max(axis=1)
    k = np.hanning(2 * la + 1); k /= k.sum()
    gs = np.real(np.fft.ifft(np.fft.fft(gmin) * np.fft.fft(np.roll(np.pad(k, (0, L - len(k))), -la))))
    gs = np.minimum(gs, gmin)
    return mix * gs, gs


meter = pyln.Meter(SR)
base = premaster(render(False))
gain = 10 ** ((-16 - meter.integrated_loudness(base.T)) / 20)
outs = {"match_loop.wav": base * gain}
if "--no-intense" not in sys.argv:
    outs["match_loop_intense.wav"] = premaster(render(True)) * gain  # same gain stage

limited = {}
for name, mix in outs.items():
    limited[name] = limit(mix)

# rotate (<= 4 ms, before the downbeat) so the base file starts on a near-zero sample in
# both channels: a cold first play has no click. Same rotation for both files keeps them aligned.
w = int(0.004 * SR)
cand = np.arange(L - w, L)
m0 = limited["match_loop.wav"][0]
best = cand[np.argmin(np.abs(m0[0, cand]) + np.abs(m0[1, cand]))]
print(f"rotated by {(L - best) / SR * 1000:.3f} ms ({L - best} frames)")


def band_share(x, lo=1000, hi=4000):
    mono = x.mean(axis=1)
    S = np.abs(np.fft.rfft(mono)) ** 2
    f = np.fft.rfftfreq(len(mono), 1 / SR)
    return S[(f >= lo) & (f < hi)].sum() / S[f >= 30].sum()


for name, (mix, gs) in limited.items():
    mix = np.roll(mix, L - best, axis=1)
    pcm = np.round(np.clip(mix, -1, 1) * 32767).astype(np.int16).T
    wavfile.write(os.path.join(OUT, name), SR, pcm)

    # verification on the written int16 data
    x = pcm.astype(float) / 32768
    d = np.abs(np.diff(x, axis=0))
    seam = np.abs(x[0] - x[-1])
    dd_seam = np.abs(np.diff(np.concatenate([x[-3:], x[:3]]), n=2, axis=0)).max(axis=0)
    dd_typ = np.percentile(np.abs(np.diff(x, n=2, axis=0)), 99.9, axis=0)
    # short-window RMS continuity: last 10 ms vs first 10 ms vs 10 ms windows elsewhere
    n10 = int(0.01 * SR)
    rms = lambda s: np.sqrt(np.mean(s ** 2))
    print(f"\n{name}: {len(x)} frames = {len(x)/SR:.3f} s, {BPM} BPM, {NBARS} bars")
    print(f"  LUFS {meter.integrated_loudness(x):.2f}  peak {20*np.log10(np.abs(x).max()):.2f} dBFS  "
          f"limiter max GR {-20*np.log10(gs.min()):.2f} dB  1-4 kHz energy share {band_share(x)*100:.1f}%")
    print(f"  seam jump L/R {seam.round(5)}  99.9pct step {np.percentile(d, 99.9, axis=0).round(4)}  max step {d.max(axis=0).round(4)}")
    print(f"  2nd diff at seam {dd_seam.round(5)}  typical 99.9pct {dd_typ.round(5)}")
    print(f"  first sample {x[0].round(5)}  last {x[-1].round(5)}  rms last10ms {rms(x[-n10:]):.4f} first10ms {rms(x[:n10]):.4f}")
