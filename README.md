# Podcast Shorts Factory

Four cooperating agents that turn long podcasts into short-form videos, semi-automatically.

```
 ┌────────────┐   ┌────────────┐   ┌────────────┐   ┌────────────┐
 │ 1. FINDER  │──▶│ 2. EDITOR  │──▶│ 3. UPLOADER│   │ 4. MANAGER │
 │ scan + AI  │   │ cut +      │   │ post to    │   │ track +    │
 │ score      │   │ captions + │   │ YT/TikTok/ │   │ optimize   │
 │ clips      │   │ music      │   │ IG         │   │ the others │
 └────────────┘   └────────────┘   └─────▲──────┘   └─────┬──────┘
        ▲                │   you approve  │                │
        └────────────────┴────────────────┴────────────────┘
                    learnings feed back into Finder
```

This pipeline is **semi-automated**: the Finder proposes clips, you approve them, then the
Editor and Uploader run. Nothing is posted without your `approve` step.

## What each agent does

1. **Finder** (`factory/agents/finder.py`) — downloads a YouTube podcast, transcribes it with
   word-level timestamps (faster-whisper), and asks Claude to score the most clip-worthy
   moments. Candidates land in a local SQLite DB for your review.
2. **Editor** (`factory/agents/editor.py`) — cuts each approved clip with ffmpeg, reframes to
   vertical 9:16, burns in animated captions, and mixes background music.
3. **Uploader** (`factory/agents/uploader.py`) — publishes to YouTube Shorts (official API),
   and prepares TikTok / Instagram Reels (see platform notes below).
4. **Manager** (`factory/agents/manager.py`) — pulls engagement metrics, figures out which
   topics/styles win, and writes `learnings.md` that the Finder reads next run.

## Setup

1. Install prerequisites (Python 3.11+, ffmpeg). On Windows:
   ```powershell
   winget install -e --id Python.Python.3.11
   winget install -e --id Gyan.FFmpeg
   ```
2. Create a virtualenv and install deps:
   ```powershell
   python -m venv .venv
   .\.venv\Scripts\Activate.ps1
   pip install -r requirements.txt
   ```
3. Copy `.env.example` to `.env` and fill in your keys (at minimum `ANTHROPIC_API_KEY`).
4. Edit `config.yaml` to taste.

## Usage

```powershell
# 1. Find clips in a podcast (downloads, transcribes, scores)
python run.py find "https://www.youtube.com/watch?v=XXXX"

# 2. Review candidates and approve the good ones
python run.py review

# 3. Edit approved clips into finished shorts
python run.py edit

# 4. Upload (you'll be asked to confirm per platform)
python run.py upload

# 5. Pull metrics + update learnings
python run.py stats

# Run the whole semi-auto pipeline (pauses for your approval)
python run.py auto "https://www.youtube.com/watch?v=XXXX"
```

## Staying current + going hands-free

**Trend Scout** uses Claude's web search to find what's trending in short-form right now
and writes `trends.md`, which the Finder reads to bias selection toward live trends:

```powershell
python run.py scout        # set your niche in config.yaml -> trend_scout.niche
```

Together with the Manager's performance loop (`learnings.md`), the factory both **learns from
your own results** and **follows external trends**.

**Fully unattended (daily):** set `scheduler.source_url` in `config.yaml` to a channel or
playlist URL, then:

```powershell
python run.py daily        # scout → newest unprocessed video → auto-approve top N → edit → upload → stats
.\tools\run_daily.ps1      # same, with logging to logs\  (what the scheduled task runs)
.\tools\schedule_daily.ps1 -At 9am   # register a Windows daily task (one time)
```

`daily` auto-approves the top `finder.auto_approve_top` clips (no human gate) and posts to the
configured platforms — uploads default to **private**, so review them before going public.

## Cost / model choice

Each agent's Claude model is set in `config.yaml` → `models` (only used when
`llm.provider: anthropic`). Default is a smart split: **Sonnet 4.6** for taste-sensitive work
and **Haiku 4.5** for cheap caption tweaks. Rough cost per 1-hour podcast: Haiku ~$0.10–0.20,
Sonnet ~$0.30–0.60, Opus ~$0.50–1.00.

### Run it 100% free

The whole pipeline is **provider-switchable** (`config.yaml` → `llm.provider`). To pay nothing,
pick a free provider, add its key to `.env`, and set a model:

```yaml
llm:
  provider: groq          # groq | nvidia | gemini | ollama
  model: llama-3.3-70b-versatile
```

| Provider | Free key | Suggested model |
|---|---|---|
| **Groq** | console.groq.com/keys | `llama-3.3-70b-versatile` (fast) |
| **NVIDIA NIM** | build.nvidia.com | `meta/llama-3.3-70b-instruct` |
| **Google Gemini** | aistudio.google.com/apikey | `gemini-2.0-flash` |
| **Ollama** (local) | none (install ollama) | `llama3.1` (needs a good GPU) |

These won't quite match Claude's taste for picking viral moments, but with the skill playbooks
they do clip-selection and captions well. The Trend Scout's web search is free for *all*
providers (uses DuckDuckGo). Anthropic stays the default so nothing changes unless you switch.

## Installed skills

Each agent loads expert **skill playbooks** (`factory/skills/*.md`) into its AI prompts —
this is how the domain expertise (hooks, storytelling, sound design, VFX, marketing) is
"installed" into a standalone Python pipeline. Edit the `skills:` section of `config.yaml`
to change which agent uses which, or drop a new `.md` file in `factory/skills/` to add one.

```powershell
python run.py skills      # list all skills + per-agent assignments
```

| Agent | Skills loaded |
|-------|---------------|
| Finder | short-form-strategy, hooks, storytelling, marketing-psychology, viral-patterns |
| **Editor** | video-editing, hooks, storytelling, sound-design, visual-effects, captions-craft, pacing |
| Uploader | copywriting, titles-thumbnails, hashtags-seo, marketing-psychology |
| Manager | analytics-optimization, marketing-psychology, short-form-strategy |

The **Editor** runs a skill-driven creative planner per clip: it writes a punchy on-screen
hook, picks the key words to enlarge in the captions, chooses a music mood, and emits timed
SFX / b-roll / transition suggestions to `output/clip_<id>.notes.md` for the finishing pass.
Toggle it with `editor.ai_plan`. Caption word-emphasis is applied automatically in the render.

It also runs an **auto-trim pass** first (`editor.trim`): using the word-level timestamps it
removes filler words ("um", "uh") and tightens dead air, then re-times the captions to match —
the single biggest "amateur vs pro" lever in short-form. Fillers default to a conservative
disfluency list; add risky ones or multi-word phrases in `config.yaml` if you want.

## Platform reality check

- **YouTube Shorts** — fully automatable via the YouTube Data API v3 (OAuth). Implemented.
- **TikTok** — direct posting requires the TikTok Content Posting API (app review/approval).
  Until you have that, the uploader exports the finished file + caption/hashtags so you can
  post manually or via a scheduler (Buffer / Metricool / Later).
- **Instagram Reels** — requires a Business/Creator account + Facebook Graph API. Same
  fallback: export-for-scheduler until you wire up Graph API.

## Copyright note

You chose "public podcasts." Clipping shows you don't own can lead to copyright claims,
demonetization, or strikes. Safer patterns: transformative commentary/reaction, fair-use
short excerpts, or getting the creator's OK. This tool does not judge rights for you.
