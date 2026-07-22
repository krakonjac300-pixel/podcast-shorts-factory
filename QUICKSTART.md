# Quick Start — Podcast Shorts Factory

A team of cooperating AI agents that turn long podcasts into short-form videos
(YouTube Shorts / TikTok / Reels), semi-automatically or fully hands-free.

> **Works on Windows, macOS, and Linux.** Commands below show PowerShell (Windows).
> On macOS/Linux use `source .venv/bin/activate` and forward-slash paths.

---

## 1. Prerequisites

- **Python 3.11+** — https://www.python.org/downloads/
- **ffmpeg** — https://ffmpeg.org/download.html (must be on your PATH)

Windows one-liners:
```powershell
winget install -e --id Python.Python.3.11
winget install -e --id Gyan.FFmpeg
```

## 2. Install — one click ✨

- **Windows:** double-click **`install.bat`**
- **macOS/Linux:** run **`bash install.sh`**

It checks your setup, installs everything, and launches the setup wizard.
After that, run any command with the `factory` shortcut — no venv activation needed:

```powershell
.\factory auto "https://www.youtube.com/watch?v=VIDEO_ID"   # Windows
./factory.sh auto "https://..."                             # macOS/Linux
```

<details><summary>Manual install (if you prefer)</summary>

```powershell
cd path\to\podcast-shorts-factory
python -m venv .venv
.\.venv\Scripts\Activate.ps1      # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
```
</details>

## 3. Run the setup wizard (easiest) ✨

```powershell
python run.py setup
```
This asks you a few questions (AI provider + key, your niche, platforms, CTA,
notifications) and writes everything for you. **Skip to "How to run it" after this.**

Prefer to configure by hand instead? Do steps 3b–4 below.

## 3b. (Manual) Add your API key (pick ONE provider)

Copy `.env.example` to `.env`, then fill in **one** key:

```powershell
copy .env.example .env            # macOS/Linux: cp .env.example .env
```

| Provider | Cost | Get a key | Then set in `config.yaml` |
|---|---|---|---|
| **OpenRouter** | free tier | https://openrouter.ai/keys | `llm.provider: openrouter` |
| **Groq** | free | https://console.groq.com/keys | `llm.provider: groq` |
| **Google Gemini** | free | https://aistudio.google.com/apikey | `llm.provider: gemini` |
| **Anthropic (Claude)** | paid, best quality | https://console.anthropic.com | `llm.provider: anthropic` |

The default `config.yaml` is set to **OpenRouter free**. Add `OPENROUTER_API_KEY=...` to `.env` and you're running.

## 4. (Optional) Extras

- **Background music** — drop royalty-free `.mp3` files into `assets\music\`
  (Pixabay Music / YouTube Audio Library). Empty = no music.
- **B-roll photos/video** — free Pexels key from https://www.pexels.com/api/ →
  add `PEXELS_API_KEY=...` to `.env`.
- **YouTube auto-upload** — follow `docs\youtube-setup.md` to create your own
  `client_secret.json`, then run `python run.py auth-youtube` once.

---

## How to run it

### Option A — step by step (recommended while learning)
```powershell
python run.py find "https://www.youtube.com/watch?v=VIDEO_ID"   # download + transcribe + AI-score clips
python run.py review                                            # approve / reject each candidate
python run.py edit                                              # render approved clips to output\
python run.py upload                                            # post (asks per platform)
python run.py stats                                             # after live: metrics + learnings
```

### Option B — one command, semi-auto (pauses for your approval)
```powershell
python run.py auto "https://www.youtube.com/watch?v=VIDEO_ID"
```

### Option C — fully hands-free
Set your source channels/playlists in `config.yaml` → `scheduler.sources`, then:
```powershell
python run.py daily          # scout trends → newest video → top clips → edit → post
```
Register a daily task with `.\tools\schedule_daily.ps1 -At 9am` (Windows).

## Where things land
- Finished shorts: `output\clip_*.mp4`
- Cover images: `output\clip_*_cover.jpg`
- TikTok/Instagram (no API): `ready_to_post\` (file + caption to post manually)

## Handy extras
```powershell
python run.py scout      # refresh trends.md (what's trending now)
python run.py skills     # list all skills + which agent uses them
```

## Troubleshooting
- **"Sign in to confirm you're not a bot"** on a video → in `config.yaml` set
  `finder.cookies_from_browser: chrome` and **close Chrome** first, or pick another video.
- **Rate limited** → switch `llm.model` to a fallback, or add credit at your provider.
- **Notifications** → set your OWN unique `notify.ntfy_topic` in `config.yaml`
  (install the free **ntfy** app, subscribe to that topic). Leave blank to disable.

## ⚠️ Copyright
Clipping podcasts you don't own can lead to copyright claims or strikes. Safer
patterns: transformative commentary, fair-use short excerpts, or the creator's
permission. This tool does not judge rights for you — use responsibly.
