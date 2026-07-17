"""Shared face detection for the editor + finishing QA.

YuNet (OpenCV's built-in DNN detector) with a Haar-cascade fallback. Haar was
the root of the 2026-07-17 'crop parked on a lamp' bug: it fires false positives
on textured objects and misses semi-profile faces, so the per-shot crop tracked
furniture while the speaker talked off-frame. YuNet is score-thresholded (no
lamp-faces), handles profiles, and costs ~the same per sampled frame.
"""
from __future__ import annotations

from ..config import ROOT

MODEL = ROOT / "assets" / "models" / "face_detection_yunet_2023mar.onnx"

_det = None            # lazy singleton (cv2 import stays inside functions)
_det_failed = False


def detect(frame) -> list[tuple[int, int, int, int]]:
    """Face boxes [(x, y, w, h), ...] for a BGR frame, best detector available.
    Returns [] on any failure — callers already treat no-faces gracefully."""
    global _det, _det_failed
    try:
        import cv2
    except Exception:  # noqa: BLE001
        return []
    if _det is None and not _det_failed and MODEL.exists():
        try:
            _det = cv2.FaceDetectorYN_create(str(MODEL), "", (320, 320), 0.6)
        except Exception:  # noqa: BLE001 - old cv2 → Haar fallback below
            _det_failed = True
    if _det is not None:
        try:
            h, w = frame.shape[:2]
            _det.setInputSize((w, h))
            _, faces = _det.detect(frame)
            if faces is None:
                return []
            return [(int(f[0]), int(f[1]), int(f[2]), int(f[3]))
                    for f in faces if f[2] > 0 and f[3] > 0]
        except Exception:  # noqa: BLE001
            pass
    # Haar fallback (model missing or YuNet errored)
    try:
        cascade = cv2.CascadeClassifier(
            cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
        if cascade.empty():
            return []
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        return [tuple(int(v) for v in f) for f in
                cascade.detectMultiScale(gray, scaleFactor=1.1,
                                         minNeighbors=5, minSize=(60, 60))]
    except Exception:  # noqa: BLE001
        return []


def active_speaker_cx(samples: list[tuple], frame_w: int) -> float | None:
    """Lightweight active-speaker detection: which face is TALKING? Given
    `samples` = [(gray_frame, faces), ...] from the same camera segment, cluster
    faces across samples by x-position and measure mouth-region pixel motion —
    lips moving = talking. Returns the talker's cx (0..1), or None when there's
    no clear winner. This is what separates pro auto-reframers (OpenShorts,
    Opus-style tools) from largest-face guessing."""
    if len(samples) < 2 or not frame_w:
        return None
    try:
        import cv2
    except Exception:  # noqa: BLE001
        return None
    clusters: dict[int, list] = {}          # bucket by cx/6% of width
    for gray, faces in samples:
        for (x, y, w, h) in faces:
            key = int((x + w / 2) / (frame_w * 0.06))
            clusters.setdefault(key, []).append((gray, x, y, w, h))
    best_cx, best_motion, second = None, 0.0, 0.0
    for _, dets in clusters.items():
        if len(dets) < 2:
            continue
        rois = []
        for gray, x, y, w, h in dets:
            # mouth region = lower third of the face box
            my, mh = y + int(h * 0.62), max(6, int(h * 0.38))
            roi = gray[max(0, my):my + mh, max(0, x):x + w]
            if roi.size == 0:
                rois = []
                break
            rois.append(cv2.resize(roi, (32, 16)).astype("float32"))
        if len(rois) < 2:
            continue
        motion = sum(float(abs(rois[i + 1] - rois[i]).mean())
                     for i in range(len(rois) - 1)) / (len(rois) - 1)
        cx = sum((x + w / 2) / frame_w for _, x, _, w, _ in dets) / len(dets)
        if motion > best_motion:
            best_cx, best_motion, second = cx, motion, best_motion
        elif motion > second:
            second = motion
    # demand a clear winner — ties mean cross-talk or noise, let the caller
    # fall back to its default (alternation / largest face)
    if best_cx is not None and best_motion > 1.35 * max(second, 1e-6):
        return best_cx
    return None
