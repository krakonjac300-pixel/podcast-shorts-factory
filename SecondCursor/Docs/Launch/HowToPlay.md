# SECOND CURSOR: How to play (Steam launch copy)

Dated 2026-09-29. Player-facing help for the store page, the demo page, a Steam Community Guide and a FAQ. Every block is Steam BBCode inside a `text` fence: paste the inside of the fence as is.

Checked against: `Resources/Content/strings.json` (base, `full/`, `night2/`, `night3/`, and the `.deck` variants), `tasks.json` and the night overlays (hints and `hintDeck`), `workorders.json`, `story.json` (camera labels), and the UI code that builds labels not in the JSON (`Taskbar.cs`, `StartMenu.cs`, `PauseMenu.cs`, `BootSequence.cs`, `WorkApps.cs`, `CameraApp.cs`, `NotepadApp.cs`). Store copy: `MarketingPackFull.md` section 2. Controls, options, Story mode, Steam Deck and checkpoints: `Docs/HANDOFF.md` phases E and G, `Docs/Launch/SteamChecklist.md` section 3.

Spoiler level: S0 (the premise and Night 1) everywhere. Block 1 names Custodial once, because it quotes the game's own Story difficulty line and the store's Night 2 bullet already introduces Custodial. No Microsoft names: the editor is the Jotter, the bin is Disposal, the menu is the Nexus menu.

---

## 1. HOW TO PLAY block (full game store page)

**Where:** in the long description (`MarketingPackFull.md` 2.2), directly above `[h2]KEY FEATURES[/h2]`.

```text
[h2]HOW TO PLAY[/h2]
[list]
[*][b]Click[/b] to select. [b]Double-click[/b] an icon or file to open it.
[*][b]Drag[/b] a window by its title bar to move it. Drag a file onto a folder to move it, or onto the [b]Disposal[/b] bin to shred it.
[*][b]Right-click[/b] anything to see what you can do with it.
[*]Your tasks are in the [b]Work Queue[/b] (top right, or the [b]Task[/b] button on the taskbar). Do them in order. The hint under each task says how.
[*]The job: read the briefing in [b]Mail[/b], move files in [b]File Manager[/b] (the [b]Workstation[/b] icon), and look up each work order's owner in [b]Personnel[/b] before you click [b]Approve[/b] or [b]Reject[/b].
[*]If the second cursor grabs your file, keep the mouse button held and drag firmly away from it. Hold still, or let go, and it keeps the file.
[*]It types to you in the [b]Jotter[/b]. Type a reply and press [b]Enter[/b].
[*]Stuck? Hints come back on their own, and [b]NEXUS Help[/b] on the desktop explains every action.
[*][b]Esc[/b] or the [b]||[/b] button on the taskbar pauses the shift and opens the options. [b]Ctrl+S[/b] saves a file you changed in the Jotter.
[*][b]Normal[/b] or [b]Story[/b] difficulty. Story: the second cursor gives in sooner, Custodial is slower, and hints come early. Nothing is cut.
[*]Each night saves at checkpoints. [b]Continue[/b] on the title picks up from the last one.
[/list]
```

---

## 2. Demo page version (Night 1 only)

**Where:** in "About this demo" (`MarketingPackFull.md` 2.4), between the "What is in the demo" list and "Things worth trying".

```text
[h2]HOW TO PLAY[/h2]
[list]
[*][b]Double-click[/b] to open. Drag a file onto a folder to move it, or onto the [b]Disposal[/b] bin to shred it. [b]Right-click[/b] anything to see your options.
[*]Your tasks are in the [b]Work Queue[/b] (top right, or the [b]Task[/b] button on the taskbar). The hint under each task says how.
[*][b]Workstation[/b] opens File Manager. Check each work order's owner in [b]Personnel[/b], then click [b]Approve[/b] or [b]Reject[/b].
[*]If the second cursor grabs your file, keep the mouse button held and drag firmly away. Hold still and it keeps the file.
[*]When it types to you in the [b]Jotter[/b], type a reply and press [b]Enter[/b].
[*]Stuck? Hints come back on their own, and [b]NEXUS Help[/b] on the desktop explains everything. [b]Esc[/b] or the [b]||[/b] button pauses.
[*][b]Story[/b] difficulty: the second cursor gives in sooner and hints come early. The demo saves at checkpoints, and [b]Continue[/b] picks up from the last one.
[/list]
```

---

## 3. Steam Community Guide: "Your first shift: a quick guide"

**Guide title:** Your first shift: a quick guide
**Guide description (summary field):** Controls, the job, and where everything is on WS-04. Spoiler-free: nothing past the premise.
**Length:** just under 700 words of visible text.

```text
[h1]Your first shift: a quick guide[/h1]
It is 1:52 AM at Letheworth Data Reclamation. You are the night operator at workstation WS-04, and the whole game happens on that one screen. This guide covers the controls and the job, and nothing past the premise.

[h2]Before the shift[/h2]
On the very first launch, choose [b]Full effects[/b] or [b]Reduce flashing[/b]. On the title, pick [b]New Game[/b], then [b]Normal[/b] or [b]Story[/b], and click [b]Log On[/b]. The [b]NEXUS OS Quick Start[/b] window lists the basics. Click [b]Begin[/b] and your first task arrives. Headphones recommended.

[h2]What the screen shows[/h2]
[list]
[*]Icons down the left: [b]Workstation[/b], [b]Mail[/b], [b]Work Queue[/b], [b]Work Orders[/b], [b]Personnel[/b], [b]Jotter[/b], [b]Camera Viewer[/b] and [b]NEXUS Help[/b]. Double-click one to open it.
[*]The [b]Disposal[/b] bin, bottom right.
[*]The [b]Work Queue[/b] window, top right: tonight's tasks, with the current task's details and hint underneath.
[/list]

[h2]The taskbar[/h2]
From left to right: the [b]Nexus[/b] button, a button for each open window, the [b]Task[/b] button, the [b]||[/b] button, and the tray with the clock. The Task button always shows your current task and blinks when a new one arrives. Click it to reopen the Work Queue. The small mouse icons in the tray count the pointing devices connected. Keep an eye on them.

[h2]The Nexus menu[/h2]
Click [b]Nexus[/b] (bottom left) for every program, plus [b]Documents[/b], [b]System Monitor[/b], [b]Help[/b] and [b]Shut Down...[/b]. If you closed something, it is in here.

[h2]The apps you use[/h2]
[list]
[*][b]Mail:[/b] your briefing. Double-click Mail and it opens by itself. New mail during the shift also gets a line in the Work Queue until you read it.
[*][b]File Manager[/b] (double-click [b]Workstation[/b]): click a folder on the left, such as Intake, to see its files. Drag a file onto another folder to move it there.
[*][b]Work Orders:[/b] select an order and note the [b]Owner Emp. No.[/b]
[*][b]Personnel:[/b] click that number in the list to see the owner's status. [b]Approve[/b] only if it says TERMINATED. Anything else, [b]Reject[/b].
[*][b]Disposal:[/b] drag a file onto the bin, then click [b]Yes[/b]. Shredding is permanent.
[*][b]Jotter:[/b] a plain text editor. It matters more than it looks.
[/list]

[h2]Replying in the Jotter[/h2]
When a remote session types to you in the Jotter, a notice says so. Type a reply and press [b]Enter[/b]. One line at a time, anything you like. Your typing goes to the Jotter that is waiting for you, even when another window is in front. Some files can be changed in the Jotter: typing starts a new line at the end, and [b]File > Save[/b] (or [b]Ctrl+S[/b]) keeps the change.

[h2]Camera Viewer[/h2]
It shows the building's security cameras. The camera list is on the left: click a camera to switch to it. The viewer is meant for Security staff, so "Access Denied" is normal. Your work is in the Work Queue. If another session closes the viewer, a notice says so: double-click Camera Viewer to open it again.

[h2]If a file gets pulled away from you[/h2]
A label above the file says [b]SESSION 017 IS PULLING[/b]. Keep the mouse button held and drag firmly away from the other pointer: a small arrow on the file shows which way. The bar under the label shows who is winning. The first fight of a night starts with a moment to read the label. Hold still, or let go, and it keeps the file; the label says which it was ([b]YOU LET GO[/b], or [b]SESSION 017 PULLED HARDER[/b]). If it takes a file you were not holding, the label says so. You can always try again. Mail may tell you to let the other pointer finish: let it move things on its own, but when a task tells you to shred a file, fight for it.

[h2]If you are stuck[/h2]
[list]
[*]Wait a moment. The hint for your task pops up again on its own. Click it to open the Work Queue.
[*]Click the [b]Task[/b] button to see what is next.
[*][b]NEXUS Help[/b] explains every action in the game.
[*]Some things are locked on purpose. Stick to your tasks.
[*]Nothing stalls for good. If a task drags on too long, the shift carries on without it.
[/list]

[h2]Options[/h2]
Press [b]Esc[/b] or click [b]||[/b] to pause. The menu holds [b]CRT effects[/b], [b]Flashing[/b] (Full or Reduced), [b]Display[/b], [b]Frame rate[/b] (VSync, 30, 60, 120, 144 or Unlimited), [b]Reading text[/b] (Large doubles Mail, Jotter pages, Help and notices), [b]Volume[/b], [b]Difficulty[/b], [b]Restart from checkpoint[/b], [b]Quit to Title[/b] and [b]Quit[/b]. The shift also pauses when you switch to another program or open the Steam overlay.

[h2]Saving[/h2]
Each night saves at a few checkpoints. [b]Continue[/b] on the title picks up from the last one.

[h2]Steam Deck[/h2]
[list]
[*]The right trackpad moves the pointer. The left stick makes slow, precise moves.
[*][b]A[/b] clicks. Press A twice to open something, or tap the screen twice.
[*]Hold [b]R2[/b] and move to drag. If a file gets pulled away, keep R2 held and pull firmly away.
[*][b]L2[/b] shows what you can do with something.
[*][b]B[/b] or [b]Menu[/b] pauses. [b]X[/b] is Enter.
[*]The on-screen keyboard opens by itself when you can type, and Reading text starts at Large.
[/list]
```

---

## 4. FAQ

**Where:** a pinned discussion thread, or a second guide. Ten questions.

```text
[h1]SECOND CURSOR: FAQ[/h1]

[b]How long is it?[/b]
A first run takes about an hour across three nights. Seeing all three endings takes around two hours. The free demo is Night 1 and takes about 12 to 15 minutes.

[b]Is it scary?[/b]
It is quiet psychological horror: slow dread more than shocks. There is no gore, and death is implied, never shown. Expect a mouse pointer that is not yours, messages typed to you, a figure on a security camera, sudden low sounds and long silences. Headphones recommended.

[b]Is it safe to stream or record?[/b]
Yes. The game does not read or change your files (anything you save in the Jotter goes to the game's own pretend disk). It does not use your webcam or microphone, never moves your real mouse, and makes no internet connection of its own; Steam handles achievements the way it does for any game. Anything the fake computer says about cameras, recording or other users is part of the story. One practical note: while a shift is running, your pointer stays inside the game window, so a hard drag cannot land on another monitor. Pausing frees it.

[b]Can I get stuck?[/b]
No. The hint for your current task comes back every half minute or so, and clicking it opens the Work Queue. NEXUS Help on the desktop explains every action. If a task still is not done a while later, the shift moves on without it, and if you do not answer in the Jotter, the conversation carries on. Some things are locked on purpose: "Access Denied" is not a bug.

[b]What does Story difficulty change?[/b]
In the game's words: the second cursor gives in sooner, Custodial is slower, and hints come early. Nothing is cut. On Normal, the second cursor still eases off if you keep losing. You can switch in Options at any time; the change takes effect at the next checkpoint.

[b]How does saving work?[/b]
The game saves by itself at a few checkpoints each night, and your settings are saved as soon as you change them. Continue on the title picks up from the last checkpoint and shows the night and the time. Restart from checkpoint in the pause menu takes you back to it. New Game keeps your endings and records. Once you finish Night 1, Night Select lets you replay any night you have unlocked. The demo keeps its own save, so the full game starts at Night 1.

[b]Does it work on Steam Deck?[/b]
Yes. The right trackpad moves the pointer, A clicks, R2 drags, L2 shows what you can do with something, and B or Menu pauses. The on-screen keyboard opens by itself when you need to type, the in-game help and hints name the Deck buttons, and Reading text starts at Large. Suspending the Deck pauses the shift.

[b]Which languages?[/b]
English only, for text and for typing. There is no voice acting. You can type in any language, but replies are matched to English words.

[b]Are there photosensitivity options?[/b]
Yes. The game contains screen glitches and tearing, screen shake, film grain, CRT flicker, bursts of static, a flickering light in the camera feeds and a bright flash when the monitor powers off. You can choose Reduce flashing on the very first screen, or set Flashing to Reduced in Options at any time. It tones down glitches, flashes, shake and flicker spikes. CRT effects can be switched off too.

[b]Do I have to type?[/b]
No. The work itself is all mouse. Typing is how you talk back in the Jotter, and it is the heart of the game, but the night carries on if you stay quiet.
```

---

## 5. Mismatches found (store copy vs the game) and fixes

Store copy = `MarketingPackFull.md` section 2 as it stands today. Items LaunchAudit 2.2 already fixed there (length line, Custodial moved to Night 2, "the Restricted code", the last-operator keyword) are not repeated.

| # | Where in the store copy | Store says | Game shows | Fix | Priority |
|---|---|---|---|---|---|
| 1 | Demo page, last paragraph | "A Reduce flashing option is in the Esc menu." | No screen is called the Esc menu. First launch: buttons **Full effects** / **Reduce flashing**. In a shift, Esc or **\|\|** opens a menu captioned **SESSION PAUSED: OPTIONS** (Phase H; it was **SESSION PAUSED**) whose row reads **Flashing: Full** / **Flashing: Reduced**; on the title the same panel is **Options**. The disclaimer says "in Options (Esc, or the \|\| button on the taskbar)". | "Choose Reduce flashing on the first screen, or set Flashing to Reduced in Options (Esc, or the \|\| button on the taskbar)." | Medium (photosensitivity) |
| 2 | Long description, Photosensitivity | "A Reduce flashing option tones ... down" (at the very bottom) | Same labels as row 1. SteamChecklist 5.2 wants the warning near the top of About This Game. | "You can choose Reduce flashing on the first screen, or set Flashing to Reduced in Options at any time. It tones glitches, flashes, shake and flicker spikes down, and the CRT effects can be switched off." Move the paragraph up, or add one line near the top pointing to it. | Medium |
| 3 | Long description ("THE GHOST USES THE MOUSE"), also Key Features | "Yank away hard to win it back. Hold still and you lose it." | Help: "keep the mouse button held and drag firmly away from it. If you let go, it keeps the file." Notice after a lost tug (Phase H): "Input conflict: session 017 kept the file. Grab it again, keep the button held and drag away from its pointer." Both are true (holding still also loses). | Use the game's verbs so the page and the in-game notice teach the same move: "Keep the button held and drag firmly away to win it back. Hold still, or let go, and it keeps the file." | Low |
| 4 | Demo, "Things worth trying" | "Open the System Monitor and end the process that should not be there." | System Monitor has no desktop icon. It is only in the Nexus menu (bottom left). | "Open System Monitor from the Nexus menu and end the process that should not be there." | Low |
| 5 | Long description, camera section; demo "What is in the demo" | "Camera 03" | The camera list button reads **CAM 03 OFFICE B-7** (`story.json`: "CAM 03 - OFFICE B-7"); the Security mail says "CAM 03". | Write "CAM 03" so players recognise the button. | Low |
| 6 | Long description, Night 3 bullet | "a longer round that reaches the basement" | The same description opens with you already "in the basement". Night 3's round starts in Sublevel C (**CAM 04 SUBLEVEL C**, a label visible from Night 1). | "a longer round that starts down in Sublevel C" (or simply "a full round"). | Low |
| 7 | Demo, second paragraph | "Before the first half hour is out, a second cursor walks onto your screen" | The shift starts at 1:52 on the tray clock and the second cursor arrives around 2:20 to 2:30 (the urgent mail is dated 2:17); in real time it is 5 to 8 minutes. Players will read "half hour" against the clock. | "A few minutes into the shift, a second cursor walks onto your screen with a file of its own." | Low |
| 8 | FAQ (this file) vs short description | The store names files, webcam, microphone and mouse, not network. The FAQ above adds "makes no internet connection of its own", as the brief asked. | No network code in `Scripts`; Phase G's Apply Release Settings turns off engine diagnostics, analytics and the crash report API. The Steam client itself is online (achievements, overlay). | Before posting the FAQ, confirm the shipped build was made after Apply Release Settings. Then the store's safety lines may add "or network" too. Keep the "Steam handles achievements" wording. | Check before posting |
| 9 | Long description, endings section | "Nineteen Steam achievements, twelve of them hidden." | True in the game (Records lists 19, hidden ones as ???). On Steam only after the 19 entries and the TUG_WINS stat are published (SteamChecklist, owner item 6). No in-game pop-up. | Keep; do not publish the page with this line before the Steamworks achievements exist. | Blocked (known) |
| 10 | Guide and FAQ, Steam Deck parts (this file) | Deck controls as listed above | They match the in-game `.deck` strings (A twice, hold R2, L2, B or Menu, keyboard opens by itself). The trackpad, left stick and X = Enter parts depend on the default Steam Input layout, which is still to be published (SteamChecklist 3, D1). Deck hardware tests are open. | Publish the D1 layout before posting the Deck sections. Do not call the game Verified until Valve's review says so (the copy above does not). | Check before posting |

**In-game text gaps found while checking labels** (not store copy; each is a JSON-only or one-line change, for the dev, not blocking these posts):

| # | Where | Issue | Fix |
|---|---|---|---|
| a | `strings.json` `quickstart.body` | The PC Quick Start never says how to pause; the Deck version does ("B or the Menu button pauses the shift."). | Done in Phase H: "- Esc or the \|\| button on the taskbar: pause and Options." |
| b | `strings.json` `notify.conflict` | No `.deck` variant, so the Deck shows "Drag firmly away", while Deck Help says "keep R2 held and pull firmly away". `ContentDatabase` picks up any `key.deck`, so no code is needed. | Done in Phase H: `notify.conflict.deck` names R2 ("...keep R2 held and pull away from its pointer."). |
| c | `strings.json` `start.button` vs `Taskbar.cs` | The taskbar button text "Nexus" is hard-coded; the unused `start.button` still says "Start". Invisible today, but the data can drift and "Start" is the wrong word to ever ship. | Done in Phase H: `start.button` is "Nexus" and `Taskbar.Create` reads it. |
| d | Pause menu caption | During a shift the menu is captioned **SESSION PAUSED**, while the disclaimer and the title call it **Options**. The copy in this file says "pauses the shift and opens the options", which fits both. | Done in Phase H: the caption reads **SESSION PAUSED: OPTIONS**. |

---

## 6. Label check (every on-screen name used above)

| Label used | Source |
|---|---|
| Workstation, Mail, Work Queue, Work Orders, Personnel, Jotter, Camera Viewer, NEXUS Help, Disposal, File Manager | `app.*` in `strings.json`; desktop order from `Desktop.cs` |
| Nexus (button), Task (button, shows "Task: ...", or "8 min left: ..." for a task with a due time), \|\| | `start.button`, `taskbar.due`, `Taskbar.cs` |
| Documents, System Monitor, Help, Shut Down... | `start.documents`, `StartMenu.cs`, `start.help`, `start.shutdown` |
| Full effects, Reduce flashing (first launch) | `BootSequence.cs` |
| New Game, Normal, Story, Continue, Night Select, Records, Options, Quit | `title.new`, `title.normal`, `title.story`, `title.continue.at`, `title.select`, `title.records`, `title.settings`, `title.quit` |
| Story line ("the second cursor gives in sooner, Custodial is slower, and hints come early. Nothing is cut.") | `title.difficulty.story.body` |
| Normal eases off if you keep losing | `title.difficulty.normal.body` |
| Log On; NEXUS OS Quick Start; Begin | `login.button`; `quickstart.title`; `Night1Director.cs` |
| Approve, Reject; Owner Emp. No. | `WorkApps.cs`, `workorder.*`; `workorders.json` |
| Yes (shred confirm), Shredding is permanent | `Services.cs`; `shred.confirm.body`, briefing mail |
| TERMINATED | `tasks.json` task description, briefing mail |
| File > Save, Ctrl+S; "Typing starts a new line at the end" | `NotepadApp.cs` File menu; `notepad.editable.hint`, `help.body` |
| Remote session typing notice; "Type a reply and press Enter" | `notify.jotter.reply` |
| Drag firmly away; if you let go, it keeps the file; SESSION 017 IS PULLING, the arrow and the bar above the file; YOU LET GO; SESSION 017 PULLED HARDER; SESSION 017 TOOK THE FILE WHILE YOU WEREN'T HOLDING IT | `notify.conflict`, `help.body`, `tug.label`, `tug.lost.release`, `tug.lost.pulled`, `tug.snatch` |
| If mail says to let the other pointer finish, let it; a task that says to shred a file means fight for it | `quickstart.body`, `help.body` |
| YOUR TASKS appear in the Work Queue when you click Begin | `quickstart.body` |
| Queue clear. Await further assignments. (also after the last task is ticked) | `workqueue.empty`, `WorkQueueApp.cs` |
| PRIORITY: Shred employee_017.dat (blocked: held by session 017); "Nobody can" | `task.blocked.017.*`, `Night1Director.cs` |
| More below (a button that scrolls one page) | `mail.more`, `MoreBelow.cs` |
| "Access Denied"; "your work is in the Work Queue" | `camera.denied.title`, `camera.denied.body` |
| Some programs need clearance you do not have. Stick to your tasks. | `quickstart.body` |
| CRT effects, Flashing (Full/Reduced), Display, Volume | `PauseMenu.cs` (hard-coded) |
| Frame rate (VSync, 30, 60, 120, 144, Unlimited), Reading text (Normal/Large), Difficulty, Restart from checkpoint, Quit to Title, Quit | `pause.framerate*`, `DisplaySettings.FrameRates`, `pause.textsize*`, `pause.difficulty`, `pause.restart`, `pause.totitle`, `pause.quit` |
| Takes effect at the next checkpoint | `pause.difficulty.note` |
| New Game keeps endings and records | `title.new.confirm` |
| Headphones recommended. | `title.headphones` |
| Steam Deck: A twice or tap twice, hold R2, L2, B or Menu, keyboard opens by itself | `quickstart.body.deck`, `help.body.deck`; trackpad, left stick, X = Enter from SteamChecklist D1 |
| CAM 03, CAM 04 SUBLEVEL C | `story.json` cameras, button text from `CameraApp.ButtonLabel` |
