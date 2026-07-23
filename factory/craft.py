"""The craft feedback loop: the editor learns editing from its own results.

Every render records WHAT IT DID (cut rate, punch count, SFX count, hook length,
caption density, reframe mode...) into `edit_specs`. Once those clips have real
retention numbers, this module joins the two and asks one question per knob:

    do the clips where I turned this knob UP actually hold viewers longer?

Findings are written to `craft.md`, which the planner loads into every edit
prompt. So the editor stops taking craft advice from whoever last edited the
config and starts taking it from the audience.

Deliberately conservative. A young channel produces tiny, noisy samples, and a
loop that "learns" from noise is worse than no loop at all: it will happily chase
a random 3-clip fluke and drag real quality down with full confidence. So a
finding must clear ALL of:
  * MIN_CLIPS total clips measured before we claim anything,
  * MIN_SIDE clips on each side of the median split,
  * MIN_EFFECT percentage points of retention difference.
Anything short of that is reported as "still measuring", never as a rule.
"""
from __future__ import annotations

from datetime import datetime

from . import db
from .config import ROOT, cfg

# Evidence bar. Tuned for a small channel: loose enough to ever fire, strict
# enough that a coin-flip run of good luck cannot mint a rule.
MIN_CLIPS = 18          # total measured clips before any rule is claimed
                        # (was 10: too permissive, rules flipped on new data)
MIN_SIDE = 4            # clips required on EACH side of a median split
MIN_EFFECT = 8.0        # retention points; below this it is noise, not craft
                        # (was 4.0: a 4-point gap on ~20 clips is not a finding)
MIN_VIEWS = 50          # a clip nobody watched has no opinion worth learning from.
                        # Without this the loop scored 4 zero-view clips and held a
                        # 9-VIEW clip up as "copy what this did" in every edit prompt,
                        # inflating the headline effect size by 68% (-31.9 vs -19.0
                        # points once filtered). _deterministic_meeting already used
                        # this exact floor; craft.py simply never applied it.
MAX_RULES = 8           # keep craft.md small enough to sit in every prompt

# The knobs worth scoring, and how to phrase each direction in plain English.
# (field, low label, high label)
NUMERIC = [
    ("cuts_per_min", "fewer, longer takes", "faster cutting"),
    ("punch_count", "fewer zoom punches", "more zoom punches"),
    ("sfx_count", "fewer sound effects", "more sound effects"),
    ("hook_words", "a shorter on-screen hook", "a longer on-screen hook"),
    ("caption_wpp", "fewer words per caption page", "more words per caption page"),
    ("speaker_switches", "staying on one face", "cutting between faces"),
    ("teaser_dur", "no/short cold-open teaser", "a longer cold-open teaser"),
]
CATEGORICAL = [("reframe", "reframe mode"), ("music_mood", "music mood")]

# DURATION IS DELIBERATELY NOT SCORED HERE.
# percent-watched is mechanically a function of length: finishing an 18s clip
# takes less commitment than finishing a 45s one. Correlating retention against
# duration therefore measures arithmetic, not craft, and the loop proved it by
# flipping its own conclusion twice on n<25 as the clip mix changed:
#   "shorter wins by 39"  (inflated: zero-view clips were voting)
#   "shorter wins by 18"  (after the view floor)
#   "longer wins by 27"   (once money clips replaced football)
# Worse, it then contradicted finder.clip_max_seconds and the selection brief,
# and every agent received BOTH. Length is an editorial decision made against
# the brief; this loop scores the choices where retention is a fair measure.


def _mean(xs) -> float:
    return sum(xs) / len(xs) if xs else 0.0


def _median(xs) -> float:
    s = sorted(xs)
    n = len(s)
    if not n:
        return 0.0
    return s[n // 2] if n % 2 else (s[n // 2 - 1] + s[n // 2]) / 2


def _numeric_finding(rows: list[dict], field: str, lo_lbl: str, hi_lbl: str):
    """Median-split one knob and compare mean retention on each side."""
    pts = [(r[field], r["retention"]) for r in rows
           if isinstance(r.get(field), (int, float))
           and isinstance(r.get("retention"), (int, float))]
    if len(pts) < MIN_CLIPS:
        return None

    med = _median([p[0] for p in pts])
    lo = [ret for val, ret in pts if val <= med]
    hi = [ret for val, ret in pts if val > med]
    if len(lo) < MIN_SIDE or len(hi) < MIN_SIDE:
        return None            # knob barely varies; nothing to compare

    delta = _mean(hi) - _mean(lo)
    if abs(delta) < MIN_EFFECT:
        return None

    better, worse = (hi_lbl, lo_lbl) if delta > 0 else (lo_lbl, hi_lbl)
    side = hi if delta > 0 else lo
    return {"field": field, "effect": abs(delta), "n": len(pts),
            "text": (f"**{better}** beats {worse} by "
                     f"{abs(delta):.0f} retention points "
                     f"({field} {'above' if delta > 0 else 'at or below'} "
                     f"{med:g}, n={len(side)} of {len(pts)})")}


def _categorical_finding(rows: list[dict], field: str, label: str):
    """Compare mean retention across the values of a categorical choice."""
    groups: dict[str, list[float]] = {}
    for r in rows:
        v, ret = r.get(field), r.get("retention")
        if v and isinstance(ret, (int, float)):
            groups.setdefault(str(v), []).append(ret)

    usable = {k: v for k, v in groups.items() if len(v) >= MIN_SIDE}
    if len(usable) < 2:
        return None

    ranked = sorted(usable.items(), key=lambda kv: _mean(kv[1]), reverse=True)
    (best, bvals), (worst, wvals) = ranked[0], ranked[-1]
    delta = _mean(bvals) - _mean(wvals)
    if delta < MIN_EFFECT:
        return None
    return {"field": field, "effect": delta, "n": sum(len(v) for v in usable.values()),
            "text": (f"{label} **{best}** beats **{worst}** by {delta:.0f} "
                     f"retention points (n={len(bvals)} vs {len(wvals)})")}


def _exemplars(rows: list[dict], n: int = 3) -> list[dict]:
    """Our own best-held clips. A concrete example teaches craft better than
    a coefficient, so the prompt carries both."""
    scored = [r for r in rows if isinstance(r.get("retention"), (int, float))]
    return sorted(scored, key=lambda r: r["retention"], reverse=True)[:n]


def _defects(rows: list[dict]) -> list[tuple[str, int]]:
    """Recurring QA flags from the finishing editor, most common first. These
    are the mistakes we keep making, which is the fastest quality win available."""
    tally: dict[str, int] = {}
    for r in rows:
        for kind in r.get("qa_flags") or []:
            tally[str(kind)] = tally.get(str(kind), 0) + 1
    return sorted(tally.items(), key=lambda kv: kv[1], reverse=True)


def analyse(niche: str = "") -> dict:
    """Score every craft knob against measured retention."""
    rows = [r for r in db.specs_with_metrics(niche)
            if int(r.get("views") or 0) >= MIN_VIEWS]
    # Not enough inside the current niche yet? Learn from everything rather than
    # nothing, but say so, since craft transfers across niches better than topic
    # choice does.
    scope = niche or "all"
    if niche and len(rows) < MIN_CLIPS:
        allrows = [r for r in db.specs_with_metrics()
                   if int(r.get("views") or 0) >= MIN_VIEWS]
        if len(allrows) > len(rows):
            rows, scope = allrows, f"all niches (too few {niche} clips yet)"

    findings = []
    for field, lo, hi in NUMERIC:
        f = _numeric_finding(rows, field, lo, hi)
        if f:
            findings.append(f)
    for field, label in CATEGORICAL:
        f = _categorical_finding(rows, field, label)
        if f:
            findings.append(f)

    findings.sort(key=lambda f: f["effect"], reverse=True)
    return {"n": len(rows), "scope": scope, "findings": findings[:MAX_RULES],
            "exemplars": _exemplars(rows), "defects": _defects(rows)}


def render(report: dict) -> str:
    """Format the analysis as the markdown block every editor prompt loads."""
    out = ["# Craft learnings (measured on OUR OWN clips)",
           f"_Auto-generated {datetime.now():%Y-%m-%d} from {report['n']} "
           f"clips with retention data, scope: {report['scope']}._", ""]

    if report["n"] < MIN_CLIPS:
        out += [f"**Still measuring.** Only {report['n']} clips have both an "
                f"edit spec and retention data; {MIN_CLIPS} are needed before "
                "any craft rule is trustworthy. Keep applying the skill files "
                "and the standing formula, and do NOT invent rules from the "
                "handful of clips so far.", ""]
    elif report["findings"]:
        out += ["## What actually holds viewers (ranked by effect size)"]
        out += [f"{i}. {f['text']}" for i, f in enumerate(report["findings"], 1)]
        out += ["", "Apply these over generic best practice: they are measured "
                "on this channel, this audience. Where a rule contradicts a "
                "skill file, the measurement wins.", ""]
    else:
        out += ["## No knob has separated yet", "",
                f"{report['n']} clips measured and no editing choice moved "
                f"retention by {MIN_EFFECT:.0f}+ points. That means the edit is "
                "not currently the bottleneck: the variation is coming from "
                "clip SELECTION and hooks, not from cut rate or effects. Do "
                "not fiddle with the edit grammar to chase this.", ""]

    if report["exemplars"]:
        out += ["## Our best-held clips (copy what these did)"]
        for r in report["exemplars"]:
            bits = [f"{k}={r[k]}" for k in
                    ("duration", "cuts_per_min", "punch_count", "sfx_count", "reframe")
                    if r.get(k) is not None]
            out += [f"- **{r['retention']:.0f}% held** ({r['views']:,} views) "
                    f"\"{(r.get('title') or '')[:60]}\" - {', '.join(bits)}"]
        out += [""]

    if report["defects"]:
        out += ["## Recurring QA defects (fix these first, they are free wins)"]
        out += [f"- {kind}: flagged on {n} clips" for kind, n in report["defects"][:6]]
        out += [""]

    return "\n".join(out)


def update(niche: str = "") -> str:
    """Re-run the analysis and write craft.md. Returns the report text."""
    niche = niche or (cfg.get("finder.niche_lock") or "")
    text = render(analyse(niche))
    (ROOT / cfg.get("craft.file", "craft.md")).write_text(text, encoding="utf-8")
    return text
