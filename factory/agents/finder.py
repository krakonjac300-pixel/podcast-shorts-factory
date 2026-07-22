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
    info: dict = {}
    transcript = media.transcribe(audio, info_out=info)
    console.print(f"  ↳ {len(transcript)} segments"
                  + (f"  [dim](lang={info.get('language')} "
                     f"{info.get('language_probability', 0):.0%})[/]"
                     if info.get("language") else ""))

    # GATE 1 — LANGUAGE. The cheapest, hardest signal that we downloaded the
    # wrong video entirely. On 2026-07-19 a mislabelled source fed us a Hindi
    # vlog; the topic gates below never fired because they only knew how to
    # reject the wrong SPORT, and clips reached the queue. Language is not a
    # matter of taste, so it is checked first and aborts the whole source.
    if not _language_ok(info, title):
        # Record it FIRST. processed_urls() reads the sources table, so returning
        # before the upsert meant the same wrong-language video was picked again
        # the next day: another full download and Whisper pass, every day, until
        # the channel happened to publish something newer. pick_next also returns
        # on the first source, so one bad video could block the whole list.
        db.upsert_source(url, title, video, transcript, channel=channel)
        return 0

    source_id = db.upsert_source(url, title, video, transcript, channel=channel)

    console.print("[bold cyan]FINDER[/] asking Claude to score moments…")
    candidates = _score_with_claude(title, transcript)

    n = 0
    flat = [w for seg in transcript for w in seg["words"]]
    max_len = float(cfg.get("finder.clip_max_seconds", 42))
    for c in candidates:
        c = _snap_to_sentence(c, flat, max_len)
        c = _thought_complete(c, flat, max_len)
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


# While niche_lock is set, mechanically DROP candidates that clearly belong to a
# DIFFERENT topic — the Finder kept clipping the boxing guest (Whittaker: 2-9
# views) despite being told to stay in lane. Coaching wasn't enough; this is the
# hard gate. Each niche has an OFF list (other-topic red flags) + a KEEP lexicon
# (this-niche terms); a clip is dropped only if it hits OFF and misses KEEP.
_OFF_NICHE = {
    "football": re.compile(
        r"\b(boxing|boxer|heavyweight|cruiserweight|welterweight|flyweight|"
        r"knockout|\bko\b|ufc|mma|octagon|sparring|ringwalk|ring walk|"
        r"title fight|undisputed|prizefight|jab|southpaw)\b", re.I),
    # money channel: drop clips that are really about sport/football or pure
    # health/relationships with no money angle (DOAC etc. mix topics)
    "money": re.compile(
        r"\b(football|soccer|penalty|midfield|boxing|ufc|world cup|premier "
        r"league|goalkeeper|striker|workout|reps|calories|protein)\b", re.I),
}
_NICHE_KEEP = {
    "football": re.compile(
        r"\b(football|soccer|goal|keeper|striker|midfield|defender|winger|"
        r"penalty|premier league|world cup|england|pitch|manager|transfer|squad|"
        r"dressing room|gaffer|nations league|champions league|var|offside|"
        r"clean sheet)\b", re.I),
    "money": re.compile(
        r"\b(money|cash|dollar|debt|income|salary|invest|investing|business|"
        r"entrepreneur|millionaire|billionaire|rich|wealth|wealthy|broke|budget|"
        r"savings?|credit|loan|mortgage|profit|revenue|startup|finance|financial|"
        r"bank|retire|retirement|portfolio|stocks?|crypto|side hustle|net worth|"
        r"bankrupt|paycheck|expenses?|afford|price|cost|\$|percent|interest)\b",
        re.I),
}


def _language_ok(info: dict, title: str) -> bool:
    """False if the episode is not in the language this channel publishes in.

    Aborts the SOURCE, not just the clip: if the language is wrong, every
    moment in it is wrong, and continuing only burns an hour of transcription
    and rendering to produce something unpostable.
    """
    want = (cfg.get("finder.expect_language", "en") or "").strip().lower()
    got = (info.get("language") or "").strip().lower()
    if not want or not got:
        return True                      # nothing to compare — don't block
    if got == want:
        return True
    # Only act when Whisper is actually sure; a noisy intro can produce a
    # low-confidence guess we should not trust enough to throw an episode away.
    conf = float(info.get("language_probability", 0.0) or 0.0)
    if conf < float(cfg.get("finder.language_min_conf", 0.6)):
        console.print(f"  [yellow]language looks like '{got}' but only "
                      f"{conf:.0%} confident — continuing[/]")
        return True
    console.print(f"  [red]LANGUAGE GATE: '{got}' ({conf:.0%}) is not "
                  f"'{want}' — dropping this source entirely[/]")
    notify.notify(
        "Wrong-language source dropped",
        f"'{title[:60]}' transcribed as '{got}' ({conf:.0%}), not '{want}'. "
        "No clips were made. Check scheduler.sources for a wrong channel URL.")
    return False


def _niche_ok(clip: dict) -> bool:
    """False if `clip` does not positively belong to the locked niche.

    This used to be a BLOCKLIST: drop only if the text hit a known off-topic
    pattern (boxing on a football channel). That silently allowed anything it
    had never heard of, which is how a Hindi shopping vlog produced four clips
    on 2026-07-19 — it matched no football terms AND no boxing terms, so it
    read as "not known to be bad" and passed.

    It is now an ALLOWLIST: while a lock is active a clip must show positive
    evidence of the niche. Unrecognised content is rejected, not admitted. That
    is the right default for an unattended pipeline, where the cost of dropping
    a good clip is one empty slot and the cost of admitting a bad one is a
    published video that does not belong on the channel.
    """
    lock = cfg.get("finder.niche_lock")
    if not lock:
        return True
    off, keep = _OFF_NICHE.get(lock), _NICHE_KEEP.get(lock)
    if not keep:
        return True                      # no lexicon defined for this niche
    text = f"{clip.get('title', '')} {clip.get('reason', '')} " \
           f"{clip.get('caption', '')}"
    if off is not None and off.search(text) and not keep.search(text):
        console.print(f"  [yellow]niche-lock: dropped off-{lock} clip "
                      f"'{str(clip.get('title', ''))[:40]}'[/]")
        return False
    if not keep.search(text):
        console.print(f"  [yellow]niche-lock: dropped clip with no {lock} "
                      f"signal '{str(clip.get('title', ''))[:40]}'[/]")
        return False
    return True


def _sentence_end(w: dict) -> bool:
    return w["word"].rstrip()[-1:] in ".?!"


def _snap_to_sentence(c: dict, words: list[dict], max_len: float) -> dict:
    """Land both cut points on finished thoughts.

    Recent clips opened and closed mid-sentence ("encourage chicken account"
    opened one; "I hope when um" closed another). The transcript already knows
    where sentences end; use it. The end may extend up to 8s to let the
    speaker finish; the start walks back up to 4s to the start of its
    sentence, but only when a real sentence boundary is found there.
    """
    s0, e0 = float(c["start"]), float(c["end"])
    inside = [w for w in words if w["end"] > s0 and w["start"] < e0]
    if not inside:
        return c
    # END: never cut a thought in half
    if not _sentence_end(inside[-1]):
        for w in words:
            if w["start"] < e0 - 0.05:
                continue
            if w["end"] - s0 > max_len + 8:
                break
            e0 = w["end"] + 0.15
            if _sentence_end(w):
                break
    # START: if we open mid-sentence, walk back to where the sentence begins
    head = [w for w in words if w["start"] < s0 + 0.05]
    if head and not _sentence_end(head[-1]):
        k = len(head) - 1
        while k > 0 and not _sentence_end(head[k - 1]) \
                and s0 - head[k - 1]["start"] <= 4.0:
            k -= 1
        if k > 0 and _sentence_end(head[k - 1]) and e0 - head[k]["start"] <= max_len + 8:
            s0 = max(0.0, head[k]["start"] - 0.1)
    c["start"], c["end"] = round(s0, 2), round(e0, 2)
    return c


THOUGHT_SCHEMA = {
    "type": "object",
    "properties": {
        "complete": {"type": "boolean",
                     "description": "true only if the clip ENDS after the "
                                    "speaker finishes the thought"},
        "better_end": {"type": "number",
                       "description": "if not complete: the absolute time (s) "
                                      "where the thought actually finishes"},
    },
    "required": ["complete"],
}


def _thought_complete(c: dict, words: list[dict], max_len: float) -> dict:
    """Ask the model to QUESTION ITSELF: is the thing being talked about over
    before the cut? Punctuation snapping catches sentences; this catches the
    thought that spans several sentences (a story whose punchline is one line
    later). Skipped silently on any model failure."""
    if not cfg.get("finder.verify_complete", True) or not llm.available():
        return c
    s0, e0 = float(c["start"]), float(c["end"])
    ctx = " ".join(
        f"[{w['start']:.1f}] {w['word'].strip()}"
        for w in words if s0 - 4 <= w["start"] <= e0 + 12)
    prompt = (
        "A short clip is being cut from a podcast. It runs from "
        f"{s0:.1f}s to {e0:.1f}s. Transcript with timestamps (the clip's end "
        f"is at {e0:.1f}):\n{ctx[:2600]}\n\n"
        "Is the thing being talked about actually FINISHED at the cut point, "
        "or does the thought complete shortly after? A clip must never end "
        "mid-story or before the punchline. Call submit_check.")
    try:
        r = llm.call_tool("finder", prompt, "submit_check", THOUGHT_SCHEMA,
                          max_tokens=300)
    except Exception:  # noqa: BLE001
        return c
    if r and not r.get("complete"):
        try:
            be = float(r.get("better_end") or 0)
        except (TypeError, ValueError):
            return c
        if e0 < be <= e0 + 12 and be - s0 <= max_len + 10:
            console.print(f"  [dim]finder self-check: thought unfinished, "
                          f"end {e0:.1f}s -> {be:.1f}s[/]")
            c["end"] = round(be, 2)
    return c


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
