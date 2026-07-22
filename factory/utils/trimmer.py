"""Filler-word and silence trimming from word-level timestamps.

Produces (a) an ffmpeg select() expression of the time ranges to KEEP and
(b) re-timed words for captions on the trimmed timeline. This is the single
biggest "amateur vs pro" lever in short-form: cut dead air and disfluencies.
"""
from __future__ import annotations

import re

# Conservative defaults — disfluencies that are almost never meaningful.
DEFAULT_FILLERS = ["um", "uh", "uhh", "uhm", "erm", "hmm", "mm", "mmm", "ah", "er"]
DEFAULT_PHRASES: list[str] = []  # e.g. ["you know", "i mean"] — opt in via config


def _norm(word: str) -> str:
    return re.sub(r"[^\w]", "", word).lower()


def compute(words: list[dict], start: float, end: float, conf: dict,
            protect: list[tuple[float, float]] | None = None) -> dict | None:
    """Return {expr, new_words, new_dur, stats} or None if nothing worth trimming.

    `expr` ranges are CLIP-LOCAL (relative to `start`) for ffmpeg input-seek.
    `new_words` are on the trimmed 0-based timeline for captions.
    """
    if not conf.get("enabled", True):
        return None
    pad = float(conf.get("pad", 0.05))
    max_gap = float(conf.get("max_gap", 0.45))
    min_removed = float(conf.get("min_removed", 0.2))

    sub = [w for w in words if w.get("end", 0) > start and w.get("start", 0) < end]
    if len(sub) < 3:
        return None

    single = set(conf.get("fillers", DEFAULT_FILLERS))
    phrases = [p.split() for p in conf.get("phrases", DEFAULT_PHRASES)]
    norm = [_norm(w["word"]) for w in sub]

    remove = [False] * len(sub)
    if conf.get("remove_fillers", True):
        for i, tok in enumerate(norm):
            if tok in single:
                remove[i] = True
        for toks in phrases:
            L = len(toks)
            for i in range(len(norm) - L + 1):
                if norm[i:i + L] == [_norm(t) for t in toks]:
                    for j in range(i, i + L):
                        remove[j] = True

    # Stammer pass: a word immediately re-spoken ("the the", a stretched word
    # then its clean retake) → drop the FIRST instance. Deliberate doubling
    # ("really really", "no no") is left alone via the whitelist.
    if conf.get("remove_stammers", True):
        keepers = {"very", "really", "no", "yes", "so", "many", "far", "way"}
        for i in range(len(norm) - 1):
            if not norm[i] or norm[i] != norm[i + 1] or norm[i] in keepers:
                continue
            dur = sub[i]["end"] - sub[i]["start"]
            gap = sub[i + 1]["start"] - sub[i]["end"]
            if len(norm[i]) >= 3 and (gap < 0.4 or dur > 1.2):
                remove[i] = True

    # PROTECT WINDOWS (absolute times): inside them, nothing is removed and
    # pauses are kept. A study of 50 viral money clips: for confession/reveal
    # moments the hesitation before the payoff IS the product, and compressing
    # it is the most common clipper mistake. The planner marks the payoff; we
    # keep our hands off the surrounding beat.
    protect = protect or []

    def _protected(t0: float, t1: float) -> bool:
        return any(t0 < pb and t1 > pa for pa, pb in protect)

    if protect:
        for i, w in enumerate(sub):
            if _protected(w["start"], w["end"]):
                remove[i] = False

    kept = [w for w, r in zip(sub, remove) if not r]
    if len(kept) < 3:
        return None

    # Build padded keep intervals (absolute), then merge across short pauses.
    intervals = []
    for w in kept:
        a = max(start, w["start"] - pad)
        b = min(end, w["end"] + pad)
        if b > a:
            intervals.append([a, b])
    if not intervals:
        return None
    intervals.sort()
    merged = [intervals[0]]
    for a, b in intervals[1:]:
        gap_a, gap_b = merged[-1][1], a
        if gap_b - gap_a <= max_gap:            # short pause → keep it
            merged[-1][1] = max(merged[-1][1], b)
        elif protect and _protected(gap_a, gap_b):
            merged[-1][1] = max(merged[-1][1], b)   # protected beat: keep it
        else:                                    # long pause → cut it out
            merged.append([a, b])

    # The pause-merge above can bridge right back over a removed word (a 0.2s
    # "um" between tight speech). Subtract removed spans explicitly so fillers
    # and stammers are ALWAYS cut — the quick jump cut is the shorts aesthetic.
    cut_spans = [(w["start"], w["end"]) for w, r in zip(sub, remove) if r]
    for ca, cb in cut_spans:
        nxt = []
        for a, b in merged:
            if cb <= a or ca >= b:
                nxt.append([a, b])
                continue
            if ca - a > 0.06:
                nxt.append([a, ca])
            if b - cb > 0.06:
                nxt.append([cb, b])
        merged = nxt
    if not merged:
        return None

    # cumulative offsets to map absolute time → trimmed-local time
    offsets, acc = [], 0.0
    for a, b in merged:
        offsets.append(acc)
        acc += (b - a)
    total = acc

    def to_local(t: float) -> float:
        for (a, b), off in zip(merged, offsets):
            if t < a:
                return off
            if t <= b:
                return off + (t - a)
        return total

    new_words = []
    for w in kept:
        ns = to_local(max(w["start"], start))
        ne = to_local(min(w["end"], end))
        if ne > ns:
            new_words.append({"start": ns, "end": ne, "word": w["word"]})

    removed = (end - start) - total
    if removed < min_removed:
        return None  # not worth the extra encode pass

    expr = "+".join(f"between(t,{a - start:.3f},{b - start:.3f})" for a, b in merged)
    return {"expr": expr, "new_words": new_words, "new_dur": total,
            "segments": [(a - start, b - start) for a, b in merged],  # clip-local
            "stats": {"orig": end - start, "trimmed": total,
                      "removed": removed, "segments": len(merged)}}
