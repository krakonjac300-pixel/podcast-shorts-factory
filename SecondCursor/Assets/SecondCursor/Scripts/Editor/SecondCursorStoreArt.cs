using System;
using System.Collections.Generic;
using System.IO;
using SecondCursor.Core.Art;
using SecondCursor.Core.Game;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// SECOND CURSOR > Render Store Art: the Steam capsules, library assets, community and client icons and the 19
    /// achievement icons (unlocked and locked), drawn from the game's own pixel sprites and bitmap font into
    /// &lt;project&gt;/Builds/StoreArt (outside Assets, never imported, never shipped). The motif is the app icon's:
    /// the player's white arrow and the inverted second cursor over CRT glass. Capsules carry the title only (Valve's
    /// capsule rules and the capsule brief in Docs/Launch/MarketingPack.md); the tagline sits on the page background.
    /// Screenshots are taken separately through the test bridge (storeshot).
    /// </summary>
    public static class SecondCursorStoreArt
    {
        public static string OutputDir => Path.GetFullPath("Builds/StoreArt");

        public const string Title1 = "SECOND", Title2 = "CURSOR", Tagline = "Someone else is logged in.";

        static readonly Color32 OffWhite = new Color32(0xF7, 0xF5, 0xEE, 0xFF);
        static readonly Color32 EchoRed = new Color32(0xB0, 0x33, 0x28, 0xFF);
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Ink = new Color32(0x0B, 0x0E, 0x0D, 0xFF);
        static readonly Color32 PaleOutline = new Color32(0xE6, 0xEC, 0xEA, 0xFF);
        static readonly Color32 ArrowWhite = new Color32(0xF4, 0xF4, 0xEE, 0xFF);
        static readonly Color32 GaryAmber = new Color32(0xD9, 0x9A, 0x3A, 0xFF);

        [MenuItem("SECOND CURSOR/Render Store Art", priority = 40)]
        public static void RenderMenu()
        {
            string dir = Render();
            EditorUtility.DisplayDialog("SECOND CURSOR store art", "Rendered into\n" + dir, "OK");
        }

        /// <summary>Renders every asset; returns the output folder.</summary>
        public static string Render()
        {
            string dir = OutputDir;
            Directory.CreateDirectory(dir);
            KeyArt(920, 430, false).SavePng(Path.Combine(dir, "capsule_header_920x430.png"));
            SmallCapsule().SavePng(Path.Combine(dir, "capsule_small_462x174.png"));
            KeyArt(1232, 706, true).SavePng(Path.Combine(dir, "capsule_main_1232x706.png"));
            Tall(748, 896).SavePng(Path.Combine(dir, "capsule_vertical_748x896.png"));
            Tall(600, 900).SavePng(Path.Combine(dir, "library_capsule_600x900.png"));
            KeyArt(920, 430, false).SavePng(Path.Combine(dir, "library_header_920x430.png"));
            Hero().SavePng(Path.Combine(dir, "library_hero_3840x1240.png"));
            Logo().SavePng(Path.Combine(dir, "library_logo_1280x720.png"));
            PageBackground().SavePng(Path.Combine(dir, "page_background_1438x810.png"));
            var community = IconTile(184);
            community.SaveJpg(Path.Combine(dir, "community_icon_184x184.jpg"));
            community.SavePng(Path.Combine(dir, "community_icon_184x184.png"));
            var client256 = IconTile(256);
            var client48 = IconTile(48);
            var client32 = IconTile(32);
            var client16 = Downscale(client32, 16);
            client256.SavePng(Path.Combine(dir, "client_icon_256x256.png"));
            client32.SavePng(Path.Combine(dir, "client_icon_32x32.png"));
            WriteIco(Path.Combine(dir, "client_icon.ico"), client16, client32, client48, client256);
            int n = Achievements(Path.Combine(dir, "achievements"));
            Debug.Log("[SYSTEM] Store art rendered into " + dir + " (" + n + " achievement icons, unlocked and locked)");
            return dir;
        }

        // ------------------------------------------------------------------ the motif

        static PixelSprite Arrow => PixelArtData.Get("cursor_arrow");

        /// <summary>The player's arrow: black outline, white fill.</summary>
        static Color32? PlayerArrow(char ch) => ch == 'K' ? Ink : ch == 'W' ? ArrowWhite : (Color32?)null;

        /// <summary>The second cursor: the same arrow with its colours inverted (pale outline, near-black fill).</summary>
        static Color32? SecondArrow(char ch) => ch == 'K' ? PaleOutline : ch == 'W' ? Ink : (Color32?)null;

        /// <summary>
        /// Two cursors fighting over one file, drawn with the group's top-left at (left, top): the file, the white arrow's
        /// tip on its lower-left, the inverted arrow's tip to its right, and the dotted tug band between the tips.
        /// </summary>
        static void Tug(ArtCanvas c, int left, int top, int cs, bool file = true, bool band = true)
        {
            int fs = FileScale(cs);
            var dat = PixelArtData.Get("icon_file_dat");
            int fLeft = left, fTop = top;
            // Tips: just inside the file's visible edge (columns 2..13), in its lower half.
            var tipW = new Vector2(fLeft + 3 * fs, fTop + 11 * fs);
            var tipD = new Vector2(fLeft + 12 * fs + 4 * cs, fTop + 12 * fs);
            if (file)
            {
                c.Shadow(dat, fLeft, fTop, fs, Mathf.Max(1, fs / 2), 0.45f);
                c.Stamp(dat, fLeft, fTop, fs);
            }
            if (band) c.TugBand(tipW + new Vector2(cs * 0.5f, cs * 0.5f), tipD + new Vector2(cs * 0.5f, cs * 0.5f), Mathf.Max(2, cs / 2), Mathf.Max(2, cs / 2));
            // The second cursor sits behind; a faint shadow keeps its dark body off the dark glass.
            c.Shadow(Arrow, (int)tipD.x, (int)tipD.y, cs, Mathf.Max(1, cs / 3), 0.5f);
            c.Stamp(Arrow, (int)tipD.x, (int)tipD.y, cs, SecondArrow);
            c.Shadow(Arrow, (int)tipW.x, (int)tipW.y, cs, Mathf.Max(1, cs / 3), 0.55f);
            c.Stamp(Arrow, (int)tipW.x, (int)tipW.y, cs, PlayerArrow);
        }

        static int FileScale(int cs) => Mathf.Max(1, Mathf.RoundToInt(cs * 0.72f));

        /// <summary>The tug group's size at cursor scale <paramref name="cs"/> (file, both arrows, their shadows).</summary>
        static Vector2Int TugSize(int cs)
        {
            int fs = FileScale(cs);
            return new Vector2Int(12 * fs + 4 * cs + 11 * cs + cs / 3, 12 * fs + 17 * cs + cs / 3);
        }

        /// <summary>The largest cursor scale whose tug group fits in <paramref name="w"/> x <paramref name="h"/>.</summary>
        static int TugScaleFor(int w, int h)
        {
            int cs = 1;
            while (TugSize(cs + 1).x <= w && TugSize(cs + 1).y <= h) cs++;
            return cs;
        }

        /// <summary>The tug group centred in a box.</summary>
        static void TugIn(ArtCanvas c, int boxLeft, int boxTop, int boxW, int boxH, int cs)
        {
            var size = TugSize(cs);
            Tug(c, boxLeft + (boxW - size.x) / 2, boxTop + (boxH - size.y) / 2, cs);
        }

        /// <summary>The overlapping arrow pair of the app icon (yours in front, the inverted one behind, down-right).</summary>
        static void Pair(ArtCanvas c, int left, int top, int cs, float amount = 1f)
        {
            c.Stamp(Arrow, left + 4 * cs, top + 5 * cs, cs, SecondArrow, amount);
            c.Stamp(Arrow, left, top, cs, PlayerArrow, amount);
        }

        /// <summary>Title lines in the bitmap font, centred on <paramref name="cx"/>; CURSOR gets the red echo.</summary>
        static void LogoLines(ArtCanvas c, string[] lines, int cx, int top, int scale, bool echo, bool shadow = true)
        {
            int lineStep = (PixelFontData.Ascent + 3) * scale;
            for (int i = 0; i < lines.Length; i++)
            {
                int y = top + i * lineStep;
                bool hasCursor = lines[i].Contains(Title2);
                int off = Mathf.Max(1, scale / 2);
                if (shadow) c.TextCentered(lines[i], cx + off, y + off, scale, new Color32(0, 0, 0, 255), true, 0.55f);
                if (echo && hasCursor) c.TextCentered(lines[i], cx + off, y - off / 2, scale, EchoRed, true, 0.9f);
                c.TextCentered(lines[i], cx, y, scale, OffWhite, true);
            }
        }

        static int LogoWidth(string line, int scale) => ArtCanvas.MeasureText(line, true) * scale;

        /// <summary>The biggest font scale that keeps <paramref name="line"/> within <paramref name="width"/> pixels.</summary>
        static int ScaleFor(string line, int width) => Mathf.Max(1, width / Mathf.Max(1, ArtCanvas.MeasureText(line, true)));

        // ------------------------------------------------------------------ capsules and library

        /// <summary>Header (920x430) and main (1232x706): the tug over the lit centre, the title below it (never overlapping).</summary>
        static ArtCanvas KeyArt(int w, int h, bool twoLines)
        {
            var c = new ArtCanvas(w, h, Ink);
            string[] lines = twoLines ? new[] { Title1, Title2 } : new[] { Title1 + " " + Title2 };
            int scale = twoLines
                ? Mathf.Min(ScaleFor(Title1, (int)(w * 0.40f)), Mathf.RoundToInt(h * 0.34f / (PixelFontData.Ascent * 2 + 3)))
                : Mathf.Min(ScaleFor(lines[0], (int)(w * 0.70f)), Mathf.RoundToInt(h * 0.17f / PixelFontData.Ascent));
            int logoH = twoLines ? (PixelFontData.Ascent * 2 + 3) * scale : PixelFontData.Ascent * scale;
            int marginTop = Mathf.RoundToInt(h * 0.07f), marginBottom = Mathf.RoundToInt(h * 0.08f), gap = Mathf.RoundToInt(h * 0.05f);
            int logoTop = h - marginBottom - logoH;
            int boxH = logoTop - gap - marginTop;
            int cs = TugScaleFor((int)(w * 0.8f), boxH);
            c.CrtGlass(w * 0.5f, marginTop + boxH * 0.5f, h * (twoLines ? 0.85f : 1.1f), 3);
            TugIn(c, 0, marginTop, w, boxH, cs);
            LogoLines(c, lines, w / 2, logoTop, scale, true);
            return c;
        }

        /// <summary>Small capsule (462x174): the title on one line at about 90% width, the arrow pair behind the last letters.</summary>
        static ArtCanvas SmallCapsule()
        {
            const int w = 462, h = 174;
            var c = new ArtCanvas(w, h, Ink);
            c.CrtGlass(w * 0.62f, h * 0.5f, w * 0.7f, 0);
            string line = Title1 + " " + Title2;
            int scale = ScaleFor(line, (int)(w * 0.92f));
            int textW = LogoWidth(line, scale);
            int left = (w - textW) / 2;
            int top = (h - PixelFontData.Ascent * scale) / 2;
            // The arrow pair sits behind the last letters, dimmed so the title stays readable at 120x45.
            int cs = Mathf.RoundToInt(h * 0.72f / 22f);
            Pair(c, left + textW - 13 * cs, (h - 22 * cs) / 2, cs, 0.42f);
            // A dark rim around the letters separates them from the arrows behind.
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0) c.TextCentered(line, w / 2 + dx * Mathf.Max(1, scale / 3), top + dy * Mathf.Max(1, scale / 3), scale, Ink, true, 0.9f);
            LogoLines(c, new[] { line }, w / 2, top, scale, false, false);
            return c;
        }

        /// <summary>Vertical capsule (748x896) and library capsule (600x900): the title on top, the tug below it.</summary>
        static ArtCanvas Tall(int w, int h)
        {
            var c = new ArtCanvas(w, h, Ink);
            int scale = ScaleFor(Title2, (int)(w * 0.66f));
            int logoTop = Mathf.RoundToInt(h * 0.08f), logoH = (PixelFontData.Ascent * 2 + 3) * scale;
            int boxTop = logoTop + logoH + Mathf.RoundToInt(h * 0.06f), boxH = Mathf.RoundToInt(h * 0.92f) - boxTop;
            int cs = TugScaleFor((int)(w * 0.82f), boxH);
            c.CrtGlass(w * 0.5f, boxTop + boxH * 0.45f, h * 0.7f, 3);
            LogoLines(c, new[] { Title1, Title2 }, w / 2, logoTop, scale, true);
            TugIn(c, 0, boxTop, w, boxH, cs);
            return c;
        }

        /// <summary>Library hero (3840x1240): no text; the two cursors small and apart across the wide centre.</summary>
        static ArtCanvas Hero()
        {
            const int w = 3840, h = 1240;
            var c = new ArtCanvas(w, h, Ink);
            c.CrtGlass(w * 0.5f, h * 0.52f, h * 1.3f, 4, 0.36f);
            int cs = 11;
            int cy = h / 2;
            var file = PixelArtData.Get("icon_file_dat");
            int fs = 8;
            c.Shadow(file, w / 2 - 8 * fs, cy - 8 * fs, fs, 4, 0.45f);
            c.Stamp(file, w / 2 - 8 * fs, cy - 8 * fs, fs);
            var tipW = new Vector2(w / 2 - 420, cy - 10);
            var tipD = new Vector2(w / 2 + 330, cy + 4);
            c.TugBand(tipW + new Vector2(cs, cs), new Vector2(w / 2 - 6 * fs, cy + 2 * fs), 6, 10);
            c.TugBand(new Vector2(w / 2 + 6 * fs, cy + 2 * fs), tipD + new Vector2(cs, cs), 6, 10);
            c.Shadow(Arrow, (int)tipD.x, (int)tipD.y, cs, 4, 0.5f);
            c.Stamp(Arrow, (int)tipD.x, (int)tipD.y, cs, SecondArrow);
            c.Shadow(Arrow, (int)tipW.x, (int)tipW.y, cs, 4, 0.55f);
            c.Stamp(Arrow, (int)tipW.x, (int)tipW.y, cs, PlayerArrow);
            return c;
        }

        /// <summary>Library logo (1280x720, transparent): the title on two lines with a dark edge for any background.</summary>
        static ArtCanvas Logo()
        {
            const int w = 1280, h = 720;
            var c = new ArtCanvas(w, h, Clear);
            int scale = ScaleFor(Title2, (int)(w * 0.84f));
            int logoH = (PixelFontData.Ascent * 2 + 3) * scale;
            int top = (h - logoH) / 2;
            // A 1-font-pixel dark rim so the off-white reads over a bright hero too.
            int lineStep = (PixelFontData.Ascent + 3) * scale;
            string[] lines = { Title1, Title2 };
            for (int i = 0; i < lines.Length; i++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (dx != 0 || dy != 0) c.TextCentered(lines[i], w / 2 + dx * scale / 3, top + i * lineStep + dy * scale / 3, scale, Ink, true, 0.85f);
            LogoLines(c, lines, w / 2, top, scale, true, false);
            return c;
        }

        /// <summary>Page background (1438x810): dark and quiet, a faint arrow pair, the tagline low on the left.</summary>
        static ArtCanvas PageBackground()
        {
            const int w = 1438, h = 810;
            var c = new ArtCanvas(w, h, Ink);
            c.CrtGlass(w * 0.72f, h * 0.45f, w * 0.6f, 3, 0.22f);
            Pair(c, (int)(w * 0.66f), (int)(h * 0.22f), 16, 0.22f);
            c.Text(Tagline, 60, h - 100, 4, OffWhite, false, 0.35f);
            return c;
        }

        /// <summary>Community and client icons: the app icon's look (rounded CRT square, your arrow over the inverted one).</summary>
        static ArtCanvas IconTile(int size)
        {
            var c = new ArtCanvas(size, size, Clear);
            RoundedGlass(c, size);
            int cs = Mathf.Max(1, Mathf.FloorToInt(size * 0.62f / 22f));
            int pairW = 15 * cs, pairH = 22 * cs;
            Pair(c, (size - pairW) / 2, (size - pairH) / 2, cs);
            return c;
        }

        /// <summary>The app icon's rounded square of CRT glass (transparent corners, faint scanlines).</summary>
        static void RoundedGlass(ArtCanvas c, int size)
        {
            var glassA = new Color32(0x10, 0x24, 0x22, 0xFF);
            var glassB = new Color32(0x1C, 0x3A, 0x36, 0xFF);
            float half = size * 0.5f, inner = size * 0.375f, radius = size * 0.117f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - half) - inner), dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - half) - inner);
                    if (dx * dx + dy * dy > radius * radius) continue;
                    var col = Color32.Lerp(glassB, glassA, y / (float)size);
                    if (size >= 64 && y % 4 == 3) col = Color32.Lerp(col, new Color32(0, 0, 0, 255), 0.25f);
                    c.Set(x, y, col);
                }
        }

        static ArtCanvas Downscale(ArtCanvas src, int size)
        {
            var dst = new ArtCanvas(size, size, Clear);
            int f = src.W / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int yy = 0; yy < f; yy++)
                        for (int xx = 0; xx < f; xx++)
                        {
                            var p = src.Px[(y * f + yy) * src.W + x * f + xx];
                            float wgt = p.a / 255f;
                            r += p.r * wgt; g += p.g * wgt; b += p.b * wgt; a += p.a;
                        }
                    float n = f * f, alpha = a / n, norm = alpha > 0 ? 255f / alpha : 0f;
                    dst.Px[y * size + x] = new Color32((byte)Mathf.Clamp(r / n * norm, 0, 255), (byte)Mathf.Clamp(g / n * norm, 0, 255), (byte)Mathf.Clamp(b / n * norm, 0, 255), (byte)alpha);
                }
            return dst;
        }

        /// <summary>A Windows .ico holding PNG images (Vista and later read PNG entries at any size).</summary>
        static void WriteIco(string path, params ArtCanvas[] images)
        {
            var pngs = new List<byte[]>();
            foreach (var img in images)
            {
                var tex = img.ToTexture();
                pngs.Add(tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            using (var fs = new FileStream(path, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)images.Length);
                int offset = 6 + 16 * images.Length;
                for (int i = 0; i < images.Length; i++)
                {
                    bw.Write((byte)(images[i].W >= 256 ? 0 : images[i].W));
                    bw.Write((byte)(images[i].H >= 256 ? 0 : images[i].H));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write(pngs[i].Length);
                    bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var png in pngs) bw.Write(png);
            }
        }

        // ------------------------------------------------------------------ achievements

        /// <summary>One motif per achievement: a sprite, an optional colour map and an optional corner mark.</summary>
        sealed class Motif
        {
            public string Sprite;
            public Func<char, Color32?> Map;
            public string Mark;
            public Color32 MarkColor = new Color32(0xF7, 0xF5, 0xEE, 0xFF);
            public string Accent; // a second, small sprite in the lower-right corner
            public bool Pair;
        }

        static Dictionary<string, Motif> Motifs()
        {
            Func<char, Color32?> inverted = ch =>
            {
                var p = ArtCanvas.PaletteColor(ch);
                if (p == null) return null;
                var v = p.Value;
                return new Color32((byte)(255 - v.r), (byte)(255 - v.g), (byte)(255 - v.b), v.a);
            };
            Func<char, Color32?> amber = ch => ch == 'K' ? new Color32(0x3A, 0x26, 0x10, 0xFF) : ch == 'W' ? GaryAmber : (Color32?)null;
            return new Dictionary<string, Motif>
            {
                [AchievementIds.Night1] = new Motif { Sprite = "icon_workstation", Mark = "1" },
                [AchievementIds.Night2] = new Motif { Sprite = "icon_workstation", Mark = "2" },
                [AchievementIds.Night3] = new Motif { Sprite = "icon_workstation", Mark = "3" },
                [AchievementIds.EndShred] = new Motif { Sprite = "icon_disposal_full" },
                [AchievementIds.EndKeep] = new Motif { Pair = true },
                [AchievementIds.EndLogOff] = new Motif { Sprite = "icon_shutdown" },
                [AchievementIds.AllEndings] = new Motif { Sprite = "icon_task_done", Mark = "3" },
                [AchievementIds.FirmGrip] = new Motif { Sprite = "cursor_grab", Map = PlayerArrow },
                [AchievementIds.WhiteKnuckles] = new Motif { Sprite = "cursor_grab", Map = PlayerArrow, Mark = "10" },
                [AchievementIds.DoNotRead] = new Motif { Sprite = "icon_file_dat", Accent = "icon_warning" },
                [AchievementIds.RemoteSession] = new Motif { Sprite = "icon_workorders", Map = inverted },
                [AchievementIds.Finished] = new Motif { Sprite = "icon_file_corrupt" },
                [AchievementIds.Half] = new Motif { Sprite = "cursor_hand", Map = amber },
                [AchievementIds.HisGlasses] = new Motif { Sprite = "icon_notepad" },
                [AchievementIds.HerName] = new Motif { Sprite = "icon_staff" },
                [AchievementIds.Authorized] = new Motif { Sprite = "icon_folder_locked" },
                [AchievementIds.RemainSeated] = new Motif { Sprite = "icon_camera" },
                [AchievementIds.NotOnMyShelf] = new Motif { Sprite = "icon_workorders", Accent = "icon_error" },
                [AchievementIds.Watchers] = new Motif { Sprite = "icon_camera", Map = inverted, Accent = "rec_dot" },
            };
        }

        /// <summary>Writes ACH_*.png (achieved) and ACH_*_locked.png (unachieved) at 256x256; returns the count.</summary>
        static int Achievements(string dir)
        {
            Directory.CreateDirectory(dir);
            var motifs = Motifs();
            int n = 0;
            foreach (var def in AchievementIds.All)
            {
                if (!motifs.TryGetValue(def.Id, out var m)) throw new InvalidOperationException("No store art motif for " + def.Id);
                var icon = AchievementIcon(m);
                icon.SavePng(Path.Combine(dir, def.Id + ".png"));
                icon.Locked().SavePng(Path.Combine(dir, def.Id + "_locked.png"));
                n++;
            }
            return n;
        }

        static ArtCanvas AchievementIcon(Motif m)
        {
            const int size = 256;
            var c = new ArtCanvas(size, size, Ink);
            c.CrtGlass(size * 0.5f, size * 0.45f, size * 0.8f, 4, 0.5f);
            // A thin bevel frame like a NEXUS OS window.
            c.FillRect(0, 0, size, 4, new Color32(0xDD, 0xDA, 0xD0, 0xFF), 0.35f);
            c.FillRect(0, 0, 4, size, new Color32(0xDD, 0xDA, 0xD0, 0xFF), 0.35f);
            c.FillRect(0, size - 4, size, 4, new Color32(0, 0, 0, 0xFF), 0.5f);
            c.FillRect(size - 4, 0, 4, size, new Color32(0, 0, 0, 0xFF), 0.5f);
            if (m.Pair)
            {
                int cs = 7;
                Pair(c, (size - 15 * cs) / 2, (size - 22 * cs) / 2, cs);
            }
            else
            {
                var s = PixelArtData.Get(m.Sprite);
                int scale = Mathf.Min(170 / s.Width, 170 / s.Height);
                int left = (size - s.Width * scale) / 2, top = (size - s.Height * scale) / 2 - (m.Mark != null ? 10 : 0);
                c.Shadow(s, left, top, scale, Mathf.Max(2, scale / 2), 0.5f);
                c.Stamp(s, left, top, scale, m.Map);
            }
            if (m.Accent != null)
            {
                var a = PixelArtData.Get(m.Accent);
                int scale = Mathf.Max(3, 64 / Mathf.Max(a.Width, a.Height));
                int left = size - a.Width * scale - 18, top = size - a.Height * scale - 18;
                c.Shadow(a, left, top, scale, 3, 0.6f);
                c.Stamp(a, left, top, scale);
            }
            if (m.Mark != null)
            {
                const int scale = 6;
                int tw = ArtCanvas.MeasureText(m.Mark, true) * scale;
                int left = size - tw - 20, top = size - PixelFontData.Ascent * scale - 18;
                c.Text(m.Mark, left + 3, top + 3, scale, new Color32(0, 0, 0, 255), true, 0.7f);
                c.Text(m.Mark, left, top, scale, m.MarkColor, true);
            }
            return c;
        }

        // ------------------------------------------------------------------ screenshot helpers (bridge storeshot)

        /// <summary>The centred 16:9 part of a capture (drops a letterbox or pillarbox).</summary>
        public static Texture2D CropTo16x9(Texture2D src)
        {
            int w = src.width, h = src.height;
            int cw = w, ch = Mathf.RoundToInt(w * 9f / 16f);
            if (ch > h)
            {
                ch = h;
                cw = Mathf.RoundToInt(h * 16f / 9f);
            }
            if (cw == w && ch == h) return src;
            var dst = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            dst.SetPixels(src.GetPixels((w - cw) / 2, (h - ch) / 2, cw, ch));
            dst.Apply();
            return dst;
        }

        /// <summary>A bilinear resample to exactly <paramref name="w"/> x <paramref name="h"/> (GPU blit).</summary>
        public static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevFilter = src.filterMode;
            src.filterMode = src.width == w && src.height == h ? FilterMode.Point : FilterMode.Bilinear;
            Graphics.Blit(src, rt);
            src.filterMode = prevFilter;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
