"""Build a karaoke-style .ass subtitle file from word timestamps."""
from __future__ import annotations

import re
from pathlib import Path


def _norm(word: str) -> str:
    """Lowercase, strip punctuation — for matching emphasis words."""
    return re.sub(r"[^\w]", "", word).lower()


def _ts(seconds: float) -> str:
    """ASS timestamp: H:MM:SS.cs"""
    if seconds < 0:
        seconds = 0
    h = int(seconds // 3600)
    m = int((seconds % 3600) // 60)
    s = seconds % 60
    return f"{h}:{m:02d}:{s:05.2f}"


def build_ass(words: list[dict], clip_start: float, clip_end: float,
              style: dict, res=(1080, 1920), emphasis_words=None) -> str:
    """Return ASS subtitle text. `words` are absolute-time word dicts; we shift
    them so the clip starts at 0. Words are grouped ~3 at a time and the active
    word is highlighted. Words in `emphasis_words` (the storytelling/VFX key
    words) are rendered larger and pre-colored for visual punch."""
    w, h = res
    font = style.get("font", "Arial")
    size = style.get("font_size", 90)
    emph_size = int(size * 1.3)
    emph_set = {_norm(x) for x in (emphasis_words or []) if _norm(x)}
    primary = style.get("primary_color", "&H00FFFFFF")
    highlight = style.get("highlight_color", "&H0000F0FF")
    # RED for the key emphasis words (the pro-clip look — matches top DOAC edits)
    emphasis = style.get("emphasis_color", "&H000000FF")
    outline = style.get("outline", 6)
    # Vertical placement. "lower" (default) sits in the lower-third — below a
    # punched-in face's chin but above the CTA/progress bar — so captions never
    # cover the speaker's face. "center" = dead middle (on the face; avoid).
    pos = style.get("position", "lower")
    if pos == "center":
        align, margin_v = 5, 0
    elif pos == "bottom":
        align, margin_v = 2, 220
    else:  # "lower" — anchor at bottom, lift into the lower third (~62% down)
        align = 2
        margin_v = int(h * float(style.get("caption_lift", 0.34)))

    header = f"""[Script Info]
ScriptType: v4.00+
PlayResX: {w}
PlayResY: {h}
WrapStyle: 2

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, OutlineColour, BackColour, Bold, Outline, Shadow, Alignment, MarginL, MarginR, MarginV
Style: Main,{font},{size},{primary},&H00000000,&H64000000,1,{outline},0,{align},80,80,{margin_v}

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""

    # keep only words inside the clip, shifted to clip-local time
    upper = style.get("uppercase", True)
    local = []
    for word in words:
        if word["end"] <= clip_start or word["start"] >= clip_end:
            continue
        text = word["word"].strip()
        if not text:
            continue
        # whisper splits numbers like "220,000" into "220" + ",000" — glue
        # punctuation-led fragments back onto the previous word
        if local and text[0] in ",.'%":
            local[-1]["word"] += text.upper() if upper else text
            local[-1]["end"] = max(0.0, word["end"] - clip_start)
            continue
        local.append({
            "start": max(0.0, word["start"] - clip_start),
            "end": max(0.0, word["end"] - clip_start),
            "word": text.upper() if upper else text,
        })

    # pop-in: the active word starts at 70% and springs to 100% in 80ms —
    # \t runs from each Dialogue line's own start, i.e. exactly when the
    # word is spoken.
    pop = "\\fscx70\\fscy70\\t(0,80,\\fscx100\\fscy100)"

    # page the words: up to words_per_page per page, but END a page early at
    # sentence punctuation so fragments like "IT. SO THEY" never happen
    group_size = max(1, int(style.get("words_per_page", 3)))
    groups, cur = [], []
    for wd in local:
        cur.append(wd)
        if len(cur) >= group_size or wd["word"].rstrip()[-1:] in ".?!":
            groups.append(cur)
            cur = []
    if cur:
        groups.append(cur)

    lines = []
    for group in groups:
        g_start = group[0]["start"]
        g_end = group[-1]["end"]

        # shrink long groups so BOLD CAPS never run off the frame edges
        chars = len(" ".join(x["word"] for x in group))
        g_size = min(size, int((w - 170) / (0.62 * max(chars, 1))))
        g_emph = int(g_size * 1.3)

        # one dialogue per active word so the highlight moves
        for j, active in enumerate(group):
            seg_start = active["start"]
            seg_end = group[j + 1]["start"] if j + 1 < len(group) else g_end

            def render(x):
                word = x["word"]
                emph = _norm(word) in emph_set
                if x is active:                  # spoken NOW: color + pop
                    col = emphasis if emph else highlight   # key word → RED
                    fs = f"\\fs{g_emph}" if emph else ""
                    return (f"{{\\c{col}{fs}{pop}}}{word}"
                            f"{{\\c{primary}\\fs{g_size}\\fscx100\\fscy100}}")
                if emph:                         # key word (not active): big + RED
                    return (f"{{\\fs{g_emph}\\c{emphasis}}}{word}"
                            f"{{\\fs{g_size}\\c{primary}}}")
                return word

            text = " ".join(render(x) for x in group)
            lines.append(
                f"Dialogue: 0,{_ts(seg_start)},{_ts(seg_end)},Main,,0,0,0,,"
                f"{{\\fs{g_size}}}{text}"
            )

    return header + "\n".join(lines) + "\n"


def write_ass(path: Path, *args, **kwargs) -> Path:
    path.write_text(build_ass(*args, **kwargs), encoding="utf-8")
    return path
