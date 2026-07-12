"""Agent 1 — FINDER.

Downloads a podcast, transcribes it, and asks Claude to pick the most
clip-worthy moments. Results are stored as 'candidate' clips for review.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field

from rich.console import Console

from .. import db, insights, llm, notify, skills
from ..config import cfg
from ..utils import media

console = Console()


@dataclass
class Candidate:
    """A scored clip-worthy moment returned by the headless find_candidates()
    API; the interactive flow stores the same fields as DB rows instead."""
    start_s: float
    end_s: float
    hook: str
    score: float
    reasoning: str
    caption: str = ""
    hashtags: list = field(default_factory=list)


def find_candidates(source_url: str, max_clips: int = 5,
                    niche: str | None = None) -> list[Candidate]:
    """Stateless clip discovery for headless/programmatic callers.

    Same engine as `find()` — download, transcribe, AI-score — but returns
    ranked Candidate objects instead of writing DB rows or asking a human to
    review. The caller decides which clips to accept.
    """
    video, title, channel = media.download(source_url)
    audio = media.extract_audio(video)
    transcript = media.transcribe(audio)
    if niche:
        # bias selection toward the buyer's niche without touching config
        title = f"{title}  [focus niche: {niche}]"
    scored = _score_with_claude(title, transcript)[: max(1, int(max_clips))]
    return [
        Candidate(
            start_s=float(c["start"]),
            end_s=float(c["end"]),
            hook=c.get("title", ""),
            score=float(c.get("score", 0)),
            reasoning=c.get("reason", ""),
            caption=c.get("caption", ""),
            hashtags=list(c.get("hashtags", []) or []),
        )
        for c in scored
    ]


def _transcript_for_prompt(transcript: list[dict]) -> str:
    """Compact, timestamped transcript so Claude can reference real times."""
    lines = []
    for seg in transcript:
        lines.append(f"[{seg['start']:.1f}-{seg['end']:.1f}] {seg['text']}")
    return "\n".join(lines)


# Keep each scoring prompt comfortably inside the model's context. A 3-hour
# podcast transcribes to ~4,000+ segments — one giant prompt silently fails,
# so long episodes are scored in chunks and the best moments merged.
_CHUNK_CHARS = 60_000


def _chunks(transcript: list[dict]) -> list[list[dict]]:
    """Split the transcript into pieces whose prompt text stays under budget."""
    out, cur, size = [], [], 0
    for seg in transcript:
        line = len(seg.get("text", "")) + 16
        if cur and size + line > _CHUNK_CHARS:
            out.append(cur)
            cur, size = [], 0
        cur.append(seg)
        size += line
    if cur:
        out.append(cur)
    return out


def find(url: str) -> int:
    """Run the finder for one podcast URL. Returns number of candidates."""
    console.print(f"[bold cyan]FINDER[/] downloading {url}")
    video, title, channel = media.download(url)
    console.print(f"  ↳ {title}" + (f"  [dim]({channel})[/]" if channel else ""))

    console.print("[bold cyan]FINDER[/] extracting audio + transcribing "
                  f"(whisper={cfg.get('finder.whisper_model')})…")
    audio = media.extract_audio(video)
    transcript = media.transcribe(audio)
    console.print(f"  ↳ {len(transcript)} segments")

    source_id = db.upsert_source(url, title, video, transcript, channel=channel)

    console.print("[bold cyan]FINDER[/] asking Claude to score moments…")
    candidates = _score_with_claude(title, transcript)

    n = 0
    for c in candidates:
        db.add_clip(source_id, c["start"], c["end"], c["title"],
                    c["reason"], c["score"], c["caption"], c.get("hashtags", []))
        n += 1
    console.print(f"[green]✓ {n} candidate clips saved.[/] Run "
                  "[bold]python run.py review[/] to approve.")
    if n == 0:
        # A whole episode yielding nothing (even after the relaxed fallback) means
        # no posts that day — alert now instead of discovering it days later.
        notify.notify("Finder found 0 clips",
                      f"'{title[:70]}' produced no candidates — today's queue may be "
                      "empty. Check the source or the selection brief.")
    return n


# While niche_lock is set, mechanically DROP candidates from another sport — the
# Finder kept clipping the boxing guest (Whittaker: 2-9 views) despite being told
# to stay in the football lane. Coaching wasn't enough; this is the hard gate.
_OFF_NICHE = {
    "football": re.compile(
        r"\b(boxing|boxer|heavyweight|cruiserweight|welterweight|flyweight|"
        r"knockout|\bko\b|ufc|mma|octagon|sparring|ringwalk|ring walk|"
        r"title fight|undisputed|prizefight|jab|southpaw)\b", re.I),
}
_FOOTBALL = re.compile(
    r"\b(football|soccer|goal|keeper|striker|midfield|defender|winger|penalty|"
    r"premier league|world cup|england|pitch|manager|transfer|squad|dressing "
    r"room|gaffer|nations league|champions league|var|offside|clean sheet)\b", re.I)


def _niche_ok(clip: dict) -> bool:
    """False if `clip` is clearly off-niche while niche_lock is active."""
    lock = cfg.get("finder.niche_lock")
    if not lock:
        return True
    off = _OFF_NICHE.get(lock)
    if not off:
        return True
    text = f"{clip.get('title', '')} {clip.get('reason', '')} " \
           f"{clip.get('caption', '')}"
    if off.search(text) and not _FOOTBALL.search(text):
        console.print(f"  [yellow]niche-lock: dropped off-{lock} clip "
                      f"'{str(clip.get('title', ''))[:40]}'[/]")
        return False
    return True


def _score_with_claude(title: str, transcript: list[dict]) -> list[dict]:
    f = cfg.finder
    schema = {
        "type": "object",
        "properties": {
            "clips": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "start": {"type": "number", "description": "start time in seconds"},
                        "end": {"type": "number", "description": "end time in seconds"},
                        "title": {"type": "string", "description": "punchy hook/title"},
                        "reason": {"type": "string", "description": "why this will perform"},
                        "score": {"type": "number", "description": "0-100 virality estimate"},
                        "caption": {"type": "string", "description": "post caption"},
                        "hashtags": {"type": "array", "items": {"type": "string"}},
                    },
                    "required": ["start", "end", "title", "reason", "score", "caption"],
                },
            }
        },
        "required": ["clips"],
    }

    skill_block = skills.load(cfg.get("skills.finder", []))
    pieces = _chunks(transcript)
    max_cand = f.get("max_candidates", 8)
    per_chunk = max_cand if len(pieces) == 1 else max(3, max_cand // len(pieces) + 1)
    if len(pieces) > 1:
        console.print(f"  [dim]long episode → scoring {len(pieces)} chunks[/]")

    # The fallback brief for when the strict pass finds nothing. The free model is
    # flaky and the strict drama-only brief can make it return an EMPTY list on a
    # whole episode (a football-pundit show reads as "merely interesting"). A day
    # with zero clips is the worst outcome, so we loosen up rather than post nothing.
    relaxed_brief = (
        "Pick the 3 most engaging, self-contained moments for a short-form clip — "
        "the bits most likely to stop someone scrolling: a strong opinion, a clash, "
        "a surprising claim, a vivid story, a bold prediction, or a genuinely funny "
        "beat. Favor emotion and stakes, but do NOT return an empty list — always "
        "return your best 3 even if nothing is sensational.")

    def _mk_prompt(pi: int, piece: list[dict], brief: str, insist: bool) -> str:
        span = ""
        if len(pieces) > 1:
            span = (f"\n(This is part {pi + 1}/{len(pieces)} of the episode, "
                    f"covering {piece[0]['start']:.0f}s–{piece[-1]['end']:.0f}s.)")
        must = ("\n- IMPORTANT: return your best picks — do NOT return an empty list."
                if insist else "")
        return f"""You are a viral short-form video editor. The podcast is titled "{title}".{span}

{skill_block}
Selection brief:
{brief}

Constraints:
- Return at most {per_chunk} clips.{must}
- LENGTH IS A CREATIVE DECISION: hard platform bounds are {f.get('clip_min_seconds', 15)}-{f.get('clip_max_seconds', 90)}s,
  but within them the IDEA decides. Reason it out per clip: a tight shocking
  one-liner earns ~20s; a layered story earns 60-80s ONLY if every sentence
  raises the stakes. Cut the moment the idea is delivered — never pad, never
  amputate a payoff. State your length reasoning in the clip's `reason`.
- Use REAL timestamps from the transcript. Snap to natural sentence boundaries.
- AVOID moments where the hosts are watching/reacting to a video or screen
  ("look at this", "watch this", "this video") — screen layouts crop terribly
  to vertical. Pick moments that work as pure conversation.

What's worked before (learn from this):
{insights.learnings()}

What's trending right now (lean into these when a clip fits naturally):
{insights.trends()}

Transcript (timestamped):
{_transcript_for_prompt(piece)}

Call submit_clips with your picks, best first."""

    def _score_pass(brief: str, insist: bool, attempts: int = 2) -> list[dict]:
        out: list[dict] = []
        for pi, piece in enumerate(pieces):
            prompt = _mk_prompt(pi, piece, brief, insist)
            got: list[dict] = []
            for _ in range(attempts):        # the free model intermittently returns
                try:                         # an empty tool call — one miss must not
                    result = llm.call_tool("finder", prompt, "submit_clips",  # zero
                                           schema, max_tokens=4000)           # out
                    got = (result or {}).get("clips", [])
                    if got:
                        break
                except Exception as ex:  # noqa: BLE001 - a failed try shouldn't kill the run
                    console.print(f"  [yellow]chunk {pi + 1} attempt failed: {ex}[/]")
            out.extend(got)
            if len(pieces) > 1:
                console.print(f"  [dim]chunk {pi + 1}/{len(pieces)}: "
                              f"{len(got)} candidates[/]")
        return out

    clips = _score_pass(f.get("selection_brief", ""), insist=False)
    clips = [c for c in clips if _niche_ok(c)]
    if not clips:
        console.print("  [yellow]0 clips on the strict pass — retrying with a "
                      "relaxed brief so the day isn't empty[/]")
        clips = [c for c in _score_pass(relaxed_brief, insist=True) if _niche_ok(c)]

    clips.sort(key=lambda c: -float(c.get("score", 0)))
    return clips[:max_cand]
