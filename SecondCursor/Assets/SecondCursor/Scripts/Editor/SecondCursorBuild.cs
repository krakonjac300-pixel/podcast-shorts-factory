using System.IO;
using SecondCursor.Core.Art;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Release build for Steam (Windows 64-bit): applies the player settings a store build needs, generates the
    /// application icon from the game's own pixel art (a white and an inverted cursor), and builds to
    /// &lt;project&gt;/Builds/Windows/SecondCursor.exe.
    /// </summary>
    public static class SecondCursorBuild
    {
        public const string IconPath = "Assets/SecondCursor/Art/AppIcon.png";
        public static string OutputDir => Path.GetFullPath("Builds/Windows");
        public static string ExePath => Path.Combine(OutputDir, "SecondCursor.exe");

        [MenuItem("SECOND CURSOR/Apply Release Settings", priority = 21)]
        public static void ApplyReleaseSettings()
        {
            PlayerSettings.productName = "SECOND CURSOR";
            // Kept stable forever: it names the save folder (AppData/LocalLow/<company>/<product>).
            PlayerSettings.companyName = "SecondCursorGame";
            var v = PlayerSettings.bundleVersion;
            if (string.IsNullOrEmpty(v) || v == "0.1" || v == "0.1.0" || v == "1.0") PlayerSettings.bundleVersion = "0.9.0";
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            // D3D11 only: nothing here needs D3D12, and it doubles every shader compile and adds 4.6 MB.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            // Leftovers of the removed AI packages.
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone,
                string.Join(";", System.Array.FindAll(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';'),
                    d => d.Length > 0 && d != "SENTIS_ANALYTICS_ENABLED" && d != "APP_UI_EDITOR_ONLY")));
            EditorBuildSettings.RemoveConfigObject("com.unity.dt.app-ui");
            // Only the game scene ships (the template's SampleScene stays out of every build).
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SecondCursorProjectSetup.ScenePath, true) };
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            // A built game pauses itself when it loses focus (PauseMenu), so it does not need to run behind.
            PlayerSettings.runInBackground = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            GenerateIcon();
            Debug.Log("[SYSTEM] SECOND CURSOR release settings applied.");
        }

        [MenuItem("SECOND CURSOR/Build Windows (Steam)", priority = 22)]
        public static void BuildMenu()
        {
            var report = BuildWindows();
            EditorUtility.DisplayDialog("SECOND CURSOR build", Summary(report), "OK");
        }

        public static BuildReport BuildWindows()
        {
            ApplyReleaseSettings();
            Directory.CreateDirectory(OutputDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SecondCursorProjectSetup.ScenePath },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            return BuildPipeline.BuildPlayer(options);
        }

        public static string Summary(BuildReport report)
        {
            var s = report.summary;
            return s.result + " in " + s.totalTime.TotalSeconds.ToString("0") + " s, " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, "
                   + s.totalErrors + " error(s), " + s.totalWarnings + " warning(s)\n" + ExePath;
        }

        /// <summary>Icons at every size Windows asks for: drawn natively from 64 px up, box-filtered below.</summary>
        public static void GenerateIcon()
        {
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var big = RenderIcon256();
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                var px = size == 256 ? big : Resample(big, 256, size);
                string path = size == 256 ? IconPath : IconPath.Replace(".png", "_" + size + ".png");
                icons[i] = SaveIcon(px, size, path);
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icons[System.Array.IndexOf(sizes, 256) >= 0 ? System.Array.IndexOf(sizes, 256) : 0] }, IconKind.Any);
        }

        /// <summary>Box filter (downscale) or nearest (upscale) from a square RGBA buffer.</summary>
        static Color32[] Resample(Color32[] src, int from, int to)
        {
            var dst = new Color32[to * to];
            float step = from / (float)to;
            for (int y = 0; y < to; y++)
                for (int x = 0; x < to; x++)
                {
                    if (to >= from) { dst[y * to + x] = src[(int)(y * step) * from + (int)(x * step)]; continue; }
                    int x0 = (int)(x * step), y0 = (int)(y * step), x1 = Mathf.Max(x0 + 1, (int)((x + 1) * step)), y1 = Mathf.Max(y0 + 1, (int)((y + 1) * step));
                    float r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++) { var c = src[yy * from + xx]; float w = c.a / 255f; r += c.r * w; g += c.g * w; b += c.b * w; a += c.a; n++; }
                    float alpha = a / n;
                    float norm = alpha > 0 ? 255f / alpha : 0f;
                    dst[y * to + x] = new Color32((byte)Mathf.Clamp(r / n * norm, 0, 255), (byte)Mathf.Clamp(g / n * norm, 0, 255), (byte)Mathf.Clamp(b / n * norm, 0, 255), (byte)alpha);
                }
            return dst;
        }

        static Texture2D SaveIcon(Color32[] px, int size, string path)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets/SecondCursor/Art");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>256x256: the player's white arrow with the second, inverted arrow behind it, on CRT glass.</summary>
        static Color32[] RenderIcon256()
        {
            const int size = 256, scale = 9;
            var px = new Color32[size * size];
            var glassA = new Color32(0x10, 0x24, 0x22, 0xFF);
            var glassB = new Color32(0x1C, 0x3A, 0x36, 0xFF);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Rounded square with a soft vertical gradient and faint scanlines.
                    float dx = Mathf.Max(0f, Mathf.Abs(x - 127.5f) - 96f), dy = Mathf.Max(0f, Mathf.Abs(y - 127.5f) - 96f);
                    bool inside = dx * dx + dy * dy <= 30f * 30f;
                    var c = Color32.Lerp(glassA, glassB, y / (float)size);
                    if (y % 4 == 0) c = Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.25f);
                    px[y * size + x] = inside ? c : new Color32(0, 0, 0, 0);
                }
            var arrow = PixelArtData.Get("cursor_arrow");
            // Second cursor (inverted palette), offset down-right, drawn first so yours sits on top.
            Stamp(px, size, arrow, 104, 168, scale, true);
            Stamp(px, size, arrow, 64, 218, scale, false);
            return px;
        }

        /// <summary>Draws a pixel sprite into a bottom-up pixel buffer with its top-left at (left, top).</summary>
        static void Stamp(Color32[] px, int size, PixelSprite sprite, int left, int top, int scale, bool inverted)
        {
            if (sprite == null) return;
            for (int row = 0; row < sprite.Height; row++)
                for (int col = 0; col < sprite.Width; col++)
                {
                    char ch = sprite.Rows[row][col];
                    Color32 c;
                    if (ch == 'K') c = inverted ? new Color32(0xE6, 0xEC, 0xEA, 0xFF) : new Color32(0x0B, 0x0E, 0x0D, 0xFF);
                    else if (ch == 'W') c = inverted ? new Color32(0x0B, 0x0E, 0x0D, 0xFF) : new Color32(0xF4, 0xF4, 0xEE, 0xFF);
                    else continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int x = left + col * scale + sx;
                            int y = top - row * scale - sy;
                            if (x >= 0 && x < size && y >= 0 && y < size) px[y * size + x] = c;
                        }
                }
        }
    }
}
