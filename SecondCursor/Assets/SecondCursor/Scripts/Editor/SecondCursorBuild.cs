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
        public static string DemoOutputDir => Path.GetFullPath("Builds/WindowsDemo");
        public static string DemoExePath => Path.Combine(DemoOutputDir, "SecondCursorDemo.exe");

        internal const string ContentFolder = "Assets/SecondCursor/Resources/Content";
        internal const string DemoExcludedFolder = "Assets/SecondCursor/_DemoExcluded";
        /// <summary>
        /// Content the demo must not ship: anything under Resources is built in even if no code loads it. "full" holds the
        /// base strings only Nights 2 and 3 use (hidden achievement text, rounds, log off, the Restricted code).
        /// </summary>
        internal static readonly string[] DemoExcludedContent = { "night2", "night3", "full" };
        /// <summary>Symbols and burst debug folders are moved here after each build (never ship them).</summary>
        public static string SymbolsDir => Path.GetFullPath("Builds/Symbols");

        /// <summary>SessionState: a demo build is moving content around (the editor-load restore must not touch it).</summary>
        const string DemoBuildRunningKey = "SecondCursor.DemoBuildRunning";
        internal static bool DemoBuildRunning
        {
            get => SessionState.GetBool(DemoBuildRunningKey, false);
            private set => SessionState.SetBool(DemoBuildRunningKey, value);
        }

        /// <summary>Valve's public test app: never ship a Steam build with it.</summary>
        const uint TestAppId = 480;
        const string RuntimeAsmdef = "Assets/SecondCursor/Scripts/Runtime/SecondCursor.Runtime.asmdef";
        const string SteamworksAssembly = "com.rlabrecque.steamworks.net";

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
            DisableTelemetry();
            GenerateIcon();
            Debug.Log("[SYSTEM] SECOND CURSOR release settings applied.");
        }

        /// <summary>
        /// The store page says the game has no network code: switch off Unity's engine diagnostics, crash reporting,
        /// analytics and hardware statistics for the player (the settings have no public API, so SerializedObject).
        /// </summary>
        static void DisableTelemetry()
        {
            PlayerSettings.enableCrashReportAPI = false;
            const string connect = "ProjectSettings/UnityConnectSettings.asset";
            SetSettingsFlag(connect, "InsightsSettings.m_EngineDiagnosticsEnabled", false);
            SetSettingsFlag(connect, "InsightsSettings.m_Enabled", false);
            SetSettingsFlag(connect, "CrashReportingSettings.m_EnableCloudDiagnosticsReporting", false);
            SetSettingsFlag(connect, "UnityAnalyticsSettings.m_Enabled", false);
            SetSettingsFlag(connect, "UnityAnalyticsSettings.m_InitializeOnStartup", false);
            SetSettingsFlag(connect, "PerformanceReportingSettings.m_Enabled", false);
            SetSettingsFlag(connect, "UnityAdsSettings.m_Enabled", false);
            SetSettingsFlag(connect, "UnityPurchasingSettings.m_Enabled", false);
            // "submitAnalytics" is the player's hardware statistics (Disable HW Statistics).
            SetSettingsFlag("ProjectSettings/ProjectSettings.asset", "submitAnalytics", false);
            AssetDatabase.SaveAssets();
        }

        static void SetSettingsFlag(string path, string property, bool value)
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
            if (objects == null || objects.Length == 0 || objects[0] == null)
            {
                Debug.LogWarning("[SYSTEM] Release settings: could not open " + path);
                return;
            }
            var so = new SerializedObject(objects[0]);
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogWarning("[SYSTEM] Release settings: " + path + " has no " + property);
                return;
            }
            bool current = p.propertyType == SerializedPropertyType.Boolean ? p.boolValue : p.intValue != 0;
            if (current == value) return;
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
            else p.intValue = value ? 1 : 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(objects[0]);
            Debug.Log("[SYSTEM] Release settings: " + property + " = " + value);
        }

        /// <summary>
        /// File > Build Settings (Build, Build And Run) applies the release settings too, like the menu items below. A build started by a
        /// script (BuildPipeline.BuildPlayer) does not pass here: the build guard refuses it when the settings are not the release ones.
        /// </summary>
        [InitializeOnLoadMethod]
        static void RegisterBuildPlayerHandler()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(options =>
            {
                ApplyReleaseSettings();
                options.scenes = new[] { SecondCursorProjectSetup.ScenePath };
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            });
        }

        /// <summary>What differs from the release settings that matter most (null: none). The build guard calls this for every build.</summary>
        internal static string ReleaseSettingsProblem()
        {
            if (PlayerSettings.runInBackground)
                return "Run In Background is on: a built game pauses itself when it loses focus (SECOND CURSOR > Apply Release Settings)";
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) || apis.Length != 1 || apis[0] != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                return "the graphics API is not Direct3D 11 only (SECOND CURSOR > Apply Release Settings)";
            if (PlayerSettings.enableCrashReportAPI)
                return "crash reporting is on: the store page says the game has no network code (SECOND CURSOR > Apply Release Settings)";
            return null;
        }

        [MenuItem("SECOND CURSOR/Build Windows (Steam)", priority = 22)]
        public static void BuildMenu()
        {
            var report = BuildWindows();
            EditorUtility.DisplayDialog("SECOND CURSOR build", Summary(report), "OK");
        }

        public static BuildReport BuildWindows()
        {
            // A left-over "define SC_DEMO on" would turn the full game into a demo with Nights 2 and 3 inside.
            if (HasStandaloneDefine("SC_DEMO"))
                throw new BuildFailedException("SC_DEMO is in the Standalone scripting defines: remove it (bridge: define SC_DEMO off) before building the full game");
            // A demo build that crashed half way may have left Nights 2 and 3 outside Resources.
            RestoreDemoExcluded();
            RequireFullContent();
            CheckSteamRelease(false);
            ApplyReleaseSettings();
            CleanOutput(OutputDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SecondCursorProjectSetup.ScenePath },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            MoveSymbolsOut(OutputDir, "Windows");
            StripUnusedGraphicsFiles(OutputDir, "Windows");
            return report;
        }

        /// <summary>The full game ships every content folder the demo leaves out.</summary>
        internal static void RequireFullContent()
        {
            foreach (var name in DemoExcludedContent)
                if (!AssetDatabase.IsValidFolder(ContentFolder + "/" + name))
                    throw new BuildFailedException("The full game needs " + ContentFolder + "/" + name + " (see SECOND CURSOR > Restore Demo-Excluded Content)");
        }

        internal static bool HasStandaloneDefine(string name)
        {
            foreach (var d in PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';'))
                if (d.Trim() == name) return true;
            return false;
        }

        /// <summary>
        /// Steamworks.NET compiled in (STEAMWORKS_NET in the defines, or the runtime asmdef's version define with the
        /// package present).
        /// </summary>
        static bool SteamworksEnabled()
        {
            if (HasStandaloneDefine("STEAMWORKS_NET")) return true;
            bool asmdefDefines = File.Exists(RuntimeAsmdef) && File.ReadAllText(RuntimeAsmdef).Contains("STEAMWORKS_NET");
            if (!asmdefDefines) return false;
            foreach (var asm in UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player))
                if (asm.name == SteamworksAssembly) return true;
            return false;
        }

        /// <summary>A Steam build must carry the real App IDs and the Steamworks.NET license text in Credits.</summary>
        static void CheckSteamRelease(bool demo)
        {
            string problem = SteamReleaseProblem(demo, SteamworksEnabled());
            if (problem != null) throw new BuildFailedException(problem);
        }

        /// <summary>Why a build with Steamworks compiled in must not ship (null: nothing). Test bridge "steamcheck".</summary>
        internal static string SteamReleaseProblem(bool demo, bool steamworks)
        {
            if (!steamworks) return null;
            uint full = Game.SteamBridge.FullGameAppId, demoId = Game.SteamBridge.DemoAppId;
            if (full == TestAppId || (demo && demoId == TestAppId))
                return "STEAMWORKS_NET is on but SteamBridge still uses the test App ID " + TestAppId + ": set FullGameAppId and DemoAppId";
            if (!ContentHasKey("credits.steamworks"))
                return "STEAMWORKS_NET is on but strings.json has no credits.steamworks (the Steamworks.NET MIT license text)";
            return null;
        }

        static bool ContentHasKey(string key)
        {
            foreach (var file in new[] { ContentFolder + "/strings.json", ContentFolder + "/full/strings.json" })
                if (File.Exists(file) && File.ReadAllText(file).Contains("\"" + key + "\"")) return true;
            return false;
        }

        /// <summary>A fresh output folder: files of an older build (a removed DLL, an old data file) must not ship.</summary>
        static void CleanOutput(string dir)
        {
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                foreach (var d in Directory.GetDirectories(dir)) Directory.Delete(d, true);
            }
            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Phase H: Unity copies the D3D12 Agility SDK (D3D12\D3D12Core.dll) into every Windows player. This game renders with
        /// Direct3D 11 only, so it is never loaded: it goes to Builds/Symbols/&lt;build&gt;/NotShipped instead of shipping (4.5 MB).
        /// The DirectStorage runtime (dstorage.dll, dstoragecore.dll, 1.7 MB) must stay although DirectStorage is off: a
        /// player without it hangs at startup before writing its log (tested on the Phase H build). If Direct3D 12 is ever
        /// enabled, D3D12 stays in the build.
        /// </summary>
        static void StripUnusedGraphicsFiles(string outputDir, string build)
        {
            if (!Directory.Exists(outputDir)) return;
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            bool d3d11Only = !PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)
                             && apis.Length == 1 && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;
            if (!d3d11Only)
            {
                Debug.Log("[SYSTEM] Build: D3D12 kept (the build uses more than Direct3D 11)");
                return;
            }
            string dest = Path.Combine(SymbolsDir, build, "NotShipped");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            string d3d12 = Path.Combine(outputDir, "D3D12");
            if (Directory.Exists(d3d12))
            {
                Directory.Move(d3d12, Path.Combine(dest, "D3D12"));
                Debug.Log("[SYSTEM] Build: moved D3D12 out (Direct3D 11 only)");
            }
        }

        /// <summary>The *_BackUpThisFolder_ButDontShipItWithYourGame folders go to Builds/Symbols/&lt;build&gt;.</summary>
        static void MoveSymbolsOut(string outputDir, string build)
        {
            if (!Directory.Exists(outputDir)) return;
            foreach (var d in Directory.GetDirectories(outputDir, "*_BackUpThisFolder_ButDontShipItWithYourGame"))
            {
                string dest = Path.Combine(SymbolsDir, build, Path.GetFileName(d));
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? SymbolsDir);
                Directory.Move(d, dest);
                Debug.Log("[SYSTEM] Build: moved " + Path.GetFileName(d) + " to " + dest);
            }
        }

        public static string Summary(BuildReport report) => Summary(report, ExePath);

        public static string Summary(BuildReport report, string exePath)
        {
            var s = report.summary;
            return s.result + " in " + s.totalTime.TotalSeconds.ToString("0") + " s, " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, "
                   + s.totalErrors + " error(s), " + s.totalWarnings + " warning(s)\n" + exePath;
        }

        // ------------------------------------------------------------------ the free demo (Night 1)

        [MenuItem("SECOND CURSOR/Build Windows Demo (SC_DEMO)", priority = 23)]
        public static void BuildDemoMenu()
        {
            var report = BuildWindowsDemo();
            EditorUtility.DisplayDialog("SECOND CURSOR demo build", Summary(report, DemoExePath), "OK");
        }

        /// <summary>
        /// The demo: SC_DEMO defined, its own product name (so its saves are separate), Night 1 only. The night2 and
        /// night3 content folders are moved out of Resources for the build (AssetDatabase.MoveAsset keeps their GUIDs)
        /// and always moved back, even if the build fails.
        /// </summary>
        public static BuildReport BuildWindowsDemo()
        {
            RestoreDemoExcluded(); // leftovers of a build that crashed half way
            CheckSteamRelease(true);
            ApplyReleaseSettings();
            string product = PlayerSettings.productName;
            CleanOutput(DemoOutputDir);
            BuildReport report;
            try
            {
                DemoBuildRunning = true;
                MoveDemoExcludedOut();
                PlayerSettings.productName = "SECOND CURSOR Demo";
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SecondCursorProjectSetup.ScenePath },
                    locationPathName = DemoExePath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { "SC_DEMO" },
                };
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.productName = product;
                DemoBuildRunning = false;
                RestoreDemoExcluded();
                // The build wrote the demo's product name to ProjectSettings.asset: write the real one back.
                AssetDatabase.SaveAssets();
            }
            MoveSymbolsOut(DemoOutputDir, "WindowsDemo");
            StripUnusedGraphicsFiles(DemoOutputDir, "WindowsDemo");
            return report;
        }

        /// <summary>
        /// Editor load: content left in _DemoExcluded by a demo build that never finished (Unity closed or crashed
        /// mid-build) goes back into Resources, so Play mode and other builds see Nights 2 and 3 again. Skipped while
        /// a demo build runs in this editor session.
        /// </summary>
        [InitializeOnLoadMethod]
        static void RestoreAfterInterruptedDemoBuild()
        {
            if (DemoBuildRunning)
            {
                Debug.Log("[SYSTEM] Editor load during a demo build: demo-excluded content left where it is");
                return;
            }
            EditorApplication.update -= RestoreWhenIdle;
            EditorApplication.update += RestoreWhenIdle;
        }

        /// <summary>Runs once the editor has settled after loading (no import or compile in progress).</summary>
        static void RestoreWhenIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= RestoreWhenIdle;
            // The folder test reads the disk: right after a reload the asset database may not list it yet.
            bool leftOver = AssetDatabase.IsValidFolder(DemoExcludedFolder) || Directory.Exists(DemoExcludedFolder);
            if (DemoBuildRunning || BuildPipeline.isBuildingPlayer || !leftOver) return;
            Debug.LogWarning("[SYSTEM] Content from an interrupted demo build is still in " + DemoExcludedFolder + ": restoring it");
            RestoreDemoExcluded();
        }

        /// <summary>Test bridge "democrash": the content a demo build moves out, left there as if Unity had died mid-build.</summary>
        internal static void SimulateInterruptedDemoBuild() => MoveDemoExcludedOut();

        static void MoveDemoExcludedOut()
        {
            if (!AssetDatabase.IsValidFolder(DemoExcludedFolder)) AssetDatabase.CreateFolder("Assets/SecondCursor", "_DemoExcluded");
            foreach (var name in DemoExcludedContent)
            {
                string from = ContentFolder + "/" + name, to = DemoExcludedFolder + "/" + name;
                if (!AssetDatabase.IsValidFolder(from)) continue;
                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error)) throw new BuildFailedException("Could not move " + from + " out of the demo: " + error);
                Debug.Log("[SYSTEM] Demo build: moved " + from + " out of Resources");
            }
        }

        /// <summary>Puts the night2 and night3 content back into Resources (after a demo build, or after one crashed).</summary>
        [MenuItem("SECOND CURSOR/Restore Demo-Excluded Content", priority = 24)]
        public static void RestoreDemoExcluded()
        {
            if (!AssetDatabase.IsValidFolder(DemoExcludedFolder)) return;
            foreach (var name in DemoExcludedContent)
            {
                string from = DemoExcludedFolder + "/" + name, to = ContentFolder + "/" + name;
                if (!AssetDatabase.IsValidFolder(from)) continue;
                if (AssetDatabase.IsValidFolder(to))
                {
                    Debug.LogError("[SYSTEM] Both " + from + " and " + to + " exist: merge them by hand");
                    continue;
                }
                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error)) Debug.LogError("[SYSTEM] Could not restore " + to + ": " + error);
                else Debug.Log("[SYSTEM] Demo build: restored " + to);
            }
            // The empty holder folder goes (it only ever holds the moved content).
            if (AssetDatabase.GetSubFolders(DemoExcludedFolder).Length == 0
                && AssetDatabase.FindAssets(string.Empty, new[] { DemoExcludedFolder }).Length == 0)
                AssetDatabase.DeleteAsset(DemoExcludedFolder);
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

    /// <summary>
    /// Guards every player build (the menu items, File > Build Settings, a script): the release settings must be on, and outside a demo
    /// build Nights 2 and 3 must be in Resources and SC_DEMO must not be defined, otherwise the "full" game ships as a broken demo.
    /// </summary>
    sealed class SecondCursorBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string settings = SecondCursorBuild.ReleaseSettingsProblem();
            if (settings != null) throw new BuildFailedException("Release settings: " + settings);
            if (SecondCursorBuild.DemoBuildRunning) return;
            if (AssetDatabase.IsValidFolder(SecondCursorBuild.DemoExcludedFolder))
                throw new BuildFailedException(SecondCursorBuild.DemoExcludedFolder + " exists (an interrupted demo build): run SECOND CURSOR > Restore Demo-Excluded Content first");
            if (SecondCursorBuild.HasStandaloneDefine("SC_DEMO"))
                throw new BuildFailedException("SC_DEMO is in the Standalone scripting defines: build the demo with SECOND CURSOR > Build Windows Demo, or remove the define");
            SecondCursorBuild.RequireFullContent();
        }
    }
}
