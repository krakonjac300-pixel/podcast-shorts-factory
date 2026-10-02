# SECOND CURSOR: Unity prototype (vertical slice)

> *You're working on a computer while another cursor starts fighting you for control.*

This folder is the whole game. It is **100% Unity**: C# scripts, Unity's UI system (uGUI), and data
files in `Resources/`. The UI, the 3D security-camera set, the pixel art and all the sounds are
built procedurally at runtime, so there are no binary assets to import.

---

## 1. Put it in Unity (2 minutes)

**Option A (easiest): import the package into your existing project** (e.g. "project 1")

1. With your project open in Unity, double-click `SecondCursor.unitypackage` (it is next to the
   `Assets` folder in the repository's `SecondCursor/` folder). Alternatively, in Unity use
   *Assets → Import Package → Custom Package...* and pick it.
2. Click **Import** in the dialog that lists the files.
3. Unity compiles the scripts. The first time, it automatically creates and opens
   `Assets/SecondCursor/Scenes/SecondCursor.unity` and adds it to Build Settings. A new
   **SECOND CURSOR** menu also appears. If Unity asks whether to save the scene you had open, either
   answer is fine.
4. Press **Play**. Maximize the Game view (16:9, e.g. Full HD) for the intended look.

To update later, import a newer package the same way: it replaces the same files. Don't combine the
package with a hand-copied `Assets/SecondCursor` folder; delete the copied folder first.

**Option B: copy the folder.** Copy this whole `SecondCursor` folder (the one containing `Scripts/`,
`Resources/`, and this README) into your project's `Assets/` folder, giving you
`Assets/SecondCursor/...`. Then continue from step 3 above.

**Option C: standalone project.** In Unity Hub, click *Add → Add project from disk* and pick the
repository's `SecondCursor/` folder (made for Unity 6 LTS; newer Unity 6 versions upgrade it
automatically).

**Requirements:** Unity 6 (6000.x) or 2022.3 LTS, plus the uGUI package, which every Unity template
already includes. Built-in and URP both work, as do the old Input Manager and the new Input System.

> You can press Play in **any** scene. The game builds itself from code (`GameBootstrap`). To stop it
> auto-starting in a scene, add a `DisableAutoBoot` component to any object in that scene.

## 2. Controls

| Input | What it does |
|---|---|
| Mouse | Your cursor. Click, double-click, drag windows and files, right-click for menus, wheel scrolls lists |
| Keyboard | Type into Jotter (the in-game notepad). `Enter` sends your reply when the entity is talking to you |
| `Delete` | Shred the file selected in Files |
| `Esc` | Pause menu (CRT, flashing, display, frame rate, reading text size, volume, difficulty, restart from checkpoint, quit to title, quit). On the title: Options, or Back from a sub-screen |
| `Up` `Down` `Left` `Right` `Tab`, `Enter` / `Space` | Move through and press the buttons of the title, its screens, the pause menu and the end cards (the mouse works there too) |

**Steam Deck** (default controller layout to publish in Steamworks): right trackpad or touchscreen moves the cursor,
**A** or **R2** clicks (hold **R2** and move to drag), **L2** right-clicks, the **D-pad** moves through menus, **X** is
Enter, **B** or **Menu** is Esc, **Y** is Delete, **View** opens the keyboard. The on-screen keyboard also opens by
itself whenever you can type (a conversation in Jotter, an editable file, the Restricted code). The first launch on a
Deck turns on Large reading text; the texts that name mouse buttons use Deck wording.

**Developer keys** (`F1`-`F5` in the Editor and development builds only): `F1` debug panel · `F2` skip to next story beat · `F3` summon/dismiss the second cursor at your mouse ·
`F4` game speed (1x/2x/4x/0.5x) · `F5` restart the shift · `F6` CRT effects on/off (works everywhere; also in the Esc menu).

### How to play (your first shift)

You are a night operator at a data-reclamation company, alone at a 1998 computer. The whole game is
that computer screen. You just do your job, and the job slowly stops being normal.

1. **Start:** on the title choose **New Game**, pick **Normal** or **Story** difficulty (Story: the second cursor gives
   in sooner and hints come early; change it any time under Options), then click **Log On**. A **Quick Start** window
   explains the controls; click **Begin**.
2. **Your tasks** are in the **Work Queue** window (top right) and on the **Task** button in the taskbar
   (bottom). Do them in order. The text under each task says exactly how. If you close the Work Queue,
   click the Task button or the Work Queue icon to get it back. If you are stuck, the hint pops up again.
3. **The tutorial tasks:**
   - *Read the shift briefing:* double-click **Mail**; the unread message opens.
   - *Archive ledger_1994.dat:* double-click **Workstation** (the File Manager), double-click
     **Intake**, then **drag** `ledger_1994.dat` with the mouse onto **Archive** in the left pane.
     During the first shift the file and its destination blink.
   - *Verify work orders:* open **Work Orders**, note the owner's employee number, find that number in
     **Personnel**; **Approve** only if the status is TERMINATED, otherwise **Reject**.
   - *Shred ~nxs0148.tmp:* drag it onto the **Disposal** bin (desktop, bottom right) and click **Yes**.
4. **After that, keep working and watch the screen.** Things start to happen on their own. When
   another cursor shows up and fights you for a file, hold the mouse button and **pull away hard**.
   When it wants to talk, it opens Jotter (the in-game notepad): type a reply and press **Enter**.
5. **Locked things are locked on purpose.** The **Camera Viewer** and the **Restricted** folder say
   *Access Denied* at the start. The story opens them for you later; don't wait on them.

Night 1 takes about 10-15 minutes. The full game has three nights (about an hour): the end cards of Nights 1 and 2
offer **Continue to Night N**, **Title** and **Quit**, and Night 3 ends in one of three endings (SHRED, KEEP or LOG OFF)
with **Title**, **Night Select** and **Quit**. The free demo build is Night 1 only and ends on the **WISHLIST NOW** card.

### Title menu and progress

- **Continue** resumes your last checkpoint (each night saves a few; the button shows the night and the time), or starts
  the night you reached. Quitting never loses more than the time since the last checkpoint.
- **New Game** starts again from Night 1 (it asks first when there is progress); endings, records and achievements stay.
- **Night Select** (once Night 2 is unlocked) replays any unlocked night from the way it began the first time, so
  replaying Night 1 never changes what Night 3 remembers of Night 2.
- **Records**: the endings you have seen, tug-of-war wins and losses, best and total time per night, and the 19
  achievements (the hidden ones show `???` until you find them). There is no pop-up in the game when one unlocks; the
  Steam overlay shows its own.
- **Options** is the pause menu's settings; **Credits** scrolls with the arrow keys.


## 3. What to test

Use `F1` → **Jump to beat** to go straight to any milestone (it starts a fresh shift at that beat).

| Milestone | Where | Test |
|---|---|---|
| **M1 Fake desktop** | after log-on | Double-click desktop icons. Drag windows by their title bars; they overlap and raise on click. Minimize to the taskbar and restore by clicking its button. Maximize (button or double-click the title). Resize from the bottom-right grip. Open the **Nexus** menu. Shut Down is refused. |
| **M2 Apps** | `work` beat | **Mail**: read the briefing (unread = bold). **Files**: browse folders. **Notepad**: open a .txt file and type. **Staff Directory**: click an employee. **Work Orders**: approve/reject. **Work Queue**: checklist + hints. |
| **M3 Virtual files** | `work` | Drag `ledger_1994.dat` from Intake onto the **Archive** folder (left pane). Drag a file onto the desktop. Drag the temp file onto **Disposal** → Yes → it shreds. Try opening **Restricted** → Access Denied. |
| **M4 Second cursor** | `presence` | A second, inverted cursor enters from the right edge carrying `employee_017.dat`, drops it on the desktop, hovers, leaves. The tray now shows **two** mice. Press `F3` to summon it anywhere; F1 → *Close top window* / *Type STOP* / *Mimic me* make it act through the normal UI. |
| **M5 Cursor conflict (key fun test)** | `conflict` | Try to shred `employee_017.dat`. Drag it toward Disposal: it grabs the file mid-drag (**tug-of-war**: yank the mouse hard *away* from it to win, hold still and you lose). On the confirm dialog it races you to **No**, and later drags the dialog out from under your cursor. During shredding it goes for **Cancel**; park your cursor on Cancel to physically block it. Reach for the file on the desktop and it snatches it away. If you do manage to shred it, the file comes back: *"in use by another user"*. |
| **M6 Communication** | `communication` | It double-clicks Jotter (the notepad) itself and types **STOP**. Type a reply + Enter; it answers by keyword (try "who are you", "why", swearing, "no"). If you stay silent, it gets impatient. |
| **M7 Work tasks** | `work` | The tutorial tasks are data (`Resources/Content/tasks.json`) and complete from what you actually do, in any order. |
| **M8 Event director** | whole run | Escalates from ordinary work → a window nudging itself → a file selecting itself (with a click you didn't make) → presence → conflict → communication → escalation → reveal. |
| **M9 Security camera** | `escalation` / `reveal` | It replays your own earlier mouse movement, unlocks **SecureView** (normally *Access Denied*) and opens **CAM 03**: your office, from behind. The seated figure's arm follows *your* mouse. Keep watching... the door... |
| **M10 Slice** | `boot` → end | Disclaimer (once per launch) → title menu → New Game → night card → BIOS → splash → log-on → the shift → blackout → **NIGHT 1** card (Continue to Night 2, Title, Quit; the demo build shows **WISHLIST NOW**). |
| **M11 Night 2 (HELD)** | F1 → **Night 2**, or **Continue to Night 2** on Night 1's end card | Night card, Batch 45/46 and orders 3319/3321 (a file renames itself). The second cursor helps with Batch 46, then writes her own tasks into your Work Queue (black rows, *remote session*): do them or let them expire. Gary, a third, faint amber hand, talks in his own Jotter and brings `employee_209.dat`: shred it (fight the second cursor; Gary covers **No** and **Cancel** for you), drag it to Archive, or let 3:00 pass. Then the first Custodial round on the Camera Viewer: the figure only moves while you watch it. |
| **M12 Night 3 (RECLAIM)** | F1 → **Night 3**, or **Continue to Night 3** on Night 2's end card | Batch 47 (one file turns *damaged*), orders 3330/3331, the temp file (its clipboard holds a number). At 2:17 the phone rings and Ruth's mail explains the **Restricted** code: the folder now asks for it (`0217`, `2:17`...). In Restricted, `session.cfg` decides whether you may log off at 7:00: open it in Jotter, **Backspace**, type `1`, **File > Save** (`System\camview.cfg` has a secret too). At 3:00 Custodial walks from Sublevel C to your seat while Security keeps forcing the viewer open; do the shelf check on **CAM 04**, look away, or track it in Personnel (000). Then three hours are gone. From 6:41: shred `employee_017.dat` (**SHRED**), log off at 7:00 (**LOG OFF**), or stay (**KEEP**). Gary is a weak ally if you kept him on Night 2, a company pointer if you finished him. |

## 4. How it is built

```
Scripts/
  Core/        engine-free simulation (no UnityEngine reference; unit-tested outside Unity)
    Content/     data classes for the JSON + ContentDatabase (safe placeholders if content is missing),
                 ContentOverlay (per-night overlays merged onto the base content)
    FileSystem/  VirtualFileSystem: files/folders by stable ID, move/shred/restore, attribution (player/entity)
    Tasks/       WorkTaskManager: data-driven work tasks judged against world state
    Story/       NarrativeFlags (save-ready), DialogueEngine (keyword replies, "=word" whole words), GameClock,
                 CustodialRounds (watch meter), NightTemplates ({line1}-style tokens), Night3Rules (log off,
                 config files, the shelf rule, KEEP's lines, the time exits)
    Entity/      TugOfWar (conflict model), Difficulty (per-night DifficultyProfile, DifficultyTable,
                 AdaptiveAssist), MovementPlanner + MovementProfiles (personality in motion),
                 EntityMemory, CursorRecorder (mimic), EntityState/Phase/Personality
    Game/        SaveData (progress.json: nights, checkpoint, cross-night memory, records; migration),
                 AchievementIds + AchievementRules (the 19 achievements), TitleMenuModel (which title items show)
    Art/         PixelFontData (bitmap font), PixelArtData (icons, cursors, logos as pixel strings)
    Audio/       ProceduralSoundBank (every sound synthesized at startup)
    Util/        GameLog (tagged logging), Routine (coroutine runner: stopping it stops nested children too),
                 Vec2/MathUtil, Rng
  Runtime/
    Rendering/   ScreenRig (960x540 virtual screen -> RenderTexture -> letterboxed), PixelText, BevelGraphic,
                 SpriteLibrary, Palette
    Input/       CursorAgent (player AND entity are agents), PointerRouter (hit-testing + events for both),
                 Interactable, DragDropSystem, CursorView, input backends (old + new Input System),
                 DeckKeyboard (Steam Deck floating keyboard)
    UI/          UIBuilder, UiButton, ScrollArea, ListView, PopupMenu, MenuNav (keyboard/D-pad menus)
    OS/          WindowManager, OSWindow, Desktop, Taskbar, StartMenu, Dialogs, Notifications,
                 ShredService / MailService / WorkOrderService
    Apps/        Files, Mail, Notepad (Save for editable files), DataViewer, Staff Directory, Work Orders, Work Queue,
                 SecureView, System Monitor, Help, Disposal, AuthPrompt (a locked folder's code)
    Entity/      EntityController (awaitable MoveTo/Click/DragTo/Type/Replay...), EntityBrain (utility AI),
                 ConflictSystem (runs the tug-of-war)
    Story/       NightDirector (shared beat flow, checkpoints, helpers; .Progress: night start, play time, difficulty
                 switch) + Night1Director, Night2Director and Night3Director (each night's beats), NightSetup (a later
                 night's starting world), RoundsSystem (Custodial on the cameras, live Personnel), BootSequence
                 (disclaimer, night card), TitleMenu + TitleScreens (title, Night Select, Records, Credits), EndingSequence
                 (EndingSpec per night; Night 3's SHRED, KEEP and LOG OFF sequences and the CAM 00 stinger), EndCard
    CameraFeed/  SecurityCameraRig (small 3D sets rendered to a low-res CCTV feed: lobby, hall, office, and on
                 Night 3 Sublevel C and Admin 1)
    FX/          VisualFx (CRT scanlines/vignette/grain/flicker, glitch tearing, shake, power-off)
    Audio/       AudioManager (entity sounds panned to where it is on screen)
    Game/        GameRoot (composition root + frame order), GameBootstrap (every start goes through one rebuild),
                 DebugOverlay, PauseMenu, DisplaySettings, SaveSystem, Achievements + AchievementWatcher (the one
                 gated unlock path), SteamBridge (optional Steamworks, compiled with STEAMWORKS_NET)
  Editor/      one-time project setup + SECOND CURSOR menu
Resources/
  Content/     strings, story, filesystem, emails, employees, workorders, tasks, dialogue (JSON);
               night2/ and night3/ hold each night's overlay (same file names, merged by id, cumulatively)
  Shaders/     CCTV shader for the camera set (Built-in and URP; falls back to default materials if unsupported)
```

**Key design choices**

- **The entity uses the computer the way you do.** It is a second `CursorAgent` pushed through the same
  `PointerRouter` as your mouse, so every click, drag and double-click is real, and your cursor can
  physically block it. A future Player 2 could drive the same agent.
- **Scripted *and* systemic.** Story beats call the same awaitable actions the systemic `EntityBrain`
  uses (intercept drag, race to "No", drag the dialog away, cancel the shred, keep-away, close windows).
- **Everything targets logical IDs** (`file:employee_017`, `button:No`, `window.close`), never screen
  coordinates, and the cursor homes onto targets that move.
- **Data-driven content**: add emails, files, employees, tasks and dialogue in `Resources/Content/*.json`
  without touching C#.
- **Never touches the real computer**: no real files, mouse, camera or network. The hardware cursor is hidden only
  while the game window has focus.

## 5. Tuning knobs

- Tug-of-war feel and how hard the second cursor defends (grip, reaction times, which tricks it uses),
  per night and for Story difficulty: `Core/Entity/Difficulty.cs` (`DifficultyTable`). The adaptive assist
  (same file) eases it after repeated losses. An `EntityTuningAsset` in Resources overrides Night 1 Normal.
- Entity movement personalities: `MovementProfiles` (HumanLike, Hesitant, Aggressive, Panicked, Mechanical, Lurking, ImitatingPlayer).
- Pacing: the night directors' beats (`Night1Director`; waits/timeouts are in one place per beat), hint
  timings in the difficulty profile.
- Colours: `Rendering/Palette.cs`. CRT strength: `FX/VisualFx.cs`.

**Builds:** *SECOND CURSOR > Build Windows (Steam)* makes `Builds/Windows/SecondCursor.exe`; *Build Windows Demo
(SC_DEMO)* makes `Builds/WindowsDemo/SecondCursorDemo.exe` with Night 1 only (the night2 and night3 content folders are
moved out of `Resources` for that build and back afterwards; *Restore Demo-Excluded Content* repairs a build that crashed).

## 6. Outside-Unity dev tools (in the repository's `SecondCursor/DevTools/`)

- `CompileCheck/`: compiles every script against real Unity reference assemblies in 8 configurations
  (player, editor, legacy input, Input System only, the demo `SC_DEMO`, Steamworks `STEAMWORKS_NET` against a stub, the
  demo with Steamworks, editor scripts). `./setup.sh` (downloads) or `./setup_local.sh <Unity>/Editor/Data <project>/Library`
  (copies from an installed Unity) once, then `./check.sh`.
  `./check_shaders.sh` compiles the CCTV shader (needs `glslang-tools`).
- `CoreTests/`: `dotnet test` unit tests for the engine-free core (conflict model, movement, tasks, files, dialogue,
  the Routine runner, and validation of every JSON content file).
- `Package/make_unitypackage.py`: rebuilds `SecondCursor.unitypackage` from `Assets/SecondCursor`.
- `FontPreview/`, `ArtPreview/`, `SoundPreview/`: render the font, pixel art and sounds to PNG/WAV for review.
