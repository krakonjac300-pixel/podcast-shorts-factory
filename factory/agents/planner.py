"""Editor's creative planner.

Given a clip's transcript and the editor's installed skills (hooks, storytelling,
sound design, visual effects, pacing), Claude returns a concrete edit plan:
an improved on-screen hook, the key words to emphasize in captions, and timed
SFX / b-roll / transition / music suggestions.

Degrades gracefully: if there's no API key or the call fails, a safe default
plan is returned so rendering still proceeds.
"""
from __future__ import annotations

from .. import insights, llm, skills
from ..config import cfg

PLAN_TOOL = {
    "name": "submit_edit_plan",
    "description": "Return the creative edit plan for one short clip.",
    "input_schema": {
        "type": "object",
        "properties": {
            "hook_text": {"type": "string",
                          "description": "punchy 2-5 word on-screen hook for the first ~2s"},
            "cover_text": {"type": "string",
                           "description": "3-5 HUGE words for the thumbnail/cover; complements (not duplicates) the hook"},
            "music_mood": {"type": "string",
                           "description": "e.g. tense, upbeat, lofi, none — matches the emotion"},
            "emphasis_words": {
                "type": "array", "items": {"type": "string"},
                "description": "the single most important word from each key line, to enlarge in captions",
            },
            "sfx_cues": {
                "type": "array",
                "items": {"type": "object", "properties": {
                    "anchor": {"type": "string",
                               "description": "the EXACT word or 2-3 word phrase from "
                               "the transcript this sound must land ON — copy it "
                               "verbatim. We sync the sound to the moment those words "
                               "are actually spoken, so pick a word where the sound "
                               "REINFORCES the meaning (impact on a shocking/violent "
                               "word, ding on a positive reveal or number, riser just "
                               "before a big statement, whoosh only at a real topic "
                               "change). No anchor = the sound is dropped."},
                    "type": {"type": "string",
                             "enum": ["whoosh", "swoosh", "riser", "impact", "ding", "pop"],
                             "description": "one of the available pack sounds: "
                             "whoosh/swoosh (transition), riser (build tension), "
                             "impact (emphasis hit), ding (positive/reveal), pop (quick accent)"},
                    "note": {"type": "string"}},
                    "required": ["anchor", "type"]},
            },
            "broll": {
                "type": "array",
                "items": {"type": "object", "properties": {
                    "time": {"type": "number"},
                    "suggestion": {"type": "string",
                                   "description": "1-3 CONCRETE stock-photo nouns "
                                   "(e.g. 'doctor hospital', 'gym weights', "
                                   "'money cash') — never abstract phrases"}},
                    "required": ["time", "suggestion"]},
            },
            "transitions": {
                "type": "array",
                "items": {"type": "object", "properties": {
                    "time": {"type": "number"}, "type": {"type": "string"}},
                    "required": ["time", "type"]},
            },
            "narrator_intro": {
                "type": "string",
                "description": "OPTIONAL narrator cold-open line, ≤12 words, spoken "
                               "by an AI voice over a teaser before the clip starts "
                               "(e.g. 'Wait for what he says about your kidneys'). "
                               "Empty string = no narrator.",
            },
            "teaser_times": {
                "type": "array", "items": {"type": "number"}, "maxItems": 2,
                "description": "1-2 timestamps of the single most jaw-dropping "
                               "MOMENTS (the payoff) to flash as a 2s preview "
                               "before the clip — the #1 retention trick. Only "
                               "with narrator_intro.",
            },
            "memes": {
                "type": "array",
                "items": {"type": "object", "properties": {
                    "time": {"type": "number"},
                    "emotion": {"type": "string",
                                "description": "reaction type: mind-blown|laughing|"
                                               "shocked|facepalm|money|crying|clapping"}},
                    "required": ["time", "emotion"]},
                "description": "0-2 full-width meme/reaction inserts at emotional "
                               "peaks (burst-sequence style). Less is more.",
            },
        },
        "required": ["hook_text", "music_mood", "emphasis_words"],
    },
}


def _short_hook(title: str, max_words: int = 6) -> str:
    """A punchy on-screen hook when the AI planner is unavailable: the first few
    words of the title (drop a trailing dash/em-dash clause) instead of the whole
    long title, which would overflow the frame."""
    head = title.split("—")[0].split(" - ")[0].strip() or title
    words = head.split()
    return " ".join(words[:max_words]) if len(words) > max_words else head


def _default_plan(clip) -> dict:
    return {"hook_text": _short_hook(clip["title"]), "cover_text": clip["title"],
            "music_mood": "none", "emphasis_words": [],
            "sfx_cues": [], "broll": [], "transitions": [],
            "narrator_intro": "", "teaser_times": [], "memes": []}


def plan_clip(clip, words: list[dict]) -> dict:
    """Return an edit plan dict. Never raises — falls back to a default."""
    if not cfg.get("editor.ai_plan", True) or not llm.available():
        return _default_plan(clip)

    # transcript local to the clip
    seg = [w for w in words if w["end"] > clip["start"] and w["start"] < clip["end"]]
    transcript = " ".join(w["word"] for w in seg).strip()
    dur = clip["end"] - clip["start"]
    skill_block = skills.load(cfg.get("skills.editor", []))

    try:                                    # Manager bounce notes (review loop)
        review_notes = clip["review_notes"]
    except (KeyError, IndexError):
        review_notes = None
    bounce = (f"\nIMPORTANT — the channel Manager REJECTED the previous edit of "
              f"this clip. You MUST address these notes:\n{review_notes}\n"
              if review_notes else "")

    prompt = f"""You are a world-class short-form video editor finishing a {dur:.0f}s vertical clip.
{bounce}

{skill_block}
Clip working title: {clip['title']}
Clip transcript (times are seconds from the START of the clip; the clip is {dur:.0f}s long):
{transcript}

What's working on our channel so far (learn from this):
{insights.learnings()}

Design the edit. Apply the skills above. Times must be within 0–{dur:.0f}s.
SFX: at most 3, and every one MUST 'anchor' to an exact word from the transcript
where the sound REINFORCES the meaning (impact on a shocking word, ding on a
positive reveal). A sound where nothing happens is worse than no sound — when in
doubt, leave it out. Transitions only at real topic shifts, and pick the ONE key
word per important line for emphasis. Call submit_edit_plan."""

    try:
        result = llm.call_tool("editor", prompt, "submit_edit_plan",
                               PLAN_TOOL["input_schema"], max_tokens=1500)
        if result:
            plan = _default_plan(clip)
            plan.update(result)
            return plan
    except Exception:  # noqa: BLE001 - never block rendering on the planner
        pass
    return _default_plan(clip)


def render_notes(clip, plan: dict) -> str:
    """Human-readable creative notes for the finishing pass / scheduler sidecar."""
    lines = [f"# Edit notes — clip {clip['id']}: {clip['title']}",
             f"\n**On-screen hook:** {plan.get('hook_text')}",
             f"**Music mood:** {plan.get('music_mood')}",
             f"**Emphasis words:** {', '.join(plan.get('emphasis_words', [])) or '—'}"]
    if plan.get("sfx_cues"):
        lines.append("\n## SFX cues")
        for c in plan["sfx_cues"]:
            where = c.get("anchor") or (f"{c['time']:.1f}s" if "time" in c else "?")
            lines.append(f"- {c.get('type', '')} on “{where}”"
                         + (f" ({c.get('note')})" if c.get("note") else ""))
    if plan.get("broll"):
        lines.append("\n## B-roll suggestions")
        for b in plan["broll"]:
            lines.append(f"- {b['time']:.1f}s — {b['suggestion']}")
    if plan.get("transitions"):
        lines.append("\n## Transitions")
        for t in plan["transitions"]:
            lines.append(f"- {t['time']:.1f}s — {t['type']}")
    return "\n".join(lines) + "\n"
