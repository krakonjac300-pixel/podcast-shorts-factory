"""Download podcasts from YouTube and transcribe with word-level timestamps."""
from __future__ import annotations

import subprocess
from pathlib import Path

from ..config import WORK, cfg


def _cookie_opts() -> dict:
    """YouTube increasingly blocks anonymous downloads ('confirm you're not a
    bot'). Using the user's logged-in browser cookies fixes it. Set
    finder.cookies_from_browser to chrome | edge | firefox | brave (or null)."""
    browser = cfg.get("finder.cookies_from_browser")
    return {"cookiesfrombrowser": (browser,)} if browser else {}


def download(url: str) -> tuple[Path, str, str]:
    """Download a YouTube video as mp4. Returns (path, title, channel_handle).
    The channel handle (e.g. '@joerogan') feeds the Manager's per-channel
    performance ranking."""
    import yt_dlp

    out_tmpl = str(WORK / "%(id)s.%(ext)s")
    opts = {
        "format": "bv*[height<=1080]+ba/b[height<=1080]",
        "merge_output_format": "mp4",
        "outtmpl": out_tmpl,
        "quiet": True,
        "no_warnings": True,
        # long podcast downloads WILL hit connection drops — be stubborn
        "retries": 15,
        "fragment_retries": 15,
        "socket_timeout": 30,
        "continuedl": True,                  # resume partial downloads
        **_cookie_opts(),
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=True)
        path = Path(WORK / f"{info['id']}.mp4")
        channel = info.get("uploader_id") or info.get("channel") or ""
        return path, info.get("title", info["id"]), channel


def _is_downloadable(watch_url: str) -> bool:
    """True if the video can actually be downloaded (not members-only / removed / gated)."""
    import yt_dlp
    opts = {"quiet": True, "no_warnings": True, "skip_download": True, **_cookie_opts()}
    try:
        with yt_dlp.YoutubeDL(opts) as ydl:
            info = ydl.extract_info(watch_url, download=False)
        return bool(info.get("formats"))
    except Exception:  # noqa: BLE001 - gated/unavailable videos raise here
        return False


def newest_downloadable(url: str, skip_urls=(), max_check: int = 12) -> str | None:
    """Newest PUBLIC/downloadable video from a channel or playlist, skipping
    members-only, unavailable, and already-processed (skip_urls) videos."""
    import yt_dlp
    skip = set(skip_urls or ())
    opts = {"quiet": True, "no_warnings": True, "extract_flat": True,
            "playlist_items": f"1-{max_check}", "skip_download": True, **_cookie_opts()}
    try:
        with yt_dlp.YoutubeDL(opts) as ydl:
            info = ydl.extract_info(url, download=False)
    except Exception:  # noqa: BLE001
        return None
    entries = info.get("entries")
    if not entries:                                   # single video URL
        w = info.get("webpage_url") or url
        return w if (w not in skip and _is_downloadable(w)) else None
    for e in entries:
        if not e or not e.get("id"):
            continue
        w = f"https://www.youtube.com/watch?v={e['id']}"
        if w in skip:
            continue
        if _is_downloadable(w):
            return w
    return None


def pick_next(sources, skip_urls=()) -> str | None:
    """Across a list of channel/playlist URLs, return the first fresh, downloadable
    video (rotates channels; skips already-processed ones)."""
    for src in sources or []:
        if not src:
            continue
        w = newest_downloadable(src, skip_urls=skip_urls)
        if w and w not in set(skip_urls or ()):
            return w
    return None


def extract_audio(video: Path) -> Path:
    """Pull a 16k mono wav for transcription."""
    audio = video.with_suffix(".wav")
    subprocess.run(
        ["ffmpeg", "-y", "-i", str(video), "-ac", "1", "-ar", "16000",
         "-vn", str(audio)],
        check=True, capture_output=True,
    )
    return audio


_REFINE_CACHE: dict = {}


def refine_words(video: str, start: float, end: float) -> list[dict]:
    """Re-transcribe ONLY a clip's window with a stronger model.

    We transcribe a whole 90-minute episode but burn captions from about 90
    seconds of it, so paying full-episode cost for caption-grade accuracy is
    backwards. Measured on this machine: small runs 0.4x realtime and medium
    1.1x, so medium over a full episode costs ~99 minutes and would eat the
    produce window, while medium over three 40s windows costs about 2 minutes.

    Returns words on the SOURCE timeline (timestamps shifted back by `start`)
    so callers can splice them straight into the episode transcript. Returns []
    on any failure, which leaves the original words in place.
    """
    import subprocess
    import tempfile

    model_name = cfg.get("finder.refine_model", "medium")
    dur = max(0.0, float(end) - float(start))
    if dur <= 0:
        return []
    try:
        from faster_whisper import WhisperModel
    except Exception:  # noqa: BLE001
        return []

    tmp = Path(tempfile.gettempdir()) / f"refine_{abs(hash((video, start, end)))}.wav"
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-ss", str(start), "-t", str(dur), "-i", str(video),
             "-vn", "-ac", "1", "-ar", "16000", str(tmp)],
            check=True, capture_output=True)

        if model_name not in _REFINE_CACHE:      # loading medium costs ~5s
            _REFINE_CACHE[model_name] = WhisperModel(
                model_name, device=cfg.get("finder.whisper_device", "cpu"),
                compute_type="float16"
                if cfg.get("finder.whisper_device", "cpu") == "cuda" else "int8")
        model = _REFINE_CACHE[model_name]

        segments, _ = model.transcribe(
            str(tmp), language=cfg.get("finder.language"), word_timestamps=True,
            vad_filter=False,          # the window is already the moment we want
            initial_prompt=(cfg.get("finder.vocabulary") or "").strip() or None,
            beam_size=int(cfg.get("finder.whisper_beam", 5)),
            condition_on_previous_text=False)

        out = []
        for seg in segments:
            for w in (seg.words or []):
                out.append({"start": float(w.start) + start,
                            "end": float(w.end) + start, "word": w.word,
                            "p": round(float(getattr(w, "probability", 1.0) or 1.0), 3)})
        return out
    except Exception:  # noqa: BLE001 - refinement is an upgrade, never a gate
        return []
    finally:
        tmp.unlink(missing_ok=True)


def transcribe(audio: Path) -> list[dict]:
    """Word-level transcript: [{start, end, text, words:[{start,end,word}]}]."""
    from faster_whisper import WhisperModel

    model_name = cfg.get("finder.whisper_model", "base")
    language = cfg.get("finder.language")
    # CPU is the safe default (GPU needs the CUDA/cuBLAS runtime installed).
    # Set finder.whisper_device: cuda only if you have a working CUDA setup.
    device = cfg.get("finder.whisper_device", "cpu")
    compute = "float16" if device == "cuda" else "int8"
    model = WhisperModel(model_name, device=device, compute_type=compute)

    # Proper nouns are where Whisper fails us, and they are exactly the words
    # our hooks are built on. A vocabulary hint conditions the decoder toward
    # the names it is about to hear ("Copenhagen" came out as "COPPEN" and got
    # burned into a published clip). Beam search over greedy decoding buys
    # accuracy for a modest amount of time on short podcast clips.
    prompt = (cfg.get("finder.vocabulary") or "").strip() or None
    beam = int(cfg.get("finder.whisper_beam", 5))

    def run(vad: bool) -> list[dict]:
        segments, _ = model.transcribe(
            str(audio), language=language, word_timestamps=True, vad_filter=vad,
            initial_prompt=prompt, beam_size=beam,
            # each segment re-anchors on the prompt instead of drifting on its
            # own earlier guesses, which is what turns one bad name into three
            condition_on_previous_text=False,
        )
        out = []
        for seg in segments:
            words = [{"start": w.start, "end": w.end, "word": w.word,
                      # kept so captions can flag what the model was unsure of
                      "p": round(float(getattr(w, "probability", 1.0) or 1.0), 3)}
                     for w in (seg.words or [])]
            out.append({"start": seg.start, "end": seg.end,
                        "text": seg.text.strip(), "words": words})
        return out

    out = run(vad=True)
    if not out:                      # VAD over-filtered (e.g. music-heavy) — retry raw
        out = run(vad=False)
    return out
