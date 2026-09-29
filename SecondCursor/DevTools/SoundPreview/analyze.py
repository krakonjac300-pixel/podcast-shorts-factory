#!/usr/bin/env python3
"""Plots waveform + spectrogram PNGs for every WAV rendered by SoundPreview and prints spectral stats.

usage: python3 analyze.py [outDir]      (default: ./out next to this script)

Per sound it reports: energy above 12 kHz relative to total (dB), spectral centroid, the -20 dB
bandwidth edge, and for loops a circular seam test (the seam's first/second difference percentile
among every sample boundary in the loop - a clean seam is not an outlier).
"""
import os
import sys
import wave

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from scipy import signal  # noqa: E402

LOOPS = {"shred_loop", "amb_room", "amb_fluorescent", "amb_crt_hum", "drone_tension",
         "entity_static", "tug_strain", "camera_static"}


def read_wav(path):
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32767.0
    return sr, x


def spectral_stats(x, sr):
    f, p = signal.welch(x, sr, nperseg=min(4096, len(x)), noverlap=None, scaling="spectrum")
    total = p.sum() + 1e-30
    hi = p[f >= 12000].sum()
    centroid = (f * p).sum() / total
    cum = np.cumsum(p) / total
    f99 = f[np.searchsorted(cum, 0.99)]
    return 10 * np.log10(hi / total + 1e-30), centroid, f99


def seam_test(x):
    d1 = np.abs(np.diff(np.concatenate([x[-1:], x])))           # d1[0] is the seam
    d2 = np.abs(np.diff(np.concatenate([x[-2:], x]), n=2))      # d2[0] is the seam
    p1 = (d1 < d1[0]).mean() * 100
    p2 = (d2 < d2[0]).mean() * 100
    return p1, p2


def plot(name, x, sr, out_png, is_loop):
    fig = plt.figure(figsize=(13, 7.5))
    gs = fig.add_gridspec(2, 3 if is_loop else 1, height_ratios=[1, 1.6])
    if is_loop:
        y = np.concatenate([x, x])
        t = np.arange(len(y)) / sr
        ax = fig.add_subplot(gs[0, 0:2])
        ax.plot(t, y, lw=0.4, color="#1f4e79")
        ax.axvline(len(x) / sr, color="r", lw=0.8, ls="--")
        ax.set_title(f"{name} (loop x2, seam dashed)")
        ax.set_ylim(-1, 1)
        ax.axhline(0.9, color="gray", lw=0.5, ls=":")
        ax.axhline(-0.9, color="gray", lw=0.5, ls=":")
        axz = fig.add_subplot(gs[0, 2])
        k = int(0.005 * sr)
        seg = np.concatenate([x[-k:], x[:k]])
        axz.plot((np.arange(len(seg)) - k) / sr * 1000, seg, lw=0.8, color="#1f4e79")
        axz.axvline(0, color="r", lw=0.8, ls="--")
        axz.set_title("seam +/-5 ms")
        axz.set_xlabel("ms")
        sig = y
        axs = fig.add_subplot(gs[1, :])
    else:
        t = np.arange(len(x)) / sr
        ax = fig.add_subplot(gs[0, 0])
        ax.plot(t, x, lw=0.5, color="#1f4e79")
        ax.set_ylim(-1, 1)
        ax.axhline(0.9, color="gray", lw=0.5, ls=":")
        ax.axhline(-0.9, color="gray", lw=0.5, ls=":")
        ax.set_title(name)
        sig = x
        axs = fig.add_subplot(gs[1, 0])
    nper = 256 if len(sig) < sr * 0.3 else 1024
    f, tt, s = signal.spectrogram(sig, sr, nperseg=nper, noverlap=nper * 7 // 8, window="hann")
    sdb = 10 * np.log10(s + 1e-14)
    vmax = sdb.max()
    axs.pcolormesh(tt, f / 1000, sdb, vmin=vmax - 90, vmax=vmax, shading="auto", cmap="magma")
    axs.axhline(12, color="cyan", lw=0.6, ls="--")
    axs.set_ylabel("kHz")
    axs.set_xlabel("s")
    fig.tight_layout()
    fig.savefig(out_png, dpi=72)
    plt.close(fig)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    names = sorted(f[:-4] for f in os.listdir(out) if f.endswith(".wav"))
    print(f"{'id':<17} {'>12k dB':>8} {'centroid':>9} {'f99':>7} {'seamP1':>7} {'seamP2':>7}")
    for name in names:
        sr, x = read_wav(os.path.join(out, name + ".wav"))
        is_loop = name in LOOPS
        hi, cen, f99 = spectral_stats(x, sr)
        p1 = p2 = ""
        if is_loop:
            a, b = seam_test(x)
            p1, p2 = f"{a:6.1f}%", f"{b:6.1f}%"
        print(f"{name:<17} {hi:8.1f} {cen:9.0f} {f99:7.0f} {p1:>7} {p2:>7}")
        plot(name, x, sr, os.path.join(out, name + ".png"), is_loop)


if __name__ == "__main__":
    main()
