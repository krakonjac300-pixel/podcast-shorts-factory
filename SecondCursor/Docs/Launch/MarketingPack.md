# SECOND CURSOR: Launch Marketing Pack

Dated 2026-09-29. Built from StoryBible.md, the game README and the content files (dialogue.json, story.json, emails.json, strings.json) plus EventDirector.cs, EndingSequence.cs, ConflictSystem.cs, EntityController.cs, SystemMonitorApp.cs, DialogueEngine.cs and VisualFx.cs. Every in-game line quoted here exists in those files. Items marked [DONE] were implemented in the game after this pack was written (see HANDOFF.md, polish pass 8).

## Read first

1. **Next Fest timing.** The October 2026 Steam Next Fest (Oct 19 to 26) closed registration on Aug 31 and its final submission deadline was Sept 28. Unless the game is already registered, the realistic slot is **February 2027: Feb 22 to Mar 1** (registration closes Jan 10, all items due Feb 8). Valve's rules: a public store page, a publicly playable demo live when the fest starts, a release date after the fest ends, one Next Fest per title. A fest title can't be a prologue, chapter 1 or demo version of existing content listed as its own product, so ship Night 1 as the demo of the full game, not as a separate product.
2. **Spoiler policy for studio marketing.** Never show or quote the head turn at the end of the CAM 03 reveal, the lines "I KEPT A COPY / OF YOU / SEE YOU TOMORROW NIGHT", or anything about Ellen Marsh. Studio posts may stop at "STILL THERE / GOOD". Streamers will show the full ending anyway, and the end card carries the wishlist call to action.
3. **Some beats are conditional.** Don't promise these in store copy: Ruth's "It did it to Gary too" email (only if the player waits 45 s without a shred attempt, or the fight ends without a shred), the "I KNOW YOU ARE THERE" silence lines (25 s without typing), the Notepad "DONT" (only if the player closes the window).

## 0. Angle lock

- **Core benefit:** a horror game where the scariest thing on screen is a mouse pointer that isn't yours.
- **Positioning:** for horror players and streamers who like dread built from ordinary things. The whole game is a fake 1998 office desktop where the ghost uses the mouse the way you do.
- **Campaign angle:** "Someone else is logged in." (the game's own end-card line).
- **Voice:** dry OS-notice voice, short declarative sentences, the impossible stated as routine, no exclamation marks. Entity lines quoted in caps exactly as the game types them.
- **Audience insights (to validate with demo feedback):** clip viewers respond to something happening to the streamer's hands and screen, not only jump scares; desktop-horror viewers worry a game will touch their real machine, so a clear safety line sells; a beige-and-teal 90s office UI reads as a hook even in a thumbnail.
- **Adjacent titles:** Hypnospace Outlaw (fake late-90s desktop, not horror), Pony Island (the interface fights back), Doki Doki Literature Club (interface horror that touches real files), Five Nights at Freddy's (camera dread), Papers, Please (bureaucratic tasks with a moral edge). None pairs a ghost that plays by your input rules with an explicit no-real-file, no-webcam promise. That is the gap.

## 1. Viral moments (ranked)

### 1. Wave at yourself (CAM 03)
- **Moment:** the second cursor types THEY WATCH YOU WORK, THEY WATCHED ME TOO, I CAN OPEN IT, LOOK AT YOU, unlocks the Camera Viewer ("Clearance override accepted: remote session."), opens CAM 03 itself: your own office from a high corner, a seated figure at WS-04, REC dot, timestamp. Move your mouse and the figure's arm moves with it.
- **Why:** every streamer tests it within seconds: a pause, a quiet "no", then a wave. Needs no context.
- **Clip:** 15 to 20 s. First 2 s: the feed already open, text "This camera is pointed at my chair", handcam of a hand on a mouse.
- **Caption:** "The security camera in this game is pointed at my chair. The arm on screen moves when my mouse moves."
- **Hit harder [DONE]:** while CAM 03 is open, each real click is echoed by a quiet, delayed mouse click, as if the room picked it up.

### 2. Closer every time you look again (reveal)
- **Moment:** static bursts, the door is ajar; another burst, a tall still figure in the doorway; room tone cuts, drone starts. The second cursor panics, races your cursor to the feed's close box and types DONT LOOK AT IT, IT MOVES WHEN YOU WATCH, DONT TURN AROUND, NOT YOU TOO, CLOSE IT. Each reopen: doorway, middle of the room, behind the chair.
- **Why:** a "one more look" loop that teaches its own rule. Chat and the ghost argue live: it types CLOSE IT while chat types LOOK.
- **Clip:** 25 to 30 s. First 2 s: the empty doorway with static, text "It only moves when I look."
- **Caption:** "the ghost keeps typing CLOSE IT. chat keeps typing LOOK. I looked."
- **Hit harder [DONE]:** the feed's timestamp freezes while nobody watches, and a low thump lands on the exact frame the figure changes.

### 3. Notepad opens by itself (communication)
- **Moment:** after 1.6 s of stillness the second cursor double-clicks the notepad and types STOP, NOT THAT FILE, PLEASE, one per line. Then the caret waits for you.
- **Why:** the ghost uses the same app you would; slow typing lets viewers read along.
- **Clip:** 10 to 15 s. First 2 s: a blank notepad opens by itself and the first "S" appears.
- **Caption:** "the second cursor opened the notepad by itself and started typing to me."
- **Hit harder [DONE]:** conversation text is shown at 2x size so it reads on a phone.

### 4. Type to it and it answers (communication)
- **Moment:** three exchanges with 21 to 24 keyword groups each. Examples: "make me" gets THE LAST ONE SAID THAT; "wtf" gets THEY LOG THAT TOO; "gary" gets HE TRIED TOO; "what is your name" gets THEY TOOK THAT FIRST; "are you a ghost" gets I WAS; "no" gets YOU WILL; "casey" gets I KNOW WHO YOU ARE, then 214. Silence for 25 s: YOUR CURSOR STOPPED, I KNOW YOU ARE THERE.
- **Why:** participation. Chat supplies inputs; every answer is short, caps, screenshot-ready. A repeatable series: "I asked the ghost X."
- **Clip:** 12 to 20 s per input. First 2 s: the typed line, then the reply arriving one letter at a time.
- **Caption:** 'I told the ghost "make me". It said "THE LAST ONE SAID THAT".'
- **Hit harder [DONE]:** new keyword groups for what streamers really type: "chat" (WHO IS CHAT / ARE THEY WATCHING TOO), "i love you" (DONT SAY THAT HERE), "lol"/"lmao" (KEEP LAUGHING / IT HELPS).

### 5. Tug-of-war (conflict)
- **Moment:** you drag employee_017.dat toward Disposal and the second cursor grabs it mid-drag. A dotted band stretches between the cursors, red as strain climbs; shake, glitches, a rising strain tone. Yank away to win; hold still and you lose. After the first loss a toast explains the fight.
- **Why:** physical. The streamer's arm jerks, chat yells PULL; both outcomes clip.
- **Clip:** 8 to 12 s. First 2 s: the file hanging between two cursors with the red band vibrating.
- **Caption:** "tug of war with a ghost. the ghost is winning."
- **Hit harder [DONE]:** a player win lands as a punch: a short hit-stop, the second cursor thrown back with a shudder, the snap pitched up.

### 6. Cursor body-blocking, both ways (conflict)
- **Moment:** it races you to No, drags the dialog out from under your cursor, goes for Cancel during the shred; park your cursor on Cancel and it jostles around yours until it gives up. It can block your clicks too.
- **Why:** the cursor is a body. "Get off my button" is instantly funny.
- **Clip:** 10 to 15 s. First 2 s: Yes and No, both cursors sprinting.
- **Caption:** "two cursors, one button. I am not moving."
- **Hit harder [DONE]:** a refused press rattles the button and plays a low thunk.

### 7. It comes back (conflict, win branch)
- **Moment:** you shred the file; 1.8 s of nothing, a glitch, employee_017.dat is back; a toast says it is in use by another user.
- **Why:** a reversal with a payoff in under three seconds.
- **Clip:** 8 to 10 s. First 2 s: the shred progress bar filling.
- **Caption:** "shredded it. it's gone. (it is not gone)"
- **Hit harder [DONE]:** the room tone ducks to silence when the shred completes; the file returns with a low thump and a highlight ring.

### 8. It replays your mouse (escalation)
- **Moment:** the second cursor replays your recorded ledger drag (about 6 s), then types THAT WAS YOU, EVERY MOVE IS KEPT, THEY ARE LEARNING YOU, SO AM I.
- **Why:** your own hesitations performed back by something else. SO AM I is the quotable line.
- **Clip:** 15 to 20 s. First 2 s: it picks up ledger_1994.dat, text "This is my mouse movement from earlier."
- **Caption:** "it just replayed my own mouse movement back at me. including the wobble."
- **Hit harder [DONE]:** the OS toast "Playback complete: WS-04 operator input." fires the instant the replay ends.

### 9. Blackout ending (ending)
- **Moment:** monitor power-off, 2.6 s of black silence; on black only the second cursor fades in and types STILL THERE, GOOD, I KEPT A COPY, OF YOU, SEE YOU TOMORROW NIGHT; end card: SECOND CURSOR, "Someone else is logged in.", blinking WISHLIST NOW.
- **Why:** the strongest chill, landing on a call to action.
- **Clip:** 25 to 35 s (trim the silence to about 1.5 s). First 2 s: the monitor collapsing to a dot.
- **Caption (creator):** "I finished the game and it typed this to me." **(studio):** "Still there? / Good."
- **Hit harder [DONE]:** end card says "Thanks for playing the demo." and offers a Wishlist on Steam button once the store URL is configured.

### 10. An email from yourself, dated 1987 (escalation)
- **Moment:** From Casey Rourke, to Casey Rourke, "(no subject)", Mon 03/02/87 2:17 AM, body "remain seated". The Voss shred order is also stamped 2:17 AM.
- **Why:** a perfect screenshot; lore hunters argue about the two 2:17s.
- **Clip:** 8 to 12 s. First 2 s: the bold unread row with your own name as sender, then the date.
- **Caption:** "got an email from myself. dated 1987. it says 'remain seated'."
- **Hit harder [DONE]:** the arrival toast shows sender and date.

### 11. First entrance (presence)
- **Moment:** after small anomalies, an inverted cursor enters from the right edge carrying employee_017.dat, drops it, hovers, leaves. Toast: "New pointing device detected." The tray shows two mice.
- **Why:** the cold open; the premise in one second. Best used in the trailer.
- **Clip:** 8 to 12 s. First 2 s: a dark cursor sliding in from the right edge dragging a file.
- **Caption:** "a second cursor just walked in and dropped a file on my desktop."
- **Hit harder [DONE]:** the room tone ducks as it crosses the edge and the tray icon flashes from one mouse to two.

### 12. You can't turn it off (any time)
- **Moment:** Shut Down refused ("Open sessions on this workstation: 2"). System Monitor lists remote_session.exe, user 017, memory "????"; End Process fails twice, then says "The process is not running." and glitches.
- **Why:** deadpan comedy in the game's own voice; an easter egg streamers find on their own.
- **Clip:** 12 to 18 s. First 2 s: the process row reading 017 and "????".
- **Caption:** "tried to end task on the ghost (in-game computer, mine is fine). it said the process is not running."
- **Hit harder [DONE]:** on the third refusal the row's CPU shows 97% while the dialog says it isn't running.

### Cross-cutting
- **Unused notes [DONE]:** story.json anomaly notes now surface as OS toasts at the fight, replay and reveal beats ("Session 017 is still open." during the tug-of-war).
- **Stream readability:** consider an option that scales both cursors 2x.
- **Vertical layout for studio posts:** put the 16:9 game frame across the top of a 1080x1920 canvas (about 1080x608), burned-in captions of the typed lines beneath, a handcam of the mouse hand at the bottom. Do not center-crop: the action spans the full width.
- Capture at 1920x1080 (exact 2x of the 960x540 screen, crisp pixels).

## 2. Trailer

Steam autoplay is often muted, so every key beat lands with sound off. No music: the game's own clicks and key taps are the rhythm; one low drone enters at 0:23, one hard low_thump at 1:03, the end tone on the title. Spoiler policy applies.

| Time | Shot | On-screen text | Sound | Job |
|---|---|---|---|---|
| 0:00 to 0:04 | BIOS lines type in fast; hold on "Pointing Device 2 ... OK", then "WARNING: Previous session was not closed." | (the BIOS is the text) | fan hum, drive ticks, one flat beep | Hook |
| 0:04 to 0:09 | Log On dialog, click, desktop appears | "1:52 AM. First solo shift." | startup chime | Who, when |
| 0:09 to 0:17 | Work montage, 1 to 1.5 s cuts: briefing, ledger to Archive, Personnel lookup, Approve, temp file to Disposal, Yes | ARCHIVE. / VERIFY. / SHRED. | clicks as rhythm | The boring |
| 0:17 to 0:23 | A window shifts, your cursor twitches, employee_017.dat highlights itself | "Files do not move on their own." | hum dips, one soft click | First doubt |
| 0:23 to 0:30 | Dark cursor enters carrying employee_017.dat, drops it, leaves; toast; tray goes to two mice | none | low drone begins | The premise |
| 0:30 to 0:38 | URGENT order, Confirm Shred, both cursors sprint for Yes and No, tug-of-war with the red band | "If it types to you, do not reply." | strain tone rising | Conflict |
| 0:38 to 0:47 | The notepad opens itself: STOP, NOT THAT FILE, PLEASE. You type "who are you". It types I WORK NIGHTS, LIKE YOU | none | key taps only | The hinge |
| 0:47 to 0:53 | It replays your ledger drag; THAT WAS YOU, then SO AM I | none | drone holds | Escalation |
| 0:53 to 1:03 | LOOK AT YOU; CAM 03 opens itself; you wave, the arm follows; static: door ajar; static: figure in the doorway; IT MOVES WHEN YOU WATCH | "CAM 03" | room tone cuts, drone up | The payoff |
| 1:03 to 1:06 | Close the feed, reopen: figure mid-room; DONT TURN AROUND | none | one hard low_thump | The sting |
| 1:06 to 1:09 | Black | none | silence | Breath |
| 1:09 to 1:15 | A white arrow blinks in on black; a dark arrow appears beside it; SECOND CURSOR; "Someone else is logged in."; "Wishlist on Steam" + release window | title, tagline, CTA | end tone | One CTA, hold 3 s |

Do not show the figure behind the chair or the head turn. 30 s cutdown: 0:00 to 0:04, 0:23 to 0:30, 0:30 to 0:38, 0:53 to 1:03, title.

**15 s teaser (works muted):** 0:00 to 0:02 BIOS "Pointing Device 2 ... OK" / "Previous session was not closed."; 0:02 to 0:05 drag employee_017.dat, a dark cursor enters and grabs it; 0:05 to 0:08 tug-of-war, red band, shake, hard cut; 0:08 to 0:11 STOP typed, then PLEASE; 0:11 to 0:13 CAM 03 static cut, figure in the doorway; 0:13 to 0:15 SECOND CURSOR, "Someone else is logged in.", "Wishlist on Steam". A/B test the opener: BIOS versus the tug-of-war.

## 3. Steam store page copy

**Game name:** SECOND CURSOR. **Tagline (social, not on the capsule):** Someone else is logged in.

**Short description (256 characters):**
Night shift, 1998. You archive and shred files on a retro desktop until a second cursor starts fighting you for one. It types to you. It answers what you type. Then it shows you the security camera. Horror played entirely inside a fake computer.

**Long description:**

**Someone else is logged in.**
It is 1:52 AM in the basement of Letheworth Data Reclamation. This is your first solo night shift. The job is simple: archive the finished files, check the work orders against Personnel, shred the junk. Shift ends at 7:00. Then a second cursor shows up.

**THE JOB**: The whole game is one screen: NEXUS OS 4.1, a 1998 office computer with Mail, a File Manager, Personnel records, Work Orders, a Disposal bin, a System Monitor and a Camera Viewer. You click, drag and double-click, and you type when asked. That is all the control you get.

**THE SECOND CURSOR**: It carries a file onto your desktop, drops it and leaves. Then you are ordered to shred that file. It has other plans.
- It grabs the file while you drag it. Yank your mouse away hard to win it back. Hold still and you lose.
- It races you to the "No" button on the confirm dialog.
- It goes for Cancel while the file shreds. Park your cursor on top of it and it has to find another way.
- It uses the computer the way you do, so your cursor can get in its way. Its cursor can get in yours.

**IT TALKS, AND IT LISTENS**: It opens the notepad by itself and types to you, a few letters at a time. Type back and press Enter. It answers what you type. Swear at it. Refuse. Ask about the last operator. Replies are written lines matched to your words; nothing is generated while you play.

**THE CAMERA**: You were told the Camera Viewer was off limits. The second cursor opens it anyway. Camera 03 is pointed at your office. You see the back of your own head, and the seated figure's arm follows your mouse. Look away. Look again.

**BUILT TO STAY INSIDE THE GAME**: Everything happens inside the fake computer. The game does not read your files, use your webcam or microphone, or move your real mouse.

**Key features:**
- **A whole 1998 office computer.** NEXUS OS 4.1 in pixel art, with a CRT look you can switch off.
- **A second cursor that plays by your rules.** It grabs files from your hand, races you to buttons, and can be blocked by your own cursor.
- **Type to it. It answers.** Every reply is an authored line matched to your words.
- **The camera is pointed at you.** Camera 03 shows your office from behind, and each time you look again something is closer.
- **Safe by design.** No file access, no webcam or microphone, no real-mouse control.

**Content notes:** psychological horror; themes of surveillance, a missing employee, deceased employees, being replaced; death implied, never depicted; no gore; a tall figure on a security camera; long silences and a few sudden loud sounds (jump scares), the loudest in one of the endings; no voice acting and no in-game profanity (players can type their own words, the game only reacts).

**Photosensitivity notice:** the game contains screen glitches and tearing (0.05 to 0.4 s each), screen shake, film grain, CRT flicker, bursts of static and a flickering light in the security-camera feed, and a bright flash when the monitor powers off. A Reduce flashing option (Esc menu) tones glitches, flashes, shake and flicker spikes down, and also softens the sudden loud sounds; CRT effects can be switched off. Run a flash-analysis tool (for example PEAT) on captures of the CAM 03 reveal and the blackout before publishing, and adjust this notice to match.

**Suggested tags:** Psychological Horror, Horror, Atmospheric, Pixel Graphics, Retro, Mystery, Story Rich, Singleplayer, Indie. Avoid "Hacking". Test "Point & Click" before using it.

**Screenshots, in order (1920x1080, gameplay only, 4 or more all-ages):**
1. Two cursors, one file: the tug-of-war with the red band (all-ages).
2. The notepad conversation: STOP / NOT THAT FILE / PLEASE, with a reply half typed.
3. The desktop at rest with File Manager, Mail and Work Queue open (all-ages).
4. CAM 03 with the figure in the doorway (not behind the chair; may be marked not all-ages).
5. Confirm Shred with the dark cursor over No (all-ages).
6. The 1987 email from Casey Rourke to Casey Rourke (all-ages).
7. System Monitor with remote_session.exe, user 017 (all-ages).
8. The BIOS: "Pointing Device 2 ... OK" and "Previous session was not closed." (all-ages).

Capture with CRT effects on, at an exact 2x scale.

## 4. Social hooks (launch week)

| Day | Channel | Hook | Visual | CTA |
|---|---|---|---|---|
| 1 | X, Bluesky | "Pointing Device 1: OK. Pointing Device 2: OK. You only plugged in one mouse." | BIOS clip | Wishlist |
| 2 | TikTok, Shorts, Reels | "The ghost has a mouse. So do I. The file is in the middle. Pull." | Moment 5 with handcam | Wishlist |
| 2 | X | "1:52 AM: you log on. 2:01 AM: IT says the cursor is a driver fault. 2:17 AM: you are ordered to shred one file. That is Night 1." | three email screenshots | Wishlist |
| 3 | X, Bluesky, r/horrorgames | "From: Casey Rourke. To: Casey Rourke. Date: 03/02/87. Body: remain seated. You are Casey Rourke." | the email | Wishlist |
| 3 | X | "remote_session.exe. User: 017. Memory: ????. End process: 'Access is denied.' Again. Again: 'The process is not running.'" | System Monitor | Wishlist |
| 4 | X, TikTok | "Type anything to the second cursor. It answers. Reply with what you would type." | Moment 4 | Play the demo |
| 5 | TikTok, Shorts | "This camera is pointed at my chair. The arm on screen moves when my mouse moves. It only gets closer when I look." | Moments 1 into 2 | Wishlist |
| 6 | r/indiegames, r/Unity3D | "The ghost in my horror game is a second cursor going through the same input code as yours. That means you can body-block it." | Moment 6 | Wishlist |
| 7 | X, creator outreach | "A horror game about a fake computer that never touches your real one." | disclaimer over a desktop shot | Wishlist |
| 7 | X, Steam announcement | "New pointing device detected. (You did not plug anything in.)" | toast and tray icon | Play the demo |

A/B test moment 5 against moment 1 as the first short. Hook 4 only works if someone answers replies with the game's real response.

**Next Fest demo angle:** "Night 1, start to blackout, in 10 to 15 minutes. Play it, then tell us what you typed." Sequence: about 14 days out ("The demo is a full shift. It starts at 1:52 AM. It ends when the screen goes black."), about 7 days out (trailer, "Demo live [DATE]"), day 1 ("The demo is live. Someone else is logged in."), mid-fest (best real thing someone typed to the ghost), last day ("Next Fest ends [DATE, TIME]"; do not imply the demo disappears unless it does).

**Creator note (five things to try on stream):** wave at CAM 03; type "make me" to the cursor; park your cursor on Cancel while the file shreds; try Shut Down; End Process on remote_session.exe three times. Add the safety line: the game never touches files, webcam or your real mouse.

## 5. Capsule art brief

**Concept: two cursors, one file** (the app icon already uses the motif).
- **Main capsule 1232x706:** CRT-lit desktop background, teal center glow (#3E7A6E at about 40%) falling off to dark green-slate (#2B3D3A into #243330) with a soft vignette; 1 px scanlines or none at small sizes. Center: one .dat file icon; a white arrow with a black outline enters from the left, a near-black arrow (#0B0E0D) with a pale outline (#E6ECEA) from the right, both tips on the file; a short dotted band between them, pale near the cursors and red (#B03328) at the file. Each cursor about 35 to 40% of the capsule height, over the lit center. Logo: SECOND CURSOR in the game's bitmap font, off-white (#F7F5EE), two lines across the lower third, CURSOR with a 1 px offset echo (skip at small sizes). Title only: no tagline, "Coming Soon" or "Wishlist" on capsules.
- **Header 920x430:** same crop, logo about 70% of the width.
- **Small 462x174:** logo on one line at about 90% width, a single overlapping arrow pair behind the last letters; drop the file and band. Valve derives 184x69 and 120x45 from this.
- **Vertical 748x896:** logo top, tug-of-war center, empty dark desktop below.
- **Library assets (600x900 capsule, 3840x1240 hero, 1280x720 logo):** hero with the two cursors small and far apart across a wide empty center, logo in the left third. Confirm sizes on Valve's Library Assets page.
- **Do not use:** a screenshot montage, tiny UI details, the figure, faces, blood, or anything that looks like another company's OS.
- **Test:** shrink to 184x69 and 120x45 (two arrows, one white one dark, and SECOND CURSOR must still read), then check in grayscale.

## Open questions and risks

- Missing facts: release window and date, price (see MarketResearch.md), system requirements, languages, controller support, the IARC age-rating questionnaire.
- Scope: promise three nights, not more.
- Naming: the in-game text editor was renamed from "Notepad" to "Jotter" to avoid any trademark question.
- AI disclosure: Steamworks asks about AI-generated content used in development; answer it from how the text, art and code were actually made.

Sources: [Steam Next Fest October 2026](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/2026october), [Steam Next Fest February 2027](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027), [Store Graphical Assets](https://partner.steamgames.com/doc/store/assets/standard)
