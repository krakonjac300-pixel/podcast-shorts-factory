"""Agent 1 — FINDER.

Downloads a podcast, transcribes it, and asks Claude to pick the most
clip-worthy moments. Results are stored as 'candidate' clips for review.
"""
from __future__ import annotations

from rich.console import Console

from .. import db, insights, llm, skills
from ..config import cfg
from ..utils import media

console = Console()


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
    return n


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

    clips: list[dict] = []
    for pi, piece in enumerate(pieces):
        span = ""
        if len(pieces) > 1:
            span = (f"\n(This is part {pi + 1}/{len(pieces)} of the episode, "
                    f"covering {piece[0]['start']:.0f}s–{piece[-1]['end']:.0f}s.)")
        prompt = f"""You are a viral short-form video editor. The podcast is titled "{title}".{span}

{skill_block}
Selection brief:
{f.get('selection_brief', '')}

Constraints:
- Return at most {per_chunk} clips.
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
        try:
            result = llm.call_tool("finder", prompt, "submit_clips", schema,
                                   max_tokens=4000)
            got = (result or {}).get("clips", [])
            clips.extend(got)
            if len(pieces) > 1:
                console.print(f"  [dim]chunk {pi + 1}/{len(pieces)}: "
                              f"{len(got)} candidates[/]")
        except Exception as ex:  # noqa: BLE001 - a failed chunk shouldn't kill the run
            console.print(f"  [yellow]chunk {pi + 1} failed: {ex}[/]")

    clips.sort(key=lambda c: -float(c.get("score", 0)))
    return clips[:max_cand]
