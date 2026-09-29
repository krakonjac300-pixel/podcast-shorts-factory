# SECOND CURSOR: Launch readiness and first-time player audit

Date: 2026-09-29, read between about 17:00 and 17:45 CEST.
Scope: the Unity project at `D:\Downloads\Podaci\Project 1` (content, directors, UI code, project settings), the repo docs under `_work\2026-09-29\repo\SecondCursor\Docs`, `_work\2026-09-29\launch\MarketingPackFull.md`, and the two built players in `Builds\Windows` and `Builds\WindowsDemo`.

Method and limits:
- Read only. Nothing under `Assets`, `ProjectSettings`, `Library` or `Builds` was changed. Unity, the bridge and dotnet were not touched.
- The players were not launched: a launch opens a borderless fullscreen window that confines the real pointer while focused, and writes saves to `C:\Users\...\LocalLow`. The builds were inspected on disk instead (file lists, sizes, byte greps of the data files and DLLs).
- Another agent was editing the project during the audit (for example `Core\Entity\Difficulty.cs` changed at 17:40: `TaskForceAfterHint` went from 240 to 150 s). Timings below are the values on disk at the time of reading; line numbers can drift, so symbols are named.

---

## 0. Verdict

1. The onboarding pass fixed the Night 1 failure the first playtester hit (Quick Start, Task button, repeating hints, blinking guide, camera denial text). One gap remains on that exact step: the hint says "Open File Manager" but the desktop has no File Manager icon (the icon is "Workstation", and it opens the drive root, not Intake).
2. Nothing can hard-stall: every beat of every night has a timeout, a force-complete or a cap. The real risks are 30 s+ "what now?" moments and one place where the player does the right thing and the game silently ignores it (editing `session.cfg` in Jotter and closing without File > Save).
3. Night 3's LOG OFF exit is badly signposted: the Log Off item appears in the "Nexus" menu with no announcement, the 7:00 toast does not say where it is, and the "disabled" message (the only in-game pointer to `session.cfg`) arrives with at most 60 s left.
4. Store copy is mostly true. Needs change: the first-run length (beat math says about 48 to 63 min, not 60 to 75), rounds are listed under Night 3 but start on Night 2, "Ask about the last operator" has no keyword, "Around 2:00 AM" and "the locked folder" in the demo copy, and the 19 Steam achievements claim is only true after Steamworks is integrated.
5. Release blockers: the full-game build on disk is a stale 11:31 Night 1 prototype (no Nights 2 and 3, ships "Thank you for playing the prototype" and 65 Sentis shaders); Steamworks.NET and real App IDs are not in; the depot must exclude the `*_BackUpThisFolder_ButDontShipItWithYourGame` folders; plus all partner-site and legal steps (fee, tax, content survey with the Generative AI disclosure, capsule art, achievement icons).

---

## 1. First-time player walkthrough

### 1.0 When help arrives (Normal unless noted)

| What | Night 1 | Night 2 | Night 3 | Story (all nights) | Source |
|---|---|---|---|---|---|
| First task hint (toast; the hint is always visible in the Work Queue detail pane) | 30 s (briefing 25 s) | 40 s (briefing 30 s) | 45 s (briefing 30 s) | 15 s | `DifficultyProfile.TaskHintFirst`, `BriefingHintFirst` |
| Hint repeats | every 40 s | every 45 s | every 50 s | every 25 s | `TaskHintRepeat` |
| Company task force-completed | first hint + 150 s | same | same | first hint + 90 s | `TaskForceAfterHint`, `NightDirector.WaitTask` |
| Batch 44 (Night 1 anomaly beat) first hint | 60 s | | | | `Night1Director.Anomaly` |
| Entity task nudge / withdraw | | 35 s / 75 s | | 20 s / 75 s | `EntityTaskNudge`, task `timeout` |
| Jotter silence reply | 25 s per exchange | 25 s | 25 s | 25 s | `RunExchangeChain` |
| Restricted code hints (from reading Ruth's mail) | | | Ellen 60 s, kept Gary 120 s, Ellen 180 s; "Code format: HHMM" after 3 wrong codes | 30 / 60 / 60 s; format after 1 | `Night3Director.CodeHints` |
| Beat caps | conflict 150 s (hard 175), reveal 150 s | help 240 s, asks 300 s, finish 150 s deadline (175 hard), rounds 95 s | Ruth 150 to 300 s, round 360 s (cap 420), finale to 7:05 (safety 480 s) | same | directors |

### 1.1 First launch and the title menu

| Beat | What tells the player | Where | Verdict |
|---|---|---|---|
| Disclaimer | Fiction and photosensitivity text, then two buttons "Full effects" / "Reduce flashing" (first launch only) | full screen | Clear. The body says "reduce flashing at any time from the Esc menu": no screen is labelled "Esc menu" (the pause caption is SESSION PAUSED, the title button is Options, the taskbar button is `||`). Later launches show it for 6.5 s, skippable, with no visible prompt (`disclaimer.continue` "Click to continue" is never used). |
| Title | New Game, Options, Credits, Quit on a first launch (`TitleMenuModel.Items`); keyboard focus ring on New Game; "Headphones recommended." | title | Clear. |
| New Game | Normal / Story with a one-line description each, "You can change it later in Options." | title | Clear. |
| Night card, BIOS, splash, Log On | "NIGHT 1 / WED 11/18/98 / 1:52 AM", then a Log On dialog with one real button (Enter also works; Cancel says "You must log on to begin your shift.") | full screen | Clear. |

### 1.2 Night 1 (full game and demo)

| Beat | What tells the player what to do | Time to help | Risk |
|---|---|---|---|
| work: Quick Start | Modal "NEXUS OS Quick Start" with the five moves; "Begin" (auto-closes after 120 s). Work Queue already open top right. | n/a | OK |
| Read the briefing | Toast "Work Queue: Read the shift briefing", Task button blinks, toast "You have N new message(s)" (click opens Mail). Hint: "Double-click the Mail icon, then click the unread message to open it." | 25 s | OK |
| Archive ledger_1994.dat | Task + description + hint "Open File Manager, open Intake, then drag ledger_1994.dat onto the Archive folder." The yellow blink guide shows the file and the Archive row once File Manager is open. | 30 s | **30 s+ risk.** The desktop icons are Workstation, Mail, Work Queue, Work Orders, Personnel, Jotter, Camera Viewer, NEXUS Help, Disposal (`Desktop.cs`). "File Manager" exists only in the Nexus menu. Workstation opens the drive root "WS-04 (C:)", not Intake (`AppManager.Launch`, `AppIds.Workstation`). The Quick Start never says Workstation = File Manager; only Help does. This is the step the first playtester failed on. Fix 3. |
| Verify #3317 / #3318 | "Open Work Orders, note the owner's number, look it up in Personnel, then click Approve or Reject." Both are desktop icons; Personnel is a clickable list sorted by number. | 30 s | OK. A wrong decision gives no feedback (`workorder.wrong` exists but is unused): acceptable, the stakes are meant to be hidden. |
| Shred ~nxs0148.tmp | "Drag ~nxs0148.tmp onto the Disposal bin, then confirm. Or right-click it for options." Guide blinks the file and the Disposal row. | 30 s | OK |
| anomaly: Batch 44 | New task, IT mail, cursor flinch, 017 selects itself (File Manager opens itself after 45 s if closed), window drift, toast "Pointing device 2 connected." | 60 s | OK (a repeat of a learned move). |
| presence | The second cursor carries employee_017.dat onto the desktop; toast "New pointing device detected."; urgent mail; task "PRIORITY: Shred employee_017.dat" (hint names drag to the bin). | 30 s | OK |
| conflict | Tug-of-war. Toast "Session 017 is still open." when the first tug starts; after the first lost tug: "Input conflict: device 2 is holding the file. Drag firmly away from it to take it back." Ruth's mail at 45 s if the player never tries. | beat ends at 4 defenses or 150 s | OK. The race to No and "park on Cancel" are never explained; they read on screen and are optional. |
| communication | Ellen opens Jotter and types STOP / NOT THAT FILE / PLEASE; a caret blinks. | silence reply at 25 s | **30 s+ risk, and the core hook can be missed.** Nothing in the game says "type a reply and press Enter" (Quick Start and Help never mention typing). The silence replies ("YOUR CURSOR STOPPED / I KNOW YOU ARE THERE", "DONT GO QUIET...", "YOU ARE VERY STILL...") do not invite typing, so a hesitant player can sit through 3 x 25 s and never learn the mechanic the store page leads with. Fix 4. |
| escalation | Mimic replay, "Playback complete" toast, record lines, mail from yourself, "Permissions on Restricted changed by a remote session." (clickable), the camera opened for you on CAM 03. | player is passive | OK |
| reveal | Watch CAM 03 (the seated figure's arm follows your mouse). Door, figure, then Ellen fights to close the feed; reopening moves the figure closer; if you do not reopen within 12 s the feed opens itself. | 150 s cap | OK |
| ending | Blackout lines, then the card. Full game: NIGHT 1 with Continue to Night 2 / Title / Quit. Demo: WISHLIST NOW with Title / Quit. | | The demo's Wishlist button is hidden until `SteamBridge.StoreUrl` is set or Steam is running (`CanOpenStore`), so today "WISHLIST NOW" blinks with no way to act on it. |

### 1.3 Night 2

| Beat | What tells the player | Time to help | Risk |
|---|---|---|---|
| boot | BIOS "Pointing Device 3 ... NOT RESPONDING", toast "Pointing device 3 is not responding." | | OK. No Quick Start on Night 2: a player who continues days later gets only the Task button, hints and the Help icon (fix 18, NICE). |
| work | Briefing, Batch 45 (hint again "Open File Manager, open Intake..."), #3319, #3321, ~nxs0149.tmp (opens in Data Viewer with your own Night 1 lines). Rename anomaly while dragging Batch 45. | 40 s | OK, same File Manager wording as Night 1 (fix 3). |
| help | Batch 46; Ellen archives one or two files after your first move or 30 s, then talks (n2_help, exchange "YOU CAME BACK" with a memory of Night 1). | 40 s hints, 240 s cap | OK |
| asks (entity-authored tasks) | "YOUR TURN / DO SOMETHING FOR ME"; she types a row into the Work Queue (key taps you are not making), toast "Work Queue changed by a remote session." plus the title, the row in her inverted colours with "(remote session)". Detail pane: "Added by remote session 017." The hint field is empty (`night2\tasks.json`). Nudge at 35 s: File Manager opens on Documents with b7_door_log.txt selected; Personnel opens and she hovers row 163; employee_214.dat is selected and she hovers Archive. Withdrawn at 75 s ("Work item withdrawn by a remote session.", "YOUR CHOICE"). Ruth's mail 20 s after the first ask: "Don't do them." | 35 s nudge | **30 s+ risk, three times.** Before the nudge the player cannot tell whether the row is a real assignment or where its target is. "THE DOOR LOG" is the file b7_door_log.txt in Documents; "YOU" in PUT YOU IN ARCHIVE is employee_214.dat (the player's number is never shown on Night 2 except in Personnel). The choice is the point, but it should be an informed choice. Fix 5. |
| third | Toast "Pointing device 3 connected.", Gary's lowercase Jotter (two exchanges), Ellen closes his pad, Gary drops employee_209.dat on the desktop. | 25 s silence | OK |
| finish | Urgent mail, task "PRIORITY: Shred employee_209.dat" with "Due: 3:00 AM" (the clock visibly reaches 3:00 in 150 s). Ellen guards 209 near the bin, Gary begs, tries to drag, sits on No / Cancel for you and types "i've got no / click yes". At 40 s with no attempt: "PUT HIM IN ARCHIVE". | 40 s | OK. On Normal the tug explanation toast only appears when the assist first rises (about the second lost tug), because `ConflictToastOnFirstLoss` is false for Nights 2 and 3: a player who resumes Night 2 in a new session may not remember "pull away" (fix 9). |
| rounds | Security mail "Camera Viewer during rounds ... Keep it open and keep watching", viewer forced open at 0 s and 45 s ("Custodial rounds in progress: B-Level.", "Keep the viewer open during rounds."). Ellen closes it when it shows the figure and types "CLOSE IT / IT MOVES WHEN YOU WATCH", then "I SAID CLOSE IT". No task, no visible meter. | 95 s cap | **30 s+ "what do I do" by design**, which is fine for dread, but the one lever that matters on Night 3 (look at a camera that does not show the figure) is never shown on Night 2. Reaching the door makes Night 3's round start a stage closer (Lobby). Fix 6. |
| ending | "Session suspended by Custodial Services.", lines, card NIGHT 2 with "He is complete." / "He is still held." and Continue to Night 3. | | OK |

### 1.4 Night 3

| Beat | What tells the player | Time to help | Risk |
|---|---|---|---|
| boot / work | BIOS "Capture Profile 214 ... 96%"; briefing; Ellen arrives 20 s after log-on ("LAST NIGHT / FOR ONE OF US"); Batch 47 (hint: "Damaged files can still be archived."); #3330 (owner 214 = you, ACTIVE, reject); #3331 (175, ON LEAVE, reject); ~nxs0150.tmp (clipboard "0217", recent files /restricted/session.cfg). Kept Gary says hello; finished Gary archives a file for you. | 45 s | OK |
| ruth | 2:17: phone rings twice, Intake files flicker to 0217.dat, toast "Missed call: ext. 2118 (R. Hale)", Ruth's mail (the comment, "It's the minute she stopped.", session.cfg), Batch 48 task. Clicking Restricted opens "This folder requires authorization. Enter the Office of Retention code:"; a wrong code shakes, "Authorization failed. This attempt has been logged." | code hints 60 / 120 / 180 s after the mail is read | OK. The 30 s+ here is the puzzle itself, capped at 3 min by hints, and the clues are dense (mail time 2:17, the call at 2:17, 0217.dat, batch47_b "02:17", the temp clipboard). 0217, 217 and 2:17 all pass. |
| editing session.cfg | Ruth: "There's a file in there called session.cfg. It decides whether WS-04 lets you log off at seven. ... You can." The file opens in Jotter; the last line is ALLOW_LOGOFF=0. | none | **HIGH risk.** What works: Backspace, type 1, File > Save. What the UI tells: nothing. Clicking in the text does not move a caret and arrow keys do nothing (typing always appends at the end, `NotepadApp.OnTyped`); Save exists only in the File menu (no Ctrl+S, although `GameKey.Ctrl` exists); closing Jotter throws the edit away with no prompt (`OSWindow.RequestClose`). A player who "fixed" the file and closed it later gets "Log off is disabled" and will read it as a bug. Fix 1. camview.cfg (CAM 00) has the same issue but is a secret. |
| rounds | Security mail "Full round tonight: 3:00 to 3:30 ... shelf check of Sublevel C"; task "Shelf check: Sublevel C (3 orders)", Due 3:30 AM, hint "Open Work Orders and the Camera Viewer. On CAM 04 the shelf labels change every few seconds." Viewer forced open at once and every 22 to 30 s; Ellen closes it whenever it shows the figure. Kept Gary switches the camera himself 1 to 1.5 s after the first forced open and types "don't look at it / look where it isn't"; finished Gary says "watch"; Ellen types "LOOK AWAY / NOT AT IT" only at the second stage advance if Gary did not teach. | 360 s | Medium. The figure starts on CAM 04, so the first shelf reads feed the watch meter and Ellen closes the viewer on the player: intended tension, but only fair if Night 2 taught the camera switch (fix 6). Undecided orders are cancelled at the end, so nothing stalls. |
| lost | Mail "remain seated", glitch, 6:41, all windows closed, "NEXUS OS recovered from an unexpected pause. Duration: 3 h 10 min.", "YOU LOST TIME / THEY DO THAT". The Log Off item is added to the Nexus menu silently (`Flags.LogoffItem`). | | OK, but see Log Off below. |
| finale | employee_017.dat carried to the desktop; "AT SEVEN THEY FINISH YOU / STAY WITH ME / OR LET ME GO". The Work Queue says "Queue clear." 6:41 to 7:00 takes about 211 s (0.09 min/s), 7:00 to 7:05 takes 60 s (1/12 min/s). | | **SHRED** (drag 017 to the bin, win the tug, Yes, hold Cancel) and **KEEP** (type stay and confirm, or wait, or let the feed reach the seat) are well signposted by her lines. **LOG OFF is not:** before 7:00 the item says only "available at the end of your shift (7:00 AM)"; after 7:00 "Log off is disabled on WS-04. Policy: session.cfg (ALLOW_LOGOFF=0)" names the file but not its folder, with at most 60 s left; the 7:00 toast "Shift complete. You may log off." does not say where; the button reads "Nexus" (the unused `start.button` string says "Start") and Help never mentions the Nexus menu. Kept Gary unlocks it at 6:48 by himself; on the finished-Gary branch the player gets no help and Gary races them to No. Fix 2. An idle player waits about 4.5 min for KEEP (fix 7). |

### 1.5 Words the text uses that the UI does not show

| Text says | Where | The UI actually shows |
|---|---|---|
| "Open File Manager" | `tasks.json` t_archive_ledger, `night2\tasks.json` t2_archive_batch45 | Desktop icon "Workstation" (opens the drive root); "File Manager" only in the Nexus menu and window titles |
| "the Esc menu" | `disclaimer.body` | SESSION PAUSED / Options / `||` |
| "Start" (unused `start.button`) | none | "Nexus" |
| "OPEN THE DOOR LOG" | e2_door_log title | Documents\b7_door_log.txt |
| "PUT YOU IN ARCHIVE" | e2_hide_214 title | employee_214.dat (your employee number) |
| "log off" | Night 1 briefing, Ruth on Night 3, `logoff.available` | the Nexus menu item "Log Off CROURKE...", present only on Night 3 after 3:31 |

### 1.6 Required or important actions that are hidden, keyboard-only or unexplained

- Enter sends a Jotter reply: never stated in game (keyboard-only; the Deck keyboard covers Deck).
- Changing a config value needs Backspace at the end of the text; there is no mouse caret.
- File > Save: never mentioned; no Ctrl+S; no save prompt on close.
- Looking away during rounds (switch to a camera without the figure): taught only on Night 3, and only fully on the kept-Gary branch.
- Log Off lives in the Nexus menu and is not announced.
- Esc on the Restricted code prompt closes the prompt and also opens the pause menu (GameRoot routes Esc to the focused window first at execution order -500, then `PauseMenu.Update` pauses on the same key).
- Right-click is never required (drag and double-click cover everything); double-click is taught in the Quick Start and Help.

---

## 2. Store-claim check (MarketingPackFull section 2)

Verdicts: TRUE, NEEDS CHANGE (copy or game must change), BLOCKED (true only after a launch task), VERIFY (needs a timed playtest).

### 2.1 Short descriptions

| Claim | Verdict | Evidence |
|---|---|---|
| "Night shift, 1998." | TRUE | Dates 11/18 to 11/20/98 (night cards, mail). |
| "You archive and shred files ... until a second cursor starts fighting you for one." | TRUE | Night 1 tutorial tasks, then the 017 tug-of-war (`Night1Director.Conflict`). |
| "It types to you. It answers what you type." | TRUE | Keyword exchanges: 3 on Night 1, 3 on Night 2, 3 on Night 3 (`DialogueEngine`, `dialogue.json` and overlays). |
| "Three nights, three endings." | TRUE | SHRED, KEEP, LOG OFF (`Night3Director.EndingBeat`); Records shows "Endings seen: n of 3". |
| "It never touches your real files, webcam or mouse." | TRUE | File IO only in `SaveSystem.cs` (its own JSON in LocalLow); no WebCamTexture, Microphone or network code in `Scripts`. The real pointer is confined to the game window while playing (`GameRoot.LateUpdate`), never moved. |
| Alt B: "Pointing Device 2 wakes up and wants the file you are deleting." / "played entirely inside a fake 1998 computer" | TRUE | BIOS line, presence and conflict beats. |
| Alt C: "Safe to stream" | TRUE | As above; no Streamer Mode needed. |

### 2.2 Long description

| Claim | Verdict | Evidence / change |
|---|---|---|
| 1:52 AM, night operator at WS-04, archive / check against Personnel / shred, shift ends at 7:00 | TRUE | Content and clocks. |
| "You click, drag, double-click and type. That is all the control you get. It is also all the control the ghost has." | TRUE | The entity drives the same UI (`EntityController`, shared `CursorAgent`). |
| "A first run takes 60 to 75 minutes." | VERIFY, likely NEEDS CHANGE | Beat math from the directors: Night 1 about 13 to 17 min, Night 2 about 14 to 21 min, Night 3 21 to 25 min (HANDOFF phase D) = about 48 to 63 min. Time three fresh players; if the median is under 60 min write "about an hour" (MarketResearch 3.4 says $6.99 holds from 50 to 90 min, so price is unaffected). |
| "Each night adds a system instead of repeating the last one." | TRUE | Night 2 entity tasks and Gary; Night 3 code, config files, full round. |
| Night 1 bullet (the job, the second cursor, a camera you were told not to use) | TRUE | Camera denial on Night 1. |
| Night 2 bullet (ghost writes tasks, a third pointer wants something) | TRUE | `Night2Director.Asks`, Gary finish beat. |
| Night 3 bullet: "Custodial makes its rounds on the security cameras, and watching it makes it move." | NEEDS CHANGE | The first round and the watch mechanic start on Night 2 (`Night2Director.RoundsBeat`, `RoundsConfig.Night2`). Put "Custodial's first round on the cameras" under Night 2 and "a full round" under Night 3. The rest of the bullet (locked folder, editable config files, clock to 7:00) is TRUE. |
| Tug: "Yank away hard to win it back. Hold still and you lose it." | TRUE | `TugOfWarSettings` (pull speed, ramp). |
| "It races you to the No button on a confirm dialog." | TRUE | `RaceToNoDelay`. |
| "It goes for Cancel while a file is shredding. Park your cursor on Cancel and it has to find another way." | TRUE | `CancelCrawl`, `CancelPatience`, `IsBlockedByPlayer`. |
| "your cursor can block its cursor. Its cursor can block yours." | TRUE | `Guarding`, `IsBlockedByOthers`. |
| "Type back and press Enter. Swear at it. Refuse. Ask about the last operator. Tell it you are streaming." | NEEDS CHANGE (small) | Swear, refuse and stream (chat, twitch, stream, viewers) keywords exist. "Last operator" has no keyword: only "gary" and "pruitt" match, so "who was the last operator" gets the generic who reply. Add the keywords (fix 10) or change the copy. |
| "nothing is generated while you play" | TRUE | Fixed tables only. |
| "Some of what you type comes back on later nights." | TRUE | `{line1..3}` in ~nxs0149.tmp and employee_214.dat (`NightTemplates`, `SaveData.playerLines`). |
| Camera: "The second cursor opens it for you anyway. Camera 03 ... the seated figure's arm moves when your mouse moves." | TRUE | `Night1Director.Escalation`, `SeatedMimicsPlayer`. |
| "Three endings. None of them is a menu: you drag, you type, or you log off." | TRUE in spirit | LOG OFF is itself an in-fiction menu item; acceptable, optional rewording. |
| "What you did on earlier nights changes how hard each way out is." | TRUE | `Night3Rules.GripMultForTrust`, kept vs finished Gary on Log Off, Night 2 door moves Night 3's start stage. |
| "Nineteen Steam achievements, twelve of them hidden." | BLOCKED | TRUE in code (`AchievementIds`: 19, 12 hidden). On Steam only after Steamworks.NET, the App ID, the 19 entries with 38 icons and the TUG_WINS stat exist. |
| Privacy block ("does not read your files, use your webcam or microphone, or move your real mouse", Jotter saves to the pretend disk, no Streamer Mode) | TRUE | `NotepadApp.Save` writes `VirtualFileSystem` only. Do not add "no network" yet: Unity engine diagnostics are still on (section 3). |
| Key features (Mail, File Manager, Personnel, Work Orders, Disposal, System Monitor, Camera Viewer; CRT can be switched off) | TRUE | Nexus menu; Options "CRT effects". |
| "60 to 75 minutes for a first run, 100 to 130 minutes to see everything." | VERIFY | Night Select exists; three Night 3 replays plus the other Night 2 branch fit 100 to 130 min on paper. |
| "Try Night 1 free first. The demo is the whole first night, about 12 to 15 minutes." | TRUE for the build, BLOCKED for the store | `SC_DEMO` build is Night 1 only; the demo Steam app does not exist yet. |
| Content notes (implied death, no gore, tall figure, sudden low sounds, no voice, no profanity written by the game, English only) | TRUE | Content read in full for all three nights. |
| Photosensitivity paragraph | TRUE | `VisualFx.ReduceFlashing`, CRT toggle. Now also true: "choose Reduce flashing on the very first screen" (disclaimer buttons). SteamChecklist 5.2 wants this line at the top of About This Game; the copy has it at the bottom. |

### 2.3 Demo page

| Claim | Verdict | Evidence / change |
|---|---|---|
| "About 12 to 15 minutes, start to blackout." / "It starts at 1:52 AM." | TRUE (verify timing) | Night card 1:52 AM; beat math about 13 to 17 min. |
| "Around 2:00 AM, a second cursor walks onto your screen with a file of its own." | NEEDS CHANGE | The Night 1 clock runs 5 game minutes per real minute from 1:52 with no hold (`NightDirector.ClockRate` 1/12); the tutorial takes 5 to 8 real minutes, so the arrival lands around 2:20 to 2:30 (the urgent mail is dated 2:17). Say "A little after two". |
| "The whole job, from log-on to the last work order." | TRUE | |
| Second cursor abilities, the Jotter, Camera 03 | TRUE | |
| "Try Shut Down." / "Open the System Monitor and end the process that should not be there." | TRUE | Shut Down refused ("Open sessions on this workstation: 2"); End Process refuses twice, then "not running". |
| "When the camera opens, move your mouse." | TRUE | |
| Safety line | TRUE | |
| "What the demo leaves out: Nights 2 and 3, the third pointer, the locked folder and all three endings." | NEEDS CHANGE | Night 1 has a locked Restricted folder that the ghost unlocks. Say "the Restricted code". |
| "A Reduce flashing option is in the Esc menu." | TRUE | Also on the first screen now. |
| "When the screen goes black, a card says WISHLIST NOW." | TRUE, button BLOCKED | `DemoCard` shows the text; the Wishlist button needs `SteamBridge.StoreUrl` or Steam (`CanOpenStore`). |

Also checked against the build: the demo contains none of the Night 2 or 3 overlay text (no "YOU CAME BACK", "i had your chair", batch47, SHELF 18, "remote session 017", "Resuming session 214"). But the base `strings.json` ships in it with Night 2 and 3 UI strings and every achievement description, including hidden ones ("Let Gary finish.", "Keep Gary on WS-04.", "Find CAM 00.", "Custodial rounds in progress", "Log Off CROURKE...", "Code format: HHMM", "He is complete."), found as plain text in `SecondCursorDemo_Data\resources.assets`. The Night 2 and 3 code is also in the demo's `SecondCursor.Runtime.dll` (UTF-16 literals such as "Gary enabled log off", "Finale exit"). Datamining spoilers only; fix 15.

Trailer copy (section 3.1) note: "The taskbar clock rolls from 3:31 AM to 6:41 AM" is not implemented yet (`Night3Director.Lost` uses an instant `Clock.Set(6, 41)`; M15 is pending). Everything else quoted in the trailer table exists in content.

---

## 3. Steam release blockers still open

Status of SteamChecklist section 0 on disk now: packages trimmed (manifest has no AI, Visual Scripting, Timeline or Collab), release defines cleared, only the game scene in Build Settings, D3D11 only, single instance on, splash and logo off, per-size icons generated, save split and atomic, Esc menu and first-screen flashing choice, display and frame-rate options, cursor confinement, prototype end card only in the demo, Credits screen. What is still open:

### 3.1 In-engine (code, settings, builds)

| # | Item | Level | Evidence |
|---|---|---|---|
| E1 | Rebuild the full game. `Builds\Windows` is from 11:31 to 11:33, before Nights 2 and 3, the title menu and achievements: its `resources.assets` is 9.7 MB with 65 `Hidden/Sentis` shader matches, "Thank you for playing the prototype" and no Night 2 text. Use SECOND CURSOR > Build Windows (Steam) once the current engineering work lands. | BLOCKER | build timestamps and byte greps |
| E2 | Steamworks.NET: package, asmdef reference and `STEAMWORKS_NET` define, real `FullGameAppId` and `DemoAppId` (both 480 today), `StoreUrl` (empty), `credits.steamworks` MIT text, `steam_appid.txt` for the Editor only (HANDOFF "How to add Steamworks.NET later"). | BLOCKER | `Game\SteamBridge.cs` |
| E3 | Depot must exclude `SecondCursor_BackUpThisFolder_ButDontShipItWithYourGame` and `SecondCursorDemo_BackUpThisFolder_ButDontShipItWithYourGame` (both builds contain one, `lib_burst_generated.txt`), `*.pdb`, `steam_appid.txt`. Use the SteamChecklist 6.6 robocopy line and the `FileExclusion` rows in the depot VDF. | BLOCKER | build folders |
| E4 | Version: `bundleVersion` is 0.9.0. Set 1.0.0 by hand before the release build (the release script only rewrites "", 0.1, 0.1.0 and 1.0 to 0.9.0; 1.0.0 is left alone). | SHOULD | `ProjectSettings.asset` |
| E5 | Unity engine diagnostics still on: `UnityConnectSettings.asset` `m_EngineDiagnosticsEnabled: 1`, `ProjectSettings.asset` `submitAnalytics: 1`. Turn off (license allowing) before claiming "no network" anywhere, or state it in a privacy note. | SHOULD | settings files |
| E6 | Demo Wishlist button needs `StoreUrl` or Steam; otherwise the WISHLIST NOW card has only Title and Quit. | SHOULD | `EndCard.ButtonsFor`, `SteamBridge.CanOpenStore` |
| E7 | Demo spoiler strings (section 2.3). | SHOULD | demo `resources.assets` |
| E8 | Steam Deck: Large reading text covers only Jotter and Mail; hints live in toasts and the Work Queue, which stay at 1x (lowercase about 8 px at 1280x800, Valve minimum 9 px). Hardware tests still open: floating keyboard (Enter in single line, Backspace, numeric code), overlay pause, suspend during a tug, trackpad pull speed (`GameRoot.DeckPullSpeedScale` is still 1), PC to Deck cloud round trip. | SHOULD (for Verified) | HANDOFF phase E, SteamChecklist 3 |
| E9 | Still in both builds: the `D3D12` folder (4.5 MB) and DirectStorage DLLs (1.7 MB) although D3D11 is the only API; unused built-in modules (Terrain, Vehicles, Cloth, XR, Video, Unity Analytics, Adaptive Performance) in `Packages\manifest.json`; template leftovers `Assets\Readme.asset`, `Assets\TutorialInfo`, `Assets\Scenes` (not in the build list). | NICE | build folders, manifest |
| E10 | Icons are box-filtered from the 256 px render; hand-pixelled 16, 32 and 48 px versions would read better in the taskbar and library. | NICE | `Assets\SecondCursor\Art` |

### 3.2 Steamworks partner site

| # | Item | Level |
|---|---|---|
| S1 | App ID for the full game and a separate demo app; one Windows 64-bit depot each; launch options `SecondCursor.exe` and `SecondCursorDemo.exe`. | BLOCKER |
| S2 | Store page: short and long description (with the section 2 corrections), 20 tags, system requirements (Windows 10 21H1 64-bit, DX11), English only, "Coming soon" / "Spring 2027". Page must be Coming Soon at least 2 weeks before release; review takes 3 to 5 business days. | BLOCKER |
| S3 | Content survey (General, Mature Content, Generative AI disclosure). It generates the regional age ratings; there is no separate questionnaire to file. The Generative AI section must be answered honestly for code, text and art made with AI tools. | BLOCKER |
| S4 | Store and library assets: header 920x430, small 462x174, main 1232x706, vertical 748x896, at least 5 screenshots at 1920x1080, library capsule 600x900, library header, hero 3840x1240, logo; community icon 184x184 and client icon; trailer strongly recommended. | BLOCKER |
| S5 | 19 achievements (API names in SteamChecklist 4) with achieved and unachieved icons, the `TUG_WINS` stat as the progress stat of `ACH_WHITE_KNUCKLES`, then Publish. Required if the page claims Steam Achievements. | BLOCKER (for the claim) |
| S6 | Auto-Cloud: root WinAppDataLocalLow, subdirectory `SecondCursorGame/SECOND CURSOR`, pattern `progress.json` only; test with `testappcloudpaths`. | SHOULD |
| S7 | Steam Input default configuration for Deck (SteamChecklist 3, D1 as revised in phase E), then request the Deck compatibility review. | SHOULD |
| S8 | Pricing $6.99 with Valve's 2026 regional matrix and a 15% launch discount (MarketResearch 3.4 to 3.6). | SHOULD |
| S9 | Accessibility wizard: claim only what is true (mouse-only is now true thanks to `||` and the silence fallback; not "Adjustable Text Size", not "Save Anytime"). | NICE |

### 3.3 The user (legal, business, art)

| # | Item | Level |
|---|---|---|
| U1 | Steam Direct fee (US$100 per app), Steamworks distribution agreement, bank details, tax interview, identity verification. The 30-day wait before release starts at payment. | BLOCKER |
| U2 | Capsule art, screenshots and trailer production (or commission). Run a flash analysis (PEAT) on the trailer's static bursts and the blackout flash. | BLOCKER (art), SHOULD (PEAT) |
| U3 | Decide the Generative AI disclosure wording (S3) and whether Credits should name the developer or studio (`credits.body` names nobody today; the store page needs developer and publisher names anyway). | BLOCKER (disclosure), SHOULD (credits) |
| U4 | Build account with Steam Guard for SteamPipe uploads (Valve holds new security changes for 3 days before builds can go live). | SHOULD |
| U5 | Name and trademark search for "SECOND CURSOR". | NICE |
| U6 | Time three fresh playtesters on the full 3-night build before locking the length line and the price (MarketResearch 3.4 decision rules). | SHOULD |

---

## 4. Prioritized fix list for the next engineering phase

Preference: text, hint and timing changes that make the game clearer without making it easier. P0 = can make a first-time player believe the game is broken or miss its core hook; P1 = 30 s+ confusion or a store-claim fix; P2 = polish.

| # | Pri | File(s) | Change | Why |
|---|---|---|---|---|
| 1 | P0 | `Scripts\Runtime\Apps\NotepadApp.cs`, `OS\OSWindow.cs`, `strings.json` | For files tagged `editable`: put `*` in the title while unsaved; on close with unsaved changes ask "Save changes to session.cfg?" (Yes / No); Ctrl+S saves (`GameKey.Ctrl` exists); a one-line status strip under the text: "Text is added at the end. File > Save to apply." | The only Night 3 action where doing the right thing can be silently discarded (no caret placement, Save only in the File menu, close discards). |
| 2 | P0 | `strings.json`, `Story\Night3Director.Finale.cs` (`RequestLogOff`, the 7:00 toast), `Night3Director.Rounds.cs` (`Lost`) | New key `logoff.early.disabled`: "Log off is available at the end of your shift (7:00 AM).\nPolicy: Restricted\\session.cfg (ALLOW_LOGOFF=0)", used before 7:00 when log off is not enabled; `logoff.disabled` adds `Restricted\\`; `logoff.available`: "Shift complete. Log off from the Nexus menu." and a click on the toast opens the Nexus menu; one toast when `LogoffItem` is set: "Log Off CROURKE... added to the Nexus menu." | Today the player learns where the policy lives only after 7:00, with at most 60 s left, and is never told where Log Off is. Same difficulty, far clearer. |
| 3 | P0 | `tasks.json` (t_archive_ledger), `night2\tasks.json` (t2_archive_batch45), `strings.json` (`quickstart.body`, `.deck`, `help.body`) | Hint: "Double-click Workstation (File Manager), click Intake on the left, then drag ledger_1994.dat onto the Archive folder." Add to the Quick Start: "- Workstation opens File Manager: your folders and files." | The hint names an icon the desktop does not have; this is the step the first playtester failed on. |
| 4 | P0 | `Story\Night1Director.cs` (`Communication`), `strings.json`, `dialogue.json` (ex_stop) | One NEXUS toast after her first three lines: new key `notify.jotter.reply` "Jotter: a remote session is typing. Type a reply and press Enter." (Deck variant). ex_stop silence becomes ["TYPE SOMETHING", "I KNOW YOU ARE THERE"]. | Typing to the ghost is the store's lead hook and is never explained in game. |
| 5 | P1 | `night2\tasks.json` (e2_door_log, e2_lookup_163, e2_hide_214, e2_archive_209) | Description: "Added by remote session 017.\nNot assigned by Night Operations.\nTarget: Documents\\b7_door_log.txt" (Personnel, employee 163; Intake\\employee_214.dat to Archive; employee_209.dat to Archive). | The player can make the choice only if they know what and where the target is and that it is not company work. The 35 s nudge stays. |
| 6 | P1 | `night2\dialogue.json` (n2_rounds_again), optionally `Night2Director.Gary.cs` (`RoundsBeat`) | n2_rounds_again: ["I SAID CLOSE IT", "OR LOOK AT ANOTHER CAMERA"]; if the player never reopens, type the second line on the second forced open. | Night 3's full round rewards switching cameras, but Night 2 never shows it; on the finished-Gary branch nobody teaches it. |
| 7 | P1 | `Story\Night3Director.Finale.cs` | Idle fast-forward: with no pointer or key input for 45 s and no exit running, run the clock to 7:00 over about 40 s (`EnsureClockAtLeast`), then the normal 7:00 to 7:05 minute. | An idle finale is about 4.5 min of waiting before KEEP (HANDOFF lists it as not yet done). Refund risk on a 1-hour game. |
| 8 | P1 | `Story\EndCard.cs` | On `FinalCard`, add `select.endings` ("Endings seen: {0} of 3") under the thanks line. | MarketResearch 6.3.4: make endings visible to invite a Night Select replay. |
| 9 | P1 | `Core\Entity\Difficulty.cs` (Night2, Night3) | `ConflictToastOnFirstLoss = true` for every night (text only, the fight is unchanged). | A player resuming Night 2 or 3 in a new session is not reminded to "drag firmly away" until the assist rises. |
| 10 | P1 | `dialogue.json` (ex_stop, ex_two, ex_three gary groups) | Add "last operator", "previous operator", "old operator", "before me", "last guy" to the `gary, pruitt` keyword groups. | Makes the store line "Ask about the last operator" true. |
| 11 | P1 | `strings.json` (`help.body`, `help.body.deck`) | Add: "NEXUS MENU: the Nexus button (bottom left) lists every program, Help, Shut Down and, at the end of a shift, Log Off." / "CAMERAS: click a camera on the left of Camera Viewer to switch." / "SAVING: some files can be saved from Jotter (File > Save)." | Help covers only Night 1 moves; Nights 2 and 3 add three new ones. |
| 12 | P1 | `strings.json` (`disclaimer.body`) | "...reduce flashing at any time in Options (Esc, or the || button on the taskbar)." | "Esc menu" is not a label the UI shows. |
| 13 | P2 | `Game\PauseMenu.cs` | Quit asks first, like Quit to Title ("Quit the game? You continue from the last checkpoint."). | One stray click ends the session. |
| 14 | P2 | `Game\PauseMenu.cs` or `Apps\AuthPromptApp.cs` | Do not open the pause menu on an Esc that a focused window consumed (or stop closing the prompt on Esc). | Esc on the code prompt both closes it and pauses. |
| 15 | P2 | `strings.json`, `Editor\SecondCursorBuild.cs` (`DemoExcludedContent`) | Move Night 2 and 3 only keys (rounds.*, logoff.*, auth.*, notify.pointer3*, notify.queue.*, end.n2.*, the ach.* names and descriptions of hidden achievements) into a content folder the demo build excludes. | Datamined spoilers in the free demo. |
| 16 | P2 | `OS\Notifications.cs`, `Apps\WorkApps.cs` (`WorkQueueApp`) | Follow `DisplaySettings.ReadingScale` (Large on Deck) for toasts and the Work Queue detail pane. | Hints are 8 px lowercase at 1280x800; Deck Verified needs 9 px. |
| 17 | P2 | `tasks.json`, `night2\tasks.json`, `night3\tasks.json` | Add `hintDeck` to every hint that says "drag" (ledger, Batch 44, 017, Batch 45, 46, 47, 48, the temp files, 209). | Deck wording consistency with the Quick Start ("Hold R2 ... move"). |
| 18 | P2 | `Story\Night2Director.cs`, `Night3Director.cs` (`Work`), `strings.json` | When a night starts from Continue or Night Select in a fresh app session, show a short "Welcome back" version of the Quick Start. | A returning player on Night 2 or 3 gets no refresher. |
| 19 | P2 | `Story\Night3Director.Rounds.cs` (`Lost`) | Roll the clock from 3:31 to 6:41 over 1.2 s with soft ticks; delay the "Duration" line 0.6 s (MarketingPackFull M15). | The trailer copy says the clock "rolls"; an instant set reads as a bug in clips. |
| 20 | P2 | `Story\BootSequence.cs` | On later launches show `disclaimer.continue` ("Click to continue") under the disclaimer. | The key exists but is unused; the 6.5 s screen has no visible way on. |

---

## 5. Evidence notes

- Docs read: `Docs\HANDOFF.md` (all sections through phase E), `Docs\Launch\SteamChecklist.md`, `Docs\Launch\MarketResearch.md` (sections 0, 3, 6), `_work\2026-09-29\launch\MarketingPackFull.md`, `ReviewPhaseD.md` (all items reported fixed in HANDOFF).
- Content read in full: base `strings.json`, `tasks.json`, `emails.json`, `workorders.json`, `story.json`, the dialogue summary; `night2\*` and `night3\*` strings, tasks, emails, work orders, story, dialogue, file system and Night 3 employees.
- Code read: `NightDirector.cs`, `Night1Director.cs`, `Night2Director.cs`, `Night2Director.Gary.cs`, `Night3Director.cs`, `.Ruth.cs`, `.Rounds.cs`, `.Finale.cs`, `.Gary.cs`, `NightSetup.cs`, `RoundsSystem.cs`, `CustodialRounds.cs`, `Night3Rules.cs`, `Difficulty.cs`, `WorkTaskManager.cs`, `ContentOverlay.cs`, `BootSequence.cs`, `TitleMenu.cs`, `TitleMenuModel.cs`, `EndCard.cs`, `PauseMenu.cs`, `Taskbar.cs`, `StartMenu.cs`, `WorkApps.cs`, `FilesApp.cs`, `AuthPromptApp.cs`, `NotepadApp.cs`, `CameraApp.cs`, `StaffApp.cs`, `AppManager.cs`, `SteamBridge.cs`, `SecondCursorBuild.cs`, `ProjectSettings.asset`, `UnityConnectSettings.asset`, `EditorBuildSettings.asset`, `Packages\manifest.json`.
- Builds: `Builds\Windows` 111 files, 89.3 MB (11:31 to 11:33); `Builds\WindowsDemo` 108 files, 76.4 MB (16:51), `resources.assets` 81.6 KB, product "SECOND CURSOR Demo", `single-instance` in `boot.config`. Both contain a `*_BackUpThisFolder_ButDontShipItWithYourGame` folder and a `D3D12` folder.
