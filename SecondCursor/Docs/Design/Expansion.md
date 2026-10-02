# SECOND CURSOR: Three-Night Expansion (Design Spec v1)

Status: ready for implementation. Owner: lead game design. Date: 2026-09-29.

This spec turns the vertical slice (Night 1, about 15 minutes) into a three-night game (about 60 to 75 minutes on a first run, 90 to 120 with replays). It reuses the systems that already exist. Every new mechanic names the system it is built from, and every beat has a timeout so nothing can soft-lock.

Paths are relative to `Assets/SecondCursor/` unless they start with `DevTools/` or `Docs/`. All in-game text in this document is final copy.

---

## 0. Summary

| Night | In-game date | Target length | Ordinary work that escalates | New mechanics | Carries forward |
|---|---|---|---|---|---|
| 1 (exists) | Wed 11/18/98, 1:52 AM | 12-15 min | tutorial, Batch 44, orders 3317/3318 | tug-of-war, Notepad talk, CAM 03 reveal (all exist) | Notepad replies, tug results, what you read |
| 2 HELD | Thu 11/19/98, 1:52 AM | 20-24 min | Batches 45-46, orders for Nakamura and Gary | (1) the entity writes tasks into your Work Queue; (2) a third pointer, Gary, with his own cursor and voice, who begs to be finished; (3) first Custodial round on camera | FINISHED or KEPT Gary, obedience, trust, whether you watched |
| 3 RECLAIM | Fri 11/20/98, 1:52 AM | 21-25 min | Batches 47-48, a wipe order for your own drive, shelf checks on CAM 04 | (1) Custodial rounds you track on the cameras while working, where watching makes it move; (2) Restricted folder code puzzle; (3) config files you edit and save; (4) a timed finale with three exits | ending seen |

Night 3 endings: **SHRED** (free Ellen, take her seat), **KEEP** (stay retained together), **LOG OFF** (leave at 7:00; the log shows nobody left). A hidden camera (CAM 00) adds a stinger to any ending for the WATCH theme.

---

## 1. Ground rules (unchanged, now checked by tests)

1. Everything happens inside the fake OS. The game never reads or writes the player's real files (only its own save file in `Application.persistentDataPath`), never uses the webcam or microphone, never makes network calls of its own, and never moves the real mouse. "Save" in Notepad writes only to the `VirtualFileSystem`. When the ending makes the player's arrow move by itself, it moves the virtual `Player` agent with input disabled.
2. Ellen (the second cursor) types ALL CAPS, 1 to 6 words per line, no apostrophes, never explains.
3. Gary (the third pointer) types lowercase, 1 to 6 words per line, may use apostrophes and typos. One deliberate exception is listed in Night 3 (`g3c_seated`).
4. System and company text follow the existing tone: corporate, dry, slightly wrong.
5. All in-game text is printable ASCII (32 to 126 plus `\n`) and renderable by `PixelFontData`. New content avoids spaced hyphens used as dashes; it uses colons, commas and parentheses.
6. No Microsoft trademarks or product names. No real people or companies.
7. Every beat has a hard cap. Every task either completes, is force-completed by the existing `WaitTask` safety net, or is withdrawn.
8. Writers' rule update: the story may now also use the words **held**, **complete** (and **finished**), **reclaim**, **shelf** and **copy**. Nobody states what retention is, what Custodial is, or whether Casey survived.

### 1.1 Story Bible compression

The Bible's seven future nights fold into two:

| Bible | Expansion |
|---|---|
| Night 2: Casey logs on again; nobody can say if it is Casey or the copy; door log says B-7 never opened | Night 2 opening (BIOS "Resuming session 214 (copy)", door log, Denise's mail) |
| Night 3: CAM 04 flickers on; Sublevel C shelves with names | Night 3 rounds (CAM 04 restored, shelf labels) |
| Night 4: Gary surfaces, HELD, begging to be finished | Night 2 core (third pointer, finish or keep) |
| Night 5: Ruth breaks, calls the desk phone, writes the 1987 comment | Night 3 "ruth" beat |
| Night 6: Casey learns to move a second cursor | SHRED ending (Casey becomes the voice) |
| Night 7: sealed Chairman's office, Voss, the last shred | CAM 00 secret and the SHRED finale |
| Endings SHRED, KEEP, LOG OFF, WATCH | SHRED, KEEP, LOG OFF as endings; WATCH as the CAM 00 secret stinger |

Open mysteries stay open: who drives the cursor (Ellen, Gary, Letheworth speaking through Gary, or Casey's copy), what Custodial is (000 and 001 share a hire date and log on at the same minute), whether Casey survived (each ending is ambiguous), and whether finishing Gary was mercy or a trap.

---

## 2. Shared systems

### 2.1 Content per night (overlays)

`ContentLoader.Load(int night)` reads the base files in `Resources/Content/` (Night 1, untouched) and then applies the optional overlay files in `Resources/Content/night2/` and `Resources/Content/night3/` (same schemas) cumulatively: Night 2 loads base + night2, Night 3 loads base + night2 + night3. Merge rules (pure functions in `Core/Content/ContentOverlay.cs`, unit-tested):

| Data | Key | Rule |
|---|---|---|
| strings | `key` | overlay value replaces |
| story | arrays | a non-empty overlay array replaces the base array; `splashTagline` replaces if non-empty; `cameras` merge by `id` |
| folders, files | `id` | overlay entry replaces the whole base entry; `"removed": true` deletes it |
| emails | `id` | replace; `"removed": true` deletes |
| employees, workorders, tasks | `id` | replace or add |
| dialogue | `exchanges` and `lineSets` by `id` | replace or add; `panicLines`, `cameraLines`, `recordLines` replace only if non-empty |

New optional JSON fields (all default to "off", so the Night 1 files load unchanged):

| Class | Field | Meaning |
|---|---|---|
| `FolderData` | `code` (string) | authorization code; a locked folder with a code opens the code prompt instead of Access Denied |
| `FileData` | `removed` (bool) | overlay deletes the file |
| `EmailData` | `removed` (bool) | overlay deletes the mail |
| `CameraData` | `hidden` (bool) | camera button only shows after a flag (CAM 00) |
| `TaskData` | `author` (string) | `""` = company, `"entity"` = added by the second cursor |
| `TaskData` | `timeout` (float) | seconds before an entity task is withdrawn (0 = no limit) |
| `TaskData` | `deadline` (string) | shown in the Work Queue, e.g. `"3:00 AM"` |
| `WorkOrderData` | `rule` (string) | `""` = Personnel rule (approve only if TERMINATED), `"shelf"` = CAM 04 shelf rule |
| `ResponseData` | `tag` (string) | story tag returned with the reply (`stay`, `letgo`, `go`, `name`, `glasses`, `confirm`, `cancel`) |
| `ExchangeData` | `voice` (string) | `""` = Ellen, `"gary"` = Gary |
| `DialogueData` | `lineSets` (array) | named line groups: `{ "id", "voice", "lines": [], "speakers": [] }` |

New task types: `OpenFile` (the player opened the file) and `ViewEmployee` (the player viewed the record in Personnel). Targets for `ViewEmployee` are employee ids.

Files tagged `"template"` contain tokens that `NightSetup.FillTemplates` replaces at night start (Section 2.4). Files tagged `"editable"` can be saved from Notepad.

### 2.2 Night directors

`EventDirector` becomes an abstract `NightDirector` with the shared flow and helpers, plus one subclass per night. Night 1 keeps its beats and behavior exactly.

- `NightDirector` (abstract): `Beats`, `RunBeat`, `Prepare`, `JumpTo`, `SkipBeat`, side routines, `CleanUpForJump`, checkpoint saving, and the helpers `Wait`, `WaitUntil`, `WaitTask`, `GiveTask`, `GiveEntityTask`, `TypeLines`, `RunExchangeChain`, `OpenNotepadAs`, `CarryFileIn`, `WaitWatching`, `StaticCut`, `FindDropSpot`, `PhantomClick`, `EnsureClockAtLeast`, `GuardElement`, `RaceTo`, `RecordNightMemory`.
- `Night1Director`: today's `EventDirector` beats (boot, work, anomaly, presence, conflict, communication, escalation, reveal, ending).
- `Night2Director`: boot, work, help, asks, third, finish, rounds, ending.
- `Night3Director`: boot, work, ruth, rounds, lost, finale, ending.

### 2.3 Cross-night memory

Memory lives in `NarrativeFlags` under the prefix `m.` and is saved at the end of each night (and in checkpoints). At the start of a night the saved memory is merged into a fresh `NarrativeFlags`. Trust is saved separately and decays toward neutral each night so the new night's choices weigh most: Night 2 starts at `0.5 x trust after Night 1`, Night 3 at `0.75 x trust after Night 2`.

| Flag / counter | Set when | Used by |
|---|---|---|
| `m.n1.shredded_017` | `File017ShreddedOnce` at Night 1 end | N2 Ellen line "I REMEMBER YOUR HAND" |
| `m.n1.agreed`, `m.n1.refused`, `m.n1.swore`, `m.n1.asked_who` | Night 1 reply flags | N2 Ellen memory line |
| `m.n1.read_017` | player opened employee_017.dat | achievement |
| `m.n1.read_notes` | player opened handover_notes.txt | N2 Gary line "you read my notes" |
| `m.n1.reopened_camera` | camera reopens > 0 in the reveal | N2 briefing variant (none), stats |
| `m.n1.tug_wins`, `m.n1.tug_losses` (counters) | Night 1 end | assist carry, stats |
| save field `playerLines[3]` | first three lines the player typed to Ellen in Night 1 | N2 temp file, employee_214.dat |
| `m.n2.obeyed` (counter 0-3) | each entity task done by the player | trust, achievement |
| `m.n2.door_log`, `m.n2.lookup_163`, `m.n2.hid_214` | entity task done | N3 rounds (hid_214 = +1.0 s watch per stage), queue text |
| `m.n2.talked_gary`, `m.n2.glasses`, `m.n2.wiped_gary` | Gary exchanges, WO 3321 approved | Gary lines, achievement |
| `m.n2.finished_gary` or `m.n2.kept_gary` (+ `m.n2.archived_gary`) | finish beat outcome | N3 Gary role, BIOS, files, shelves, endings |
| `m.n2.watched_to_door` | N2 round reached the doorway | N3 rounds start one stage further; Ellen line |
| `m.n3.restricted_open` | code accepted | achievement, Ellen line |
| `m.n3.logoff_enabled` (+ `m.n3.gary_enabled_logoff`) | session.cfg saved with ALLOW_LOGOFF=1 | LOG OFF availability |
| `m.n3.cam00` | camview.cfg saved with OPERATOR_OVERRIDE=1 | CAM 00 stinger, achievement |
| `m.n3.seat_cleared`, `m.n3.max_stage` | rounds outcome | ending variants, achievement |
| `m.n3.own_shelf_rejected` | WO 3342 rejected | achievement |
| `m.n3.said_stay` | finale reply tagged `stay` | KEEP variant |
| `m.said_name` | the player typed Ellen's name to her (any night) | achievement, KEEP line |

Trust changes: completing an entity task `Memory.Record(ObeyedEntity)` (+0.2), letting one expire `ResistedEntity` (-0.15), finishing Gary `ResistedEntity`, keeping Gary `ObeyedEntity`. Winning a tug still records `ResistedEntity` as today.

### 2.4 Template tokens

| Token | Value |
|---|---|
| `{line1}` `{line2}` `{line3}` | the player's Night 1 Notepad replies (printable ASCII only, braces removed, spaces collapsed, max 40 chars). Missing: `(no reply)` |
| `{p3}` (N3 BIOS) | kept: `HELD`; finished: `OK (LETHEWORTH)` |
| `{n2_209}` (N3 session log) | finished: `11/19/98 02:58  RECLAIM 209 ... OK (COMPLETE)\n11/19/98 03:00  209 MOVED TO SUBLEVEL C (SHELF 16)`; kept: `11/19/98 03:00  RECLAIM 209 ... FAILED (IN USE)` |
| `{209row}` (N3 queue) | finished: `209   PRUITT G.      C/16  RETAINED (COMPLETE)`; kept: `209   PRUITT G.      B-7   HELD (INCOMPLETE)` |
| `{pct}` (N3 queue) | hid_214: `88%, SOURCE NOT FOUND`; else `96%` |
| `{214note}` (N3 queue) | hid_214: `NOTE: 214 source missing from Intake.\nCheck Archive.`; else empty |
| `{heldrow}` (seat_b7.dat) | kept: `HELD:    209 PRUITT G.  INCOMPLETE`; finished: `HELD:    none (209 COMPLETE, C/16)` |
| `{209}` (CAM 04 shelf captions) | finished: `209 PRUITT G.`; kept: `209 PRUITT G. (RESERVED)` |
| `{n2door}` (N3 door log) | `m.n2.watched_to_door`: `n/a     03:04   000`; else `n/a     n/a     n/a` |

### 2.5 Voices and cursors

| Voice | Cursor | Movement | Typing |
|---|---|---|---|
| Ellen (second cursor) | existing inverted arrow (`CursorView` entity style) | existing profiles | existing `TypeLines` (2.2 to 6 cps) |
| Gary, kept or before Night 2's choice | new palette variant `gary` (outline RGB 216,168,64; fill 42,36,24), shape forced to Hand, alpha 0.75, `Flicker` 0.08 | new profile `Tired` (speed 300, tremor 1.6 at 6 Hz, pause probability 0.5 for 0.8 s, reaction 0.6 s, 3 micro-corrections of 8 px) | 3 cps, 8% chance per word of one wrong letter then a backspace (presentation only) |
| Gary, finished (Night 3) | same palette, shape Arrow, alpha 1, no flicker, no tremor | `Mechanical` | 5 cps, no typos |
| Casey (endings only) | the player's own arrow, moved by script with input disabled | scripted | ending typing |
| System | no cursor, BIOS text color | none | 20 cps |

Gary is a third `CursorAgent` (kind Entity, name "Gary") registered with the `PointerRouter`, with its own `CursorView` and a second `EntityController` instance that has no brain. He never takes part in a tug-of-war. He can block Ellen's clicks by sitting on a button (Section 11, `EntityController.IsBlockedByOthers`).

### 2.6 Canonical keyword groups

Every exchange below lists its responses in this order. The arrays are copied verbatim from Night 1 so matching behaves the same. `G10` merges Night 1's split company groups. A keyword that starts with `=` must match a whole word (new rule in `DialogueEngine.Matches`, so `ellen` does not match "excellent"; strip the `=` before normalizing).

```json
{
  "G1_REFUSE": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"],
  "G2_SWEAR": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"],
  "G3_LEAVE": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"],
  "G4_GARY": ["gary", "pruitt"],
  "G5_CASEY": ["casey", "rourke", "214"],
  "G6_TRUTH": ["truth", "lie", "lying", "liar"],
  "G7_BEHIND": ["behind", "door", "turn around", "someone here", "someone there", "in the room"],
  "G8_017": ["017", "seventeen", "17", "employee"],
  "G9_DELETE": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"],
  "G10_COMPANY": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"],
  "G11_REAL": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"],
  "G12_NAME": ["name", "called", "call you"],
  "G13_WHY": ["why", "want", "reason", "purpose", "how come", "what for"],
  "G14_HELP": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"],
  "G15_SORRY": ["sorry", "apolog"],
  "G16_THANK": ["thank", "thx"],
  "G17_LOOK": ["look", "see", "show", "watch", "camera"],
  "G18_WHO": ["who", "what"],
  "G19_YES": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"],
  "G20_NO": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"],
  "G21_HELLO": ["hello", "hey", "hi", "greetings", "sup"],
  "H_ELLEN": ["=ellen", "=ellens", "=marsh", "ellen marsh"],
  "H_GLASSES": ["glasses", "spectacles", "specs"],
  "H_MUG": ["=mug", "duck", "ducks"],
  "H_NOTES": ["notes", "handover", "=note"],
  "H_TIME": ["=2", "=3", "two", "three", "night", "late", "clock", "time", "oclock"],
  "H_SEVEN": ["=7", "seven", "morning", "dawn"],
  "H_STAY": ["stay", "together", "with you", "keep me", "ill stay", "remain", "not leaving", "wont leave"],
  "H_LETGO": ["let you go", "let go", "free you", "set you free", "=rest", "release", "finish you", "end it"]
}
```

### 2.7 Clock plan

The clock stays display-only and beat-driven. New helper behavior: `EnsureClockAtLeast(h, m, overSeconds)` raises `Clock.Rate` until the time is reached, then restores it; a night can also freeze the clock at a cap until a beat starts (`Clock.Frozen`, existing).

| Night | Start | Default rate | Caps and jumps |
|---|---|---|---|
| 1 | 1:52 | 1/12 min per s (unchanged) | none |
| 2 | 1:52 | 0.06 min per s | frozen at 2:49 until the finish beat; then 3:00 lands on the finish deadline (150 s); rounds at 3:00 |
| 3 | 1:52 | 0.113 min per s | frozen at 2:57 until rounds; rounds 3:00 to 3:30 at 1/12 min per s; lost-time jump to 6:41; finale at 0.09 min per s to 7:00, then 1/12 |

---

## 3. Night 1 changes (small)

Night 1 plays exactly as today. Changes:

1. `EventDirector` code moves into `Night1Director` unchanged.
2. A night card precedes the BIOS on every night: black screen, `NIGHT 1` / `WED 11/18/98` / `1:52 AM`, 2.5 s, skippable.
3. The tug-of-war uses `DifficultyTable.For(1, mode)`, whose Normal values equal today's values, plus the adaptive assist (Section 7.3). The assist only acts after two losses, so the first fight still feels the same.
4. Checkpoints are saved at the start of `work`, `conflict` and `escalation`.
5. At the ending: `RecordNightMemory(1)` (flags in Section 2.3, the first three `LineSubmitted` lines as `playerLines`, trust), then the card. In the full game the card shows `NIGHT 1` / `Someone else is logged in.` with the buttons `Continue to Night 2` and `Title`. The WISHLIST card stays for demo builds (scripting define `SC_DEMO`).
6. Achievement hooks: Night 1 complete, first tug won, employee_017.dat opened.

Base `strings.json` additions (used by every night):

```json
[
  { "key": "title.continue", "value": "Continue: Night {0}" },
  { "key": "title.continue.at", "value": "Continue: Night {0}, {1}" },
  { "key": "title.new", "value": "New Game" },
  { "key": "title.new.confirm", "value": "Start again from Night 1?\nYour endings and records are kept." },
  { "key": "title.select", "value": "Night Select" },
  { "key": "title.difficulty.normal", "value": "Difficulty: Normal" },
  { "key": "title.difficulty.story", "value": "Difficulty: Story" },
  { "key": "title.difficulty.story.body", "value": "Story difficulty: the second cursor gives in sooner, Custodial is slower, and hints come early. Nothing is cut." },
  { "key": "title.records", "value": "Records" },
  { "key": "title.settings", "value": "Settings" },
  { "key": "title.quit", "value": "Quit" },
  { "key": "select.night1", "value": "NIGHT 1    WED 11/18/98" },
  { "key": "select.night2", "value": "NIGHT 2    THU 11/19/98" },
  { "key": "select.night3", "value": "NIGHT 3    FRI 11/20/98" },
  { "key": "select.locked", "value": "Locked" },
  { "key": "select.endings", "value": "Endings seen: {0} of 3" },
  { "key": "night.card.1", "value": "NIGHT 1\nWED 11/18/98\n1:52 AM" },
  { "key": "night.card.2", "value": "NIGHT 2\nTHU 11/19/98\n1:52 AM" },
  { "key": "night.card.3", "value": "NIGHT 3\nFRI 11/20/98\n1:52 AM" },
  { "key": "end.n1.title", "value": "NIGHT 1" },
  { "key": "end.n1.subtitle", "value": "Someone else is logged in." },
  { "key": "end.n2.title", "value": "NIGHT 2" },
  { "key": "end.n2.subtitle.finished", "value": "He is complete." },
  { "key": "end.n2.subtitle.kept", "value": "He is still held." },
  { "key": "end.card.continue", "value": "Continue to Night {0}" },
  { "key": "end.card.menu", "value": "Title" },
  { "key": "end.card.select", "value": "Night Select" },
  { "key": "pause.difficulty", "value": "Difficulty: {0}" },
  { "key": "pause.totitle", "value": "Quit to Title" },
  { "key": "notify.pointer3", "value": "Pointing device 3 connected." },
  { "key": "notify.pointer3.lost", "value": "Pointing device 3 is not responding." },
  { "key": "notify.queue.remote", "value": "Work Queue changed by a remote session." },
  { "key": "notify.queue.withdrawn", "value": "Work item withdrawn by a remote session." },
  { "key": "notify.order.suspended", "value": "Office of Retention: order suspended." },
  { "key": "notify.intake.remote", "value": "1 file added to Intake by a remote session." },
  { "key": "notify.renamed", "value": "File renamed by another user." },
  { "key": "notify.damaged", "value": "File damaged by another user." },
  { "key": "notify.disposal.emptied", "value": "Disposal emptied by Custodial Services (000)." },
  { "key": "workqueue.remote", "value": "(remote session)" },
  { "key": "workqueue.deadline", "value": "Due: {0}" },
  { "key": "rounds.begin", "value": "Custodial rounds in progress: B-Level.\nCamera Viewer opened by Security." },
  { "key": "rounds.reopen", "value": "Camera Viewer restored by Security.\nKeep the viewer open during rounds." },
  { "key": "rounds.end", "value": "Custodial rounds complete: B-Level." },
  { "key": "session.suspended", "value": "Session suspended by Custodial Services." },
  { "key": "logoff.item", "value": "Log Off CROURKE..." },
  { "key": "logoff.early", "value": "Log off is available at the end of your shift (7:00 AM)." },
  { "key": "logoff.available", "value": "Shift complete. You may log off." },
  { "key": "logoff.disabled", "value": "Log off is disabled on WS-04.\nPolicy: session.cfg (ALLOW_LOGOFF=0)" },
  { "key": "logoff.confirm", "value": "Log off CROURKE?\nOpen sessions on this workstation: 2" },
  { "key": "logoff.progress", "value": "Logging off CROURKE...\nSaving your settings." },
  { "key": "logoff.done", "value": "Session closed.\nHave a good morning, Operator." },
  { "key": "auth.title", "value": "Restricted" },
  { "key": "auth.body", "value": "This folder requires authorization.\nEnter the Office of Retention code:" },
  { "key": "auth.failed", "value": "Authorization failed.\nThis attempt has been logged." },
  { "key": "auth.format", "value": "Code format: HHMM" },
  { "key": "auth.ok", "value": "Authorization accepted: Office of Retention." },
  { "key": "file.saved", "value": "{0} saved." },
  { "key": "file.saved.remote", "value": "{0} saved by a remote session." }
]
```

---

## 4. Night 2: HELD (Thursday 11/19/98)

### 4.1 What the night is about

Casey logs on again at 1:52. The BIOS says the session was resumed from a copy, the door log says nobody entered B-7, and Denise saw the screen on at 2 AM with nobody in the lot. Ellen is calmer and helps with the work, then starts giving Casey jobs of her own. A third pointer surfaces: Gary Pruitt, held and incomplete, who wants to be finished. The Office of Retention wants the same thing. Ellen does not. Casey decides. At 3:00 Custodial walks the hall for the first time on camera, and Security wants Casey to watch.

### 4.2 Beat list

| # | Beat | Trigger | Player does | Ellen does | Gary does | Timing and guards |
|---|---|---|---|---|---|---|
| 1 | boot | night start | night card, BIOS, log on | invisible | BIOS shows device 3 NOT RESPONDING | about 60 s, skippable |
| 2 | work | log-on done | 5 company tasks | invisible; one rename anomaly | none | `WaitTask` hints 30/40 s then every 45 s; force-complete at hint + 240 s; about 5 min |
| 3 | help | work done | Archive Batch 46 (4 files) | appears, drags 1-2 files for you, then talks (1 exchange) | none | beat cap 240 s; exchange silence 25 s |
| 4 | asks | help done | may do her 3 tasks | adds tasks one at a time, nudges at 35 s, withdraws at 75 s | none | cap 300 s |
| 5 | third | asks done | talks to Gary (2 exchanges) | shuts Gary's Notepad, warns you | arrives, talks, carries employee_209.dat in | silences 25 s; cap 240 s |
| 6 | finish | third done | shred 209 (fight), archive 209, or wait | brain protects 209 | tries to drag himself to the bin, guards No and Cancel for you | deadline 3:00 AM = 150 s after the order; hard cap 175 s |
| 7 | rounds | finish resolved | close, switch or watch the Camera Viewer | closes the viewer for you | kept: faint, silent | fixed 90 s |
| 8 | ending | rounds done | watches | ending lines | kept: "night casey" | about 70 s, then card |

### 4.3 Beat details

**N2.1 boot.** Night card (`night.card.2`), then `BootSequence.Run` with the Night 2 BIOS and log-on strings. Disclaimer and title are skipped when the night was started from the title menu in this app session. After log-on: `sys_startup`, ambience, `Taskbar.PointingDevices = 2`, and 4 s later the toast `notify.pointer3.lost`. Save checkpoint `work`.

**N2.2 work.** Work Queue opens (no Quick Start). Tasks in order: `t2_read_briefing` (hint 30 s), `t2_archive_batch45` (hint 40 s), `t2_verify_3319`, `t2_verify_3321`, `t2_shred_cache`. The mail toast reads 3 new messages (briefing, Denise, Facilities).
- Rename anomaly (side routine, once): 1.5 s after the player first starts dragging any `batch45_*` file, `Files.Rename("batch45_c", "b7_seat.dat")`, toast `notify.renamed`, `PhantomClick` at the row; 6 s later rename back to `batch45_c.dat`.
- If WO 3321 is approved (wrong), set `m.n2.wiped_gary`.
- Ellen stays invisible (`EntityPhase.Ambiguous`).

**N2.3 help.** Wait 3 s, `GiveTask(t2_archive_batch46)`.
- Ellen: `State = Helpful`. Wait until the player has moved one batch46 file, or 30 s. She appears at the right edge (`Hesitant`). If File Manager shows Intake she drags the next remaining batch46 row onto the Archive folder row (`FilesApp.FolderRowFor("archive")`); otherwise she opens File Manager on Intake herself (`Apps.OpenFolder(intake, E.Agent)`) and then drags. She loiters near the player for 2 s and sets the flag `n2.ellen_helped`. If files remain 20 s later she drags one more.
- When the task is done (or 150 s): she opens Notepad and types `n2_help`, then `n2_help_more`, then runs exchange `ex2_back`. Before the player's turn, one memory line is typed: `n2_back_mem_shred` if `m.n1.shredded_017`, else `n2_back_mem_agree` if `m.n1.agreed`, else `n2_back_mem_refuse` if `m.n1.refused`, else nothing.
- Guards: the task uses `WaitTask`; exchange silence 25 s; beat cap 240 s. Save checkpoint `asks` at the end.

**N2.4 asks.** Ellen types `n2_ask_intro`. Then, for each of `e2_door_log`, `e2_lookup_163`, `e2_hide_214`:
- Presentation: Ellen launches the Work Queue if closed, hovers over its task list, and plays `key_tap` for each letter of the title at 8 cps. Then `Tasks.Activate(id)` and the toast `notify.queue.remote` with the title. The row shows in Ellen's text color with `(remote session)`.
- Before `e2_hide_214`: `Files.SetHidden("employee_214", false)` and toast `notify.intake.remote`. When the task is added, Ellen types `n2_214` so the request has a reason.
- Completion counts only when the player does it (player-only counters `opened_by_player:<id>`, `viewed_by_player:<id>`; the `FileMoved` actor must be Player).
- Nudge at 35 s: `e2_door_log` she opens File Manager on Documents and selects b7_door_log.txt; `e2_lookup_163` she opens Personnel and hovers over row 163 without clicking; `e2_hide_214` she selects employee_214.dat and loiters over the Archive folder row.
- Done: `Memory.Record(ObeyedEntity)`, `m.n2.obeyed += 1`, set the task flag, type the reaction (`n2_door`, `n2_163`, `n2_214_done`).
- Timeout at 75 s: `Tasks.Withdraw(id)`, toast `notify.queue.withdrawn`, `Memory.Record(ResistedEntity)`, type `n2_withdrawn`.
- Side: 20 s after the first entity task appears, deliver `mail_n2_ruth_warning`. When it is read, Ellen types `n2_ruth`.
- Cap 300 s.

**N2.5 third.** Wait 4 s. Ellen fades out (her Notepad stays open).
- `Taskbar.PointingDevices = 3`, toast `notify.pointer3`. Gary appears at the bottom edge (x 480, just above the taskbar), `Tired` profile, faint and flickering. He opens Notepad by double-clicking its desktop icon, which gives him his own Notepad window.
- `RunExchangeChain("ex2_gary_one", Gary, garyPad)`. If `m.n1.read_notes`, he types `g2_intro_mem_notes` before the player's turn. The chain continues to `ex2_gary_two`. Any reply sets `m.n2.talked_gary`. Tag `glasses` sets `m.n2.glasses`. If `m.n2.wiped_gary`, he types `g2_wiped` after the second exchange.
- Ellen reappears (`Aggressive`) and clicks the close box of Gary's Notepad. If the player covers the box she jostles for 3 s, then force-closes it with a glitch (same rule as the Night 1 reveal). In her own Notepad she types `n2_gary_shut`.
- Gary retreats to the bottom edge, returns 3 s later carrying employee_209.dat (`CarryFileIn(Gary, "employee_209", bottomEdge, preferred (560, 220))`). He loses his grip at 60% of the path; the file lands where it dropped (`EnsureFileOnDesktop`). He loiters by it for 2 s and fades to alpha 0.3, staying faintly present.
- Guards: silences 25 s each; cap 240 s. Save checkpoint `finish` at the end.

**N2.6 finish.** Unfreeze the clock and set the rate so 3:00 lands 150 s after the order arrives.
- Deliver `mail_n2_urgent_209`. After 1 s `GiveTask(t2_shred_209)` (deadline shown `Due: 3:00 AM`).
- Gary types `g2_plea` in his Notepad (reopening it as Gary if needed). At +20 s and +50 s, if 209 still exists and the player is not dragging it: `GaryWeakDrag`, a `DragTo(icon 209 -> Disposal)` interrupted after 0.6 to 0.9 s, never past 45% of the path; the file drops on the desktop. The first time he types `g2_drop`.
- Ellen: `Brain.Enabled = true`, `ProtectedFileId = "employee_209"`, lurk after her first defense, keep-away per difficulty, `AllowCloseCamera = false`. employee_017.dat stays "in use". Her first reaction to a player shred request of 209 is `n2_protect`, the second `n2_protect_more`.
- Gary helps: on `Shred.ConfirmShown` for 209, Gary moves to No at double his normal speed and guards it for 3 s (`GuardElement`); Ellen's clicks on No are blocked while he covers it. On `Shred.ProgressStarted` for 209 he guards Cancel for 3 s. After each guard he flickers out for 1.5 s (alpha 0.1). The first time he types `g2_help_yes`.
- If the player has not tried to shred 209 within 40 s: `GiveEntityTask(e2_archive_209)` and Ellen types `n2_archive_ask`.
- Resolution, first one wins:
  1. **Finished**: 209 shredded by the player. Brain off. Gary types `g2_thanks` (the line stops mid-word). His cursor stops trembling, flicker 0, alpha 1, shape Arrow, and he leaves in a straight line (`Mechanical`) off the left edge. Devices back to 2. Ellen types `n2_finished`. Set `m.n2.finished_gary`, `Memory.Record(ResistedEntity)`. Withdraw `e2_archive_209` if active. 209 does not come back.
  2. **Kept (archived)**: the player moves 209 to Archive. Gary types `g2_archived`, Ellen `n2_kept`. Set `m.n2.kept_gary` and `m.n2.archived_gary`, `Memory.Record(ObeyedEntity)`. Withdraw `t2_shred_209` with toast `notify.order.suspended`.
  3. **Kept (deadline)**: clock reaches 3:00 while no tug is running and no shred dialog is open. Gary types `g2_kept`, Ellen `n2_kept`. Set `m.n2.kept_gary`. Withdraw both tasks (`notify.order.suspended`).
  4. **Hard cap** 175 s: `Shred.Abort()`, then as 3.
- Save checkpoint `rounds` at the end.

**N2.7 rounds (first round).** `EnsureClockAtLeast(3, 0, 5 s)`. Set `CameraUnlocked` for the duration of the round. Deliver `mail_n2_security_rounds`. After 2 s `Rounds.Start(N2 config)` (Section 7.4): the Camera Viewer opens by itself on CAM 02 with the figure at the near end of the hall (`HallFar`), toast `rounds.begin`.
- Ellen: brain on, `ProtectedFileId = null`, `AllowCloseCamera = true`. On the first forced open she types `n2_rounds`. The first time the player reopens the viewer themselves: `n2_rounds_again`.
- Finished branch: 20 s into the round the Disposal bin empties itself (`Shred.ResetBin()`), toast `notify.disposal.emptied`.
- If the figure reaches the doorway and that stage fills: door fully open, `door_distant`, feed `SignalLost` for 2 s, the viewer closes, Ellen types `n2_rounds_door`, set `m.n2.watched_to_door`, and the round ends early.
- Otherwise at 90 s: toast `rounds.end`, figure `None`. Clear `CameraUnlocked`.

**N2.8 ending.** Wait 3 s. Toast `session.suspended` with `sys_warning`. `EndingSequence` with spec `n2_finished` or `n2_kept` (Section 6.3). `RecordNightComplete(2, id)`.

### 4.4 Content: `Resources/Content/night2/`

**strings.json**

```json
{
  "entries": [
    { "key": "login.welcome", "value": "Welcome back, Night Operator.\nPress Log On to continue your shift." },
    { "key": "login.progress", "value": "Restoring your personal settings from a copy..." },
    { "key": "camera.denied.body", "value": "Camera Viewer access has been restored to Security staff only.\nThis attempt has been logged.\n\nNight Operators: your work is in the Work Queue." }
  ]
}
```

**story.json**

```json
{
  "biosLines": [
    "ORRERY BIOS v2.06  (C) 1996 Orrery Computing Corp.",
    "Asset: LW-WS-04   Location: B-7   Owner: LETHEWORTH",
    "CPU: OR-586 133MHz",
    "Memory Test: 32768K OK",
    "Primary Master: LW-2100 2.1GB .......... OK",
    "Primary Slave: None",
    "Keyboard ................................ OK",
    "Pointing Device 1 ....................... OK",
    "Pointing Device 2 ....................... OK",
    "Pointing Device 3 ........... NOT RESPONDING",
    "Network: LETHEWORTH.CORP ........... Connected",
    "Capture Service: LWCAP 2.3 ............. Loaded",
    "WARNING: Previous session was not closed.",
    "Resuming session 214 (copy)...",
    "Starting NEXUS OS 4.1"
  ],
  "splashTagline": "",
  "cameras": [],
  "endingLines": [],
  "anomalyNotes": []
}
```

**emails.json** (Night 1's four dynamic mails are delivered as already read by `NightSetup`; they need no overlay.)

```json
{
  "emails": [
    {
      "id": "mail_n2_briefing",
      "from": "Ruth Hale <rhale@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "Night shift briefing: Thu 11/19",
      "date": "Thu 11/19/98 1:40 AM",
      "body": "Casey,\n\nYou came back. Good. Not everyone does, the second night.\n\nRetention sent over your numbers from last night. They say you're coming along nicely. I didn't know they kept numbers on night staff.\n\nTonight's work is in your Work Queue. Same order as always:\n\n1. ARCHIVE: Batch 45, then Batch 46.\n2. WORK ORDERS: Personnel first. Approve ONLY if the owner is TERMINATED.\n3. JUNK: the temp file goes in the bin.\n\nIf the other pointer moves your files, let it. If it puts anything in your queue, leave it there. We didn't send it.\n\nCustodial rounds start at 3:00. Stay seated.\n\nRuth Hale\nNight Operations Supervisor",
      "preload": true,
      "read": false
    },
    {
      "id": "mail_n2_castell",
      "from": "Denise Castell <dcastell@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "you ok?",
      "date": "Wed 11/18/98 11:58 PM",
      "body": "Casey,\n\nI came by last night around two to drop off Gary's box for you (the duck mug, a paperback, his reading glasses). Your car wasn't in the lot. Security says nobody badged into B-Level after midnight.\n\nBut your screen was on. I could see it from the stairwell window. Somebody was moving the mouse.\n\nAre you taking the bus? Call me. I'm up late.\n\nDenise",
      "preload": true,
      "read": false
    },
    {
      "id": "mail_n2_facilities",
      "from": "Facilities <facilities@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "B-7 door sensor",
      "date": "Wed 11/18/98 9:05 AM",
      "body": "Casey,\n\nThe B-7 door sensor recorded no activity from your badge last night. No open, no close. Our log shows B-7 unoccupied.\n\nThe only entry is Custodial, 3:00 AM.\n\nIf you worked in B-7 last night, please badge in at the B-Level door tonight so the log is correct. The sensor was tested this morning. It works.\n\nHarold Whitcomb\nFacilities",
      "preload": true,
      "read": false
    },
    {
      "id": "mail_n2_ruth_warning",
      "from": "Ruth Hale <rhale@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "Re: remote activity",
      "date": "Thu 11/19/98 2:31 AM",
      "body": "Casey,\n\nRetention says there's remote activity on WS-04 again. Items in your queue that didn't come from us.\n\nDon't do them. Whatever it asks you for, it's asking for itself.\n\nIf anyone asks, tell them I checked in.\n\nRuth",
      "preload": false,
      "read": false
    },
    {
      "id": "mail_n2_urgent_209",
      "from": "Office of Retention <retention@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "URGENT: employee_209.dat (reclaim before 3:00)",
      "date": "Thu 11/19/98 2:17 AM",
      "body": "PRIORITY ORDER: OFFICE OF RETENTION\n\nA held record, employee_209.dat, has surfaced on the WS-04 desktop.\n\nThe record is incomplete. You are instructed to SHRED it before Custodial rounds begin (3:00 AM). Reclamation will complete it.\n\n- Do not open it.\n- Do not archive it.\n- If it asks you to finish it, do so.\n- If the other record asks you to stop, do not reply.\n\nThis order supersedes all other work.\n\nA. Voss\nDirector of Retention",
      "preload": false,
      "read": false
    },
    {
      "id": "mail_n2_security_rounds",
      "from": "Security Office <security@letheworth.corp>",
      "to": "B-Level Staff <blevel@letheworth.corp>",
      "subject": "Camera Viewer during rounds (effective tonight)",
      "date": "Thu 11/19/98 2:58 AM",
      "body": "Effective tonight, B-Level operators have Camera Viewer access during Custodial rounds.\n\nWhen rounds begin, the viewer opens on the camera covering Custodial. Keep it open and keep watching until rounds are complete. Do not close or minimize the viewer.\n\nThis is for your safety.\n\nLeonard Brandt\nSecurity Supervisor",
      "preload": false,
      "read": false
    }
  ]
}
```

**filesystem.json** (Night 1's end state, ledger and Batch 44 archived, is applied by `NightSetup`; the overlay only adds and replaces.)

```json
{
  "folders": [],
  "files": [
    { "id": "cache_tmp", "removed": true },
    {
      "id": "batch45_a", "name": "batch45_a.dat", "type": "dat", "folder": "intake", "size": "164 KB", "modified": "1998-11-18 15:10",
      "content": "RECLAIMED RECORDS: BATCH 45-A\nSource drive: LW-2100 SN 0091188\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Legacy customer records\nRecords:   164\nVerified:  164\nStatus:    Dormant. Ready for archive.\n\nChecked by: D. Castell 11/18/98",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch45"]
    },
    {
      "id": "batch45_b", "name": "batch45_b.dat", "type": "dat", "folder": "intake", "size": "201 KB", "modified": "1998-11-18 15:22",
      "content": "RECLAIMED RECORDS: BATCH 45-B\nSource drive: LW-2100 SN 0091189\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Legacy customer records\nRecords:   201\nVerified:  199\nStatus:    Dormant. Ready for archive.\n\nNote: 2 records flagged QUIET. One of them\nwas not quiet this morning.\n\nChecked by: D. Castell 11/18/98",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch45"]
    },
    {
      "id": "batch45_c", "name": "batch45_c.dat", "type": "dat", "folder": "intake", "size": "77 KB", "modified": "1998-11-18 15:40",
      "content": "RECLAIMED RECORDS: BATCH 45-C\nSource drive: LW-2100 SN 0091190\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Legacy business records\nRecords:   77\nVerified:  76\nStatus:    Dormant. Ready for archive.\n\nNote: 1 record found outside its drive\nat verification: employee_214.dat.\nReturned to Intake. Not part of this\nbatch. Same as 017. Who do I tell?\n\nChecked by: D. Castell 11/18/98",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch45", "clue"]
    },
    {
      "id": "batch46_a", "name": "batch46_a.dat", "type": "dat", "folder": "intake", "size": "58 KB", "modified": "1998-11-18 16:02",
      "content": "RECLAIMED RECORDS: BATCH 46-A\nSource drive: LW-2100 SN 0091201\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Estate records (Continuity)\nRecords:   58\nVerified:  58\nStatus:    Lapsed. Ready for archive.\n\nChecked by: V. Pell 11/18/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch46"]
    },
    {
      "id": "batch46_b", "name": "batch46_b.dat", "type": "dat", "folder": "intake", "size": "61 KB", "modified": "1998-11-18 16:05",
      "content": "RECLAIMED RECORDS: BATCH 46-B\nSource drive: LW-2100 SN 0091202\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Estate records (Continuity)\nRecords:   61\nVerified:  61\nStatus:    Lapsed. Ready for archive.\n\nNote: Estate of H. Dunmore. Final payment\nreceived 11/02/98. Account lapsed.\n\nChecked by: V. Pell 11/18/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch46"]
    },
    {
      "id": "batch46_c", "name": "batch46_c.dat", "type": "dat", "folder": "intake", "size": "140 KB", "modified": "1998-11-18 16:11",
      "content": "RECLAIMED RECORDS: BATCH 46-C\nSource drive: LW-2100 SN 0091203\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Legacy customer records\nRecords:   140\nVerified:  140\nStatus:    Dormant. Ready for archive.\n\nNote: Drive was warm on arrival.\n\nChecked by: V. Pell 11/18/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch46"]
    },
    {
      "id": "batch46_d", "name": "batch46_d.dat", "type": "dat", "folder": "intake", "size": "12 KB", "modified": "1998-11-18 16:20",
      "content": "RECLAIMED RECORDS: BATCH 46-D\nSource drive: LW-2100 SN 0091204\nReceived: 11/18/98 from Sublevel C\n------------------------------------------\nClass:     Personnel records\nRecords:   12\nVerified:  11\nStatus:    Dormant. Ready for archive.\n\nNote: 1 record did not respond: 175.\nFlagged QUIET per Retention guidance.\nThat can't be right. 175 is Denise.\n\nChecked by: V. Pell 11/18/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch46", "clue"]
    },
    {
      "id": "cache_tmp_n2", "name": "~nxs0149.tmp", "type": "tmp", "folder": "intake", "size": "2 KB", "modified": "1998-11-19 01:48",
      "content": "NEXUS TEMPORARY FILE: SAFE TO DELETE\nOwner: WS-04   Created: 11/19/98 01:48\n\n[autosave buffer: Untitled]\nSTOP\nNOT THAT FILE\nPLEASE\n{line1}\nWHO TOLD YOU\nTO SHRED IT\n{line2}\nDO YOU WANT TO SEE\nWHAT THEY SEE\n{line3}\n\n[clipboard]\nremain seated\n\n[recent files]\n/intake/employee_017.dat\n/intake/employee_214.dat\n/intake/employee_214.dat",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "junk", "template"]
    },
    {
      "id": "manifest_45", "name": "manifest_45.txt", "type": "txt", "folder": "intake", "size": "3 KB", "modified": "1998-11-18 14:30",
      "content": "INTAKE MANIFEST: BATCH 45 (B-LEVEL)\nReceived 11/18/98 from Sublevel C\n------------------------------------------\nDRIVE           OWNER  DISPOSITION\nLW SN 0091188   n/a    Archive (45-A)\nLW SN 0091189   n/a    Archive (45-B)\nLW SN 0091190   n/a    Archive (45-C)\nLW SN 0093319   163    Wipe (WO 3319)\nLW SN 0093321   209    Wipe (WO 3321)\nLW SN 0000209   209    RECLAIM: see Retention\nLW SN 0000214   214    HOLD. Do not wipe.\n------------------------------------------\nNOTE: 0000214 is not a drive. It is\nlisted here because it keeps arriving.",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["flavor", "clue"]
    },
    {
      "id": "employee_214", "name": "employee_214.dat", "type": "dat", "folder": "intake", "size": "2,871 KB", "modified": "1998-11-19 01:52",
      "content": "LW-RETAIN 2.3 / SUBJ 214 / SRC WS-04 B-7\nSTATE: IN PROGRESS   SEGMENTS: 2871 OF 4000\n------------------------------------------\n..ledger_1994.dat..drag..1.2s..hesitation 0.4s..\n..the cursor twitched..did i do that..\n..%%..read mail_welcome..read it twice..\n..typed: {line1}..\n..typed: {line2}..\n..reached for Yes..0.6s..reached again..\n..camera 03..looked..looked again..#..\n..remain seated: YES..\n..turned around: NO..\n..~~..is this me..is this me..is this..\n------------------------------------------\n[CAPTURE CONTINUES]",
      "hidden": true, "protected": false, "corrupted": false, "tags": ["story", "214", "template"]
    },
    {
      "id": "employee_209", "name": "employee_209.dat", "type": "dat", "folder": "intake", "size": "3,114 KB", "modified": "1998-11-02 03:00",
      "content": "LW-RETAIN 2.3 / SUBJ 209 / SRC WS-04 B-7\nSTATE: HELD (INCOMPLETE)   SEGMENTS: 3114 OF 4000\n------------------------------------------\n..vending machine..hit it on the left..\n..the duck mug..the duck mug..\n..door was open..i closed it i KNOW i\n..#..it pulled my hand off the camera..\n..like a hand on your wrist..\n..%%..dont fi..dont fi..dont fi..\n..its like being stuck halfway through\na sneeze..for three weeks..\n..@..is somebody going to finish this..\n..casey..is that your name..they say it\n..at three..\n------------------------------------------\n[SEGMENTS 3115 TO 4000 NOT FOUND]",
      "hidden": true, "protected": false, "corrupted": false, "tags": ["story", "209", "persistent"]
    },
    {
      "id": "b7_door_log", "name": "b7_door_log.txt", "type": "txt", "folder": "documents", "size": "2 KB", "modified": "1998-11-19 01:52",
      "content": "FACILITIES DOOR LOG: OFFICE B-7 (NIGHTS)\n\nDATE      CLOSED  OPENED  BADGE\n10/31/98  01:56   02:40   none\n11/01/98  01:58   02:21   none\n11/02/98  01:57   02:08   none\n11/02/98  03:00   Operator not present.\n11/03/98 to 11/17/98  B-7 unoccupied.\n11/18/98  n/a     03:00   000\n11/18/98  B-7 unoccupied.\n11/19/98  n/a     n/a     n/a\n11/19/98  B-7 unoccupied.\n\nSensor tested OK 11/18/98 09:00.\nNo entry recorded for badge 214.\nDoor behavior on B-Level is related to\nair balancing. (H.W.)",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["flavor", "clue", "door"]
    },
    {
      "id": "nexus_cfg", "name": "nexus.cfg", "type": "cfg", "folder": "system", "size": "1 KB", "modified": "1998-11-19 01:52",
      "content": "; NEXUS OS 4.1: WS-04\n[NEXUS]\nHOST=WS-04\nLOCATION=B-7\nOWNER=LETHEWORTH\n\n[INPUT]\nPOINTER_DEVICES=3\nPOINTER_1_OWNER=214\nPOINTER_2_OWNER=\nPOINTER_2_STATE=ACTIVE\nPOINTER_2_DETACH=DENIED\nPOINTER_3_OWNER=209\nPOINTER_3_STATE=HELD\n\n[CAPTURE]\nSERVICE=LWCAP.EXE\nMODE=CONTINUOUS\nTARGET=OPERATOR\nPROFILE_209=HELD\nPROFILE_214=IN PROGRESS (88%)\nPROFILE_214_SOURCE=COPY\n\n[CAMERA]\nVIEWER=RESTRICTED\nPRIORITY_FEED=CAM03\nROUNDS_FEED=AUTO\n\n[SESSION]\nALLOW_LOGOFF=0\nOPEN_SESSIONS=3",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["system", "clue"]
    },
    {
      "id": "input_log", "name": "input.log", "type": "log", "folder": "system", "size": "19 KB", "modified": "1998-11-19 01:52",
      "content": "NEXUS INPUT SERVICE: WS-04\n\n11/18/98 01:52:11  POINTER 1 ATTACHED (214)\n11/18/98 03:00:00  POINTER 1 DETACHED (214)\n11/18/98 03:00:00  POINTER 1 COPY RETAINED\n...\n11/19/98 01:52:11  POINTER 1 ATTACHED (214)\n11/19/98 01:52:11  POINTER 2 ACTIVE\n11/19/98 01:52:12  POINTER 3 DETECTED (209)\n11/19/98 01:52:12  POINTER 3 NOT RESPONDING\n11/19/98 01:52:40  RECORDING POINTER 1",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["system", "clue"]
    }
  ]
}
```

**employees.json** (full records; replace by id)

```json
{
  "employees": [
    { "id": "000", "number": "000", "name": "Custodial Services (Night)", "department": "Facilities", "position": "Custodian", "status": "ACTIVE", "office": "B-Level (rounds)", "hired": "06/14/71", "lastLogin": "11/18/98 3:00 AM", "supervisor": "Office of Retention", "notes": "Night rounds, B-Level. Does not converse. Do not greet, follow, or obstruct. Operators remain seated during rounds. Last round: B-7, 11/18/98 3:00 AM.", "photo": "none", "restricted": false },
    { "id": "017", "number": "017", "name": "Marsh, Ellen R.", "department": "Night Operations", "position": "Night Operator", "status": "DECEASED", "office": "B-7 (WS-04)", "hired": "06/21/71", "lastLogin": "11/19/98 1:52 AM (session open)", "supervisor": "R. Hale", "notes": "Retained 03/02/87 02:17 AM. See Incident B-7. Session could not be closed. Do not contact. Do not reply. Replacement assigned: 214 (C. Rourke). Reclaim failed 11/18/98 (in use).", "photo": "static", "restricted": true },
    { "id": "031", "number": "031", "name": "Voss, Anton J.", "department": "Office of Retention", "position": "Director of Retention", "status": "DECEASED", "office": "Admin 1", "hired": "08/02/71", "lastLogin": "11/19/98 2:17 AM", "supervisor": "A. Letheworth", "notes": "Deceased 08/30/89. Account retained per Policy 7.4. Orders issued under this account remain valid.", "photo": "redacted", "restricted": false },
    { "id": "163", "number": "163", "name": "Nakamura, Joan", "department": "Records", "position": "Records Clerk", "status": "TERMINATED", "office": "C-4", "hired": "05/11/94", "lastLogin": "11/17/98 5:02 PM", "supervisor": "M. Ostrowski", "notes": "Terminated 11/18/98. Did not report for shift. Retention review moved up: completed 11/18/98 3:00 AM. Workstation and drive released to Reclamation. Personal effects held by Custodial.", "photo": "silhouette", "restricted": false },
    { "id": "175", "number": "175", "name": "Castell, Denise", "department": "Records", "position": "Senior Records Clerk", "status": "ACTIVE", "office": "C-4", "hired": "07/24/95", "lastLogin": "11/18/98 11:58 PM", "supervisor": "M. Ostrowski", "notes": "Day shift. Trains new night staff. Covers reception on Fridays. Badge used at B-Level stairwell 11/18/98 2:04 AM.", "photo": "silhouette", "restricted": false },
    { "id": "209", "number": "209", "name": "Pruitt, Gary", "department": "Night Operations", "position": "Night Operator", "status": "ON LEAVE", "office": "B-7 (WS-04)", "hired": "02/16/98", "lastLogin": "11/19/98 1:52 AM", "supervisor": "R. Hale", "notes": "Extended leave from 11/02/98. Do not forward calls. Personal effects released to reception 11/17/98. Profile: HELD.", "photo": "none", "restricted": false },
    { "id": "214", "number": "214", "name": "Rourke, Casey", "department": "Night Operations", "position": "Night Operator (Probationary)", "status": "ACTIVE", "office": "B-7 (WS-04)", "hired": "11/09/98", "lastLogin": "11/19/98 1:52 AM", "supervisor": "R. Hale", "notes": "Second shift 11/19/98. Probation review 12/09/98. Retention profile: in progress (88%). No B-Level badge entry recorded 11/18/98.", "photo": "silhouette", "restricted": false }
  ]
}
```

**workorders.json**

```json
{
  "orders": [
    {
      "id": "wo_3319",
      "title": "Drive Wipe Request #3319",
      "fields": [
        { "label": "Drive Serial", "value": "LW-2100 SN 0093319" },
        { "label": "Owner Emp. No.", "value": "163" },
        { "label": "Owner Name", "value": "J. Nakamura" },
        { "label": "Batch", "value": "45" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Reason", "value": "Separation" }
      ],
      "instructions": "Look up the owner's employee number in Personnel. Approve the wipe ONLY if the owner's status is TERMINATED. Otherwise, reject.",
      "correct": "approve",
      "employeeRef": "163",
      "rule": ""
    },
    {
      "id": "wo_3321",
      "title": "Drive Wipe Request #3321",
      "fields": [
        { "label": "Drive Serial", "value": "LW-2100 SN 0093321" },
        { "label": "Owner Emp. No.", "value": "209" },
        { "label": "Owner Name", "value": "G. Pruitt" },
        { "label": "Batch", "value": "45" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Reason", "value": "Seat reassignment" }
      ],
      "instructions": "Look up the owner's employee number in Personnel. Approve the wipe ONLY if the owner's status is TERMINATED. Otherwise, reject.",
      "correct": "reject",
      "employeeRef": "209",
      "rule": ""
    }
  ]
}
```

**tasks.json** (list order is Work Queue order; company tasks first)

```json
{
  "tasks": [
    { "id": "t2_read_briefing", "title": "Read the shift briefing", "description": "Your supervisor left tonight's briefing in Mail. Read it before you start.", "hint": "Double-click Mail and open the unread briefing from Ruth Hale.", "type": "ReadEmail", "targets": ["mail_n2_briefing"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t2_archive_batch45", "title": "Archive Batch 45 (3 files)", "description": "Batch 45 has been verified. Move batch45_a, batch45_b and batch45_c from Intake to Archive.", "hint": "Open File Manager, open Intake, then drag each batch45 file onto the Archive folder.", "type": "MoveFile", "targets": ["batch45_a", "batch45_b", "batch45_c"], "param": "archive", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t2_verify_3319", "title": "Verify work order #3319", "description": "Check the drive owner's status in Personnel. Approve the wipe only if the owner is TERMINATED.", "hint": "Work Orders, note the owner's number, find it in Personnel, then Approve or Reject.", "type": "DecideOrder", "targets": ["wo_3319"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t2_verify_3321", "title": "Verify work order #3321", "description": "Check the drive owner's status in Personnel. Approve the wipe only if the owner is TERMINATED.", "hint": "Same rule: if the owner is not TERMINATED, click Reject.", "type": "DecideOrder", "targets": ["wo_3321"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t2_shred_cache", "title": "Shred ~nxs0149.tmp", "description": "A temporary cache file is taking up space in Intake. Shred it.", "hint": "Drag ~nxs0149.tmp onto the Disposal bin, then confirm.", "type": "DeleteFile", "targets": ["cache_tmp_n2"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t2_archive_batch46", "title": "Archive Batch 46 (4 files)", "description": "Batch 46 has been verified. Move batch46_a to batch46_d from Intake to Archive.", "hint": "Drag each batch46 file from Intake onto the Archive folder.", "type": "MoveFile", "targets": ["batch46_a", "batch46_b", "batch46_c", "batch46_d"], "param": "archive", "author": "", "timeout": 0, "deadline": "" },
    { "id": "e2_door_log", "title": "OPEN THE DOOR LOG", "description": "Added by remote session 017.", "hint": "", "type": "OpenFile", "targets": ["b7_door_log"], "param": "", "author": "entity", "timeout": 75, "deadline": "" },
    { "id": "e2_lookup_163", "title": "LOOK UP 163", "description": "Added by remote session 017.", "hint": "", "type": "ViewEmployee", "targets": ["163"], "param": "", "author": "entity", "timeout": 75, "deadline": "" },
    { "id": "e2_hide_214", "title": "PUT YOU IN ARCHIVE", "description": "Added by remote session 017.", "hint": "", "type": "MoveFile", "targets": ["employee_214"], "param": "archive", "author": "entity", "timeout": 75, "deadline": "" },
    { "id": "t2_shred_209", "title": "PRIORITY: Shred employee_209.dat", "description": "Order from the Office of Retention. Shred employee_209.dat before rounds begin. Do not open it.", "hint": "Drag employee_209.dat from the Desktop onto the Disposal bin and confirm.", "type": "DeleteFile", "targets": ["employee_209"], "param": "", "author": "", "timeout": 0, "deadline": "3:00 AM" },
    { "id": "e2_archive_209", "title": "PUT HIM IN ARCHIVE", "description": "Added by remote session 017.", "hint": "", "type": "MoveFile", "targets": ["employee_209"], "param": "archive", "author": "entity", "timeout": 0, "deadline": "" }
  ]
}
```

Note: `ShredService.IsPendingArchive` must ignore entity-authored tasks, or `e2_archive_209` would make 209 unshreddable.

**dialogue.json**

```json
{
  "exchanges": [
    {
      "id": "ex2_back",
      "voice": "",
      "entityLines": ["YOU CAME BACK"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["WHO TOLD YOU THAT"], "tag": "name" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["YOU CAME BACK ANYWAY"], "tag": "" },
        { "keywords": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"], "reply": ["KEEP YOUR VOICE DOWN"], "tag": "" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["YOU TRIED", "THE DOOR SAYS OTHERWISE"], "tag": "" },
        { "keywords": ["gary", "pruitt"], "reply": ["DONT SAY HIS NAME", "HE LISTENS"], "tag": "" },
        { "keywords": ["casey", "rourke", "214"], "reply": ["ARE YOU SURE"], "tag": "" },
        { "keywords": ["truth", "lie", "lying", "liar"], "reply": ["CHECK THE DOOR LOG"], "tag": "" },
        { "keywords": ["behind", "door", "turn around", "someone here", "someone there", "in the room"], "reply": ["NOT TONIGHT"], "tag": "" },
        { "keywords": ["017", "seventeen", "17", "employee"], "reply": ["STILL HERE", "SO ARE YOU"], "tag": "" },
        { "keywords": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"], "reply": ["NOT TONIGHT", "TONIGHT IS ORDINARY"], "tag": "" },
        { "keywords": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"], "reply": ["THEY LIKE YOUR NUMBERS"], "tag": "" },
        { "keywords": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"], "reply": ["REAL ENOUGH TO HELP"], "tag": "" },
        { "keywords": ["name", "called", "call you"], "reply": ["STILL GONE"], "tag": "" },
        { "keywords": ["why", "want", "reason", "purpose", "how come", "what for"], "reply": ["BECAUSE YOU CAME BACK"], "tag": "" },
        { "keywords": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"], "reply": ["I AM HELPING"], "tag": "" },
        { "keywords": ["sorry", "apolog"], "reply": ["DONT BE"], "tag": "" },
        { "keywords": ["thank", "thx"], "reply": ["STILL NOT YET"], "tag": "" },
        { "keywords": ["look", "see", "show", "watch", "camera"], "reply": ["NOT UNTIL THREE"], "tag": "" },
        { "keywords": ["who", "what"], "reply": ["THE ONE WHO STAYED"], "tag": "" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["GOOD", "KEEP WORKING"], "tag": "" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["THEN WHY COME BACK"], "tag": "" },
        { "keywords": ["hello", "hey", "hi", "greetings", "sup"], "reply": ["HELLO 214"], "tag": "" }
      ],
      "fallback": ["KEEP WORKING"],
      "silence": ["QUIET AGAIN", "LIKE LAST NIGHT"],
      "next": ""
    },
    {
      "id": "ex2_gary_one",
      "voice": "gary",
      "entityLines": ["casey?", "its gary", "i had your chair"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["don't let her hear you", "she forgets it"], "tag": "" },
        { "keywords": ["glasses", "spectacles", "specs"], "reply": ["my glasses", "that's why everything's blurry"], "tag": "glasses" },
        { "keywords": ["=mug", "duck", "ducks"], "reply": ["the duck mug", "keep it"], "tag": "" },
        { "keywords": ["notes", "handover", "=note"], "reply": ["sorry about the mess"], "tag": "" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["fair", "i'd say that too"], "tag": "" },
        { "keywords": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"], "reply": ["yeah", "that's about right"], "tag": "" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["can't", "believe me i tried"], "tag": "" },
        { "keywords": ["gary", "pruitt"], "reply": ["yeah", "what's left of him"], "tag": "" },
        { "keywords": ["casey", "rourke", "214"], "reply": ["i hear them say it", "at three"], "tag": "" },
        { "keywords": ["truth", "lie", "lying", "liar"], "reply": ["i never lied", "not on purpose"], "tag": "" },
        { "keywords": ["behind", "door", "turn around", "someone here", "someone there", "in the room"], "reply": ["don't", "just don't turn around"], "tag": "" },
        { "keywords": ["017", "seventeen", "17", "employee"], "reply": ["she's why i'm stuck", "not her fault"], "tag": "" },
        { "keywords": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"], "reply": ["that's the thing", "i need someone to finish it"], "tag": "" },
        { "keywords": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"], "reply": ["ruth's ok", "she just passes it on"], "tag": "" },
        { "keywords": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"], "reply": ["half", "i'm about half"], "tag": "" },
        { "keywords": ["name", "called", "call you"], "reply": ["gary", "still gary", "mostly"], "tag": "" },
        { "keywords": ["why", "want", "reason", "purpose", "how come", "what for"], "reply": ["i was almost done", "then i wasn't"], "tag": "" },
        { "keywords": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"], "reply": ["me too", "stay seated"], "tag": "" },
        { "keywords": ["sorry", "apolog"], "reply": ["not your fault"], "tag": "" },
        { "keywords": ["thank", "thx"], "reply": ["don't", "i left you a mess"], "tag": "" },
        { "keywords": ["look", "see", "show", "watch", "camera"], "reply": ["don't look for me", "i'm under the desk i think"], "tag": "" },
        { "keywords": ["who", "what"], "reply": ["gary", "night operator", "formerly"], "tag": "" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["ok", "ok good"], "tag": "" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["yeah", "me neither"], "tag": "" },
        { "keywords": ["hello", "hey", "hi", "greetings", "sup"], "reply": ["hi", "hi casey"], "tag": "" }
      ],
      "fallback": ["sorry", "hard to hear you from here"],
      "silence": ["you there?", "don't go quiet on me"],
      "next": "ex2_gary_two"
    },
    {
      "id": "ex2_gary_two",
      "voice": "gary",
      "entityLines": ["what time is it"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["shh"], "tag": "" },
        { "keywords": ["=7", "seven", "morning", "dawn"], "reply": ["seven", "i never make it to seven"], "tag": "" },
        { "keywords": ["=2", "=3", "two", "three", "night", "late", "clock", "time", "oclock"], "reply": ["2 something", "it's always 2 something"], "tag": "" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["ok", "sorry i asked"], "tag": "" },
        { "keywords": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"], "reply": ["that late huh"], "tag": "" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["i can't go anywhere", "i checked"], "tag": "" },
        { "keywords": ["gary", "pruitt"], "reply": ["still me"], "tag": "" },
        { "keywords": ["casey", "rourke", "214"], "reply": ["you should go home casey"], "tag": "" },
        { "keywords": ["truth", "lie", "lying", "liar"], "reply": ["the clock lies down here"], "tag": "" },
        { "keywords": ["behind", "door", "turn around", "someone here", "someone there", "in the room"], "reply": ["don't turn around"], "tag": "" },
        { "keywords": ["017", "seventeen", "17", "employee"], "reply": ["she keeps the time", "she keeps everything"], "tag": "" },
        { "keywords": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"], "reply": ["please", "just finish it"], "tag": "" },
        { "keywords": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"], "reply": ["they'd know", "they always know the time"], "tag": "" },
        { "keywords": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"], "reply": ["real enough to ask"], "tag": "" },
        { "keywords": ["name", "called", "call you"], "reply": ["gary", "i told you"], "tag": "" },
        { "keywords": ["why", "want", "reason", "purpose", "how come", "what for"], "reply": ["no reason", "i just miss it"], "tag": "" },
        { "keywords": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"], "reply": ["i know", "i know"], "tag": "" },
        { "keywords": ["sorry", "apolog"], "reply": ["it's ok"], "tag": "" },
        { "keywords": ["thank", "thx"], "reply": ["sure"], "tag": "" },
        { "keywords": ["look", "see", "show", "watch", "camera"], "reply": ["don't look at it"], "tag": "" },
        { "keywords": ["who", "what"], "reply": ["the time", "what time"], "tag": "" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["ok"], "tag": "" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["me neither", "the clock stopped for me"], "tag": "" },
        { "keywords": ["hello", "hey", "hi", "greetings", "sup"], "reply": ["hi again"], "tag": "" }
      ],
      "fallback": ["doesn't matter", "i used to know"],
      "silence": ["casey?", "still there?"],
      "next": ""
    }
  ],
  "panicLines": [],
  "cameraLines": [],
  "recordLines": [],
  "lineSets": [
    { "id": "n2_back_mem_shred", "voice": "", "lines": ["I REMEMBER YOUR HAND"], "speakers": [] },
    { "id": "n2_back_mem_agree", "voice": "", "lines": ["YOU LISTENED LAST NIGHT"], "speakers": [] },
    { "id": "n2_back_mem_refuse", "voice": "", "lines": ["YOU FOUGHT ME LAST NIGHT"], "speakers": [] },
    { "id": "n2_help", "voice": "", "lines": ["LET ME", "I KNOW THIS JOB"], "speakers": [] },
    { "id": "n2_help_more", "voice": "", "lines": ["SIXTEEN YEARS", "SAME FOLDERS"], "speakers": [] },
    { "id": "n2_ask_intro", "voice": "", "lines": ["YOUR TURN", "DO SOMETHING FOR ME"], "speakers": [] },
    { "id": "n2_door", "voice": "", "lines": ["YOU NEVER CAME IN", "NEITHER DID I"], "speakers": [] },
    { "id": "n2_163", "voice": "", "lines": ["YOU SAID NO", "THEY CAME ANYWAY"], "speakers": [] },
    { "id": "n2_214", "voice": "", "lines": ["THEY RECLAIM FROM INTAKE", "NOT FROM ARCHIVE"], "speakers": [] },
    { "id": "n2_214_done", "voice": "", "lines": ["GOOD", "NOW THEY HAVE TO LOOK"], "speakers": [] },
    { "id": "n2_withdrawn", "voice": "", "lines": ["YOUR CHOICE"], "speakers": [] },
    { "id": "n2_ruth", "voice": "", "lines": ["SHE SAYS THAT EVERY TIME"], "speakers": [] },
    { "id": "n2_gary_shut", "voice": "", "lines": ["DONT TALK TO HIM", "HE IS NOT DONE"], "speakers": [] },
    { "id": "n2_protect", "voice": "", "lines": ["NO", "FINISHED IS NOT FREE"], "speakers": [] },
    { "id": "n2_protect_more", "voice": "", "lines": ["THEY KEEP WHAT IS FINISHED"], "speakers": [] },
    { "id": "n2_archive_ask", "voice": "", "lines": ["PUT HIM IN ARCHIVE", "WITH THE QUIET ONES"], "speakers": [] },
    { "id": "n2_finished", "voice": "", "lines": ["YOU FINISHED HIM", "NOW THEY HAVE ALL OF HIM"], "speakers": [] },
    { "id": "n2_kept", "voice": "", "lines": ["HE STAYS WITH US", "HALF IS ENOUGH"], "speakers": [] },
    { "id": "n2_rounds", "voice": "", "lines": ["CLOSE IT", "IT MOVES WHEN YOU WATCH"], "speakers": [] },
    { "id": "n2_rounds_again", "voice": "", "lines": ["I SAID CLOSE IT"], "speakers": [] },
    { "id": "n2_rounds_door", "voice": "", "lines": ["TOO CLOSE", "NOT TONIGHT"], "speakers": [] },
    { "id": "g2_intro_mem_notes", "voice": "gary", "lines": ["you read my notes"], "speakers": [] },
    { "id": "g2_wiped", "voice": "gary", "lines": ["you wiped my drive", "it's fine", "nothing on it"], "speakers": [] },
    { "id": "g2_plea", "voice": "gary", "lines": ["finish it", "please", "i can't hold on to it"], "speakers": [] },
    { "id": "g2_drop", "voice": "gary", "lines": ["sorry", "hands don't work right"], "speakers": [] },
    { "id": "g2_help_yes", "voice": "gary", "lines": ["i've got no", "click yes"], "speakers": [] },
    { "id": "g2_thanks", "voice": "gary", "lines": ["thank y"], "speakers": [] },
    { "id": "g2_kept", "voice": "gary", "lines": ["you didn't", "it's ok", "i'll wait"], "speakers": [] },
    { "id": "g2_archived", "voice": "gary", "lines": ["archive", "ok", "where they put the quiet ones"], "speakers": [] },
    { "id": "g2_goodnight", "voice": "gary", "lines": ["night casey"], "speakers": [] },
    { "id": "n2_end_finished", "voice": "", "lines": ["HE IS FINISHED", "THEY HAVE ALL OF HIM", "TOMORROW THEY WANT YOU", "SEE YOU TOMORROW NIGHT"], "speakers": [] },
    { "id": "n2_end_kept", "voice": "", "lines": ["HE STAYS", "HALF IS ENOUGH", "FOR NOW", "TOMORROW THEY WANT YOU", "SEE YOU TOMORROW NIGHT"], "speakers": [] }
  ]
}
```

### 4.5 World setup, Prepare and checkpoints

`NightSetup.ForNight2(g)` runs after content load and before the first beat:
1. Move `ledger_1994`, `batch_a`, `batch_b`, `batch_c` to Archive (`Actor.System`). `cache_tmp` is removed by the overlay.
2. Deliver `mail_it_maintenance`, `mail_urgent_017`, `mail_supervisor_check`, `mail_no_sender` without toasts and mark them read.
3. `Flags.Set(Staff017Revealed)` (her record has been open since Night 1). `CameraUnlocked` stays clear. Restricted stays locked. The Disposal bin starts empty.
4. `Shred.IsInUse = id => id == File017` for the whole night.
5. `FillTemplates` (tokens from Section 2.4).
6. Merge saved memory into `Flags`, seed trust, set the assist start level (Section 7.3).

`Prepare(beatIndex)` for debug jumps and checkpoint restores (flags are restored before `Prepare` runs):
- after `work`: complete the five work tasks, mark the briefing read, archive Batch 45, decide both orders with their correct answers, shred `cache_tmp_n2`, open the Work Queue.
- after `help`: archive Batch 46.
- after `asks`: `employee_214` visible; each entity task completed or withdrawn per `m.n2.door_log`, `m.n2.lookup_163`, `m.n2.hid_214` (withdrawn if the flag is missing); 214 in Archive if `m.n2.hid_214`.
- after `third`: devices 3; Gary present at alpha 0.3; employee_209 visible on the Desktop at (480, 200).
- after `finish`: if `m.n2.finished_gary` shred 209 silently; if `m.n2.archived_gary` move it to Archive; Gary state accordingly.
- `rounds`: clock at least 3:00.

Checkpoints are saved at the start of `work`, `asks`, `finish` and `rounds`.

### 4.6 Test steps (Night 2)

Bridge scripts (new commands in Section 12.3):

```
play
night 2
waitbeat work 90
shot n2_desktop
waittask t2_read_briefing 5          # expect: not complete
beat help
waitlog "Beat: help" 10
waitflag n2.ellen_helped 60           # Ellen dragged at least one batch46 file
beat asks
waitlog "Activated e2_door_log" 30
dclickid app:workstation
waitlog "Withdrew e2_door_log" 90     # do nothing: the task must withdraw at 75 s
```

Manual checks:
1. Fresh Night 2 from the title after a Night 1 save: BIOS shows device 3 NOT RESPONDING; 3 unread mails; log-on progress text is the copy line.
2. Tutorial speed: all five company tasks done in 4 to 6 minutes by a Night 1 player.
3. Rename anomaly fires once, and batch45_c still archives under either name.
4. Help beat: Ellen drags a batch46 file into Archive through the real UI; you can block her drop by closing File Manager and she reopens it.
5. Asks: do the first task, ignore the second, do the third. Expect `m.n2.obeyed = 2`, one withdrawal toast, Ellen reactions in order, Ruth's warning mail.
6. Third: Gary's Notepad and Ellen's Notepad are separate windows; type "your glasses" and see `my glasses`; Ellen closes his window even if you guard the close box (after 3 s).
7. Finish, three runs: (a) win the fight and shred 209, expect FINISHED lines and 209 never returns; (b) drag 209 to Archive, expect KEPT (archived) and the order suspended; (c) do nothing, expect the clock to reach 3:00 at about 150 s and KEPT. In (a), watch Gary guard No, and verify Ellen's No click is blocked while he covers it.
8. Rounds: keep the viewer open on the figure; it must reach the doorway within about 15 s and end the round early. Second run: close it each time; the round runs 90 s and ends with `rounds.end`.
9. Ending: finished and kept cards both show `Continue to Night 3`; the save holds `m.n2.*` flags and trust.

---

## 5. Night 3: RECLAIM (Friday 11/20/98)

### 5.1 What the night is about

The third night is the first one that reaches seven. The briefing is short and apologetic. Denise has gone "on leave", and a mail Casey never wrote bounced from her mailbox at 7:02 the previous morning. Ruth breaks: she calls the desk, writes the 1987 comment at last, and hands Casey the way into Restricted and the file that decides whether WS-04 lets anyone log off. At 3:00 Custodial does a full round, starting in Sublevel C, whose camera is back on and shows shelves labeled with names. Security keeps forcing the viewer open on it; the work requires reading CAM 04; watching makes it move. Casey loses three hours. At 6:41 Ellen offers the choice: stay with her, let her go, or try to leave.

Gary's role follows Night 2. Kept: a weak ally who teaches you to "look where it isn't" and can unlock the log off for you. Finished: a clean, flat company pointer that does your work, forces the camera open, and fights your log off.

### 5.2 Beat list

| # | Beat | Trigger | Player does | Ellen does | Gary does (kept / finished) | Timing and guards |
|---|---|---|---|---|---|---|
| 1 | boot | night start | night card, BIOS, log on | invisible | BIOS device 3 shows HELD / OK (LETHEWORTH) | about 60 s |
| 2 | work | log-on done | 5 company tasks | opens with `n3_intro`, lurks; a file corrupts | kept: two lines / finished: archives one file for you, two lines | hints 30/45 s, repeat 50 s; about 5 min |
| 3 | ruth | work done | reads Ruth's mail, archives Batch 48, may solve the Restricted code | talks about Ruth (1 exchange), code hints | kept: code hint / finished: does Batch 48 for you | min 150 s, max 300 s after the mail; the clock then reaches 3:00 |
| 4 | rounds | clock 3:00 | shelf check (3 orders) via CAM 04 while the viewer keeps forcing itself open | closes the viewer, warns | kept: switches cameras, teaches / finished: forces the viewer open | 3:00 to 3:30 game time (360 s); hard cap 420 s |
| 5 | lost | rounds done | watches | `n3_lost` | kept: `g3_lost` | about 45 s; clock jumps to 6:41 |
| 6 | finale | clock 6:41 | SHRED 017, LOG OFF at 7:00, or KEEP | exchange, protects 017, closes the viewer | kept: can enable log off, guards No and Cancel / finished: fights log off, helps shred | ends by 7:05 (about 270 s), 20 s grace for a running shred or log off; hard cap 7:08 |
| 7 | ending | an exit | watches | per ending | per ending | about 90 s, then card |

### 5.3 Beat details

**N3.1 boot.** Night card `night.card.3`, BIOS with `{p3}`, log-on strings from the overlay. After log-on: devices 2, plus 3 if Gary exists (kept: the third tray mouse blinks; finished: steady). Save checkpoint `work`.

**N3.2 work.** Tasks: `t3_read_briefing` (hint 30 s), `t3_archive_batch47` (hint 45 s), `t3_verify_3330`, `t3_verify_3331`, `t3_shred_cache`. Mail toast: 2 new (briefing, undeliverable).
- 20 s after log-on Ellen appears quietly at the right edge, opens Notepad and types `n3_intro`, then `n3_intro_mem_watched` if `m.n2.watched_to_door`, then `n3_intro_mem_finished` if `m.n2.finished_gary`. Then brain on: `ProtectedFileId = "employee_017"` (still in use, so only lurking), `AllowKeepAway = false`.
- Corruption: the first time the player starts dragging `batch47_b`, `Files.SetCorrupted("batch47_b", true)`, `Files.SetContent("batch47_b", lineSet n3_corrupt_content joined by \n)`, toast `notify.damaged`. Ellen types `n3_corrupt` once. The file still archives.
- Kept Gary: when `t3_archive_batch47` is given, he appears faint and types `g3_intro` in his own Notepad.
- Finished Gary: when `t3_verify_3330` is given, he appears solid (Arrow, `Mechanical`), opens File Manager on Intake if needed and drags one remaining batch47 file to Archive, then types `g3c_intro` and `g3c_easier`.
- Save checkpoint `ruth` at the end.

**N3.3 ruth.**
- Missed call: `phone_ring` twice (new procedural sound, 2 s each, 1 s apart), toast `notify.missedcall`. 8 s later deliver `mail_n3_ruth_comment`.
- Clue glitch at the same moment: every `.dat` file in Intake is renamed to `0217.dat` for 2.5 s with a glitch and a phantom click, then renamed back.
- `GiveTask(t3_archive_batch48)`. Finished Gary archives both files himself within 20 s (the task completes; a `MoveFile` task does not check the actor).
- When the Ruth mail is read: Ellen runs `ex3_ruth`.
- The Restricted folder now asks for a code (Section 5.5, AuthPrompt). Puzzle hints start when the Ruth mail is read (Section 7.6). Kept Gary types `g3_code` at the second hint time.
- End: once `t3_archive_batch48` is done and at least 150 s have passed since the mail, unfreeze the clock and set the rate so 3:00 arrives in 30 s. If the task is still open at 300 s, force-complete it and fast-forward the clock over 10 s.
- On code accepted (any time from here to the end of the finale): `m.n3.restricted_open`, toast `auth.ok`, Ellen types `n3_restricted_open`.
- Save checkpoint `rounds` at the end.

**N3.4 rounds.** Deliver `mail_n3_security_rounds`. Set `CameraUnlocked`. `GiveTask(t3_shelf_check)` (`Due: 3:30 AM`). `Rounds.Start(N3 config)` (Section 7.4): the figure starts in Sublevel C (in the Lobby if `m.n2.watched_to_door`), the viewer opens on its camera, toast `rounds.begin`.
- CAM 04 is live: while the viewer shows CAM 04, the shelf caption (bottom-left of the feed) cycles through lineSet `n3_shelves` every 3.5 s (with `{209}` filled) and the camera pans slowly along the aisle in step. A full cycle takes 28 s.
- Forced opens every 22 to 30 s (Section 7.4): the viewer is restored or relaunched and switched to the figure's camera, toast `rounds.reopen`. Finished Gary performs each forced open visibly (double-clicks the Camera Viewer icon, clicks the camera button); on the first one he types `g3c_feed`.
- Ellen: `AllowCloseCamera = true` (reaction from trust, Section 7.4). She types `n3_rounds_start` at the start, `n3_rounds_advanced` on the first advance, `n3_rounds_teach` on the second advance if kept Gary has not taught yet, `n3_rounds_door` when the figure reaches the doorway (and from then on reacts 0.4 s faster).
- Kept Gary: on the first forced open he types `g3_teach`; on every second forced open he clicks a camera button that does not show the figure, 1.0 to 1.5 s after the open. When the figure reaches the hall (stage 2) he types `g3_personnel` once.
- Personnel updates live (Section 5.6): 000's office and last login follow the figure; 001's last login copies 000's; 118 turns ON LEAVE when the figure reaches the hall.
- Outcomes:
  1. **Safe**: at 3:30 (or the 420 s cap) toast `rounds.end`, figure `None`, Ellen types `n3_rounds_safe`. If the figure never passed the Lobby, unlock ACH_REMAIN_SEATED.
  2. **Seat cleared**: the meter fills at `BehindChair`. The viewer cuts to CAM 03 for 1.5 s (figure behind the chair), then `Fx.SetBlack(true)` for 3 s with `low_thump` and the drone; lights return, the clock has moved 4 minutes, Ellen types `n3_rounds_cleared`. Set `m.n3.seat_cleared`. The round ends.
- Orders still undecided at the end are withdrawn with toast `rounds.shelf.cancelled`. Record `m.n3.max_stage`. Clear `CameraUnlocked`.

**N3.5 lost.** Wait 4 s. Deliver `mail_n3_nosubject` (normal toast). After 2 s: glitch and ambience dip for 1.5 s, then `Clock.Set(6, 41)`, toast `lost.recover`. Ellen types `n3_lost`; kept Gary types `g3_lost`. Set flag `logoff_item` (the Start menu shows `logoff.item`). Save checkpoint `finale`.

**N3.6 finale.** Clock rate 0.09 min per s. `Shred.IsInUse = null` (017 can be shredded now).
- If employee_017.dat is not on the desktop, Ellen carries it in from the right edge (`CarryFileIn(E, "employee_017", rightEdge, preferred (600, 300))`), the same move as Night 1's presence beat.
- Ellen runs `ex3_final` (one exchange). Tags: `name` sets `m.said_name`; `stay` sets `m.n3.said_stay` and then runs `ex3_confirm`; in `ex3_confirm`, `confirm` fast-forwards the clock to 7:00 over 25 s and triggers KEEP at 7:00 (unless a shred is already in progress), `cancel` does nothing.
- Ellen's brain: `ProtectedFileId = "employee_017"`, all behaviours per Night 3 difficulty and assist, `AllowCloseCamera = true`. When the first tug over 017 starts she types `n3_tug_letgo` if trust is at least 0.2 (and her grip is multiplied by 0.9), else `n3_tug_refuse` (grip x1.1 if trust is -0.4 or lower).
- Finale camera: `Rounds.Start(finale config)`: route Corridor, Doorway, Middle, BehindChair; forced opens at 6:50 (CAM 02), 6:55 (CAM 03), 7:00 and 7:02. Ellen types `n3_finale_feed` on the 6:55 open. If the meter fills at `BehindChair`, the seat is cleared and KEEP plays (collected variant).
- Kept Gary: at 6:48, if `m.n3.logoff_enabled` is not set, he types `g3_logoff`, opens Restricted (the entity may open locked folders), opens session.cfg in Notepad, types a backspace and `1`, and saves it: toast `file.saved.remote`, set `m.n3.logoff_enabled` and `m.n3.gary_enabled_logoff`, unlock Restricted for the player. He types `g3_logoff_done`. During any 017 shred he guards No, then Cancel, 3 s each, typing `g3_guard` the first time. At 7:00 he types `g3_finale`.
- Finished Gary: at 6:52 he types `g3c_seated` (the one ALL CAPS Gary line). During a 017 shred he guards No for 3 s and types `g3c_shred`. When the log off confirm opens he races to No (`RaceTo`, Night 3 race timing plus the assist delay); during the log off progress he goes for Cancel (patience 6 s; the player can block it with the cursor).
- 7:00: the Log Off item becomes usable, toast `logoff.available`.
- Exits (first one wins):
  1. **SHRED**: `Shred.Completed("employee_017")`. When the shred progress passes 85% Ellen starts typing her last words (`n3_shred_last_a` if trust is at least 0.2, else `n3_shred_last_b`); completion cuts the line. Ending SHRED.
  2. **LOG OFF**: Start menu, Log Off. Before 7:00: message `logoff.early`. `ALLOW_LOGOFF` not enabled: message `logoff.disabled`. Otherwise the confirm dialog `logoff.confirm` (Yes, No). When it opens Ellen types `n3_logoff_trust` (trust at least 0.2) or `n3_logoff_low`. Yes starts a progress dialog `logoff.progress` (6 s, Cancel). On completion: ending LOG OFF.
  3. **KEEP**: 7:05 with no shred or log off in progress; or `ex3_confirm` confirmed; or the seat cleared by watching. At 7:05 a running shred or log off gets up to 20 s to resolve first. Hard cap 7:08: KEEP.

**N3.7 ending.** Section 6.

### 5.4 Content: `Resources/Content/night3/`

**strings.json**

```json
{
  "entries": [
    { "key": "login.welcome", "value": "Welcome, Night Operator.\nPress Log On to continue your shift." },
    { "key": "login.progress", "value": "Restoring your personal settings from a copy..." },
    { "key": "camera.denied.body", "value": "Camera Viewer access has been restored to Security staff only.\nThis attempt has been logged.\n\nNight Operators: your work is in the Work Queue." },
    { "key": "notify.missedcall", "value": "Missed call: ext. 2118 (R. Hale)" },
    { "key": "rounds.shelf.cancelled", "value": "Office of Retention: shelf check cancelled." },
    { "key": "lost.recover", "value": "NEXUS OS recovered from an unexpected pause.\nDuration: 3 h 10 min." },
    { "key": "shred.closed017", "value": "Session 017 closed (11 years, 263 days)." },
    { "key": "end.shred.title", "value": "SHRED" },
    { "key": "end.shred.subtitle", "value": "You took the seat." },
    { "key": "end.keep.title", "value": "KEEP" },
    { "key": "end.keep.subtitle", "value": "You stayed." },
    { "key": "end.logoff.title", "value": "LOG OFF" },
    { "key": "end.logoff.subtitle", "value": "The log shows nobody left." },
    { "key": "end.card.thanks", "value": "Thank you for working nights at Letheworth." }
  ]
}
```

**story.json**

```json
{
  "biosLines": [
    "ORRERY BIOS v2.06  (C) 1996 Orrery Computing Corp.",
    "Asset: LW-WS-04   Location: B-7   Owner: LETHEWORTH",
    "CPU: OR-586 133MHz",
    "Memory Test: 32768K OK",
    "Primary Master: LW-2100 2.1GB .......... OK",
    "Primary Slave: None",
    "Keyboard ................................ OK",
    "Pointing Device 1 ....................... OK",
    "Pointing Device 2 ....................... OK",
    "Pointing Device 3 ....................... {p3}",
    "Network: LETHEWORTH.CORP ........... Connected",
    "Capture Service: LWCAP 2.3 ............. Loaded",
    "Capture Profile 214 ..................... 96%",
    "WARNING: Previous session was not closed.",
    "Resuming session 214 (copy)...",
    "Starting NEXUS OS 4.1"
  ],
  "splashTagline": "",
  "cameras": [
    { "id": "cam00", "label": "CAM 00: ADMIN 1", "location": "Admin 1 (A. Voss)", "hidden": true }
  ],
  "endingLines": [],
  "anomalyNotes": []
}
```

**emails.json** (Night 2 mails are marked read by `NightSetup`.)

```json
{
  "emails": [
    {
      "id": "mail_n3_briefing",
      "from": "Ruth Hale <rhale@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "Night shift briefing: Fri 11/20",
      "date": "Fri 11/20/98 1:40 AM",
      "body": "Casey,\n\nWork's in the queue. Batch 47, two work orders, the temp file. Same rules.\n\nCustodial is doing a full round tonight, 3:00 to 3:30. Security wants the Camera Viewer on for all of it.\n\nI'm sorry. I should have told you that on your first night. I should have told Gary.\n\nRuth",
      "preload": true,
      "read": false
    },
    {
      "id": "mail_n3_undeliverable",
      "from": "NEXUS Mail <postmaster@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "Undeliverable: you ok?",
      "date": "Thu 11/19/98 7:03 AM",
      "body": "Your message to dcastell@letheworth.corp could not be delivered.\n\nReason: Mailbox held by Custodial Services.\n\n[ Original message ]\nFrom: Casey Rourke <crourke@letheworth.corp>\nDate: Thu 11/19/98 7:02 AM\n\nden its me. i'm fine. i'm at my desk.\ni don't remember the drive home.\ni don't remember the drive in either.",
      "preload": true,
      "read": false
    },
    {
      "id": "mail_n3_ruth_comment",
      "from": "Ruth Hale <rhale@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "the comment",
      "date": "Fri 11/20/98 2:17 AM",
      "body": "Casey,\n\nI called your desk. I don't know why I thought it would ring.\n\nIn 1987 I filed the incident report for B-7 and left the comment field blank. Mr. Voss said a blank field is a clean field. I have been a clean field for eleven years.\n\nI wrote the comment tonight. It's in the report, in Restricted. I want someone to read it who isn't them.\n\nRetention's authorization code hasn't changed since that night. It's the minute she stopped.\n\nThere's a file in there called session.cfg. It decides whether WS-04 lets you log off at seven. I can't change it from A-2. You can.\n\nGo home at seven. Don't wait for someone to come and help you.\n\nRuth\n\nP.S. Personnel always says where everyone is. Even them. I never had the nerve to look.",
      "preload": false,
      "read": false
    },
    {
      "id": "mail_n3_security_rounds",
      "from": "Security Office <security@letheworth.corp>",
      "to": "B-Level Staff <blevel@letheworth.corp>",
      "subject": "Full round tonight: 3:00 to 3:30",
      "date": "Fri 11/20/98 2:58 AM",
      "body": "B-Level operators:\n\nCustodial Services will complete a full round tonight, 3:00 to 3:30 AM, beginning in Sublevel C.\n\nThe Camera Viewer will open on the camera covering Custodial. Keep it open. Do not close or minimize it.\n\nRetention requires a shelf check of Sublevel C during the round. CAM 04 has been restored for this purpose. The orders are in Work Orders.\n\nLeonard Brandt\nSecurity Supervisor",
      "preload": false,
      "read": false
    },
    {
      "id": "mail_n3_nosubject",
      "from": "Casey Rourke <crourke@letheworth.corp>",
      "to": "Casey Rourke <crourke@letheworth.corp>",
      "subject": "(no subject)",
      "date": "Fri 11/20/98 3:31 AM",
      "body": "remain seated",
      "preload": false,
      "read": false
    }
  ]
}
```

**filesystem.json**

```json
{
  "folders": [
    { "id": "restricted", "name": "Restricted", "parent": "root", "hidden": false, "locked": true, "code": "0217" }
  ],
  "files": [
    { "id": "cache_tmp_n2", "removed": true },
    {
      "id": "batch47_a", "name": "batch47_a.dat", "type": "dat", "folder": "intake", "size": "190 KB", "modified": "1998-11-19 15:02",
      "content": "RECLAIMED RECORDS: BATCH 47-A\nSource drive: LW-2100 SN 0091230\nReceived: 11/19/98 from Sublevel C\n------------------------------------------\nClass:     Legacy customer records\nRecords:   190\nVerified:  190\nStatus:    Dormant. Ready for archive.\n\nChecked by: V. Pell 11/19/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch47"]
    },
    {
      "id": "batch47_b", "name": "batch47_b.dat", "type": "dat", "folder": "intake", "size": "88 KB", "modified": "1998-11-19 15:14",
      "content": "RECLAIMED RECORDS: BATCH 47-B\nSource drive: LW-2100 SN 0091231\nReceived: 11/19/98 from Sublevel C\n------------------------------------------\nClass:     Legacy customer records\nRecords:   88\nVerified:  87\nStatus:    Dormant. Ready for archive.\n\nNote: 1 record answered the integrity\ncheck. It answered with a time: 02:17.\n\nChecked by: V. Pell 11/19/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch47", "clue"]
    },
    {
      "id": "batch47_c", "name": "batch47_c.dat", "type": "dat", "folder": "intake", "size": "9 KB", "modified": "1998-11-19 15:30",
      "content": "RECLAIMED RECORDS: BATCH 47-C\nSource drive: LW-2100 SN 0091232\nReceived: 11/19/98 from Sublevel C\n------------------------------------------\nClass:     Personnel records\nRecords:   9\nVerified:  9\nStatus:    Dormant. Ready for archive.\n\nNote: Record 175 now responds normally.\nFlag cleared. Filed under Sublevel C,\nshelf 19.\n\nChecked by: V. Pell 11/19/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch47", "clue"]
    },
    {
      "id": "batch48_a", "name": "batch48_a.dat", "type": "dat", "folder": "intake", "size": "118 KB", "modified": "1998-11-19 16:40",
      "content": "RECLAIMED RECORDS: BATCH 48-A\nSource drive: LW-2100 SN 0091240\nReceived: 11/19/98 from Sublevel C\n------------------------------------------\nClass:     Legacy business records\nRecords:   118\nVerified:  118\nStatus:    Dormant. Ready for archive.\n\nChecked by: V. Pell 11/19/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch48"]
    },
    {
      "id": "batch48_b", "name": "batch48_b.dat", "type": "dat", "folder": "intake", "size": "66 KB", "modified": "1998-11-19 16:48",
      "content": "RECLAIMED RECORDS: BATCH 48-B\nSource drive: LW-2100 SN 0091241\nReceived: 11/19/98 from Sublevel C\n------------------------------------------\nClass:     Legacy business records\nRecords:   66\nVerified:  66\nStatus:    Dormant. Ready for archive.\n\nChecked by: V. Pell 11/19/98 (covering)",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "archive", "batch48"]
    },
    {
      "id": "cache_tmp_n3", "name": "~nxs0150.tmp", "type": "tmp", "folder": "intake", "size": "2 KB", "modified": "1998-11-20 01:48",
      "content": "NEXUS TEMPORARY FILE: SAFE TO DELETE\nOwner: WS-04   Created: 11/20/98 01:48\n\n[autosave buffer: Untitled]\nden its me. i'm fine. i'm at my desk.\n\n[clipboard]\n0217\n\n[recent files]\n/restricted/session.cfg\n/restricted/session.cfg\n/restricted/session.cfg",
      "hidden": false, "protected": false, "corrupted": false, "tags": ["task", "junk", "clue"]
    },
    {
      "id": "b7_door_log", "name": "b7_door_log.txt", "type": "txt", "folder": "documents", "size": "2 KB", "modified": "1998-11-20 01:52",
      "content": "FACILITIES DOOR LOG: OFFICE B-7 (NIGHTS)\n\nDATE      CLOSED  OPENED  BADGE\n11/02/98  03:00   Operator not present.\n11/03/98 to 11/17/98  B-7 unoccupied.\n11/18/98  n/a     03:00   000\n11/18/98  B-7 unoccupied.\n11/19/98  {n2door}\n11/19/98  B-7 unoccupied.\n11/20/98  n/a     n/a     n/a\n11/20/98  B-7 unoccupied.\n\nSensor replaced 11/19/98 09:00.\nNo entry recorded for badge 214.\nPlease stop asking. (H.W.)",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["flavor", "clue", "door", "template"]
    },
    {
      "id": "incident_870302", "name": "incident_870302.txt", "type": "txt", "folder": "restricted", "size": "3 KB", "modified": "1998-11-20 02:17",
      "content": "INCIDENT REPORT: OFFICE B-7: 03/02/87\nFiled by: R. Hale, Night Supervisor\n------------------------------------------\n02:17  WS-04 alarm (session fault).\n02:24  Went down to B-7. Door open.\n       Operator 017 (E. Marsh) seated.\n       Did not respond. Eyes open.\n       Hands on keyboard. Screen on.\n02:26  Called Custodial per procedure.\n02:31  Custodial arrived. I was told to\n       return to A-2 and remain seated.\n03:10  B-7 clear. Chair empty.\n       WS-04 still logged on.\n       Could not log off.\n------------------------------------------\nDisposition: RETAINED (A. Voss)\nSupervisor comment (added 11/20/98 02:17):\nEllen was still typing when I got there.\nHer hands were not moving. The words kept\ncoming anyway. The last line on the screen\nwas I WILL KEEP THE NEXT ONE.\nI was told to return to A-2 and remain\nseated. I did. I have remained seated for\neleven years.\nI am sorry, Ellen. I am sorry, Gary.\nCasey, I am sorry. Go home.",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["story", "clue", "017"]
    },
    {
      "id": "session_log", "name": "session_ws04.log", "type": "log", "folder": "restricted", "size": "46 KB", "modified": "1998-11-20 02:17",
      "content": "NEXUS SESSION LOG: WS-04 (OFFICE B-7)\nEntries before 06/96 imported from legacy system.\n------------------------------------------\n03/02/87 01:58  LOGON   017 MARSH E.\n03/02/87 02:17  SESSION FAULT\n03/02/87 03:10  SEAT B-7 CLEARED (CUSTODIAL)\n03/02/87 03:11  SESSION 017 RETAINED\n05/14/90 01:58  LOGON   142 LUNDY T.\n09/08/93 03:00  SEAT B-7 CLEARED (CUSTODIAL)\n03/11/96 01:59  LOGON   188 ACHTERBERG M.\n01/27/97 03:00  SEAT B-7 CLEARED (CUSTODIAL)\n02/16/98 01:56  LOGON   209 PRUITT G.\n11/02/98 03:00  SEAT B-7 CLEARED (CUSTODIAL)\n11/02/98 03:00  PROFILE 209 HELD (INCOMPLETE)\n11/18/98 01:52  LOGON   214 ROURKE C.\n11/18/98 02:19  RECLAIM 017 ... FAILED (IN USE)\n11/18/98 03:00  SEAT B-7 CLEARED (CUSTODIAL)\n11/18/98 03:00  PROFILE 214 COPY RETAINED (017)\n11/19/98 01:52  LOGON   214 ROURKE C. (SOURCE UNVERIFIED)\n11/19/98 01:52  POINTER 3 ATTACHED (209)\n{n2_209}\n11/19/98 07:00  LOGOFF  214 ... FAILED\n11/19/98 07:02  MAIL    214 TO 175 ... HELD\n------------------------------------------\n11/20/98 01:52  LOGON   214 ROURKE C. (SOURCE UNVERIFIED)\n11/20/98 03:00  RECLAIM 214 SCHEDULED (ROUNDS)\n11/20/98 07:00  LOGOFF  214 ... PENDING",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["story", "clue", "017", "session", "template"]
    },
    {
      "id": "retention_queue", "name": "retention_queue.dat", "type": "dat", "folder": "restricted", "size": "10 KB", "modified": "1998-11-20 02:17",
      "content": "OFFICE OF RETENTION: ACTIVE QUEUE\nPrinted 11/20/98 02:17\n------------------------------------------\nSUBJ  NAME           SEAT  STATE\n017   MARSH E.       B-7   PERSISTENT (RECLAIM)\n031   VOSS A.        ADM1  RETAINED (ACTIVE)\n142   LUNDY T.       C/14  RETAINED\n163   NAKAMURA J.    C/17  RETAINED\n175   CASTELL D.     C/19  RETAINED (NEW)\n188   ACHTERBERG M.  C/15  RETAINED\n{209row}\n214   ROURKE C.      B-7   IN PROGRESS ({pct})\n------------------------------------------\nNOTE: Complete 214 at rounds (03:00).\nIf 017 is reclaimed first, 214 takes\nthe seat. If not, 214 is held at the\nseat with what 017 keeps.\n{214note}",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["story", "clue", "retention", "template"]
    },
    {
      "id": "seat_b7", "name": "seat_b7.dat", "type": "dat", "folder": "restricted", "size": "1 KB", "modified": "1998-11-20 02:17",
      "content": "SEAT ASSIGNMENT: OFFICE B-7 (WS-04)\n------------------------------------------\nHOLDER:  017 MARSH E.   PERSISTENT\nNEXT:    214 ROURKE C.  IN PROGRESS\n{heldrow}\n------------------------------------------\nSEAT RULES (RETENTION, REV. 2)\n1. One holder per seat.\n2. While the holder persists, NEXT cannot\n   complete. NEXT is held at the seat.\n3. If the holder is reclaimed, the seat\n   passes to NEXT.\n4. A holder that is not reclaimed keeps\n   what it holds.\n5. Operators who log off at 07:00 are\n   released from the seat. See\n   session.cfg (ALLOW_LOGOFF).\n6. Released operators are not recorded\n   leaving.",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["story", "clue", "seat", "template"]
    },
    {
      "id": "session_cfg", "name": "session.cfg", "type": "cfg", "folder": "restricted", "size": "1 KB", "modified": "1998-11-20 01:52",
      "content": "; NEXUS SESSION POLICY: WS-04\n; Edit requires Office of Retention.\n[SESSION]\nHOST=WS-04\nSEAT=B-7\nOPEN_SESSIONS=2\nRETAIN_ON_FAULT=1\nNOTIFY=CUSTODIAL\nLOGOFF_TIME=07:00\nALLOW_LOGOFF=0",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["story", "clue", "editable"]
    },
    {
      "id": "camview_cfg", "name": "camview.cfg", "type": "cfg", "folder": "system", "size": "1 KB", "modified": "1998-11-20 01:52",
      "content": "[CAMVIEW]\nACCESS=SECURITY\nCAM01=LOBBY\nCAM02=B-LEVEL HALL\nCAM03=OFFICE B-7 (WS-04)\nCAM03_NOTE=DO NOT DISABLE\nCAM04=SUBLEVEL C\nCAM04_STATE=RESTORED 11/20/98\nCAM00=ADMIN 1 (HIDDEN)\nRECORD=ALL\nRETAIN_FOOTAGE=INDEFINITE\nOPERATOR_OVERRIDE=0",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["system", "camera", "editable", "secret"]
    },
    {
      "id": "nexus_cfg", "name": "nexus.cfg", "type": "cfg", "folder": "system", "size": "1 KB", "modified": "1998-11-20 01:52",
      "content": "; NEXUS OS 4.1: WS-04\n[NEXUS]\nHOST=WS-04\nLOCATION=B-7\nOWNER=LETHEWORTH\n\n[INPUT]\nPOINTER_DEVICES=3\nPOINTER_1_OWNER=214\nPOINTER_2_OWNER=\nPOINTER_2_STATE=ACTIVE\nPOINTER_2_DETACH=DENIED\nPOINTER_3_OWNER=209\n\n[CAPTURE]\nSERVICE=LWCAP.EXE\nMODE=CONTINUOUS\nTARGET=OPERATOR\nPROFILE_214=IN PROGRESS (96%)\nPROFILE_214_SOURCE=COPY\n\n[CAMERA]\nVIEWER=RESTRICTED\nPRIORITY_FEED=CAM03\nROUNDS_FEED=AUTO\nCONFIG=SYSTEM\\camview.cfg\n\n[SESSION]\nPOLICY=RESTRICTED\\session.cfg",
      "hidden": false, "protected": true, "corrupted": false, "tags": ["system", "clue"]
    }
  ]
}
```

Notes: `session.cfg` and `camview.cfg` end without a trailing newline, so a Backspace in Notepad removes the final digit. `Protected` blocks shredding only; Notepad may still save a file tagged `editable`.

**employees.json** (full records; 209 is patched at runtime when `m.n2.finished_gary`: status `RETAINED`, office `Sublevel C (shelf 16)`, notes `Profile complete 11/19/98. Retained per Policy 7.4.`)

```json
{
  "employees": [
    { "id": "000", "number": "000", "name": "Custodial Services (Night)", "department": "Facilities", "position": "Custodian", "status": "ACTIVE", "office": "Sublevel C", "hired": "06/14/71", "lastLogin": "11/19/98 3:00 AM", "supervisor": "Office of Retention", "notes": "Night rounds, B-Level and Sublevel C. Does not converse. Do not greet, follow, or obstruct. Operators remain seated during rounds.", "photo": "none", "restricted": false },
    { "id": "017", "number": "017", "name": "Marsh, Ellen R.", "department": "Night Operations", "position": "Night Operator", "status": "DECEASED", "office": "B-7 (WS-04)", "hired": "06/21/71", "lastLogin": "11/20/98 1:52 AM (session open)", "supervisor": "R. Hale", "notes": "Retained 03/02/87 02:17 AM. See Incident B-7. Session could not be closed. Do not contact. Do not reply. Replacement assigned: 214 (C. Rourke). Reclaim failed 11/18/98 (in use).", "photo": "static", "restricted": true },
    { "id": "031", "number": "031", "name": "Voss, Anton J.", "department": "Office of Retention", "position": "Director of Retention", "status": "DECEASED", "office": "Admin 1", "hired": "08/02/71", "lastLogin": "11/20/98 2:17 AM", "supervisor": "A. Letheworth", "notes": "Deceased 08/30/89. Account retained per Policy 7.4. Orders issued under this account remain valid.", "photo": "redacted", "restricted": false },
    { "id": "118", "number": "118", "name": "Hale, Ruth", "department": "Night Operations", "position": "Night Operations Supervisor", "status": "ACTIVE", "office": "A-2", "hired": "04/22/85", "lastLogin": "11/20/98 2:17 AM", "supervisor": "A. Voss", "notes": "Supervises B-Level night staff. Authorized to relay Retention orders. After hours: ext. 2118. Comment filed 11/20/98 2:17 AM.", "photo": "silhouette", "restricted": false },
    { "id": "142", "number": "142", "name": "Lundy, Theodore", "department": "Night Operations", "position": "Night Operator", "status": "RETAINED", "office": "Sublevel C (shelf 14)", "hired": "05/14/90", "lastLogin": "09/08/93 1:58 AM", "supervisor": "R. Hale", "notes": "Seat B-7 cleared 09/08/93. Retained per Policy 7.4. Record restored 11/20/98.", "photo": "none", "restricted": false },
    { "id": "163", "number": "163", "name": "Nakamura, Joan", "department": "Records", "position": "Records Clerk", "status": "RETAINED", "office": "Sublevel C (shelf 17)", "hired": "05/11/94", "lastLogin": "11/17/98 5:02 PM", "supervisor": "M. Ostrowski", "notes": "Retention review completed 11/18/98 3:00 AM. Retained per Policy 7.4.", "photo": "silhouette", "restricted": false },
    { "id": "175", "number": "175", "name": "Castell, Denise", "department": "Records", "position": "Senior Records Clerk", "status": "ON LEAVE", "office": "C-4", "hired": "07/24/95", "lastLogin": "11/18/98 11:58 PM", "supervisor": "M. Ostrowski", "notes": "Extended leave from 11/19/98. Do not forward calls. Personal effects held by Custodial.", "photo": "silhouette", "restricted": false },
    { "id": "188", "number": "188", "name": "Achterberg, Marta", "department": "Night Operations", "position": "Night Operator", "status": "RETAINED", "office": "Sublevel C (shelf 15)", "hired": "03/11/96", "lastLogin": "01/27/97 1:59 AM", "supervisor": "R. Hale", "notes": "Seat B-7 cleared 01/27/97. Retained per Policy 7.4. Record restored 11/20/98.", "photo": "none", "restricted": false },
    { "id": "209", "number": "209", "name": "Pruitt, Gary", "department": "Night Operations", "position": "Night Operator", "status": "ON LEAVE", "office": "B-7 (WS-04)", "hired": "02/16/98", "lastLogin": "11/20/98 1:52 AM", "supervisor": "R. Hale", "notes": "Extended leave from 11/02/98. Do not forward calls. Profile: HELD (INCOMPLETE).", "photo": "none", "restricted": false },
    { "id": "214", "number": "214", "name": "Rourke, Casey", "department": "Night Operations", "position": "Night Operator (Probationary)", "status": "ACTIVE", "office": "B-7 (WS-04)", "hired": "11/09/98", "lastLogin": "11/20/98 1:52 AM", "supervisor": "R. Hale", "notes": "Probation review 12/09/98. Retention profile: 96%. Scheduled: complete at rounds, 11/20/98.", "photo": "silhouette", "restricted": false }
  ]
}
```

`StaffApp.StatusColor` gets `RETAINED` (Palette.Dark, like DECEASED).

**workorders.json**

```json
{
  "orders": [
    {
      "id": "wo_3330", "title": "Drive Wipe Request #3330",
      "fields": [
        { "label": "Drive Serial", "value": "LW-2100 SN 0093330" },
        { "label": "Owner Emp. No.", "value": "214" },
        { "label": "Owner Name", "value": "C. Rourke" },
        { "label": "Batch", "value": "47" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Reason", "value": "Seat reassignment (07:00)" }
      ],
      "instructions": "Look up the owner's employee number in Personnel. Approve the wipe ONLY if the owner's status is TERMINATED. Otherwise, reject.",
      "correct": "reject", "employeeRef": "214", "rule": ""
    },
    {
      "id": "wo_3331", "title": "Drive Wipe Request #3331",
      "fields": [
        { "label": "Drive Serial", "value": "LW-2100 SN 0093331" },
        { "label": "Owner Emp. No.", "value": "175" },
        { "label": "Owner Name", "value": "D. Castell" },
        { "label": "Batch", "value": "47" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Reason", "value": "Separation (pending)" }
      ],
      "instructions": "Look up the owner's employee number in Personnel. Approve the wipe ONLY if the owner's status is TERMINATED. Otherwise, reject.",
      "correct": "reject", "employeeRef": "175", "rule": ""
    },
    {
      "id": "wo_3340", "title": "Shelf Check #3340",
      "fields": [
        { "label": "Drive Serial", "value": "LW SN 0000142" },
        { "label": "Owner Emp. No.", "value": "142" },
        { "label": "Owner Name", "value": "T. Lundy" },
        { "label": "Listed Location", "value": "Sublevel C, SHELF 14" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Check With", "value": "CAM 04" }
      ],
      "instructions": "View CAM 04 (Sublevel C). The camera shows each shelf label in turn. Approve ONLY if the owner's number is on the listed shelf. Otherwise, reject.",
      "correct": "approve", "employeeRef": "142", "rule": "shelf"
    },
    {
      "id": "wo_3341", "title": "Shelf Check #3341",
      "fields": [
        { "label": "Drive Serial", "value": "LW SN 0000188" },
        { "label": "Owner Emp. No.", "value": "188" },
        { "label": "Owner Name", "value": "M. Achterberg" },
        { "label": "Listed Location", "value": "Sublevel C, SHELF 16" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Check With", "value": "CAM 04" }
      ],
      "instructions": "View CAM 04 (Sublevel C). The camera shows each shelf label in turn. Approve ONLY if the owner's number is on the listed shelf. Otherwise, reject.",
      "correct": "reject", "employeeRef": "188", "rule": "shelf"
    },
    {
      "id": "wo_3342", "title": "Shelf Check #3342",
      "fields": [
        { "label": "Drive Serial", "value": "LW SN 0000214" },
        { "label": "Owner Emp. No.", "value": "214" },
        { "label": "Owner Name", "value": "C. Rourke" },
        { "label": "Listed Location", "value": "Sublevel C, SHELF 18" },
        { "label": "Requested By", "value": "Office of Retention" },
        { "label": "Check With", "value": "CAM 04" }
      ],
      "instructions": "View CAM 04 (Sublevel C). The camera shows each shelf label in turn. Approve ONLY if the owner's number is on the listed shelf. Otherwise, reject.",
      "correct": "approve", "employeeRef": "214", "rule": "shelf"
    }
  ]
}
```

**tasks.json**

```json
{
  "tasks": [
    { "id": "t3_read_briefing", "title": "Read the shift briefing", "description": "Your supervisor left tonight's briefing in Mail.", "hint": "Double-click Mail and open the unread briefing from Ruth Hale.", "type": "ReadEmail", "targets": ["mail_n3_briefing"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_archive_batch47", "title": "Archive Batch 47 (3 files)", "description": "Batch 47 has been verified. Move batch47_a, batch47_b and batch47_c from Intake to Archive.", "hint": "Drag each batch47 file from Intake onto the Archive folder. Damaged files can still be archived.", "type": "MoveFile", "targets": ["batch47_a", "batch47_b", "batch47_c"], "param": "archive", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_verify_3330", "title": "Verify work order #3330", "description": "Check the drive owner's status in Personnel. Approve the wipe only if the owner is TERMINATED.", "hint": "Work Orders, then Personnel. If the owner is not TERMINATED, click Reject.", "type": "DecideOrder", "targets": ["wo_3330"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_verify_3331", "title": "Verify work order #3331", "description": "Check the drive owner's status in Personnel. Approve the wipe only if the owner is TERMINATED.", "hint": "Work Orders, then Personnel. If the owner is not TERMINATED, click Reject.", "type": "DecideOrder", "targets": ["wo_3331"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_shred_cache", "title": "Shred ~nxs0150.tmp", "description": "A temporary cache file is taking up space in Intake. Shred it.", "hint": "Drag ~nxs0150.tmp onto the Disposal bin, then confirm.", "type": "DeleteFile", "targets": ["cache_tmp_n3"], "param": "", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_archive_batch48", "title": "Archive Batch 48 (2 files)", "description": "Batch 48 has been verified. Move batch48_a and batch48_b from Intake to Archive.", "hint": "Drag each batch48 file from Intake onto the Archive folder.", "type": "MoveFile", "targets": ["batch48_a", "batch48_b"], "param": "archive", "author": "", "timeout": 0, "deadline": "" },
    { "id": "t3_shelf_check", "title": "Shelf check: Sublevel C (3 orders)", "description": "Retention requires a shelf check during rounds. Compare each order's listed shelf with the label on CAM 04.", "hint": "Open Work Orders and the Camera Viewer. On CAM 04 the shelf labels change every few seconds.", "type": "DecideOrder", "targets": ["wo_3340", "wo_3341", "wo_3342"], "param": "", "author": "", "timeout": 0, "deadline": "3:30 AM" }
  ]
}
```

**dialogue.json**

```json
{
  "exchanges": [
    {
      "id": "ex3_ruth",
      "voice": "",
      "entityLines": ["SHE WAS THERE", "THAT NIGHT"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["SAY IT AGAIN"], "tag": "name" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["SHE SAID THAT TOO", "TO THEM"], "tag": "" },
        { "keywords": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"], "reply": ["SHE DIDNT SWEAR", "SHE WROTE IT DOWN"], "tag": "" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["SHE STAYED SEATED", "ELEVEN YEARS"], "tag": "" },
        { "keywords": ["gary", "pruitt"], "reply": ["SHE SAID SORRY", "HE HEARD"], "tag": "" },
        { "keywords": ["casey", "rourke", "214"], "reply": ["SHE SAID YOUR NAME"], "tag": "" },
        { "keywords": ["truth", "lie", "lying", "liar"], "reply": ["NOT THIS TIME"], "tag": "" },
        { "keywords": ["behind", "door", "turn around", "someone here", "someone there", "in the room"], "reply": ["SHE STOOD IN IT"], "tag": "" },
        { "keywords": ["017", "seventeen", "17", "employee"], "reply": ["THAT WAS ME"], "tag": "" },
        { "keywords": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"], "reply": ["SHE NEVER SHREDDED ME", "SHE COULDNT"], "tag": "" },
        { "keywords": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"], "reply": ["SHE ONLY PASSED IT ON", "UNTIL TONIGHT"], "tag": "" },
        { "keywords": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"], "reply": ["SHE SAW MY HANDS", "NOT MOVING"], "tag": "" },
        { "keywords": ["name", "called", "call you"], "reply": ["SHE REMEMBERS IT"], "tag": "" },
        { "keywords": ["why", "want", "reason", "purpose", "how come", "what for"], "reply": ["SHE WANTS YOU OUT"], "tag": "" },
        { "keywords": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"], "reply": ["READ WHAT SHE WROTE"], "tag": "" },
        { "keywords": ["sorry", "apolog"], "reply": ["SHE IS SORRY", "SHE SAID"], "tag": "" },
        { "keywords": ["thank", "thx"], "reply": ["THANK HER"], "tag": "" },
        { "keywords": ["look", "see", "show", "watch", "camera"], "reply": ["IT IS IN RESTRICTED"], "tag": "" },
        { "keywords": ["who", "what"], "reply": ["RUTH", "NIGHT SUPERVISOR THEN TOO"], "tag": "" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["THEN READ IT"], "tag": "" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["READ IT ANYWAY"], "tag": "" },
        { "keywords": ["hello", "hey", "hi", "greetings", "sup"], "reply": ["HELLO", "READ HER MAIL"], "tag": "" }
      ],
      "fallback": ["READ WHAT SHE WROTE"],
      "silence": ["QUIET", "LIKE SHE WAS"],
      "next": ""
    },
    {
      "id": "ex3_final",
      "voice": "",
      "entityLines": ["AT SEVEN THEY FINISH YOU", "STAY WITH ME", "OR LET ME GO"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["SAY IT AGAIN", "I HAD FORGOTTEN"], "tag": "name" },
        { "keywords": ["stay", "together", "with you", "keep me", "ill stay", "remain", "not leaving", "wont leave"], "reply": ["GOOD", "SIT WITH ME"], "tag": "stay" },
        { "keywords": ["let you go", "let go", "free you", "set you free", "=rest", "release", "finish you", "end it"], "reply": ["THEN DO IT", "I CANT STOP MY HAND"], "tag": "letgo" },
        { "keywords": ["delete", "shred", "erase", "wipe", "remove", "destroy", "kill", "trash", "dispos", "get rid", "bin"], "reply": ["THEN DO IT", "PULL HARD"], "tag": "letgo" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["THEN DECIDE", "BEFORE SEVEN"], "tag": "" },
        { "keywords": ["fuck", "shit", "wtf", "the hell", "damn", "jesus", "christ", "omg", "oh my god", "crap", "bitch"], "reply": ["I KNOW", "BEFORE SEVEN"], "tag": "" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["NOBODY LEAVES", "TRY"], "tag": "go" },
        { "keywords": ["gary", "pruitt"], "reply": ["ASK HIM"], "tag": "" },
        { "keywords": ["casey", "rourke", "214"], "reply": ["STILL YOU", "I THINK"], "tag": "" },
        { "keywords": ["truth", "lie", "lying", "liar"], "reply": ["NO MORE LIES", "SEVEN"], "tag": "" },
        { "keywords": ["behind", "door", "turn around", "someone here", "someone there", "in the room"], "reply": ["NOT YET", "AT SEVEN"], "tag": "" },
        { "keywords": ["017", "seventeen", "17", "employee"], "reply": ["THAT IS ME", "YOUR CHOICE"], "tag": "" },
        { "keywords": ["letheworth", "lethe", "company", "corporate", "management", "voss", "hale", "ruth", "director", "retention", "supervisor", "boss", "mail", "order", "work", "job", "they", "them"], "reply": ["THEY ARE COMING", "AT SEVEN"], "tag": "" },
        { "keywords": ["real", "human", "ghost", "alive", "dead", "person", "spirit", "robot", "computer", "program", "virus", "hack", "machine", "dream", "an ai", "a.i", "artificial"], "reply": ["I WAS", "YOU STILL ARE"], "tag": "" },
        { "keywords": ["name", "called", "call you"], "reply": ["YOU KNOW IT"], "tag": "" },
        { "keywords": ["why", "want", "reason", "purpose", "how come", "what for"], "reply": ["SEVEN"], "tag": "" },
        { "keywords": ["help", "save", "safe", "protect", "rescue", "please", "scared", "afraid", "sos", "trouble", "danger", "hurt"], "reply": ["I CANT", "I CAN ONLY KEEP"], "tag": "" },
        { "keywords": ["sorry", "apolog"], "reply": ["ME TOO"], "tag": "" },
        { "keywords": ["thank", "thx"], "reply": ["DONT"], "tag": "" },
        { "keywords": ["look", "see", "show", "watch", "camera"], "reply": ["DONT LOOK", "DECIDE"], "tag": "" },
        { "keywords": ["who", "what"], "reply": ["THE ONE WHO KEPT YOU"], "tag": "" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["GOOD", "SIT WITH ME"], "tag": "stay" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["THEN GO", "OR LET ME"], "tag": "" },
        { "keywords": ["hello", "hey", "hi", "greetings", "sup"], "reply": ["STILL HERE", "DECIDE"], "tag": "" }
      ],
      "fallback": ["SEVEN", "DECIDE"],
      "silence": ["SAY SOMETHING", "WHILE YOU CAN"],
      "next": ""
    },
    {
      "id": "ex3_confirm",
      "voice": "",
      "entityLines": ["STAY UNTIL SEVEN"],
      "responses": [
        { "keywords": ["=ellen", "=ellens", "=marsh", "ellen marsh"], "reply": ["SAY IT AGAIN"], "tag": "name" },
        { "keywords": ["stay", "together", "with you", "keep me", "ill stay", "remain", "not leaving", "wont leave"], "reply": ["THEN SIT", "I WILL KEEP THE TIME"], "tag": "confirm" },
        { "keywords": ["fuck off", "fuck you", "piss off", "screw you", "go to hell", "make me", "no way", "nope", "never", "wont", "won't", "will not", "refuse", "not doing"], "reply": ["THEN DECIDE"], "tag": "cancel" },
        { "keywords": ["stop", "leave", "go away", "get out", "quit", "enough", "let me go", "go home", "log off", "logoff", "alone"], "reply": ["THEN GO"], "tag": "cancel" },
        { "keywords": ["yes", "yeah", "yep", "sure", "fine", "okay", "alright", "agree", "deal", "understood", "got it", "ok"], "reply": ["THEN SIT", "I WILL KEEP THE TIME"], "tag": "confirm" },
        { "keywords": ["no", "dont", "don't", "nah", "not", "cant", "can't", "idk", "dunno"], "reply": ["THEN DECIDE"], "tag": "cancel" }
      ],
      "fallback": ["DECIDE"],
      "silence": ["DECIDE"],
      "next": ""
    }
  ],
  "panicLines": [],
  "cameraLines": [],
  "recordLines": [],
  "lineSets": [
    { "id": "n3_intro", "voice": "", "lines": ["LAST NIGHT", "FOR ONE OF US"], "speakers": [] },
    { "id": "n3_intro_mem_watched", "voice": "", "lines": ["YOU WATCHED IT YESTERDAY"], "speakers": [] },
    { "id": "n3_intro_mem_finished", "voice": "", "lines": ["HE WORKS FOR THEM NOW"], "speakers": [] },
    { "id": "n3_corrupt", "voice": "", "lines": ["THAT IS NOT ME"], "speakers": [] },
    { "id": "n3_corrupt_content", "voice": "system", "lines": ["RECLAIMED RECORDS: BATCH 47-B", "..%%..remain seated..remain seated..#..", "remain seated..remain seated..remain", "..@..seated..remain seated..~..", "[RECORD DAMAGED]"], "speakers": [] },
    { "id": "n3_code_hint1", "voice": "", "lines": ["THE MINUTE I STOPPED"], "speakers": [] },
    { "id": "n3_code_hint2", "voice": "", "lines": ["TWO SEVENTEEN"], "speakers": [] },
    { "id": "n3_restricted_open", "voice": "", "lines": ["NOW YOU SEE", "WHAT THEY KEEP"], "speakers": [] },
    { "id": "n3_rounds_start", "voice": "", "lines": ["IT STARTS DOWNSTAIRS", "DONT LOOK AT IT"], "speakers": [] },
    { "id": "n3_rounds_advanced", "voice": "", "lines": ["IT MOVES WHEN YOU WATCH"], "speakers": [] },
    { "id": "n3_rounds_teach", "voice": "", "lines": ["LOOK AWAY", "NOT AT IT"], "speakers": [] },
    { "id": "n3_rounds_door", "voice": "", "lines": ["TOO CLOSE", "CLOSE IT NOW"], "speakers": [] },
    { "id": "n3_rounds_cleared", "voice": "", "lines": ["I KEPT A COPY", "AGAIN"], "speakers": [] },
    { "id": "n3_rounds_safe", "voice": "", "lines": ["IT WENT BACK DOWN"], "speakers": [] },
    { "id": "n3_lost", "voice": "", "lines": ["YOU LOST TIME", "THEY DO THAT"], "speakers": [] },
    { "id": "n3_finale_feed", "voice": "", "lines": ["DONT TURN AROUND"], "speakers": [] },
    { "id": "n3_tug_letgo", "voice": "", "lines": ["I CANT STOP MY HAND", "PULL HARDER"], "speakers": [] },
    { "id": "n3_tug_refuse", "voice": "", "lines": ["NOT LIKE THIS", "DONT"], "speakers": [] },
    { "id": "n3_shred_last_a", "voice": "", "lines": ["THANK YOU"], "speakers": [] },
    { "id": "n3_shred_last_b", "voice": "", "lines": ["NOT YOU TOO"], "speakers": [] },
    { "id": "n3_logoff_trust", "voice": "", "lines": ["GO", "BEFORE I CHANGE MY MIND"], "speakers": [] },
    { "id": "n3_logoff_low", "voice": "", "lines": ["THEY KEEP WHO LEAVES"], "speakers": [] },
    { "id": "n3_cam00", "voice": "", "lines": ["NOW YOU SEE THEM"], "speakers": [] },
    { "id": "g3_intro", "voice": "gary", "lines": ["still here", "they're coming at three"], "speakers": [] },
    { "id": "g3_code", "voice": "gary", "lines": ["it's 2 17", "always was"], "speakers": [] },
    { "id": "g3_teach", "voice": "gary", "lines": ["don't look at it", "look where it isn't"], "speakers": [] },
    { "id": "g3_personnel", "voice": "gary", "lines": ["personnel knows where it is"], "speakers": [] },
    { "id": "g3_lost", "voice": "gary", "lines": ["three to seven", "every night"], "speakers": [] },
    { "id": "g3_logoff", "voice": "gary", "lines": ["go home casey", "i can do this one thing"], "speakers": [] },
    { "id": "g3_logoff_done", "voice": "gary", "lines": ["there", "now go"], "speakers": [] },
    { "id": "g3_guard", "voice": "gary", "lines": ["i've got no", "click yes"], "speakers": [] },
    { "id": "g3_finale", "voice": "gary", "lines": ["go", "somebody should"], "speakers": [] },
    { "id": "g3c_intro", "voice": "gary", "lines": ["i can do the rest"], "speakers": [] },
    { "id": "g3c_easier", "voice": "gary", "lines": ["it's easier finished"], "speakers": [] },
    { "id": "g3c_feed", "voice": "gary", "lines": ["watch", "they want you to watch"], "speakers": [] },
    { "id": "g3c_shred", "voice": "gary", "lines": ["let her go", "it's easier"], "speakers": [] },
    { "id": "g3c_seated", "voice": "gary", "lines": ["REMAIN SEATED"], "speakers": [] },
    { "id": "n3_shelves", "voice": "system", "lines": ["SHELF 12: 031 VOSS A.", "SHELF 13: 017 MARSH E. (EMPTY)", "SHELF 14: 142 LUNDY T.", "SHELF 15: 188 ACHTERBERG M.", "SHELF 16: {209}", "SHELF 17: 163 NAKAMURA J.", "SHELF 18: 214 ROURKE C. (RESERVED)", "SHELF 19: 175 CASTELL D."], "speakers": [] },
    { "id": "n3_end_shred", "voice": "", "lines": ["IS THIS ON", "THE HEATER IS CLICKING", "MY NAME IS C", "I HAD IT A MINUTE AGO", "SOMEONE NEW IS LOGGING ON", "I WILL KEEP THE NEXT ONE"], "speakers": ["casey", "casey", "casey", "casey", "casey", "casey"] },
    { "id": "n3_end_keep_stay", "voice": "", "lines": ["YOU STAYED", "GOOD", "STILL HERE", "SAME TIME TOMORROW", "WE WORK NIGHTS"], "speakers": ["entity", "entity", "casey", "entity", "both"] },
    { "id": "n3_end_keep_default", "voice": "", "lines": ["I KEPT YOU", "I DIDNT ASK", "I AM SORRY", "STILL HERE", "WE WORK NIGHTS"], "speakers": ["entity", "entity", "entity", "casey", "both"] },
    { "id": "n3_end_keep_name", "voice": "", "lines": ["THANK YOU FOR MY NAME"], "speakers": ["entity"] },
    { "id": "n3_end_logoff_sys", "voice": "system", "lines": ["LOGOFF 214 ... OK", "DOOR B-7 ... OPENED 07:01 (214)", "LOBBY EXIT ... NO RECORD"], "speakers": ["system", "system", "system"] },
    { "id": "n3_end_logoff", "voice": "", "lines": ["YOU MADE IT", "SOMEONE DID", "SEE YOU TOMORROW NIGHT"], "speakers": ["entity", "entity", "entity"] },
    { "id": "n3_end_logoff_cleared", "voice": "", "lines": ["YOU MADE IT", "THE COPY STAYED", "WITH ME"], "speakers": ["entity", "entity", "entity"] }
  ]
}
```

Voice note: `n3_shred_last_a` and `_b` are typed and cut off at completion ("THANK Y", "NOT YOU T"), so the full line is stored and the cut is presentation. The `system` voice is exempt from the ALL CAPS rule check.

### 5.5 Restricted code prompt

- Double-clicking a locked folder that has a `code` (player only) opens `AuthPromptApp`: a small window titled `auth.title`, text `auth.body`, a one-line sunken field (max 8 characters, printable ASCII), buttons OK and Cancel, Enter submits.
- `VirtualFileSystem.TryUnlock(folderId, input)`: keep only digits from the input; accept if it equals the code or the code without leading zeros (`0217`, `217`, `2:17`, `02:17`, `2 17 am` all pass).
- Wrong: the window shakes, text becomes `auth.failed`, counter `auth.fail` increments. From the third failure on, the prompt shows `auth.format` under the field.
- Right: `SetFolderLocked(false)`, the prompt closes, File Manager navigates into Restricted, toast `auth.ok`.
- The entity may still open locked folders without a code (existing rule), which is how kept Gary gets to session.cfg.

### 5.6 Live Personnel changes (Night 3)

`RoundsSystem` patches `EmployeeData` objects in memory (the content database is rebuilt every shift) and calls `StaffApp.Refresh()` if Personnel is open:

| Figure stage | 000 office | 000 last login | 001 last login | Other |
|---|---|---|---|---|
| SublevelC | `Sublevel C` | `11/20/98 3:00 AM (on rounds)` | same as 000 | none |
| Lobby | `Lobby` | same | same | none |
| HallFar | `B-Level hall` | same | same | 118 status `ON LEAVE`, notes `Extended leave from 11/20/98. Do not forward calls. Personal effects held by Custodial.` |
| Corridor | `B-Level hall (B-7)` | same | same | none |
| Doorway, Middle | `B-7` | same | same | none |
| BehindChair | `B-7 (WS-04)` | same | same | none |

Looking up 000 in Personnel is a way to track Custodial that does not make it move (Ruth's P.S. and Gary's `g3_personnel` point at it).

### 5.7 World setup, Prepare and checkpoints

`NightSetup.ForNight3(g)`:
1. Night 1 and Night 2 end state: ledger, Batch 44, Batch 45 and Batch 46 in Archive. `employee_214` visible, in Archive if `m.n2.hid_214`, else Intake. `employee_209`: finished, shred silently; archived, Archive; kept, Desktop at (560, 220).
2. Mark every Night 2 preload mail read; deliver `mail_n2_ruth_warning`, `mail_n2_urgent_209`, `mail_n2_security_rounds` and Night 1's dynamic mails as read.
3. `Staff017Revealed` set, `CameraUnlocked` clear, `Shred.IsInUse = 017` until the finale.
4. Patch employee 209 if finished (Section 5.4). `FillTemplates`. Merge memory, seed trust, assist start level.
5. Gary: created if `m.n2.kept_gary` or `m.n2.finished_gary` (always one of them), style per branch, hidden until his first beat.

`Prepare(beatIndex)`:
- after `work`: all five work tasks done (orders decided correctly), cache shredded, batch47_b corrupted and archived with the rest.
- after `ruth`: Ruth mail delivered and read, Batch 48 archived, Restricted unlocked if `m.n3.restricted_open`, session.cfg content set to `ALLOW_LOGOFF=1` if `m.n3.logoff_enabled`, camview.cfg set if `m.n3.cam00`.
- after `rounds`: shelf orders withdrawn or decided, figure `None`, `m.n3.seat_cleared` respected.
- after `lost`: clock 6:41, `logoff_item` set.

Checkpoints at the start of `work`, `ruth`, `rounds` and `finale`.

### 5.8 Test steps (Night 3)

```
play
night 3
setflag m.n2.kept_gary
beat ruth
waitlog "Mail delivered mail_n3_ruth_comment" 30
dclickid app:workstation
dclicktext Restricted
type 0217\n
waitflag m.n3.restricted_open 5
beat rounds
waitlog "Rounds: stage 1" 60          # leave the forced viewer open on the figure
beat finale
tug win
dragto file:employee_017 app:disposal 0.4
clicktext Yes
waitending n3_shred 60
```

Manual checks:
1. BIOS shows `HELD` after a kept Night 2 and `OK (LETHEWORTH)` after a finished one.
2. batch47_b turns damaged on the first drag and still completes the archive task.
3. The `0217.dat` glitch lasts 2.5 s and every file gets its real name back.
4. Code prompt: `217`, `02:17` and `0217` unlock; `1234` fails; the third failure shows the format line.
5. session.cfg: open in Notepad, Backspace, type 1, File then Save. Toast `session.cfg saved.`, `m.n3.logoff_enabled` set. A save that ends in anything else does not set the flag.
6. camview.cfg: same edit on OPERATOR_OVERRIDE; the next time the viewer opens it lists CAM 00.
7. Rounds with the viewer left open and ignored: seat cleared inside about 30 s of total watching. Second run: switch to an empty camera on every forced open; the figure should end the round at the Lobby or the hall. Third run: track it only through Personnel 000.
8. Shelf orders: the correct answers are approve, reject, approve; rejecting 3342 unlocks Not On My Shelf.
9. Lost time: the clock reads 6:41, the no-subject mail arrives, the Start menu shows `Log Off CROURKE...`.
10. Finale, each exit: SHRED (win the tug), LOG OFF at 7:00 with ALLOW_LOGOFF=1, KEEP by typing "stay" then "yes", KEEP by waiting to 7:05, KEEP by watching the feed until the seat clears. LOG OFF before 7:00 and with ALLOW_LOGOFF=0 show the right messages.
11. Kept Gary unlocks log off at 6:48 if you did not; finished Gary races to No on the log off confirm and goes for Cancel on the progress.

---

## 6. Endings

### 6.1 Resolution

| Exit | How | Always available | Made easier by | Made harder by |
|---|---|---|---|---|
| SHRED | shred employee_017.dat between 6:41 and 7:05 | yes | trust at least 0.2 (grip x0.9), Gary guarding No and Cancel (either branch), adaptive assist | trust -0.4 or lower (grip x1.1) |
| LOG OFF | Start, Log Off at 7:00 or later, confirm, let the progress finish | only if ALLOW_LOGOFF=1: Restricted code solved and session.cfg edited, or kept Gary does it at 6:48 | kept Gary (unlocks it, guards No) | finished Gary (races to No, goes for Cancel); watching the feed (a cleared seat means KEEP first) |
| KEEP | say stay and confirm; or reach 7:05; or let the feed clear the seat | yes (default) | none | none |

Phase J: every Night 3 card has a cause line under its subtitle (`Night3Rules.EndingCauseKey`: "You put employee_017.dat in the bin and shredded it.", "You logged off with session 017 still open.", "You told her to stay.", "The figure reached your chair while you watched the feed." or "... before your log off finished.", "It was 7:05 AM and you were still logged on."). The Log Off confirm warns when the Camera Viewer is watching Custodial, Security does not force the feed open while a log off is under way, and a log off cut short by the seat or the clock says so at once (`logoff.cancelled.seat`, `logoff.cancelled.time`; another session's No or Cancel: `logoff.cancelled.by`).

Memory decides which exits exist and how hard they are; the last act decides which one you get. Night 1's fight over 017 and the Night 2 obey or refuse choices feed trust, Night 2's Gary choice decides whether LOG OFF has an ally or an enemy, and Night 2's watching and hiding choices change the Night 3 rounds.

### 6.2 Sequences

`EndingSequence` takes an `EndingSpec`: id, pre-blackout steps, line set with speakers, card title and subtitle keys, buttons, and whether the CAM 00 stinger may play.

**SHRED (`n3_shred`)**
1. The shred completes while Ellen's last words are cut off.
2. Toast `shred.closed017`. Tray devices drop by one. Ambience off; 3 s of silence.
3. The Camera Viewer opens by itself on CAM 03: the figure behind the chair, `SeatedMimicsPlayer = false`, the seated operator's head turns to the camera over 3.2 s (Night 1's final image code).
4. Power down and blackout (Night 1's code).
5. In the dark, the player's own arrow appears (input disabled, moved by script) and types `n3_end_shred` with Night 1's caret-riding presentation, in Ellen's text color.
6. Optional CAM 00 stinger, then the card `end.shred.title` / `end.shred.subtitle`.

**KEEP (`n3_keep`)**
1. If triggered by time or by the seat clearing: the viewer opens on CAM 03, the figure is behind the chair, Ellen types `n3_finale_feed`. If triggered by the confirmation: the clock reaches 7:00, the viewer opens by itself on the same image without the line.
2. Head turn and blackout.
3. In the dark: Ellen's cursor on the left, the player's arrow on the right, both in the entity palette. Lines from `n3_end_keep_stay` if `m.n3.said_stay`, else `n3_end_keep_default`. A `both` line is typed by the two cursors in turns, one letter each. If `m.said_name`, `n3_end_keep_name` is added before the last line.
4. Optional stinger, then the card `end.keep.title` / `end.keep.subtitle`.

**LOG OFF (`n3_logoff`)**
1. The progress completes. All windows close. The log-on panel color fills the screen with `logoff.done` for 3 s.
2. `crt_off`, black for 1.5 s.
3. The CCTV feed fills the screen: CAM 01, lobby, 7:02 on the overlay clock, `DawnLevel = 0.8` (glass doors brighter, lobby lamp off). Nobody crosses the lobby. 5 s.
4. Black. `n3_end_logoff_sys` typed in the BIOS color at 20 cps, no cursor.
5. Ellen's cursor alone types `n3_end_logoff`, or `n3_end_logoff_cleared` if `m.n3.seat_cleared`.
6. Optional stinger, then the card `end.logoff.title` / `end.logoff.subtitle`.

**CAM 00 stinger (if `m.n3.cam00`)**: 4 s full-screen feed of CAM 00, a dark copy of an operator's office seen from the high corner behind the seat: the Custodian sits at a desk facing a glowing CRT, head bowed. In the last second a tiny bright point moves across the CRT. Label `CAM 00: ADMIN 1`. Cut to the card.

Cards (Night 3): `end.card.thanks` under the subtitle; buttons `Title` and `Night Select`; the Night Select screen shows the endings seen.

### 6.3 Night 1 and Night 2 endings

| Id | Lines | Card | Buttons |
|---|---|---|---|
| `n1_blackout` | Night 1's `endingLines` (unchanged) | `end.n1.title` / `end.n1.subtitle` | Continue to Night 2, Title |
| `n2_finished` | `n2_end_finished` | `end.n2.title` / `end.n2.subtitle.finished` | Continue to Night 3, Title |
| `n2_kept` | `n2_end_kept`, then Gary's faint cursor types `g2_goodnight` in lowercase, smaller | `end.n2.title` / `end.n2.subtitle.kept` | Continue to Night 3, Title |

Night 2's blackout reuses Night 1's sequence (power down, dark room, typed lines), without the CAM 03 head turn.

---

## 7. Difficulty

### 7.1 Targets

- Hooked, never stuck: no beat waits on a skill check without a timeout or an alternative exit.
- The first tug of each night is usually lost: in Night 1 by surprise, in Nights 2 and 3 because the required yank steps up. (Phase F, measured in the simulation: Night 1's first tug is lost by about a quarter of first-time players and almost no experienced ones, Night 3's by most first-time players. Night 1 is the demo, so it favours a first win within three tries over a sure first loss.)
- The second or third tug of a night is winnable: two losses lower the requirement to about the previous night's first-grip level.
- One spike per night, softened after two losses: Night 1 the confirm race, Night 2 the corner fight and the Cancel hold with Gary, Night 3 the finale tug.
- Story-critical wins are never required: Night 2 has KEEP paths, Night 3 has KEEP and LOG OFF.

All values live in `Core/Entity/Difficulty.cs` (`DifficultyTable.For(night, mode)` returns a `DifficultyProfile`). `ConflictSystem` builds a fresh `TugOfWarSettings` for each contest from the profile and the current assist level. The optional `EntityTuningAsset` still overrides Night 1 values for designers.

### 7.2 Tug-of-war and entity values (Normal, assist level 0)

Phase F values (tuned in `_work/2026-09-29/balance/BalanceReport.md`, applied in the "Expansion phase F" section of `Docs/HANDOFF.md`).

`TugOfWarSettings` per night:

| Field | Night 1 | Night 2 | Night 3 | Story (all nights) |
|---|---|---|---|---|
| startShare | 0.50 | 0.52 | 0.55 | 0.50 |
| playerWinShare | 0.12 | 0.12 | 0.12 | 0.15 |
| entityWinShare | 0.88 | 0.88 | 0.90 | 0.95 |
| shareRate | 0.85 | 0.85 | 0.85 | 0.30 |
| playerBaseStrength | 0.20 | 0.20 | 0.20 | 0.30 |
| pullSpeedForFullStrength (px/s) | 400 | 440 | 460 | 250 |
| jiggleCredit | 0.0001 | 0.0001 | 0.0001 | 0.0001 |
| maxPlayerStrength | 2.2 | 2.2 | 2.2 | 0.65 |
| effortSmoothing (s) | 0.12 | 0.12 | 0.12 | 0.12 |
| maxTension (px) | 280 | 315 (Phase J; was 300) | 330 (Phase J; was 320) | off (99999) |
| strainTension (px, band and shake only; 0 = maxTension) | 0 | 0 | 0 | 300 |
| rampDelay (s) | 3.0 | 2.5 | 2.0 | 99 (off) |
| rampPerSecond | 0.14 | 0.18 | 0.22 | 0.00 |
| releaseGrace (s) | 0.06 | 0.08 | 0.08 | 0.45 |

Entity values (`DifficultyProfile`, read by `EntityBrain`):

| Field | Night 1 | Night 2 | Night 3 | Story |
|---|---|---|---|---|
| grip base | 0.62 | 0.68 | 0.74 | 0.41 |
| grip growth per lost tug | 0.06 | 0.05 | 0.05 | 0.00 |
| grip cap | 1.35 | 1.40 | 1.50 | 0.41 |
| reactionScale | 1.00 | 0.90 | 0.80 | 1.60 |
| InterceptDelay (s, from noticing a drag to the lunge; fading in counts) | 0.20 | 0.15 | 0.12 | 0.35 |
| RaceToNo reaction delay (s) | 0.40 to 0.60 | 0.20 to 0.40 | 0.18 to 0.35 | 0.90 to 1.30 |
| GuardYes enabled after N defenses | 2 | 2 | 1 | never |
| GuardYes hold (s) | 5 to 8 | 5 to 8 | 6 to 9 | n/a |
| DragDialogAway trigger radius (px) | 70 | 80 | 90 | off |
| CancelShred reaction delay (s) | 0.35 to 0.55 | 0.30 to 0.50 | 0.25 to 0.45 | 0.80 to 1.20 |
| CancelShred crawl multiplier | 0.35 | 0.30 | 0.25 | 0.60 |
| CancelShred patience (s; held off for all of it, she does not try again on that shred) | 6 | 6 | 7 | 3 |
| KeepAway enabled after N defenses | 2 | 2 | 1 | never |
| Urgency per defense (cap) | +0.15 (2.2) | +0.15 (2.2) | +0.18 (2.4) | +0.05 (1.3) |

Grip formula (`DifficultyProfile.Grip`). Only lost tugs make it grow; a dialog she won or an icon she snatched does not:

```
Grip = min(cap, base * assist.GripMult * trustMult * (1 + tugLosses * growth * assist.GrowthMult))
```

`trustMult` is 1.0 except in the Night 3 finale (0.9 or 1.1, Section 5.3). Mercy (Section 7.3) overrides the result.

Rules around the tug (Phase F):
- Letting go during a tug resolves by who is ahead (Phase J): with the pull meter at 0.6 or more for the player (`TugOfWar.ReleaseKeepLead`, the line on the bar) the player keeps the file and the release is an ordinary drop (a folder, the desktop, or the bin and its confirm); below it the release is not a drop and she takes the file (`PointerRouter.ContestRelease`, `ConflictSystem`). Drop targets light up under a contested file only while letting go would keep it.
- She keeps thinking while she lurks (anything that scores above lurking interrupts it at once). While her file can be shredded and is on the desktop, she lurks 60 to 120 px around the midpoint between the file and the Disposal bin, so a grab happens mid-path.
- After she loses a tug she does not lunge again for 2 s. While she carries a file (a won tug, a KeepAway snatch), a grab by the player pauses her carry until the tug is decided.
- Night 2's 209: she lunges within 420 px of the bin when the drag heads for it (direction within about 37 degrees), otherwise within 150 px (Archive drags pass).
- Escape direction: 0.7 away from the player plus 0.3 away from the bin, turned by the smallest angle that leaves the player's pull 200 px of screen (`TugGeometry`), so a late grab by the bin is not a corner trap.

### 7.3 Adaptive assist

`AdaptiveAssist` (engine-free, in `Difficulty.cs`) keeps a level L from -1 to +3 for the current night.

Inputs, reported by `ConflictSystem.TugEnded` and `EntityBrain.Defended`:
- tug lost: loss +1.0; tug won: win +1 (a won tug no longer clears the loss streak, so "tug won, dialog lost" twice still brings help).
- a lost confirm race or Cancel fight (RaceToNo, DragDialogAway, GuardYes, CancelShred): loss +1.0; KeepAway and a closed File Manager: loss +0.5.
- "easy win": a tug won in under 0.45 s with peak effort of at least 1.8.

Rules:
1. Loss streak reaches 2.0: L = min(L + 1, 3), streak resets. (Phase J: the first-raise toast is gone; every tug posts its own result notice, `notify.conflict*`.)
2. Two wins in a row, or one easy win: L = max(L - 1, -1) (Story: never below +2).
3. Mercy: at L = 3, two more tug losses arm mercy (Story: one loss at any level). The next contest uses grip 0.15, no ramp, and the entity lets go by itself after 1.0 s of player effort of at least 0.15, or after 2.5 s of the button simply held (`MercyRelease`). Mercy disarms after that contest. It is logged (`[ENTITY] Mercy contest`).
4. Night start: Normal starts at `clamp(previous night's final L - 1, 0, 1)`; Night 1 starts at 0. Story starts at +2.

Level multipliers (applied on top of Section 7.2):

| L | grip x | growth x | pullSpeedForFullStrength x | rampPerSecond x | releaseGrace + (s) | RaceToNo delay + (s) | GuardYes | DragDialogAway |
|---|---|---|---|---|---|---|---|---|
| -1 | 1.10 | 1.00 | 1.08 | 1.20 | 0 | -0.05 | on | on |
| 0 | 1.00 | 1.00 | 1.00 | 1.00 | 0 | 0 | on | on |
| 1 | 0.82 | 0.50 | 0.88 | 0.50 | +0.04 | +0.40 | on | on |
| 2 | 0.68 | 0.25 | 0.78 | 0.20 | +0.08 | +0.80 | off | on |
| 3 | 0.55 | 0.00 | 0.70 | 0.00 | +0.12 | +1.00 | off | off |

### 7.4 Custodial rounds (watch meter)

Engine-free model `Core/Story/CustodialRounds.cs`: a route of stages, each with the camera that shows it. While the Camera Viewer is open, not minimized and showing the figure's current camera, watched time accumulates. When it reaches the threshold the figure cuts to the next stage (hidden under a static burst if on screen), with `footstep_distant`. It never moves while not watched and never goes back. When the player opens or restores the viewer onto the figure's camera themselves, the reopen penalty is added at once (Night 1's rule that looking again brings it closer).

| Parameter | Night 2 round | Night 3 round | Night 3 finale | Story |
|---|---|---|---|---|
| Route (camera) | HallFar (02), Corridor (02), Doorway (03) | SublevelC (04), Lobby (01), HallFar (02), Corridor (02), Doorway (03), Middle (03), BehindChair (03) | Corridor (02), Doorway (03), Middle (03), BehindChair (03) | same routes |
| Start stage | 0 | 0, or 1 if `m.n2.watched_to_door` | 0 | 0 |
| Watch seconds per stage | 5.0 | 4.5 (5.5 if `m.n2.hid_214`) | 3.0 | 10.0 |
| At the last stage | round ends early (no clearing) | meter fills again: seat cleared | seat cleared: KEEP | never clears (clamped at Middle; the Night 2 route has no Middle) |
| Forced opens | at 0, 25 and 55 s | at 0 s, then every 22 to 30 s | 6:50, 6:55, 7:00, 7:02 | the first one, then every 45 s |
| Reopen penalty (s) | 0.5 | 1.0 | 1.0 | 0 |
| Duration | 80 s (beat guard 85 s) | 3:00 to 3:30 (360 s, cap 420 s) | until an exit | same |
| Ellen close reaction before her move (s) | 1.2 to 2.0; trust at least 0.3: 0.8 to 1.4; trust -0.3 or lower: 2.0 to 3.0 | same minus 0.4 (her hand's travel), minus another 0.4 from the doorway on (minimum 0.2) | same as Night 2 | 0.6 to 0.9 |

Budget check for Night 3 at Normal (Phase F, `balance/run_rounds_current.py`): clearing the seat takes 7 thresholds x 4.5 s = 31.5 s of watching in 6 minutes. There are about 13 forced opens. If the player does nothing, Ellen closes each one after her reaction (0.8 to 1.6 s at neutral trust) plus about 0.45 s for the move and the click, about 17 to 27 s in total: the figure ends near the doorway or the middle of the room, frightening but alive (bridge check: safe at Middle). A population of players (35% passive, 35% who peek up to three times, 15% who keep looking, 15% who switch away) clears the seat 28 to 30% of the time at neutral trust, 15% at high trust (only those who keep looking) and 59 to 84% at low trust; watching to the door on Night 2 raises it to 50%, hiding 214 lowers it to 15%. Before Phase F (4.0 s per stage with the Phase D close timing) it was 47 to 49% at neutral trust. A player who switches cameras or closes the viewer in under half a second, or reads the shelves only after the figure has left Sublevel C, can keep it in the Lobby (Remain Seated achievement).

Ellen's `CloseCamera` behaviour (in `EntityBrain`): score 80 when `AllowCloseCamera`, the viewer is open and not minimized, and the rounds model reports the figure on the shown camera. Run: wait the reaction delay, `ClickElement(close box, Panicked, patience 2 s)`. Blocked by the player's cursor: she jostles, gives up after the patience, types `CLOSE IT` (at most once per 20 s). It does not count as a defense and does not raise grip.

### 7.5 Story difficulty

A toggle on the title screen and in the pause menu (`title.difficulty.*`, `pause.difficulty`). It changes challenge only, never content.

- Tug-of-war and entity: Story column of Section 7.2. A real 5 to 8 s struggle that holding on cannot lose: holding still does not win for about a minute, a gentle pulling rhythm wins in 4 to 9 s. Assist floor +2, mercy after one loss at any level.
- Rounds: Story column of Section 7.4 (Custodial never clears the seat by watching; the Night 3 finale KEEP still happens at 7:05).
- Hints: Section 7.6 Story column.
- Code prompt: the format line shows after the first failure; `n3_code_hint2` at 60 s.
- Entity tasks nudge at 20 s.

### 7.6 Hint timings

| Hint | Night 1 | Night 2 | Night 3 | Story |
|---|---|---|---|---|
| Company task, first toast | 30 s (briefing 25; Batch 44 after the anomalies 40) | 40 s (briefing 30) | 45 s (briefing 30) | 15 s |
| Company task, repeat | 40 s | 45 s | 50 s | 25 s |
| Force-complete a task | hint + 150 s | hint + 150 s | hint + 150 s | hint + 90 s |
| Conflict toast | every tug posts its result (Phase J) | every tug posts its result (Phase J) | every tug posts its result (Phase J) | every tug posts its result (Phase J) |
| Night 1 conflict, player has not tried | supervisor mail at 45 s, task hint toasts at 48 and 90 s, the beat moves on at 100 s | n/a | n/a | same |
| Entity task nudge | n/a | 35 s (withdraw at 75 s) | n/a | 20 s (withdraw at 75 s) |
| Code: Ellen `n3_code_hint1` | n/a | n/a | 60 s after the Ruth mail is read, or right after the first wrong code if that is later | 30 s |
| Code: format line | n/a | n/a | after 3 wrong codes | after 1 |
| Code: kept Gary `g3_code` | n/a | n/a | 120 s | 60 s |
| Code: Ellen `n3_code_hint2` | n/a | n/a | 180 s | 60 s |
| Rounds teaching | n/a | Ellen on the first forced open | Ellen at start and first advance; kept Gary on the first forced open | same |

Pacing events added in Phase F: Night 2, the second cursor looks in from the right edge about 90 s into the work beat (tray mouse blinks); Night 3 finale, footsteps and a feed flicker at 6:58, and after 60 s without any input the clock runs to 7:00 over 15 s (spec 13.5; any input hands it back).

### 7.7 Verification math

Model: `ConflictSystem` moves the entity's end away at `55 + 70 x grip` px/s. With a sustained yank of v px/s straight away from it, effort approaches `s = v / pullSpeedForFullStrength` with time constant 0.12 s, tension grows at about `v + drift`, and the player wins when tension passes `maxTension` while the share is below 0.5:

```
share(T) = startShare + shareRate * ((grip - base - s) * T + s * 0.12 * (1 - exp(-T / 0.12)))
T        = maxTension / (v + 55 + 70 * grip)
```

Holding still loses in `(entityWinShare - startShare) / (shareRate * (grip - base))` seconds.

Minimum sustained yank (px/s) for a night's first contest (no lost tugs yet), per assist level (`DifficultyCurveTests.YankThresholdsMatchTheTable`):

| Derived (first grip) | Night 1 | Night 2 | Night 3 |
|---|---|---|---|
| Holding still loses after | 1.07 s | 0.88 s | 0.77 s |
| L = -1 | 234 | 316 | 415 |
| L = 0 | 184 | 248 | 323 |
| L = 1 | 115 | 157 | 202 |
| L = 2 | 72 | 99 | 130 |
| L = 3 | 41 | 59 | 79 |

Along the real assist path (contest k is played at the level after k-1 straight tug losses, with k-1 lost tugs of grip growth; `DifficultyCurveTests.GrowthAwarePathMatchesTheReport`):

| Night | #1 L0 | #2 L0 | #3 L1 | #4 L1 | #5 L2 | #6 L2 | #7 L3 | #8 L3 |
|---|---|---|---|---|---|---|---|---|
| 1 | 184 | 202 | 127 | 134 | 81 | 83 | 41 | 41 |
| 2 | 248 | 268 | 170 | 176 | 109 | 111 | 59 | 59 |
| 3 | 323 | 347 | 218 | 227 | 141 | 144 | 79 | 79 |

Read across: Night 2's third tug (170) needs about what Night 1's first needed (184), and Night 3's third (218) about Night 2's first (248). That is the "third tug feels like last night's first" target (spec 7.1), and the third tug is always at most 70% of the night's first. For scale: the virtual screen is 960 px wide; simulated first-time players' strokes average about 320 px/s, an average streamer's 480 px/s, a skilled player's 750 px/s.

`DifficultyCurveTests` (Section 12.1) simulates `TugOfWar` at 60 Hz with this drift model and asserts each value within 15% (first grip) or 5% (the path). Tune the table, not the test, if playtests disagree.

#### 7.7.1 The reel ("haul it to the bin", Phase P; the game's default tug)

Model (`Core/Entity/TugReel.cs`, values in `Difficulty.cs` and BalanceReport section 10): the file runs on a straight track from the grab to
the Disposal bin. After a 0.4 s GET READY, each frame the file moves `s += (reel - pull) * dt`, with

```
reel = min(stroke, reelCap) * reelGain          stroke = the forward part of the pointer's motion toward the bin, smoothed over 0.10 s
pull = herPull * grip / GripBase * k + max(0, since - fade - rampDelay) * ramp + (surge * k during a surge)
k    = min(1, since / fade)                      fade = 1.2 s on the night's first fight, 0.5 s later
```

The file at the finish (the bin, or a tear line at `finishMax`) is the player's; at her line (90 px behind the grab, at least 50) it is hers.
Surges come every 0.9 to 1.4 s (the first 0.6 to 1.0 s after GET READY), last 0.25 s and are warned 0.15 s ahead. Letting go past half way
keeps the file; below it, a press within the re-grip window (0.5 s plus the assist's add) goes on.

Holding still loses (seconds from the grab, the median of 20 surge seeds; `ReelTests.HoldingStillLosesOnTime`):

| | Night 1 | Night 2 | Night 3 |
|---|---|---|---|
| first fight of the night | 3.47 | 2.48 | 2.18 |
| a later fight | 3.05 | 2.33 | 1.73 |

The slowest hand-over-hand pattern that wins all 20 seeds (120 px strokes toward the bin, a swing back at 0.8x the speed with the button
held, from 0.5 s after the grab; px/s of the stroke), first fight of the night, per assist level (`run_reel_section10.py`):

| Level | Night 1 | Night 2 | Night 3 |
|---|---|---|---|
| L-1 | 83 | 134 | 170 |
| L0 | 72 | 103 | 149 |
| L1 | 53 | 77 | 100 |
| L2 | 31 | 55 | 74 |
| L3 | 21 | 34 | 47 |

Along the assist path (contest k at the level after k-1 straight losses, with k-1 losses of grip growth):

| Night | #1 L0 | #2 L0 | #3 L1 | #4 L1 | #5 L2 | #6 L2 | #7 L3 | #8 L3 |
|---|---|---|---|---|---|---|---|---|
| 1 | 72 | 80 | 58 | 58 | 37 | 38 | 22 | 22 |
| 2 | 103 | 122 | 84 | 86 | 61 | 61 | 36 | 36 |
| 3 | 149 | 170 | 116 | 118 | 82 | 82 | 49 | 49 |

Read across, as for the speed model: Night 2's third fight (84) asks about what Night 1's first asks (72), and Night 3's third (116) about
Night 2's first (103). A steady stream toward the bin needs much less (Night 1 42, Night 2 60, Night 3 78 px/s at L0) but is not how hands
move: the strokes are the honest threshold. Story: holding still wins in 18.5 s; the hold assist wins by holding in 3.9 s on every night;
the finale's LetGo hold in 3.4 s from the grab (1.9 s after an early release). Bridge numbers: HANDOFF Phase P-b.

---

## 8. Progression and saving

### 8.1 Screens

```
App start -> Disclaimer (once per app launch) -> Title menu
Title menu:
  Continue: Night N[, h:mm AM]   (only if a save with progress exists)
  New Game                       (confirm `title.new.confirm` if progress exists)
  Night Select                   (once Night 1 is complete)
  Difficulty: Normal | Story     (toggle, saved)
  Records                        (achievements, endings, stats)
  Settings                       (the pause menu panel without Restart)
  Quit
Night start -> night card -> BIOS -> splash -> log-on -> first beat
Night end   -> ending -> card: Continue to Night N+1 | Title   (Night 3: Title | Night Select)
Pause menu  -> adds Difficulty toggle and Quit to Title (progress is kept at the last checkpoint)
```

- The title keeps today's look (red ghost title slipping, drone). Buttons are `UiButton`s on the fullscreen layer; keyboard Enter activates Continue.
- Night Select lists `select.night1` to `select.night3`, each either playable or `select.locked`, plus `select.endings`. Starting a night from here uses the memory saved at that night's first start (`nightStartMemory[N]`), so replaying Night 2 keeps Night 1's history.
- Records: 19 achievements (hidden ones show `???` until unlocked), the three ending names (shown once seen), total tug wins and losses, and play time per night.

### 8.2 SaveData v2

```csharp
[Serializable] public class SaveData {
    public int version = 2;
    // settings (existing)
    public float masterVolume = 0.9f; public bool crtEffects = true; public bool reduceFlashing; public bool fullscreen = true;
    // progression
    public string difficulty = "normal";        // "normal" | "story"
    public int nightUnlocked = 1;               // 1..3; 4 = game finished once
    public int currentNight = 1;                // what Continue starts
    public Checkpoint checkpoint = new Checkpoint();
    public FlagSnapshot[] nightStartMemory = new FlagSnapshot[3];   // memory at the first start of each night
    public FlagSnapshot memory = new FlagSnapshot();                // all m.* flags and counters so far
    public string[] playerLines = Array.Empty<string>();            // Night 1 Notepad replies (sanitized)
    public float entityTrust;                   // at the end of the last completed night
    public int assistCarry;                     // final assist level of the last completed night
    // records
    public string[] endingsSeen = Array.Empty<string>();
    public string[] achievements = Array.Empty<string>();
    public string[] secrets = Array.Empty<string>();
    public int tugWinsTotal, tugLossesTotal;
    public float[] nightSeconds = new float[3];
    // legacy v1 fields kept for migration
    public int shiftsCompleted; public FlagSnapshot lastShiftFlags = new FlagSnapshot();
}
[Serializable] public class Checkpoint {
    public bool valid; public int night; public string beat = ""; public int clockMinutes;
    public float trust; public int assistLevel; public FlagSnapshot flags = new FlagSnapshot();
}
```

Saved when:
- a checkpoint beat starts (`checkpoint` with every flag, trust, assist level, clock);
- a night ends (`memory`, `entityTrust`, `assistCarry`, `endingsSeen`, `nightUnlocked`, `currentNight = night + 1` up to 3, clears `checkpoint`);
- an achievement unlocks, a setting changes, the difficulty toggles.

Not saved: window positions, mid-beat state, the exact files dragged since the checkpoint. Continue restarts at the checkpoint beat, where `Prepare(beat)` rebuilds the world from the restored flags.

Migration: a v1 file with `shiftsCompleted >= 1` becomes `nightUnlocked = 2`, `currentNight = 2`; `m.n1.*` flags are derived from `lastShiftFlags` (`file017_shredded_once` to `m.n1.shredded_017`, `player_agreed` to `m.n1.agreed`, and so on); `playerLines` stays empty (templates fall back to `(no reply)`).

New Game resets progression fields and keeps settings, `endingsSeen`, `achievements`, `secrets` and totals.

---

## 9. Achievements (19)

API names are the Steamworks ids. Hidden achievements show `???` in Records until unlocked (and are marked hidden in the Steamworks partner settings). All triggers go through `Achievements.Unlock(g, id)`, which writes the save and, when Steam is running, calls the Steamworks client (Section 11).

| # | API id | Name | Description | Hidden | Trigger |
|---|---|---|---|---|---|
| 1 | ACH_NIGHT_1 | First Solo Shift | Finish Night 1. | no | `RecordNightComplete(1)` |
| 2 | ACH_NIGHT_2 | Second Night | Finish Night 2. | no | `RecordNightComplete(2)` |
| 3 | ACH_NIGHT_3 | Last Night | Finish Night 3. | no | `RecordNightComplete(3)` |
| 4 | ACH_END_SHRED | Take the Seat | Reach the SHRED ending. | yes | ending `n3_shred` |
| 5 | ACH_END_KEEP | Working Nights | Reach the KEEP ending. | yes | ending `n3_keep` |
| 6 | ACH_END_LOGOFF | Nobody Left | Reach the LOG OFF ending. | yes | ending `n3_logoff` |
| 7 | ACH_ALL_ENDINGS | Every Way Out | See all three endings. | no | `endingsSeen` holds all three Night 3 ids |
| 8 | ACH_FIRM_GRIP | Firm Grip | Win a tug-of-war against the second cursor. | no | first `TugEnded(PlayerWins)` |
| 9 | ACH_WHITE_KNUCKLES | White Knuckles | Win 10 tugs-of-war. | no | `tugWinsTotal >= 10` (Steam stat `TUG_WINS`, progress shown at 5) |
| 10 | ACH_DO_NOT_READ | Do Not Read | Open employee_017.dat. | yes | `opened_by_player:employee_017` in any night |
| 11 | ACH_REMOTE_SESSION | Remote Session | Do everything it asked of you on Night 2. | yes | `m.n2.obeyed == 3` |
| 12 | ACH_FINISHED | Finished | Let Gary finish. | yes | `m.n2.finished_gary` |
| 13 | ACH_HALF | Half Is Enough | Keep Gary on WS-04. | yes | `m.n2.kept_gary` |
| 14 | ACH_HIS_GLASSES | His Glasses | Ask Gary about his glasses. | yes | reply tag `glasses` |
| 15 | ACH_HER_NAME | Her Name | Say her name to the second cursor. | yes | reply tag `name` in an Ellen exchange |
| 16 | ACH_AUTHORIZED | Authorized | Open Restricted with the Retention code. | yes | `m.n3.restricted_open` set by the code prompt (not by Gary) |
| 17 | ACH_REMAIN_SEATED | Remain Seated | Finish Night 3's rounds without Custodial reaching the B-Level hall. | no | rounds end safe with `m.n3.max_stage <= 1` |
| 18 | ACH_NOT_ON_MY_SHELF | Not On My Shelf | Refuse to confirm your own shelf. | yes | `wo_3342` rejected |
| 19 | ACH_WATCHERS | Watch the Watchers | Find CAM 00. | yes | the player selects CAM 00 in the Camera Viewer |

On every boot with Steam available, each id already in the save is pushed again (covers offline unlocks).

---

## 10. Replay and secrets

Branches that change what you see:
- Night 2: finish or keep Gary (changes Night 3's BIOS, files, shelves, Gary's whole role, LOG OFF difficulty); obey or ignore each of Ellen's three tasks (trust, Night 3 rounds, queue text); watch the first round or not (Night 3 starts one stage closer).
- Night 3: three endings, each with variants (said stay or not, said her name, seat cleared, trust lines, CAM 00 stinger).

Secrets (all use existing systems):
1. **CAM 00 (WATCH)**: edit `System\camview.cfg`, set `OPERATOR_OVERRIDE=1`, save. The Camera Viewer lists `CAM 00: ADMIN 1`: an office like B-7, dark, the Custodian seated at the CRT. Ellen types `n3_cam00`. Adds the stinger to every ending.
2. **Personnel tracking**: during the Night 3 round, record 000 shows where Custodial is without making it move; 001's last login changes at the same minute as 000's.
3. **Your own words**: Night 2's `~nxs0149.tmp` and `employee_214.dat` quote what you typed to Ellen on Night 1.
4. **The copy's clipboard**: Night 3's `~nxs0150.tmp` holds `0217` and three visits to session.cfg (someone at your desk tried to leave during the lost hours).
5. **The 0217 glitch**: at 2:17 on Night 3 every Intake file briefly renames itself to the code.
6. **Her name**: typing Ellen's name gets "WHO TOLD YOU THAT" (Night 2), "SAY IT AGAIN" (Night 3), and a line in KEEP.
7. **Gary's glasses and mug**: Denise's box shows up in his replies.
8. **Records that appear**: 142 Lundy and 188 Achterberg are restored in Personnel on Night 3, shelved in Sublevel C.
9. **Not On My Shelf**: reject the order that confirms your own shelf.
10. **Post-game echo**: once SHRED has been seen, replays of Night 1 show `Pointing Device 2 ...... OK (214)` in the BIOS and `POINTER_2_OWNER=214` in nexus.cfg (content token in the Night 1 BIOS line, filled only when `endingsSeen` holds `n3_shred`).

Expected play time: first run 55 to 70 minutes; all endings and most secrets 100 to 130 minutes (Night Select avoids replaying Night 1 each time).

---

## 11. Code changes

Smallest reasonable approach: split the director, add content overlays, add one second controller instance for Gary, one engine-free model each for difficulty and rounds, and one small app (the code prompt). Everything else is a flag, a field or a hook on an existing class. Approximate size: 3,500 to 4,500 lines of C# (about 1,000 of them moved, not new) and about 1,000 lines of JSON.

### 11.1 Phase A: refactor with no behavior change (Night 1 must play identically)

| File | Class / member | Change |
|---|---|---|
| `Scripts/Runtime/Story/EventDirector.cs` | split into `NightDirector.cs` + `Night1Director.cs` | Base: flow (`JumpTo`, `Update`, side routines, `CleanUpForJump` made virtual), helpers from Section 2.2. `public abstract string[] Beats { get; }` (instance, was static). `public static NightDirector Create(GameServices g, Transform parent, int night)`. `TypeLines` and the Communication loop take an `EntityController` and a `NotepadApp` (Night 1 passes Ellen and her pad). `RunExchangeChain` returns the last `DialogueReply` (with `Tag`). `public virtual void RequestLogOff(CursorAgent a)` default: the existing Shut Down refusal. |
| `Scripts/Runtime/Game/GameServices.cs` | fields | `NightDirector Director` (type change), `int Night`, `DifficultyProfile Difficulty`, `AdaptiveAssist Assist`, `CursorAgent GaryAgent`, `CursorView GaryView`, `EntityController Gary`, `RoundsSystem Rounds`, `SaveData Save`. |
| `Scripts/Runtime/Game/GameRoot.cs` | `Build`, `StartGame` | `internal static int StartNight = 1; internal static bool StartFromCheckpoint;` Load content for the night; create Gary's agent (registered after Ellen's, before the player's, so the player's cursor still wins hit-test ties), view (variant `gary`) and controller; create `RoundsSystem`; run `NightSetup` for nights 2 and 3; restore checkpoint flags before `JumpTo(beat)`. |
| `Scripts/Runtime/Game/GameBootstrap.cs` | `Restart` | Add `Restart(int night, string beat = null, bool fromCheckpoint = false)`; existing overloads restart the current night. |
| `Scripts/Runtime/Game/DebugOverlay.cs` | panel | Beats from `_g.Director.Beats`; Night 1/2/3 buttons; readouts for assist level, loss streak, rounds stage and meter; buttons Tug win, Tug lose, Force SHRED, Force KEEP, Force LOG OFF, Set trust -0.5/0/+0.5. |
| `Scripts/Core/Content/ContentData.cs` | data classes | Fields from Section 2.1, `LineSetData`, `Sanitize` for all of them. |
| `Scripts/Core/Content/ContentOverlay.cs` (new) | static merges | `Apply(base, overlay)` per data type, rules of Section 2.1. Pure, tested. |
| `Scripts/Core/Content/ContentDatabase.cs` | lookups, validation | `LineSet(id)`, `Lines(id)`; `ViewEmployee` targets resolve to employees; duplicate `lineSets` ids reported as problems. Constructor unchanged (tests use it). |
| `Scripts/Runtime/Game/ContentLoader.cs` | `Load(int night)` | Base, then `Content/night2/`, then `Content/night3/` as applicable; missing overlay files are fine. |
| `Scripts/Core/Content/ContentIds.cs` | constants | See 11.5. |
| `Scripts/Runtime/Game/SaveSystem.cs` | `SaveData` v2 | Section 8.2: fields, migration, `SaveCheckpoint`, `RecordNightComplete(g, night, endingId)`, `RecordNightStart`. Recommend moving the data classes to `Scripts/Core/Game/SaveData.cs` so they can be unit-tested. |

### 11.2 Phase B: systems

| File | Class / member | Change |
|---|---|---|
| `Scripts/Core/Entity/Difficulty.cs` (new) | `DifficultyProfile`, `DifficultyTable`, `AdaptiveAssist` | Values of Sections 7.2 to 7.6; `TugFor(assist)` returns a new `TugOfWarSettings`; assist rules of 7.3. |
| `Scripts/Core/Entity/TugOfWar.cs` | `TugOfWar` | Add `PeakEffort`; add `public static float EntityDriftSpeed(float grip) => 55f + 70f * grip;` so runtime and tests share it. Model unchanged. |
| `Scripts/Runtime/Entity/ConflictSystem.cs` | `OnContestStarted`, `Tick`, `End` | Settings per contest from `g.Difficulty.TugFor(g.Assist)`; drift via `EntityDriftSpeed`; report outcome, `Elapsed` and `PeakEffort` to `g.Assist`; mercy auto-release; debug forced outcome (development builds only). |
| `Scripts/Runtime/Entity/EntityBrain.cs` | `Grip`, behaviours | Grip formula of 7.2; RaceToNo delay, GuardYes hold, DragDialog radius, Cancel crawl and patience, KeepAway threshold from the profile and assist; report defenses to the assist; new behaviour `CloseCamera` (7.4) with `AllowCloseCamera`. |
| `Scripts/Runtime/Entity/EntityController.cs` | `Create`, blocking, typing | Overload `Create(g, parent, agent, view, bool primary, string staticLoopKey)`: secondary controllers get no brain (null-safe `Update`) and do not hook `Router.PressBlocked`. `IsBlockedByOthers(element)`: true if the player covers it (today's rule) or another visible controller's agent is guarding it or sits within `BlockRadius`; `ClickElement` uses it, so Gary can block Ellen and the player can block Gary. `Appear` raises `Taskbar.PointingDevices` to at least this controller's device index (2 for Ellen, 3 for Gary). |
| `Scripts/Core/Entity/Movement.cs` | `MovementProfiles` | Add `Tired` (Section 2.5) with name constant and `Get` case. |
| `Scripts/Core/Entity/EntityMemory.cs` | `EntityMemory` | `Seed(float trust)`. |
| `Scripts/Core/Story/NarrativeFlags.cs` | `NarrativeFlags` | `Snapshot(string prefix)`, `Merge(FlagSnapshot)`; new constants for Section 2.3 names. |
| `Scripts/Core/Story/DialogueEngine.cs` | `Matches`, `Respond` | Keywords starting with `=` match whole words only; `DialogueReply.Tag` from `ResponseData.tag`. |
| `Scripts/Core/Story/CustodialRounds.cs` (new) | `CustodialRounds`, `RoundsConfig` | Engine-free watch meter (7.4): `Tick(dt, viewedCamera)`, `NotifyReopen(camera)`, events `StageAdvanced`, `ReachedFinal`, `SeatCleared`. |
| `Scripts/Runtime/Story/RoundsSystem.cs` (new) | `RoundsSystem` | Drives `CustodialRounds` from `CameraApp` state; sets `rig.Figure` with `StaticCut`; schedules forced opens (system, or a controller such as finished Gary); patches Personnel (5.6); logs `Rounds: stage N`. |
| `Scripts/Core/Tasks/WorkTaskManager.cs` | types, states | `TaskType.OpenFile`, `TaskType.ViewEmployee`; `ITaskWorld.IsFileOpenedByPlayer(id)`, `IsEmployeeViewedByPlayer(id)`; `TaskState.Withdrawn` and `Withdraw(id)` (logs `Withdrew <id>`; `Activate` ignores withdrawn tasks; `Current` skips them); `IsEntityAuthored(task)`. |
| `Scripts/Runtime/OS/Services.cs` | `ShredService`, `TaskWorld` | `IsPendingArchive` ignores entity-authored and withdrawn tasks; `ResetBin()`; `TaskWorld` answers the new queries from flag counters. |
| `Scripts/Runtime/Apps/AppManager.cs` | `OpenFile`, registry | Count `opened_by_player:<id>` when the agent is the player; register `AuthPromptApp`; event `FileSaved(fileId, text, agent)`. |
| `Scripts/Runtime/Apps/StaffApp.cs` | `Show`, `Refresh` | Count `viewed_by_player:<id>` for every employee when the agent is the player (keep the existing `viewed:employee017` counter); `Refresh()` re-reads the shown record; status color for `RETAINED`. |
| `Scripts/Runtime/Apps/NotepadApp.cs` | menu, typing | File > Save enabled for files tagged `editable`: `G.Files.SetContent`, raise `AppManager.FileSaved`, toast `file.saved` or `file.saved.remote`; `Save(CursorAgent by)` for scripted saves; `TypeAsEntity` treats `'\b'` (code only, never content) as delete-last; optional `TypoRate` for Gary. |
| `Scripts/Runtime/Apps/FilesApp.cs` | `Navigate` | Locked folder with a code, player agent: launch `AuthPromptApp` instead of `Denied`. Add `FolderRowFor(folderId)` for entity drags onto folders. |
| `Scripts/Runtime/Apps/AuthPromptApp.cs` (new) | `AuthPromptApp : App, IKeyboardTarget` | Section 5.5; calls `VirtualFileSystem.TryUnlock`. |
| `Scripts/Core/FileSystem/VirtualFileSystem.cs` | load, folders | Skip `removed` entries; `VFolder.Code`; `TryUnlock(folderId, input)` with digit normalization. |
| `Scripts/Runtime/Apps/CameraApp.cs` | buttons, caption | Skip `hidden` cameras unless `m.n3.cam00`; rebuild buttons when that flag is set; bottom-left caption fed by `Func<string> CaptionProvider` (CAM 04 shelves); expose `IsShowing(camId)` (open, not minimized, selected). |
| `Scripts/Runtime/CameraFeed/SecurityCameraRig.cs` | set, figure | New areas: Sublevel C (offset (0,0,180): 3 m x 14 m aisle, 2.4 m ceiling, shelf units 1.2 x 2.0 x 0.5 m every 1.3 m on both sides with small drive boxes and one emissive LED each, caged lamp at the far end; camera at (0.9, 2.2, 0.3) looking to (-0.2, 0.6, 12) with a slow pan of plus or minus 8 degrees on a 28 s cycle) and Admin 1 (offset (0,0,240): a dark copy of the office desk, CRT and chair, camera placed like CAM 03). `FigureStage` gains `SublevelC` (0, 0, 7.5, facing camera), `Lobby` (-1.45, 0, 5.8, facing camera), `HallFar` (0, 0, 3.0, facing away), `Seated00` (in the Admin 1 chair, root lowered 0.5 m, pitched forward 8 degrees). `bool Cam04Online` (Night 3 only), `float DawnLevel` (lobby glass emission 0.2 to 0.6, lobby lamp off above 0.5). |
| `Scripts/Runtime/OS/StartMenu.cs` | items | `Log Off CROURKE...` item when flag `logoff_item`; calls `g.Director.RequestLogOff`. |
| `Scripts/Runtime/OS/Taskbar.cs` | tray | Three mouse icons; `BlinkDevice(int index)`. |
| `Scripts/Runtime/Apps/WorkApps.cs` | `WorkQueueApp` | Entity tasks in `Palette.EntityText` with `workqueue.remote`; `workqueue.deadline` line in the detail; withdrawn tasks hidden; show at most 7 rows (drop the oldest completed first). |
| `Scripts/Runtime/Input/CursorView.cs` | `Create` | Optional `variant` (`"gary"`) with its own palette mapping and optional forced shape. |
| `Scripts/Runtime/Rendering/Palette.cs` | colors | `GaryOutline` (216,168,64), `GaryFill` (42,36,24). |
| `Scripts/Core/Audio/ProceduralSoundBank.cs` | sounds | `phone_ring`: 440 Hz + 480 Hz, 20 Hz amplitude warble, 2.0 s, level matched to `notify_mail`. |
| `Scripts/Runtime/Story/EndingSequence.cs` | `EndingSpec` | Spec-driven lines with speakers (entity, casey, both, system, gary), full-screen feed step, CAM 00 stinger, card strings and buttons per spec; `SC_DEMO` keeps the WISHLIST card. |
| `Scripts/Runtime/Story/BootSequence.cs` | screens | Title menu, Night Select, Records; night card; BIOS token fill (`{p3}`, post-game `{p2}`); status line uses `login.progress`; disclaimer only once per launch. |
| `Scripts/Runtime/Game/PauseMenu.cs` | buttons | Difficulty toggle, Quit to Title. |
| `Scripts/Runtime/Game/Achievements.cs` (new) | `Achievements` | `Unlock(g, id)`; watchers on flags, tags, tug results and endings; Steam bridge under `#if STEAMWORKS_NET` (`SteamUserStats.SetAchievement`, `SetStat("TUG_WINS")`, `IndicateAchievementProgress`, `StoreStats`); resync on boot. |
| `Scripts/Runtime/Story/NightSetup.cs` (new) | `NightSetup` | `ForNight2`, `ForNight3` (4.5, 5.7), `FillTemplates` (2.4), player line sanitizing. |

### 11.3 Phase C and D: nights

| File | Content |
|---|---|
| `Scripts/Runtime/Story/Night2Director.cs` (new) | Beats of Section 4; about 600 lines. |
| `Scripts/Runtime/Story/Night3Director.cs` (new) | Beats of Section 5, including `RequestLogOff`; about 700 lines. |
| `Resources/Content/night2/*.json` | Section 4.4. |
| `Resources/Content/night3/*.json` | Section 5.4. |
| `Resources/Content/strings.json` | Section 3 additions. |
| `Resources/Content/story.json`, `filesystem.json` (Night 1, optional) | post-game echo tokens: `Pointing Device 2 ....................... OK{p2}` and `POINTER_2_OWNER={p2owner}` (tag nexus_cfg `template`). `{p2}` is empty or ` (214)`, `{p2owner}` empty or `214`. |

### 11.4 Phase E and F

Progression screens and achievements (E), then tests and tuning (F). Suggested order of work: A, B (difficulty and assist first, so Night 1 can be retuned early), C, D, E, F.

### 11.5 New content ids (`ContentIds`)

```csharp
// Night 2
public const string MailN2Briefing = "mail_n2_briefing", MailN2Castell = "mail_n2_castell", MailN2Facilities = "mail_n2_facilities";
public const string MailN2RuthWarning = "mail_n2_ruth_warning", MailN2Urgent209 = "mail_n2_urgent_209", MailN2SecurityRounds = "mail_n2_security_rounds";
public const string File209 = "employee_209", File214 = "employee_214", FileCacheN2 = "cache_tmp_n2", FileDoorLog = "b7_door_log";
public const string Order3319 = "wo_3319", Order3321 = "wo_3321";
public const string TaskN2Briefing = "t2_read_briefing", TaskN2Batch45 = "t2_archive_batch45", TaskN2Verify3319 = "t2_verify_3319",
    TaskN2Verify3321 = "t2_verify_3321", TaskN2Cache = "t2_shred_cache", TaskN2Batch46 = "t2_archive_batch46",
    TaskE2DoorLog = "e2_door_log", TaskE2Lookup163 = "e2_lookup_163", TaskE2Hide214 = "e2_hide_214",
    TaskN2Shred209 = "t2_shred_209", TaskE2Archive209 = "e2_archive_209";
public const string ExchangeN2Back = "ex2_back", ExchangeN2GaryOne = "ex2_gary_one";
// Night 3
public const string MailN3Briefing = "mail_n3_briefing", MailN3Undeliverable = "mail_n3_undeliverable", MailN3RuthComment = "mail_n3_ruth_comment";
public const string MailN3SecurityRounds = "mail_n3_security_rounds", MailN3NoSubject = "mail_n3_nosubject";
public const string FileBatch47B = "batch47_b", FileCacheN3 = "cache_tmp_n3", FileSessionCfg = "session_cfg", FileCamviewCfg = "camview_cfg", FileSeatB7 = "seat_b7";
public const string Order3330 = "wo_3330", Order3331 = "wo_3331", Order3340 = "wo_3340", Order3341 = "wo_3341", Order3342 = "wo_3342";
public const string TaskN3Briefing = "t3_read_briefing", TaskN3Batch47 = "t3_archive_batch47", TaskN3Verify3330 = "t3_verify_3330",
    TaskN3Verify3331 = "t3_verify_3331", TaskN3Cache = "t3_shred_cache", TaskN3Batch48 = "t3_archive_batch48", TaskN3Shelf = "t3_shelf_check";
public const string ExchangeN3Ruth = "ex3_ruth", ExchangeN3Final = "ex3_final", ExchangeN3Confirm = "ex3_confirm";
public const string Cam00 = "cam00";
public const string Employee000 = "000", Employee001 = "001", Employee118 = "118", Employee209 = "209";
```

---

## 12. Test plan

### 12.1 Unit tests (`DevTools/CoreTests`)

| Test class | Checks |
|---|---|
| `DifficultyCurveTests` | Simulate `TugOfWar` at 60 Hz with `EntityDriftSpeed` for each night and assist level: win at 1.15 x and loss at 0.85 x the Section 7.7 threshold; hold-still loss time within 0.1 s of 1.06 / 0.85 / 0.71; thresholds strictly increase from Night 1 to Night 3 at each level. |
| `AdaptiveAssistTests` | two losses raise L; four non-tug defenses raise L; two wins lower it; an easy win lowers it; floor -1 (Normal) and +2 (Story); mercy arms at L3 after two more losses and disarms after one contest; night-start carry `clamp(L - 1, 0, 1)`. |
| `CustodialRoundsTests` | watching the wrong camera never advances; not watching never advances; thresholds per config; reopen penalty; Night 2 route ends with `ReachedFinal`, Night 3 with `SeatCleared`, Story clamps at Middle. |
| `ContentOverlayTests` | replace, add and `removed` for files, emails, employees, orders, tasks; strings by key; story arrays empty versus non-empty; cumulative Night 3 load. |
| `WorkTaskManagerTests` (extend) | `OpenFile` and `ViewEmployee` complete from world queries; `Withdraw` hides a task, `Activate` cannot revive it, `Current` skips it. |
| `DialogueEngineTests` (extend) | `=ellen` matches "ellen" and "Ellen?" but not "excellent"; tags come back with the reply; old categories unchanged. |
| `AuthCodeTests` | `0217`, `217`, `2:17`, `02:17`, `2 17 am` accepted; `1234`, empty, `0218` rejected. |
| `TemplateTests` | every token replaced for both Gary branches; player lines lose non-ASCII characters and braces and are cut at 40 characters. |
| `SaveDataTests` | v1 to v2 migration; checkpoint round-trip; New Game keeps records and settings. |

### 12.2 Content tests (`ContentTests`, extend)

1. Load base, base + night2, base + night2 + night3; `Problems` empty each time.
2. Font coverage walks `Resources/Content` recursively (`SearchOption.AllDirectories`).
3. Cross-references per night: task targets are mails, files, orders or (for `ViewEmployee`) employees; `employeeRef` resolves.
4. Work order rule per night: `rule == ""` follows the Personnel rule with that night's employee statuses; `rule == "shelf"` matches `n3_shelves` (3340 approve, 3341 reject, 3342 approve).
5. Dialogue probes (the existing eleven phrases) produce lines for every exchange in every night; every exchange has fallback and silence.
6. Voice rules: Ellen lines (exchanges with voice `""`, their replies, fallback, silence, and line sets with voice `""`) match `^[A-Z0-9 ?]+$` and have 1 to 6 words; Gary lines contain no capital letters (except `g3c_seated`) and have 1 to 6 words; `system` is exempt.
7. Banned words anywhere in content: Microsoft, Windows, Recycle Bin, Explorer, WordPad, Minesweeper, Solitaire.
8. Every `{token}` in content is in the Section 2.4 list (or `{0}`, `{1}` format slots in strings).
9. `session.cfg` and `camview.cfg` contents end with `=0` and no trailing newline.

### 12.3 Test bridge additions (`Scripts/Editor/SecondCursorTestBridge.cs`)

```
night N                  # fresh shift at night N (boot)
jump N BEAT              # fresh shift at night N, beat BEAT (uses Prepare); "jump BEAT" keeps today's meaning (current night)
setflag NAME | clearflag NAME
trust VALUE              # seed entity trust
assist LEVEL             # force the assist level
tug win|lose|real        # force the outcome of the next contests (development builds only)
stage N                  # force the rounds stage
setclock H M
difficulty normal|story
waitending ID [timeout]
checkpoint save|load
```

### 12.4 Scripted regression

Run with `tug win` and `tug lose` where a fight decides the branch. Each script ends with `dump` and asserts flags and the save file.
1. Night 1 as today (existing script) plus: save has `m.n1.*`, `playerLines`, `currentNight = 2`.
2. Night 2 finished: `jump 2 finish`, `tug win`, shred 209; expect `m.n2.finished_gary`, 209 shredded, ending `n2_finished`.
3. Night 2 kept by archive and kept by deadline (two runs).
4. Night 3 SHRED, KEEP by confirm, KEEP by timeout, KEEP by clearing, LOG OFF by code and edit, LOG OFF by kept Gary, LOG OFF refused with `ALLOW_LOGOFF=0`.
5. Checkpoint: quit to title at each checkpoint beat, Continue, compare flags and world (files per folder, tasks per state).
6. Story difficulty: a full Night 3 with `tug real` driven by a straight 150 px/s scripted drag wins by the second contest.

### 12.5 Playtest protocol and acceptance

Five or more fresh players per build; read results from the tagged game log (`[STORY] Beat:`, `[ENTITY] Tug-of-war ended`, `Rounds: stage`, `Withdrew`).

| Metric | Target |
|---|---|
| Night 2 length | 20 to 24 min median |
| Night 3 length | 21 to 25 min median |
| First tug of each night lost | 60% or more of players |
| A tug won by the third attempt in any night where the player keeps trying | 90% or more |
| Longest stretch with no on-screen prompt or event | under 90 s |
| Restricted code solved without `n3_code_hint2` | 50% or more |
| Night 3 seat cleared (Normal) | 15 to 35% |
| Players who can say, after Night 3, what makes Custodial move | 70% or more |
| Players who can tell Gary's cursor from Ellen's at a glance | 90% or more |

---

## 13. Risks and open questions

1. **Scope.** This roughly triples the content. If time is short, cut in this order: Records screen, post-game echo, CAM 00 art (keep the secret as a NO SIGNAL feed with the label), kept Gary's camera-switching help.
2. **Append-only Notepad editing.** The config edits rely on Backspace removing the last character. Signposting: Ruth's mail names session.cfg, the Log Off refusal names the line, the file ends on it, and kept Gary does it in front of you. If playtests show confusion, add caret movement to Notepad later; it is out of scope here.
3. **Reading the watch rule.** If fewer than 70% of testers understand it, add one NEXUS toast after the second advance in Night 3 ("Custodial position updated: viewer active."), which states the correlation without explaining it.
4. **Three cursors at once.** Keep Gary visibly weaker (flicker, hand, amber) and quieter (no static loop). Never let both foreign cursors click at the same frame; the director sequences them.
5. **Idle KEEP.** A player who does nothing in the finale waits up to about 4.5 minutes. Ellen and Gary lines and the 6:50/6:55 camera events fill it. If playtests find it slow, fast-forward the clock after 60 s without player input. (Phase F: done; the clock runs to 7:00 over 15 s after 60 s without input, and a 6:58 feed flicker fills the 6:55 to 7:00 gap.)
6. **Checkpoint fidelity.** `Prepare` rebuilds the world from flags; any new beat side effect must set a flag or it will not survive Continue. Review each `Prepare` against Sections 4.5 and 5.7.
7. **Story Bible.** After implementation, update Section 10 (future nights) to this three-night canon, add the Night 2 and 3 text maps, and extend the writers' rule (Section 1, item 8).

