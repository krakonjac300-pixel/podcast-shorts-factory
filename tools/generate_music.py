"""Generate a small license-free background-music library into assets/music/.

Four ~32s seamless loops, one per mood the planner uses: upbeat, tense, lofi,
ambient. Pure synthesis (numpy) — no samples, no copyright, safe to monetize.
They play DUCKED at ~12% volume under the voice, where soft synth pads read as
"produced", not "MIDI demo".

Run:  .venv\\Scripts\\python.exe tools\\generate_music.py
"""
from __future__ import annotations

import wave
from pathlib import Path

import numpy as np

SR = 44100
OUT = Path(__file__).resolve().parents[1] / "assets" / "music"

# chord progressions as frequency stacks (Hz), 4 bars each
A3, C4, D4, E4, F4, G4, A4, B4, C5, D5, E5 = (
    220.00, 261.63, 293.66, 329.63, 349.23, 392.00, 440.00, 493.88,
    523.25, 587.33, 659.25)

PROGRESSIONS = {
    "upbeat":  ([[C4, E4, G4], [G4/2, B4/2, D5/2], [A3, C4, E4], [F4/2, A4/2, C5/2]], 118),
    "tense":   ([[A3, C4, E4], [A3, C4, F4], [A3, B4/2, E4], [A3/2, C4, E4]], 90),
    "lofi":    ([[F4/2, A4/2, C5/2], [E4/2, G4/2, B4/2], [D4, F4, A4], [C4, E4, G4]], 82),
    "ambient": ([[C4/2, G4/2, E4], [A3/2, E4/2, C5/2], [F4/4, C4, A4], [G4/4, D4, B4]], 60),
}


def _env(n: int, a: float, r: float) -> np.ndarray:
    """Attack/release envelope over n samples."""
    env = np.ones(n)
    na, nr = int(a * SR), int(r * SR)
    if na:
        env[:na] = np.linspace(0, 1, na)
    if nr:
        env[-nr:] = np.linspace(1, 0, nr)
    return env


def _pad(freqs, dur: float, soft: float) -> np.ndarray:
    """Detuned-saw-ish pad: layered sines + slight detune = warm and wide."""
    t = np.arange(int(dur * SR)) / SR
    sig = np.zeros_like(t)
    for f in freqs:
        for detune, amp in ((1.0, 1.0), (1.003, 0.6), (0.997, 0.6), (2.0, 0.15)):
            sig += amp * np.sin(2 * np.pi * f * detune * t)
    sig *= _env(len(t), 0.4 * soft + 0.05, 0.5 * soft + 0.1)
    return sig / max(1e-9, np.abs(sig).max())


def _kick(t_beat: np.ndarray) -> np.ndarray:
    """Soft synth kick: pitch-dropping sine with fast decay."""
    f = 110 * np.exp(-t_beat * 18) + 45
    return np.sin(2 * np.pi * f * t_beat) * np.exp(-t_beat * 22)


def _hat(t_beat: np.ndarray, rng) -> np.ndarray:
    return rng.standard_normal(len(t_beat)) * np.exp(-t_beat * 90) * 0.5


def make_track(mood: str, loops: int = 4) -> np.ndarray:
    chords, bpm = PROGRESSIONS[mood]
    bar = 4 * 60 / bpm                       # 4/4 bar length in seconds
    rng = np.random.default_rng(42)
    soft = {"upbeat": 0.2, "tense": 0.6, "lofi": 0.8, "ambient": 1.0}[mood]

    bars = []
    for chord in chords:
        pad = _pad(chord, bar, soft) * 0.7
        if mood == "tense":                  # slow pulse for suspense
            t = np.arange(len(pad)) / SR
            pad *= 0.75 + 0.25 * np.sin(2 * np.pi * (bpm / 120) * t)
        if mood in ("upbeat", "lofi"):       # light beat
            beat = np.zeros(len(pad))
            step = int(60 / bpm * SR)
            for k in range(0, len(pad) - step, step):
                seg = np.arange(min(step, len(pad) - k)) / SR
                beat[k:k + len(seg)] += _kick(seg) * (0.5 if mood == "lofi" else 0.8)
                if mood == "upbeat":
                    off = k + step // 2
                    if off + len(seg) <= len(pad):
                        beat[off:off + len(seg)] += _hat(seg, rng) * 0.35
            pad = pad + beat * 0.5
        if mood == "lofi":                   # vinyl hiss
            pad += rng.standard_normal(len(pad)) * 0.006
        bars.append(pad)

    loop = np.concatenate(bars)
    sig = np.tile(loop, loops)
    sig = sig / max(1e-9, np.abs(sig).max()) * 0.35    # leave plenty of headroom
    return sig


def write_wav(path: Path, sig: np.ndarray) -> None:
    stereo = np.stack([sig, sig]).T
    pcm = (np.clip(stereo, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as f:
        f.setnchannels(2)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(pcm.tobytes())


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for mood in PROGRESSIONS:
        p = OUT / f"{mood}.wav"
        write_wav(p, make_track(mood))
        print(f"wrote {p} ({p.stat().st_size // 1024} KB)")
    print("done — the editor picks these by the planner's music_mood.")
