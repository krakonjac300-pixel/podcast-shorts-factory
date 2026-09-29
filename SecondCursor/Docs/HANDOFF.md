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
