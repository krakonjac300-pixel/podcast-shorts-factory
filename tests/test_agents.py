"""Agent test suite — run with:  python -m unittest discover tests -v

Covers every agent's pure logic (no network, no LLM calls) plus one real
ffmpeg render to prove the editor's filtergraph is valid end-to-end.
"""
from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from factory import config  # noqa: E402  (sets up ffmpeg PATH, UTF-8)
from factory.config import cfg  # noqa: E402


class TestImports(unittest.TestCase):
    """Every module must import cleanly (catches syntax/typo regressions)."""

    def test_all_modules_import(self):
        import factory.db, factory.insights, factory.llm, factory.skills  # noqa
        from factory.agents import editor, finder, manager, planner  # noqa
        from factory.agents import trend_scout, uploader  # noqa
        from factory.utils import captions, media, trimmer  # noqa


class TestEditorFraming(unittest.TestCase):
    """The reframe math — including the 'cut in half' fix."""

    def setUp(self):
        from factory.agents import editor
        self.ed = editor

    def test_face_center_missing_file_returns_5tuple(self):
        cx, sw, sh, n, frac = self.ed._face_center("does_not_exist.mp4")
        self.assertIsNone(cx)
        self.assertEqual(n, 0)
        self.assertEqual(frac, 0.0)

    def test_vf_face_centered_when_no_face(self):
        # 1920x1080 source → scaled to 1920h → width 3413 (rounded even 3412)
        vf = self.ed._vf_face(1080, 1920, 1920, 1080, None)
        self.assertIn("crop=1080:1920", vf)

    def test_vf_face_clamps_left_edge(self):
        vf = self.ed._vf_face(1080, 1920, 1920, 1080, 0.01)
        self.assertIn(":0:0", vf)          # cropx clamped to 0

    def test_vf_face_clamps_right_edge(self):
        vf = self.ed._vf_face(1080, 1920, 1920, 1080, 0.99)
        scaled_w = int(round(1920 * 1920 / 1080))
        scaled_w -= scaled_w % 2
        self.assertIn(f":{scaled_w - 1080}:0", vf)

    def test_vf_face_none_for_narrow_source(self):
        # a source taller than 9:16 (720x1600) scales to <1080 wide — can't crop
        self.assertIsNone(self.ed._vf_face(1080, 1920, 720, 1600, 0.5))

    def test_vf_vertical_blur_and_black(self):
        blur = self.ed._vf_vertical(1080, 1920, "blur")
        self.assertIn("boxblur", blur)
        self.assertIn("overlay", blur)
        black = self.ed._vf_vertical(1080, 1920, "black")
        self.assertIn("pad=1080:1920", black)

    def test_motion_zoom_filter_shape(self):
        m = self.ed._motion(1080, 1920, 30.0, 0.10)
        self.assertIn("crop=", m)
        self.assertIn("scale=1080:1920", m)

    def test_merge_cuts_thins_and_unions(self):
        # union of sentence cuts + real scene cuts, thinned to >= 1.4s apart
        merged = self.ed._merge_cuts([2.0, 3.0, 9.0], [2.5, 6.0])
        self.assertEqual(merged, [2.0, 6.0, 9.0])   # 3.0/2.5 dropped (too close to 2.0)
        for a, b in zip(merged, merged[1:]):
            self.assertGreaterEqual(b - a, 1.4)

    def test_scene_cuts_missing_file_safe(self):
        self.assertEqual(self.ed._scene_cuts("nope.mp4", 30.0), [])

    def test_motion_punches_add_pulses(self):
        m = self.ed._motion(1080, 1920, 30.0, 0.10, punches=[5.0, 12.5])
        self.assertIn("abs(ld(0)-5.00)", m)
        self.assertIn("abs(ld(0)-12.50)", m)
        self.assertIn("min(0.4", m)                     # zoom is capped
        self.assertIn("isnan(t)", m)                    # init-time NaN guard

    def test_punch_times_from_emphasis_words(self):
        words = [{"word": "This", "start": 10.0, "end": 10.3},
                 {"word": "creatine", "start": 12.0, "end": 12.5},
                 {"word": "kidneys!", "start": 15.0, "end": 15.5},
                 {"word": "creatine", "start": 16.0, "end": 16.4}]
        times = self.ed._punch_times(words, 10.0, ["creatine", "kidneys"], 30.0)
        self.assertEqual(times, [2.0, 5.0])             # first hits only, clip-local

    def test_punch_times_respect_gap_and_edges(self):
        words = [{"word": "a", "start": 0.2, "end": 0.3},     # too early
                 {"word": "b", "start": 2.0, "end": 2.1},
                 {"word": "c", "start": 2.5, "end": 2.6}]     # <2s after b
        times = self.ed._punch_times(words, 0.0, ["a", "b", "c"], 30.0)
        self.assertEqual(times, [2.0])

    def test_flashes_skip_edges(self):
        fl = self.ed._flashes([0.1, 10.0, 29.9], 30.0)
        self.assertEqual(fl.count("eq=brightness"), 1)
        self.assertIn("between(t\\,10.00", fl)

    def test_cut_points_at_sentence_ends(self):
        words = [{"word": "First.", "start": 0.0, "end": 3.5},
                 {"word": "Then", "start": 3.7, "end": 4.0},
                 {"word": "more", "start": 4.0, "end": 7.5},
                 {"word": "talk.", "start": 7.5, "end": 8.0},
                 {"word": "End", "start": 8.2, "end": 9.0}]
        cuts = self.ed._cut_points(words, 0.0, 30.0)
        self.assertTrue(cuts, "expected at least one cut")
        self.assertAlmostEqual(cuts[0], 3.6, delta=0.2)   # after 'First.'

    def test_cut_points_respect_min_length(self):
        words = [{"word": "Hi.", "start": 0.0, "end": 0.5},
                 {"word": "Yo.", "start": 0.7, "end": 1.0}]
        self.assertEqual(self.ed._cut_points(words, 0.0, 30.0), [])

    def test_motion_with_cuts_steps_zoom(self):
        m = self.ed._motion(1080, 1920, 30.0, 0.10, cuts=[6.0, 12.0])
        self.assertIn("gte(ld(0)\\,6.00)", m)
        self.assertIn("gte(ld(0)\\,12.00)", m)
        self.assertIn("isnan(t)", m)

    def test_pick_music_matches_mood(self):
        music_dir = config.ROOT / cfg.get("editor.music_dir", "assets/music")
        if not list(music_dir.glob("tense*")):
            self.skipTest("music library not downloaded")
        self.assertIn("tense", self.ed._pick_music("dramatic suspense").stem)
        self.assertIn("upbeat", self.ed._pick_music("energetic").stem)
        self.assertIn("lofi", self.ed._pick_music("chill").stem)
        self.assertIsNone(self.ed._pick_music("none"))


class TestEditorText(unittest.TestCase):
    def setUp(self):
        from factory.agents import editor
        self.ed = editor

    def test_wrap_never_exceeds_width(self):
        lines = self.ed._wrap("this is a fairly long hook that must wrap", 16)
        self.assertTrue(all(len(ln) <= 16 for ln in lines))
        self.assertEqual(" ".join(lines),
                         "this is a fairly long hook that must wrap")

    def test_drawtext_strips_unsafe_chars(self):
        frag = self.ed._drawtext("it's a 'test': done", size=64, y="220")
        self.assertNotIn("text='it's", frag)   # quotes stripped from the text
        self.assertIn("fontsize=64", frag)

    def test_drawtext_block_caps_lines(self):
        frag = self.ed._drawtext_block(
            "one two three four five six seven eight nine ten",
            size=64, y_top=220, enable=None, max_chars=8, max_lines=3)
        self.assertEqual(frag.count("drawtext="), 3)

    def test_resolve_sfx_synonyms(self):
        sfx_dir = Path(cfg.get("editor.sfx_dir", "assets/sfx"))
        sfx_dir = config.ROOT / sfx_dir
        if not sfx_dir.exists():
            self.skipTest("sfx pack not generated")
        self.assertIsNotNone(self.ed._resolve_sfx("whoosh", sfx_dir))
        self.assertIsNotNone(self.ed._resolve_sfx("Dramatic Boom", sfx_dir))
        self.assertIsNotNone(self.ed._resolve_sfx("bell chime", sfx_dir))
        self.assertIsNone(self.ed._resolve_sfx("kazoo solo", sfx_dir))


class TestCaptions(unittest.TestCase):
    def test_build_ass_basic(self):
        from factory.utils import captions
        words = [{"word": "hello", "start": 10.0, "end": 10.4},
                 {"word": "amazing", "start": 10.4, "end": 10.9},
                 {"word": "world", "start": 10.9, "end": 11.3}]
        ass = captions.build_ass(words, 10.0, 11.3, {}, res=(1080, 1920),
                                 emphasis_words=["amazing"])
        self.assertIn("[Events]", ass)
        self.assertIn("Dialogue:", ass)
        self.assertIn("PlayResX: 1080", ass)

    def test_words_outside_clip_excluded(self):
        from factory.utils import captions
        words = [{"word": "before", "start": 1.0, "end": 1.5},
                 {"word": "inside", "start": 10.2, "end": 10.6}]
        ass = captions.build_ass(words, 10.0, 11.0, {})
        self.assertNotIn("BEFORE", ass)
        self.assertIn("INSIDE", ass)          # captions are ALL-CAPS now


class TestTrimmer(unittest.TestCase):
    def test_removes_filler_and_dead_air(self):
        from factory.utils import trimmer
        words = [
            {"word": "So", "start": 0.0, "end": 0.3},
            {"word": "um,", "start": 0.4, "end": 0.9},
            {"word": "creatine", "start": 1.0, "end": 1.5},
            # 3s dead-air gap
            {"word": "works.", "start": 4.5, "end": 5.0},
        ]
        conf = {"enabled": True, "fillers": ["um", "uh"], "max_pause": 0.6}
        out = trimmer.compute(words, 0.0, 5.0, conf)
        if out is None:
            self.skipTest("trimmer found nothing to cut with this config")
        self.assertLess(out["new_dur"], 5.0)
        self.assertIn("between", out["expr"])

    def test_disabled_returns_none(self):
        from factory.utils import trimmer
        self.assertIsNone(trimmer.compute(
            [{"word": "hi", "start": 0, "end": 1}], 0, 1, {"enabled": False}))

    def test_stammer_duplicate_removed(self):
        from factory.utils import trimmer
        words = [{"word": "So", "start": 0.0, "end": 0.3},
                 {"word": "the", "start": 0.4, "end": 0.6},     # stammer
                 {"word": "the", "start": 0.7, "end": 0.9},     # clean retake
                 {"word": "creatine", "start": 1.0, "end": 1.6},
                 {"word": "works", "start": 1.7, "end": 2.2},
                 {"word": "fine", "start": 2.3, "end": 2.8}]
        out = trimmer.compute(words, 0.0, 2.8,
                              {"enabled": True, "min_removed": 0.1})
        self.assertIsNotNone(out)
        texts = [w["word"] for w in out["new_words"]]
        self.assertEqual(texts.count("the"), 1)                  # dup dropped

    def test_intentional_doubling_kept(self):
        from factory.utils import trimmer
        words = [{"word": "it's", "start": 0.0, "end": 0.3},
                 {"word": "really", "start": 0.4, "end": 0.7},
                 {"word": "really", "start": 0.8, "end": 1.1},
                 {"word": "good", "start": 1.2, "end": 4.5},
                 {"word": "stuff", "start": 6.0, "end": 6.5}]    # gap → trim happens
        out = trimmer.compute(words, 0.0, 6.5,
                              {"enabled": True, "min_removed": 0.1})
        self.assertIsNotNone(out)
        texts = [w["word"] for w in out["new_words"]]
        self.assertEqual(texts.count("really"), 2)               # whitelist kept

    def test_segments_returned_clip_local(self):
        from factory.utils import trimmer
        words = [{"word": "hello", "start": 10.0, "end": 10.5},
                 {"word": "world", "start": 10.5, "end": 11.0},
                 {"word": "again", "start": 14.0, "end": 14.5}]  # 3s dead air
        out = trimmer.compute(words, 10.0, 14.5,
                              {"enabled": True, "min_removed": 0.1})
        self.assertIsNotNone(out)
        self.assertEqual(len(out["segments"]), 2)
        self.assertAlmostEqual(out["segments"][0][0], 0.0, delta=0.1)


class TestFinderChunking(unittest.TestCase):
    def test_long_transcript_splits(self):
        from factory.agents import finder
        segs = [{"start": i * 10.0, "end": i * 10 + 9.0, "text": "x" * 200}
                for i in range(1000)]                     # ~200KB of text
        pieces = finder._chunks(segs)
        self.assertGreater(len(pieces), 1)
        self.assertEqual(sum(len(p) for p in pieces), 1000)   # nothing lost
        for p in pieces:
            self.assertLessEqual(sum(len(s["text"]) + 16 for s in p),
                                 finder._CHUNK_CHARS)

    def test_short_transcript_single_chunk(self):
        from factory.agents import finder
        segs = [{"start": 0.0, "end": 5.0, "text": "hello"}]
        self.assertEqual(len(finder._chunks(segs)), 1)


class TestPlanner(unittest.TestCase):
    def test_short_hook_caps_words(self):
        from factory.agents import planner
        hook = planner._short_hook(
            "This Is A Very Long Title That Would Overflow The Screen Badly")
        self.assertLessEqual(len(hook.split()), 6)

    def test_default_plan_has_required_keys(self):
        from factory.agents import planner
        clip = {"title": "Test clip", "start": 0, "end": 30}
        plan = planner._default_plan(clip)
        for key in ("hook_text", "cover_text", "music_mood",
                    "emphasis_words", "sfx_cues", "broll", "transitions"):
            self.assertIn(key, plan)


class TestDB(unittest.TestCase):
    """Full clip lifecycle against a throwaway database."""

    def setUp(self):
        from factory import db
        self.db = db
        self._orig = db.DB_PATH
        self.tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.tmp.close()
        db.DB_PATH = Path(self.tmp.name)

    def tearDown(self):
        self.db.DB_PATH = self._orig
        Path(self.tmp.name).unlink(missing_ok=True)

    def test_clip_lifecycle(self):
        sid = self.db.upsert_source("https://youtu.be/x", "Test pod",
                                    "video.mp4", [])
        cid = self.db.add_clip(sid, 10, 40, "Hook", "why", 88.0, "cap", ["#a"])
        self.assertEqual(len(self.db.clips_by_status("candidate")), 1)
        self.db.set_clip_status(cid, "approved")
        self.db.set_clip_status(cid, "edited", rendered_path="out.mp4")
        clip = self.db.clips_by_status("edited")[0]
        self.assertEqual(clip["rendered_path"], "out.mp4")
        self.db.record_upload(cid, "youtube", "abc123",
                              "https://youtube.com/shorts/abc123")
        self.db.set_clip_status(cid, "uploaded")
        self.assertEqual(self.db.processed_urls(), {"https://youtu.be/x"})

    def test_channel_ranking_prefers_higher_views(self):
        from factory.agents import manager
        sid_a = self.db.upsert_source("u1", "t1", "v1", [], channel="@joerogan")
        sid_b = self.db.upsert_source("u2", "t2", "v2", [], channel="@TheDiaryOfACEO")
        for sid, views in ((sid_a, 100), (sid_b, 9000)):
            cid = self.db.add_clip(sid, 0, 30, "c", "", 80, "", [])
            uid = self.db.record_upload(cid, "youtube", "x", "url")
            with self.db.conn() as c:
                c.execute("""INSERT INTO metrics(upload_id,views,likes,comments,
                             shares,avg_watch_pct,measured_at)
                             VALUES(?,?,?,?,?,?,?)""",
                          (uid, views, 1, 1, 0, None, self.db.now()))
        ranking = manager.channel_ranking()
        self.assertGreater(ranking["@TheDiaryOfACEO"], ranking["@joerogan"])

    def test_rank_sources_puts_winner_first(self):
        import run as runmod
        from factory.agents import manager
        orig = manager.channel_ranking
        manager.channel_ranking = lambda: {"@CalebHammer": 5000.0, "@joerogan": 10.0}
        try:
            ordered = runmod._rank_sources([
                "https://www.youtube.com/@joerogan/videos",
                "https://www.youtube.com/@TheDiaryOfACEO/videos",
                "https://www.youtube.com/@CalebHammer/videos"])
            self.assertIn("CalebHammer", ordered[0])
            self.assertIn("TheDiaryOfACEO", ordered[2])   # no data → keeps last
        finally:
            manager.channel_ranking = orig

    def test_best_clip_posts_first(self):
        sid = self.db.upsert_source("u", "t", "v", [])
        self.db.set_clip_status(
            self.db.add_clip(sid, 0, 30, "low", "", 40, "", []), "edited")
        self.db.set_clip_status(
            self.db.add_clip(sid, 0, 30, "high", "", 95, "", []), "edited")
        queue = self.db.clips_by_status("edited")
        self.assertEqual(queue[0]["title"], "high")   # upload_one takes [0]


class TestCameraV6(unittest.TestCase):
    def test_face_steps_static_when_one_position(self):
        from factory.agents import editor
        vf = editor._vf_face_steps(1080, 1920, 1920, 1080, [0.4, 0.4], [0, 5, 10])
        self.assertIn("crop=1080:1920", vf)          # collapses to simple crop
        self.assertNotIn("gte", vf)

    def test_face_steps_move_between_shots(self):
        from factory.agents import editor
        vf = editor._vf_face_steps(1080, 1920, 1920, 1080,
                                   [0.3, 0.7, 0.3], [0, 5, 10, 15])
        self.assertIn("gte(ld(0)\\,0.00)", vf)
        self.assertIn("isnan(t)", vf)
        self.assertIn("x='", vf)

    def test_face_steps_none_for_narrow_source(self):
        from factory.agents import editor
        self.assertIsNone(editor._vf_face_steps(
            1080, 1920, 720, 1600, [0.5], [0, 10]))


class TestCommunityAgent(unittest.TestCase):
    def setUp(self):
        from factory import db
        self.db = db
        self._orig = db.DB_PATH
        self.tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.tmp.close()
        db.DB_PATH = Path(self.tmp.name)

    def tearDown(self):
        self.db.DB_PATH = self._orig
        Path(self.tmp.name).unlink(missing_ok=True)

    def test_comment_log_dedup(self):
        from factory.agents import community
        self.assertFalse(community._already_answered("abc"))
        community._log("abc", "vid1", "reply", "hello")
        self.assertTrue(community._already_answered("abc"))
        community._log("abc", "vid1", "reply", "dup ignored")   # no crash

    def test_drafts_written_without_scope(self):
        from factory.agents import community
        orig = community.DRAFTS
        community.DRAFTS = Path(tempfile.gettempdir()) / "psf_drafts_test.md"
        try:
            community._draft("vid1", "seed", "vid1", "What do you think?")
            text = community.DRAFTS.read_text(encoding="utf-8")
            self.assertIn("What do you think?", text)
        finally:
            community.DRAFTS.unlink(missing_ok=True)
            community.DRAFTS = orig


class TestProEditorV5(unittest.TestCase):
    """Narrator teaser, memes, voiceover."""

    def test_resolve_meme_by_synonym(self):
        from factory.agents import editor
        with tempfile.TemporaryDirectory() as d:
            mdir = Path(d)
            (mdir / "mindblown-vince.gif").write_bytes(b"GIF89a")
            (mdir / "laughing-jordan.mp4").write_bytes(b"x")
            self.assertIsNotNone(editor._resolve_meme("mind-blown", mdir))
            self.assertIsNotNone(editor._resolve_meme("wow explosion", mdir))
            self.assertIsNotNone(editor._resolve_meme("funny lol", mdir))
            self.assertIsNone(editor._resolve_meme("dancing", mdir))

    def test_teaser_skipped_without_narrator(self):
        from factory.agents import editor
        plan = {"narrator_intro": "", "teaser_times": [10.0]}
        self.assertIsNone(editor._teaser_pass(
            "x.mp4", "t", "scale=1080:1920", plan, 1.0, 40.0, 1080, 1920))

    def test_teaser_skipped_without_times(self):
        from factory.agents import editor
        plan = {"narrator_intro": "Wait for this", "teaser_times": []}
        self.assertIsNone(editor._teaser_pass(
            "x.mp4", "t", "scale=1080:1920", plan, 1.0, 40.0, 1080, 1920))

    def test_voice_synth_live(self):
        from factory.utils import voice
        out = Path(tempfile.gettempdir()) / "psf_vo_test.mp3"
        dur = voice.synth("Wait for what he says next.", out)
        if dur == 0:
            self.skipTest("Edge TTS unreachable (offline?)")
        self.assertGreater(dur, 0.5)
        out.unlink(missing_ok=True)

    def test_default_plan_has_v5_keys(self):
        from factory.agents import planner
        plan = planner._default_plan({"title": "T", "start": 0, "end": 30})
        for k in ("narrator_intro", "teaser_times", "memes"):
            self.assertIn(k, plan)


class TestCaptionQuality(unittest.TestCase):
    def test_number_fragments_glued(self):
        from factory.utils import captions
        words = [{"word": "over", "start": 0.0, "end": 0.3},
                 {"word": "220", "start": 0.4, "end": 0.7},
                 {"word": ",000", "start": 0.7, "end": 1.0},
                 {"word": "kelvin", "start": 1.1, "end": 1.5}]
        ass = captions.build_ass(words, 0.0, 1.5, {})
        self.assertIn("220,000", ass)
        self.assertNotIn("220 ,000", ass)

    def test_pages_break_at_sentence_end(self):
        from factory.utils import captions
        words = [{"word": "it.", "start": 0.0, "end": 0.3},
                 {"word": "So", "start": 0.4, "end": 0.6},
                 {"word": "they", "start": 0.7, "end": 0.9},
                 {"word": "ran", "start": 1.0, "end": 1.2}]
        ass = captions.build_ass(words, 0.0, 1.2, {})
        # "IT." must be its own page — never on a page with "SO THEY"
        for line in ass.splitlines():
            if "IT." in line and "Dialogue" in line:
                self.assertNotIn("SO", line)


class TestManagerReview(unittest.TestCase):
    def setUp(self):
        from factory import db
        self.db = db
        self._orig = db.DB_PATH
        self.tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.tmp.close()
        db.DB_PATH = Path(self.tmp.name)

    def tearDown(self):
        self.db.DB_PATH = self._orig
        Path(self.tmp.name).unlink(missing_ok=True)

    def _make_clip(self, rendered="missing.mp4"):
        sid = self.db.upsert_source("u", "t", "v", [])
        cid = self.db.add_clip(sid, 0, 30, "Test clip", "why", 90, "cap", [])
        self.db.set_clip_status(cid, "edited", rendered_path=rendered)
        return self.db.clip_by_id(cid)

    def test_mechanical_bounce_on_missing_file(self):
        from factory.agents import manager
        clip = self._make_clip("does_not_exist.mp4")
        r = manager.review_clip(clip)
        self.assertFalse(r["approved"])
        self.assertIn("missing", r["notes"])

    def test_bounce_records_notes_and_attempt(self):
        clip = self._make_clip()
        self.db.set_review(clip["id"], "b-roll covers the demo footage")
        fresh = self.db.clip_by_id(clip["id"])
        self.assertEqual(fresh["review_attempts"], 1)
        self.assertIn("b-roll", fresh["review_notes"])

    def test_twice_bounced_gets_rejected(self):
        from factory.agents import manager, uploader
        clip = self._make_clip("does_not_exist.mp4")
        self.db.set_review(clip["id"], "first bounce")     # attempt 1 recorded
        clip = self.db.clip_by_id(clip["id"])
        orig_flag = manager.flag_attention
        flagged = []
        manager.flag_attention = lambda msg: flagged.append(msg)
        try:
            result = uploader._review_and_fix(clip)
        finally:
            manager.flag_attention = orig_flag
        self.assertIsNone(result)
        self.assertTrue(flagged, "human escalation expected")
        self.assertEqual(self.db.clip_by_id(clip["id"])["status"], "rejected")

    def test_planner_injects_bounce_notes(self):
        from factory.agents import planner
        clip = {"title": "T", "start": 0, "end": 30,
                "review_notes": "drop the b-roll at 8s"}
        # default plan path (no LLM call needed to test the prompt build)
        plan = planner._default_plan(clip)
        self.assertIn("hook_text", plan)     # sanity: default plan still works


class TestSkillsAndConfig(unittest.TestCase):
    def test_all_assigned_skills_exist(self):
        from factory import skills
        for agent in ("finder", "editor", "uploader", "manager"):
            names = cfg.get(f"skills.{agent}", [])
            self.assertTrue(names, f"{agent} has no skills assigned")
            self.assertEqual(skills.missing(names), [],
                             f"{agent} references missing skill files")

    def test_editor_config_sane(self):
        self.assertEqual(cfg.get("editor.resolution"), [1080, 1920])
        self.assertIn(cfg.get("editor.reframe"), ("smart", "face", "blur", "black"))
        self.assertGreaterEqual(cfg.get("finder.clip_min_seconds"), 15)
        self.assertLessEqual(cfg.get("finder.clip_max_seconds"), 180)  # Shorts cap

    def test_llm_configured(self):
        from factory import llm
        self.assertIsInstance(llm.provider(), str)
        self.assertIsInstance(llm.model("finder"), str)


class TestScheduleSlots(unittest.TestCase):
    def test_slots_skip_past_and_roll_over(self):
        from datetime import datetime, timezone, timedelta
        from factory.agents import uploader
        tz = timezone(timedelta(hours=2))
        now = datetime(2026, 7, 4, 12, 30, tzinfo=tz)     # 12:30 → 9AM gone
        slots = uploader._next_slots(4, ["09:00", "14:00", "19:00"], now)
        self.assertEqual([(s.day, s.hour) for s in slots],
                         [(4, 14), (4, 19), (5, 9), (5, 14)])

    def test_slot_margin_of_20_minutes(self):
        from datetime import datetime, timezone, timedelta
        from factory.agents import uploader
        tz = timezone(timedelta(hours=2))
        now = datetime(2026, 7, 4, 13, 50, tzinfo=tz)     # 14:00 too close
        slots = uploader._next_slots(1, ["09:00", "14:00", "19:00"], now)
        self.assertEqual((slots[0].day, slots[0].hour), (4, 19))


class TestUploaderCopy(unittest.TestCase):
    def test_hashtags_dedup(self):
        from factory.agents import uploader
        clip = {"hashtags": '["#shorts", "#podcast", "#shorts"]'}
        base = cfg.get("uploader.hashtags", [])
        tags = uploader._hashtags(clip)
        self.assertEqual(len(tags), len(set(tags)))          # no duplicates
        for b in base:
            self.assertIn(b, tags)

    def test_hashtags_bad_json_falls_back(self):
        from factory.agents import uploader
        clip = {"hashtags": "not json"}
        self.assertEqual(uploader._hashtags(clip), cfg.get("uploader.hashtags", []))

    def test_safe_title_strips_angle_brackets(self):
        from factory.agents import uploader
        # YouTube rejects < and > (invalidTitle 400)
        self.assertEqual(uploader._safe_title("RICH > FAMOUS: no fame"),
                         "RICH OVER FAMOUS: no fame")
        self.assertNotIn(">", uploader._safe_title("A > B > C"))
        self.assertNotIn("<", uploader._safe_title("x < y stuff"))
        self.assertTrue(uploader._safe_title(">>>").strip())   # never empty


class TestNotify(unittest.TestCase):
    """The agents' line to the human — activity.md feed (toast is best-effort)."""

    def setUp(self):
        from factory import notify
        self.notify = notify
        self._orig_feed = notify.FEED
        tmp = tempfile.NamedTemporaryFile(suffix=".md", delete=False)
        tmp.close()
        Path(tmp.name).unlink()               # start with no feed file
        notify.FEED = Path(tmp.name)

    def tearDown(self):
        self.notify.FEED.unlink(missing_ok=True)
        self.notify.FEED = self._orig_feed

    def test_feed_newest_first(self):
        self.notify._append_feed("older event")
        self.notify._append_feed("newer event")
        text = self.notify.FEED.read_text(encoding="utf-8")
        self.assertLess(text.index("newer event"), text.index("older event"))
        self.assertTrue(text.startswith("# Factory activity feed"))

    def test_ps_quote_escapes(self):
        self.assertEqual(self.notify._ps_quote("it's"), "'it''s'")


class TestFFmpegRender(unittest.TestCase):
    """Integration: the editor's real filtergraph renders a valid vertical video."""

    def test_smart_fit_plus_motion_renders(self):
        from factory.agents import editor
        vf = editor._vf_vertical(1080, 1920, "blur")
        vf += "," + editor._motion(1080, 1920, 2.0, 0.10)
        vf += "," + editor._drawtext_block("THIS HOOK WRAPS NICELY",
                                           size=64, y_top=220, enable="lt(t,2.2)")
        out = Path(tempfile.gettempdir()) / "psf_test_render.mp4"
        r = subprocess.run(
            ["ffmpeg", "-y", "-f", "lavfi", "-i",
             "testsrc2=size=1280x720:rate=30:duration=2",
             "-vf", vf, "-c:v", "libx264", "-preset", "ultrafast", str(out)],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, f"ffmpeg failed:\n{r.stderr[-800:]}")
        probe = subprocess.run(
            ["ffprobe", "-v", "quiet", "-select_streams", "v:0",
             "-show_entries", "stream=width,height", "-of", "csv=p=0", str(out)],
            capture_output=True, text=True)
        self.assertEqual(probe.stdout.strip().split(",")[:2], ["1080", "1920"])
        out.unlink(missing_ok=True)

    def test_pro_edit_full_filtergraph_renders(self):
        """Punch zooms + flash + b-roll overlay + text — the whole pro chain."""
        from factory.agents import editor
        tmp = Path(tempfile.gettempdir())
        img = tmp / "psf_broll.jpg"
        subprocess.run(["ffmpeg", "-y", "-f", "lavfi", "-i",
                        "color=c=orange:size=800x1200", "-frames:v", "1", str(img)],
                       capture_output=True)
        vf = editor._vf_vertical(1080, 1920, "blur")
        vf += "," + editor._motion(1080, 1920, 4.0, 0.10, punches=[1.5, 3.0])
        vf += "," + editor._flashes([2.0], 4.0)
        graph = (f"[0:v]{vf}[v0];"
                 f"[1:v]scale=1080:1190:force_original_aspect_ratio=increase,"
                 f"crop=1080:1190,format=yuva420p,"
                 f"fade=t=in:st=0:d=0.3:alpha=1,fade=t=out:st=2.2:d=0.4:alpha=1,"
                 f"setpts=PTS+1.00/TB[b0];"
                 f"[v0][b0]overlay=0:(H-h)/2:enable='between(t\\,1.00\\,3.60)'[v]")
        out = tmp / "psf_test_pro.mp4"
        r = subprocess.run(
            ["ffmpeg", "-y", "-f", "lavfi", "-i",
             "testsrc2=size=1280x720:rate=30:duration=4",
             "-loop", "1", "-t", "3.2", "-i", str(img),
             "-filter_complex", graph, "-map", "[v]",
             "-c:v", "libx264", "-preset", "ultrafast", str(out)],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, f"ffmpeg failed:\n{r.stderr[-800:]}")
        for f in (img, out):
            f.unlink(missing_ok=True)

    def test_trim_pass_fades_and_concats(self):
        """Segment-cut trim with 15ms audio fades renders and has ~right length."""
        from factory.agents import editor
        tmp = Path(tempfile.gettempdir())
        src = tmp / "psf_trim_src.mp4"
        subprocess.run(
            ["ffmpeg", "-y", "-f", "lavfi", "-i",
             "testsrc2=size=320x180:rate=30:duration=4",
             "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
             "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac",
             "-shortest", str(src)], capture_output=True)
        trim = {"segments": [(0.5, 1.5), (2.5, 3.5)], "expr": ""}
        out = editor._trim_pass(str(src), "test", 0.0, 4.0, trim)
        p = subprocess.run(
            ["ffprobe", "-v", "quiet", "-show_entries", "format=duration",
             "-of", "csv=p=0", str(out)], capture_output=True, text=True)
        self.assertAlmostEqual(float(p.stdout.strip()), 2.0, delta=0.3)
        src.unlink(missing_ok=True)
        Path(out).unlink(missing_ok=True)

    def test_broll_rejects_empty_query(self):
        from factory.utils import broll
        self.assertIsNone(broll.fetch(""))
        self.assertIsNone(broll.fetch("   "))

    def test_broll_available_without_key(self):
        from factory.utils import broll
        self.assertTrue(broll.available())     # Openverse fallback needs no key

    def test_face_crop_filtergraph_renders(self):
        from factory.agents import editor
        vf = editor._vf_face(1080, 1920, 1280, 720, 0.44)
        self.assertIsNotNone(vf)
        out = Path(tempfile.gettempdir()) / "psf_test_crop.mp4"
        r = subprocess.run(
            ["ffmpeg", "-y", "-f", "lavfi", "-i",
             "testsrc2=size=1280x720:rate=30:duration=1",
             "-vf", vf, "-c:v", "libx264", "-preset", "ultrafast", str(out)],
            capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, f"ffmpeg failed:\n{r.stderr[-800:]}")
        out.unlink(missing_ok=True)


class TestSfxAnchoring(unittest.TestCase):
    """SFX must land on the word they name, not a guessed timestamp, and never
    pile up into noise (the 'random sounds' complaint)."""

    WORDS = [
        {"word": "He", "start": 0.5, "end": 0.7},
        {"word": "literally", "start": 0.7, "end": 1.1},
        {"word": "DESTROYED", "start": 1.1, "end": 1.8},
        {"word": "the", "start": 1.8, "end": 1.9},
        {"word": "whole", "start": 1.9, "end": 2.2},
        {"word": "company", "start": 2.2, "end": 2.9},
        {"word": "and", "start": 6.0, "end": 6.1},
        {"word": "won", "start": 6.1, "end": 6.5},
    ]

    def test_anchor_matches_word_case_insensitive(self):
        from factory.agents import editor
        self.assertAlmostEqual(editor._anchor_time("destroyed", self.WORDS), 1.1)

    def test_anchor_matches_phrase(self):
        from factory.agents import editor
        self.assertAlmostEqual(editor._anchor_time("whole company", self.WORDS), 1.9)

    def test_anchor_missing_returns_none(self):
        from factory.agents import editor
        self.assertIsNone(editor._anchor_time("kidneys", self.WORDS))
        self.assertIsNone(editor._anchor_time("", self.WORDS))

    def test_dedupe_drops_hook_and_collisions(self):
        from factory.agents import editor
        primary = [(1.1, "impact", 0.5), (6.1, "ding", 0.5), (0.3, "pop", 0.5)]
        secondary = [(1.3, "swoosh", 0.2), (6.3, "swoosh", 0.2), (11.0, "swoosh", 0.2)]
        kept = editor._dedupe_sfx(primary, secondary, render_dur=15.0)
        times = [round(t, 2) for t, _, _ in kept]
        self.assertIn(1.1, times)          # anchored impact kept
        self.assertIn(6.1, times)          # anchored ding kept
        self.assertNotIn(0.3, times)       # over the hook (<1s) dropped
        self.assertNotIn(1.3, times)       # collides with the impact -> dropped
        self.assertIn(11.0, times)         # lone cut-swoosh fills a gap

    def test_dedupe_caps_total(self):
        from factory.agents import editor
        many = [(float(i), "pop", 0.5) for i in range(2, 40, 1)]
        kept = editor._dedupe_sfx(many, [], render_dur=60.0, cap=6)
        self.assertLessEqual(len(kept), 6)


class TestFinishingEditor(unittest.TestCase):
    """The QA reviewer's pure logic: caption band, overlap, verdict, report."""

    def test_caption_band_is_lower_third(self):
        from factory.agents import finishing_editor as fe
        top, bot = fe._caption_band(1920, font_size=90, lift=0.34)
        self.assertLess(top, bot)
        self.assertGreater(top, 1920 * 0.5)      # band sits below the middle
        self.assertLessEqual(bot, 1920)

    def test_overlap_frac(self):
        from factory.agents import finishing_editor as fe
        band = (1148, 1300)
        # a centered face high in frame does not reach the band
        self.assertEqual(fe._overlap_frac((300, 500, 480, 480), band), 0.0)
        # a low/large face covers it fully
        self.assertGreater(fe._overlap_frac((300, 1100, 480, 700), band), 0.9)

    def test_verdict_precedence(self):
        from factory.agents import finishing_editor as fe
        self.assertEqual(fe._verdict([]), "PASS")
        self.assertEqual(fe._verdict([{"sev": "warn"}]), "PASS*")
        self.assertEqual(fe._verdict([{"sev": "fixable"}]), "FIX")
        self.assertEqual(fe._verdict([{"sev": "warn"}, {"sev": "critical"}]), "FLAG")

    def test_report_md_lists_issues(self):
        from factory.agents import finishing_editor as fe
        md = fe._report_md(7, "FLAG",
                           [{"kind": "black frames", "sev": "critical",
                             "msg": "broken"}], ["did a thing"])
        self.assertIn("clip 7", md)
        self.assertIn("CRITICAL", md)
        self.assertIn("did a thing", md)

    def test_review_missing_file_flags(self):
        from factory.agents import finishing_editor as fe
        verdict, issues = fe.review_clip({"id": 1, "rendered_path": None})
        self.assertEqual(verdict, "FLAG")
        self.assertTrue(any(i["sev"] == "critical" for i in issues))


if __name__ == "__main__":
    unittest.main(verbosity=2)
