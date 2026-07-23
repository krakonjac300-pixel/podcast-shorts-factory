"""Editor's creative planner.

Given a clip's transcript and the editor's installed skills (hooks, storytelling,
sound design, visual effects, pacing), Claude returns a concrete edit plan:
an improved on-screen hook, the key words to emphasize in captions, and timed
SFX / b-roll / transition / music suggestions.

Degrades gracefully: if there's no API key or the call fails, a safe default
plan is returned so rendering still proceeds.
"""
from __future__ import annotations

from rich.console import Console

from .. import insights, llm, skills
from ..config import cfg

console = Console()

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
            "comment_question": {
                "type": "string",
                "description": "REQUIRED. A binary/forced-choice question about THIS "
                               "clip, ≤6 words, burned on screen at the end to drive "
                               "replies (comments are a top ranking signal and our "
                               "weakest metric). e.g. 'Keane or Neville?', 'Overrated "
                               "or elite?', 'Would you pay it?'. No hashtags.",
            },
            "moment_type": {
                "type": "string",
                "enum": ["ADVICE", "CONFESSION", "CONFLICT", "REVEAL", "WISDOM"],
                "description": "REQUIRED. What KIND of moment this is; it "
                               "routes the whole edit. ADVICE = expert "
                               "explaining or teaching. CONFESSION = someone "
                               "admitting their own situation. CONFLICT = "
                               "confrontation between people. REVEAL = a "
                               "number or fact being dramatically disclosed. "
                               "WISDOM = a quotable aphorism that needs no "
                               "editing energy.",
            },
            "payoff_anchor": {
                "type": "string",
                "description": "For CONFESSION/CONFLICT/REVEAL: the EXACT "
                               "words from the transcript where the payoff "
                               "lands (the number, the admission, the snap). "
                               "The silence AROUND these words is protected "
                               "from trimming, because the pause is the "
                               "product. Empty for ADVICE/WISDOM.",
            },
            "action_captions": {
                "type": "array", "maxItems": 2,
                "items": {"type": "object", "properties": {
                    "anchor": {"type": "string",
                               "description": "the EXACT transcript words "
                               "spoken right BEFORE the physical action"},
                    "text": {"type": "string",
                             "description": "the action, 2-4 words, e.g. "
                                            "'SLAMS THE TABLE', 'WALKS OFF', "
                                            "'RIPS THE STATEMENT'"}},
                    "required": ["anchor", "text"]},
                "description": "0-2 PHYSICAL actions that happen without "
                               "speech (from the 3M-view reference edit: "
                               "*MOVES MIC* *WALKS AWAY* captioned in red keep "
                               "the caption rhythm alive when nobody talks). "
                               "ONLY actions clearly implied by the "
                               "transcript; when unsure, none.",
            },
            "takeaway": {
                "type": "string",
                "description": "REQUIRED. The ONE thing a viewer LEARNS from "
                               "this clip, as ≤8 words they can act on, burned "
                               "on screen at the teaching beat. Must be "
                               "specific and true: 'Minimum payments barely "
                               "touch a 27% APR' or 'Gap insurance covers what "
                               "the payout won't'. NOT vague filler like "
                               "'Budget better' or 'Money matters'. Empty "
                               "string ONLY if the clip genuinely teaches "
                               "nothing.",
            },
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
                             "enum": ["whoosh", "swoosh", "riser", "impact",
                                      "ding", "pop", "cash", "coin"],
                             "description": "one of the available pack sounds: "
                             "whoosh/swoosh (transition), riser (build tension), "
                             "impact (deep boom on a bombshell), ding (positive/"
                             "reveal), pop (quick accent), cash (register — a "
                             "money amount is REVEALED), coin (small money beat)"},
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
            "scene_map": {
                "type": "array",
                "items": {"type": "object", "properties": {
                    "start": {"type": "number"},
                    "end": {"type": "number"},
                    "role": {"type": "string", "enum": ["visual", "breather"],
                             "description": "visual = this line is crucial, it gets "
                             "a designed scene; breather = pure subtitles so the "
                             "viewer rests and reconnects with the speaker (NOTHING "
                             "else may appear on screen during a breather)"},
                    "design_brief": {"type": "string",
                                     "description": "visual segments only: a CONCRETE "
                                     "image that literally depicts the line (e.g. "
                                     "'person stepping out of a line of identical "
                                     "grey figures'). Describe the IMAGE only — no "
                                     "text/words in it. Never abstract concepts."},
                    "overlay_text": {"type": "string",
                                     "description": "visual segments only: 1-4 words "
                                     "taken from the line itself, burned crisply over "
                                     "the design (e.g. 'EXTRAORDINARY')."}},
                    "required": ["start", "end", "role"]},
                "description": "line-by-line edit map covering the WHOLE clip: "
                               "hook = breather (viewer locks onto the speaker), "
                               "then alternate visual scenes on the value lines "
                               "with breathers between them; a list in the script "
                               "is ONE visual showing all items; the final payoff "
                               "line gets a visual with the lesson written out.",
            },
        },
        "required": ["hook_text", "music_mood", "emphasis_words",
                     "comment_question", "takeaway", "moment_type"],
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
    return {"_default": True,    # marks a no-AI plan so degraded renders log it
            "hook_text": _short_hook(clip["title"]), "cover_text": clip["title"],
            "music_mood": "none", "emphasis_words": [],
            "sfx_cues": [], "broll": [], "transitions": [],
            "comment_question": "AGREE?", "takeaway": "",
            "moment_type": "ADVICE", "payoff_anchor": "",
            "action_captions": [],
            "narrator_intro": "", "teaser_times": [], "memes": [],
            "scene_map": []}


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

CRAFT RULES MEASURED ON OUR OWN EDITS (these outrank generic best practice --
they are computed from the retention our real clips earned, so where they
contradict a skill file above, FOLLOW THE MEASUREMENT):
{insights.craft()}

Design the edit. Apply the skills above. Times must be within 0–{dur:.0f}s.
moment_type: classify FIRST, because it routes the whole edit. A study of 50
viral money clips found two OPPOSITE editing philosophies that both work, but
only when matched to content: ADVICE clips want compression (cut every pause),
while CONFESSION/CONFLICT/REVEAL clips want tension (the hesitation before the
number IS the product, and cutting it is the most common clipper mistake).
WISDOM clips want almost no editing at all: the words are treated as scripture.
For the tension types you MUST also give payoff_anchor, the exact transcript
words where the payoff lands, so the edit protects the silence around them.
takeaway: the channel's promise is that a viewer LEARNS something, so name the
single usable lesson in this clip and keep it concrete and TRUE. Drama earns the
watch; the lesson earns the follow. If the moment teaches nothing, say so with an
empty string rather than inventing filler.
hook_text: include the SPEAKER'S NAME when it fits (names stop the scroll — "KEANE:
SACK THEM" beats "SACK THEM"). The first frame must show a name + a stake.
SFX: at most 3, and every one MUST 'anchor' to an exact word from the transcript
where the sound REINFORCES the meaning (impact on a shocking word, ding on a
positive reveal). A sound where nothing happens is worse than no sound — when in
doubt, leave it out. Transitions only at real topic shifts, and pick the ONE key
word per important line for emphasis. Loop rate is a ranking signal: if the clip's
last line relates to its first, note it in the hook design so the ending flows
straight back into the opening.
scene_map: go through the transcript LINE BY LINE asking one question — is this
line crucial for the viewer to understand? Crucial → 'visual' segment with a
concrete design_brief; everything else → 'breather' (pure subtitles, the viewer
rests on the speaker). The hook is always a breather. 2-4 visual segments per
clip, never back-to-back without a breather between clusters. Call submit_edit_plan."""

    try:
        result = llm.call_tool("editor", prompt, "submit_edit_plan",
                               PLAN_TOOL["input_schema"], max_tokens=2000)
        if result:
            plan = _default_plan(clip)
            plan.update(result)
            plan.pop("_default", None)
            return plan
        console.print("[yellow]planner: no AI plan returned - using the "
                      "default (compression route, no moment typing)[/]")
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
    if plan.get("scene_map"):
        lines.append("\n## Scene map (visual = designed scene, breather = pure subtitles)")
        for s in plan["scene_map"]:
            what = s.get("design_brief", "") if s.get("role") == "visual" else "rest on speaker"
            lines.append(f"- {s.get('start', 0):.1f}–{s.get('end', 0):.1f}s "
                         f"[{s.get('role', '?')}] {what}")
    return "\n".join(lines) + "\n"
