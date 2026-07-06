# Quick Start — Podcast Shorts Factory

Project location: `D:\Downloads\Podaci\podcast-shorts-factory`

## Already set up for you ✅
- Python 3.11 + virtualenv (`.venv`) with all dependencies
- ffmpeg (auto-found, even in scheduled runs)
- Free AI provider: OpenRouter (`nvidia/nemotron-3-ultra-550b-a55b:free`), key in `.env`
- 19 skill playbooks, face-tracking, auto-trim, captions, SFX pack, covers
- Daily Windows scheduled task ("PodcastShortsFactory", 9:00 AM) — registered, idle until you set a source

## One-time setup (do these once)

1. **Rotate your OpenRouter key** (you shared it in chat, so regenerate it):
   - Go to https://openrouter.ai/keys → create a new key → delete the old one
   - Open `D:\Downloads\Podaci\podcast-shorts-factory\.env` and replace the `OPENROUTER_API_KEY=` value

2. **(Optional) Background music** — drop a few royalty-free `.mp3` files into
   `assets\music\` (Pixabay Music / YouTube Audio Library). Leave empty for no music.

3. **(Optional) YouTube uploads** — only if you want auto-posting to YouTube:
   - Follow `docs\youtube-setup.md` to get `client_secret.json`
   - Run `python run.py auth-youtube` once (signs in with your Google account)

4. **(Optional) Daily automation** — set your podcast's channel/playlist URL in
   `config.yaml` → `scheduler.source_url`. The 9 AM task then runs itself.

## How to run it

Open **PowerShell**, then every time:
```powershell
cd D:\Downloads\Podaci\podcast-shorts-factory
.\.venv\Scripts\Activate.ps1
```

### Option A — step by step (recommended while learning)
```powershell
python run.py find "https://www.youtube.com/watch?v=VIDEO_ID"   # download + transcribe + AI-score clips
python run.py review                                            # approve / reject each candidate
python run.py edit                                              # render approved clips to output\
python run.py upload                                            # post (asks per platform) — or skip & grab files
python run.py stats                                             # after they're live: metrics + learnings
```

### Option B — one command, semi-auto (pauses for your approval)
```powershell
python run.py auto "https://www.youtube.com/watch?v=VIDEO_ID"
```

### Option C — fully hands-free (after step 4 above)
```powershell
python run.py daily          # scout trends → newest video → top clips → edit → post
```
…or just let the 9 AM scheduled task do it.

## Where things land
- Finished shorts: `output\clip_*.mp4`
- Cover images: `output\clip_*_cover.jpg`
- Creative notes (SFX/b-roll cues): `output\clip_*.notes.md`
- TikTok/Instagram (no API): `ready_to_post\` (file + caption to post manually)

## Handy extras
```powershell
python run.py scout      # refresh trends.md (what's trending now)
python run.py skills     # list all skills + which agent uses them
```

## Troubleshooting
- **"Sign in to confirm you're not a bot"** on a video → in `config.yaml` set
  `finder.cookies_from_browser: chrome` and **close Chrome** first (it locks its cookies),
  or just pick a different video.
- **Rate limited / model busy** → in `config.yaml` `llm.model`, switch to a fallback
  (e.g. `google/gemma-4-26b-a4b-it:free`), or add $10 credit at OpenRouter to lift the daily cap.
- **Want it cheaper-but-smarter later** → set `llm.provider: anthropic` + an `ANTHROPIC_API_KEY`.
- **Logs from scheduled runs** → `logs\`

## Pro-editor features (all in `config.yaml` → editor)
- **Punch zooms** on emphasis words (`punch_zoom`), **flash cuts** at topic
  shifts (`flash_transitions`), **mood-matched music** (assets/music — regenerate
  with `python tools\generate_music.py`).
- **B-roll photos** (`broll`, `max_broll`): needs a FREE Pexels key —
  https://www.pexels.com/api/ → "Get Started" → copy the key → add
  `PEXELS_API_KEY=your_key` to `.env`. Without it, clips render fine, just
  without the stock-photo overlays.

## Notifications (how the agents reach you)
- `activity.md` — plain-language feed of everything that happened, newest first.
- Windows toasts (`notify.toast`) + phone push via the free **ntfy** app —
  subscribe to the topic in `notify.ntfy_topic`.
