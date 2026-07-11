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
