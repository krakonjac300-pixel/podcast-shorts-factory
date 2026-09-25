"""Tiny SQLite store shared by all four agents."""
from __future__ import annotations

import json
import sqlite3
from contextlib import contextmanager
from datetime import datetime, timezone

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
-- clipping campaigns (Whop Content Rewards, Vyro, ...) the Campaigner found
CREATE TABLE IF NOT EXISTS campaigns (
    id TEXT PRIMARY KEY,               -- '<provider>:<native id>'
    provider TEXT,
    title TEXT,
    data_json TEXT,                    -- the full Campaign record
    seen_at TEXT
);
-- one row per (post URL -> campaign) submission, whatever its state
CREATE TABLE IF NOT EXISTS campaign_submissions (
    id INTEGER PRIMARY KEY,
    campaign_id TEXT,
    clip_id INTEGER,
    upload_id INTEGER,
    post_url TEXT,
    status TEXT,     -- pending_approval | submitted | needs_manual | approved | denied | failed | skipped
    external_id TEXT,
    detail TEXT,
    created_at TEXT,
    updated_at TEXT,
    UNIQUE(campaign_id, post_url)
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
                "ALTER TABLE clips ADD COLUMN kind TEXT",
                # footage that came from a clipping campaign, not a channel
                "ALTER TABLE sources ADD COLUMN campaign_id TEXT",
                # when a scheduled post actually goes public (campaigns only
                # accept live posts); NULL = public at created_at
                "ALTER TABLE uploads ADD COLUMN publish_at TEXT"):
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
def upsert_source(url, title, video_path, transcript, channel="",
                  campaign_id=None) -> int:
    with conn() as c:
        c.execute(
            """INSERT INTO sources(url,title,video_path,transcript_json,channel,
                                  campaign_id,created_at)
               VALUES(?,?,?,?,?,?,?)
               ON CONFLICT(url) DO UPDATE SET
                 title=excluded.title, video_path=excluded.video_path,
                 transcript_json=excluded.transcript_json, channel=excluded.channel,
                 campaign_id=COALESCE(excluded.campaign_id, sources.campaign_id)""",
            (url, title, str(video_path), json.dumps(transcript), channel,
             campaign_id, now()),
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


def record_upload(clip_id, platform, external_id, url, publish_at=None) -> int:
    """`publish_at` (aware datetime) for scheduled posts that go public later."""
    pub = publish_at.astimezone(timezone.utc).replace(tzinfo=None).isoformat() \
        if publish_at is not None else None
    with conn() as c:
        cur = c.execute(
            """INSERT INTO uploads(clip_id,platform,external_id,url,created_at,publish_at)
               VALUES(?,?,?,?,?,?)""",
            (clip_id, platform, external_id, url, now(), pub),
        )
        return cur.lastrowid


# ── campaign helpers ──────────────────────────────────────────────
def save_campaign(cid: str, provider: str, title: str, data: dict) -> None:
    with conn() as c:
        c.execute(
            """INSERT INTO campaigns(id,provider,title,data_json,seen_at)
               VALUES(?,?,?,?,?)
               ON CONFLICT(id) DO UPDATE SET title=excluded.title,
                 data_json=excluded.data_json, seen_at=excluded.seen_at""",
            (cid, provider, title, json.dumps(data), now()))


def get_campaign(cid: str) -> dict | None:
    with conn() as c:
        r = c.execute("SELECT data_json FROM campaigns WHERE id=?", (cid,)).fetchone()
    return json.loads(r[0]) if r else None


def campaign_sources(cid: str) -> set:
    """Source URLs already pulled for a campaign."""
    with conn() as c:
        return {r[0] for r in c.execute(
            "SELECT url FROM sources WHERE campaign_id=?", (cid,)).fetchall()}


def campaign_uploads():
    """Every real (http) post of a clip cut from campaign footage, with the
    campaign it belongs to."""
    with conn() as c:
        return c.execute(
            """SELECT u.id AS upload_id, u.clip_id, u.platform, u.url,
                      u.created_at, u.publish_at, s.campaign_id
               FROM uploads u JOIN clips k ON k.id=u.clip_id
               JOIN sources s ON s.id=k.source_id
               WHERE s.campaign_id IS NOT NULL AND u.url LIKE 'http%'""").fetchall()


def campaign_for_clip(clip_id) -> str | None:
    with conn() as c:
        r = c.execute("""SELECT s.campaign_id FROM clips k JOIN sources s
                         ON s.id=k.source_id WHERE k.id=?""", (clip_id,)).fetchone()
    return r[0] if r else None


def add_submission(campaign_id, clip_id, upload_id, post_url, status,
                   external_id=None, detail="") -> int | None:
    """Insert once per (campaign, post URL); returns None if it already exists."""
    with conn() as c:
        cur = c.execute(
            """INSERT OR IGNORE INTO campaign_submissions(campaign_id,clip_id,
                 upload_id,post_url,status,external_id,detail,created_at,updated_at)
               VALUES(?,?,?,?,?,?,?,?,?)""",
            (campaign_id, clip_id, upload_id, post_url, status, external_id,
             detail, now(), now()))
        return cur.lastrowid if cur.rowcount else None


def update_submission(sub_id, status, external_id=None, detail=None) -> None:
    with conn() as c:
        c.execute(
            """UPDATE campaign_submissions SET status=?,
                 external_id=COALESCE(?,external_id), detail=COALESCE(?,detail),
                 updated_at=? WHERE id=?""",
            (status, external_id, detail, now(), sub_id))


def submissions(status: str | None = None):
    with conn() as c:
        if status:
            return c.execute("SELECT * FROM campaign_submissions WHERE status=? "
                             "ORDER BY id", (status,)).fetchall()
        return c.execute("SELECT * FROM campaign_submissions ORDER BY id").fetchall()


def submitted_count_since(iso: str) -> int:
    """Submissions actually sent to a platform since `iso` (for daily caps)."""
    with conn() as c:
        return c.execute(
            """SELECT COUNT(*) FROM campaign_submissions WHERE updated_at>=?
               AND status IN ('submitted','approved','denied')""",
            (iso,)).fetchone()[0]
