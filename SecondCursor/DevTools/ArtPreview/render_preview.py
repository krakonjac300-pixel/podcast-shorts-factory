#!/usr/bin/env python3
"""Render preview sheets for the NEXUS OS pixel art.

Input : out/sprites.json, written by the ArtPreview console program (which
        loads PixelArtData.cs, so every sprite has already been validated).
Output: out/contact_sheet.png   every sprite at 4x with its name, on the UI
                                grey (#C2BFB2) and on the desktop teal (#2B3D3A)
        out/desktop_preview.png a mock NEXUS OS screen: desktop icons at 2x
                                with 5x7 labels, 1x icons in a window, glyphs
                                on bevelled buttons, cursors over light/dark
        out/review/*.png        (--review) per-category 8x zooms + 1x strips

Usage: python3 render_preview.py [--json out/sprites.json] [--out out] [--review]
"""
import argparse
import json
import os

from PIL import Image, ImageDraw, ImageFont

LIGHT_BG = (0xC2, 0xBF, 0xB2)
DARK_BG = (0x2B, 0x3D, 0x3A)
BLACK = (0, 0, 0)


def rgba(hex8):
    v = int(hex8, 16)
    return ((v >> 24) & 255, (v >> 16) & 255, (v >> 8) & 255, v & 255)


class Art:
    def __init__(self, path):
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        self.palette = {k: rgba(v) for k, v in data['palette'].items()}
        self.sprites = {s['name']: s for s in data['sprites']}
        self.order = [s['name'] for s in data['sprites']]

    def col(self, ch):
        return self.palette[ch][:3]

    def image(self, name, scale=1, recolor=None):
        s = self.sprites[name]
        img = Image.new('RGBA', (s['w'], s['h']), (0, 0, 0, 0))
        px = img.load()
        for y, row in enumerate(s['rows']):
            for x, ch in enumerate(row):
                c = self.palette[ch]
                if recolor and ch in recolor and c[3]:
                    c = recolor[ch] + (255,)
                px[x, y] = c
        if scale != 1:
            img = img.resize((s['w'] * scale, s['h'] * scale), Image.NEAREST)
        return img

    def paste(self, canvas, name, x, y, scale=1, recolor=None):
        img = self.image(name, scale, recolor)
        canvas.alpha_composite(img, (x, y))
        return img.size


# --------------------------------------------------------------------------
# 5x7 pixel font for OS-style labels (uppercase, digits, a little punctuation)
# --------------------------------------------------------------------------
FONT5x7 = {
    'A': ".###.|#...#|#...#|#####|#...#|#...#|#...#",
    'B': "####.|#...#|#...#|####.|#...#|#...#|####.",
    'C': ".###.|#...#|#....|#....|#....|#...#|.###.",
    'D': "####.|#...#|#...#|#...#|#...#|#...#|####.",
    'E': "#####|#....|#....|####.|#....|#....|#####",
    'F': "#####|#....|#....|####.|#....|#....|#....",
    'G': ".###.|#...#|#....|#.###|#...#|#...#|.####",
    'H': "#...#|#...#|#...#|#####|#...#|#...#|#...#",
    'I': "###|.#.|.#.|.#.|.#.|.#.|###",
    'J': "..###|...#.|...#.|...#.|...#.|#..#.|.##..",
    'K': "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#",
    'L': "#....|#....|#....|#....|#....|#....|#####",
    'M': "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#",
    'N': "#...#|#...#|##..#|#.#.#|#..##|#...#|#...#",
    'O': ".###.|#...#|#...#|#...#|#...#|#...#|.###.",
    'P': "####.|#...#|#...#|####.|#....|#....|#....",
    'Q': ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#",
    'R': "####.|#...#|#...#|####.|#.#..|#..#.|#...#",
    'S': ".####|#....|#....|.###.|....#|....#|####.",
    'T': "#####|..#..|..#..|..#..|..#..|..#..|..#..",
    'U': "#...#|#...#|#...#|#...#|#...#|#...#|.###.",
    'V': "#...#|#...#|#...#|#...#|#...#|.#.#.|..#..",
    'W': "#...#|#...#|#...#|#.#.#|#.#.#|#.#.#|.#.#.",
    'X': "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#",
    'Y': "#...#|#...#|.#.#.|..#..|..#..|..#..|..#..",
    'Z': "#####|....#|...#.|..#..|.#...|#....|#####",
    '0': ".###.|#...#|#..##|#.#.#|##..#|#...#|.###.",
    '1': "..#..|.##..|..#..|..#..|..#..|..#..|.###.",
    '2': ".###.|#...#|....#|...#.|..#..|.#...|#####",
    '3': "####.|....#|....#|.###.|....#|....#|####.",
    '4': "...#.|..##.|.#.#.|#..#.|#####|...#.|...#.",
    '5': "#####|#....|####.|....#|....#|#...#|.###.",
    '6': "..##.|.#...|#....|####.|#...#|#...#|.###.",
    '7': "#####|....#|...#.|..#..|.#...|.#...|.#...",
    '8': ".###.|#...#|#...#|.###.|#...#|#...#|.###.",
    '9': ".###.|#...#|#...#|.####|....#|...#.|.##..",
    ' ': "...|...|...|...|...|...|...",
    '.': ".|.|.|.|.|.|#",
    ',': "..|..|..|..|..|.#|#.",
    ':': ".|#|.|.|.|#|.",
    '!': "#|#|#|#|#|.|#",
    '?': ".###.|#...#|....#|...#.|..#..|.....|..#..",
    '-': "....|....|....|####|....|....|....",
    '_': ".....|.....|.....|.....|.....|.....|#####",
    '\\': "#....|#....|.#...|..#..|...#.|....#|....#",
    '(': "..#|.#.|#..|#..|#..|.#.|..#",
    ')': "#..|.#.|..#|..#|..#|.#.|#..",
    '/': "....#|....#|...#.|..#..|.#...|#....|#....",
    "'": "#|#|.|.|.|.|.",
    '+': ".....|..#..|..#..|#####|..#..|..#..|.....",
    '>': "#...|.#..|..#.|...#|..#.|.#..|#...",
    '<': "...#|..#.|.#..|#...|.#..|..#.|...#",
    '=': "....|....|####|....|####|....|....",
    '#': ".#.#.|.#.#.|#####|.#.#.|#####|.#.#.|.#.#.",
    '%': "##..#|##..#|...#.|..#..|.#...|#..##|#..##",
}
FONT5x7 = {k: v.split('|') for k, v in FONT5x7.items()}


def text_width(s):
    w = 0
    for ch in s.upper():
        g = FONT5x7.get(ch, FONT5x7['?'])
        w += len(g[0]) + 1
    return max(0, w - 1)


def draw_text(canvas, s, x, y, color, scale=1):
    px = canvas.load()
    cx = x
    for ch in s.upper():
        g = FONT5x7.get(ch, FONT5x7['?'])
        for gy, row in enumerate(g):
            for gx, c in enumerate(row):
                if c == '#':
                    for sy in range(scale):
                        for sx in range(scale):
                            X, Y = cx + gx * scale + sx, y + gy * scale + sy
                            if 0 <= X < canvas.width and 0 <= Y < canvas.height:
                                px[X, Y] = color + (255,) if len(color) == 3 else color
        cx += (len(g[0]) + 1) * scale
    return cx - x


def ui_font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # very old Pillow
        return ImageFont.load_default()


# --------------------------------------------------------------------------
# Contact sheet
# --------------------------------------------------------------------------
CATEGORIES = [
    ('ICONS 16x16', lambda n: n.startswith('icon_')),
    ('CURSORS (K/W only, hotspot ticks in magenta)', lambda n: n.startswith('cursor_')),
    ('UI GLYPHS (K on transparent, drawn for button faces - see desktop_preview.png)',
     lambda n: n.startswith('glyph_')),
    ('LOGOS', lambda n: n.startswith('logo_')),
    ('PATTERN + MISC', lambda n: not n.startswith(('icon_', 'cursor_', 'glyph_', 'logo_'))),
]


def contact_panel(art, bg, scale, width, title):
    font = ui_font(12)
    hfont = ui_font(14)
    fg = (20, 19, 17) if sum(bg) > 380 else (235, 232, 222)
    frame = tuple(max(0, c - 18) for c in bg) if sum(bg) > 380 else tuple(min(255, c + 22) for c in bg)
    pad, gap = 14, 10
    # first pass: layout
    items = []
    x, y, row_h = pad, pad + 26, 0
    for cat, pred in CATEGORIES:
        names = [n for n in art.order if pred(n)]
        if not names:
            continue
        if x != pad:
            x, y, row_h = pad, y + row_h + gap, 0
        items.append(('header', cat, x, y))
        y += 22
        for n in names:
            s = art.sprites[n]
            iw, ih = s['w'] * scale, s['h'] * scale
            label = n
            tw = int(font.getlength(label))
            cw = max(iw, tw) + 8
            ch = ih + 22 + 6
            if x + cw > width - pad:
                x, y, row_h = pad, y + row_h + gap, 0
            items.append(('sprite', n, x, y, cw, iw, ih))
            x += cw + gap
            row_h = max(row_h, ch)
        x, y, row_h = pad, y + row_h + gap + 6, 0
    height = y + pad
    img = Image.new('RGBA', (width, height), bg + (255,))
    d = ImageDraw.Draw(img)
    d.text((pad, pad), title, fill=fg, font=hfont)
    for it in items:
        if it[0] == 'header':
            _, cat, hx, hy = it
            d.text((hx, hy), cat, fill=fg, font=hfont)
            continue
        _, n, cx, cy, cw, iw, ih = it
        s = art.sprites[n]
        ox = cx + (cw - iw) // 2
        oy = cy + 4
        d.rectangle([ox - 1, oy - 1, ox + iw, oy + ih], outline=frame)
        art.paste(img, n, ox, oy, scale)
        if n.startswith('cursor_'):
            hx = ox + s['hx'] * scale + scale // 2
            hy = oy + s['hy'] * scale + scale // 2
            mag = (230, 40, 200)
            d.line([hx, oy - 4, hx, oy - 2], fill=mag, width=1)
            d.line([ox - 4, hy, ox - 2, hy], fill=mag, width=1)
        label = n
        tw = int(font.getlength(label))
        d.text((cx + (cw - tw) // 2, oy + ih + 4), label, fill=fg, font=font)
        dims = f"{s['w']}x{s['h']}"
        _ = dims
    return img


def contact_sheet(art, out_dir, scale=4, width=1500):
    a = contact_panel(art, LIGHT_BG, scale, width, 'NEXUS OS pixel art  |  4x  |  on UI grey #C2BFB2')
    b = contact_panel(art, DARK_BG, scale, width, 'NEXUS OS pixel art  |  4x  |  on desktop #2B3D3A')
    sheet = Image.new('RGBA', (width, a.height + b.height), (0, 0, 0, 255))
    sheet.alpha_composite(a, (0, 0))
    sheet.alpha_composite(b, (0, a.height))
    sheet.convert('RGB').save(os.path.join(out_dir, 'contact_sheet.png'))
    a.convert('RGB').save(os.path.join(out_dir, 'contact_sheet_light.png'))
    b.convert('RGB').save(os.path.join(out_dir, 'contact_sheet_dark.png'))


# --------------------------------------------------------------------------
# Desktop mock (virtual screen, then upscaled 2x for viewing)
# --------------------------------------------------------------------------
def bevel(d, x0, y0, x1, y1, art, face='G', sunken=False):
    """Classic 2px bevel. Coordinates are inclusive."""
    W, L, G, D, K = (art.col(c) for c in 'WLGDK')
    if face:
        d.rectangle([x0, y0, x1, y1], fill=art.col(face))
    if not sunken:
        tl_o, tl_i, br_i, br_o = L, W, D, K
    else:
        tl_o, tl_i, br_i, br_o = D, K, L, W
    d.line([x0, y0, x1 - 1, y0], fill=tl_o)
    d.line([x0, y0, x0, y1 - 1], fill=tl_o)
    d.line([x0 + 1, y0 + 1, x1 - 2, y0 + 1], fill=tl_i)
    d.line([x0 + 1, y0 + 1, x0 + 1, y1 - 2], fill=tl_i)
    d.line([x1 - 1, y0 + 1, x1 - 1, y1 - 1], fill=br_i)
    d.line([x0 + 1, y1 - 1, x1 - 1, y1 - 1], fill=br_i)
    d.line([x1, y0, x1, y1], fill=br_o)
    d.line([x0, y1, x1, y1], fill=br_o)


def glyph_button(canvas, d, art, glyph, x, y, w=16, h=14, recolor=None):
    bevel(d, x, y, x + w - 1, y + h - 1, art)
    s = art.sprites[glyph]
    gx = x + (w - s['w']) // 2
    gy = y + (h - s['h']) // 2
    art.paste(canvas, glyph, gx, gy, 1, recolor)


DESKTOP_LABELS = {
    'icon_workstation': 'WORKSTATION', 'icon_folder': 'FOLDER', 'icon_folder_open': 'OPEN',
    'icon_folder_locked': 'LOCKED', 'icon_file_txt': 'NOTES.TXT', 'icon_file_dat': 'DUMP.DAT',
    'icon_file_log': 'AUDIT.LOG', 'icon_file_tmp': '~TMP0042', 'icon_file_exe': 'RECLAIM.EXE',
    'icon_file_cfg': 'NEXUS.CFG', 'icon_file_corrupt': 'M3M0RY.???', 'icon_mail': 'MAIL',
    'icon_mail_unread': 'MAIL (1)', 'icon_notepad': 'NOTEPAD', 'icon_staff': 'STAFF',
    'icon_camera': 'CAMERA 04', 'icon_workorders': 'WORK ORDERS', 'icon_disposal_empty': 'DISPOSAL',
    'icon_disposal_full': 'DISPOSAL', 'icon_system': 'SYSTEM', 'icon_help': 'HELP',
    'icon_info': 'INFO', 'icon_warning': 'WARNING', 'icon_error': 'ERROR', 'icon_question': 'QUESTION',
    'icon_lock': 'LOCK', 'icon_nexus': 'NEXUS', 'icon_shutdown': 'SHUT DOWN', 'icon_task_done': 'DONE',
    'icon_task_pending': 'PENDING', 'icon_task_active': 'ACTIVE', 'icon_drive': 'DRIVE C',
}


def selected_overlay(img, tint):
    """Checkerboard tint over opaque pixels (the classic 'selected icon' look)."""
    out = img.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            if px[x, y][3] and ((x // 2 + y // 2) % 2 == 0):
                px[x, y] = tint + (255,)
    return out


def desktop_preview(art, out_dir):
    VW, VH = 720, 560
    cv = Image.new('RGBA', (VW, VH), art.col('Q') + (255,))
    d = ImageDraw.Draw(cv)
    # desktop pattern at 2x
    if 'pattern_desktop' in art.sprites:
        tile = art.image('pattern_desktop', 2)
        for ty in range(0, VH, tile.height):
            for tx in range(0, VW, tile.width):
                cv.alpha_composite(tile, (tx, ty))
    white = art.col('W')
    # ---- desktop icons at 2x, 4 columns
    icons = [n for n in art.order if n.startswith('icon_')]
    colw, rowh = 68, 58
    for i, n in enumerate(icons):
        c, r = i % 4, i // 4
        x0, y0 = 6 + c * colw, 6 + r * rowh
        img = art.image(n, 2)
        label = DESKTOP_LABELS.get(n, n.replace('icon_', '').upper())
        tw = text_width(label)
        lx = x0 + (colw - tw) // 2
        ly = y0 + 36
        if n == 'icon_folder':  # show the selected state once
            img = selected_overlay(img, art.col('t'))
            d.rectangle([lx - 2, ly - 2, lx + tw + 1, ly + 8], fill=art.col('t'))
            for xx in range(lx - 2, lx + tw + 2):
                if (xx // 2) % 2 == 0:
                    cv.putpixel((xx, ly - 2), art.col('C') + (255,))
                    cv.putpixel((xx, ly + 8), art.col('C') + (255,))
        cv.alpha_composite(img, (x0 + (colw - 32) // 2, y0))
        draw_text(cv, label, lx, ly, white)

    # ---- window with 1x icons
    wx0, wy0, wx1, wy1 = 290, 8, 712, 262
    bevel(d, wx0, wy0, wx1, wy1, art)
    d.rectangle([wx0 + 3, wy0 + 3, wx1 - 3, wy0 + 20], fill=art.col('T'))
    art.paste(cv, 'icon_folder_open', wx0 + 5, wy0 + 4)
    draw_text(cv, 'C:\\ARCHIVE\\RECLAIMED', wx0 + 24, wy0 + 8, white)
    bx = wx1 - 3 - 16
    glyph_button(cv, d, art, 'glyph_close', bx, wy0 + 5)
    glyph_button(cv, d, art, 'glyph_maximize', bx - 18, wy0 + 5)
    glyph_button(cv, d, art, 'glyph_minimize', bx - 34, wy0 + 5)
    # menu bar
    mx = wx0 + 8
    for m in ('FILE', 'EDIT', 'VIEW', 'HELP'):
        draw_text(cv, m, mx, wy0 + 26, art.col('K'))
        mx += text_width(m) + 12
    # list view (sunken white)
    lx0, ly0, lx1, ly1 = wx0 + 5, wy0 + 38, wx1 - 5, wy1 - 22
    bevel(d, lx0, ly0, lx1, ly1, art, face='W', sunken=True)
    listed = icons
    col_w = 128
    for i, n in enumerate(listed):
        c, r = i // 11, i % 11
        ix, iy = lx0 + 5 + c * col_w, ly0 + 5 + r * 17
        if ix + 110 > lx1 - 20:
            break
        art.paste(cv, n, ix, iy)
        label = n.replace('icon_', '').upper()
        if i == 2:
            d.rectangle([ix + 19, iy + 3, ix + 20 + text_width(label) + 1, iy + 13], fill=art.col('T'))
            draw_text(cv, label, ix + 21, iy + 5, white)
        else:
            draw_text(cv, label, ix + 21, iy + 5, art.col('K'))
    # scrollbar
    sx0 = lx1 - 2 - 16
    d.rectangle([sx0, ly0 + 2, lx1 - 2, ly1 - 2], fill=art.col('L'))
    glyph_button(cv, d, art, 'glyph_arrow_up', sx0, ly0 + 2, 16, 16)
    glyph_button(cv, d, art, 'glyph_arrow_down', sx0, ly1 - 17, 16, 16)
    bevel(d, sx0, ly0 + 40, sx0 + 15, ly0 + 80, art)
    # status bar + grip
    d.rectangle([wx0 + 3, wy1 - 18, wx1 - 3, wy1 - 3], fill=art.col('G'))
    bevel(d, wx0 + 5, wy1 - 17, wx0 + 180, wy1 - 5, art, face='G', sunken=True)
    draw_text(cv, '32 OBJECTS', wx0 + 10, wy1 - 14, art.col('K'))
    art.paste(cv, 'glyph_resize_grip', wx1 - 3 - 12, wy1 - 3 - 12)

    # ---- dialog: message icons at 2x + controls
    dx0, dy0, dx1, dy1 = 290, 270, 560, 420
    bevel(d, dx0, dy0, dx1, dy1, art)
    d.rectangle([dx0 + 3, dy0 + 3, dx1 - 3, dy0 + 20], fill=art.col('T'))
    art.paste(cv, 'icon_warning', dx0 + 5, dy0 + 4)
    draw_text(cv, 'RECLAMATION', dx0 + 24, dy0 + 8, white)
    glyph_button(cv, d, art, 'glyph_close', dx1 - 3 - 16, dy0 + 5)
    for i, n in enumerate(('icon_info', 'icon_warning', 'icon_error', 'icon_question')):
        art.paste(cv, n, dx0 + 12 + i * 40, dy0 + 30, 2)
    draw_text(cv, 'ERASE DRIVE 7 OF 12?', dx0 + 12, dy0 + 70, art.col('K'))
    # check box + radio + dropdown
    cbx, cby = dx0 + 12, dy0 + 86
    bevel(d, cbx, cby, cbx + 12, cby + 12, art, face='W', sunken=True)
    art.paste(cv, 'glyph_check', cbx + 3, cby + 3)
    draw_text(cv, 'VERIFY', cbx + 18, cby + 3, art.col('K'))
    rx = cbx + 70
    d.ellipse([rx, cby, rx + 12, cby + 12], fill=art.col('W'), outline=art.col('D'))
    art.paste(cv, 'glyph_radio_dot', rx + 5 - 1, cby + 5 - 1)
    draw_text(cv, 'SHRED', rx + 18, cby + 3, art.col('K'))
    ddx = dx0 + 12
    bevel(d, ddx, cby + 20, ddx + 120, cby + 36, art, face='W', sunken=True)
    draw_text(cv, '7-PASS', ddx + 5, cby + 25, art.col('K'))
    glyph_button(cv, d, art, 'glyph_dropdown', ddx + 120 - 2 - 16, cby + 22, 16, 13)
    # buttons with glyphs
    bx = dx0 + 150
    for i, g in enumerate(('glyph_arrow_left', 'glyph_arrow_right', 'glyph_restore', 'glyph_minimize')):
        glyph_button(cv, d, art, g, bx + i * 20, cby + 20, 16, 16)
    bevel(d, dx0 + 150, cby + 2, dx0 + 200, cby + 16, art)
    draw_text(cv, 'OK', dx0 + 169, cby + 6, art.col('K'))
    bevel(d, dx0 + 206, cby + 2, dx0 + 256, cby + 16, art)
    draw_text(cv, 'CANCEL', dx0 + 213, cby + 6, art.col('K'))

    # ---- boot splash (black) + company plate
    bx0, by0 = 570, 270
    d.rectangle([bx0, by0, 712, by0 + 150], fill=BLACK)
    if 'logo_nexus' in art.sprites:
        art.paste(cv, 'logo_nexus', bx0 + 23, by0 + 10, 2)
    draw_text(cv, 'NEXUS OS', bx0 + 47, by0 + 112, art.col('W'))
    draw_text(cv, 'STARTING...', bx0 + 40, by0 + 126, art.col('C'))

    # ---- cursors over light and dark, at 2x
    cy0 = 430
    cursors = [n for n in art.order if n.startswith('cursor_')]
    d.rectangle([290, cy0, 712, cy0 + 44], fill=art.col('G'))
    d.rectangle([290, cy0 + 45, 712, cy0 + 89], fill=art.col('Q'))
    for i, n in enumerate(cursors):
        s = art.sprites[n]
        x = 296 + i * 42
        art.paste(cv, n, x, cy0 + 4, 2)
        art.paste(cv, n, x, cy0 + 49, 2)
    # second-cursor recolor demo (same sprite, K/W swapped for alert colours)
    rec = {'K': art.col('r'), 'W': (0xF2, 0xC4, 0xBC)}
    if 'cursor_arrow' in art.sprites:
        art.paste(cv, 'cursor_arrow', 690, cy0 + 49, 2, rec)
    # company logo on paper
    if 'logo_company' in art.sprites:
        d.rectangle([6, 470, 90, 530], fill=art.col('P'))
        art.paste(cv, 'logo_company', 16, 484)
        art.paste(cv, 'logo_company', 50, 470 + 14, 1)
    # camera overlay mock
    d.rectangle([100, 470, 280, 530], fill=(18, 24, 22))
    for yy in range(470, 531, 2):
        d.line([100, yy, 280, yy], fill=(26, 34, 31))
    if 'rec_dot' in art.sprites:
        art.paste(cv, 'rec_dot', 106, 476)
    draw_text(cv, 'REC  CAM 04  23:47:12', 114, 475, (230, 230, 220))
    if 'badge_dot' in art.sprites:
        art.paste(cv, 'badge_dot', 270, 520)
    if 'caret_block' in art.sprites:
        d.rectangle([104, 506, 200, 522], fill=art.col('W'))
        draw_text(cv, 'ADMIN', 108, 510, art.col('K'))
        art.paste(cv, 'caret_block', 108 + text_width('ADMIN') + 2, 509)
    if 'selection_marquee_h' in art.sprites:
        tile = art.image('selection_marquee_h')
        for xx in range(210, 276, 4):
            cv.alpha_composite(tile, (xx, 510))
            cv.alpha_composite(tile, (xx, 522))

    # ---- taskbar
    ty0 = VH - 24
    d.rectangle([0, ty0, VW, VH], fill=art.col('G'))
    d.line([0, ty0, VW, ty0], fill=art.col('L'))
    d.line([0, ty0 + 1, VW, ty0 + 1], fill=art.col('W'))
    bevel(d, 3, ty0 + 4, 74, VH - 3, art)
    art.paste(cv, 'icon_nexus', 7, ty0 + 5)
    draw_text(cv, 'NEXUS', 27, ty0 + 9, art.col('K'))
    bevel(d, 80, ty0 + 4, 230, VH - 3, art, face='L', sunken=True)
    art.paste(cv, 'icon_folder_open', 84, ty0 + 5)
    draw_text(cv, 'RECLAIMED', 104, ty0 + 9, art.col('K'))
    bevel(d, VW - 90, ty0 + 4, VW - 4, VH - 3, art, face='G', sunken=True)
    art.paste(cv, 'icon_mail_unread', VW - 86, ty0 + 5)
    draw_text(cv, '11:47 PM', VW - 64, ty0 + 9, art.col('K'))
    # busy cursor on the desktop, second cursor on the window
    art.paste(cv, 'cursor_busy_0', 250, 300, 2)

    big = cv.resize((VW * 2, VH * 2), Image.NEAREST)
    big.convert('RGB').save(os.path.join(out_dir, 'desktop_preview.png'))


# --------------------------------------------------------------------------
# Review zooms
# --------------------------------------------------------------------------
def review(art, out_dir):
    rdir = os.path.join(out_dir, 'review')
    os.makedirs(rdir, exist_ok=True)
    groups = {
        'icons_a': [n for n in art.order if n.startswith('icon_')][:16],
        'icons_b': [n for n in art.order if n.startswith('icon_')][16:],
        'cursors': [n for n in art.order if n.startswith('cursor_')],
        'glyphs': [n for n in art.order if n.startswith('glyph_')] +
                  [n for n in art.order if not n.startswith(('icon_', 'cursor_', 'glyph_', 'logo_'))],
        'logos': [n for n in art.order if n.startswith('logo_')],
    }
    for gname, names in groups.items():
        if not names:
            continue
        scale = 8 if gname != 'logos' else 6
        font = ui_font(12)
        cells = []
        for n in names:
            s = art.sprites[n]
            cells.append((n, s['w'] * scale, s['h'] * scale))
        per_row = 8 if gname.startswith('icons') else 6
        rows = [cells[i:i + per_row] for i in range(0, len(cells), per_row)]
        cw = max(c[1] for c in cells) + 16
        rh = max(c[2] for c in cells) + 22
        W = per_row * cw + 8
        H = len(rows) * rh * 2 + 8
        img = Image.new('RGBA', (W, H), (0, 0, 0, 255))
        d = ImageDraw.Draw(img)
        for bi, bg in enumerate((LIGHT_BG, DARK_BG)):
            oy = bi * len(rows) * rh
            d.rectangle([0, oy, W, oy + len(rows) * rh], fill=bg)
            for ri, row in enumerate(rows):
                for ci, (n, iw, ih) in enumerate(row):
                    x = 8 + ci * cw
                    y = oy + 4 + ri * rh
                    art.paste(img, n, x, y, scale)
                    fg = (20, 19, 17) if bi == 0 else (235, 232, 222)
                    d.text((x, y + ih + 2), n, fill=fg, font=font)
        img.convert('RGB').save(os.path.join(rdir, f'{gname}.png'))
    # 1x strip of icons, magnified 3x with nearest neighbour (true 1x shapes)
    icons = [n for n in art.order if n.startswith('icon_')]
    strip = Image.new('RGBA', (len(icons) * 20 + 4, 44), (0, 0, 0, 255))
    d = ImageDraw.Draw(strip)
    d.rectangle([0, 0, strip.width, 21], fill=LIGHT_BG)
    d.rectangle([0, 22, strip.width, 44], fill=DARK_BG)
    for i, n in enumerate(icons):
        art.paste(strip, n, 4 + i * 20, 3)
        art.paste(strip, n, 4 + i * 20, 25)
    strip.resize((strip.width * 3, strip.height * 3), Image.NEAREST).convert('RGB').save(
        os.path.join(rdir, 'icons_1x_x3.png'))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument('--json', default=os.path.join(here, 'out', 'sprites.json'))
    ap.add_argument('--out', default=os.path.join(here, 'out'))
    ap.add_argument('--review', action='store_true')
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    art = Art(args.json)
    contact_sheet(art, args.out)
    desktop_preview(art, args.out)
    if args.review:
        review(art, args.out)
    print(f"rendered {len(art.order)} sprites -> {args.out}")


if __name__ == '__main__':
    main()
