#!/usr/bin/env python3
"""Render PNG previews of the NEXUS OS pixel font (SecondCursor.Core.Art.PixelFontData).

Reads out/font.json written by the C# FontPreview tool, verifies that this renderer lays text
out exactly like the C# API (MeasureWidth + reference render out/samples_cs.pgm), then writes
previews into out/:

  charset_1x.png / charset_3x.png      full charset + fallback, both themes
  charset_zoom.png                     8x zoom with pixel grid and metric guides
  samples_<theme>_1x.png / _3x.png     sample lines + email paragraph wrapped at 330 px
  ui_mock_2x.png                       the font inside a NEXUS OS style window, at 2x (1080p view)
  preview_final.png                    contact sheet of the above
"""
import json
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

THEMES = {
    "light": {"bg": (0xFB, 0xFA, 0xF5), "fg": (0x11, 0x11, 0x11), "dim": (0x8C, 0x88, 0x7C), "rule": (0xE2, 0xDF, 0xD4)},
    "teal": {"bg": (0x18, 0x3F, 0x3A), "fg": (0xFF, 0xFF, 0xFF), "dim": (0x86, 0xB0, 0xA8), "rule": (0x2A, 0x57, 0x50)},
}

WRAP_WIDTH = 330

EMAIL = (
    "From: R. Hallam <rhallam@nexus-corp.net>\n"
    "To: All Staff (Building C)\n"
    "Subject: Re: Mandatory update tonight - NEXUS OS 4.2\n"
    "\n"
    "Please do not power down your workstation tonight. The update will run "
    "automatically at 02:13 and may take up to 45 minutes. If you see a second cursor "
    "on your screen, do not click anything: report it to IT (ext. 3317) immediately. "
    "Files in C:\\NEXUS\\TEMP\\ will be deleted; back up anything you need before 6:00 PM.\n"
    "\n"
    "Thank you for your cooperation,\n"
    "-- Systems & Compliance"
)


class Font:
    def __init__(self, data):
        m = data["metrics"]
        self.height = m["GlyphHeight"]
        self.ascent = m["Ascent"]
        self.x_height = m["XHeight"]
        self.line_height = m["LineHeight"]
        self.space_advance = m["SpaceAdvance"]
        self.letter_spacing = m["LetterSpacing"]
        self.page_margin = m["PageMargin"]
        self.glyphs = {chr(int(k)): v for k, v in data["glyphs"].items()}
        self.fallback = data["fallback"]

    def glyph(self, c):
        return self.glyphs.get(c, self.fallback)

    def width(self, c):
        if c == " ":
            return self.space_advance - self.letter_spacing
        return len(self.glyph(c)[0])

    def measure(self, text):
        w = 0
        n = 0
        for c in text:
            if c in "\r\n":
                continue
            if n:
                w += self.letter_spacing
            w += self.width(c)
            n += 1
        return w

    def draw(self, img, x, y, text, color):
        """Draw one line at 1x with its top-left at (x, y). Returns the pen position."""
        px = img.load()
        pen = x
        first = True
        for c in text:
            if c in "\r\n":
                continue
            if not first:
                pen += self.letter_spacing
            first = False
            for r, row in enumerate(self.glyph(c)):
                for col, ch in enumerate(row):
                    if ch == "#":
                        px[pen + col, y + r] = color
            pen += self.width(c)
        return pen

    def wrap(self, text, max_width):
        lines = []
        for para in text.split("\n"):
            cur = ""
            for word in para.split(" "):
                trial = word if not cur else cur + " " + word
                if not cur or self.measure(trial) <= max_width:
                    cur = trial
                else:
                    lines.append(cur)
                    cur = word
            lines.append(cur)
        return lines


def zoom(img, k):
    return img.resize((img.width * k, img.height * k), Image.NEAREST)


def load_pgm(path):
    with open(path, "rb") as f:
        data = f.read()
    parts = data.split(b"\n", 3)
    assert parts[0] == b"P5", "not a binary PGM"
    w, h = map(int, parts[1].split())
    return w, h, parts[3][: w * h]


# ---------------------------------------------------------------------------- verification

def verify(font, data):
    samples = [m["text"] for m in data["measure"]]
    for m in data["measure"]:
        mine = font.measure(m["text"])
        if mine != m["width"]:
            sys.exit(f"measure mismatch for {m['text']!r}: python {mine}, C# {m['width']}")

    w, h, ref = load_pgm(os.path.join(OUT, "samples_cs.pgm"))
    img = Image.new("L", (w, h), 255)
    for i, s in enumerate(samples):
        font.draw(img, font.page_margin, font.page_margin + i * font.line_height, s, 0)
    if img.tobytes() != ref:
        sys.exit("python render differs from C# reference render (samples_cs.pgm)")

    # Nothing may be drawn outside the glyph box or overlap between lines.
    for c, rows in font.glyphs.items():
        assert len(rows) == font.height, c
    print(f"verified: {len(samples)} sample widths and reference render match the C# API")


# ---------------------------------------------------------------------------- charset

def charset_chars():
    return [chr(c) for c in range(32, 127)] + ["\u0001"]  # last cell = fallback glyph


def charset_image(font, theme, cols=16, cell_w=9, cell_h=14, pad=6):
    chars = charset_chars()
    rows = (len(chars) + cols - 1) // cols
    img = Image.new("RGB", (cols * cell_w + 2 * pad, rows * cell_h + 2 * pad), theme["bg"])
    for i, c in enumerate(chars):
        cx = pad + (i % cols) * cell_w
        cy = pad + (i // cols) * cell_h
        gx = cx + (cell_w - font.width(c)) // 2
        font.draw(img, gx, cy + 2, c, theme["fg"])
    return img


def charset_zoom(font, k=8, cols=16):
    theme = THEMES["light"]
    chars = charset_chars()
    cell_w, cell_h, pad = 9, 14, 2
    base = charset_image(font, theme, cols=cols, cell_w=cell_w, cell_h=cell_h, pad=pad)
    img = zoom(base, k)
    d = ImageDraw.Draw(img)
    grid = (0xEC, 0xE9, 0xDF)
    rows = (len(chars) + cols - 1) // cols
    # pixel grid (drawn under the guides, over the background only)
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            if (x % k == 0 or y % k == 0) and px[x, y] == theme["bg"]:
                px[x, y] = grid
    guides = [(0, (0xB8, 0xB4, 0xA8)), (2, (0x6C, 0x8E, 0xD8)), (8, (0xD8, 0x5A, 0x5A)), (10, (0xB8, 0xB4, 0xA8))]
    for r in range(rows):
        top = (pad + r * cell_h + 2) * k
        for row, color in guides:
            y = top + row * k
            for x in range(0, img.width, 4):
                if px[x, y] != theme["fg"]:
                    d.line([(x, y), (x + 1, y)], fill=color)
    return img


# ---------------------------------------------------------------------------- samples

def samples_image(font, theme, samples):
    m = 8
    ops = [("label", "SAMPLE LINES")]
    ops += [("text", s) for s in samples]
    ops += [("gap", 6), ("label", f"MAIL BODY - word wrap at {WRAP_WIDTH} px")]
    wrapped = font.wrap(EMAIL, WRAP_WIDTH)
    ops += [("wrap", s) for s in wrapped]

    width = max([font.measure(s) for kind, s in ops if kind in ("text", "wrap", "label")] + [WRAP_WIDTH]) + 2 * m + 4
    y = m
    placed = []
    wrap_top = wrap_bottom = None
    for kind, val in ops:
        if kind == "gap":
            y += val
            continue
        if kind == "label":
            placed.append((y, val, "dim"))
            y += font.line_height + 2
            continue
        if kind == "wrap" and wrap_top is None:
            wrap_top = y
        placed.append((y, val, "fg"))
        y += font.line_height
        if kind == "wrap":
            wrap_bottom = y
    height = y + m

    img = Image.new("RGB", (width, height), theme["bg"])
    d = ImageDraw.Draw(img)
    # wrap guide: faint dotted line one pixel right of the 330 px limit
    gx = m + WRAP_WIDTH
    for yy in range(wrap_top, wrap_bottom, 2):
        d.point((gx, yy), fill=theme["rule"])
    for yy, text, color in placed:
        font.draw(img, m, yy, text, theme[color])
    longest = max(font.measure(s) for s in wrapped)
    assert longest <= WRAP_WIDTH, longest
    return img


DETAIL_LINES = [
    "Thank you, cooperation; QUARTZ, 67% 100%",
    "back click kiosk KICK lucky milk",
    "COW GOOD DOG QUO OBOE CODEC 0O0",
    "Systems & Compliance @ #3317 $5 *.*",
    "Il1| O0o rn m w vv ij fj ft tt ff",
    "NEXUS WAX VOW XYZ MAW KEY AVAST",
    "(a) [b] {c} <d> \"e\" 'f' `g` ^h~ a-b a_b",
]


def detail_image(font, theme, lines=DETAIL_LINES):
    m = 4
    width = max(font.measure(s) for s in lines) + 2 * m
    height = len(lines) * font.line_height + 2 * m
    img = Image.new("RGB", (width, height), theme["bg"])
    for i, s in enumerate(lines):
        font.draw(img, m, m + i * font.line_height, s, theme["fg"])
    return img


# ---------------------------------------------------------------------------- UI mock

def ui_mock(font):
    """A small NEXUS OS window at 1x (callers zoom it)."""
    W, H = 372, 212
    desk = (0x18, 0x3F, 0x3A)
    face = (0xFB, 0xFA, 0xF5)
    ink = (0x11, 0x11, 0x11)
    shade = (0x9C, 0x98, 0x8C)
    title_bg = (0x23, 0x5C, 0x54)
    sel_bg = (0x18, 0x3F, 0x3A)
    white = (0xFF, 0xFF, 0xFF)
    img = Image.new("RGB", (W, H), desk)
    d = ImageDraw.Draw(img)

    font.draw(img, 8, 6, "NEXUS OS 4.2   Workstation C-117   (c) 1998 Nexus Data Systems", white)

    # window
    wx, wy, ww, wh = 8, 22, 356, 182
    d.rectangle([wx, wy, wx + ww - 1, wy + wh - 1], fill=face, outline=ink)
    d.rectangle([wx + 1, wy + 1, wx + ww - 2, wy + 14], fill=title_bg)
    font.draw(img, wx + 5, wy + 3, "File Manager - C:\\NEXUS\\USERS\\hallam", white)
    font.draw(img, wx + ww - 14, wy + 3, "x", white)
    font.draw(img, wx + 5, wy + 18, "File  Edit  View  Help", ink)
    d.line([wx + 1, wy + 30, wx + ww - 2, wy + 30], fill=shade)

    rows = [
        ("employee_015.dat", "9 KB", "1998-10-28 09:41"),
        ("employee_016.dat", "12 KB", "1998-11-02 17:40"),
        ("employee_017.dat", "14 KB", "1998-11-03 02:13"),
        ("employee_018.dat", "0 KB", "1998-11-03 02:14"),
        ("README.TXT", "1 KB", "1998-06-11 12:00"),
    ]
    font.draw(img, wx + 8, wy + 35, "Name", shade)
    font.draw(img, wx + 120, wy + 35, "Size", shade)
    font.draw(img, wx + 170, wy + 35, "Modified", shade)
    for i, (name, size, date) in enumerate(rows):
        ry = wy + 49 + i * 13
        color = ink
        if i == 2:
            d.rectangle([wx + 5, ry - 2, wx + ww - 7, ry + 10], fill=sel_bg)
            color = white
        font.draw(img, wx + 8, ry, name, color)
        font.draw(img, wx + 120 + 30 - font.measure(size), ry, size, color)
        font.draw(img, wx + 170, ry, date, color)

    # progress dialog
    dx, dy, dw, dh = wx + 36, wy + 104, 260, 70
    d.rectangle([dx + 2, dy + 2, dx + dw + 1, dy + dh + 1], fill=shade)
    d.rectangle([dx, dy, dx + dw - 1, dy + dh - 1], fill=face, outline=ink)
    d.rectangle([dx + 1, dy + 1, dx + dw - 2, dy + 13], fill=title_bg)
    font.draw(img, dx + 5, dy + 3, "Secure Delete", white)
    font.draw(img, dx + 8, dy + 18, "Shredding employee_017.dat... 67%", ink)
    bx, by, bw = dx + 8, dy + 32, dw - 16
    d.rectangle([bx, by, bx + bw - 1, by + 7], outline=ink)
    d.rectangle([bx + 2, by + 2, bx + 2 + int((bw - 4) * 0.67) - 1, by + 5], fill=sel_bg)
    for label, x in (("OK", dx + dw - 116), ("Cancel", dx + dw - 60)):
        d.rectangle([x, dy + 46, x + 50, dy + 61], fill=face, outline=ink)
        font.draw(img, x + 26 - (font.measure(label) + 1) // 2, dy + 49, label, ink)
    return img


# ---------------------------------------------------------------------------- main

def main():
    with open(os.path.join(OUT, "font.json"), encoding="utf-8") as f:
        data = json.load(f)
    font = Font(data)
    verify(font, data)
    samples = [m["text"] for m in data["measure"]]

    written = []

    def save(img, name):
        path = os.path.join(OUT, name)
        img.save(path)
        written.append((name, img.size))
        return img

    cs_light = charset_image(font, THEMES["light"])
    cs_teal = charset_image(font, THEMES["teal"])
    both = Image.new("RGB", (cs_light.width, cs_light.height * 2), THEMES["light"]["bg"])
    both.paste(cs_light, (0, 0))
    both.paste(cs_teal, (0, cs_light.height))
    save(both, "charset_1x.png")
    save(zoom(both, 3), "charset_3x.png")
    save(charset_zoom(font), "charset_zoom.png")

    pages = {}
    for name, theme in THEMES.items():
        page = samples_image(font, theme, samples)
        pages[name] = page
        save(page, f"samples_{name}_1x.png")
        save(zoom(page, 3), f"samples_{name}_3x.png")

    save(zoom(detail_image(font, THEMES["light"]), 4), "detail_4x.png")

    mock = ui_mock(font)
    save(mock, "ui_mock_1x.png")
    save(zoom(mock, 2), "ui_mock_2x.png")

    # contact sheet at 2x (what a 1080p player sees): samples in both themes + UI mock
    gap = 8
    a, b = pages["light"], pages["teal"]
    sheet_1x = Image.new("RGB", (max(a.width + b.width + gap, mock.width), max(a.height, b.height) + gap + mock.height), (0x0C, 0x1F, 0x1C))
    sheet_1x.paste(a, (0, 0))
    sheet_1x.paste(b, (a.width + gap, 0))
    sheet_1x.paste(mock, (0, max(a.height, b.height) + gap))
    save(zoom(sheet_1x, 2), "preview_final.png")

    for name, size in written:
        print(f"wrote out/{name}  {size[0]}x{size[1]}")


if __name__ == "__main__":
    main()
