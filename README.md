# Podcast Shorts Factory

**Ten cooperating AI agents that turn long podcasts into short-form videos, automatically.**

Free and open source. It ships configured for **free AI providers**, so you can run the whole
thing at roughly zero cost. No subscription, no paid API required.

You give it a YouTube podcast link. The agents transcribe the episode, find the moments most
likely to perform, cut them to vertical 9:16, burn in animated captions, add music, b-roll and
effects, quality-check every render, then write the titles and hashtags and schedule the posts
to YouTube Shorts. It can run completely unattended on a daily schedule.

---

## What each agent does

| # | Agent | What it does |
|---|-------|--------------|
| 1 | **Finder** | Downloads the podcast and transcribes it with word-level timestamps (faster-whisper), then uses AI to score the most clip-worthy moments. Long episodes are scored in chunks so nothing is missed. |
| 2 | **Editor** | Cuts each clip with ffmpeg, reframes to vertical 9:16 with face tracking, burns in karaoke-style captions, and mixes music. Adds punch zooms on emphasis words, fake-multicam camera cuts, b-roll, a cinematic grade and a retention bar. |
| 3 | **Uploader** | Writes platform-specific titles, descriptions and hashtags, then uploads to YouTube Shorts through the official API. It can schedule a whole day server-side, so your PC can be off and the posts still go out. TikTok and Reels are exported ready to post. |
| 4 | **Manager** | Pulls your real metrics (views, retention), works out what is actually winning, and writes what it learns back into a file the Finder reads on the next run. This is the feedback loop that makes the system improve over time. |
| 5 | **Trend Scout** | Uses free web search to find what is trending in your niche right now, so clip selection leans toward what people are currently watching. |
| 6 | **Planner** | Gives each clip its creative direction: the on-screen hook text, music mood, which words to emphasise, and where to place sound effects, b-roll and transitions. |
| 7 | **Community** | Drafts replies to the comments on your posts, so engagement does not die on the vine. |
| 8 | **Finishing Editor** | The quality gate. It watches every finished render the way a picky human editor would, and catches captions covering a speaker's face, black or frozen frames, dead air, clipped or too-quiet audio, and wrong durations. It auto-fixes the cheap problems and blocks broken clips from ever being posted. |
| 9 | **Trainer** | The team's coach. Weekly, it studies the top-performing Shorts in your niche and updates one craft playbook, so the whole system levels up instead of standing still. |
| 10 | **Compiler** | The showrunner. It stitches the week's best moments into a long-form 16:9 episode with an AI-narrator editorial spine (thesis, per-clip analysis, verdict), designed to satisfy 2026 reused-content monetization rules. |

On top of the agents there are **24 skill playbooks** in `factory/skills/`: written craft
knowledge on hooks, storytelling, pacing, sound design, captions, thumbnails, SEO, engagement
and monetization. These get loaded directly into the agents' prompts, which is what makes the
output feel deliberate instead of random. Edit them freely, they are just markdown.

---

## Quick start

**Prerequisites:** Python 3.11+ and ffmpeg.

```powershell
winget install -e --id Python.Python.3.11
winget install -e --id Gyan.FFmpeg
```

**Install:**

* **Windows:** double-click `install.bat`
* **macOS / Linux:** `bash install.sh`

The installer builds the environment and then launches an interactive setup wizard that asks
you a handful of questions: which AI provider to use, your API key, your niche (pick from
presets), which platforms to post to, and your call-to-action. It writes the config for you,
so there are no files to edit by hand.

**Then run it:**

```powershell
.\factory auto "https://www.youtube.com/watch?v=VIDEO_ID"
```

```bash
./factory.sh auto "https://www.youtube.com/watch?v=VIDEO_ID"
```

Or go fully hands-free: set your source channels in `config.yaml` under `scheduler.sources`,
then run `.\factory daily`.

A full step-by-step walkthrough is in **SETUP-GUIDE.pdf**, and `QUICKSTART.md` has the same
thing in text form.

---

## It runs free

The default configuration points at free AI providers. Pick one, paste the key in during
setup, and you are running:

| Provider | Cost | Get a key |
|---|---|---|
| OpenRouter | free tier | https://openrouter.ai/keys |
| Groq | free | https://console.groq.com/keys |
| Google Gemini | free | https://aistudio.google.com/apikey |
| Ollama | free, local | https://ollama.com |
| Anthropic (Claude) | paid, best quality | https://console.anthropic.com |

Transcription runs locally with faster-whisper, the voiceover uses free Edge TTS, and the
background music library is generated with pure synthesis, so it is safe to monetize.

---

## Commands

```
setup          First-run wizard (provider, key, niche, platforms, CTA)
find <url>     Download, transcribe, and AI-score clip candidates
review         Approve or reject candidates yourself
edit           Render approved clips into vertical shorts
finish         QA-review and finish each render before it can post
upload         Post or export to platforms
compile        Build the weekly long-form episode
stats          Refresh metrics and update learnings
scout          Refresh current trends
skills         List installed skills and which agent uses each
auto <url>     The whole pipeline in one command
daily          Fully unattended: trends, newest video, clips, edit, post
```

---

## Why I am giving this away

I built this over **almost a month**. It started because I wanted a channel that could run
itself, and it turned into a proper little team of agents that argue with each other about
what makes a clip good.

I am releasing it free because I would rather it be used than sit on my drive. **If one of you
takes this and actually manages to monetize it, that genuinely makes me happy.** That is the
whole hope. The MIT license means you can use it commercially, modify it, and sell whatever
you build with it. You do not owe me anything.

If you do build something with it, I would love to hear about it.

---

## Contributing

Issues and pull requests are very welcome. Useful places to start:

* Better clip-selection prompts in `config.yaml` and `factory/skills/`
* More editor styles and transitions
* Platform support beyond YouTube (direct TikTok and Instagram posting)
* Anything in the Finishing Editor's quality checks

---

## Please use it responsibly

This automates production, not judgement. Only clip content you have the rights to: your own
material, licensed material, fair-use commentary, or content you have permission to use. You
are responsible for the platform rules you operate under and any API costs you incur. Fully
automated posting can attract strikes if you are careless, so start with `privacy: private`,
watch what it produces, and only then go public.

---

## Disclaimer

**This is not perfect.** It is a working system that produces real videos, but it has rough
edges, the output quality varies, and some parts are held together with duct tape and
stubbornness. I am releasing it as-is, honestly.

Hopefully, with the help of the community, we can make it perfect for everyone. Every fix,
idea and pull request genuinely helps.

---

## License

MIT. See [LICENSE](LICENSE). Use it, change it, sell it, have fun.
