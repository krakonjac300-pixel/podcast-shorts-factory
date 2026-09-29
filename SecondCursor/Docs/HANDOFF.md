# Handoff: getting SECOND CURSOR running inside the Unity Editor

This is for whoever next works with the Unity Editor open: a person, or a Claude session running on
the machine that has Unity. The game was written in a cloud session with no Unity Editor.

What that session could verify:
- Every script compiles against real Unity reference assemblies in 5 configurations (`DevTools/CompileCheck`).
- The engine-free core is unit-tested (`DevTools/CoreTests`).

What it could not verify: the game has **not been run in Unity yet**. Expect a first round of
visual/runtime fixes.

## 1. Install into the Unity project

1. With the project open, import `SecondCursor/SecondCursor.unitypackage` (double-click it, or
   *Assets → Import Package → Custom Package...*). Alternatively, copy `SecondCursor/Assets/SecondCursor`
   into the project's `Assets/` folder. Don't do both: the package's GUIDs differ from those Unity
   generates for a copied folder.
2. Let Unity compile. The one-time editor setup (`Scripts/Editor/SecondCursorProjectSetup.cs`) creates
   and opens `Assets/SecondCursor/Scenes/SecondCursor.unity`.
3. Check the **Console**. Warnings about the missing asmdef reference `Unity.InputSystem` are expected
   when that package isn't installed. Anything else that is red must be fixed first.
4. Press **Play**, with the Game view at 16:9 (Full HD) and **Maximize On Play** on.

Reading logs without the Editor UI: on Windows the Editor log is `%LOCALAPPDATA%\Unity\Editor\Editor.log`.
Every game log line is tagged, e.g. `[PLAYER]`, `[ENTITY]`, `[STORY]`, `[TASK]`, `[OS]`, `[SYSTEM]`.

## 2. First checks, in order

| # | Check | If broken, look at |
|---|---|---|
| 1 | The screen is black for about 1s, then the disclaimer, title, BIOS, splash and log-on appear | `GameRoot.Build`, `BootSequence` |
| 2 | Pixel text is crisp and readable; windows have 3D bevels | `PixelText.OnPopulateMesh`, `PixelFont.Build`, `BevelGraphic` |
| 3 | Your cursor is the in-game arrow, the Windows cursor is hidden, and clicking works | `GameRoot.Update` (input), `ScreenRig.ScreenToVirtual`, `PointerRouter.HitTest` |
| 4 | Windows drag, focus, minimize, maximize and close; the taskbar mirrors them | `OSWindow`, `WindowManager`, `Taskbar` |
| 5 | Dragging a file shows a ghost; dropping on a folder moves it, and dropping on Disposal asks Yes/No then shreds | `DragDropSystem`, `FilesApp`, `ShredService` |
| 6 | F1 → **conflict**: the second cursor grabs `employee_017.dat` when you drag it | `EntityBrain.RunIntercept`, `ConflictSystem` |
| 7 | F1 → **escalation**/**reveal**: SecureView shows a grey 3D office on CAM 03 | `CameraFeed/SecurityCameraRig.cs`, `Resources/Shaders/CCTV.shader` |
| 8 | Sound: startup chime after log-on, ambience hum, entity clicks panned | `AudioManager`, `ProceduralSoundBank` |

## 3. Known risk areas

- **World-space canvas → RenderTexture** (`ScreenRig`). If the OS doesn't appear at all, check:
  - `UiCamera` renders layer 5 into `ScreenTexture`.
  - The canvas sits at the world origin, 960x540.
  - With URP, the camera needs no special data; post-processing stays off by default.
- **Input System only** projects (Unity 6 templates) use `InputSystemBackend`. If typing does nothing,
  check that `Keyboard.onTextInput` gets subscribed.
- **Fonts/sprites** are generated textures (`PixelFont.Atlas`, `SpriteLibrary`) with point filtering. If
  anything looks blurry, check for fractional positions (UIBuilder rounds positions; `PixelText` snaps).
- **Camera set shader**: `SecondCursor/CCTV`. If it is magenta or missing, the rig falls back to default
  materials (see `SecurityCameraRig`). It deliberately renders in the transparent queue (depth writes on),
  because URP's depth priming would otherwise hide it. If SecureView shows only the dark background in a
  URP project, check the renderer's *Transparent Layer Mask* includes layer 8.

## 4. Tuning after the first play

The **tug-of-war** is the key fun test.
- `TugOfWarSettings` controls feel: pull speed for full strength, share rate, max tension.
- `EntityBrain.Grip` controls how strong the entity is and how that grows with each defense.
- `MovementProfiles.Aggressive` sets how fast it intercepts.

**Pacing** lives in `EventDirector` (per-beat waits and timeouts). Use `F4` to speed up time while testing.

## 5. First run in the Unity Editor (Unity 6.6, 6000.6.3f1, URP, Input System only)

Imported by extracting `SecondCursor.unitypackage` into a fresh URP project. Result:

- Compiles with no errors. The whole slice plays from the disclaimer to the WISHLIST card with no game
  errors or exceptions in the Console (Unity's AI Assistant package logs its own unrelated errors).
- Every beat was played through as a player: boot, the work tutorial (mail, archive, work orders with
  Personnel lookups, shred with confirm), anomalies, presence, the tug-of-war and the dialog/Cancel races,
  the Notepad conversation, the mimic replay, CAM 03 with the 3D set, the figure, and the blackout ending.
- Fixed: a debug jump made in the first seconds (while the sound bank generates) was overridden by the
  story restarting at `boot`. Unity 6.6 deprecation warnings (`FindObjectsSortMode`) and serialization
  analyzer warnings are gone.

### Polish pass 1

- CRT vignette: `Mathf.SmoothStep` interpolates (it is not GLSL `smoothstep`), so the vignette covered
  the whole screen at 64-100% black. Now a GLSL-style edge function and a lighter curve: the taskbar
  and window edges stay readable.
- F1 "Jump to beat" starts a fresh shift at that beat (`GameBootstrap.Restart(beat)`), so jumping back
  never leaves later windows or entity state behind. F2 still skips in place.
- Presence: the second cursor drops employee_017.dat where its whole icon is visible.
- Pacing: the self-selecting file never fires in the same instant as the cursor flinch; a pause after the
  shredded file returns before the entity starts typing.
- Tug-of-war band: more dots, thicker and red as strain rises. Entity afterimages are a short smear.
- Ending: the second cursor rides after the typed letters instead of sitting on the words.
- CCTV static is a fine 2x2 hiss; camera buttons show whole words; mail lists newest first with an
  untruncated Received column; the F1 hint is faint, top-centre, Editor/development builds only.
- Unity 6 "fast enter play mode" (no domain reload) was audited: the game's static caches all
  null-check destroyed objects, so repeated Play presses are safe.

### Polish pass 2 (includes a code review of passes 0-1)

- Conflict: the "in use" guard is re-armed before the shredded file returns, so it cannot be shredded a
  second time during the pause that follows.
- App windows open where they cover the least of the windows already open (and of the desktop icon
  column), so Personnel sits beside Work Orders instead of burying it.
- Keys typed in Notepad while the second cursor is typing are held and appear on your line when it is
  your turn (Enter still has to be pressed then).
- F1-F5 developer keys work only in the Editor and development builds. NEXUS Help no longer tells
  players to press F1.
- No pointer during the BIOS and splash screens; it appears at log-on.
- Presence drop-spot search runs nearest-first over several frames and ignores toasts. The CCTV grain is
  a cheap xorshift and the 3D set stops rendering while the viewer is minimized.
- A fresh Play session clears the game log (statics survive without domain reload); an unknown start
  beat falls back to boot instead of a black screen; mail date sort is stable.
- Bridge: scripted input only while a script runs a pointer/keyboard command (`scriptinput` makes it
  sticky), so manual Play always has the real mouse.

### Polish pass 3

- Sharp scaling: at non-integer display scales (e.g. a 1194 px wide Game view is 1.24x) the OS renders at
  the next whole multiple (`ScreenRig.TextureScale`, here 2x = 1920x1080) and is scaled down smoothly,
  so pixel text keeps crisp interiors at any window size. Integer scales still render 1:1 with point
  filtering. `ScreenRig.ScreenTextureChanged` lets copies (the glitch strips) re-bind.
- Real-mouse check in the Editor: clicks, double-clicks, window drags and file drags all work through the
  Input System path, not only through the bridge.

### Polish pass 4 (pacing)

- Anomaly beat: if nobody has File Manager showing employee_017.dat within 45 s, File Manager opens by
  itself on the file's folder and the file selects itself. Before, closing File Manager after the batch
  task could stall the story for up to 2 minutes.
- Debug jumps past the tutorial now leave the world as a player would: briefing read, ledger and batch
  files archived, temp file shredded, both work orders decided, Work Queue open.

### Polish pass 5 (the key fun test)

- After the player loses their first tug-of-war, NEXUS OS shows one toast in its own voice:
  "Input conflict: device 2 is holding the file. Drag firmly away from it to take it back."
  (`strings.json` key `notify.conflict`). Nothing else explains the fight, so without it a first-time
  player can read the loss as a bug.
- Sound mix audit (all 34 generated sounds rendered and measured): effects sit around 0.1 RMS, ambience
  about 13 dB under the UI clicks, no outliers.

### Polish pass 6 (full regression playthrough)

A complete playthrough from a fresh Play (boot, log-on, tutorial, anomalies, presence, conflict,
conversation, escalation, reveal, ending) passes with no errors. It found:

- Reveal: after a debug jump straight to `reveal` the second cursor was never present, so it could not
  click and the beat idled until its 150 s cap. It now appears if needed. If another window covers the
  feed's close box, it brings the feed to the front and forces it shut. If you block the box with your
  own cursor, it keeps losing that fight as before.
- Notepad type-ahead: a reply typed with Enter while it is typing is sent, one line per turn, as soon as
  it is your turn (before, the Enter was dropped and the next reply merged into it).
- Window placement also keeps the desktop Disposal bin uncovered (weighted 8x a window pixel).
- Bridge: `waittext TEXT [timeout]`; pointer commands aim at the visible part of a partly covered element.

### Polish pass 7 (onboarding, after the first real playtest)

The first human session (read from the game log) got lost: the player closed the Work Queue, tried the
camera three times, and never discovered that files are dragged between folders, so the archive task
timed out after about 5 minutes. Changes:

- **Quick Start** window right after log-on (`quickstart.*` strings): where the tasks are and the five
  basic moves; the first task is given when it is dismissed.
- **Task button on the taskbar**: the current Work Queue task (with progress) is always visible and
  blinks when a new one arrives; click it to reopen the Work Queue.
- **Hints repeat**: first after 30 s (25 s for the briefing), then every 40 s while stuck.
- **Tutorial guide**: during the archive and shred tutorial tasks, File Manager blinks the file to
  drag and its destination folder pale yellow.
- Camera denial now says "Night Operators: your work is in the Work Queue."; NEXUS Help has a proper
  how-to (bigger window); the README has a player-facing "How to play".
- Second code review (all LOW): a sent Notepad line ends the turn at once (no stray keystroke), the
  drop-spot search keeps a 2 ms per-frame budget, the conflict hint handler is removed on any jump,
  debug jumps past the shred task show a full Disposal bin, README F6 note corrected.

### Expansion phase A+B (night directors, overlays, saves, difficulty)

First step of `Docs/Design/Expansion.md` (Section 11.1 and the difficulty part of 11.2, Section 7).
Night 1 plays as before; the only intended differences are the adaptive assist and checkpoint saves.

- **Night directors.** `EventDirector` is split into `Runtime/Story/NightDirector.cs` (abstract: beat flow,
  jumps, side routines, checkpoints, `CompleteNight`, and helpers such as `WaitTask`, `GiveTask`,
  `TypeLines`/`RunExchangeChain` for any talking cursor (`Speaker`), `CarryFileIn`, `GuardElement`,
  `RaceTo`, `WaitWatching`, `StaticCut`, `FindDropSpot`, `EnsureClockAtLeast`, `RequestLogOff`) and
  `Night1Director.cs` (today's beats, unchanged). `NightDirector.Create(g, parent, night)` picks the
  director; Nights 2 and 3 do not have one yet, so they run Night 1's beats with that night's
  difficulty ("stand-in", nothing is saved). `Beats` is now per instance (`g.Director.Beats`).
- **Content overlays.** `ContentLoader.Load(night)` applies `Resources/Content/night2/` and `night3/`
  (none exist yet) with the merge rules in `Core/Content/ContentOverlay.cs`. New optional JSON fields
  (folder `code`, `removed` on folders/files/mails, camera `hidden`, task `author`/`timeout`/`deadline`,
  order `rule`, response `tag`, exchange `voice`, dialogue `lineSets`) default to off. `ContentIds` has
  the Section 11.5 ids.
- **Saves.** `progress.json` is version 3 (`Core/Game/SaveData.cs`, unit-tested): difficulty, nights
  unlocked, Continue's night, a checkpoint (Night 1: at the start of `work`, `conflict`, `escalation`),
  cross-night memory (`m.` flags, see `MemoryFlags`), Night 1's first three Notepad replies, trust,
  assist carry, endings, totals. Older files migrate on load (a finished Night 1 becomes Night 2
  unlocked). Finishing a night replaces the saved memory with that run's. Writes stay atomic.
  `GameBootstrap.Restart(night, beat, fromCheckpoint)` resumes a checkpoint (no title-menu Continue yet).
- **Difficulty.** `Core/Entity/Difficulty.cs`: `DifficultyTable.For(night, Normal|Story)` gives a
  `DifficultyProfile` (tug settings, grip, reaction times, which defenses the entity uses, hint
  timings); Night 1 Normal equals the old values (an `EntityTuningAsset` still overrides it).
  `AdaptiveAssist` (level -1..3) rises after 2 tug losses (other defenses count half), drops after two
  wins or one easy win, and at level 3 arms a mercy contest (grip 0.30, entity lets go). Each contest
  builds fresh settings (`ConflictSystem.CurrentSettings`); the brain reads grip and timings from the
  profile and assist. The NEXUS conflict toast also shows the first time the assist rises. Story mode is
  stored in the save (debug panel or bridge `difficulty story`).
- **Debug panel (F1).** Night 1/2/3 buttons, Normal/Story toggle, Tug win/lose/real (development
  builds only), Assist -/+, Trust -0.5/0/+0.5, and an assist readout.
- **Bridge additions.** `night N`, `jump N BEAT` (plain `jump BEAT` keeps the night), `setflag`/`clearflag`,
  `trust V`, `assist L`, `tug win|lose|real`, `setclock H M`, `difficulty normal|story` (restarts the
  current beat), `checkpoint save|load`, `save` (prints progress.json), `warnings` (the game's compiler
  warnings from the last compilation). `status` and `dump` print night, assist, grip and trust.
- **Not yet (later phases):** Night 2/3 directors and content, Gary (second controller), Custodial rounds,
  endings driven by `EndingSpec`, night card, title menu/Night Select/Records, achievements hooks,
  `=` whole-word keywords, entity-authored tasks (`GiveEntityTask`, `Withdraw`).

## 6. Editor test bridge (drive the game from outside the Editor)

`Scripts/Editor/SecondCursorTestBridge.cs` is an editor-only tool for repeatable play-testing. It does
nothing unless the folder `Library/SecondCursorBridge` exists. Write commands to
`Library/SecondCursorBridge/cmd.txt` (one per line); it runs them and writes `out.txt`.

```
play                     # enter Play mode (survives the domain reload)
jump conflict            # fresh shift at a beat ("beat NAME" jumps in place)
dclickid app:mail        # double-click an element by its logical id ("APP/ID" scopes to a window, "ID#N" picks the Nth)
dragto file:employee_017 app:disposal 0.3
clicktext Log On         # click visible pixel text
type hello\n             # keyboard text (\n = Enter)
shot name                # save the 960x540 virtual screen to Library/SecondCursorBridge/shots/name.png
dump | ids [filter] | texts [filter] | log [n] | errors
waitbeat reveal 60 | waitflag camera_unlocked 90 | waittask t_shred_017 30 | waitlog "text" 20
```

While attached, the player's cursor is driven by a scripted input backend in virtual pixels (960x540,
origin bottom-left), never the real mouse. Real keys (F1-F6, Esc) still work. `realinput` hands the cursor
back to the mouse. `help` lists every command.

A minimal client: write the file, then poll for `out.txt`:

```bash
d="Library/SecondCursorBridge"; mkdir -p "$d"; rm -f "$d/out.txt"
printf 'play\nbeat work\nshot desk\n' > "$d/cmd.txt"
until [ -f "$d/out.txt" ]; do sleep 0.3; done; cat "$d/out.txt"
```
