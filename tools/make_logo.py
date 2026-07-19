"""Generate the channel logo / avatar for Money Mugshots.

Built in code rather than by hand so it is reproducible and exact. The hard
constraint is that a YouTube avatar is displayed as a circle at ~32px in the
Shorts feed, where anything with words in it turns to mush. So the mark has to
carry the brand at thumbnail size and the wordmark is a separate asset.

Design: a police-lineup height chart (the "mugshot" idea) with a bold $ standing
in the lineup. Reads as a shape at 32px, reads as a joke at 800px.

Outputs into assets/brand/:
  logo_800.png        the avatar (square, safe inside the circle crop)
  logo_32.png         a downscaled proof, to check it survives the feed
  banner_2048.png     channel banner with the wordmark
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OUT = ROOT / "assets" / "brand"

INK = (14, 15, 19)          # near-black background
CHART = (58, 62, 72)        # height-chart rules
GOLD = (255, 205, 0)        # the money accent (matches caption highlight)
BONE = (242, 242, 238)      # off-white text


def _font(size: int, bold: bool = True):
    from PIL import ImageFont
    for name in (("arialbd.ttf", "ariblk.ttf") if bold else ("arial.ttf",)):
        for base in (r"C:\Windows\Fonts", "/usr/share/fonts/truetype/dejavu"):
            p = Path(base) / name
            if p.exists():
                try:
                    return ImageFont.truetype(str(p), size)
                except Exception:  # noqa: BLE001
                    pass
    return ImageFont.load_default()


def _centred(draw, xy, text, font, fill):
    """Draw text centred on xy using its real ink bounds, not the font box.
    Glyph metrics include ascender padding, so anchor-free centring visibly
    sits low, which is obvious on a symmetric mark like this."""
    l, t, r, b = draw.textbbox((0, 0), text, font=font)
    draw.text((xy[0] - (l + r) / 2, xy[1] - (t + b) / 2), text, font=font, fill=fill)


def avatar(size: int = 800):
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (size, size), INK)
    d = ImageDraw.Draw(img)
    s = size / 800.0

    # Height chart, kept inside the circular crop YouTube applies (the corners
    # of a square avatar are cut off, so nothing meaningful goes near them).
    #
    # Deliberately only THREE thick rules and no tick marks. The first version
    # had five thin rules plus ticks, which looked right at 800px and turned
    # into grey mush at the 32px the Shorts feed actually renders. A feed avatar
    # has room for exactly one shape, so the rules are a detail that rewards a
    # closer look and the $ has to carry it alone when small.
    for i in range(3):
        y = int((238 + i * 162) * s)
        d.rounded_rectangle([int(132 * s), y, int(668 * s), y + int(19 * s)],
                            radius=int(9 * s), fill=CHART)

    # The subject in the lineup: a heavy dollar sign, sized to dominate so it
    # still reads as a $ when the whole mark is 32px wide.
    _centred(d, (size / 2, size * 0.505), "$", _font(int(580 * s)), GOLD)
    return img


def banner(w: int = 2048, h: int = 1152):
    """Channel banner. YouTube crops this hard on mobile, so everything that
    matters lives in the centre 1235x338 safe area."""
    from PIL import Image, ImageDraw
    img = Image.new("RGB", (w, h), INK)
    d = ImageDraw.Draw(img)
    cx, cy = w / 2, h / 2

    # Rules ABOVE and BELOW only. A third rule through the middle read as a
    # strikethrough across the wordmark, which is the opposite of the intent.
    for y in (int(cy - 118), int(cy + 122)):
        d.rounded_rectangle([int(cx - 560), y, int(cx + 560), y + 7],
                            radius=3, fill=CHART)

    _centred(d, (cx, cy - 24), "MONEY MUGSHOTS", _font(120), BONE)
    _centred(d, (cx, cy + 72), "THE RECEIPTS DON'T LIE", _font(44), GOLD)
    return img


def main() -> int:
    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        print("Pillow is required: pip install pillow")
        return 1

    OUT.mkdir(parents=True, exist_ok=True)
    a = avatar(800)
    a.save(OUT / "logo_800.png")
    a.resize((32, 32)).save(OUT / "logo_32.png")     # legibility proof
    a.resize((128, 128)).save(OUT / "logo_128.png")
    banner().save(OUT / "banner_2048.png")
    for f in sorted(OUT.glob("*.png")):
        print(f"  {f.name:20s} {f.stat().st_size // 1024:4d} KB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
