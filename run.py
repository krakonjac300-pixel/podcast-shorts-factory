#!/usr/bin/env python
"""Podcast Shorts Factory — orchestrator CLI.

Commands:
  find <url>     Agent 1: download, transcribe, AI-score clip candidates
  review         Approve/reject the candidates (human in the loop)
  edit           Agent 2: render approved clips into vertical shorts
  finish         Agent 8: QA-review + finish each render before it can post
  compile        Agent 9: weekly 16:9 long-form episode (--no-upload to just render)
  upload         Agent 3: post/export to platforms (asks per platform)
  stats          Agent 4: refresh metrics + update learnings.md
  scout          Trend Scout: web-search current trends → trends.md
  skills         List installed skills and which agent loads each
  auth-youtube   Connect your YouTube channel (OAuth) and cache the token
  auto <url>     Full semi-auto pipeline (pauses at review + upload)
                 add --yes to auto-approve the top N clips and post unattended
  meeting        Daily standup: scan analytics -> per-agent instructions in learnings.md
  craft          Score our own edits vs retention → craft.md (the editor's learning loop)
  daily          Unattended: scout + newest video from scheduler.source_url + auto --yes
  produce        Make the day's clips into the post queue (no posting)
  post-next      Post the single best queued clip (for staggered 3x/day posting)
"""
from __future__ import annotations

import sys

from rich.console import Console
from rich.prompt import Prompt
from rich.table import Table

from factory import craft, db, notify, skills
from factory.agents import (community, compiler, editor, finder,
                            finishing_editor, manager, montage, trainer,
                            trend_scout, uploader)
from factory.config import cfg
from factory.utils import media

console = Console()


def cmd_skills():
    installed = skills.available()
    console.print(f"[bold]{len(installed)} skills installed[/] in factory/skills/\n")
    for agent in ("finder", "editor", "uploader", "manager"):
        names = cfg.get(f"skills.{agent}", [])
        miss = skills.missing(names)
        line = ", ".join(f"[green]{n}[/]" if n not in miss else f"[red]{n}?[/]"
                         for n in names)
        console.print(f"[bold cyan]{agent:9}[/] {line}")
    unused = set(installed) - {n for a in ("finder", "editor", "uploader", "manager")
                               for n in cfg.get(f"skills.{a}", [])}
    if unused:
        console.print(f"\n[dim]installed but unassigned: {', '.join(sorted(unused))}[/]")


def cmd_review():
    cands = db.clips_by_status("candidate")
    if not cands:
        console.print("[yellow]No candidates to review. Run `find` first.[/]")
        return
    for c in cands:
        t = Table(show_header=False, box=None)
        t.add_row("[bold]" + c["title"] + "[/]")
        t.add_row(f"⏱  {c['start']:.0f}s → {c['end']:.0f}s "
                  f"({c['end']-c['start']:.0f}s)   score {c['score']:.0f}/100")
        t.add_row(f"[dim]{c['reason']}[/]")
        t.add_row(f"[italic]caption:[/] {c['caption']}")
        console.print(t)
        choice = Prompt.ask("  [a]pprove / [r]eject / [s]kip / [q]uit",
                            choices=["a", "r", "s", "q"], default="a")
        if choice == "q":
            break
        if choice == "a":
            db.set_clip_status(c["id"], "approved")
            console.print("  [green]approved[/]\n")
        elif choice == "r":
            db.set_clip_status(c["id"], "rejected")
            console.print("  [red]rejected[/]\n")


def auto_approve_top(n: int) -> int:
    """Approve the highest-scoring N candidates (for unattended runs)."""
    cands = db.clips_by_status("candidate")  # already ordered by score desc
    approved = 0
    for c in cands:
        db.set_clip_status(c["id"], "approved" if approved < n else "rejected")
        approved += approved < n
    console.print(f"[green]✓ auto-approved top {min(n, len(cands))} of "
                  f"{len(cands)} candidates[/]")
    return min(n, len(cands))


def approve_next(n: int) -> int:
    """Approve up to N best still-'candidate' clips WITHOUT rejecting the rest,
    so leftovers stay available to backfill any clip the finishing QA blocks."""
    if n <= 0:
        return 0
    take = db.clips_by_status("candidate")[:n]      # score desc
    for c in take:
        db.set_clip_status(c["id"], "approved")
    if take:
        console.print(f"[green]✓ approved {len(take)} more clip(s) to render[/]")
    return len(take)


def cmd_auto(url: str, assume_yes: bool = False):
    finder.find(url)
    console.rule("[bold]Review")
    if assume_yes:
        auto_approve_top(cfg.get("finder.auto_approve_top", 3))
    else:
        cmd_review()
    console.rule("[bold]Edit")
    editor.edit_all()
    console.rule("[bold]Finishing review")
    finishing_editor.finish_all()
    finishing_editor.ensure_floor()   # never let the day go fully dark (salvage least-bad)
    console.rule("[bold]Upload")
    uploader.upload_all(assume_yes=assume_yes)
    console.rule("[bold]Stats")
    manager.collect()
    manager.report()


def _rank_sources(sources: list[str]) -> list[str]:
    """Order sources by real performance (Manager's channel_ranking, avg views),
    but ONLY once there's a meaningful signal — otherwise config order wins.
    A handful of views is noise; reordering on it would silently override a
    deliberate choice (e.g. leaning into the World Cup while it's live)."""
    ranking = manager.channel_ranking()
    min_signal = cfg.get("scheduler.rank_min_views", 100)
    if not ranking or max(ranking.values()) < min_signal:
        return sources                              # not enough data → trust config

    def score(src: str) -> float:
        s = src.lower()
        return max((v for ch, v in ranking.items()
                    if ch and ch.lower().lstrip("@") in s), default=0.0)

    ordered = sorted(sources, key=score, reverse=True)   # stable: ties keep order
    if ordered != sources:
        console.print(f"[dim]source order (by performance): {', '.join(ordered)}[/]")
    return ordered


def _pick_source_video():
    """Fresh, downloadable video from scheduler.sources (best-performing channel
    first, skipping members-only/unavailable and already-processed). Falls back
    to source_url."""
    sources = cfg.get("scheduler.sources") or []
    if not sources:
        s = cfg.get("scheduler.source_url", "")
        sources = [s] if s else []
    if not sources:
        console.print("[red]Set scheduler.sources (channel/playlist URLs) in config.yaml.[/]")
        return None
    url = media.pick_next(_rank_sources(sources), skip_urls=db.processed_urls())
    if not url:
        console.print("[yellow]No fresh downloadable video across the sources right now.[/]")
    return url



def _ensure_day_covered(target: int | None = None) -> int:
    """Confirm the day really has `target` Shorts live or scheduled, and if not,
    produce to fill the gap.

    The pipeline's promise is 3 posts a day. Nothing was actually CHECKING that:
    produce ran at 06:00 and whatever it managed became the day. When produce
    died (it was being killed by the battery policy for days) the day silently
    went to zero, and post-next reported "already scheduled on YouTube's side".
    """
    target = target or len(cfg.get("uploader.post_times", []) or []) or 3
    try:
        have = uploader.videos_for_day()
    except Exception as ex:  # noqa: BLE001 - never let the check break posting
        console.print(f"[yellow]could not verify today's coverage: {ex}[/]")
        return -1

    if len(have) >= target:
        console.print(f"[green]day covered: {len(have)}/{target} shorts live or "
                      f"scheduled[/]")
        return len(have)

    short = target - len(have)
    msg = (f"only {len(have)}/{target} shorts are live or scheduled today — "
           f"producing {short} more")
    console.print(f"[yellow]{msg}[/]")
    notify.notify("Day under-filled, recovering", msg)
    try:
        _run_produce(force=True)
    except Exception as ex:  # noqa: BLE001
        notify.notify("Recovery produce FAILED",
                      f"{msg}, but the recovery run failed: {ex}")
    return len(have)


def cmd_daily():
    """Unattended scheduled run: trends → fresh video from sources → full pipeline."""
    console.rule("[bold]Trend Scout")
    trend_scout.scout()
    console.rule("[bold]Pick source video")
    url = _pick_source_video()
    if not url:
        return
    console.print(f"[green]New video:[/] {url}")
    cmd_auto(url, assume_yes=True)


def cmd_produce(force: bool = False):
    """Make the day's clips into the post queue (no posting). Scout → fresh video
    → top N clips rendered and left as 'edited' for staggered posting.
    force=True bypasses the catch-up guard (used to deliberately pre-load a day
    that's already partly scheduled — new clips roll into the next free slots)."""
    # SINGLE-INSTANCE LOCK — the #1 cause of the "produce dies mid-transcription"
    # failures (2026-07-10..15) was TWO runs colliding: the 6AM/startup task, the
    # logon catch-up, and manual triggers could all fire minutes apart, then fight
    # over the same workdir files and kill each other. A stale-tolerant lockfile
    # lets exactly one produce run at a time; extras exit cleanly.
    import os
    import time
    from factory.config import ROOT
    lock = ROOT / "workdir" / ".produce.lock"
    lock.parent.mkdir(exist_ok=True)
    if lock.exists() and time.time() - lock.stat().st_mtime < 1800:
        console.print("[yellow]produce: another run is already in progress — "
                      "exiting to avoid a collision.[/]")
        return
    try:
        lock.write_text(str(os.getpid()))
    except OSError:
        pass
    try:
        _run_produce(force)
    finally:
        lock.unlink(missing_ok=True)


def _run_produce(force: bool):
    # Catch-up guard: PSF-Produce also fires at machine startup (the PC is often
    # off at 6AM and Windows' missed-run catch-up proved unreliable, 2026-07-10/11).
    # If today's posts are already locked in server-side, this run is a no-op —
    # so the startup trigger can never double-book a day the 6AM run handled.
    from datetime import datetime
    from factory.agents.uploader import _taken_slots
    today = [t for t in _taken_slots()
             if t.date() == datetime.now().astimezone().date()]
    if not force and len(today) >= 2:
        console.print(f"[dim]produce: {len(today)} post(s) already scheduled for "
                      f"today — day covered, skipping (catch-up guard).[/]")
        return
    console.print(f"[dim]post queue has {len(db.clips_by_status('edited'))} clip(s) ready[/]")
    console.rule("[bold]Trend Scout")
    trend_scout.scout()
    console.rule("[bold]Pick source video")
    url = _pick_source_video()
    if not url:
        return
    console.print(f"[green]New video:[/] {url}")
    finder.find(url)

    # Format experiment: one montage Short (cross-episode moments from PAST
    # sources) takes one of today's slots when it builds successfully.
    montage_id = None
    try:
        montage_id = montage.build_daily()
        if montage_id:
            finishing_editor.finish_all(clip_ids=[montage_id])
    except Exception as ex:  # noqa: BLE001 - experiment must never kill the day
        console.print(f"[yellow]montage skipped: {ex}[/]")

    # Render up to `target` clips that PASS finishing QA. The montage (already
    # status 'edited') occupies one of the slots, so the loop tops up with fresh
    # clips around it. If block_on_fail holds a broken clip back, backfill the
    # freed slot with the next-best candidate — blocking never thins the day.
    target = cfg.get("finder.auto_approve_top", 3)
    console.rule(f"[bold]Render {target} clip(s) to the queue")
    for _ in range(target + 2):                     # safety bound on rounds
        need = target - len(db.clips_by_status("edited"))
        if need <= 0 or approve_next(need) == 0:
            break
        before = {c["id"] for c in db.clips_by_status("edited")}
        editor.edit_all()                           # renders the just-approved clips
        new = [c["id"] for c in db.clips_by_status("edited") if c["id"] not in before]
        finishing_editor.finish_all(clip_ids=new)   # QA only the fresh renders

    for c in db.clips_by_status("candidate"):        # drop spares we didn't need
        db.set_clip_status(c["id"], "rejected")

    if cfg.get("manager.review_before_post", True):
        console.rule("[bold]Manager review")
        from factory.agents.uploader import _review_and_fix
        for c in db.clips_by_status("edited"):
            _review_and_fix(c)      # approve / bounce+re-edit / reject+escalate
    finishing_editor.ensure_floor()  # never let the day go fully dark (salvage least-bad)
    n = len(db.clips_by_status("edited"))
    console.print(f"[green]✓ queue now has {n} clip(s).[/]")
    if cfg.get("uploader.schedule_mode", True) and n:
        console.rule("[bold]Schedule the day on YouTube")
        uploader.schedule_day()     # server-side publishAt — PC can be off
    else:
        notify.notify("Morning batch ready",
                      f"{n} clip(s) queued and Manager-approved for today's posts")


def main(argv: list[str]):
    if not argv:
        console.print(__doc__)
        return
    cmd, *rest = argv
    urls = [a for a in rest if not a.startswith("--")]
    if cmd == "find":
        if not urls:
            console.print("[red]usage: run.py find <youtube-url>[/]"); return
        finder.find(urls[0])
    elif cmd == "review":
        cmd_review()
    elif cmd == "edit":
        editor.edit_all()
    elif cmd == "finish":
        finishing_editor.finish_all()
    elif cmd == "compile":
        compiler.compile_episode(upload="--no-upload" not in rest,
                                 force="--force" in rest)
    elif cmd == "montage":
        montage.build_daily(register="--dry-run" not in rest)
    elif cmd == "flip-money":
        import subprocess as _sp
        import sys as _sys
        from factory.config import ROOT as _root
        _sp.run([_sys.executable, str(_root / "tools" / "flip_to_money.py")],
                check=True)
    elif cmd == "enable-refine":
        # two-pass captions ON — scheduled a day after the flip so a failure
        # has one cause, not five
        import subprocess as _sp
        import sys as _sys
        from factory.config import ROOT as _root
        _sp.run([_sys.executable, str(_root / "tools" / "enable_refine.py")],
                check=True)
    elif cmd == "upload":
        uploader.upload_all(assume_yes="--yes" in rest)
    elif cmd == "stats":
        manager.collect(); manager.report()
    elif cmd == "scout":
        trend_scout.scout()
    elif cmd == "trainer":
        trainer.train()
    elif cmd == "skills":
        cmd_skills()
    elif cmd == "auth-youtube":
        uploader.authenticate()
    elif cmd == "auto":
        if not urls:
            console.print("[red]usage: run.py auto <youtube-url> [--yes][/]"); return
        cmd_auto(urls[0], assume_yes="--yes" in rest)
    elif cmd == "daily":
        cmd_daily()
    elif cmd == "produce":
        cmd_produce(force="--force" in rest)
    elif cmd == "comments":
        community.engage()
    elif cmd == "digest":
        manager.weekly_digest()
    elif cmd == "meeting":
        # daily 13:00 standup: fresh numbers -> per-agent instructions
        manager.team_meeting()
    elif cmd == "craft":
        # Re-score our own edits against measured retention and rewrite craft.md
        console.print(craft.update())
    elif cmd == "post-next":
        manager.refresh_learnings()  # fresh metrics + re-reasoned team directives
        if not uploader.upload_one(assume_yes=True):
            if cfg.get("uploader.schedule_mode", True):
                # An empty queue used to be treated as proof the day was already
                # locked in. It is not: it is ALSO what a failed produce looks
                # like, and the two were indistinguishable, so 2026-07-21 went
                # completely empty in silence. Verify against YouTube instead of
                # assuming, and recover rather than just reporting.
                _ensure_day_covered()
            else:
                notify.notify("Nothing to post",
                              "The queue is empty — did the 6AM produce run fail?")
    elif cmd == "schedule-day":
        uploader.schedule_day()
    else:
        console.print(f"[red]unknown command:[/] {cmd}")
        console.print(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
