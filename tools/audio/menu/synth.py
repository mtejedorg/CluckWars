"""Procedural menu audio for Cluck Wars. No samples; numpy/scipy only."""
import os, sys, json
import numpy as np
from scipy import signal
from scipy.io import wavfile
import pyloudnorm as pyln

SR = 48000
OUT = r"C:\Users\MARCO\Documents\GitHub\CluckWars\Assets\_Game\Audio\UI"
os.makedirs(OUT, exist_ok=True)
RNG = np.random.default_rng(1234)


def tt(d):
    return np.arange(int(round(d * SR))) / SR


def db(x):
    return 20 * np.log10(max(x, 1e-12))


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, btype="low", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, btype="high", fs=SR, output="sos"), x)


def noise(n, seed=None):
    r = np.random.default_rng(seed) if seed is not None else RNG
    return r.standard_normal(n)


def place(buf, start_s, sig):
    i = int(round(start_s * SR))
    n = min(len(sig), len(buf) - i)
    if n > 0:
        buf[i:i + n] += sig[:n]
    return buf


def modal(dur, modes, seed=None):
    """modes: (freq, tau_seconds, amp). Damped sinusoids = struck wood/rubber."""
    t = tt(dur)
    y = np.zeros_like(t)
    r = np.random.default_rng(seed)
    for f, tau, a in modes:
        y += a * np.sin(2 * np.pi * f * t + r.uniform(0, 0.3)) * np.exp(-t / tau)
    return y


def fades(x, fin=0.001, fout=0.008):
    x = x.copy()
    ni, no = max(1, int(fin * SR)), max(1, int(fout * SR))
    x[:ni] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, ni))
    x[-no:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, no))
    return x


def trim_tail(x, thresh_db=-60):
    """Cut trailing near-silence (keep 10 ms after last sample above thresh)."""
    a = np.abs(x) / (np.max(np.abs(x)) + 1e-12)
    idx = np.where(a > 10 ** (thresh_db / 20))[0]
    end = min(len(x), idx[-1] + int(0.01 * SR)) if len(idx) else len(x)
    return x[:end]


# ---------------------------------------------------------------- SFX ----
def ui_tap():
    d = 0.07
    body = modal(d, [(880, 0.02, 1.0), (2040, 0.008, 0.5), (3310, 0.004, 0.3), (520, 0.025, 0.5)], 1)
    n = bp(noise(len(body), 2), 1500, 6000) * np.exp(-tt(d) / 0.0025) * 0.6
    return np.tanh(2.2 * (body + n)) / 2.2


def ui_back():
    d = 0.09
    body = modal(d, [(560, 0.018, 1.0), (1290, 0.009, 0.45), (2150, 0.005, 0.2), (330, 0.025, 0.35)], 3)
    n = bp(noise(len(body), 4), 600, 2500) * np.exp(-tt(d) / 0.003) * 0.35
    y = body + n
    att = np.minimum(1, tt(d) / 0.003)  # softer onset than tap
    return lp(y * att, 3500)


def ui_select():
    d = 0.16
    t = tt(d)
    f = 520 * (1500 / 520) ** np.clip(t / 0.07, 0, 1)  # upward sweep
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = (np.sin(ph) + 0.25 * np.sin(2 * ph)) * np.exp(-t / 0.045)
    pop = modal(d, [(1800, 0.004, 0.6), (3600, 0.002, 0.3)], 5)
    sparkle = np.sin(2 * np.pi * 3000 * t) * np.exp(-np.maximum(t - 0.05, 0) / 0.03) * np.clip((t - 0.05) / 0.004, 0, 1) * 0.12
    return tone + pop + sparkle


def slot_arm():
    d = 0.045
    body = modal(d, [(2200, 0.004, 0.7), (1400, 0.006, 0.5), (3800, 0.002, 0.3)], 6)
    n = bp(noise(len(body), 7), 2000, 7000) * np.exp(-tt(d) / 0.0015) * 0.5
    return lp(body + n, 6000)


def equip_thunk():
    d = 0.28
    t = tt(d)
    f = 140 * np.exp(-t / 0.08) + 85
    ph = 2 * np.pi * np.cumsum(f) / SR
    low = np.sin(ph) * np.exp(-t / 0.07)
    wood = modal(d, [(310, 0.05, 0.7), (720, 0.025, 0.45), (1180, 0.012, 0.3), (1950, 0.006, 0.15)], 8)
    n = lp(noise(len(t), 9), 1800) * np.exp(-t / 0.006) * 0.8
    return low * 1.1 + wood + n


def clear_pop():
    d = 0.09
    t = tt(d)
    f = 950 * (380 / 950) ** np.clip(t / 0.05, 0, 1)
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = np.sin(ph) * np.exp(-t / 0.022)
    click = bp(noise(len(t), 10), 1500, 5000) * np.exp(-t / 0.0012) * 0.5
    return tone + click


def bell(f, d, amp=1.0, seed=0):
    # small inharmonic bell / jingle partials
    y = modal(d, [(f, 0.18, 1.0), (f * 2.76, 0.07, 0.45), (f * 5.40, 0.03, 0.25), (f * 1.5, 0.1, 0.2)], seed)
    return amp * y * np.minimum(1, tt(d) / 0.0015)


def ready_stamp():
    d = 0.65
    t = tt(d)
    f = 120 * np.exp(-t / 0.05) + 55
    ph = 2 * np.pi * np.cumsum(f) / SR
    thump = np.sin(ph) * np.exp(-t / 0.09) * 1.2
    rubber = modal(d, [(210, 0.04, 0.6), (470, 0.02, 0.35), (900, 0.008, 0.15)], 11)
    slap = lp(noise(len(t), 12), 2500) * np.exp(-t / 0.008) * 0.7
    y = thump + rubber + slap
    jing = np.zeros_like(t)
    place(jing, 0.07, bell(1318.5, 0.5, 0.35, 13))   # E6
    place(jing, 0.15, bell(1975.5, 0.45, 0.30, 14))  # B6
    shk = hp(noise(len(t), 15), 6000) * np.exp(-np.maximum(t - 0.07, 0) / 0.05) * np.clip((t - 0.07) / 0.003, 0, 1) * 0.08
    return y + jing + shk


def countdown_tick():
    d = 0.14
    t = tt(d)
    y = modal(d, [(1046.5, 0.05, 1.0), (2093, 0.02, 0.3), (4186 * 0.98, 0.006, 0.2)], 16)  # C6 marimba-ish tick
    click = bp(noise(len(t), 17), 2000, 8000) * np.exp(-t / 0.0015) * 0.4
    return y + click


def saw(ph, k=12):
    return sum(np.sin(i * ph) / i for i in range(1, k + 1))


def countdown_go():
    d = 0.6
    t = tt(d)
    y = np.zeros_like(t)
    for m in (69, 73, 76, 81):  # A major, bright
        f0 = 440 * 2 ** ((m - 69) / 12)
        f = f0 * (1 - 0.12 * np.exp(-t / 0.025))  # quick upward scoop
        ph = 2 * np.pi * np.cumsum(f) / SR
        y += saw(ph, 8) * 0.25
    env = np.minimum(1, t / 0.005) * np.exp(-t / 0.22)
    y = lp(y, 5000) * env
    burst = hp(noise(len(t), 18), 4000) * np.exp(-t / 0.06) * 0.12
    thump = np.sin(2 * np.pi * np.cumsum(110 * np.exp(-t / 0.04) + 60) / SR) * np.exp(-t / 0.08) * 0.7
    return y + burst + thump


def brass(m, d, amp=1.0, vib=True):
    t = tt(d)
    f0 = 440 * 2 ** ((m - 69) / 12)
    v = 1 + (0.006 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.15) / 0.2, 0, 1) if vib else 0)
    f = f0 * v * (1 - 0.04 * np.exp(-t / 0.03))
    ph = 2 * np.pi * np.cumsum(f) / SR
    raw = saw(ph, 14)
    # brightness envelope: dynamic low-pass via blending two filtered copies
    bright = lp(raw, 4500)
    dark = lp(raw, 1200)
    benv = np.exp(-t / 0.12) * 0.7 + 0.3
    y = bright * benv + dark * (1 - benv)
    env = np.minimum(1, t / 0.012) * (0.75 + 0.25 * np.exp(-t / 0.1))
    rel = int(0.05 * SR)
    env[-rel:] *= np.linspace(1, 0, rel)
    return y * env * amp


def match_sting():
    d = 1.75
    t = tt(d)
    y = np.zeros_like(t)
    step = 0.11
    for i, m in enumerate([67, 72, 76, 79]):  # G4 C5 E5 G5 run
        place(y, i * step, brass(m, step * 1.15, 0.5, vib=False))
    hold = 4 * step
    for m in (72, 76, 79, 84):  # C major chord, top C6
        place(y, hold, brass(m, d - hold - 0.02, 0.32))
    # snare-ish roll swelling into the hit
    roll = np.zeros_like(t)
    for k in range(int(hold / 0.035)):
        s = tt(0.03)
        place(roll, k * 0.035, bp(noise(len(s), 100 + k), 1500, 7000) * np.exp(-s / 0.012) * (0.15 + 0.35 * k / (hold / 0.035)))
    # cymbal + timpani hit on the chord
    tc = t - hold
    cym = hp(noise(len(t), 19), 5000) * np.exp(-np.maximum(tc, 0) / 0.3) * np.clip(tc / 0.002, 0, 1) * 0.15
    timp = np.sin(2 * np.pi * np.cumsum(np.where(tc > 0, 90 * np.exp(-np.maximum(tc, 0) / 0.1) + 65, 0)) / SR) * np.exp(-np.maximum(tc, 0) / 0.3) * np.clip(tc / 0.002, 0, 1) * 0.9
    return y + roll * 0.6 + cym + timp


# ------------------------------------------------------------ CLUCKS -----
def contour(points, n):
    """points: list of (frac, f0); log-linear interpolation."""
    xs = np.array([p[0] for p in points]); ys = np.log([p[1] for p in points])
    return np.exp(np.interp(np.linspace(0, 1, n), xs, ys))


def syllable(d, f0pts, formants, rasp=0.3, sub=0.0, breath=0.1, att=0.006, decay_frac=0.6, seed=0):
    n = int(d * SR)
    t = np.arange(n) / SR
    r = np.random.default_rng(seed)
    jitter = 1 + 0.025 * lp(r.standard_normal(n), 40) * 8
    f0 = contour(f0pts, n) * jitter
    ph = 2 * np.pi * np.cumsum(f0) / SR
    src = np.zeros(n)
    for k in range(1, 40):
        src += (np.sin(k * ph) / k ** 0.7) * (k * f0 < 9000)
    src += sub * np.sin(ph / 2)
    # roughness: fast irregular amplitude modulation (the "rasp" in a cluck)
    am = 1 + rasp * np.tanh(3 * lp(r.standard_normal(n), 120) * 6)
    src *= am
    src += breath * bp(r.standard_normal(n), 1200, 7000) * 2
    voc = np.zeros(n)
    for f, bw, g in formants:
        voc += g * bp(src, max(60, f - bw / 2), f + bw / 2, 2)
    # envelope: fast attack, short plateau, decay
    env = np.minimum(1, t / att)
    dstart = d * (1 - decay_frac)
    env *= np.where(t < dstart, 1, np.exp(-(t - dstart) / (d * decay_frac / 3.5)))
    return voc * env


def cluck_warrior():
    F = [(750, 300, 1.0), (1500, 400, 0.7), (2700, 600, 0.35)]
    y = np.zeros(int(0.56 * SR))
    place(y, 0.00, syllable(0.08, [(0, 300), (0.4, 330), (1, 250)], F, rasp=0.8, sub=0.45, breath=0.08, decay_frac=0.6, seed=21) * 1.0)
    place(y, 0.15, syllable(0.36, [(0, 250), (0.12, 430), (0.45, 380), (1, 240)], F, rasp=0.8, sub=0.4, breath=0.1, att=0.01, decay_frac=0.55, seed=22))
    return y


def cluck_speedy():
    F = [(1100, 400, 1.0), (2400, 600, 0.6), (3600, 800, 0.3)]
    y = np.zeros(int(0.42 * SR))
    for i, s in enumerate([0.0, 0.085, 0.17]):
        place(y, s, syllable(0.06, [(0, 720 + 40 * i), (0.3, 980 + 40 * i), (1, 820)], F, rasp=0.15, breath=0.05, att=0.003, decay_frac=0.7, seed=30 + i) * 0.75)
    place(y, 0.26, syllable(0.14, [(0, 800), (0.25, 1250), (1, 900)], F, rasp=0.2, breath=0.05, att=0.003, decay_frac=0.65, seed=34))
    return y


def cluck_fatty():
    F = [(520, 250, 1.0), (900, 300, 0.6), (2300, 500, 0.15)]
    y = np.zeros(int(0.6 * SR))
    place(y, 0.0, syllable(0.12, [(0, 210), (0.3, 225), (1, 170)], F, rasp=0.3, sub=0.25, breath=0.03, att=0.012, decay_frac=0.6, seed=41) * 0.85)
    place(y, 0.2, syllable(0.38, [(0, 170), (0.2, 260), (0.6, 220), (1, 150)], F, rasp=0.3, sub=0.3, breath=0.03, att=0.02, decay_frac=0.55, seed=42))
    return lp(y, 4000)


def cluck_assassin():
    F = [(680, 280, 1.0), (1350, 400, 0.6), (2600, 600, 0.35)]
    n = int(0.6 * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    hiss = bp(noise(n, 50), 3500, 9500, 3)
    henv = np.clip(t / 0.12, 0, 1) * np.exp(-np.maximum(t - 0.12, 0) / 0.04)
    y += hiss * henv * 0.35
    for i, s in enumerate([0.15, 0.22]):
        place(y, s, syllable(0.05, [(0, 340), (1, 300)], F, rasp=0.5, breath=0.35, att=0.003, decay_frac=0.7, seed=51 + i) * 0.6)
    place(y, 0.31, syllable(0.25, [(0, 400), (0.2, 420), (1, 230)], F, rasp=0.5, breath=0.45, att=0.008, decay_frac=0.6, seed=53))
    # sly trailing hiss
    tail = bp(noise(n, 54), 4000, 9000, 3) * np.exp(-np.maximum(t - 0.4, 0) / 0.05) * np.clip((t - 0.34) / 0.06, 0, 1) * 0.12
    return y + tail


# ------------------------------------------------------------ MASTER -----
METER = pyln.Meter(SR)


def kweight(x):
    y = x.copy()
    for f in METER._filters.values():
        y = f.apply_filter(y)
    return y


def max_loud_100ms(x):
    """Max K-weighted loudness over a 100 ms window (LUFS-like; short sounds)."""
    k = kweight(np.concatenate([x, np.zeros(int(0.1 * SR))]))
    w = int(0.1 * SR)
    e = np.convolve(k ** 2, np.ones(w) / w, mode="valid")
    return -0.691 + 10 * np.log10(e.max() + 1e-20)


PEAK_TARGET = 10 ** (-3 / 20)
LOUD_TARGET = -15.0  # max 100 ms loudness for SFX


def master_sfx(x):
    x = x - np.mean(x)
    x = hp(x, 40)
    x = trim_tail(x)
    x = fades(x)
    x = x / np.max(np.abs(x)) * PEAK_TARGET
    L = max_loud_100ms(x)
    if L > LOUD_TARGET:  # too dense/loud -> pull down to match the group
        x *= 10 ** ((LOUD_TARGET - L) / 20)
    return x


def write(name, x, stereo=False):
    path = os.path.join(OUT, name)
    data = np.clip(x, -1, 1)
    pcm = np.round(data * 32767).astype(np.int16)
    wavfile.write(path, SR, pcm)
    return path


SFX = {
    "ui_tap.wav": ui_tap, "ui_back.wav": ui_back, "ui_select.wav": ui_select,
    "cluck_warrior.wav": cluck_warrior, "cluck_speedy.wav": cluck_speedy,
    "cluck_fatty.wav": cluck_fatty, "cluck_assassin.wav": cluck_assassin,
    "slot_arm.wav": slot_arm, "equip_thunk.wav": equip_thunk, "clear_pop.wav": clear_pop,
    "ready_stamp.wav": ready_stamp, "countdown_tick.wav": countdown_tick,
    "countdown_go.wav": countdown_go, "match_sting.wav": match_sting,
}

if __name__ == "__main__":
    which = sys.argv[1:] or list(SFX)
    for name in which:
        if name in SFX:
            write(name, master_sfx(SFX[name]()))
            print("wrote", name)
