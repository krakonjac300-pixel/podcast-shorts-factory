# SECOND CURSOR: Steam release checklist (Windows + Steam Deck)

Audit date: 2026-09-29, snapshot taken 11:15 to 11:35 CEST.
Project audited: `D:\Downloads\Podaci\Project 1` (Unity 6000.6.3f1, URP 17.6.0, Input System 1.20.0 only, `activeInputHandler: 1`).
Game code: `Assets\SecondCursor` (the repo copy under `_work\2026-09-29\repo\SecondCursor` is the source of the unitypackage).

Important context: another session was editing the project while this audit ran. Between 11:17 and 11:22 it added
`Scripts\Editor\SecondCursorBuild.cs` and `Scripts\Runtime\Game\Achievements.cs`, ran `SECOND CURSOR/Apply Release Settings`
(which rewrote `ProjectSettings.asset` and `EditorBuildSettings.asset` at 11:22) and produced a build in
`D:\Downloads\Podaci\Project 1\Builds\Windows` (finished 11:31, "Build Finished, Result: Success"). Every value below is what
was on disk at 11:25 to 11:35. No project file was modified by this audit.

Legend: **P0** = blocks upload or review, or ships something wrong. **P1** = blocks a good 1.0 / Deck Verified. **P2** = polish.

---

## Status after Phase G (2026-09-29)

Everything below this section is the original 11:15 audit; this block says where each item stands now. Details are in
`Docs/HANDOFF.md` (phases E, F and G) and `Docs/Launch/LaunchAudit.md` (the 17:45 re-audit).

### Done in the project (engineering)

| # | Item | Status |
|---|---|---|
| 1 | AI/editor packages and their defines | Done (Phase E prep): manifest trimmed, defines stripped by Apply Release Settings |
| 2 | Esc menu, Quit and flashing choice from the first screen | Done: title Options and Quit, first-launch Full effects / Reduce flashing, pause menu everywhere, Quit asks first (Phase G) |
| 3 | Display, frame rate, cursor confinement | Done (Phase E) |
| 4 | Crash-safe saves, Steam Cloud layout | Done: progress.json and settings.json split, atomic writes with .bak; Phase G: a locked file is retried and never overwritten, only an unparseable file is set aside as .corrupt |
| 5 | Steamworks code | Done in code, package not installed: `SteamBridge` waits for the user's stats before pushing, queues and retries refused pushes (Phase G); builds refuse to ship with App ID 480 or without `credits.steamworks` once STEAMWORKS_NET is on |
| 6 | Prototype copy | Done: per-night end cards, Credits screen; the WISHLIST card is demo-only |
| 7 | Version | Shown in the pause caption and on the title. Still `0.9.0`: set `1.0.0` by hand before the release build (Apply Release Settings leaves 1.0.0 alone) |
| 8 | Icons | App icon generated at every size; Phase G adds the community icon, the client icon (.ico and .png) and all store art (below) |
| 9 | Build hygiene | Done: D3D11 only, only the game scene, output folder cleaned before each build, `*_BackUpThisFolder_ButDontShipItWithYourGame` moved to `Builds/Symbols/<build>` after each build, a build with SC_DEMO in the defines or with demo content still moved out is refused |
| 10 | Steam Deck | Done in code (floating keyboard calls, Deck wording and hints, Large reading text for Jotter, Mail, Help, toasts and the Work Queue detail). Hardware tests still open |
| | Unity telemetry | Done (Phase G): Apply Release Settings turns off engine diagnostics, cloud diagnostics, analytics, performance reporting and hardware statistics, and the crash report API |
| | QA launch arguments | `-scnight` / `-scbeat` never write progress.json; `-scsavedir` logs where saves go |
| | Demo spoilers | Night 2/3 strings and hidden achievement text live in `Content/full` (left out of the demo), Night 2/3 code is compiled out of the demo |

### Generated store art (Phase G, `SECOND CURSOR > Render Store Art`, output `Builds/StoreArt/`)

- Capsules: header 920x430, small 462x174, main 1232x706, vertical 748x896.
- Library: capsule 600x900, header 920x430, hero 3840x1240 (no text), logo 1280x720 (transparent).
- Page background 1438x810, community icon 184x184 (JPG and PNG), client icon 32x32 and 256x256 PNG plus `client_icon.ico` (16, 32, 48, 256).
- `achievements/`: the 19 icons at 256x256 (`ACH_*.png`) and their locked versions (`ACH_*_locked.png`).
- `screenshots/`: 10 shots at 1920x1080 (S0 and S1 only, per MarketingPackFull): title, Night 1 desk, the second cursor arriving, a tug-of-war, the Jotter conversation (two), CAM 03 (two), the remote Work Queue task, the lost three hours.
- These are placeholders good enough for a Coming Soon page; a hand-made key art pass can replace any of them later.

### Owner: you (Steamworks partner site and business)

1. Pay the Steam Direct fee (US$100 per app), sign the distribution agreement, finish bank, tax and identity forms. The 30-day wait before release starts at payment.
2. Create the App IDs: the full game and a separate demo app. Put them in `Game/SteamBridge.cs` (`FullGameAppId`, `DemoAppId`) and set `StoreUrl`.
3. Install Steamworks.NET as described in section 4 and HANDOFF phase E ("How to add Steamworks.NET later"), copy its LICENSE text into `credits.steamworks` (the build refuses to ship without it), rebuild both builds.
4. Store page: short and long description (MarketingPackFull section 2 with the LaunchAudit 2.2 corrections), 20 tags, system requirements (Windows 10 64-bit, DX11), English only, "Coming soon" or "Spring 2027"; upload the capsules, library assets and at least 5 of the screenshots from `Builds/StoreArt`. Coming Soon at least 2 weeks before release; review takes 3 to 5 business days.
5. Content survey: General, Mature Content (text in 5.2) and the Generative AI disclosure. Answer it honestly for code, text and art made with AI tools; the game itself generates nothing at run time.
6. Achievements: enter the 19 from section 4 (names and descriptions are in `Content/strings.json` and `Content/full/strings.json`), upload `Builds/StoreArt/achievements/ACH_*.png` as achieved and `ACH_*_locked.png` as unachieved, add the `TUG_WINS` stat as the progress stat of `ACH_WHITE_KNUCKLES`, then Publish.
7. Depots: one Windows 64-bit depot per app; launch options `SecondCursor.exe` (full) and `SecondCursorDemo.exe` (demo). Upload `Builds/Windows` and `Builds/WindowsDemo`; the Symbols folders already live in `Builds/Symbols` and must never be uploaded (keep the robocopy exclusions and the `FileExclusion` rows anyway).
8. Steam Cloud: Auto-Cloud, root WinAppDataLocalLow, subdirectory `SecondCursorGame/SECOND CURSOR`, pattern `progress.json` only (never settings.json). Test with `testappcloudpaths`.
9. Steam Deck: publish a default Steam Input layout (section 3, D1), run the hardware tests (floating keyboard Enter/Backspace/numeric code, overlay pause, suspend during a tug, trackpad yank speed for `GameRoot.DeckPullSpeedScale`, cloud round trip), then request the Deck compatibility review.
10. Pricing ($6.99 with the regional matrix and a launch discount), a build account with Steam Guard, a trailer (run a flash check on its glitch bursts), and three timed fresh playtests before locking the length line on the store page.

---

## 0. Top 10 blocking items (priority order)

| # | Item | Where | Section |
|---|---|---|---|
| 1 | Remove AI/editor packages: `com.unity.ai.inference` put ~9.4 MB of Sentis shaders into the 89.3 MB build (65 shaders, 2906 of 4033 compiled variants); `com.unity.ai.assistant` is a pre-release (2.20.0-pre.1); leftover defines `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY` | `Packages\manifest.json`, `ProjectSettings.asset` | 2.1 |
| 2 | The Esc menu is unreachable before log-on, so there is no Quit and no Reduce Flashing until after the BIOS/splash glitches, although the disclaimer promises "at any time" | `PauseMenu.cs` line 39, `BootSequence.cs` | 2.2 |
| 3 | Display options are wrong/missing: Windowed then Fullscreen stays at 1280x720 (blurry); no window size, no VSync/frame cap; saved `fullscreen` is never applied; no cursor confinement (a hard "pull away" flings the real mouse to a second monitor in borderless fullscreen) | `PauseMenu.cs` `ToggleDisplay`, `GameRoot.cs` | 2.3 |
| 4 | Save is not crash safe and not cloud ready: non-atomic write, a corrupt file is silently replaced by defaults (losing achievements/endings), settings and progress share one file, `Player.log` lives in the same folder | `SaveSystem.cs` | 2.4 |
| 5 | Steamworks not integrated: no package, `SteamBridge.AppId = 480`, runtime asmdef lacks the Steamworks reference (compile break once `STEAMWORKS_NET` is defined), zero `Achievements.Unlock` call sites | `Achievements.cs`, `SecondCursor.Runtime.asmdef` | 4 |
| 6 | Prototype copy ships in the paid build: end card "WISHLIST NOW" and "Thank you for playing the prototype."; no credits / third-party notices | `Resources\Content\strings.json` | 2.6 |
| 7 | Version is still `0.1.0` (the release script only bumps "", "0.1", "1.0"); not shown in game | `ProjectSettings.asset` `bundleVersion` | 2.7 |
| 8 | Icons: only one generated 256x256 default icon; no hand-made 16/32/48 for the .exe; Steam client and community icons missing | `Assets\SecondCursor\Art\AppIcon.png`, Player > Icon | 2.8 |
| 9 | Build hygiene: D3D11 + D3D12 doubles every shader compile and ships a 4.6 MB `D3D12` folder; URP PC asset ships SSAO, GPU Resident Drawer, terrain shaders; `SampleScene` still enabled in Build Settings; depot must exclude `SecondCursor_BackUpThisFolder_ButDontShipItWithYourGame` | `ProjectSettings.asset`, `Assets\Settings\PC_*`, `EditorBuildSettings.asset` | 2.9 |
| 10 | Steam Deck: no official Steam Input config, no on-screen keyboard call for Notepad, on-screen text says "Esc"/"Enter"/"Right-click", lowercase text is 8 px at 1280x800 (Valve minimum 9 px). Today this is a "Playable" game, not "Verified" | Steamworks + `EventDirector.cs` line 339, `strings.json` | 3 |

---

## 1. What is already fine

Verified on disk (values at 11:25 unless noted):

- [x] **Product name**: `productName: SECOND CURSOR`.
- [x] **Company name**: `companyName: SecondCursorGame` (was `DefaultCompany` until 11:22). It names the save folder
      `%USERPROFILE%\AppData\LocalLow\SecondCursorGame\SECOND CURSOR\` and the registry key `HKCU\Software\SecondCursorGame\SECOND CURSOR`. Freeze it now; never change it after the first public build.
- [x] **Unity splash**: `m_ShowUnitySplashScreen: 0`. Allowed on Unity Personal for Unity 6 games (Unity: "The Made with Unity splash screen will become optional for Unity Personal games made with Unity 6", https://unity.com/blog/unity-is-canceling-the-runtime-fee). See 2.9 for the 2.7 MB logo texture still in the build.
- [x] **Default window**: `defaultIsNativeResolution: 1`, `fullscreenMode: 1` (Fullscreen Window, borderless native), `resizableWindow: 1`, `allowFullscreenSwitch: 1` (Alt+Enter works), `visibleInBackground: 1`.
- [x] **Run in background**: `runInBackground: 0` since 11:22, and `PauseMenu.OnApplicationFocus` pauses the shift on focus loss after log-on, so the entity never plays on while alt-tabbed.
- [x] **Color space** linear (`m_ActiveColorSpace: 1`).
- [x] **Scripting backend** Mono (`scriptingBackend: Standalone: 0`), **stripping** Low (`managedStrippingLevel: Standalone: 1`), **API compatibility** .NET Standard 2.1 (`apiCompatibilityLevel: 6`). Right choice for 1.0, see 2.10.
- [x] **64-bit only**: `SecondCursorBuild.BuildWindows()` uses `BuildTarget.StandaloneWindows64`, `BuildOptions.None` (non-development), scenes = only `Assets/SecondCursor/Scenes/SecondCursor.unity`, output `Builds\Windows\SecondCursor.exe`.
- [x] **Build works**: 11:31 build succeeded, 89.3 MB on disk, Managed folder 67 DLLs (Sentis and Visual Scripting assemblies were stripped, but their Resources were not, see 2.1).
- [x] **Graphics API order** for Windows: D3D11 first (`m_APIs: 0200000012000000`, `m_Automatic: 0`). D3D11 is the safest path under Proton (DXVK). See 2.9 for dropping D3D12.
- [x] **Input**: Input System only; `SecondCursor.Runtime.asmdef` defines `SC_INPUT_SYSTEM` through `versionDefines`, so `InputSystemBackend` compiles (no silent `NullInputBackend`). Typed text uses `Keyboard.onTextInput`, which also receives the characters Steam's on-screen keyboard injects.
- [x] **Rendering**: fixed 960x540 RenderTexture, letterboxed, integer scale when it keeps at least 93 percent of the area, 2x to 4x supersampled "sharp bilinear" otherwise (`ScreenRig.UpdateLetterbox`). Any aspect ratio works (16:10 Deck gets 40 px bars, 21:9 gets pillarbox).
- [x] **Frame pacing**: `GameRoot.Awake` sets `QualitySettings.vSyncCount = 1` and `Application.targetFrameRate = 60`.
- [x] **Saves** go to `Application.persistentDataPath` (not PlayerPrefs), JSON, and `Load`/`Save` never throw into gameplay.
- [x] **Photosensitivity basics**: boot disclaimer with a photosensitivity warning (shown 6.5 s, `BootSequence.Disclaimer`), a Reduce Flashing toggle (`VisualFx.ReduceFlashing` scales `Glitch`, `Flash` x0.2, `Shake` x0.3, removes flicker spikes), persisted in the save.
- [x] **Dev keys** F1 to F5 and the debug panel are gated by `Debug.isDebugBuild` (`DebugOverlay.cs` line 64), so a release build hides them. F6 (CRT toggle) is harmless.
- [x] **Quit** exists in the Esc menu and on the end card (`Application.Quit()`).
- [x] **No soft-locks from typing**: Notepad conversation has a silence fallback and every beat has a timeout (`EventDirector.WaitUntil`), so a player who cannot type (Deck without keyboard) still finishes.
- [x] **Works without Steam today**: no Steam code compiles unless `STEAMWORKS_NET` is defined; `SteamBridge` wraps init in try/catch and every call is a no-op when not ready.
- [x] **Privacy**: no network, no real file access, Unity Services off (`UnityConnectSettings m_Enabled: 0`).
- [x] **Content size**: everything is procedural; the game's own `Resources` folder is 95 KB.

---

## 2. Blocking gaps for a Steam Windows release (priority order, with exact fixes)

### 2.1 P0: Remove AI/editor packages and their leftovers

Evidence (11:22 to 11:31 build, `Logs\Editor.log`): 65 `Hidden/Sentis/*` shaders compiled, 2906 of 4033 variants; build report lists
`Packages/com.unity.ai.inference/Runtime/Core/Resources/Sentis/ComputeShaders/ConvGeneric.compute` at 6.4 MB and 482 Sentis entries totalling about 9.4 MB of the 14.6 MB "user assets". Anything inside a package `Resources` folder ships even if no code uses it.

Fix in `Packages\manifest.json` (remove these lines):

| Package | Why remove |
|---|---|
| `"com.unity.ai.assistant": "2.20.0-pre.1"` | Editor AI assistant, pre-release; pulls newtonsoft-json, mono-cecil, 2d.sprite |
| `"com.unity.ai.inference": "2.6.1"` | Sentis runtime; ships ~9.4 MB of shaders; pulls burst, collections, `com.unity.dt.app-ui` |
| `"com.unity.ai.navigation": "2.0.14"` | NavMesh; no NavMesh use in `Scripts` |
| `"com.unity.visualscripting": "1.9.12"` | Unused runtime package |
| `"com.unity.collab-proxy": "2.13.6"` | Unity Version Control editor plugin; remove unless you use Plastic |
| `"com.unity.timeline": "6.6.0"` | No Timeline/Playables use in `Scripts` |

Keep: `com.unity.inputsystem`, `com.unity.render-pipelines.universal`, `com.unity.ugui`, one IDE package, `com.unity.test-framework` (editor only).

Then:
1. Player > Other Settings > Scripting Define Symbols (Standalone): delete `SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY` (currently `scriptingDefineSymbols: Standalone: SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY`).
2. `ProjectSettings\EditorBuildSettings.asset` > `m_configObjects`: remove `com.unity.dt.app-ui` (or run `EditorBuildSettings.RemoveConfigObject("com.unity.dt.app-ui")`). Keep `com.unity.input.settings.actions`.
3. Optional, via Package Manager > Built-in (it refuses if something depends on a module): disable Terrain, Terrain Physics, Vehicles, Wind, Cloth, Video, XR, Umbra, Unity Analytics, Adaptive Performance, AndroidJNI, AI. Keep Physics, Physics2D (uGUI dependencies), Audio, UI, IMGUI, JSONSerialize, ImageConversion and ScreenCapture (editor scripts use them). The size effect on Mono is small; the win is fewer assemblies and faster builds.
4. Rebuild and confirm in the Editor.log build report that no `com.unity.ai.inference` lines remain and `resources.assets` drops from 9.3 MB to well under 1 MB.

### 2.2 P0: Menu, Quit and flashing choice reachable from the first screen

**Status: done** (release audit fixes, then Phase E). Esc opens Options on the title and the full menu everywhere else; the
first launch asks Full effects / Reduce flashing on the disclaimer; the title menu has Options and Quit; the taskbar has a
menu button (`||`). The menu also works with the keyboard alone (Up/Down/Tab, Enter/Space, Esc).

Evidence: `PauseMenu.Update` returns early unless `Flags.LoggedIn` (line 39); during boot Esc only skips (`BootSequence.SkipPressed`); the Log On dialog's Cancel only prints "You must log on to begin your shift."; Start > Shut Down is refused by design. A player who wants to leave before log-on must use Alt+F4. The disclaimer (`strings.json` `disclaimer.body`) says "You can reduce flashing at any time from the Esc menu", which is not true until after BIOS and splash.

Fix:
1. `PauseMenu.Update`: allow Esc from the Title card onward (drop the `LoggedIn` gate for opening; keep it only for `OnApplicationFocus` auto-pause if you want). While paused during boot, freeze the boot coroutine via `Time.timeScale = 0` as today.
2. Title card (`BootSequence.Title`): add two on-screen buttons, "Options" (opens the same menu) and "Quit".
3. Disclaimer (`BootSequence.Disclaimer`): on first launch only (no save file), show a choice "Flashing effects: Full / Reduced" before continuing, write it with `SaveSystem.SaveSettings`.
4. Add an on-screen pause affordance after log-on (for example a tray icon on the Taskbar that opens the menu) so the game is fully playable with a mouse only (also needed for the Steam "Mouse Only Option" accessibility tag and for Deck).

### 2.3 P0: Settings menu (display, VSync, volume, flashing) that actually works

**Status: done for display, VSync and frame cap** (Phase E): Display (borderless fullscreen / largest whole-number window,
applied at start), Frame rate (VSync default, 30, 60, 120, 144, Unlimited: `DisplaySettings`), Reading text (Normal / Large),
volume, CRT, flashing and difficulty, all saved to `settings.json` at once. Still open: a separate screen-shake setting,
window sizes to pick from, sticky drag.

Evidence: `PauseMenu.ToggleDisplay` does `Screen.fullScreenMode = FullScreenMode.FullScreenWindow` when returning to fullscreen, which keeps the current 1280x720 render size, so the game is upscaled and blurry until restart; windowed is always 1280x720 (non-integer 1.333x of 960x540); `SaveData.fullscreen` is written but never read in `GameRoot.Build`; VSync and frame cap are hard-coded in `GameRoot.Awake`; only master volume exists.

Fix (Esc menu, plus the title "Options"):

| Option | Values | Code |
|---|---|---|
| Display mode | Fullscreen (borderless) / Windowed | Fullscreen: `Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow)` |
| Window size (windowed) | 960x540, 1920x1080, 2880x1620 (only those that fit `Display.main.systemWidth/Height` minus 80 px) | `Screen.SetResolution(w, h, FullScreenMode.Windowed)`; integer multiples keep pixels square |
| VSync | On (default) / Off | `QualitySettings.vSyncCount = on ? 1 : 0` |
| Frame cap (when VSync off) | 60 / 120 / 144 / Unlimited | `Application.targetFrameRate = cap` (-1 for unlimited) |
| Volume | Master (exists); add SFX and Ambience if you want the Steam "Custom Volume Controls" tag | `AudioManager` |
| Flashing | Full / Reduced (exists) | `VisualFx.ReduceFlashing` |
| Screen shake | On / Reduced / Off | split from ReduceFlashing so it can go to zero (Steam "Camera Comfort" tag) |
| CRT effects | On / Off (exists) | `VisualFx.CrtEnabled` |
| Sticky drag (accessibility, Deck) | Off / On (click to pick up, click to drop) | `DragDropSystem` |

Apply all of these at startup in `GameRoot.Build` right after `SaveSystem.Load()` (today only volume, CRT and flashing are applied).

Cursor confinement: in borderless fullscreen on a multi-monitor PC, "pull away hard" in a tug-of-war moves the real mouse onto the other monitor, the next click lands on the desktop, the game loses focus and pauses. Set `Cursor.lockState = CursorLockMode.Confined` while the game is focused and not paused, and `CursorLockMode.None` when the menu opens or focus is lost (`GameRoot`/`PauseMenu`).

Store the settings in their own file (see 2.4).

### 2.4 P0: Save location, crash safety and Steam Cloud layout

**Status: done in code** (release audit fixes): `settings.json` and `progress.json` are separate, writes are atomic with a
`.bak`, an unreadable file is kept as `.corrupt` (the title shows a one-line notice), the old file is migrated once.
Still to do in Steamworks: the Auto-Cloud rows below (sync `progress.json` only).

Evidence: `SaveSystem.Save` uses `File.WriteAllText` directly on `second_cursor_save.json`. A crash or power loss mid-write leaves a truncated file; `Load` then logs a warning and returns `new SaveData()`, and the next `SaveSettings`/`Achievements.Unlock` overwrites the damaged file with defaults, silently wiping achievements, endings and `shiftsCompleted`. Settings (volume, fullscreen) and progress share one file, so Steam Cloud would copy a Deck's display settings to a PC. `Player.log` and `Player-prev.log` are written to the same folder.

Path today (Windows): `%USERPROFILE%\AppData\LocalLow\SecondCursorGame\SECOND CURSOR\second_cursor_save.json`.

Fix (`SaveSystem.cs`):
1. Split into `settings.json` (local only, never cloud) and `progress.json` (achievements, endings, flags, trust: cloud).
2. Atomic write: write `progress.json.tmp`, then `File.Replace(tmp, path, path + ".bak")` (use `File.Move(tmp, path)` when `path` does not exist yet; `File.Move` with overwrite is not in .NET Standard 2.1).
3. On load failure: try `progress.json.bak`; if both fail, rename the bad file to `progress.json.corrupt` before creating defaults. Never overwrite an unreadable file.
4. Keep one version field and migrate old `second_cursor_save.json` once (read, split, delete).
5. Optional: `PlayerSettings.forceSingleInstance = true` (`forceSingleInstance: 1`) so two copies never write the same file.

Steam Auto-Cloud (Steamworks > App Admin > Cloud, no code needed), per https://partner.steamgames.com/doc/features/cloud :
- Byte quota per user: `1048576` (1 MB). Number of files per user: `4`.
- Root path: Root `WinAppDataLocalLow`, Subdirectory `SecondCursorGame/SECOND CURSOR`, Pattern `progress.json` (exact name, NOT `*`, or the logs and settings sync too), OS `Windows`, Recursive `No`. Add a second row for `progress.json.bak` if you want the backup synced.
- Enable developer-only mode first, test with the Steam console command `testappcloudpaths <AppID>`, then publish.
- Test on Deck: the Windows build runs under Proton, so the file lives inside the Proton prefix (`steamapps/compatdata/<AppID>/pfx/drive_c/users/steamuser/AppData/LocalLow/...`). Do a PC to Deck round trip before release.

### 2.5 P0: Crash-free first launch, with and without Steam

Today the game has no Steam code, so a launch outside Steam is fine. After integrating Steamworks (section 4):
- [ ] Double-click `SecondCursor.exe` with Steam closed: `SteamAPI.RestartAppIfNecessary` starts Steam and relaunches (expected). Decide deliberately: keep it (Valve recommendation, fixes overlay/achievements) or skip it for a DRM-free feel (the README promises "The game never depends on Steam").
- [ ] Rename `steam_api64.dll` and launch: `SteamBridge.Init` must catch the `DllNotFoundException` (it does, `catch (Exception)`) and the game must run with local achievements.
- [ ] `SteamBridge.Init` runs at `BeforeSceneLoad`, before `GameRoot.Awake` wires `GameLog.Output`, so its `GameLog.Warn` is lost. Use `Debug.LogWarning` there so the reason lands in `Player.log`.
- [ ] Fresh Windows user (delete the LocalLow folder and `HKCU\Software\SecondCursorGame`): first launch must reach the title with no errors in `Player.log`.
- [ ] Launch on a 4K monitor, an ultrawide, a 1366x768 laptop, and with a second monitor. Launch with no audio device (Unity then plays silently; confirm `AudioManager.GenerateAll` does not stall).

### 2.6 P0: Prototype copy in the shipping build

**Status: done** (Phase E): the full game's Night 1 card is `NIGHT 1` with Continue / Title / Quit; the WISHLIST card exists only
in the demo build (`SC_DEMO`); Title > Credits exists. When Steamworks.NET is added, paste its LICENSE text into the base
`strings.json` key `credits.steamworks` (Credits shows it under "Third-party notices" in `STEAMWORKS_NET` builds).

`Assets\SecondCursor\Resources\Content\strings.json`:
- `end.card.cta` = "WISHLIST NOW" (blinks on the end card, `EndingSequence.cs`). Replace for 1.0, for example "END OF SHIFT" or remove the CTA.
- `end.card.thanks` = "Thank you for playing the prototype." Replace ("Thank you for playing.").
- Add a Credits screen (from the end card and the menu) with a third-party notices block: Steamworks.NET is MIT licensed and its copyright notice must ship with the game. If you ship a free demo, it needs its own App ID; keep the wishlist card only in that app.

### 2.7 P1: Version number

`ProjectSettings.asset` `bundleVersion: 0.1.0`. `SecondCursorBuild.ApplyReleaseSettings` only replaces "", "0.1" or "1.0", so it stays 0.1.0.
Fix: set Player > Version to `1.0.0` (bump per patch), show `Application.version` in the Esc menu corner and in `Player.log` at startup, and use the same string in the SteamPipe `Desc` field.

### 2.8 P1: Application and Steam icons

State: `m_BuildTargetIcons` has one default icon, `Assets\SecondCursor\Art\AppIcon.png` (256x256, generated by `SecondCursorBuild.GenerateIcon`, point filtered). Unity downsamples it for the smaller .exe sizes, and a 9x-scaled pixel cursor turns to mush at 16 and 32 px.

Fix:
1. Player > Icon > "Override for Windows, Mac, Linux": provide hand-pixelled PNGs at 16, 32, 48 (redraw the arrow at 1x/2x/3x) and 256; leave 64/128/512/1024 to the 256 source.
2. Steamworks > Community Assets (https://partner.steamgames.com/doc/store/assets/community): Client Icon 256x256 or 512x512 (ICO or PNG; Steam builds the .ico from a PNG) and Community Icon 184x184 JPG.

### 2.9 P1: Build hygiene, URP trimming, depot contents

1. **Graphics APIs** (Player > Other > Graphics APIs for Windows): remove Direct3D12, keep Direct3D11 (`m_APIs` becomes `02000000`). The log shows every shader pass compiled twice (dx11 and dx12), and the build ships a 4.6 MB `D3D12` folder (`D3D12Core.dll`). Nothing in this game needs D3D12.
2. **URP PC asset** (`Assets\Settings\PC_RPAsset.asset`, `PC_Renderer.asset`): the game renders UI into a RenderTexture plus a tiny CCTV set. Set HDR off (`m_SupportsHDR: 0`), GPU Resident Drawer Disabled (`m_GPUResidentDrawerMode: 0`), remove the `ScreenSpaceAmbientOcclusion` renderer feature, Soft Shadows off, and set Rendering Path to Forward (not Forward+, `m_RenderingMode: 2` today) unless the CCTV set needs many lights. The build report shows SSAO, GPU-driven occlusion kernels and terrain grass shaders shipping today. Delete `Mobile_RPAsset.asset`, `Mobile_Renderer.asset` and the Mobile quality level if you never target mobile.
3. **Scenes**: `EditorBuildSettings.asset` lists `SecondCursor.unity` first and `Assets/Scenes/SampleScene.unity` second, enabled. The release script ignores it, but a manual Build Profiles build would include it. Remove SampleScene from the list (and delete `Assets\Scenes\SampleScene.unity`, `Assets\TutorialInfo`, `Assets\Readme.asset` template leftovers).
4. **Build Profiles** (Unity 6): open File > Build Profiles > Windows and confirm "Player Settings Overrides" is off, otherwise the release settings in `ProjectSettings.asset` are ignored for that profile.
5. **Splash logo texture**: the build report still lists "Built-in Texture2D: Splash Screen Unity Logo" (2.7 MB). Also untick Show Unity Logo (`m_ShowUnitySplashLogo: 0`) and confirm on a real launch that no splash appears.
6. **Depot contents**: the build folder contains `SecondCursor_BackUpThisFolder_ButDontShipItWithYourGame` (debug data, Unity says do not redistribute: https://docs.unity3d.com/Manual/WindowsPlayerIL2CPPScriptingBackend.html). Exclude it and any `*_BurstDebugInformation_DoNotShip` folder, `*.pdb` and `steam_appid.txt` (see runbook step 6).
7. **Measured size** (11:31 build): 89.3 MB total: `UnityPlayer.dll` 37 MB, `SecondCursor_Data` 36 MB (Managed 14 MB, resources.assets 9.3 MB), `MonoBleedingEdge` 8.8 MB, `D3D12` 4.6 MB, DirectStorage DLLs 1.7 MB. After 2.1 and 2.9.1 expect roughly 75 MB before Steam's depot compression.

### 2.10 P2: Mono vs IL2CPP, stripping

- Keep **Mono** for 1.0: faster iteration, no C++ toolchain, CPU cost here is trivial. IL2CPP is optional later; it needs the "Windows Build Support (IL2CPP)" module and Visual Studio with the C++ desktop workload, and produces the do-not-ship folder described above.
- Keep **Managed Stripping Level = Low**. Medium/High can strip members that `JsonUtility` content classes and Steamworks.NET callbacks need; only raise it with a `link.xml` and a full playthrough.
- `stripEngineCode: 1` only applies to IL2CPP; no effect on Mono.

### 2.11 P2: Smaller release items

- [ ] Pause on Steam overlay open (Shift+Tab): `Callback<GameOverlayActivated_t>` in `SteamBridge` calls the pause menu (section 4).
- [ ] `Player.log` location for support: `%USERPROFILE%\AppData\LocalLow\SecondCursorGame\SECOND CURSOR\Player.log`. Put it in the store FAQ / Help.
- [ ] Privacy: `InsightsSettings m_EngineDiagnosticsEnabled: 1` and `submitAnalytics: 1` may send anonymous engine/hardware data to Unity. Review in Project Settings and state it in the store page's privacy note or turn it off where your license allows.
- [ ] Unity 6 Windows player minimum is Windows 10 21H1 (build 19043), x64 (https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html). Store system requirements: "Windows 10 (21H1) 64-bit or newer, DirectX 11 GPU, 4 GB RAM, 300 MB storage", tick "Requires a 64-bit processor and operating system".

---

## 3. Steam Deck

Valve criteria (https://partner.steamgames.com/doc/steamdeck/compat): default controller configuration must give access to all content; on-screen glyphs must match the controller; text entry must use the Steamworks text-entry API (ShowGamepadTextInput / ShowFloatingGamepadTextInput) or controller-driven entry; 1280x800 (preferred) or 1280x720; "the smallest on-screen font character should never fall below 9 pixels in height at 1280x800"; no unsupported-device warnings; 30 fps at 800p by default. "Playable" means it works but needs manual steps (community config, Steam+X keyboard).

**Likely rating today: Playable.** It runs under Proton (D3D11, no launcher, no anti-cheat), performance is trivially above 30 fps, and the right trackpad can drive the cursor. It misses Verified on input defaults, text entry, glyph wording and borderline text size.

Measured scale on Deck: `min(1280/960, 800/540) = 1.333` (non-integer), so the 960x540 screen is shown at 1280x720 with 40 px bars, supersampled from a 1920x1080 texture. Font "Nexus System 10": capitals 8 virtual px = 10.7 px (passes), lowercase x-height 6 virtual px = 8.0 px (below 9), full glyph cell 13.3 px.

| # | Change | Where | Needed for |
|---|---|---|---|
| D1 | Publish an official Steam Input config: right trackpad = mouse, R2 and right-pad click = left click (hold R2 while swiping to drag), L2 = right click, A = left click, B and Menu = Esc, X = Enter, Y = Delete, View = on-screen keyboard, left stick = slow precise mouse (joystick-as-mouse). Set it as the default in Steamworks > App Admin > Application > Steam Input (https://partner.steamgames.com/doc/features/steam_controller/getting_started_for_devs) | Steamworks, no code | Verified |
| D2 | When Notepad enters conversation mode (`EventDirector.cs` line 339 `_notepad.ConversationMode = true`) and `SteamUtils.IsSteamRunningOnSteamDeck()`, call `SteamUtils.ShowFloatingGamepadTextInput(k_EFloatingGamepadTextInputModeModeSingleLine, x, y, w, h)` with the Notepad rect in window pixels (Steam expects top-left origin: `y = Screen.height - top`). Characters arrive as OS key events, so `InputSystemBackend` needs no change (https://partner.steamgames.com/doc/api/isteamutils) | `SteamBridge`, `EventDirector` | Verified |
| D3 | Deck wording: when `IsSteamRunningOnSteamDeck()` swap strings that name keys or mouse buttons: `quickstart.body` ("Double-click", "Right-click anything"), `help.body`, `disclaimer.body` ("from the Esc menu"), `tasks.json` hint "Or right-click it", README-style Esc/Enter/Delete prompts. Add `*.deck` variants in `strings.json` ("press L2", "press B / Menu") | `strings.json`, `ContentDatabase.Text` | Verified |
| D4 | Text size: add a "Large text" option, default On when on Deck, that renders reading text (Mail body, Notepad, Work Queue hints, Help, dialogs) with `PixelText.Scale = 2` and re-wraps. Without it expect a "some text may be hard to read" note | `PixelText`, app views | Verified |
| D5 | Pointer from touch: read `Pointer.current` (mouse, pen or touch) instead of `Mouse.current` for position and press in `InputSystemBackend`, so the touchscreen works when Steam passes touches through | `InputBackend.cs` | Quality |
| D6 | Trackpad-friendly conflict: when on Deck (or Sticky drag on), relax `TugOfWarSettings` (`ConflictSystem.Settings`) so a trackpad flick can win, and enlarge the minimum hit area for title-bar buttons | `ConflictSystem`, `OSWindow` | Quality |
| D7 | Suspend/resume: press the power button during a tug-of-war, during entity typing and during the CCTV sequence; resume after 1 minute. Expected: the focus-loss auto-pause opens the menu; nothing is decided while suspended. Also add `OnApplicationPause(true)` to trigger the same pause | `PauseMenu` | Verified QA |
| D8 | Cloud round trip PC to Deck (2.4) and confirm `Player.log` is readable inside the Proton prefix | Steamworks | QA |
| D9 | Performance: keep VSync on and 60 fps cap by default; check battery draw with CRT effects on (full-screen overlay plus 2x supersample) | none | Verified |

**Phase E status:**
- D1: the default config to publish is now: D-pad = arrow keys (every menu is keyboard-navigable), A = left click, B and Menu =
  Esc, X = Enter, Y = Delete, View = on-screen keyboard, R2 = left click held (drag), L2 = right click, right trackpad = mouse,
  left stick = slow precise mouse. Still to publish in Steamworks.
- D2: done in code (`Input/DeckKeyboard.cs`): the floating keyboard opens for Jotter conversations (single line), editable
  files (multi line) and the Restricted code prompt (numeric), and closes when they lose focus; a click brings it back after a
  Steam-side dismissal. Needs hardware testing (Enter in single-line mode, Backspace, overlay callback).
- D3: done: `*.deck` string variants (`disclaimer.body`, `quickstart.body`, `help.body`) and `hintDeck` for the task hints
  that named mouse moves; the wording switches when `SteamBridge.OnDeck`.
- D4: partly done: Reading text Large (default on the Deck's first launch) doubles Jotter documents and Mail. Dialogs, toasts,
  the Work Queue and the hex viewer stay 1x.
- D5: done (`Pointer.current` for position and left button). D6: hook in place (`GameRoot.DeckPullSpeedScale`, 1 until
  trackpad drags are measured). D7: done in code (`OnApplicationPause(true)` and the Steam overlay pause the shift).

After D1 to D4 and D7, request the compatibility review from the Steam Deck section of the app's Steamworks page. Valve tests the Windows build under Proton when there is no Linux build.

---

## 4. Steamworks integration plan

### Library choice for Unity 6: Steamworks.NET

| | Steamworks.NET | Facepunch.Steamworks |
|---|---|---|
| Latest | tag `2025.164.1`, Steamworks SDK 1.64 (https://github.com/rlabrecque/Steamworks.NET/tags) | `2.5.2`, April 2024 (https://github.com/Facepunch/Facepunch.Steamworks/releases) |
| Install | UPM git URL | copy DLLs from a release |
| API | 1:1 with Valve's C++ API, Valve docs apply directly | friendlier C# API, lags SDK updates |
| Fit here | `Achievements.cs` already targets it (`STEAMWORKS_NET`, `Steamworks.SteamAPI`) | would need a rewrite |

Recommendation: **Steamworks.NET**. Minimum supported Unity is 2019.4 LTS, Unity 6 is fine.

### Install (after the App ID exists)

1. `Packages\manifest.json`, add:
   `"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.164.1"`
   (URL format from https://steamworks.github.io/installation/, pinned to the newest tag).
2. `Assets\SecondCursor\Scripts\Runtime\SecondCursor.Runtime.asmdef`: add `"com.rlabrecque.steamworks.net"` to `references` and a version define, otherwise `SecondCursor.Runtime` cannot see the `Steamworks` namespace (custom asmdefs do not auto-reference package assemblies):
   ```json
   "references": ["SecondCursor.Core", "UnityEngine.UI", "Unity.InputSystem", "com.rlabrecque.steamworks.net"],
   "versionDefines": [
     { "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "SC_INPUT_SYSTEM" },
     { "name": "com.rlabrecque.steamworks.net", "expression": "1.0.0", "define": "STEAMWORKS_NET" }
   ]
   ```
   Steamworks.NET's editor script can also add `STEAMWORKS_NET` to Scripting Define Symbols automatically (toggle added in 2025.162.1); either source is fine.
3. The package drops `steam_appid.txt` (contents `480`) in the project root for Editor testing. Put the real App ID in it. Never ship it: with that file present `RestartAppIfNecessary` always returns false (https://partner.steamgames.com/doc/api/steam_api).
4. `Game/SteamBridge.cs`: `FullGameAppId` and `DemoAppId` (today both `480`, Valve's Spacewar test app), and `StoreUrl`.
5. Check with `DevTools/CompileCheck`: `RuntimeSteam` and `RuntimeSteamDemo` compile the `STEAMWORKS_NET` code against a stub.

### Minimal code shape (implemented in Phase E as `Game/SteamBridge.cs`; kept here as the reference)

```csharp
#if STEAMWORKS_NET
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Init()
{
    if (Ready) return;
    try
    {
        if (!Application.isEditor && Steamworks.SteamAPI.RestartAppIfNecessary(new Steamworks.AppId_t(AppId)))
        {
            Application.Quit();          // Steam relaunches the game
            return;
        }
        var result = Steamworks.SteamAPI.InitEx(out string error);   // SDK 1.58+; use SteamAPI.Init() on older wrappers
        Ready = result == Steamworks.ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
        if (!Ready) Debug.LogWarning("[SYSTEM] Steam not available: " + result + " " + error);
    }
    catch (Exception e)                   // DllNotFoundException, BadImageFormatException...
    {
        Ready = false;
        Debug.LogWarning("[SYSTEM] Steam not available: " + e.Message);   // GameLog is not wired yet here
    }
    if (!Ready) return;                   // game runs normally, achievements stay local

    var go = new GameObject("Steam Callbacks") { hideFlags = HideFlags.HideAndDontSave };
    UnityEngine.Object.DontDestroyOnLoad(go);
    go.AddComponent<Pump>();
    _overlay = Steamworks.Callback<Steamworks.GameOverlayActivated_t>.Create(cb => { if (cb.m_bActive != 0) OverlayOpened?.Invoke(); });
    SyncLocalAchievements();              // unlocks earned offline get pushed once
}

static Steamworks.Callback<Steamworks.GameOverlayActivated_t> _overlay;
public static event Action OverlayOpened;   // PauseMenu subscribes and pauses

static void SyncLocalAchievements()
{
    foreach (var id in SaveSystem.Load().achievements) Steamworks.SteamUserStats.SetAchievement(id);
    Steamworks.SteamUserStats.StoreStats();
}

public static bool OnDeck => Ready && Steamworks.SteamUtils.IsSteamRunningOnSteamDeck();

public static void ShowKeyboard(Rect windowPixelsTopLeft)
{
    if (!OnDeck) return;
    Steamworks.SteamUtils.ShowFloatingGamepadTextInput(
        Steamworks.EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine,
        (int)windowPixelsTopLeft.x, (int)windowPixelsTopLeft.y, (int)windowPixelsTopLeft.width, (int)windowPixelsTopLeft.height);
}

public static void Unlock(string id)
{
    if (!Ready) return;
    Steamworks.SteamUserStats.SetAchievement(id);
    Steamworks.SteamUserStats.StoreStats();
}
#else
public static bool OnDeck => false;
public static event Action OverlayOpened { add { } remove { } }
public static void ShowKeyboard(Rect r) { }
public static void Unlock(string id) { }
#endif
```

Notes:
- No `RequestCurrentStats` call: since SDK 1.61 stats and achievements are synced by the Steam client before the game starts (https://partner.steamgames.com/doc/api/isteamuserstats).
- Keep the `Pump` MonoBehaviour (`SteamAPI.RunCallbacks()` in `Update`, `SteamAPI.Shutdown()` in `OnApplicationQuit`) as it is.
- Cloud: use Auto-Cloud only (2.4). Do not also call `ISteamRemoteStorage`.
- Test resets: Steam console `achievement_clear <appid> <API_NAME>` and `reset_all_stats <appid>` (https://partner.steamgames.com/doc/features/achievements).

### Achievements: define in Steamworks, call from code

Done in code (Phase E): the 19 achievements of the expansion spec, Section 9 (`Core/Game/AchievementIds.cs`), unlocked
through one gate (`Achievements.Unlock(g, id)`: only in runs that count, never from a checkpoint restore, a debug jump or a
forced tug). Enter them in Steamworks > Stats & Achievements exactly like this (names and descriptions are also in
`Content/full/strings.json` as `ach.<ID>.name` / `.desc`; Phase G moved them out of the base strings so the demo does not carry them).
Icons: `Builds/StoreArt/achievements/ACH_*.png` (achieved) and `ACH_*_locked.png` (unachieved), 256x256:

| API name | Name | Description | Hidden |
|---|---|---|---|
| `ACH_NIGHT_1` | First Solo Shift | Finish Night 1. | no |
| `ACH_NIGHT_2` | Second Night | Finish Night 2. | no |
| `ACH_NIGHT_3` | Last Night | Finish Night 3. | no |
| `ACH_END_SHRED` | Take the Seat | Reach the SHRED ending. | yes |
| `ACH_END_KEEP` | Working Nights | Reach the KEEP ending. | yes |
| `ACH_END_LOGOFF` | Nobody Left | Reach the LOG OFF ending. | yes |
| `ACH_ALL_ENDINGS` | Every Way Out | See all three endings. | no |
| `ACH_FIRM_GRIP` | Firm Grip | Win a tug-of-war against the second cursor. | no |
| `ACH_WHITE_KNUCKLES` | White Knuckles | Win 10 tugs-of-war. (progress stat `TUG_WINS`, 0 to 10) | no |
| `ACH_DO_NOT_READ` | Do Not Read | Open employee_017.dat. | yes |
| `ACH_REMOTE_SESSION` | Remote Session | Do everything it asked of you on Night 2. | yes |
| `ACH_FINISHED` | Finished | Let Gary finish. | yes |
| `ACH_HALF` | Half Is Enough | Keep Gary on WS-04. | yes |
| `ACH_HIS_GLASSES` | His Glasses | Ask Gary about his glasses. | yes |
| `ACH_HER_NAME` | Her Name | Say her name to the second cursor. | yes |
| `ACH_AUTHORIZED` | Authorized | Open Restricted with the Retention code. | yes |
| `ACH_REMAIN_SEATED` | Remain Seated | Finish Night 3's rounds without Custodial reaching the B-Level hall. | no |
| `ACH_NOT_ON_MY_SHELF` | Not On My Shelf | Refuse to confirm your own shelf. | yes |
| `ACH_WATCHERS` | Watch the Watchers | Find CAM 00. | yes |

Stat: `TUG_WINS`, type INT, increment only, min 0, max 10, default 0; set as the progress stat of `ACH_WHITE_KNUCKLES`
(Steam shows "5 of 10" once, the game calls `IndicateAchievementProgress` at 5). Every boot with Steam pushes the saved
achievements and the stat again (offline unlocks).

Demo: the demo is its own Steam app (`SteamBridge.DemoAppId`, 480 until it exists). The demo build keeps achievements local
(no Steam pushes) until the demo app gets its own achievements; its Wishlist button opens the FULL game's page in the Steam
overlay (`ActivateGameOverlayToStore(FullGameAppId)`), or `SteamBridge.StoreUrl` in the browser without Steam. Demo saves are
separate (product name "SECOND CURSOR Demo").

Each needs a display name, description and two icons (achieved, unachieved). Valve recommends 256x256 per icon; confirm in the achievement editor upload dialog. Publish the stats/achievements changes in Steamworks after editing.

### What needs an App ID first

1. Steam Direct fee (US$100 per app, recoupable after US$1,000 adjusted gross revenue), bank, tax and identity forms: https://partner.steamgames.com/steamdirect
2. App ID and a Windows depot ID (created in Steamworks > SteamPipe > Depots).
3. `SteamBridge.AppId`, editor `steam_appid.txt`, all SteamPipe VDFs.
4. Achievements, Auto-Cloud quota and paths, Steam Input default config, launch options, store page, content survey, accessibility wizard.

---

## 5. Store page assets and content survey

### 5.1 Store assets (https://partner.steamgames.com/doc/store/assets/standard)

| Asset | Size (px) | Required | Notes for this game |
|---|---|---|---|
| Header capsule | 920 x 430 | yes | logo must be readable; CRT glass + two cursors |
| Small capsule | 462 x 174 | yes | Steam scales to 120x45 and 184x69; logo only, no small text |
| Main capsule | 1232 x 706 | yes | home carousel |
| Vertical capsule | 748 x 896 | yes | seasonal sale pages |
| Screenshots | 1920 x 1080 minimum, 16:9, at least 5 | yes | real gameplay only. Capture with CRT on, integer scale 2x (1920x1080 native). Mark at least 4 as suitable for all ages |
| Page background | 1438 x 810 | optional | auto-generated from last screenshot if missing; keep it dark and quiet |
| Trailer | up to 1920 x 1080, 30 or 60 fps, 5,000+ kbps, .mp4 H.264 + AAC, 16:9 (https://partner.steamgames.com/doc/store/trailer) | strongly recommended | first seconds must show the desktop and the second cursor; put a flashing warning card before any glitch burst |

Library assets (https://partner.steamgames.com/doc/store/assets/libraryassets):

| Asset | Size (px) | Format |
|---|---|---|
| Library capsule | 600 x 900 | PNG |
| Library header | 920 x 430 | PNG |
| Library hero | 3840 x 1240 (keep key art inside the centred 860 x 380 safe area) | PNG, no text |
| Library logo | 1280 wide and/or 720 tall | PNG, transparent |

Community and client (https://partner.steamgames.com/doc/store/assets/community): Community icon 184 x 184 JPG; Client icon 256 x 256 or 512 x 512, ICO or PNG.
Events (https://partner.steamgames.com/doc/store/assets/eventassets): event cover 800 x 450, event header 1920 x 622 (JPG, PNG or GIF) for the launch announcement.
Achievement icons: 256 x 256, achieved + unachieved per achievement (section 4).
Description images/GIFs: each under 5 MB, 15 MB total (https://partner.steamgames.com/doc/store/page/description). Keep GIFs free of fast full-screen flashes.

Store text to prepare: short description (a few hundred characters), About This Game, English as the only language (Interface; no audio voice), features: Single-player, Steam Achievements, Steam Cloud, "Partial Controller Support" only after D1 is published, Family Sharing.

### 5.2 Content survey (https://partner.steamgames.com/doc/gettingstarted/contentsurvey)

The survey has three parts: General Content (generates ratings for several regional rating boards), Mature Content (drives how the game is shown against each customer's content preferences) and Generative AI disclosure. Answers for SECOND CURSOR, from the shipped content:

| Topic | Answer | Evidence |
|---|---|---|
| Fear / horror | Yes: psychological horror, a presence controlling your computer, surveillance of the player's own office, jump-scare style glitches and sudden loud sounds | README M4 to M9, `disclaimer.body` |
| Violence / gore | None depicted. Implied themes (disappeared employees, records "TERMINATED") | `employees.json`, `workorders.json` |
| Language | The game writes no profanity; it recognises profanity the player types and replies ("THEY LOG THAT TOO") | `dialogue.json` lines 7 to 8 |
| Sexual content, drugs, gambling | None | content scan |
| User-generated content / online | None; typed text stays local | no network |
| Mature Content descriptor | "General Mature Content": describe "psychological horror, disturbing surveillance themes, sudden loud sounds, flickering and glitch effects (can be reduced in Options)" | |
| Generative AI | Disclose honestly how code, text and art were produced. Live-generated AI: none (the entity's replies are fixed keyword tables in `dialogue.json`) | `DialogueEngine` |

Photosensitivity: Steam has no dedicated survey checkbox or accessibility tag for flashing (the accessibility tag list has none, https://partner.steamgames.com/doc/accessibility_features). Cover it three ways: the in-game warning plus first-launch choice (2.2), one line at the top of About This Game ("Contains flickering, screen glitches and sudden loud sounds; a Reduced Flashing option is available from the first screen"), and the Mature Content description above.

Accessibility wizard (Steamworks, optional but shown on the store page): claim only what is true after the fixes.
- "Camera Comfort": yes once screen shake can be reduced or turned off (2.3).
- "Mouse Only Option": yes once there is an on-screen pause/menu button (2.2); typing is optional thanks to the silence fallback.
- "Custom Volume Controls": only if you add per-channel sliders.
- Do not claim "Adjustable Text Size" (Valve wants text scalable to at least 38 px tall at 1080p), "Save Anytime" or "Playable without Quick Time Events" (tug-of-war and the race to "No" are timed).

---

## 6. Release runbook

### Timeline (count back from release day R)

| When | Step | Source |
|---|---|---|
| R minus 45 days or earlier | Pay Steam Direct fee, finish bank/tax/identity. 30-day minimum wait from payment to release starts now | https://partner.steamgames.com/steamdirect |
| R minus 35 | Store page complete (assets 5.1, survey 5.2, accessibility wizard), submit for review. Review "typically takes 3-5 business days"; submit at least 7 business days ahead | https://partner.steamgames.com/doc/store/review_process |
| R minus 28 at the latest | Store page public as Coming Soon. Must be Coming Soon for at least 2 weeks before release | https://partner.steamgames.com/doc/store/releasing |
| R minus 21 | Release candidate uploaded to a password beta branch, full playthrough on PC and Deck | this doc |
| R minus 14 | Final build on default branch, build checklist marked ready for review (3-5 business days, plan 7). Build review checks: it starts on all listed OSes, every feature listed on the store page works, Steam Wallet for any in-game purchase | https://partner.steamgames.com/doc/store/review_process |
| R | Press "Release App" (Publish Now, then Release Now). Needs the "Publish app changes to Steam" and "Manage pricing and discounts" permissions | https://partner.steamgames.com/doc/store/releasing |

### Step by step

1. **Freeze settings**: company `SecondCursorGame`, product `SECOND CURSOR`, version `1.0.0`, packages trimmed (2.1), D3D11 only (2.9), Build Profiles overrides off.
2. **Steamworks app config** (once): Installation > General: Launch option 0 = Executable `SecondCursor.exe`, OS Windows, CPU architecture 64-bit. SteamPipe > Depots: one Windows depot (64-bit). Cloud: quota + Auto-Cloud row (2.4). Stats & Achievements: table in section 4, then Publish. Steam Input: default config (D1). Redistributables: none (Unity Mono player needs no extra runtime).
3. **Build**: Unity menu `SECOND CURSOR/Build Windows (Steam)`. For batch builds add a static void wrapper (for example `SecondCursorBuild.BuildWindowsCI()`) that calls `BuildWindows()` and ends with `EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1)`, then run `Unity.exe -batchmode -projectPath "D:\Downloads\Podaci\Project 1" -executeMethod SecondCursor.EditorTools.SecondCursorBuild.BuildWindowsCI -logFile "D:\Downloads\Podaci\Project 1\_work\<date>\build.log"` (without the wrapper, `-quit` exits 0 even when the build fails). Check `Build Finished, Result: Success` and the size summary in the log.
4. **Smoke test the raw build** outside Steam: fresh LocalLow, first launch, flashing choice, quit from title, full shift, end card, relaunch keeps settings and achievements.
5. **ContentBuilder** (from the Steamworks SDK `tools\ContentBuilder`, kept on D:, for example `D:\Downloads\Podaci\SteamSDK\tools\ContentBuilder`): `builder\steamcmd.exe`, `content\`, `output\`, `scripts\` (https://partner.steamgames.com/doc/sdk/uploading).
6. **Copy the build** into `content\windows` without the do-not-ship data:
   ```
   robocopy "D:\Downloads\Podaci\Project 1\Builds\Windows" "D:\Downloads\Podaci\SteamSDK\tools\ContentBuilder\content\windows" /MIR /XD *_BackUpThisFolder_ButDontShipItWithYourGame *_BurstDebugInformation_DoNotShip /XF *.pdb steam_appid.txt
   ```
7. **Scripts** (replace `<APPID>` and `<DEPOTID>`):
   `scripts\app_build_<APPID>.vdf`
   ```
   "AppBuild"
   {
     "AppID" "<APPID>"
     "Desc" "SECOND CURSOR 1.0.0"
     "ContentRoot" "..\content\"
     "BuildOutput" "..\output\"
     "SetLive" "rc"
     "Depots"
     {
       "<DEPOTID>" "depot_build_<DEPOTID>.vdf"
     }
   }
   ```
   `scripts\depot_build_<DEPOTID>.vdf`
   ```
   "DepotBuild"
   {
     "DepotID" "<DEPOTID>"
     "FileMapping"
     {
       "LocalPath" "windows\*"
       "DepotPath" "."
       "Recursive" "1"
     }
     "FileExclusion" "*.pdb"
     "FileExclusion" "steam_appid.txt"
   }
   ```
   `SetLive` can only target a beta branch; the default branch is set live by hand on `https://partner.steamgames.com/apps/builds/<APPID>`.
8. **Upload** from `ContentBuilder\builder`: `steamcmd.exe +login <build_account> +run_app_build ..\scripts\app_build_<APPID>.vdf +quit`. Use a dedicated build account with Steam Guard (mobile authenticator); after security changes Valve enforces a 3-day wait before that account can set builds live. Enter the password interactively once; do not store it in scripts.
9. **Branches**: create `rc` (password protected) in SteamPipe > Builds. Testers: Steam > game Properties > Betas > enter password. Test on Windows and on Deck (Proton), including Cloud round trip, achievements (then `reset_all_stats <APPID>`), overlay pause, floating keyboard, suspend/resume.
10. **Promote**: on the Builds page, set the tested BuildID live on `default`, then mark the Game Build checklist ready for review.
11. **Release** on R with the Release App button. Post the launch announcement (event cover 800x450, header 1920x622).
12. **Patches**: bump version, build, upload with `SetLive "rc"`, test, promote to `default`. Keep every shipped `Builds\Windows` folder (including the do-not-ship debug folder) archived on D: per version for crash analysis.

---

## 7. Sources

- Steam store assets: https://partner.steamgames.com/doc/store/assets/standard
- Library assets: https://partner.steamgames.com/doc/store/assets/libraryassets
- Community and client icons: https://partner.steamgames.com/doc/store/assets/community
- Event assets: https://partner.steamgames.com/doc/store/assets/eventassets
- Trailers: https://partner.steamgames.com/doc/store/trailer
- Store description: https://partner.steamgames.com/doc/store/page/description
- Steam Deck compatibility criteria: https://partner.steamgames.com/doc/steamdeck/compat
- Steam Deck recommendations: https://partner.steamgames.com/doc/steamdeck/recommendations
- Steam Input for developers: https://partner.steamgames.com/doc/features/steam_controller/getting_started_for_devs
- ISteamUtils (IsSteamRunningOnSteamDeck, ShowFloatingGamepadTextInput): https://partner.steamgames.com/doc/api/isteamutils
- Content survey: https://partner.steamgames.com/doc/gettingstarted/contentsurvey
- Accessibility features: https://partner.steamgames.com/doc/accessibility_features
- SteamPipe uploading: https://partner.steamgames.com/doc/sdk/uploading
- Review process: https://partner.steamgames.com/doc/store/review_process
- Releasing: https://partner.steamgames.com/doc/store/releasing
- Steam Direct: https://partner.steamgames.com/steamdirect
- Steam Cloud: https://partner.steamgames.com/doc/features/cloud
- Achievements: https://partner.steamgames.com/doc/features/achievements
- Steam API init / RestartAppIfNecessary: https://partner.steamgames.com/doc/api/steam_api
- ISteamUserStats (SDK 1.61 RequestCurrentStats change): https://partner.steamgames.com/doc/api/isteamuserstats
- Steamworks.NET install: https://steamworks.github.io/installation/
- Steamworks.NET tags: https://github.com/rlabrecque/Steamworks.NET/tags
- Facepunch.Steamworks releases: https://github.com/Facepunch/Facepunch.Steamworks/releases
- Unity splash optional on Personal (Unity 6): https://unity.com/blog/unity-is-canceling-the-runtime-fee
- Unity 6 system requirements: https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html
- Unity Windows IL2CPP (do-not-ship folder): https://docs.unity3d.com/Manual/WindowsPlayerIL2CPPScriptingBackend.html
