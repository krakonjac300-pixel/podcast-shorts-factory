# SECOND CURSOR: capture, consequence and replay

## Decision

Use a dramatic, readable capture and let players choose their replay cost. The primary loss action is **Retry Night N**. On the Night 3 loss screen, **Start from Night 1** is also offered. Starting a new run asks for confirmation, resets run progress, and keeps discovered endings, records, achievements and settings. Night 1 has only its single retry option because both choices would start at the same place.

The psychological rationale is a design inference, not a proven retention result for this game. Research connects game engagement with competence, autonomy and relatedness. It does not establish that forcing repeated work after a late death will improve investment. We want a player to understand the mistake and choose another attempt, with a full restart available for those who enjoy that pressure.

Sources:

- [A Motivational Model of Video Game Engagement](https://www.selfdeterminationtheory.org/SDT/documents/2010_PrzybylskiRigbyRyan_ROGP.pdf)
- [Xbox Accessibility Guideline 108: Game difficulty options](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/108)

## Gameplay changes

- Night 1 now has a real camera decision. Session 017 warns the player explicitly before the danger timer starts. The figure advances after three and six seconds of viewing, and commits to capture after nine seconds. Two uninterrupted seconds with Camera 03 closed, minimized, switched away or covered by the active application prevents capture. Pausing does not advance exposure.
- Ellen no longer repeatedly closes the camera for the player during this decision. She explains the danger and leaves the action to the player.
- Closing the feed lets Ellen end the shift and preserves the existing chapter-completion path. Getting caught bypasses `CompleteNight`, does not write the demo completion handoff, does not award Night 1 completion, and does not unlock Night 2.
- Captures on Night 1 and the Night 3 KEEP path use a 0.95-second visible rush with moving limbs and accelerating footsteps. The figure travels toward the actual camera lens instead of appearing there in a single cut.
- The committed attack keeps the camera maximized through impact. Pointer labels fade away and the operator's camera label is hidden during the rush so it cannot identify the attacker as the player.
- The existing synthesized `scare_hit` supplies the digital shriek and impact, followed by NO SIGNAL, CRT collapse, silence and a loss screen. No external audio asset was added. Softer-sound and reduced-flashing settings remain respected.
- Lost endings go to the result and retry card without an extended dialogue epilogue or blocking record page. Night 3 records are still saved and accessible from the title.
- Retry clears the stale checkpoint and reconstructs the current night from its beginning using the saved start-of-night state. It does not automatically advance to another night.

## Verification

- 712 core tests passed with zero failures or skips. New exposure tests cover capture timing, last-moment closing, interrupted viewing, and paused/invalid time increments. Result tests include camera capture and demo-available loss copy.
- The deployed source compiled in Unity with zero game compiler errors or warnings. The standalone demo C# compile check passed with zero errors or warnings.
- In an isolated save, watched the Night 1 warning through capture: Night 2 remained locked, no completion achievement was awarded, and Retry returned to Night 1.
- Used the actual camera close button during the warning: capture was avoided, the normal ending appeared, and Night 2 unlocked.
- Exercised the Night 3 loss menu: retry returned to Night 3. Canceling the full-restart confirmation returned to the menu. Confirming it restarted Night 1, reset run progression, and retained discovered endings, achievements and the previous retention record.
- Inspected successive rush frames and fixed the camera returning to its small window before impact. Checked the final full-size attack, loss card and confirmation layout.
- Verified four accelerating footstep requests, the normal impact, CRT-off and ringing requests. With softer sounds enabled, the request changed to `scare_hit_soft`; the reduced-flashing route reached the same loss card without errors.
- No game runtime errors were reported in these targeted checks. Ending setup used debug jumps; this was not an uninterrupted replay of every narrative branch.
- Final Windows build: succeeded in 53 seconds, 76.9 MB, zero errors. Final demo build: succeeded in 47 seconds, 76.6 MB, zero errors. Both reported three build warnings; game scripts reported no compiler warnings.
- Both final executables passed isolated 12-second headless startup checks. These verify startup and story initialization, not rendering; the Editor screenshots cover rendering.
- Restored real input, normal save selection, stopped play mode, full-game compilation mode and the original background setting after verification.

Playable builds:

- `D:/Downloads/Podaci/Project 1/Builds/Windows/SecondCursor.exe`
- `D:/Downloads/Podaci/Project 1/Builds/WindowsDemo/SecondCursorDemo.exe`

## Files

- `Scripts/Core/Game/CameraExposure.cs`: deterministic warning and survival decision.
- `Scripts/Core/Game/EndingResult.cs`: camera capture is a loss rather than chapter completion.
- `Scripts/Runtime/Story/Night1Director.cs` and `Night1Director.Reveal.cs`: branch survival and capture before progression is saved.
- `Scripts/Runtime/CameraFeed/SecurityCameraRig.Capture.cs`: movement and limb pose toward the lens.
- `Scripts/Runtime/Story/NightDirector.Scares.cs`: framing, footsteps, impact and cursor cleanup.
- `Scripts/Runtime/Story/Night3Director.Finale.cs`: use the shared rush for capture.
- `Scripts/Runtime/Story/EndingSequence*.cs` and `EndCard.cs`: prompt loss resolution and replay choices.
- `Resources/Content/dialogue.json`, `strings.json`, `full/strings.json`: warnings, failure explanation and restart copy.
- `DevTools/CoreTests/CameraExposureTests.cs` and `StoryClarityTests.cs`: regression checks.

## Manual playtest

1. Play Night 1 to Camera 03. When session 017 warns you to close it, keep watching. Confirm the approach, rush, shutdown and YOU LOST card. Use Retry Night 1.
2. Repeat and close the camera after the warning. Confirm the chapter completes and offers Continue to Night 2.
3. On Night 3, trigger the capture ending. Confirm Retry Night 3 starts that night from its beginning.
4. Try Start from Night 1, then Back. Progress must remain intact. Choose it again and confirm Start New Run. The run resets while discovered endings and records remain.
5. Repeat a capture with softer sounds and reduced flashing enabled.

Next evaluation: with new players, record whether they can explain the cause of death, which replay option they choose, and whether they attempt the challenge again. Compare that with observed playtime before considering a separate mode that requires restarting from Night 1.

The previous playable builds are preserved under `D:/Downloads/Podaci/Project 1/_work/backups/pre-capture-20261002`. Source backups are in the scratch workspace's `second-cursor-clarity/capture-originals`.
