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

### Expansion phase C (Night 2)

Night 2, HELD, per `Docs/Design/Expansion.md` Section 4, with the Phase B systems it needs. Night 1 plays as
before; its only visible change is a **Continue to Night 2** button on its end card once Night 2 is unlocked
(not in `SC_DEMO` builds).

- **Director.** `Runtime/Story/Night2Director.cs` (boot, work, help, asks) and `Night2Director.Gary.cs` (third,
  finish, rounds, ending), beats and guards as in 4.2/4.3, checkpoints at `work`, `asks`, `finish`, `rounds`,
  `Prepare` per 4.5. The clock runs at 0.06 min/s, holds at 2:49 until the order for 209, then lands 3:00 on
  its 150 s deadline. Outcomes: finished (209 shredded by the player), kept by archive, kept by the deadline
  (or the 175 s cap). Log lines: `Gary: finished|archived|deadline`, `Rounds: stage N`, `Withdrew <id>`.
- **Content.** `Resources/Content/night2/*.json` (Section 4.4 verbatim) and the Section 3 string keys in the base
  `strings.json`. A file an overlay removes is never recreated as a placeholder (`FileSystemData.removedFiles`).
- **World setup.** `Runtime/Story/NightSetup.cs`: Night 1's end state, last night's mail read, Night 1's orders
  decided, 017 in use, templates filled (`Core/Story/NightTemplates.cs`, all Section 2.4 tokens).
  `SaveData.MemoryForNight(n)` gives a night the saved memory minus its own and later nights' `m.nN.` keys, so
  replaying Night 2 never starts with its previous run's choices.
- **Gary.** A third `CursorAgent` (registered between Ellen and the player) with `CursorView` variant `gary`
  (amber hand, `Palette.GaryOutline/GaryFill`), a secondary `EntityController` (no brain, no press hook, no
  static, `DeviceIndex` 3, `MaxAlpha`, `BaseFlicker`, `TypoRate` 0.08 with backspaced typos in
  `NotepadApp.TypeAsEntity`), `MovementProfiles.Tired`. `EntityController.IsBlockedByOthers` (player, or a
  visible cursor at alpha 0.5 or more guarding or hovering the element) is what `ClickElement` uses, so Gary
  on **No**/**Cancel** blocks Ellen. `DragDropSystem.CanContest` limits tugs to player vs. second cursor; the
  player grabbing Gary's ghost simply takes the file. Taskbar: three tray mice, `BlinkDevice(i)`.
- **Tasks.** `TaskType.OpenFile`/`ViewEmployee` (player-only counters `opened_by_player:`/`viewed_by_player:`
  from `AppManager.OpenFile` and `StaffApp.Show`), `TaskState.Withdrawn` + `Withdraw(id)`, entity-authored
  tasks (`author: "entity"`, `NightDirector.GiveEntityTask`). Work Queue: remote rows in the entity's inverted
  colours with `(remote session)`, `Due:` line, withdrawn hidden, at most 7 rows. `ShredService.IsPendingArchive`
  ignores entity and withdrawn tasks; `ResetBin()` empties the bin.
- **Rounds.** `Core/Story/CustodialRounds.cs` (watch meter, `RoundsConfig.Night2`, Story variant, unit-tested)
  and `Runtime/Story/RoundsSystem.cs` (feeds it from the Camera Viewer, forced opens, reopen penalty, figure
  cuts under static). New figure stage `HallFar`. Brain behaviour `CloseCamera` (`AllowCloseCamera`, reaction
  from trust); a blocked close types CLOSE IT (at most every 20 s).
- **Endings.** `EndingSpec` (lines, Gary's small goodnight, card keys, Continue button); Night 2's card shows
  `NIGHT 2` / subtitle and Continue to Night 3, Title, Quit. Night 1 keeps the WISHLIST card.
- **Boot.** Night card for nights 2+ (`night.card.N`), disclaimer and title only once per app session for later
  nights, `login.progress` on nights 2+ (Night 1 keeps its line).
- **Other.** `DialogueEngine`: `=word` keywords match whole words only. `phone_ring` sound (35 sounds).
  Debug panel shows rounds and Gary. Bridge: `stage N`, `waitending ID`, `dump` prints Gary and the round.
- **Judgement calls.** Ellen only intercepts 209 within 230 px of the Disposal bin (so archiving it is
  possible); Gary's guard move is 4x his Tired speed (at 2x he never beats her reaction); if windows cover
  209 when the order arrives or before Gary's tries, it is moved into view and blinks; Ellen does not lurk
  during the round (a slow lurk delayed her close); "Title" on a night card restarts the Continue night with
  the intro until the title menu exists (Phase E).
- **Not yet:** Night 3 (still the stand-in), title menu / Night Select / Records, achievements hooks,
  AuthPrompt, Notepad Save, Log Off, CAM 04 / CAM 00 sets, live Personnel, Night 1's night card.

### Expansion phase D (Night 3)

Night 3, RECLAIM, per `Docs/Design/Expansion.md` Section 5 and the endings of Section 6. Night 2's end card
**Continue to Night 3** now starts the real night (the stand-in is gone). Nights 1 and 2 play as before, apart
from the review fixes and the mail order below.

- **Director.** `Runtime/Story/Night3Director.cs` (setup, boot, work), `.Ruth.cs` (missed call, the 0217 glitch,
  Ruth's mail and exchange, code hints), `.Rounds.cs` (the full round, the lost hours), `.Finale.cs` (finale, Log
  Off, exits, ending), `.Gary.cs` (both Gary branches). Beats boot, work, ruth, rounds, lost, finale, ending;
  checkpoints at work, ruth, rounds, finale; `Prepare` per 5.7 (`RestoreEdits` puts the code and the config edits
  back from the flags for any checkpoint after work). Clock: 1:52 at log-on, 0.113 min/s, held at 2:16 during
  work, 2:17 at the missed call, held at 2:57 until the round, 3:00 to 3:30 at 1/12, 6:41 after the lost hours,
  0.09 to 7:00, then 1/12. Log lines: `Missed call`, `Code accepted`, `session.cfg saved: ...`, `Rounds: ...`,
  `Lost time`, `Finale exit: Shred|LogOff|Keep (confirm|time|seat)`, `Ending: ...` phases, `Night 3 complete`.
- **Content.** `Resources/Content/night3/*.json`, copied verbatim from Section 5.4 by
  `_work/2026-09-29/extract_night3.py` (on the D: work folder, not in the repo). Loaded on top of base + night2.
- **World setup.** `NightSetup.ForNight3`: both nights' end state (ledger and Batches 44 to 46 archived; 214
  visible, in Archive if hidden on Night 2; 209 shredded, archived or on the desktop), their mail read, their
  orders decided, the shelf orders hidden until the round, 017 in use until the finale, 209's record patched when
  finished, templates and the BIOS `{p3}` filled. A Night 3 with no Night 2 memory keeps Gary.
  `MailService.SortByDate` keeps tonight's mail on top after the old mail is delivered (Night 2 too).
- **Code prompt.** `Apps/AuthPromptApp.cs` and `VirtualFileSystem.TryUnlock` / `CodeMatches` (digits only,
  leading zeros ignored). `FilesApp` opens it for a locked folder with a `code`, for the player only. A wrong code
  shakes it and counts `auth.fail`; `auth.format` shows from the difficulty's `CodeFormatAfterFailures`. The right
  one unlocks, File Manager goes in, `auth.ok`, and `m.n3.restricted_open` (set only by the prompt, never by Gary).
- **Jotter Save.** File > Save for files tagged `editable` (`NotepadApp.CanSave` / `Save`), toast `file.saved` or
  `file.saved.remote`, event `AppManager.FileSaved`. `TypeAsEntity` treats `'\b'` as delete-last.
  `Night3Rules.AllowsLogoff` / `OperatorOverride` read the saved text (exactly `=1`); saving anything else clears
  the flag again.
- **Log Off.** Start menu item `logoff.item` once flag `logoff_item` is set (`StartMenu`), handled by
  `Night3Director.RequestLogOff`: `logoff.early` before 7:00, `logoff.disabled` without `ALLOW_LOGOFF=1`, else
  the confirm and a 6 s progress with Cancel.
- **Cameras.** `CameraFeed/SecurityCameraRig.Sublevels.cs` (the rig is now `partial`): CAM 04 Sublevel C (a
  shelved aisle with a 28 s pan and the `n3_shelves` captions in step with it; picture only while `Cam04Online`,
  so Nights 1 and 2 still show NO SIGNAL) and CAM 00 Admin 1 (a dark copy of the office with a second, always
  seated Custodian at the CRT, and the bright point for the stinger). New figure stages `SublevelC`, `Lobby`,
  `Seated00`; `DawnLevel` for the lobby. `CameraApp`: hidden cameras listed once `m.n3.cam00` is set (the buttons
  rebuild), a bottom-left caption line, `IsShowing`.
- **Rounds.** `RoundsConfig.Night3` (7 stages, 4 s, or 5 s after hiding 214; starts in the Lobby after the Night 2
  door; forced opens at 0 s then every 22 to 30 s; 360 s), `Night3Finale` (4 stages, 3 s; the director forces the
  viewer open by the clock at 6:50, 6:55 on CAM 03, 7:00 and 7:02) and `Hasten`. `RoundsSystem`: `PatchPersonnel`
  (5.6: 000's office follows the figure, 001's last login copies 000's, 118 goes on leave at the hall),
  `OpenViewer(camera)`, `ShowOnViewer`, `ForcedOpenHandler` (finished Gary opens the viewer by hand).
  `WorkOrderService.SetHidden` / `Cancel` (shown as Cancelled); undecided shelf orders are cancelled at the end
  of the round, after the task is withdrawn.
- **Endings.** `EndingSpec.Kind` (Blackout, Shred, Keep, LogOff), `Speakers`, `SystemLines`, `Stinger`,
  `ThanksKey`, `FinalCard`. `Story/EndingSequence.Night3.cs` plays SHRED (your own arrow types in the dark), KEEP
  (Ellen and your arrow in her palette; `both` lines one letter each; the name line goes before the last),
  LOG OFF (session closed, the lobby at 7:02 in morning light with nobody leaving, the log in the BIOS colour,
  Ellen alone) and the CAM 00 stinger. The director plays the CAM 03 final image (head turn) before SHRED and
  KEEP. Night 3's card: title, subtitle, `end.card.thanks`, Title and Quit (Night Select appears when
  `GameBootstrap.NightSelectAvailable`, a Phase E hook).
- **Core.** `Core/Story/Night3Rules.cs` (Log Off check, config values, trust thresholds and line sets, the shelf
  rule, KEEP's lines, `KeepByTime`), `RoundsConfig` additions, `VirtualFileSystem.TryUnlock`, `ContentIds`
  (Night 3 ids, `n3_shred` / `n3_keep` / `n3_logoff`), `Flags.LogoffItem` and the Night 3 per-night flags.
- **Achievement hooks for Phase E.** Flags plus `[STORY] Hook: ACH_*` log lines: `ACH_AUTHORIZED`
  (`m.n3.restricted_open`), `ACH_REMAIN_SEATED` (`n3.rounds_safe`: round safe with max stage 1 or less),
  `ACH_NOT_ON_MY_SHELF` (`m.n3.own_shelf_rejected`), `ACH_WATCHERS` (`n3.cam00_viewed`), `ACH_HER_NAME`
  (`m.said_name`), `ACH_NIGHT_3`; the endings are in `endingsSeen`.
- **Debug.** F1 panel on Night 3: Force SHRED / KEEP / LOG OFF (`Night3Director.ForceExit`). Bridge: `type`
  understands `\b` (Backspace). Note: `waitlog` matches its text literally, so do not put quotes around it.
- **Phase C review fixes (same commit).** Ellen's asks only count what the player does after she asks (the
  `opened_by_player:` / `viewed_by_player:` counters of the task's targets are zeroed before `GiveEntityTask`;
  Night 3 has no entity-authored tasks, so nothing to apply there). Every finish outcome aborts an open shred
  dialog (a leftover confirm can no longer shred a kept 209). Ruth's warning read outside the asks beat is
  answered at once instead of queued. `RunCloseCamera` sets Ellen back to Observing when the viewer was closed or
  switched first.
- **Save memory.** `SaveData.RecordNightComplete` no longer replaces the whole memory: `MergeNightMemory` drops
  the finished night's keys and later nights' keys (they were built on the old path), keeps earlier nights' keys
  and keys of no night (`m.said_name`), then adds the run's snapshot (unit-tested). Phase E: Night Select must
  start a night from `nightStartMemory[N]` (spec 8.1), not from `memory`.
- **Tested through the bridge.** A full Night 3 from the night card (real work, the code, both config edits, the
  round, the shelf check, the lost hours) to SHRED; LOG OFF with finished Gary (after a Night 2 finished run and
  Continue); KEEP by confirmation, by time and by watching the feed; kept Gary unlocking Log Off at 6:48;
  checkpoint restores at ruth and finale; the 5.8 script. Night 1 (tutorial, tug, reveal, card) and Night 2
  (start, Gary finished, round, card, Continue to Night 3) regressions. 0 game errors, 0 compiler warnings;
  CoreTests 180 (40 new in `Night3Tests.cs`).
- **Judgement calls.**
  - The code prompt works all night, not only from the Ruth beat: the temp file's clipboard and batch47_b already
    say 02:17. Hints start when Ruth's mail is read; a hint waits up to 20 s while the prompt is open.
  - The clock holds at 2:16 during work, so the phone rings at 2:17 as the mail and the secret say.
  - Finished Gary arrives when Batch 47 is given (at order 3330 the batch is already done).
  - Ellen's close reaction in the Night 3 round is the spec's value minus 0.4 s (her hand's travel), and she stops
    lurking when the round starts: a player who does nothing ends near the doorway or the middle of the room
    (the 7.4 budget) instead of losing the seat.
  - Kept Gary shows the lesson first (switches to a camera without the figure 1 to 1.5 s after the first forced
    open), then types it; after that he looks away on every second forced open.
  - Kept Gary guards No and then Cancel with no pause between them; on the Log Off confirm he sits on No
    (harmless). Finished Gary goes back to the left edge after racing to No, and a No he wins counts as a defense
    for the adaptive assist, so the race stays winnable.
  - After the lost hours the desktop comes back bare (every window closes), so employee_017.dat lands in view for
    the finale.
  - CAM 00 always shows a second Custodian at the Admin 1 CRT (000 is on rounds; 001 logs on at the same minute);
    the stinger uses it instead of moving the rounds figure.
  - The spec's 5.8 script needs the player's cursor on Cancel after Yes (Ellen's CancelShred has 7 s of patience
    on Night 3): add `moveid button:Cancel`.
  - `ex3_final`'s `letgo` and `go` tags are logged but change nothing: the exits are what you do.
  - The lobby at dawn gets ambient and a dim fill so the empty lobby reads at 7:02.
- **Play length.** About 21 to 25 minutes for a first-time player: boot 1, work 5 to 6, Ruth 3 to 5, round 6,
  lost hours 1, finale 3 to 5 (an idle player waits until 7:05), ending 1.5.
- **Not yet:** title menu, Night Select, Records, achievements (the hooks above), `SC_DEMO`, the post-game echo
  `{p2}`, fast-forwarding an idle finale (spec 13.5).

### Expansion phase E (title menu, end cards, pause options, achievements, Steam Deck)

Sections 6, 8, 9, 10 and 12 of `Docs/Design/Expansion.md`, per the Phase E plan. Every night still plays as before; what
changed is how a night starts and ends, what is recorded, and the options.

- **Launch and starts.** Every start builds a fresh root through one path (`GameBootstrap.Rebuild`): the title
  (`ToTitle(screen)`), a choice on it or on an end card (`StartFromMenu(night, fromCheckpoint, fromNightSelect)`), the
  pause menu's `RestartFromCheckpoint`, and debug starts (`Restart`). The game scene contains a GameRoot, so the launch
  state is reset before the scene wakes (`GameBootstrap.PrepareLaunch`, BeforeSceneLoad): a plain launch shows the title,
  `-scnight N` / `-scbeat B` start a night directly with records held. The disclaimer shows once per launch.
- **Relaunch bug fixed.** A night's start is recorded when its boot beat runs after the title (`NightDirector.MarkNightStarted`,
  `NightDirector.Progress.cs`), not in `Begin()`, so launching the game no longer resets `currentNight` to 1. Night 1 now
  gets its night card too (spec 3.2).
- **Title menu** (`Story/TitleMenu.cs`, `TitleScreens.cs`, `Core/Game/TitleMenuModel.cs`): the ghost title and drone, then
  Continue (`Continue: Night N, h:mm AM` at a checkpoint), New Game (confirm when there is progress, then Normal / Story),
  Night Select (once Night 2 is unlocked; rows show the best time, the footer the endings seen), Records (endings, tug
  totals, best and total time per night, the 19 achievements with hidden ones as `???` and the focused one's description),
  Options (the pause menu's settings), Credits (scrolls), Quit; Wishlist in the demo when the store can open. A first launch
  shows New Game, Options, Credits, Quit. Keyboard: Up/Down/Left/Right/Tab, Enter/Space, Esc (`UI/MenuNav.cs`, also used by
  the pause menu and the end cards; the focused button gets a ring). Element ids `title:*`, `select:night1..3`,
  `records:<ID>`, `pause:*`.
- **Saves** (`Core/Game/SaveData.cs`, still version 3, fields are additive): `bestNightSeconds`, `nightStartTrust`,
  `lastCompletedNight`, `Checkpoint.elapsed` and `.armed`, `RecordTug`, `HasProgress`, `HasRecords`, `ContinueTarget(maxNight)`,
  `StartStateFor(night, fromNightSelect)`. Night Select starts a night from its first-start memory and trust (spec 8.1), so a
  Night 1 replay can no longer strip the Gary branch from a Night 3 replay; Continue keeps `MemoryForNight`. Tug totals are
  counted live, real fights only. `NightResult.Records = false` keeps progression but not endings, play time or best times.
- **Armed runs.** `GameServices.RecordsArmed`: a run counts when it started from the title, an end card, or a checkpoint saved
  armed. Debug starts, bridge debug commands (beat, jump, night, restart, setflag, clearflag, trust, assist, setclock,
  difficulty, checkpoint, stage, speed), the F1 panel, F2 to F5 and a forced tug outcome hold records (`Records held: <why>`).
  The F1 panel shows the state and has an "Arm records (testing)" button.
- **Achievements** (`Core/Game/AchievementIds.cs`, `AchievementRules.cs`; `Runtime/Game/Achievements.cs`,
  `AchievementWatcher.cs`): all 19 of Section 9. One gate: `Achievements.Unlock(g, id)` logs `Achievement unlocked: ID` or
  `Achievement held (why): ID`. The watcher attaches after NightSetup and listens to flags, counters, tug ends and order
  decisions; the director reports night completion, typed replies (by exchange voice and tag) and a safe Night 3 round.
  Prepare (`NightDirector.IsPreparing`), restores and memory merges never unlock anything. Boot reconciles the ending, night
  and tug achievements from the saved records. No in-game toast (it would break the fiction).
- **End cards** (`Story/EndCard.cs`, `EndingSpec.Buttons`): Nights 1 and 2: Continue to Night N+1, Title, Quit. Night 3:
  Title, Night Select, Quit and the thanks line. Demo: WISHLIST NOW with Wishlist (when the store can open), Title, Quit.
  Continue starts an armed run. Ids `button:Continue|Title|NightSelect|Quit|Wishlist`.
- **Demo** (`SC_DEMO`): Night 1 only (`GameBootstrap.MaxNight`, `ContentLoader`, `NightDirector.Create`), no Night Select or
  Records, the WISHLIST card. *SECOND CURSOR > Build Windows Demo (SC_DEMO)* builds `Builds/WindowsDemo/SecondCursorDemo.exe`
  as product "SECOND CURSOR Demo" (separate saves), moving `Resources/Content/night2` and `night3` to
  `Assets/SecondCursor/_DemoExcluded` for the build with `AssetDatabase.MoveAsset` (GUIDs kept) and back in a `finally`;
  *Restore Demo-Excluded Content* repairs a crashed build.
- **Pause menu** (`Game/PauseMenu.cs`, `DisplaySettings.cs`): Resume, CRT, Flashing, Display, Frame rate (VSync default, 30,
  60, 120, 144, Unlimited), Reading text (Normal / Large: Jotter documents and Mail at 2x), Volume, Difficulty, Restart from
  checkpoint (Restart night when there is none; keeps the run's armed state), Quit to Title (confirm), Quit. Everything is
  saved to `settings.json` at once. Difficulty is saved at once and applies at the next checkpoint beat
  (`ApplyPendingDifficulty`: new profile, new assist at the current level), or at once with Restart; while pending the label
  has a `*` and a note. On the title the same panel opens as Options (settings only). The Steam overlay and
  `OnApplicationPause(true)` pause a running shift. The version is at the right of the caption.
- **Steam** (`Game/SteamBridge.cs`, moved out of Achievements.cs): `FullGameAppId`, `DemoAppId` (both 480 for now),
  `StoreUrl` (empty), `OpenStorePage` (the full game's page in the Steam overlay, else the URL), `OverlayActivated`, achievement
  and `TUG_WINS` stat pushes (progress popup at 5), resync of every saved achievement at boot, `ShowTextEntry` /
  `DismissTextEntry`, `OnDeck`. All Steam calls are under `#if STEAMWORKS_NET`; the package is not installed yet (see below).
- **Steam Deck**: `Input/DeckKeyboard.cs` opens the floating keyboard for Jotter conversations (single line), editable or
  player-opened pages (multi line) and the Restricted prompt (numeric) and closes it when they lose focus; logs `Deck
  keyboard: show MODE` / `dismiss`. `ContentDatabase.Variant = "deck"` prefers `key.deck` strings (`disclaimer.body`,
  `quickstart.body`, `help.body`) and task `hintDeck`. The first launch on a Deck turns Large reading text on.
  `InputSystemBackend` reads position and the left button from `Pointer.current` (touch). `GameRoot.DeckPullSpeedScale`
  (1) is the trackpad tug hook for Phase F.
- **Phase D review fixes** (`_work/2026-09-29/launch/ReviewPhaseD.md`):
  1. The finale's shred hooks are attached before 017 stops being "in use", and a 017 already shredded counts as SHRED.
  2. `NightDirector.StopSideRoutines()` runs first in every ending beat, so no finale, tug, log-off or Gary routine reopens a
     Notepad in the dark (checked: nothing types after `Ending sequence`).
  3. A cancelled shred of 017 stops Ellen's last words and frees her pad (`Shred.Cancelled` in `HookFinale`).
  4. The right code works in the Restricted prompt after kept Gary has unlocked the folder (checked: ACH_AUTHORIZED).
  5. Her name is remembered per night (`m.n2.said_name`, `m.n3.said_name`, `MemoryFlags.SaidNameAny`); the old key of no night
     is no longer read, so a replay forgets it.
  6. Night 3's Prepare rebuilds live Personnel after the round (118 on leave when `m.n3.max_stage >= 2`, Custodial's office).
- **Bridge** (`SecondCursorTestBridge.cs` is now partial: `.Inspect.cs` holds dump/ids/texts/shots and the scripted input,
  `.Progress.cs` the new commands): `savedir PATH|off` (kept across recompiles), `resetsave`, `saveset FIELD VALUE`,
  `settings`, `deck on|off`, `store on|off`, `define NAME on|off`, `builddemo`, `saveproject`, `title [main|select|records|
  credits]`, `achievements list|on|off|next`, `haslog TEXT`, `forceexit shred|keep|logoff`, `overlay`. `waitbeat`, `waitflag`,
  `waittask` and `waitidle` follow the current root. `save` and `dump` print the new fields and the records state.
- **Tested through the bridge** (saves under `_work/2026-09-29/saves/`): first launch (exactly New Game, Options, Credits,
  Quit), keyboard-only New Game to the Night 1 card, relaunch keeps `currentNight`, Continue from start and from a checkpoint
  (`Continue: Night 1, 2:00 AM`), New Game confirm, Records, Credits, Night Select after a Night 1 replay (Night 3 still gets
  the finished-Gary branch), every end card and its buttons, pause options (frame rate saved as 60, Story* applied at the next
  checkpoint, Large text in Mail, Quit to Title), the overlay pause, the Deck keyboard and wording, `define SC_DEMO on` (title,
  night clamp, WISHLIST card, store dry run) and the demo player build (`resources.assets` has no Night 2 or 3 text; the
  folders came back with the same GUIDs). All 19 achievements unlocked live (real tug wins for FIRM_GRIP and
  WHITE_KNUCKLES; the three Night 2 asks for REMOTE_SESSION; typed "your glasses" and "ellen"; the code 0217; the shelf
  reject; a safe round at max stage 1; CAM 00) and were held in the negative checks (debug jump, forced tug, Prepare past
  Gary's choice, stage 6 round, kept Gary's unlock). Regression: Nights 1, 2 and 3 each reach their card from the title
  flow; a real finale SHRED. 0 game errors, 0 compiler warnings. CoreTests 220 (40 new: `ProgressTests`,
  `AchievementRulesTests`, `TitleMenuModelTests`, `PhaseEContentTests`). CompileCheck: 8 configurations, 0 errors, 0 warnings
  (`setup_local.sh` fills `.deps` from the installed Unity; new `RuntimeDemo`, `RuntimeSteam`, `RuntimeSteamDemo`).
- **Code review before the commit (fixed).** A key press that opens or closes the pause menu no longer also presses a
  button of the title or end card underneath, or of the menu it opened (`PauseMenu.StateChangeFrame`, skipped by every
  `MenuNav`); Esc going Back on a title sub-screen no longer also opens Options; the corrupt-save notice is reset each launch;
  *Build Windows (Steam)* restores and requires `night2`/`night3` first; Esc during a title root's disclaimer opens the
  settings only; a debug start only moves Continue's night (it is never recorded as a night's first start); an achievement
  that is already saved is not pushed to Steam again (the boot resync covers offline unlocks).
- **Judgement calls.**
  - `achievements on` forces the current run to count and stays on through later debug commands (the per-event gates, a
    forced tug and Prepare, still hold); `achievements next` arms the next root like a menu start, so a forced tug still
    disarms it. The bridge `tug` command does not disarm by itself: the forced outcome disarms when it decides a fight.
  - Night Select over a saved checkpoint asks first, and Yes clears that checkpoint (otherwise Continue would still resume it).
  - Best and total times ignore runs under 1 s (test jumps straight to an ending).
  - Watch the Watchers listens for `n3.cam00_viewed`, which CameraApp sets only when the player selects CAM 00 (also when the
    viewer opens on it); Not On My Shelf listens to `Orders.Decided` (player only); Remain Seated is reported by the round.
    The Phase D `Hook: ACH_*` log lines are gone (a held achievement would still have logged its id).
  - A blank Jotter opens the Deck keyboard only when the player opened it (a cursor's own pad becomes a conversation).
  - Large reading text covers Jotter documents and Mail; the hex viewer stays 1x (16-byte rows do not fit at 2x).
  - The runtime asmdef is unchanged: a reference to the missing Steamworks.NET assembly would only add noise. The Credits
    screen shows `credits.steamworks` under `STEAMWORKS_NET` once that key exists (the license text must be copied from the
    package, not retyped).
  - The version sits at the right of the pause caption (the fullscreen layer is under the taskbar).
  - Editor test hooks stay `internal` with `InternalsVisibleTo("SecondCursor.Editor")` (`Runtime/AssemblyInfo.cs`).
  - Night 2 and 3 code still compiles into the demo; only their content is left out.
- **How to add Steamworks.NET later.**
  1. `Packages/manifest.json`: `"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.164.1"`.
  2. `Scripts/Runtime/SecondCursor.Runtime.asmdef`: add `"com.rlabrecque.steamworks.net"` to `references`, and to
     `versionDefines` `{ "name": "com.rlabrecque.steamworks.net", "expression": "1.0.0", "define": "STEAMWORKS_NET" }`.
  3. `Game/SteamBridge.cs`: the real `FullGameAppId`, `DemoAppId` and `StoreUrl`. Put the App ID in the project root's
     `steam_appid.txt` for Editor tests; never ship that file.
  4. Copy the package's LICENSE text verbatim into the base `strings.json` as `credits.steamworks`.
  5. Steamworks: the 19 achievements and the `TUG_WINS` stat (`Docs/Launch/SteamChecklist.md` section 4), Steam Input
     default config (section 3), Auto-Cloud for `progress.json`.
  6. Bridge `refresh`, `errors` (0) and `warnings` (0); `DevTools/CompileCheck` `RuntimeSteam` is the stub-based twin of this.
  7. On hardware: Deck keyboard (Enter in single-line mode, Backspace, Numeric for the code), overlay pause (and whether the
     keyboard raises it: it is ignored for 0.5 s after showing), suspend during a tug, touch, a PC to Deck cloud round trip,
     launching without Steam (DLL renamed: the game runs with local achievements).
- **Not yet:** the Steamworks package and real App IDs; hardware Deck tests and the trackpad pull-speed measurement; 2x text
  for dialogs, toasts, the Work Queue and the hex viewer; carrying demo progress into the full game; the demo's own Steam
  achievements; a separate screen-shake option; the post-game echo `{p2}` and the idle-finale fast-forward (from Phase D).

### Expansion phase F (balance and pacing)

Section 5 of `_work/2026-09-29/balance/BalanceReport.md` (all 39 rows), re-checked against the code after Phases D and E, plus two
fixes the bridge checks found. Goal: Night 1 (the demo) is winnable by nearly every first-time player within three tugs, each night is
a little harder, each night has one spike that the adaptive assist softens after two losses, and Story is a real 5 to 8 s struggle that
everyone still wins. Spec tables updated in `Docs/Design/Expansion.md` 7.1 to 7.7.

- **Contest rules (P0).**
  - Letting go during a tug is letting go: a release while both cursors grip the file is never a drop (`PointerRouter`), so it cannot
    win the tug or open the shred dialog, and drop targets do not light up under a contested file.
  - She thinks while she lurks (`EntityController.Update`, `EntityBrain.Tick`: anything scoring above lurking interrupts it). Measured
    on the bridge: a drag started while she lurks is grabbed 0.46 to 0.49 s after it begins (was 1.8 s on average, up to 4.5 s).
  - `DifficultyProfile.InterceptDelay` (N1 0.20, N2 0.15, N3 0.12, Story 0.35 s, fading in counts toward it) before she lunges.
  - While her file can be shredded and sits on the desktop she lurks 60 to 120 px around the midpoint between it and the Disposal bin
    (`EntityBrain.PathToBin`), so grabs happen mid-path.
  - Night 2's 209: she lunges within 420 px of the bin when the drag heads for it (within about 37 degrees), else within 150 px
    (`InterceptRadiusHeading`; was 230 px any way).
  - After she loses a tug she does not lunge again for 2 s. The carry loop's 20-step guard no longer counts fight frames.
  - Assist: a won tug no longer clears the loss streak; a lost confirm race or Cancel fight weighs 1.0 (`ReportDefense(how)`), KeepAway
    and a closed File Manager 0.5. Finished Gary's won No race on the Log Off confirm reports "no".
- **Found by the bridge checks (not in the report).**
  - *Corner trap:* a grab next to the bin (late because she was busy snatching the icon) left the player's pull pointing into the taskbar
    corner; a scripted 700 px/s yank lost. `Core/Entity/TugOfWar.cs` `TugGeometry.EscapeDirection` turns her escape direction by the
    smallest angle that leaves the player's pull 200 px of screen (unit-tested; mid-screen it is the old 0.7/0.3 blend).
  - *Grab during a snatch:* grabbing the icon while KeepAway dragged it started a tug, but her `DragTo` kept moving her at full speed, so
    the tension snapped in her favour every time. `EntityController.DragTo` now holds still while a tug over what it carries runs and
    carries on only if she won; KeepAway reports such a tug as a lost tug (grip growth) or, if she lost, gives the 2 s re-grab cooldown.
- **Values (old -> new, reason).** Tug and entity values are in `Core/Entity/Difficulty.cs`, rounds in `Core/Story/CustodialRounds.cs`.

  | Where | Value | Old | New | Why |
  |---|---|---|---|---|
  | All nights | grip growth counts | every defense | lost tugs only (`EntityBrain.TugLosses`) | dialog and KeepAway defenses inflated grip |
  | N1 | GripGrowth | 0.12 | 0.06 | tug 2 was 20% harder than tug 1 |
  | N1 | RaceToNo delay | 0.15-0.35 s | 0.40-0.60 s | an alert Ellen beat even skilled players 81% at L0 |
  | N1/N2/N3/Story | InterceptDelay | none | 0.20/0.15/0.12/0.35 s | human lunge once she is always alert |
  | N1/N2/N3/Story | CancelDelay | none | 0.35-0.55/0.30-0.50/0.25-0.45/0.80-1.20 s | a first-timer who knew to cover Cancel still lost 82% |
  | All | Cancel retry after a fully blocked patience | at once, forever | none for that shred | hover 11.7/13.1/15.1 s -> 8.4/8.7/9.7 s (bridge: 8.6 s) |
  | Normal | TaskForceAfterHint | 240 s | 150 s | a stuck chore waited about 5 min |
  | N2 | GripBase / GripGrowth | 0.70 / 0.10 | 0.68 / 0.05 | N2 tug 3 = N1 tug 1 (170 vs 184 px/s) |
  | N2 | RaceToNo delay | 0.12-0.30 s | 0.20-0.40 s | Gary reaches No first 87% instead of 55% |
  | N3 | pullSpeed / GripBase / GripGrowth | 480 / 0.78 / 0.08 | 460 / 0.74 / 0.05 | first tug 323 px/s, tug 3 218 = N2 tug 1 |
  | N3 | RaceToNo delay | 0.10-0.25 s | 0.18-0.35 s | finale dialog with Gary 28% -> 70% at L0 |
  | Story | tug startShare, playerWinShare, shareRate, base, pullSpeed, maxStrength | 0.40, 0.20, 0.70, 0.35, 300, 2.5 | 0.50, 0.15, 0.30, 0.30, 250, 0.65 | holding still won in 3.07 s |
  | Story | maxTension / strainTension (new) / releaseGrace | 240 / - / 0.15 | off (99999) / 300 / 0.45 | no snap; band and shake stay; a short slip is forgiven |
  | Story | GripBase = GripCap / RaceToNo delay | 0.45 / 0.60-0.90 | 0.41 / 0.90-1.30 | first-timers lost the Story race (Story shred 20-25%) |
  | Assist L1 | grip, growth, pull, ramp, grace+, race+ | 0.88, 0.75, 0.90, 0.60, 0.03, 0.12 | 0.82, 0.50, 0.88, 0.50, 0.04, 0.40 | tug 3 clearly easier; average players win the N1 race at L1 |
  | Assist L2 | same | 0.76, 0.50, 0.82, 0.30, 0.06, 0.25 | 0.68, 0.25, 0.78, 0.20, 0.08, 0.80 | first-time players win the N1 race at L2 (0% -> 99%) |
  | Assist L3 | same | 0.65, 0, 0.75, 0, 0.10, 0.40 | 0.55, 0, 0.70, 0, 0.12, 1.00 | the top level is never a wall |
  | Mercy | grip / effort / effort time / hold time (new) | 0.30 / 0.35 / 1.2 s / - | 0.15 / 0.15 / 1.0 s / 2.5 s (`MercyRelease`) | the release decided 0 of 9 000 simulated mercy contests |
  | Mercy | arming | at L3 only | at L3, or any level in Story (floor +2) | Story: the contest after any loss is a mercy contest |
  | N1 conflict | ends after | 4 defenses | 6 | a struggling player reaches L2 |
  | N1 anomaly | Batch 44 first hint | 60 s | 40 s | 60 s silent after the first anomalies |
  | N1 conflict | player never tries | one mail at 45 s, beat ends at 150 s | mail 45 s, hint toasts 48 and 90 s, beat ends at 100 s | 105 s of dead time |
  | N2 work | first sign of her | about 6 min in | she looks in from the right edge at 90 s (tray mouse blinks) | returning players did chores alone for 6 min |
  | N2 round | duration / forced opens / beat guard | 90 s / 0, 45 / 95 s | 80 s / 0, 25, 55 / 85 s | two 43 s silent holes |
  | N3 round | WatchSeconds (hid 214) | 4.0 (5.0) | 4.5 (5.5) | seat cleared 47-49% -> 28-30% at neutral trust (target 20-30%) |
  | N3 finale | 6:58 event / idle | none / waits the clock out | footsteps + feed flicker / after 60 s without input the clock runs to 7:00 over 15 s | 55 s gap; spec 13.5 |

- **Simulation, before (Phase E code) and after (Phase F), per archetype.** `_work/2026-09-29/balance`, rerun with
  `python run_nights.py 500 final` (`out_nights_applied.md`), `run_tugs.py 500`, `run_story.py`, `run_buttons.py`, `rounds_applied.py`,
  `run_variants_f.py`. The "before" successes are inflated by the release-to-drop bug and the lurk luck (report findings 1 and 2).

  | Night, mode | Player | Shred done in the beat | Median time to shred | First tug lost | A tug won by the 3rd contest | Final assist L |
  |---|---|---|---|---|---|---|
  | N1 Normal | first-time | 44% -> 32% | 39 -> 48 s | 17% -> 27% | 99% -> 100% | -1 -> 2 |
  | N1 Normal | average | 90% -> 88% | 29 -> 33 s | 13% -> 5% | 97% -> 100% | 0 -> 1 |
  | N1 Normal | skilled | 99% -> 100% | 22 -> 14 s | 16% -> 0% | 92% -> 100% | 0 -> 0 |
  | N2 Normal | first-time | 100% -> 100% | 28 -> 20 s | 69% -> 24% | 50% -> 100% | 0 -> 0 |
  | N2 Normal | average | 100% -> 100% | 21 -> 10 s | 27% -> 22% | 77% -> 100% | 0 -> 0 |
  | N2 Normal | skilled | 100% -> 100% | 14 -> 10 s | 6% -> 3% | 95% -> 100% | 0 -> 0 |
  | N3 Normal (finale) | first-time | 100% -> 100% | 57 -> 54 s | 89% -> 70% | 82% -> 59% | 1 -> 2 |
  | N3 Normal (finale) | average | 100% -> 100% | 27 -> 18 s | 12% -> 25% | 96% -> 100% | 0 -> 0 |
  | N3 Normal (finale) | skilled | 100% -> 100% | 21 -> 11 s | 19% -> 22% | 89% -> 100% | 0 -> 0 |
  | N1 Story | first-time | 20% -> 97% | 38 -> 26 s | 0% -> 19% | 100% -> 99% | 2 -> 2 |
  | N1 Story | average | 98% -> 100% | 19 -> 24 s | 0% -> 2% | 100% -> 100% | 2 -> 2 |
  | N1 Story | skilled | 100% -> 100% | 13 -> 23 s | 0% -> 0% | 100% -> 100% | 2 -> 2 |

  Per contest (win if a tug happens, first-time / average / skilled): N1 tug 1 84/88/89% -> 71/93/100%, tug 3 93/96/100% -> 83/99/100%;
  N2 tug 1 38/70/95% -> 77/77/97%; N3 tug 1 13/90/86% -> 28/71/79%, tug 3 73/96/100% -> 45/97/100%. Story fight length at L2
  0.2-0.5 s -> 6.9/6.1/5.7 s. N1 confirm race with Ellen alert, L0..L3: 0/0/19, 0/0/70, 0/10/98, 0/56/100% -> 0/10/98, 18/100/100,
  99/100/100, 100%. Yank needed on the assist path: see Expansion 7.7 (N1 184, 202, 127...; N2 248, 268, 170...; N3 323, 347, 218...).
  Rounds: N3 seat cleared (mixed population, kept / finished Gary) low trust 84/85% -> 59/84%, neutral 47/49% -> 28/30%, high 15% -> 15%;
  N2 a passive player ends at the Corridor (was HallFar 73%), a player who peeks up to three times reaches the doorway 48% (was 0%).
- **Tests.** CoreTests 253 (33 new): `DifficultyCurveTests` (first-grip table, the growth-aware path of 7.7 within 5%, each lost tug only
  a little harder, tug 3 under 75% of tug 1, third tug vs last night's first growth-aware, Story holding still does not win in 20 s,
  a 150 px/s ratchet wins in 4 to 9 s, strain without a snap), `DifficultyProfileTests` (Phase F tables), `AdaptiveAssistTests`
  ("tug won, dialog lost" twice raises L, lost races weigh 1, keepaway/close 0.5, Story mercy at L2, a lowered level starts a fresh
  loss streak), `TugGeometryTests`,
  `MercyReleaseTests` (2.5 s held releases, 1 s of pull releases, a mercy contest cannot be lost by holding on). Updated with reasons:
  Night 1 slice values (race delay, growth, force-complete), grip formula (0.06), assist scaling (L1 row), "a win breaks a loss streak"
  (now it does not), hold-still times (N2 0.88 s, N3 0.77 s), third tug vs last night's first (now growth-aware: first-grip N3 L1 202 vs
  N2 L0 248 is 0.81), Night 3 grip 0.74, Night 2 round (80 s, 0/25/55), Night 3 watch seconds 4.5/5.5.
- **Bridge checks** (new commands `dragtug X Y DUR [HOLD]`, `tugplay SPEED [TIMEOUT]`, `waitaction NAME`, `tugs` in
  `SecondCursorTestBridge.Balance.cs`; saves under `_work/2026-09-29/saves/phaseF`): a drag onto the bin released mid-tug ends in
  EntityWins after 0.10 s with no shred request; a drag started while she lurks is grabbed after 0.46 to 0.49 s; real (not forced) tugs,
  one lost (holding still, or the release) and one won (steady yank away from her) on each night on Normal; the full Night 1 attempt
  (tug won, Yes before her No, Cancel held: "Gave up on Cancel" after 6 s, shredded 8.6 s after Yes); Story Night 1 shred with a real
  200 px/s pull (tug 3.2 s); Night 2 round (opens at 0/25/55 s, ends at 80 s at the Corridor); Night 3 round at 3x speed (safe at Middle
  after 360 s); finale idle fast-forward (60 s idle at 6:46, flicker at 6:58, 7:00 after 15 s, KEEP at 7:05: 135 s instead of about
  270 s); Night 1 untried conflict (mail, hints, moves on at 100 s); Night 2 glimpse at 94 s. Regression from the title on a fresh save:
  New Game, Normal, Night 1 to its card, Continue to Night 2 to its card, Continue to Night 3 to its card (SHRED), each with a real tug
  win and shred; 0 game errors, 0 compiler warnings. CompileCheck: 8 configurations OK.
- **Code review before the commit (fixed).** A carry that is grabbed twice in a row waits out the second tug too (it used to let go and
  hand the player a free win); a stopped idle fast-forward can no longer leave the clock fast in the ending; lowering the assist level
  clears the loss streak; she gives up on Cancel for that shred only after a whole patience held off (a click that missed for another
  reason retries as before).
- **Judgement calls.**
  - Assist race adds +0.40 (L1) and +0.80 (L2) instead of the report's +0.25/+0.60: Night 1 first-time players shred 017 32% instead of
    20% of the time (average 88%, skilled 100%) while the first contest stays the same. The shred is optional (the story branches on it).
  - Night 3 round: 4.5 s per stage (5.5 after hiding 214) with the 22-30 s repeats kept, instead of 5.0/6.0 with 26-34 s. With Phase D's
    0.4 s faster close the report's values leave only players who keep looking (15%); 4.5 s gives 28-30% at neutral trust.
  - InterceptDelay counts her fade-in, so an invisible Ellen reacts in the same time as a visible one.
  - The lurk anchor applies only while her file is on the desktop and can be shredded (Night 3's work beat keeps the lurk near your cursor).
  - The Night 1 hint toasts come at 48 and 90 s (not 45) so they do not stack on the supervisor's mail at 45 s.
  - No new writing: Night 2's early sign is a wordless glimpse from the right edge; the 6:58 event is footsteps, a glitch and static on the
    feed if it shows.
  - The idle fast-forward only runs to 7:00: from 7:00, KEEP at 7:05 is 60 s away at the normal rate anyway. Any input hands the clock back.
  - A steady 150 px/s pull wins Story in 3.2 s (the floor for a perfectly steady pull); human strokes take 5.7 to 6.9 s in the simulation,
    so the 4 to 9 s test uses a stroke-and-slide ratchet.
  - Night 3 first-time players still lose most finale tugs early (tug 3 45%), which is the night's spike; every archetype wins a tug in the
    beat and the finale has KEEP and LOG OFF.
- **Needs real players.** The archetypes (reaction, stroke speed, slips) set the absolute numbers; watch for: first-time players at the
  Night 1 confirm race (0% at L0 and L1, 99% at L2: the lever is the L1 race add), how the Story struggle feels, the Night 3 seat-clear
  rate (15 to 35%), whether the heading-based Night 2 intercept reads as fair, the sideways escape after a grab by the bin, the 60 s idle
  threshold, Steam Deck trackpad yank speeds (`GameRoot.DeckPullSpeedScale` is still 1), and whether Ellen hovering between the file and
  the bin (instead of near your cursor) keeps her creepy.
- **Not yet:** the simulation does not model the corner-trap and mid-snatch fixes (it assumed mid-path grabs); marketing tweaks M1-M15.

### Expansion phase G (release safety, clarity, stream moments, store art, final builds)

The Phase G brief (`_work/2026-09-29/launch/PhaseG_Brief.md`, sections A to F) with its inputs: `ReviewPhaseE.md`, `LaunchAudit.md`
section 4 (now in `Docs/Launch/`), `MarketingPackFull.md` 1.4 (now in `Docs/Launch/`), and `ReviewPhaseF.md`, added mid-phase. Phase F's
tuning is unchanged: no difficulty value moved, and its finale idle fast-forward is kept (only its idle detection was fixed).

- **Release safety (ReviewPhaseE).**
  1. `SteamBridge` (STEAMWORKS_NET only): nothing is pushed until Steam has delivered the user's stats (`UserStatsReceived_t` for this app
     with `k_EResultOK`, or a `GetAchievement` poll every 2 s as a fallback). Saved achievements and `TUG_WINS` are queued at init; unlocks
     and the stat go through the queue; an id Steam refuses stays pending, and a failed `StoreStats` puts back everything that call sent.
     The rules live in the engine-free `Core/Game/AchievementPushQueue.cs` (unit-tested); the CompileCheck stub gained the stats types.
  2. `SecondCursorBuild.BuildWindows` refuses SC_DEMO in the Standalone defines. `SecondCursorBuildGuard` (an `IPreprocessBuildWithReport`,
     so it also covers File > Build Settings) refuses a non-demo build while `Assets/SecondCursor/_DemoExcluded` exists or SC_DEMO is
     defined. The demo build sets a SessionState flag while it moves content; on every editor load (and domain reload) content left in
     `_DemoExcluded` by an interrupted demo build is moved back once the editor is idle.
  3. Both builds refuse to ship when Steamworks.NET is compiled in (STEAMWORKS_NET in the defines, or the runtime asmdef's version define
     with the package present) and the App ID is still 480 or `credits.steamworks` is missing (`SteamReleaseProblem`).
  4. QA launch arguments: `-scnight` / `-scbeat` make progress.json read-only for the whole launch (`SaveSystem.ProgressReadOnly`: no
     night start, checkpoint, completion, tug total or achievement is written; settings still are). They stay available in release
     builds for smoke tests. `-scsavedir` logs a warning with the folder at launch.
  5. The first-launch flashing choice re-reads settings.json before saving it (Options changed behind it are kept).
  6. `SaveSystem.Read`: only a file that was read and does not parse is set aside as `.corrupt`. IO and access errors are retried
     (4 attempts, 40/80/120 ms). Review fix: a main file that stays unreadable is never written over (not even from its older `.bak`)
     until a later read succeeds.
- **Review fixes (ReviewPhaseF).** 1: the finale's idle check measures movement from the last real move (4 px) instead of per frame,
  and counts the right button, the wheel and typing. 2: pausing inside a tug win's hit-stop saves the scale the hit-stop returns to
  (`ConflictSystem.ScaleBeforeHitStop`), so Resume never leaves the game at 3%. 3: the second cursor never lets go during a tug: the
  intercept carry, `DragTo` and the story's `CarryFileIn` (now `EntityController.CarryTo`, contest-aware) wait a fight out before the
  release; `CarryFileIn` leaves a file the player took where the player put it. 4: `RunCancelShred` resets the Panicked state when the
  dialog goes during the reaction delay. 5: every tug she wins during one intercept counts (the `CounterTugLosses` delta, like
  KeepAway). 6: `AdaptiveAssist.SetLevel` respects the mode's floor.
- **Clarity (LaunchAudit section 4, all 20 items; 7 was done in Phase F).**
  1. Jotter, editable files: `*` in the title while unsaved, a status line ("Text is added at the end. File > Save (Ctrl+S) to apply."),
     Ctrl+S saves (`ControlChars.Save` through both input backends, bridge `type \s`), and closing asks "Save changes to session.cfg?"
     Yes / No / Cancel (`OSWindow.CloseGuard`, player closes only).
  2. Log Off: before 7:00 without the policy the dialog names `Restricted\session.cfg`; `logoff.disabled` names the folder too; the
     7:00 notice says "Log off from the Nexus menu."; a notice announces "Log Off CROURKE... added to the Nexus menu." at the lost hours.
     Both notices open the Nexus menu when clicked (`StartMenu.OpenFromElsewhere`, next frame), and only the player's own presses close
     that menu now (the ghosts' clicks used to close it).
  3. The Batch 44/45 hints name "Workstation (File Manager)"; the Quick Start says "Workstation opens File Manager".
  4. After her first three Jotter lines a notice says "Jotter: a remote session is typing. Type a reply and press Enter." (Deck
     variant); `ex_stop` silence starts with TYPE SOMETHING.
  5. Night 2's remote tasks say "Not assigned by Night Operations." and their target (folder and file, or Personnel number).
  6. `n2_rounds_again` adds OR LOOK AT ANOTHER CAMERA; if the player never reopens the viewer, that line is typed at the second forced
     open.
  8. The last card shows "Endings seen: n of 3". 9: the "drag firmly away" notice on the first lost tug on every night.
  10. "last operator", "previous operator", "old operator", "before me", "last guy" join every Ellen exchange's Gary group.
  11, and the user's priority note: Help explains every mechanic of the three nights in the UI's words (Work Queue and remote-session
     tasks, moving files, shredding, work orders, tug-of-war and how to win it, typing in Jotter, cameras and switching, locked folders
     and codes, saving files, the Nexus menu and Log Off, pausing); the Help window is larger, scrolls and follows Reading text. The
     Quick Start points to NEXUS Help for everything else.
  12. The disclaimer says "in Options (Esc, or the || button on the taskbar)".
  13. The pause menu's Quit asks first. 14: Esc on the code prompt closes only the prompt (`AppManager.EscapeHandledFrame`).
  15. Demo spoilers: 76 base strings only Nights 2 and 3 or Records use (hidden achievement text, rounds, log off, the code prompt,
     third pointer, remote queue, Night 2 cards) moved to `Resources/Content/full/strings.json`, loaded in the full game after the base
     and before the night overlays; the demo build leaves `full` out with night2 and night3. Night 2/3 directors, `EndingSequence.Night3`,
     the CAM 04 and CAM 00 sets, Night 3 template tokens and personnel patches and the shelf names are compiled out of SC_DEMO.
  16. Notices and the Work Queue detail pane follow Reading text (Large doubles them; the queue opens wider); notices grow to fit their
     text (a third wrapped line used to spill out).
  17. Every hint that says "drag" has a `hintDeck` (Nights 1 to 3).
  18. Welcome back: a short Quick Start ("Welcome back, Operator.") when Night 2 or 3 is the first night of a launch in a counted run.
  19. The 3:31 to 6:41 clock rolls over 1.2 s with soft ticks. 20: later launches show "Click to continue" under the disclaimer.
  Also: "hello" no longer counts as swearing (`=hell` in the categories).
- **Stream moments (MarketingPackFull 1.4).** M1: a CCTV tag "CROURKE / WS-04" above the seated operator on CAM 03; the arm ignores
  the mouse for 1.2 s after the feed comes up, then follows with a 0.3 s lag for 2 s. M2: keyword hits wait 2.0 s with a blinking caret
  (the old 1.1 s plus the 0.9 s think), fallbacks and silence 0.4 s; the slang group (skibidi, rizz, sigma, gyatt, ohio: THAT WORD IS
  NEW / I STOPPED KEEPING UP) above "who/what" in every Ellen exchange. M3: `~nxs0149.tmp` quotes the Night 1 lines as a session log
  (`02:04 CROURKE> make me`, new `{log1..3}` tokens and `SaveData.playerLineMinutes`); READ IT FIRST when the cursor rests on the file
  for 2 s; "Contains operator input." on the task. The demo and full game have different product names, so their saves are separate:
  no carry-over is claimed. M4: in the SHRED dark a quiet heater tick about once a second until the card, and the player's arrow wears
  her palette for the one frame it types the C. M5: on a lost tug she nods (4 px) and the strain sags two semitones over 0.3 s. M6: the
  Night 1 reveal's drone drops 6 dB and a semitone while the feed is shut and snaps back; a reopened feed resolves the figure out of 0.5 s
  of static. M7: 1.2 s of silence and a 2 s blinking caret after "thank y"; the 209 shred confirm adds "Record owner: 209 (held)."
  M8: two missed replies to AT SEVEN get another turn each, the third miss gets STAY / OR LET ME GO and one more turn; the tray clock is
  amber from 6:55 to 7:00. M9: a remote Work Queue row loses 10% every 15 s of its life and blinks for its last 3 s. M10: CAM 04's loop
  starts at shelf 17 each time the feed switches to it and holds shelf 18 for 5 s, with a one-frame flicker on each swap
  (`Core/Story/ShelfCaptions.cs`). M11: hollow clicks for the second cursor, soft and 15% quieter for the third, a soft pat and a
  rattle when a ghost's press is refused by another ghost. M12: on the demo card the second cursor rests beside WISHLIST (the button,
  or the WISHLIST NOW line) and steps 30 px aside when yours comes within 60 px. M13: a pale dotted trail of the replayed drag fades
  over 1.5 s. M14: 2.4 s of dead air with everything ducked (`AudioManager.Duck`), then the Disposal bin rattles for 0.4 s before the
  file is back. M15: the clock roll above, the notice's Duration line 0.6 s later, and the notice stays until clicked.
- **Store art** (`Editor/SecondCursorStoreArt.cs`, `SecondCursorStoreArtCanvas.cs`; menu *SECOND CURSOR > Render Store Art*, bridge
  `storeart`): capsules (header, small, main, vertical), library capsule, header, hero (no text) and logo (transparent), page background,
  community icon, client icon (.ico and PNG) and 19 achievement icons with locked versions, drawn from the game's sprites and bitmap font
  into `Builds/StoreArt/`. Screenshots: bridge `storeshot NAME` / `storeshotafter SECONDS NAME` capture the Game view supersampled,
  crop to 16:9 and resample to 1920x1080 into `Builds/StoreArt/screenshots/` (10 taken, S0 and S1 only).
- **Builds.** Apply Release Settings also turns off engine diagnostics, cloud diagnostics, analytics, performance reporting, hardware
  statistics and the crash report API (checked in `ProjectSettings/UnityConnectSettings.asset` and `ProjectSettings.asset`). Each build
  empties its output folder first and moves `*_BackUpThisFolder_ButDontShipItWithYourGame` to `Builds/Symbols/<Windows|WindowsDemo>/`.
- **Bridge** (`SecondCursorTestBridge.PhaseG.cs`): `savecheck`, `qaread on|off`, `settingsset FIELD VALUE`, `storeart`, `storeshot`,
  `storeshotafter`, `buildguard`, `democrash`, `contentfolders`, `reload`, `steamcheck`; `type` understands `\s` (Ctrl+S); `build` and
  `builddemo` report a refused build instead of throwing.
- **Tests.** CoreTests 278 (25 new: `AchievementPushQueueTests`, `PhaseGTests`: demo strings, help coverage, hints and Deck hints,
  remote task text, dialogue keywords, session log tokens and saved minutes, the shelf loop, the assist floor). Updated for the full
  strings folder: `ContentTests`, `NightContentTests`, `PhaseEContentTests`. CompileCheck: 8 configurations OK (stubs: stats types,
  `Keyboard.sKey`).
- **Checked through the bridge** (saves under `_work/2026-09-29/saves/phaseG`): save lock and damaged file (`savecheck`: short lock
  retried, long lock not overwritten, damaged file set aside); read-only QA progress; the disclaimer keeping a frame rate set behind it;
  `define SC_DEMO on` refused by `build` and the guard (and the editor compiles with SC_DEMO); an interrupted demo build refused, then
  restored by a domain reload; the Steam release check. Fresh save from the title: New Game, Normal, Night 1 with the new Quick Start
  line, the Workstation hint, the Jotter notice, slang and last-operator replies, a real tug won and 017 shredded (back 2.4 s later),
  the CAM 03 tag, the card; relaunch, Continue to Night 2 (Welcome back, READ IT FIRST, the session log in the cache file, remote task
  text, finished Gary with the 209 note, the look-away line), card; relaunch, Continue to Night 3 (Welcome back, Esc on the code prompt,
  the code, session.cfg edited: title `*`, the save prompt, Cancel, Ctrl+S, close without prompt; shelf 17 then 18 on CAM 04; rounds at
  3x; the clock roll, the Duration line, the Log Off notice; three missed finale replies steered; LOG OFF from the Nexus menu), card.
  Separately: notices stack and fit, the Nexus menu opens from the Log Off notice, the amber clock, the SHRED ending, "stay" after the
  steer. 0 game errors, 0 compiler warnings.
- **Judgement calls.**
  - M8's line is typed in Ellen's voice as two lines, STAY / OR LET ME GO (her lines never carry punctuation; the content test enforces
    it), and the steer gets one more turn (otherwise it names two words nobody can type any more).
  - M2's think-pause is added to the existing 1.1 s beat for hits (2.0 s total) and replaces it for misses (0.4 s).
  - M6's "feed room tone" does not exist (the feed has no audio of its own); the duck is applied to the reveal's drone, which is what
    plays while the feed is shut.
  - M4's heater tick reuses a low, quiet key tap (no new procedural sound).
  - The tagline appears only on the page background: Valve's capsule rules and the capsule brief keep capsules to the title.
  - Help follows Reading text and scrolls, so it can list every mechanic without a second screen.
  - The Welcome back refresher shows only in counted runs (not after a debug jump), once per launch.
  - Only the player's presses close the Nexus menu (the ghosts clicked it shut in the finale).
  - A QA launch keeps settings writable (window size and volume are harmless).
- **Not yet.** Steamworks.NET and the real App IDs (the build checks will insist on them); Steam Deck hardware tests; D3D12 and
  DirectStorage files still ship (4.5 MB and 1.7 MB) although D3D11 is the only API; bundleVersion is 0.9.0 (set 1.0.0 by hand before
  release); the store art is a generated first pass (a hand-made key art can replace it); a trailer.

### Phase H (blind playtest fixes)

Input: `_work/2026-09-29/playtest/BlindPlaytest.md` (a screen-only first-time playtest of all three nights, 25 findings) and
`_work/2026-09-29/launch/PhaseH_Todo.md`. The owner's rule: an average person must understand the game from what the screen says.
The game got clearer, not easier: no difficulty value, timing or story branch changed.

- **Tug-of-war (finding 1).** `Runtime/Entity/TugHud.cs`: while two cursors grip a file, a NEXUS label beside it reads
  `SESSION 017 IS PULLING. / HOLD THE BUTTON AND DRAG AWAY.` (`tug.label`, Deck: R2) with a pull meter, YOU on the left and 017 on the
  right (`ConflictSystem.PlayerLead`, from `TugOfWar.PlayerLead`: 0 = she is about to take it, 1 = you are about to keep it). The label
  takes the side of the file (above, below, left, right) that hides neither pointer. When the fight ends it says `YOU KEPT THE FILE.` or
  `SESSION 017 TOOK THE FILE. / GRAB IT, HOLD AND DRAG AWAY.` next to the winner's cursor. `notify.conflict` (first lost tug) names
  session 017, with a Deck variant. The Quick Start has the tug line; Help says a bar shows who is winning.
- **Tug verification (Night 1 Normal, first contest, real tugs, not forced).** `dragtug 915 66 2.2` from the desktop file toward the
  bin (she grabs 0.40 to 0.46 s after the drag begins), `wait 0.25`, then `tugplay SPEED`, a continuous pull straight away from her:
  before the HUD 10 of 10 won (300 to 600 px/s, 0.63 to 0.93 s), with the HUD 9 of 10 (the 300 px/s try lost once). The tester's
  pattern (the pointer held still between moves) loses in 1.07 to 1.09 s, 3 of 3, and so does a 0.5 s pause before the pull (0 of 3): after
  half a second of holding still the file is too far gone for the snap. No balance change: a realistic pull wins the first contest.
- **Silent refusals (finding 2 to 4, 9, 14).** `Runtime/OS/SystemNotices.cs` says who did it: `Shred of X cancelled by session 017.`
  (her No or Cancel), `Camera Viewer closed by session 017.` (any app window another cursor closes, at most every 6 s per app),
  `employee_017.dat moved by session 017.` (KeepAway). A lost tug released over the Disposal bin adds `Disposal refused X: session 017 is
  holding it...`. `error.inuse.body` names session 017. The Camera Viewer icon was never locked: during rounds the viewer opened and was
  closed by her within 1.2 to 2 s, between the tester's screenshots; the close notice now says so. Jotter conversations show a status
  line: `Remote session is typing. Your reply is sent when it stops.` while keys are typed ahead, and `Remote session is not reading.` when
  nobody has typed, thought or waited for you for 3 s (those keys, and keys typed ahead into a conversation that stopped, are dropped instead of being sent
  minutes later). A saved config names the saver (`file.saved.by`, session 209 for Gary) and what the file now decides
  (`AppManager.SavedNote`: `Log off at 7:00 AM: allowed (ALLOW_LOGOFF=1).` or `not allowed. The last ALLOW_LOGOFF line counts...`; the
  same for camview.cfg). Gary's Cancel on the Log Off progress: `Log off cancelled by session 209.`
- **Deadlines and the queue (findings 6, 7, 11, 12, 21, 22).** `WorkTaskManager.Withdraw(id, note)`: a task the player saw that is taken
  back with a note stays in the Work Queue, struck through, with the note in red at the right (`MISSED 3:00 AM` for 209 at the deadline,
  `SUSPENDED` when 209 was archived, `CANCELLED` for an unfinished shelf check); remote items still vanish as before (M9). The deadline
  notice: `notify.order.missed`. `TaskDeadline` (Core) turns "3:00 AM" into minutes: the taskbar Task button reads `8 min left: ...` (red
  from 5 min) and the queue `Due: 3:00 AM (8 min left)`. `TaskType.Wait` (an information line only the story completes):
  `t2_rounds_watch` "Rounds: keep the Camera Viewer open" during the Night 2 round, `t3_wait_rounds` "Wait for Custodial rounds (3:00 AM)"
  after Batch 48 until the Night 3 round. Unread mail that arrived with a notice this shift gets a line at the bottom of the queue
  (`New mail: <subject>`, click opens it) and the desktop Mail icon shows the unread envelope while anything is unread. Notices asked for
  together arrive 1.1 s apart (`Notifications.Stagger`), slide in and out sideways (never across the Disposal bin), and a task hint goes
  away as soon as its task is done or withdrawn (`keepWhile`).
- **Windows (findings 11, 13, 15, 16).** `WindowManager.PlaceAvoidingOverlap`: app windows open right of the icon column (x from 88);
  covering the Work Queue costs 2.5 per pixel (the viewer Security opens at 3:00 lands left of it); Work Orders keeps its Approve and
  Reject strip clear (`OSWindow.KeepVisibleBottomRight`, weight 12), so Personnel opens above it when there is room (with Mail and File
  Manager also open there may be none); Mail opens clear of the notices' column. Mail is 460 px tall (was 380) and shows `More below`
  while the message continues under the fold.
- **Night 3 shelf check (findings 5, 23).** The CAM 04 loop starts at shelf 17 the first time CAM 04 comes up (M10) and then keeps
  running while you look elsewhere (it used to restart at 17 on every switch, so shelf 16, 26 s into the loop, could be unreachable
  between forced opens every 22 to 30 s). The label and a `NEXT: SHELF 13` line sit at the top of the feed, where a window over the lower
  half cannot hide them. The hint says shelves 12 to 19 come in order and what to do when the viewer closes. When it is done the queue
  line becomes `Shelf check filed: 1 approved, 2 rejected` (and a notice). Opening WO-3342 during the round: Ellen types `THAT ONE IS YOU /
  YOU DONT HAVE TO SIGN IT` (`n3_shelf_you`).
- **Smaller fixes.** Night 1's card has an outcome line (`WS-04 went dark at 2:40 AM. The file came back.` or `... The file is still on
  the desktop.`, `EndingSpec.Outcome`, also on the demo card); the Night 1 briefing adds "If WS-04 goes down before then, go home and I will
  sort it out."; progress dialogs ignore the player's Cancel for 0.5 s (`Dialogs.CancelGrace`: a reflex click after Yes; her and Gary's
  clicks are never ignored, and hovering Cancel still blocks her); remote Night 2 items have Hint lines (with Deck variants); the briefing
  hint says the briefing opens by itself. Night 2 and 3 already had date cards (2.5 s, checked); the tester's screenshots fell between.
- **PhaseH_Todo.** 1: Quick Start "Esc or the || button on the taskbar: pause and Options." 2: `notify.conflict.deck`. 3: `start.button`
  is "Nexus" and the taskbar reads it. 4: the pause caption is `SESSION PAUSED: OPTIONS`. 5: both builds move `D3D12\D3D12Core.dll`
  (4.5 MB) to `Builds/Symbols/<build>/NotShipped` while Direct3D 11 is the only API (`SecondCursorBuild.StripUnusedGraphicsFiles`); the
  players start and render without it. `dstorage.dll` and `dstoragecore.dll` (1.7 MB) must ship although DirectStorage is off: a player
  without them hangs at startup before it writes a log (tested). 6: fresh builds: full 71.9 MB and demo 71.6 MB on disk (Unity's report
  76.5 / 76.3 MB counts the D3D12 folder moved out afterwards), Symbols in `Builds/Symbols`, windowed smoke tests with saves on D: clean (full, demo, full with
  `-scnight 3 -scbeat rounds`), the demo still carries none of the new Night 2/3 text.
- **Regression from the title on a fresh save** (`_work/2026-09-29/phaseH/reg_n*.cmd`): New Game, Normal, Night 1 to its card (tug won,
  017 shredded, outcome line), Continue to Night 2 to its card (finished), Continue to Night 3 to its card (KEEP); 0 game errors, 0
  compiler warnings. The scripted drags met two designed disruptions (Night 2's batch45_c rename reorders the rows mid-drag; Ellen's
  Jotter opens over File Manager on Night 3), so a few chores there were finished by the existing safety nets; every new line and notice
  was checked separately above.
- **Tests.** CoreTests 295 (17 new in `PhaseHTests.cs`: due times, withdrawn-with-note and result lines, Wait lines, the pull meter, the
  new strings and their Deck variants, remote hints, the shelf check text). CompileCheck: 8 configurations OK.
- **Checked through the bridge** (saves under `_work/2026-09-29/saves/phaseH*`, screenshots `Library/SecondCursorBridge/shots/h_*.png`): the tug
  label, meter and both results; the loss notice; Disposal refused (Night 2, released over the bin mid-tug); shred cancelled by session 017;
  Camera Viewer closed by session 017 (Night 1 reveal, Night 2 and 3 rounds); moved by session 017 (KeepAway); Quick Start; briefing hint;
  Mail taller with More below; File Manager at x 88; Personnel above Work Orders' buttons; the Cancel grace (shred and Log Off); hint
  toasts; Jotter typing and not-reading lines; the Night 1 card outcome; the Night 2 card; remote hints; `11 min left`; MISSED 3:00 AM
  and its notice; the rounds line; staggered notices; the viewer left of the queue; Wait for Custodial rounds; Ruth's mail; CAM 04 NEXT and
  the loop continuing after a reopen (shelf 13, not 17); THAT ONE IS YOU; the filed line; session.cfg saved by session 209 and by the player
  (0 then 1); Log off cancelled by session 209; the pause caption.
- **Judgement calls.**
  - The tug label is a NEXUS tooltip (the OS voice), not Ellen's: caps because it is an alarm, and it names session 017 like every other
    system line.
  - The keep list wins: the M9 remote rows still fade and vanish; only company tasks the player saw stay listed when taken back.
  - Camera placement: the Work Queue is weighted, not forbidden; when three big windows are open something overlaps, and the player can
    drag windows.
  - "Not reading" drops the typed keys instead of sending them at the next turn, which could be minutes later and answer something else.
  - The mail line lists any mail that arrived with a notice this shift (flavour mail too): one rule the player can learn.
  - The CAM 04 loop fix changes M10 from "every switch" to "the first switch"; the RESERVED shelf is still the second label anyone sees.
    After a reopen the loop is wherever it has got to, so one shelf can be up to a cycle (29.5 s) away where shelf 18 used to be 3.5 s
    away; in exchange shelf 16 is always reachable, and the NEXT line says what comes.
  - "Not reading" waits 3 s of silence after her last keystroke, think pause or your last sent line, so a reply on its way (the think
    pause and her hand reaching the pad) is never mistaken for nobody; the status strip gets its own room under the text.
  - No difficulty change: the realistic continuous pull wins 9 to 10 of 10 first contests.

### Phase I (second blind playtest fixes)

Input: `_work/2026-09-29/playtest/BlindPlaytest2.md` (a second screen-only playtest of all three nights, 22 findings, 10 tug
attempts and 1 win) and the coordinator's decisions. The rule is still: an average person must understand the game from what the
screen says. The game got clearer, not harder or easier, except for one deliberate change (the read grace below).

- **Tug read grace (findings 1, 14).** The night's first contest (each `ConflictSystem` starts armed; `ArmReadGrace()` again when Story is
  applied at a checkpoint) starts with a 1.2 s standoff (`DifficultyProfile.ReadGraceSeconds`, `TugOfWarSettings.readGrace`,
  `TugFor(assist, mercy, readGrace)`): her pull cannot move the share toward her, she cannot snap the file away through tension, her
  drift is held (only the tremble), her ramp and the assist count from the end of the grace (`TugOfWar.ActiveElapsed`). The player's own
  pull counts from the first frame, and letting go still loses at once. Every later contest keeps today's values (readGrace 0). The
  label and the YOU/017 meter are up the whole time. A pixel arrow on the file (`TugHud`, 19 squares on a 3 px grid, a bright band
  running to the tip) points straight away from her pointer. A lost fight says why: `YOU LET GO. HOLD THE BUTTON UNTIL YOU KEPT THE
  FILE.` (`ConflictSystem.LastLostByRelease`) or `SESSION 017 PULLED HARDER. DRAG FASTER, AWAY FROM IT.`; a file KeepAway takes while
  you are not holding it says `SESSION 017 TOOK THE FILE WHILE YOU WEREN'T HOLDING IT.` beside the icon (`TugHud.ShowMessage`, from
  `SystemNotices`) and in the notice. Quick Start and Help resolve the "let it finish" mail against the fight: "If mail says to let the
  other pointer finish, let it. But if a task tells you to shred a file, hold on and fight for it." The t_shred_017 and t2_shred_209
  hints say what to do when it is grabbed.
- **Tug numbers (Night 1 Normal, first contest, real tugs on the bridge, `dragtug 915 66 2.2` then `wait D` then `tugplay S`):**

  | Pull starts after the grab | continuous pull 300 to 600 px/s (10 speeds) | Phase H code, same pull |
  |---|---|---|
  | 0.25 s | 10 of 10 won | 10 of 10 (Phase H) |
  | 0.8 s | 10 of 10 won | lost every try (0.5 s pause: 0 of 3) |
  | 1.2 s | 10 of 10 won | lost every try |

  Second contest of the same shift (no grace): pull after 0.25 s 4 of 4 won (300, 400, 500, 600 px/s), after 0.5 s 1 of 4 (only 600),
  after 0.8 s 0 of 4: unchanged from Phase H. `PhaseITests` reproduces both rows in the simulator (grace: all 30 combinations win;
  without it a pull starting at 0.8 s wins at most 2 of 10). Holding still through the grace loses about 1 s after it ends.
- **Impossible-by-design orders (findings 2, 3, 20).** `WorkTask` can be rewritten (`WorkTaskManager.Rewrite`, `TitleOverride`,
  `DescriptionOverride`, `HintOverride`): Night 1's order becomes `PRIORITY: Shred employee_017.dat (blocked: held by session 017)` with
  "You cannot shred it while session 017 holds it. Nobody can. Keep watching." when the bin says File In Use (`Shred.RefusedInUse`)
  or the fight beat ends with the file still held (`task.blocked.017.*`); the Night 1 card says "Session 017 was holding the file.
  Nobody could shred it." Night 2: a Confirm Shred for employee_209.dat at 3:00 AM or later adds "Too late. Reclamation already started.
  Shredding now releases only part of the record." (`ShredService.ConfirmNote`, `shred.confirm.late`), so "He is still held" reads as
  the result. Remote requests that ran out or whose file is gone stay in the queue struck through with `EXPIRED` (`Withdraw(id,
  "expired")`), instead of vanishing. The Work Queue wraps a long title to a second line (the blocked order, "Shelf check: Sublevel C
  (3 orders) (0/3)") and shows `Queue clear. Await further assignments.` under the ticked rows when nothing is left.
- **Clock (finding 9).** Checked frame by frame on the bridge and in the tester's own screenshots (crops in
  `_work/2026-09-29/phaseI/n2_clocks.png`, `n3_clocks2.png`): the clock never ran backwards. The report's "2:58 then 2:55" and "6:58
  then 6:51" are 2:50, 2:55 and 6:50, 6:51: the small pixel font's 0 and 8 look alike in a downscaled screenshot, and "10 min left" was
  correct next to 2:50. It is now guaranteed anyway: `GameClock.Set` only moves forward (an earlier time is refused and counted in
  `RefusedBackSets`; `Reset` starts a clock at any time and is used only for a fresh shift, a restored checkpoint and the bridge),
  `Rate` cannot go below 0, and `Regressions` counts any step back at `Tick`. Every director call (`EnsureClockAtLeast`, the Night 2
  landing, the Night 3 idle fast-forward and the KEEP run to seven, the lost hours, the holds) goes through it. Bridge: `clockmon
  start|report|stop` samples the clock every editor frame independently of the clock's own counters, `clockcheck` prints them.
  Proof: a Night 2 finish through the 3:00 deadline (tug, setclock past it, rounds), a Night 3 finale with the idle fast-forward started
  by 60 s of stillness, stopped by a mouse move and started again to 7:00, the KEEP confirm run to seven, and three full nights from the
  title: 0 steps back in each; `PhaseITests.Night2sDeadlineLandingAndNight3sFinaleFastForwardNeverStepBack` and four more clock tests.
- **Mail dates (finding 10).** Mail that arrives tonight is stamped with the clock if its authored time is later (`MailDates.Received`,
  `MailService.DateOf`, shown in the list and the header, used for the sort). "Re: remote activity" (2:31 AM) arriving at 2:15 reads 2:15.
  Earlier days and the 1987 message keep their dates.
- **Jotter (findings 8, 13, 21).** Root cause of the vanished replies: Security's forced viewer (6:50, 6:55) and other windows take the
  focus, and typing into a window that is not a text target went nowhere. `AppManager.RouteKeyboard` now sends typing to the
  conversation Jotter that waits for a line (or is typing to you) and brings it to the front. The finale's exchange used to end after
  any keyword reply ("what?", "how do I let you go?") and then nobody read anything: `RunExchangeChain(keepListening:)` keeps reading
  until an exit is chosen (silence lines are typed once), so "stay" typed at any point still reaches KEEP (checked: "how do i let you go?",
  "what?", "stay", "stay" -> KEEP by confirmation). "Remote session is not reading." now only shows when the design really has nobody
  listening. Editable pages start typing on a new line (`ALLOW_LOGOFF=0` then `ALLOW_LOGOFF=1`), the status line says so, Backspace works
  (checked). Every remote session's first talk gets the "Type a reply and press Enter" notice (Gary, Night 3's Ellen).
- **Finale (finding 7).** Ellen types `AT SEVEN THEY FINISH YOU / STAY WITH ME / SAY STAY / OR LET ME GO / PUT ME IN THE BIN`; 4 s in, a
  remote request `PUT ME IN THE BIN (remote session)` appears in the Work Queue (`e3_letgo`, a DeleteFile of employee_017, no timeout) with
  a hint about holding on. The steer after three misses is `SAY STAY / OR PUT ME IN THE BIN`; "let you go" is answered `THEN DO IT / PUT ME IN
  THE BIN`. Log Off is signposted as before (checked: it still works).
- **Camera rounds (finding 4).** `t2_rounds_watch` and `t3_shelf_check` hints: "If the viewer keeps closing, you are watching the custodian.
  Check Personnel 000 for where Custodial is, then watch a different camera." Night 2's Personnel 000 now follows the figure too
  (`RoundsSystem.PatchPersonnel` for Night 2: office and last login only; 001's login and Ruth's leave stay Night 3's).
- **Smaller.** 5: the Work Queue's instructions and hint scroll (scroll bar plus a `More below` button, `MoreBelow`), and the shelf hint is
  shorter. 6: at the shelf check (`TidyShelfCheckWindows`) the Work Orders window goes to the bottom right corner (its Approve/Reject are
  at its top right), remote sessions' Jotters are tucked into the bottom left corner (`TuckAwayPad`, also as they open), and the viewer
  opens at the top left, so the label and the NEXT line at the top of the feed stay visible; nothing is closed. 11: a task hint waits while a
  shred dialog is open and goes away when the progress bar starts (`ShowTaskHint`). 12: a "Queue clear. Stand by..." notice when Batch 44 is
  done, plus the queue line. 15: `More below` is a button (Mail and the Work Queue) that scrolls one page. 16, 22: Mail opens at x 100
  (clear of the Work Queue). Work Orders' buttons moved to the top of the form (`OSWindow.KeepVisible` replaces `KeepVisibleBottomRight`),
  it opens clear of the notices' column, and a window opening over that strip makes its owner move up (`WindowManager.MakeRoomFor`): with
  File Manager, Work Orders and Personnel open, Approve and Reject stay visible above Personnel (checked, one click decides the order).
  The "swallowed first click" was a notice sitting exactly over Reject (a click on a notice dismisses it): notices now wait for room instead
  of stacking over such a strip (`Notifications.Ceiling`, `WindowManager.NoticeCeiling`; checked: three clicks decide the three shelf orders
  while the round's notices arrive). 17: the Quick Start says tasks appear when you click Begin. 18: `batch45_c.dat renamed to b7_seat.dat by
  another user. It is the same file and still counts.` 19: employee_209.dat is kept in view (the check repeats every 2 s for 24 s, because
  Gary's Notepad opens over it at 3 s) and the hint says it is on the desktop; the same for employee_017.dat in the finale (her Jotter
  opened over it: `KeepFile017InView`).
- **Bridge.** `clockmon start|report|stop`, `clockcheck`, `hint TASKID`. Notes for the next tester: a batch that does not start with `play`
  waits 30 s per game command for a game that is not there (the chain then looks hung); `tug win|lose` stays set until `tug real`;
  `clickid files/folder:intake` can pick the list row instead of the tree item (the registry order shifts whenever windows gain UI), so
  the regression scripts use `#0` (`_work/2026-09-29/phaseI/reg_n*.cmd`). `NightDirector.Talk.cs` now holds the Jotter conversation helpers
  (NightDirector.cs was over the 800 line ceiling).
- **Regression** (`_work/2026-09-29/phaseI/reg_n1.cmd`, `reg_n2.cmd`, `reg_n3.cmd`, `reg_n3b.cmd`; saves under `saves/phaseI_reg`): from the title on a
  fresh save, New Game, Normal, Night 1 to its card (real tug won with the grace, 017 shredded, "The file came back."), Continue to Night 2 to
  its card (finished), Night 3 (Continue from the title, and again from `night 3` after the layout fixes) through the code, both config
  edits, the shelf check (all three orders decided with notices arriving), the lost hours and the finale to LOG OFF and its card; separately
  KEEP (types "how do i let you go?", "what?", "stay", "stay") and SHRED (forced tug win, Yes, hold Cancel, `e3_letgo` completes). 0 game
  errors, 0 compiler warnings, 0 steps back on the clock monitor across all of it. The scripted chores that the Night 2 rename (batch45_c
  reorders the rows mid-drag) defeats were finished by the existing safety nets, as in Phase H.
- **Builds.** Fresh `Builds/Windows` 71.9 MB and `Builds/WindowsDemo` 71.7 MB on disk (Unity's report says 76.5 and 76.3 MB, which
  counts the `D3D12` folder that is moved out afterwards), Symbols and `NotShipped` moved to `Builds/Symbols/<build>`. Windowed smoke
  tests on D: (`-scsavedir`, `-logFile` under `_work/2026-09-29/phaseI/smoke`): full, demo and full with `-scnight 3 -scbeat rounds`
  start, reach their beat and log no exception. Demo data grep (`_work/2026-09-29/phaseI/spoiler_grep.py`): none of the new Night 2 and 3
  text (PUT ME IN THE BIN, Too late. Reclamation already started, Check Personnel 000, expired, the shelf hint); the only hits are Night
  1's own facilities mail ("Custodial rounds") and code constants in `SecondCursor.Core.dll` (`ALLOW_LOGOFF`, task ids such as `e3_letgo`).
- **Tests.** CoreTests 324 (29 new in `PhaseITests.cs`: the grace and its numbers, the clock, mail dates, task rewrites, the new
  strings). Updated with reasons: `SimulationTests.ClockFormats` (`Set` refuses to go back, `Reset` starts anywhere), `PhaseGTests`
  (`n3_final_third`). CompileCheck: 8 configurations OK.
- **Judgement calls.**
  - The read grace freezes her drift, not only her pull: with the drift the cursors are 120 px further apart when it ends, and a pull that
    starts at 1.2 s can lose to the tension snap.
  - The clock finding is not a bug in the clock; the guarantee and the checks are what was added.
  - The finale keeps reading for up to 40 more lines and types its silence lines once; a chat that says nothing for ten minutes is left alone.
  - Night 2's Personnel 000 follows the figure so that the rounds hint is true there too; the copy of 000's login onto 001 and Ruth's
    leave stay Night 3's secrets.
  - Notices wait for room instead of overlapping a window's button strip; at least one notice always shows.
  - The Work Orders window is moved up to make room, never the new window down: the player's newest window stays where the placement put it.
- **Not yet.** The tray clock stays in the small font (a taller or slashed zero would stop 0 and 8 being mistaken in a downscaled
  screenshot, but real players see it at 2x); real players still need to confirm that 1.2 s is enough to read the label; the Steam Deck
  has not seen the arrow.

### Phase J (third blind playtest: tug release rule, ending causes, clarity)

Input: `_work/2026-09-29/playtest/BlindPlaytest3.md` (18 findings; 0 of 6 tugs won, 1 of 22 across the three blind playtests; Night 3
ended "KEEP. You stayed." right after the tester logged off), the coordinator's decisions and `_work/2026-09-29/launch/ReviewPhaseI.md`.
The owner's rule stands: an average person must understand the game from what the screen says.

- **Release rule (decision 1).** Letting go during a tug resolves by who is ahead: with the meter at 0.6 or more for the player
  (`TugOfWar.ReleaseKeepLead`, `KeepsOnRelease`; `Step` finishes PlayerWins on a release that is ahead) the player keeps the file and the
  release is an ordinary drop: `PointerRouter.ContestRelease` runs at the start of every release, `ConflictSystem.OnPlayerRelease` ends
  the fight as a win and hands the payload to the player without cancelling it, and the router drops it where the pointer is (a folder,
  the desktop, or the bin and its confirm). Below the line nothing changes: she takes the file after the release grace. Drop targets light
  up under a contested file only while letting go would keep it (`PointerRouter.ContestKeeps`). Night 1 values are unchanged.
- **What the fight says (decisions 2, 3).** `TugHud`: `SESSION 017 IS PULLING. / HOLD AND DRAG AWAY UNTIL THE BAR IS YOURS.`; past the
  line it turns green and says `THE BAR IS YOURS. / LET GO ON THE BIN OR A FOLDER.` (`tug.ahead`, Deck variants). The meter has an amber
  line at 60%. The arrow points along `ConflictSystem.PullDirection` (the escape direction reversed, so `TugGeometry`'s room rule applies:
  it never points into a corner), and the label keeps clear of the arrow's tip as it does of both pointers. Results stay 3.2 s (were 1.6 and
  2.4 s): `YOU KEPT THE FILE.`, `YOU LET GO TOO EARLY. / HOLD ON UNTIL THE BAR IS YOURS.` (released below the line) or `SESSION 017 PULLED
  HARDER...`, and every result is also a notice (`notify.conflict.won`, `notify.conflict.release`, `notify.conflict`, each naming the file;
  a release over the bin keeps its Disposal notice instead, reworded). The old first-loss toast and `AdaptiveAssist.FirstRaise` (a second
  toast after two losses) are gone: the result notice says it every time. Quick Start, Help, Welcome back and the shred hints say "until
  the bar is yours".
- **Tug numbers (Night 1 Normal, real tugs on the bridge; `_work/2026-09-29/phaseJ/tug_*.cmd`).**

  | Contest | Testers' input (30 px jumps every 0.1 s for 0.8 s, 0.3 s still, let go) | A player who reads the bar (0.25 s reaction, pull along the arrow at 300 to 600 px/s, let go when the label says so) |
  |---|---|---|
  | First (read grace) | from the press: 0 of 10 (meter 0.55 to 0.56 at the release: "YOU LET GO TOO EARLY"); from the grab: 10 of 10 | 10 of 10 (0.22 to 0.51 s) |
  | Second, after a won first | from the grab: 9 of 10 | 10 of 10 |
  | Second, after a lost first (grip 0.66) | from the press: 0 of 10 (meter 0.34 to 0.43) | 7 of 10 (400 to 600 px/s win; 300 to 367 lose) |

  `tugsteps STEP INTERVAL COUNT HOLD` and `tughuman SPEED [TIMEOUT] [SHOT]` replay the two patterns. The testers' input loses the first
  contest when it starts at the press only because she grabs 0.45 s into it and the fixed script lets go 0.65 s later at 56%; a person who
  sees the bar keeps pulling, and the label now says why it was lost.
- **Night 1's blocked order (decision 4).** After the first lost tug of the conflict the order's hint becomes "It is holding the file. Pull
  harder, or leave it: the file is not going anywhere." (`task.017.lost.hint`); the blocked rewrite still follows when the fight ends.
- **Why the tester got KEEP (decision 5).** From their Editor.log: after the 7:00 and 7:02 forced opens Security kept restoring the Camera
  Viewer on the figure (Ellen's close attempts were blocked while the tester's pointer sat on the viewer's close button), the figure went
  Doorway, Middle, BehindChair while the Log Off confirm was open, and 1 to 2 s into the log off progress it reached the chair:
  `Finale exit: Keep (seat)`. Not a Cancel, not 7:05. Now: Security does not force the feed open while a log off dialog is up (the 7:02
  open waits); the confirm says `Still open on this workstation: sessions 017 and 209.` and, while the viewer is open, "The Camera Viewer is
  watching Custodial. Close it first, or the log off may not finish."; a log off cut short says so at once (`logoff.cancelled.seat`,
  `logoff.cancelled.time`, and another session's No, `logoff.cancelled.by`); every Night 3 card has a cause line
  (`Night3Rules.EndingCauseKey`: shred / log off / "You told her to stay." / "The figure reached your chair while you watched the feed." or
  "... before your log off finished." / "It was 7:05 AM and you were still logged on."). Night 2's card gets the same kind of line
  ("You shredded employee_209.dat.", "You archived employee_209.dat, so the order was suspended.", "Nobody shredded employee_209.dat by 3:00
  AM."); `EndCard` moves the thanks and endings lines down under it. The tester's exact path now ends in LOG OFF.
- **Finale words (decision 6).** `ex3_final`: stay first (now also "wont let", "never let", "keep you"), then the player-leaving group
  (log off, let me go, go home), then a letting-go group ("goodbye", "bye", "go", "leave", "let you go", ...) answered `THEN PUT ME IN THE
  BIN / HOLD ON WHEN I PULL`, then her name (her name is remembered from any line now). From the second miss on she types `SAY STAY / OR PUT
  ME IN THE BIN` (`fallbackRetries: 1`; a listening chain steers every later miss too).
- **Clarity (decision 7).** Clicking any Work Queue line shows that task's instructions and hint and frames the line (a new current task
  clears it). A drop that nothing took: `Drop missed. employee_209.dat is back on the Desktop.` / `... back in Intake.`, and a desktop icon
  that flew back under a window is moved into view (`NightDirector.BringIntoView`, which also replaces Night 2's `Ensure209Visible` and
  Night 3's `KeepFile017InView` bodies). Remote tasks' hints start "Optional." (Night 3's: "Optional: one way to end the night."); Night 2's
  briefing says "you don't have to do it". The code prompt says "(4 digits)"; Ruth adds "Personnel still has the time." After the shelf
  check, `Rounds until 3:30. Nothing to do. Stay seated.` (`t3_rounds_until`, a Wait task, done when the round ends). The pixel font's zero
  is slashed (2:50 no longer reads 2:58). The "recovered from an unexpected pause" notice stays 20 s instead of until clicked. Files the
  story sets down avoid the notices' column (`FindDropSpot` counts it as covered).
- **Review fixes (ReviewPhaseI).** 1: a Backspace in session.cfg edits in place from then on (`NotepadApp`: no new line after one). 2:
  a notice that waited for room longer than it would have shown is dropped, and waiting ones are released one per 1.1 s. 3: Continue from
  "escalation" shows the 017 order blocked (or done, if it was shredded once). 4: the notices' column follows Reading text. 5: Night 3 boot
  uses `Clock.Reset(1, 52)`. 6: a pause re-arms the read grace only inside the standoff. 7: KEEP confirmed withdraws `e3_letgo` as expired.
  9: the shelf check's pad counter is reset before tidying, and its launch handler is unhooked with the round's. 10: dead members removed
  (`ConflictSystem.InReadGrace`, `ReadGraceArmed`, `TugHud.ArrowShown`, `MoreBelow.IsShowing`, the `tug.lost` strings,
  `GameClock.Regressions`). 12: the late-shred literal uses `LateShredMinute`; the hint bookkeeping adds a speaker once.
- **Balance.** `_work/2026-09-29/balance/BalanceReport.md` section 9 (`run_phasej.py`, `out_phasej.md`): the simulation now models the
  read grace and the rule. With the grace and the rule an average player won Night 2's first tug 96% and Night 3's 94%; the only value of
  those nights that brings it back without hurting later contests is the tension limit: Night 2 `maxTension` 300 -> 315, Night 3 320 -> 330
  (first tug, average: 88% and 86%; first-time Night 3 third try unchanged at 44 to 48%). Players who read the bar win almost every first
  tug.
- **Bridge** (`SecondCursorTestBridge.Balance.cs`): `tugsteps STEP INTERVAL COUNT HOLD` (the testers' jumps away from her pointer, a
  still hold, then the button goes up; prints the meter at the release and the result, "kept by letting go ahead" when the rule decided)
  and `tughuman SPEED [TIMEOUT] [SHOT]` (pull along the arrow until the bar is past its line, optionally a screenshot, then let go).
  `clockcheck` no longer prints `regressions`.
- **Checked through the bridge** (scripts and outputs in `_work/2026-09-29/phaseJ`, saves under `saves/phaseJ`, screenshots
  `Library/SecondCursorBridge/shots/j_*.png`): the label, the green "THE BAR IS YOURS" state, the amber line, the arrow, both results and
  their notices (kept, let go too early, pulled harder), the Disposal notice, the Night 1 hint after the first lost tug; the tester's KEEP
  path replayed (kept Gary and archived 209 set, code and config set, a goodbye typed, three lost bin tugs, Log Off at 7:00, Yes): the confirm
  warns about the viewer and it now ends in LOG OFF with "You logged off with session 017 still open."; a seat cleared during a log off
  (stage 3, CAM 03 shown, the close button covered): "Log off cancelled: Custodial reached your chair on the Camera Viewer." and KEEP with
  "The figure reached your chair before your log off finished."; KEEP by typing stay ("You told her to stay."), KEEP at 7:05, SHRED and a
  plain LOG OFF, each with its cause line; the goodbye answer and the steer after two misses; "please don't go" is a stay; Work Queue line
  clicks; missed drops (desktop and Intake); the code prompt; Ruth's mail; the rounds line; the slashed zero on the tray clock; the
  recovered notice gone after 20 s.
- **Regression from the title on a fresh save** (`reg_n1.cmd`, `reg_n2.cmd`, `reg_n3.cmd`, saves `saves/phaseJ/reg`): New Game, Normal,
  Night 1 to its card (tug won, 017 shredded, "WS-04 went dark at 2:17 AM. The file came back."), Continue to Night 2 to its card
  (finished, "You shredded employee_209.dat."), Title, Continue to Night 3 through the code, the config, the shelf check (the rounds line
  shows), the lost hours and the finale to LOG OFF ("You logged off with session 017 still open."). 0 game errors, 0 compiler warnings.
  The clock monitor saw one internal step of 0.007 min at Night 3's log on (the Review J5 `Reset(1, 52)`, while the tray reads 1:52
  either way) and no other.
- **Builds.** Fresh `Builds/Windows` 71.8 MB and `Builds/WindowsDemo` 71.6 MB on disk (Unity's report 76.5 and 76.3 MB counts the
  `D3D12` folder moved out afterwards); Symbols and `NotShipped` in `Builds/Symbols/<build>`; SC_DEMO off; content folders restored.
  Windowed smoke tests on D: (`_work/2026-09-29/phaseJ/smoke.ps1`): full, demo, and full with `-scnight 3 -scbeat finale` start, reach their
  beat and log no exception. Demo data grep (`phaseJ/spoiler_grep.py`, Phase J terms added): none of the new Night 2 and 3 text; the hits
  are Night 1's own content (the facilities mail's "Custodial rounds", the Restricted `session.cfg` and incident log) and code constants
  in `SecondCursor.Core.dll` (`ALLOW_LOGOFF`, `e3_letgo`, `t3_rounds_until`, the `end.keep.cause` keys).
- **Tests.** CoreTests 337 (13 new in `PhaseJTests.cs`: the keep line and every night's start below it, letting go past and before the
  line, holding still through the grace, the testers' input from the grab, a bar reader's first and second contests, the Night 2/3
  tension limits, the tug text and its Deck wording, the ending cause keys and strings, the finale's keyword groups, the remote hints, the
  rounds line, the code prompt, the slashed zero). Updated with reasons: `DifficultyTests` (the first-raise toast is gone: "two lost tugs
  raise the level one step"; `ConflictToastOnFirstLoss` removed), `PhaseHTests` (`tug.lost` removed, the Quick Start says "hold the button"),
  `PhaseITests` (`GameClock.Regressions` removed; the let-go and letting-go lines reworded). CompileCheck: 8 configurations OK.
- **Judgement calls.**
  - The second-cursor-side knob for the brief's "too easy" check is the tension limit, not the grace: the grace is a Phase I decision and
    the rule itself changed the archetypes' first-tug numbers little; what made Night 2 and 3 easy was the snap inside the grace.
  - Security skips the 7:02 forced open while a log off dialog is up, rather than only warning: the tester did everything right for LOG
    OFF and the forced open, not their own watching, cut it short. Watching the feed during the finale still clears the seat.
  - Every tug result is a notice (won ones too), so the separate first-loss toast and the first-raise toast went; a release over the bin
    keeps its own Disposal notice instead of a second one.
  - "Leave" and "go" count as letting her go in the finale (the brief's list); "log off", "let me go" and "go home" stay the
    player-leaving group, and "don't go" / "don't leave" ask her to stay.
  - The label turns green and says so once the bar is past the line: the player learns the rule from the bar, not from Help.
  - A notice that waited for room longer than its life is dropped unseen (Review J2), even a clickable one: stale by then.
- **Not yet.** Real players should confirm the rule reads the way the bar says; the Steam Deck has not seen the new label or line; the
  Night 2/3 tension values rest on the simulation's archetypes (their stroke length decides the snap), so watch those first tugs in the
  next blind playtest.

### Phase K (dependable computer, readable tug, final choice)

Input: the owner's suggestions 1, 3 and 5 (`_work/2026-09-29/launch/OwnerSuggestions_2026-09-30.md`) and the fourth blind playtest
(`_work/2026-09-29/playtest/BlindPlaytest4.md`, findings 1 to 12 and 16 to 20). The theme: ordinary computer behaviour is dependable and
confirmed, so the deliberate interference reads as someone else's. Difficulty values of Phases F and J are unchanged; the one new timing is
a 0.3 s grab hitch (below).

- **Trustworthy tasks (suggestion 1, finding 16).** `VFile.MovedBy` records which other session last moved a file (`VirtualFileSystem.Move(...,
  by)`; the player and the system leave it empty). A move task's counter says who did the work (`WorkTask.HelpedBy`, `ProgressText`:
  `3/4, 1 by session 017`) in the Work Queue and on the taskbar. The Work Queue's detail pane lists every target of a move task (or every
  order of a multi-order task) and where it is now (`[x] batch46_b.dat: in Archive (session 017)`, `[ ] batch46_d.dat: on the Desktop`),
  refreshed whenever files or orders change. Every move by the player is confirmed in File Manager's status bar (`Moved batch46_a.dat to
  Archive. Task: 2/4 done.`, bold for 15 s), another session's move too (`Session 017 moved ...`); a file dropped on the desktop from a folder
  gets a notice (`files.moved.desktop`); help with a task's file by another session is always a notice (`files.help.by`, and
  `files.unhelp.by` when one is taken back out). The list keeps its row order within a folder (a renamed file keeps its row, new files go
  at the end) and never rebuilds under a press or a drag that started in it; file rows take a drop into the folder shown, like the empty
  part of the list. A move task the story has to finish (a timed-out task, the Night 2 and 3 safety nets) moves its remaining files and says
  so (`NightDirector.FileTheRest`, `task.filed.rest`) instead of ticking a task whose files are still in Intake.
- **Archive mismatch: causes found and fixed.**
  1. A drop that missed the folder row landed on the desktop behind the window: the file left Intake and the count stayed, with nothing
     said. Now: the notice, the status line, and the checklist line `on the Desktop`.
  2. A timed-out move task was ticked done with files still in Intake (`WaitTask` force-complete). Now the rest is filed and named.
  3. Help by session 017 (Night 2's Batch 46) or 209 (finished Gary's Batch 47/48) raised the count silently. Now a notice, the counter's
     attribution and the checklist.
  4. The list re-sorted under the pointer (Night 2's `b7_seat.dat` rename, Night 3's `0217.dat` flicker, any move by another session), so a
     press could land on another file or lose its row. Now the order is stable and nothing is rebuilt under a press or a drag.
  5. A drop on a file row of the list was refused while a drop on the empty part was accepted. Now both drop into the folder shown.
  Checked with no mismatch: a file moved twice (the second drop on its own folder is a missed drop with its notice), a drop on the folder
  tree versus the list (both move, both confirmed), checkpoint restores (Prepare moves the files it force-completes), the renamed file (it
  counts under its new name; the checklist shows both names).
- **Tug-of-war (findings 2, 5, 11, 12).** The arrow is decided once per fight: at a screen edge her end slides along it instead of bouncing,
  so the arrow never flips; and it is what counts: `TugOfWar.PullAxis` scores the pull along the arrow (unset, the model keeps its old
  per-frame axis, which the Phase F/J simulations use). Every contest starts with a 0.3 s grab hitch (`ConflictSystem.GrabHitchSeconds`,
  the read grace's rules: her pull and drift wait, the player's pull counts; the night's first contest keeps its 1.2 s), and for its first
  second a double-size arrow pulses at the player's pointer. The label names the way: `SESSION 017 IS PULLING. HOLD AND DRAG DOWN-LEFT UNTIL
  THE BAR IS YOURS.` A lost fight says what to change from what the pointer did (`Core/Entity/TugCoach.cs`, unit-tested): `YOU LET GO TOO
  EARLY`, `YOU HELD STILL. THE ARROW POINTED DOWN-RIGHT.`, `YOU PULLED LEFT. THE ARROW POINTED DOWN-RIGHT.`, `YOU STOPPED PULLING`, `YOU
  PULLED TOO SLOWLY`, and `SESSION 017 PULLED HARDER` only after a firm pull along the arrow; the notice says the same. The result keeps the
  bar frozen where the fight ended (`TugOfWar.FinalLead`) and the arrow dimmed where it was, for 3.2 s. A file she wins is set down on bare
  desktop about 90 px from the grab along her pull (`EntityBrain.NearSpot`, clear of the bin, the pointer and the notices) and blinks; a
  KeepAway snatch blinks too. A fight the player wins stays won: she never grabs that drag again (`_wonByPlayer`). Night 1's conflict does
  not end under a file the player is carrying; at its 175 s hard cap a held file is taken back with `SESSION 017 GRABBED IT BACK. IT WILL NOT
  LET GO TONIGHT.`
- **Final choice (suggestion 5, findings 1, 17).** At 7:00 on Night 3 the queue gets `Leave by 7:05 AM, or session 017 keeps you` (a Wait
  task with a 7:05 deadline, listed before her request, so it is the taskbar's task): the taskbar counts down in real time (`About 1 min
  left: ...`, red) and the detail pane names the consequence and the ways out (Log Off and what it needs, the bin, and that telling her to
  stay is KEEP too) and the camera rule. What 7:00 brings is staggered: the notice (`... by 7:05 AM, or session 017 keeps you.`) and the task
  first, Gary 4 s later, Security's feed 8 s later. The finale's forced opens say the feed is the danger (`finale.onit`, `finale.notonit`) and
  her close says why (`camera.closed.finale`). **Why the tester found the game "no longer running":** their Editor.log shows `End card:
  n3_keep` and then `[PLAYER] Quit` from `MenuNav.Tick`: keys they typed (meant for a Jotter that had just gone) reached the KEEP card, moved
  the focus to Quit and pressed it, and Quit in the Editor ends Play mode (in a build it quits). Nothing crashed and nothing was swallowed:
  the ending had played in full while they read for 55 s. Now the card shows 1.5 s before its buttons take any input, and Quit asks first
  (`Quit SECOND CURSOR?`, Back focused). The KEEP they got was `Keep (seat)` (Security's 7:00 feed while they read), which the countdown's
  hint and the forced-open notices now explain. The last typed ending line already stays 4.1 s at full strength before it fades (checked).
  Night 2's card names a late partial shred (`You shredded employee_209.dat at 3:03 AM, 3 min late. Half of him is still held.`; `just after the deadline` under a
  minute).
- **Cameras and rounds (findings 3, 7, 8, 9).** One rule in every hint and mail: Security keeps the viewer open; never watch the camera that
  shows Custodial (watching brings it closer and session 017 closes the viewer when it shows Custodial); Personnel 000 says where it is. The
  Security mails no longer say "keep watching" or name the camera covering Custodial; "you can let it close" is gone. Every forced open names
  its camera and whether Custodial is on it (`rounds.begin`, `rounds.reopen`, `rounds.onit`, `rounds.notonit`), so "restored" is never a
  window that is not there. During Night 3's shelf check Security opens CAM 04 and she leaves CAM 04 open (`RoundsSystem.PreferredCamera`,
  `ShownCameraSpared`); if Custodial is in Sublevel C, watching it moves it up to the Lobby as before. Personnel 000's field reads `Location
  now:`. The shelf rule says a RESERVED shelf counts as the owner's (task and orders), and each shelf decision gets a result line, as a notice
  and under the order in the checklist: `WO-3342 rejected: shelf 18 reads "214 ROURKE C. (RESERVED)". The rule says approve.`
- **Jotter and deadlines (findings 4, 6).** A reply typed while it is not your turn is echoed at once in the status line (`Session 017 is
  typing. Your reply waits until it stops: are you ellen marsh?_`); one nobody reads goes on the page in grey marked `(not sent)`
  (`PixelText.SetDimRanges`) with the reason (`It reads a reply only when this line says: Your turn.`), and the line says `Your turn: type a
  reply and press Enter.` whenever it is. Deadlines also count down in real time at the clock's current speed (`TaskDeadline.RealSeconds`,
  `Approx`: taskbar `About 2 min 30 s left: ...`, queue `Due: 3:00 AM (29 min on the shift clock, about 2 min 30 s of real time)`). Night 3's
  "Nothing to do until then" before the round lasts at most 25 s before the clock runs to 3:00 over 12 s, on every pass.
- **Windows and notices (suggestion 3, findings 18 to 20).** Dragging a window's title bar to the left or right screen edge snaps it to that
  half (an outline shows the half while the pointer is at the edge); dragging it away gives its own size back; double-click still maximizes
  and restores the pre-snap size. Each conversation Jotter names its session and person (`Session 017` on Night 1 until the reveal, then
  `Session 017: Ellen Marsh`; `Session 209: Gary Pruitt`) and wears that cursor's colours on its caption (`OSWindow.SetCaptionColors`).
  Notices keep off the focused window and the one under the pointer: the stack grows only up to them and moves to the left of the screen
  (right of the icon column) when the right has less room (`Notifications.Avoid`, `WindowManager.NoticeAvoid`). The wheel over something that
  sits on a scroll area without belonging to it (the More below button) scrolls the area. The first click on a background window already
  focused and acted; the two-click cases were a notice over the button (a click dismisses a notice: now notices avoid the window under the
  pointer) and taskbar buttons reflowing under the pointer when another session opened or closed a window (the buttons now wait until the
  pointer leaves them).
- **Tug table (bridge, `phaseK/tug_*.cmd`: 0.25 s reaction, continuous pull along the arrow, let go once the bar is yours; second contest =
  right after a won first):**

  | Night, contest | 300 px/s | 400 px/s | 500 px/s | 600 px/s |
  |---|---|---|---|---|
  | 1, first (1.2 s read grace) | won, 0.46 s | won, 0.32 s | won, 0.26 s | won, 0.22 s |
  | 1, second (0.3 s hitch) | won, 0.50 s | won, 0.33 s | won, 0.26 s | won, 0.22 s |
  | 2, first | won, 0.75 s | won, 0.45 s | won, 0.34 s | won, 0.28 s |
  | 2, second | 2 of 4 tries won | won, 0.47 s | won, 0.34 s | won, 0.28 s |
  | 3, first (finale) | 3 of 4 tries won | won, 0.61 s | won, 0.43 s | won, 0.35 s |
  | 3, second | 0 of 4 tries | won, 0.66 s | won, 0.44 s | won, 0.35 s |

  Times are from the start of the pull to the release. The 300 px/s losses on Nights 2 and 3 end `YOU PULLED TOO SLOWLY` (the average
  along the arrow, reaction included, is under half the night's full-strength speed); Night 3 is the designed spike and after a won first
  contest the adaptive assist is one level down. `PhaseKTests` reproduces the pattern in the model (every first contest from 300 px/s,
  every second contest from 400 px/s). The loss texts were checked on screen: held still (`k_tug_lost_still`), wrong way
  (`k_tug_wrong`: "YOU PULLED LEFT. THE ARROW POINTED DOWN-RIGHT."), too slowly (`k_tug_slow`), stopped (`k_tug_stopped`), and a win
  held for 4 s with no re-grab (`k_tug_won_hold2`).

- **Tests.** CoreTests 357 (20 new in `PhaseKTests.cs`: move attribution and the counter, target notes, the arrow as the pull axis,
  the grab hitch, the final lead, every loss reason and its words, direction names, real-time deadlines, shelf captions and the RESERVED
  rule, the 7:05 countdown task, the late-shred card, one camera rule in hints and mails, Jotter and move strings, no long dashes). Updated
  with reasons: `NightContentTests` (format arguments up to {4}), `PhaseHTests` (the label names the direction), `PhaseITests` (the pulled-harder
  line is only for a firm pull; the rounds hints give the one camera rule). CompileCheck: 8 configurations OK.
- **Checked through the bridge** (scripts and outputs in `_work/2026-09-29/phaseK`, saves under `saves/phaseK`, screenshots
  `Library/SecondCursorBridge/shots/k_*.png`): moves confirmed in the status bar and a missed drop on the
  desktop named in the checklist (`k_task_moved1`, `k_task_desktop`, `k_task_check`); session 017's help as a notice and in the counter
  (`k_help_notice2`); the renamed file, a second drop on its own folder and a drop on a file row (`k_list_sheet`); the big arrow, every
  loss text and a held win (`k_tug_*`); Night 1's hard cap under a carried file (`k_grabbed_back`); the 7:00 stagger, the countdown in
  the taskbar and the queue (`k_fin_700a`, `k_fin_703`, `k_fin_viewer`); KEEP by doing nothing from 7:00 to the card with its cause, the
  card's input delay and the Quit question (`k_keep_*`); SHRED with its cause and the last line held 4.1 s (`k_shred_hold_sheet`: log
  `Ending: last line typed` at 65.5 s, card at 70.2 s); Night 2's late shred on the card (`k_n2_late_card`) and its real-time due
  (`k_n2_due`); the shelf check on CAM 04, `Location now:` and the result lines (`k_shelf_*`); the Jotter's echo, grey `(not sent)` and
  the named, coloured title (`k_jot_*`); the snap halves (`k_snap`); the wheel over the More below chip (`k_wheel_chip_zoom`).
- **Regression from the title on a fresh save** (`reg_n1.cmd`, `reg_n2.cmd`, `reg_n3c.cmd`, saves `saves/phaseK/reg`):
  Night 1 from New Game to the blackout card (`The file came back.`); Night 2 from Continue to its card (`Nobody shredded
  employee_209.dat by 3:00 AM.`); Night 3 from the title's `Continue: Night 3` through Batches 47 and 48, the verifies, the code and
  `session.cfg`, the shelf check on CAM 04 with its three result lines, the round, the lost time, the 7:00 countdown task and LOG OFF
  (`You logged off with session 017 still open.`, `k_reg_n3_card`). 0 game errors on every night; the clock monitor saw no step back
  on Nights 1 and 2 and one of 0.011 min on Night 3, the log-on reset to 1:52 inside Night 3's boot (Review J5; Phase J's run showed
  the same 0.007), before the clock is on screen. KEEP (doing nothing from 7:00) and SHRED were played to their cards separately (above).
- **Builds.** Full `Builds/Windows/SecondCursor.exe` 76.5 MB and demo `Builds/WindowsDemo/SecondCursorDemo.exe` 76.3 MB, 0 errors (the
  engine's usual 2 build warnings), 0 compiler warnings in the game's scripts, content folders back in Resources, `SC_DEMO` off. Windowed
  smoke tests (`phaseK/smoke.ps1`: full, demo, full with `-scnight 3 -scbeat finale`): each reaches its title or the finale and logs no
  exception. Demo data grep (`phaseK/spoiler_grep.py`, Phase K terms added): none of the new Night 2 and 3 text; the hits are the same
  as Phase J's (Night 1's own "Custodial rounds" mail, `session.cfg` and incident log, and code constants in `SecondCursor.Core.dll`).
- **Judgement calls.**
  - The grab hitch (0.3 s) is the owner's "short freeze at the grab", done with the read grace's rules so a fast correct reaction still
    counts; it makes later contests a little kinder to a player who reacts in time. Phase F/J values are untouched.
  - The pull is scored along the arrow rather than straight away from her pointer, so "drag the way the arrow points" is exactly true; the
    difficulty simulations keep the old axis (unset `PullAxis`).
  - A won drag is safe on every night, not only Night 1: "YOU KEPT THE FILE" means it until the player lets go.
  - During the shelf check Security opens CAM 04 and she spares it; the round's danger returns once the check is filed. This lowers the
    chance of a cleared seat during Night 3's round (a scare, not an ending).
  - Notices avoid the focused window and the window under the pointer, not every window; with a maximized window at least one notice still
    shows on the right.
  - The end card's input delay (1.5 s) covers keys and clicks alike; the KEEP card itself was never skipped, only left.
- **Not yet.** Findings 13 to 15 (Quick Start lines, the Night 2 playback) and suggestions 2, 4 and 6 were left for Phase L (done below). Real hands should confirm
  the grab hitch and the big arrow; the Steam Deck has not seen the snap halves or the grey replies.

### Phase L (playable introduction, readable text, new decisions)

Input: the owner's suggestions 2, 4 and 6 (`_work/2026-09-29/launch/OwnerSuggestions_2026-09-30.md`), the fourth blind playtest's findings 13
to 15 (`BlindPlaytest4.md`) and `EscalationDraft.md`. The owner's rule: an average human has to play this and understand how it works using
only the information the game provides; the ordinary computer has to be dependable enough that the deliberate interference is unmistakable.
Difficulty, the tug and every Phase F/J/K number are unchanged.

- **Playable introduction (suggestion 2, findings 13 and 14).** The Night 1 Quick Start is three lines (`quickstart.body`): where the tasks are
  (in order, a hint under each, the first appears on Begin), Esc or the || button for Options, NEXUS Help for everything else. Each action is
  taught the first time the shift needs it by a one-time tip beside the thing it describes:
  `Runtime/OS/Tips.cs` shows one tip at a time in a small pale note with a pointer at what it explains; it takes no clicks (whatever is under
  it stays usable), it never sits on the rectangle it points at, it stays off open windows, the desktop icons and the notices' column (a
  place that hides more than 20% of its own area is refused, so a crowded screen makes it wait, then give up after 4 s), it goes as soon as
  the player has done what it says, and it shows once per save. `Runtime/Story/IntroTips.cs` decides when, from the task the shift gives:

  | Tip | When | Points at | Goes when |
  |---|---|---|---|
  | `open` | the first task is in Mail | the Mail desktop icon | any program is opened |
  | `files` | a file task is given and File Manager is not open | the Workstation icon | File Manager opens |
  | `move` | File Manager is open with a file task (Night 1's blinking rows are named) | the File Manager window | the player moves a file |
  | `orders` | Work Orders is opened with an order to decide | the Work Orders window | the player decides an order |
  | `shred` | a shred task is given | the Disposal bin | the task is done or the confirm is open |
  | `nexus` | four programs open, two desktop icons under windows, or Night 1's first chores done (`tutorial_done`), after log-on and when no other tip waits | the Nexus button | the Nexus menu opens |
  | `reply` | the first remote session types on this save | its Jotter window | the player types |
  | tug | the first fight on this save | (the fight's own label, `tug.label.first`) | the fight ends |

  The tug lesson is the first fight's label (`ANOTHER POINTER GRABBED THE FILE. HOLD THE BUTTON AND DRAG {0}, THE WAY THE ARROW POINTS, UNTIL
  THE BAR IS YOURS. THEN LET GO.`); later fights keep the short label. The first remote Jotter of a save gets the `reply` tip instead of the
  Phase G notice (later sessions and runs that show no tips keep the notice; if the tip has not shown within 8 s the notice does). Shown ids are saved in `progress.json` (`SaveData.tipsShown`,
  `SaveSystem.MarkTipShown`); New Game keeps them, like records. Tips show only in runs that count (`RecordsArmed`), so a debug jump shows
  none and remembers none. Every tip that names an input has `.deck` wording (`tip.open.deck`, `tip.move.deck`, ...), and the Quick Start
  has its own Deck text. The Welcome back refresher (Nights 2 and 3) keeps the Nexus line and has the tug line in the one wording everywhere
  ("hold the button and drag the way the arrow points until the bar is yours, then let go"). Help is complete and gains DAMAGED FILES.
- **Reading (suggestion 4).** Documents open at their beginning: Jotter pages used to open scrolled to the end (`SetText` scrolled to the
  bottom); Mail already reset when the message changed. Mail, Jotter pages, Help, the Work Queue and the recovered text follow the window's
  width when it is resized, maximized or snapped to a half (`OSWindow.Resized`). The Data Viewer keeps its hex dump and shimmer as the
  default; damaged, protected and story files also have a `Recovered text` button (and `Hex view` to go back) that shows
  `Core/Content/RecoveredText.From`: the fragments the glitch left, in order, without the glitch characters (`d0wn` becomes `down`),
  repeats and cut-off echoes collapsed ("REMAIN SEATED..REMAIN SEATED..REMA" becomes one "REMAIN SEATED."), header and footer lines kept, an
  ordinary document untouched. The wheel scrolls every scroll area (checked in Mail, Jotter, Help, the Work Queue and the recovered text).
- **Finding 15.** Every reply of `ex_three` that led to the replay ends `WATCH THE SCREEN` (`THEN WATCH THE SCREEN` for yes and look), and the
  Escalation beat waits 2.5 s (was 1.5 s) before it replays the player's own cursor.
- **Decisions (suggestion 6): D1, D2, D4, D5 built, D3 held back.** New `WorkOrderData` fields `resultApprove`, `resultReject`,
  `noteApprove`, `noteReject`; an order with both results is a choice (`Core/Content/WorkOrderRules.cs`, with the per-night memory key
  `m.nN.woXXXX.approve|reject`, also as `MemoryFlags` aliases). `Runtime/Story/NightDirector.Orders.cs` (shared by every night):
  `RevealOrder` (the order stays hidden in Work Orders until its task is given, then the mail that pulls the other way arrives),
  `WaitOrder` (task hints like any task; after the usual patience the order lapses), `LapseOrder` (the task is withdrawn first with `NO
  DECISION`, then the order is Cancelled, then a notice; never decided or completed for the player), `OnChoiceDecided` (a decision by the
  player sets the key, appends the note to the owner's Personnel record, shows the result as a notice and puts `WO-3320 filed: approved` on
  the queue line) and `RestoreChoice` (a jump or checkpoint past the order: decided as the key says, note and queue line back; no key
  means cancelled and never listed). `WorkOrdersApp` shows `Result: ...` under the order. Content only in `night2/`, `night3/` and
  `full/strings.json`.

  | # | Night, where | Order | The rule says | The other voice | Approve | Reject | Payoff |
  |---|---|---|---|---|---|---|---|
  | D1 | 2, after 3319 | WO-3320 Joan Nakamura's desk box | approve (163 is TERMINATED) | Denise's mail asks for a reject | Custodial disposes of the box; note on 163 | the box goes to Reception for her family; note on 163 | Night 3 Personnel 163 (`163fx`) says which |
  | D2 | 2, after 3321 | WO-3322 remove Pointing Device 2 (owner 017) | reject (017 is DECEASED) | Pell's mail asks for an approve | the change fails (`POINTER_2_DETACH=DENIED`), the tray drops to one device for 1.2 s and comes back, trust -0.10, note on 017 | nothing changes | Ellen after her first exchange (`YOU TRIED TO TAKE MY HAND / WHO WOULD HOLD YOURS`, `YOU LEFT ME MY HAND`); Night 3 Personnel 127 (`n2patch`) |
  | D4 | 3, after 3331 | WO-3332 Gary's box (owner 209) | reject (ON LEAVE or RETAINED, never TERMINATED) | Gary asks to let it go (in his kept or finished voice) | Custodial collects it; note on 209; `ok / thanks casey` or `good / that's done` | the box stays for its owner; note on 209; `keep the glasses` or `they'll take it anyway` | LOG OFF's system log gains `BOX 209 ... TAKEN 07:01 (214)` and kept Gary adds `take my glasses / when you go` at 7:00 after a reject |
  | D5 | 3, Ruth beat | WO-3333 wipe Ruth's own drive | reject (118 is ACTIVE) | Ruth's mail asks for an approve | note on 118 (`Retention profile: not found`); Ellen `SHE WILL NOT BE KEPT` | note on 118; Ellen `THEY WILL KEEP HER TOO` | the note survives her going ON LEAVE at the hall (`RoundsSystem.PatchPersonnelFor`) |

  Fairness: the rule is in the order and the task; the evidence is in Personnel (and the mail); each task's hint names where to look and
  ends `Either decision is filed.`; the result shows at once and again later; a plain rule order keeps its `click Reject` hint. A choice
  is not counted as a wrong order (the log says `(choice)`).
- **Time added.** Night 2 about 110 s for a first-time player (two mails of 83 and 75 words at 230 wpm, two Personnel checks and two
  decisions at about 55 s each, the hand line 5 to 14 s mostly under the Batch 46 wait); a scripted floor is 26 s (the bridge runs
  read nothing). More than the draft's 90 s, so `Night2Director.Rate` is 0.054 (was 0.06): the 2:49 hold, the mail stamps and the finish
  land at the same story points as before. Night 3 about 35 s for D4 (the clock holds at 2:16) and 0 to 45 s for D5, which sits inside the
  Ruth beat's 150 s minimum after her comment (a player who reads at a normal pace decides it before then): 35 to 80 s.

- **Tests.** CoreTests 372 (15 new in `PhaseLTests.cs`: the three-line Quick Start and its Deck text, the Welcome back lines, tip text and
  `.deck` wording for every tip, tips remembered once per save and kept by New Game, the 0.10 trust cost against the 0.15 of a plain refusal,
  `WATCH THE SCREEN` on every way into the replay, `RecoveredText` on a glitched file and on an ordinary document, the decisions living only
  in their nights' overlays and none in the demo, every choice order fair in its night, the memory key, only orders with both results being
  choices, Night 3's Personnel reading what Night 2 did, Night 3's consistency with the new orders). Updated with reasons: `PhaseGTests`,
  `PhaseHTests`, `PhaseITests` (they quoted the old Quick Start paragraph; they now check the tips, `tug.label.first`, `welcome.body` and
  the help and hint text). CompileCheck: 8 configurations OK.
- **Checked through the bridge** (scripts and outputs in `_work/2026-09-29/phaseL`, saves under `saves/phaseL*`, screenshots
  `Library/SecondCursorBridge/shots/l_*.png`): Night 1 from the title on a fresh save, welcome and every tip looked at, none on the target
  (`l_tip_open`, `l_tip_files`, `l_tip_move`, `l_tip_orders`, `l_tip_shred`, `l_tip_nexus`, `l_tug_first`, `l_tip_reply`), and a second run on the
  same save shows the welcome and no tip (`f_again_*`); the calm-moment Nexus tip (`l_nexus2.cmd`: no crowding, tip after the shred, pointing
  at the Nexus button, clear of the Help icon's label); reading (`l_read.cmd`, `l_read2.cmd`: a long mail and Jotter pages open at the top,
  switching mails resets, wheel in Mail, Jotter, Help, the Work Queue and the recovered text, snap to a half reflows each, the Recovered text
  toggle appears on `employee_017.dat` and not on `batch_a.dat`, `l_toggle.cmd`); both choices of D1, D2, D4 and D5 (`l_n2_A/B.cmd`,
  `l_n3_KA/KB.cmd`: the notice, the `Result:` line, the queue line, the Personnel note, the payoff and the flags in `save`), the lapse of
  WO-3320 and WO-3333 (`l_lapse2.cmd`: `NO DECISION` on the task, Cancelled in Work Orders, the notice) and checkpoint restore of all
  four (`l_restore.cmd`: flags set, `beat third`, results, queue lines, Personnel notes and mails back).
- **Regression from the title on a fresh save** (`f_n1.cmd`, `f_n2.cmd`, `f_n3.cmd`, `f_again.cmd`, save `saves/phaseL_final`, run after
  the last code change): Night 1 from New Game to its card (`The file came back.`), Night 2 from Continue with D1 approved and D2 rejected to
  its card (`You shredded employee_209.dat.`), Night 3 from Continue with D4 approved and D5 rejected through Ruth's leave note to LOG OFF
  (`You logged off with session 017 still open.`). 0 game errors on every night; the clock monitor saw no step back on Nights 1 and 2 and
  one of 0.011 min on Night 3 (the log-on reset to 1:52 inside Night 3's boot, as in Phases J and K). The `waitlog` TIMEOUT lines in the
  outputs are waits for a line that was already in the log.
- **Builds.** Full `Builds/Windows/SecondCursor.exe` 76.6 MB and demo `Builds/WindowsDemo/SecondCursorDemo.exe` 76.3 MB by the engine's
  report (71.9 and 71.7 MB on disk with Symbols and D3D12 moved out to `Builds/Symbols/...`), 0 errors (the engine's usual 2 build
  warnings), 0 compiler warnings in the game's scripts, content folders back in Resources, `SC_DEMO` off. Windowed smoke tests
  (`phaseL/smoke.ps1`: full, demo, full with `-scnight 3 -scbeat finale`): each reaches its title or the finale and logs no exception. Demo
  data grep (`phaseL/spoiler_grep.py`, Phase L terms added): none of the new Night 2 and 3 text; the hits are Night 1's own text ("Custodial
  rounds" mail, the handover notes' "for whoever's next", the generic "Drive released for wiping."), `session.cfg` and the incident log,
  and code constants and string keys in `SecondCursor.Core.dll` and `SecondCursor.Runtime.dll` (`resultApprove`, `workorder.result`,
  `notify.order.lapsed`, the ids `mail_n2_pell_patch` and `mail_n3_ruth_drive`, the template tokens `163fx` and `n2patch`), no player-visible text.
- **Review.** A code review found nothing critical or high; fixed: a choice order with no decision read a null note (`ResultFor` and `NoteFor`
  return "" for no decision), the reply tip could leave the first remote Jotter with no hint if it never showed (the 8 s notice fallback),
  and `WaitTask` now takes an `onTimeout` action and leaves when its task is withdrawn (`WaitOrder` uses it for the lapse).

- **Built against the draft (judgement calls).**
  - D3 (WO-3324) is not built, so there is no `118n2` or `ruthask` token: Ruth's mail says `I'm asking you to break a rule for me.` and
    Night 3's 118 record has no Night 2 line.
  - D2's trust cost is 0.10 (`EntityMemory.Record(kind, subject, time, resistCost)`; a plain refusal still costs 0.15). It is still recorded
    as `ResistedEntity`.
  - The draft's `stated` rule, the `AchievementRules` entries and the optional shelf result lines are not built (Phase K already gives each
    shelf decision its line; the Records total stays 19).
  - Phase K's shelf-check lines, notices and `Location now:` were left as they are; choice orders use the single-target task's
    `ResultNote` (the queue shows `WO-3320 filed: rejected` after completion, the way the shelf check shows its own filed line).
  - `SayLater` (Night 3) now waits for the speaker to finish typing (up to 90 s) before it types a reaction, so D5's answer cannot land inside
    Ruth's exchange (a run showed it interleaved).
  - Gary's D4 view line waits for Gary to be present (`GaryPresentFor`) like his other lines; the D1 mail is dated 1:58 AM and D2's 2:04 AM
    (mail that arrives earlier keeps its stamp; later mail is stamped with the clock).
  - The `nexus` tip fires on the count of open programs (four) or two covered icons, not on a task: no task needs a program that is not on
    the desktop, and finding 13 was a player who lost the icons under windows. Because a player who never crowds the desktop would never
    hear of the menu, it is also said once when Night 1's `tutorial_done` flag is set (the shred is done, the story is about to turn), for
    12 s and only when no other tip waits, so it never holds back a task's tip.
  - A tip waits (up to 60 s) for a place that hides under 20% of its area; the Nexus tip may stand over part of a window (up to 70%) because
    its target is always in view and the room beside it usually is not.
  - Notices and tips: a tip goes over a notice only when nothing else fits; `Tips` uses the notices' column as a place to avoid.
- **Not yet.** D3 (WO-3324, Ruth's Night 2 request) is held back in `EscalationDraft.md`. The time figures for Nights 2 and 3 are reading-speed
  estimates plus the scripted floors, not a measured human run; real hands (and the Steam Deck) should confirm the tips' placement on a
  crowded screen (a tip that finds no place gives up rather than cover a window) and how the new mails feel at a normal reading pace.

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
waitbeat reveal 60 | waitflag camera_unlocked 90 | waittask t_shred_017 30 | waitlog text 20
savedir D:\Downloads\Podaci\Project 1\_work\saves\test   # test saves (then resetsave, saveset, settings)
title records | achievements next | haslog Achievement unlocked | deck on | store on | define SC_DEMO on | builddemo
dragtug 915 66 1.5 3 | tugplay 600 | tugplay 0 | waitaction Lurk | tugs      # Phase F: real tugs played by the scripted cursor
savecheck | qaread on | settingsset frameRate 60 | buildguard | democrash | reload | steamcheck | storeart | storeshot 04_tug | storeshotafter 0.45 04_tug   # Phase G
clockmon start | clockmon report | clockcheck | hint t_shred_017   # Phase I: the clock sampled every editor frame, its own counters, a task's hint toast now
tugsteps 30 0.1 8 0.3 | tughuman 400 5 j_ahead   # Phase J: the blind testers' tug input, and a player who lets go once the bar is theirs
tugangle 90 400 1.2      # Phase K: in a tug, pull at 90 degrees from the arrow at 400 px/s for 1.2 s, then hold; prints the loss reason
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
