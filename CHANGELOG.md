# Changelog

## v2.0 — 2026-07-11
**Two new agents — it's now a 10-agent team:**
- **Finishing Editor (Agent 8)** — QA reviewer that WATCHES every finished render
  before it can post: catches captions covering the speaker's face, black/frozen
  frames, dead-air gaps, clipped or too-quiet audio, wrong duration. Auto-fixes
  quiet mixes; blocks broken clips (with automatic backfill so your schedule
  never thins out). Optional film-grain finishing pass.
- **Compiler (Agent 9 → #10)** — the showrunner: builds a weekly 16:9 long-form
  episode from the week's best moments with an AI-narrator editorial spine
  (thesis → per-clip setup/analysis → verdict). Monetization-safe by construction
  for 2026 reused-content rules (commentary share measured on the finished cut;
  under-floor episodes are held back). Scheduled server-side.

**Editor upgrades:**
- Scene-aware cuts (`editor.scene_cuts`): cuts on the source's REAL camera
  changes (PySceneDetect), not just sentence boundaries.
- Optional animated kinetic-typography intro cards via Remotion
  (`editor.intro_card` — remotion/ project source included; needs Node 18+).
- Captions position "lower" default (clears faces better than center).

**Smarter clip selection:**
- Battle-tested selection brief: drama/stakes-first picking, the 3-second test,
  "start where the drama lands" timestamping, loop-back endings for rewatches.
- Relaxed-brief fallback: a strict pass that finds nothing re-runs loosened, so
  an episode never silently yields zero clips (plus a phone alert if it does).
- `produce` renders-to-target with QA backfill: a blocked clip's slot is refilled
  with the next-best candidate automatically.

**Reliability:**
- Startup catch-up guard: a missed 6AM run is retried at boot without ever
  double-booking a day that's already scheduled.
- All temp/scratch routed to the project drive (no more filling up C:).
- opencv pinned <5 (v5 removed the face-tracking API); scenedetect added.

## v1.1 — 2026-07-06
- **Headless API** for scripting/integration — call the agents directly without
  the interactive prompts:
  - `finder.find_candidates(source_url, max_clips, niche)` → scored clips
  - `editor.edit_clip_range(source_url, start_s, end_s, style)` → finished MP4
    (`style`: `bold-captions` | `minimal` | `podcast-frame`)
  - `uploader.build_post_package(transcript_excerpt, platforms, niche)` → per-platform copy
- More robust post-copy generation (auto-retries if the model returns empty).

## v1.0 — 2026-07-06
First public release.

- 8 cooperating agents: Finder, Editor, Uploader, Manager, Trend Scout,
  Planner, Community, Trainer
- 24 expert skill playbooks loaded into agent prompts
- Provider-switchable LLM layer with automatic fallback
  (OpenRouter / Groq / Gemini / NVIDIA / Ollama / Anthropic / OpenAI)
- Interactive setup wizard (`python run.py setup`)
- One-click installers: `install.bat` (Windows), `install.sh` (macOS/Linux)
- `factory` shortcut command (no venv activation needed)
- Pro editing: filler-word trimming, face tracking, animated captions,
  punch zooms, b-roll, color grade, AI voiceover, auto thumbnails
- YouTube Shorts auto-upload + server-side day scheduling;
  TikTok / Reels export-for-scheduler
- Fully hands-free `daily` mode with performance learning loop
