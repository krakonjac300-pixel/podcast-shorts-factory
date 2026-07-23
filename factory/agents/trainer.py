"""Agent 7 — TRAINER (the team's coach).

Weekly job, four duties:
1. WATCH the winners: pull the most-viewed recent Shorts in our niche from the
   YouTube API — titles, lengths, view counts — plus their top comments (what
   viewers actually respond to).
2. STUDY the rules & meta: fresh web research on YouTube monetization policy
   and the current editing-style meta.
3. BACKTEST our own team: the Finder's predicted scores vs what really
   happened — find its systematic biases.
4. TEACH, with the Manager's sign-off: distill everything into a lesson and a
   concrete update to ONE skill playbook. The Manager reviews the lesson before
   it lands; every agent reads playbooks each run, so an approved lesson
   retrains the whole team automatically.

Hard guardrail: the Trainer can NEVER touch editorial-standards.md (the MUSTs)
— only the craft playbooks listed in config. Updates live between TRAINER
markers so they're inspectable and reversible.
"""
from __future__ import annotations

from datetime import datetime, timedelta

from rich.console import Console

from .. import llm, notify, skills
from ..config import ROOT, cfg
from . import manager

console = Console()

MARK_START = "<!-- TRAINER:START (auto-updated weekly, Manager-approved) -->"
MARK_END = "<!-- TRAINER:END -->"

DEFAULT_PLAYBOOKS = ["hooks", "pacing", "captions-craft", "video-editing",
                     "titles-thumbnails", "growth-strategy",
                     "youtube-monetization", "thumbnail-design"]

TRAIN_SCHEMA = {
    "type": "object",
    "properties": {
        "report_md": {"type": "string",
                      "description": "training.md content: what's working on top "
                                     "videos and WHY, policy notes, backtest findings"},
        "lesson_summary": {"type": "string",
                           "description": "the ONE lesson of the week, <25 words"},
        "playbook_target": {"type": "string",
                            "description": "which playbook to update (from the allowed list)"},
        "playbook_update_md": {"type": "string",
                               "description": "markdown section to inject into that "
                                              "playbook: current meta, concrete do/don't, "
                                              "cite the evidence. <200 words"},
    },
    "required": ["report_md", "lesson_summary", "playbook_target",
                 "playbook_update_md"],
}


def _top_shorts(max_results: int = 12) -> list[dict]:
    """Most-viewed recent Shorts to study. Prefers the curated winner channels
    (trainer.study_channels — uploads playlists at ~2 quota units per channel);
    falls back to 100-unit keyword searches when no channel list is configured."""
    out = []
    try:
        from googleapiclient.discovery import build
        creds = manager._creds()
        if not creds:
            return out
        yt = build("youtube", "v3", credentials=creds)
        ids = []
        chans = cfg.get("trainer.study_channels", []) or []
        if chans:
            # MOST-VIEWED, not most-recent. This used to take each channel's
            # newest 7 uploads and rank those by views, so the actual viral
            # outliers — the only videos worth reverse-engineering — never
            # entered the sample at all. order=viewCount asks YouTube for the
            # channel's biggest hits directly. Costs 100 quota units per channel
            # against a 10,000 daily budget, and the trainer runs weekly.
            days = int(cfg.get("trainer.study_window_days", 180))
            after = (datetime.utcnow() - timedelta(days=days)).strftime(
                "%Y-%m-%dT%H:%M:%SZ")
            for ch in chans[:int(cfg.get("trainer.study_channels_per_run", 3))]:
                try:
                    r = yt.search().list(part="id", channelId=ch, type="video",
                                         videoDuration="short",
                                         order="viewCount", publishedAfter=after,
                                         maxResults=6).execute()
                    ids += [i["id"]["videoId"] for i in r.get("items", [])
                            if i.get("id", {}).get("videoId")]
                except Exception as ex:  # noqa: BLE001 - one channel is fine
                    console.print(f"  [dim]study channel {ch[:24]} failed: "
                                  f"{str(ex)[:60]}[/]")
                    continue
        if not ids and chans:
            # CURATED CHANNELS ARE A DECISION, NOT A HINT. If they were
            # configured and returned nothing (usually search quota 403), do NOT
            # silently fall back to keyword search: that returns whatever is
            # viral on YouTube, which on 2026-07-22 meant Bugatti and diamond
            # channels heading into the agents' playbooks. Learning from the
            # wrong niche is worse than not training this week.
            console.print("[yellow]study channels returned nothing (quota?) — "
                          "skipping training rather than learning from random "
                          "viral content[/]")
            return out
        if not ids:
            after = (datetime.utcnow() - timedelta(days=14)).strftime("%Y-%m-%dT%H:%M:%SZ")
            queries = cfg.get("trainer.study_queries",
                              ["podcast clips", "joe rogan clips"])
            for q in queries[:3]:
                r = yt.search().list(q=q, part="id", type="video",
                                     videoDuration="short", order="viewCount",
                                     publishedAfter=after,
                                     maxResults=max_results // len(queries[:3]) + 2
                                     ).execute()
                ids += [i["id"]["videoId"] for i in r.get("items", [])]
        if not ids:
            return out
        v = yt.videos().list(part="snippet,statistics,contentDetails",
                             id=",".join(ids[:50])).execute()
        for it in v.get("items", []):
            sn, st = it["snippet"], it.get("statistics", {})
            views = int(st.get("viewCount", 0))
            likes = int(st.get("likeCount", 0))
            cmts = int(st.get("commentCount", 0))
            out.append({"_id": it["id"], "title": sn["title"],
                        "channel": sn["channelTitle"],
                        "duration": it["contentDetails"]["duration"],
                        "views": views, "likes": likes, "comments_n": cmts,
                        # ratios travel across channel sizes; raw views do not,
                        # so these are what actually say "this one over-performed"
                        "like_per_1k": round(likes / views * 1000, 1) if views else 0,
                        "cmt_per_1k": round(cmts / views * 1000, 1) if views else 0,
                        "published": sn.get("publishedAt", "")[:10],
                        "desc": (sn.get("description") or "")[:180],
                        "comments": []})
        out.sort(key=lambda r: -r["views"])
        out = out[:max_results]
        for row in out:                     # comments only for the final top-N
            row["id"] = row.pop("_id")      # keep it: the breakdown layer
            try:                            # downloads the video by this id
                c = yt.commentThreads().list(part="snippet",
                                             videoId=row["id"],
                                             order="relevance", maxResults=2,
                                             textFormat="plainText").execute()
                row["comments"] = [
                    x["snippet"]["topLevelComment"]["snippet"]["textDisplay"][:120]
                    for x in c.get("items", [])]
            except Exception:  # noqa: BLE001 - comments may be disabled
                pass
    except Exception as ex:  # noqa: BLE001 - study is best-effort
        console.print(f"[yellow]top-shorts study failed (continuing): {ex}[/]")
    return out


REFS_MD = ROOT / "research" / "viral_refs.md"


def _download_ref(video_id: str):
    """Fetch one reference Short with yt-dlp for ANALYSIS ONLY (deleted once
    the breakdown lands). No API quota is spent; capped at 720p and ~3 minutes
    so a mislabelled long video cannot eat the disk."""
    import yt_dlp
    refs = ROOT / "workdir" / "refs"
    refs.mkdir(parents=True, exist_ok=True)
    out = refs / f"{video_id}.mp4"
    if out.exists():
        return out
    opts = {"format": "bv*[height<=720]+ba/b[height<=720]",
            "merge_output_format": "mp4",
            "outtmpl": str(refs / "%(id)s.%(ext)s"),
            "quiet": True, "no_warnings": True,
            "match_filter": yt_dlp.utils.match_filter_func("duration < 200")}
    try:
        with yt_dlp.YoutubeDL(opts) as ydl:
            ydl.download([f"https://www.youtube.com/watch?v={video_id}"])
        return out if out.exists() else None
    except Exception:  # noqa: BLE001 - one missing ref is fine
        return None


def _measure_ref(path) -> dict:
    """Objective numbers the tagger cannot hallucinate: what the video actually
    says in its first seconds, how fast it talks, how often it cuts."""
    import subprocess
    from ..utils import media

    def probe(pp):
        try:
            r = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                                "format=duration", "-of", "csv=p=0", str(pp)],
                               capture_output=True, text=True)
            return float(r.stdout.strip() or 0)
        except Exception:  # noqa: BLE001
            return 0.0

    dur = probe(path)
    if dur <= 0:
        return {}
    audio = media.extract_audio(path)
    segs = media.transcribe(audio)
    words = [w for sg in segs for w in sg["words"]]
    hook3 = " ".join(w["word"] for w in words if w["start"] < 3.0).strip()
    hook8 = " ".join(w["word"] for w in words if w["start"] < 8.0).strip()
    full = " ".join(w["word"] for w in words).strip()
    try:
        r = subprocess.run(["ffmpeg", "-i", str(path), "-vf",
                            "select='gt(scene,0.4)',showinfo", "-f", "null", "-"],
                           capture_output=True, text=True)
        cuts = r.stderr.count("pts_time:")
    except Exception:  # noqa: BLE001
        cuts = 0
    return {"duration_s": round(dur, 1),
            "hook_first_3s": hook3[:220],
            "hook_first_8s": hook8[:400],
            "words_per_min": round(len(words) / dur * 60) if dur else 0,
            "cuts_per_min": round(cuts / (dur / 60), 1) if dur else 0,
            "transcript": full[:1500]}


BREAKDOWN_SCHEMA = {
    "type": "object",
    "properties": {
        "hook_type": {"type": "string",
                      "description": "shock-stat | contrarian | question | "
                                     "confession | callout | number-reveal | other"},
        "promise": {"type": "string",
                    "description": "what the first 3s promises the viewer, one line"},
        "structure": {"type": "string",
                      "description": "beat-by-beat shape in one line, e.g. "
                                     "'number, escalation, turn, lesson'"},
        "why_viral": {"type": "string",
                      "description": "the single strongest reason THIS one "
                                     "outperformed, grounded in the transcript"},
        "tactics": {"type": "array", "items": {"type": "string"},
                    "description": "3-6 concrete, replicable tactics (hook "
                                   "phrasing, pacing, title pattern, payoff "
                                   "placement). Each must be actionable by our "
                                   "agents, not a vague observation."},
        "title_pattern": {"type": "string",
                          "description": "the reusable shape of the title, "
                                         "e.g. '[$amount] [asset] at [%rate]'"},
    },
    "required": ["hook_type", "promise", "structure", "why_viral", "tactics",
                 "title_pattern"],
}


def _breakdown_refs(refs: list[dict], max_n: int = 3) -> list[dict]:
    """Reverse-engineer the top outliers: download, MEASURE, then tag.

    This is the layer the trainer was missing. It used to study metadata
    (title, views, two comments) and never watched a single video, so its
    lessons were guesses about content it had not seen. Now the biggest
    outliers are transcribed and measured, and the tagger reasons over the
    video's actual words and pace. Results accumulate in research/viral_refs.md
    so knowledge builds week over week instead of evaporating per run.
    """
    import json
    REFS_MD.parent.mkdir(parents=True, exist_ok=True)
    seen = REFS_MD.read_text(encoding="utf-8") if REFS_MD.exists() else ""
    tagged = []
    fails = {"download": 0, "measure": 0, "tag": 0}
    for ref in refs[:max_n]:
        vid = ref.get("id")
        if not vid:
            continue
        if f"<!-- ref:{vid} -->" in seen:
            continue                        # already analyzed a past week
        path = _download_ref(vid)
        if not path:
            fails["download"] += 1
            continue
        m = _measure_ref(path)
        if not m:
            fails["measure"] += 1
            continue
        stats = {k: m[k] for k in ("duration_s", "words_per_min", "cuts_per_min")}
        prompt = (
            "You are reverse-engineering a VIRAL money/finance Short. Ground "
            "every claim in the measured data below; do not invent visuals you "
            "cannot see.\n\n"
            f"title: {ref.get('title')}\nchannel: {ref.get('channel')}\n"
            f"views: {ref.get('views'):,}  likes/1k: {ref.get('like_per_1k')}  "
            f"comments/1k: {ref.get('cmt_per_1k')}\n"
            f"measured: {json.dumps(stats)}\n"
            f"first 3s (verbatim): {m['hook_first_3s']}\n"
            f"first 8s (verbatim): {m['hook_first_8s']}\n"
            f"transcript: {m['transcript']}\n\n"
            "Call submit_breakdown.")
        try:
            r = llm.call_tool("manager", prompt, "submit_breakdown",
                              BREAKDOWN_SCHEMA, max_tokens=900)
        except Exception:  # noqa: BLE001
            r = None
        if not r:
            fails["tag"] += 1
            continue
        r.update({"id": vid, "title": ref.get("title"),
                  "views": ref.get("views"), "measured": m})
        tagged.append(r)
        stamp = datetime.now().strftime("%Y-%m-%d")
        hookq = m["hook_first_3s"][:120]
        entry = (f"\n<!-- ref:{vid} -->\n"
                 f"## {ref.get('title')}  ({ref.get('views'):,} views, {stamp})\n"
                 f"- channel: {ref.get('channel')}  |  {m['duration_s']}s, "
                 f"{m['words_per_min']} wpm, {m['cuts_per_min']} cuts/min, "
                 f"L/1k={ref.get('like_per_1k')} C/1k={ref.get('cmt_per_1k')}\n"
                 f'- hook ({r["hook_type"]}): "{hookq}"\n'
                 f"- promise: {r['promise']}\n"
                 f"- structure: {r['structure']}\n"
                 f"- why it won: {r['why_viral']}\n"
                 f"- title pattern: {r['title_pattern']}\n"
                 + "".join(f"- tactic: {t}\n" for t in r.get("tactics", [])[:6]))
        with REFS_MD.open("a", encoding="utf-8") as f:
            f.write(entry)
        try:                                # analysis done: drop the footage
            path.unlink(missing_ok=True)
            path.with_suffix(".wav").unlink(missing_ok=True)
        except Exception:  # noqa: BLE001
            pass
        console.print(f"  [dim]broke down: {str(ref.get('title'))[:50]} "
                      f"({ref.get('views'):,} views)[/]")
    if any(fails.values()):
        console.print(f"  [yellow]breakdowns skipped: {fails} - if download "
                      f"fails persist, yt-dlp likely needs an update[/]")
    return tagged


def _meta_scan() -> str:
    """Fresh web snippets: monetization policy + current editing-style meta."""
    try:
        from ddgs import DDGS
    except Exception:  # noqa: BLE001
        return ""
    queries = [
        "YouTube Shorts monetization policy update reused content",
        "YouTube Shorts editing style trends what works retention",
        "viral shorts caption hook style meta this month",
    ]
    lines = []
    try:
        with DDGS() as d:
            for q in queries:
                for r in d.text(q, max_results=4):
                    lines.append(f"- {r.get('title', '')}: {r.get('body', '')[:200]}")
    except Exception:  # noqa: BLE001
        pass
    return "\n".join(lines[:20])


def _backtest() -> list[dict]:
    """The Finder's predictions vs reality — its report card, now with the real
    retention % (the metric that actually drives distribution)."""
    from .. import db
    with db.conn() as c:
        rows = c.execute("""
            SELECT cl.title, cl.score AS predicted,
                   ROUND(cl.end - cl.start, 0) AS secs, MAX(m.views) AS views,
                   m.avg_watch_pct AS watch_pct
            FROM clips cl
            JOIN uploads up ON up.clip_id = cl.id AND up.platform='youtube'
            JOIN metrics m ON m.upload_id = up.id
            GROUP BY cl.id ORDER BY views DESC
        """).fetchall()
    return [dict(r) for r in rows]


def _our_best() -> dict | None:
    """Our single best-retention clip — the Trainer studies THIS as a positive
    template ('do more of what already worked for US'), not just outside winners."""
    from .. import db
    with db.conn() as c:
        r = c.execute("""
            SELECT cl.title, cl.reason, ROUND(cl.end - cl.start, 0) AS secs,
                   MAX(m.views) AS views, m.avg_watch_pct AS watch_pct
            FROM clips cl
            JOIN uploads up ON up.clip_id = cl.id AND up.platform='youtube'
            JOIN metrics m ON m.upload_id = up.id
            GROUP BY cl.id
            ORDER BY m.avg_watch_pct DESC, m.views DESC LIMIT 1
        """).fetchone()
    return dict(r) if r else None


def _apply_playbook_update(target: str, update_md: str) -> bool:
    """Inject the lesson between TRAINER markers in the playbook (replace old)."""
    allowed = cfg.get("trainer.playbooks", DEFAULT_PLAYBOOKS)
    if target not in allowed or target == "editorial-standards":
        return False
    f = ROOT / "factory" / "skills" / f"{target}.md"
    if not f.exists():
        return False
    text = f.read_text(encoding="utf-8")
    stamp = datetime.now().strftime("%Y-%m-%d")
    block = (f"{MARK_START}\n## Trainer's current-meta notes ({stamp})\n"
             f"{update_md.strip()}\n{MARK_END}")
    if MARK_START in text:
        pre = text.split(MARK_START)[0]
        post = text.split(MARK_END, 1)[1] if MARK_END in text else ""
        text = pre.rstrip() + "\n\n" + block + post
    else:
        text = text.rstrip() + "\n\n" + block + "\n"
    f.write_text(text, encoding="utf-8")
    return True


def train() -> bool:
    """The weekly coaching session. Returns True if a lesson landed."""
    if not cfg.get("trainer.enabled", True) or not llm.available():
        return False
    console.print(f"[bold cyan]TRAINER[/] studying the winners ({llm.describe()})…")

    top = _top_shorts()
    if cfg.get("trainer.study_channels") and not top:
        # the guard in _top_shorts already refused to fall back to random
        # keyword search; honor it here too instead of coaching from thin air
        console.print("[yellow]trainer: study channels configured but returned "
                      "nothing (quota?) - skipping the week[/]")
        return False
    tagged = _breakdown_refs(top)
    meta = _meta_scan()
    back = _backtest()
    best = _our_best()
    allowed = cfg.get("trainer.playbooks", DEFAULT_PLAYBOOKS)

    import json
    prompt = f"""You are the TRAINER (coach) of an automated YouTube Shorts team.
Study the evidence, figure out WHAT IS WORKING AND WHY — for OTHER channels AND
for US — and teach it by updating one craft playbook.

TOP-PERFORMING RECENT SHORTS IN OUR NICHE (title/channel/duration/views/top comments):
{json.dumps(top, indent=1)[:4000] or '(API unavailable this week)'}

DEEP BREAKDOWNS OF THE BIGGEST OUTLIERS (we downloaded, transcribed and
measured these; every tactic is grounded in the video's actual words and pace):
{json.dumps([dict((k, t[k]) for k in ('title', 'views', 'hook_type', 'promise',
'structure', 'why_viral', 'tactics', 'title_pattern')) for t in tagged],
indent=1)[:3500] if tagged else '(none new this week; see research/viral_refs.md)'}

FRESH POLICY & EDITING-META RESEARCH:
{meta or '(no web results)'}

OUR OWN REPORT CARD (predicted 0-100 vs actual views AND watch_pct = % retention):
{json.dumps(back, indent=1)[:1800] or '(no posted clips yet)'}

OUR OWN BEST CLIP (highest retention — this is the template to REPEAT):
{json.dumps(best, indent=1) if best else '(none yet)'}

Reason like a coach: (1) What does OUR best clip do that our flops don't — copy
it. (2) retention (watch_pct) drives distribution — clips under ~50% watched are
losing people early; what's different about them? (3) What do outside winners
share? (4) Where is our Finder systematically wrong (high predicted, low views)?

playbook_target MUST be one of: {', '.join(allowed)}.
Call submit_training."""

    result = llm.call_tool("manager", prompt, "submit_training", TRAIN_SCHEMA,
                           max_tokens=2000)
    if not result:
        console.print("[yellow]Trainer: no lesson this week (LLM unavailable).[/]")
        return False

    # Manager sign-off before anything reaches the team
    skill_block = skills.load(cfg.get("skills.manager", []))
    review_prompt = (f"{skill_block}\nYou are the channel Manager. The Trainer "
                     f"proposes this update to the '{result['playbook_target']}' "
                     f"playbook:\n---\n{result['playbook_update_md']}\n---\n"
                     "Approve ONLY if it is specific, evidence-based, and does not "
                     "contradict the editorial standards. Call submit_review.")
    review = None
    for attempt in range(3):                 # review call is cheap — retry hard;
        review = llm.call_tool("manager", review_prompt, "submit_review",
                               manager.REVIEW_TOOL_SCHEMA, max_tokens=400)
        if review is not None:
            break
        import time
        time.sleep(30 * (attempt + 1))       # rate-limit cooldown
    approved = bool(review and review.get("approved", False))

    (ROOT / "training.md").write_text(result["report_md"].strip() + "\n",
                                      encoding="utf-8")
    if approved and _apply_playbook_update(result["playbook_target"],
                                           result["playbook_update_md"]):
        console.print(f"[green]✓ TRAINER lesson approved → "
                      f"{result['playbook_target']}.md updated[/]")
        notify.notify("Trainer: lesson of the week",
                      f"{result['lesson_summary'][:120]} "
                      f"(→ {result['playbook_target']})")
        return True
    manager.flag_attention(
        f"Trainer's lesson was NOT applied "
        f"({'Manager rejected: ' + (review or {}).get('notes', '')[:100] if review else 'review unavailable'}). "
        f"Proposed for {result['playbook_target']}: {result['lesson_summary'][:100]}")
    return False
