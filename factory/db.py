"""Tiny SQLite store shared by all four agents."""
from __future__ import annotations

import json
import sqlite3
from contextlib import contextmanager
from datetime import datetime

from .config import ROOT

DB_PATH = ROOT / "factory.db"

SCHEMA = """
CREATE TABLE IF NOT EXISTS sources (
    id INTEGER PRIMARY KEY,
    url TEXT UNIQUE,
    title TEXT,
    video_path TEXT,
    transcript_json TEXT,
    created_at TEXT
);
CREATE TABLE IF NOT EXISTS clips (
    id INTEGER PRIMARY KEY,
    source_id INTEGER,
    start REAL,
    end REAL,
    title TEXT,
    reason TEXT,
    score REAL,
    caption TEXT,
    hashtags TEXT,
    status TEXT DEFAULT 'candidate',   -- candidate | approved | rejected | edited | uploaded
    rendered_path TEXT,
    created_at TEXT,
    FOREIGN KEY(source_id) REFERENCES sources(id)
);
CREATE TABLE IF NOT EXISTS uploads (
    id INTEGER PRIMARY KEY,
    clip_id INTEGER,
    platform TEXT,
    external_id TEXT,
    url TEXT,
    created_at TEXT,
    FOREIGN KEY(clip_id) REFERENCES clips(id)
);
CREATE TABLE IF NOT EXISTS metrics (
    id INTEGER PRIMARY KEY,
    upload_id INTEGER,
    views INTEGER,
    likes INTEGER,
    comments INTEGER,
    shares INTEGER,
    avg_watch_pct REAL,
    measured_at TEXT,
    FOREIGN KEY(upload_id) REFERENCES uploads(id)
);
-- What the editor ACTUALLY did to each clip (cut rate, punch count, SFX count,
-- hook length, reframe mode...). Without this the channel can measure that a
-- clip held 82% but never learn WHICH craft choices earned it, so every
-- "learning" stays an opinion. factory/craft.py joins this against `metrics`
-- to score individual editing decisions on real retention.
CREATE TABLE IF NOT EXISTS edit_specs (
    id INTEGER PRIMARY KEY,
    clip_id INTEGER UNIQUE,
    niche TEXT,
    spec_json TEXT,
    created_at TEXT,
    FOREIGN KEY(clip_id) REFERENCES clips(id)
);
"""


@contextmanager
def conn():
    """`with conn() as c:` — commits on success AND closes the connection.
    (A bare sqlite3 connection's `with` only commits; it never closes, which
    leaks a file handle per call.)"""
    c = sqlite3.connect(DB_PATH)
    c.row_factory = sqlite3.Row
    c.executescript(SCHEMA)
    for mig in ("ALTER TABLE sources ADD COLUMN channel TEXT",
                "ALTER TABLE clips ADD COLUMN review_notes TEXT",
                "ALTER TABLE clips ADD COLUMN review_attempts INTEGER DEFAULT 0",
                # format experiments: NULL = regular clip, 'montage' = the
                # multi-moment montage Short — lets the Manager compare formats
                "ALTER TABLE clips ADD COLUMN kind TEXT"):
        try:                                # migrate older databases
            c.execute(mig)
        except sqlite3.OperationalError:    # column already exists
            pass
    try:
        with c:
            yield c
    finally:
        c.close()


def now() -> str:
    return datetime.utcnow().isoformat()


# ── source helpers ────────────────────────────────────────────────
def upsert_source(url, title, video_path, transcript, channel="") -> int:
    with conn() as c:
        c.execute(
            """INSERT INTO sources(url,title,video_path,transcript_json,channel,created_at)
               VALUES(?,?,?,?,?,?)
               ON CONFLICT(url) DO UPDATE SET
                 title=excluded.title, video_path=excluded.video_path,
                 transcript_json=excluded.transcript_json, channel=excluded.channel""",
            (url, title, str(video_path), json.dumps(transcript), channel, now()),
        )
        return c.execute("SELECT id FROM sources WHERE url=?", (url,)).fetchone()[0]


def add_clip(source_id, start, end, title, reason, score, caption, hashtags) -> int:
    with conn() as c:
        cur = c.execute(
            """INSERT INTO clips(source_id,start,end,title,reason,score,caption,hashtags,created_at)
               VALUES(?,?,?,?,?,?,?,?,?)""",
            (source_id, start, end, title, reason, score, caption,
             json.dumps(hashtags), now()),
        )
        return cur.lastrowid


def clips_by_status(status: str):
    with conn() as c:
        return c.execute("SELECT * FROM clips WHERE status=? ORDER BY score DESC",
                         (status,)).fetchall()


def set_clip_status(clip_id, status, rendered_path=None):
    with conn() as c:
        if rendered_path:
            c.execute("UPDATE clips SET status=?, rendered_path=? WHERE id=?",
                      (status, str(rendered_path), clip_id))
        else:
            c.execute("UPDATE clips SET status=? WHERE id=?", (status, clip_id))


def get_source(source_id):
    with conn() as c:
        return c.execute("SELECT * FROM sources WHERE id=?", (source_id,)).fetchone()


def clip_by_id(clip_id):
    with conn() as c:
        return c.execute("SELECT * FROM clips WHERE id=?", (clip_id,)).fetchone()


def uploaded_to(clip_id, platform: str) -> bool:
    with conn() as c:
        return c.execute("SELECT 1 FROM uploads WHERE clip_id=? AND platform=?",
                         (clip_id, platform)).fetchone() is not None


def set_review(clip_id, notes: str) -> None:
    """Store the Manager's bounce notes and count the attempt."""
    with conn() as c:
        c.execute("""UPDATE clips SET review_notes=?,
                     review_attempts=COALESCE(review_attempts,0)+1 WHERE id=?""",
                  (notes, clip_id))


def processed_urls() -> set:
    """All source video URLs already processed (to skip them next run)."""
    with conn() as c:
        return {r[0] for r in c.execute("SELECT url FROM sources").fetchall()}


def record_upload(clip_id, platform, external_id, url) -> int:
    with conn() as c:
        cur = c.execute(
            """INSERT INTO uploads(clip_id,platform,external_id,url,created_at)
               VALUES(?,?,?,?,?)""",
            (clip_id, platform, external_id, url, now()),
        )
        return cur.lastrowid


# ── craft feedback loop ───────────────────────────────────────────
def record_edit_spec(clip_id: int, spec: dict, niche: str = "") -> None:
    """Save the measured craft parameters of a finished render.

    Re-rendering a clip (a Manager bounce) REPLACES the row, so the spec always
    describes the cut that actually shipped.
    """
    with conn() as c:
        c.execute(
            """INSERT INTO edit_specs(clip_id,niche,spec_json,created_at)
               VALUES(?,?,?,?)
               ON CONFLICT(clip_id) DO UPDATE SET
                 spec_json=excluded.spec_json, niche=excluded.niche,
                 created_at=excluded.created_at""",
            (clip_id, niche, json.dumps(spec, sort_keys=True), now()),
        )


def edit_spec(clip_id: int) -> dict:
    with conn() as c:
        r = c.execute("SELECT spec_json FROM edit_specs WHERE clip_id=?",
                      (clip_id,)).fetchone()
    return json.loads(r[0]) if r else {}


def specs_with_metrics(niche: str = "") -> list[dict]:
    """Every clip that has BOTH a recorded edit spec and measured performance.

    Uses the most recent metrics row per upload (retention keeps moving for days
    after publish, so an early sample would understate every clip equally but
    add noise). Returns one dict per clip: spec fields + views/retention.
    """
    q = """SELECT s.clip_id, s.niche, s.spec_json, c.title,
                  m.views, m.likes, m.comments, m.avg_watch_pct
             FROM edit_specs s
             JOIN clips   c ON c.id = s.clip_id
             JOIN uploads u ON u.clip_id = s.clip_id AND u.platform = 'youtube'
             JOIN metrics m ON m.upload_id = u.id
            WHERE m.id = (SELECT MAX(m2.id) FROM metrics m2
                           WHERE m2.upload_id = u.id)"""
    args: tuple = ()
    if niche:
        q += " AND s.niche = ?"
        args = (niche,)
    with conn() as c:
        rows = c.execute(q, args).fetchall()

    out = []
    for r in rows:
        try:
            spec = json.loads(r["spec_json"])
        except (ValueError, TypeError):
            continue
        spec.update({"clip_id": r["clip_id"], "title": r["title"],
                     "niche": r["niche"], "views": r["views"] or 0,
                     "likes": r["likes"] or 0, "comments": r["comments"] or 0,
                     "retention": r["avg_watch_pct"]})
        out.append(spec)
    return out
