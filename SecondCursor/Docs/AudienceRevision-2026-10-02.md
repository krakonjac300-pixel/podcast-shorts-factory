# Audience revision, 2026-10-02

## Changed

- Opening orientation identifies Casey Rourke, WS-04, the white player arrow, the Work Queue and opt-in assistance. It no longer explains the other cursor's intentions.
- Title legend calls the dark arrow unknown. Login attributes the autofill to the workstation without identifying a session.
- Early window nudges and unexplained selection retain their behavior and developer logs, without explanatory notifications identifying session 017.
- Gary's early handover stays in Documents. Later entries move to handover_recovered.txt in Restricted, which the existing story unlocks after the confrontation and communication. Both entries use stable file IDs.
- Contested confirmations retain their actor icon, status line and first-encounter reading grace. Removed the decorative countdown, top race banner and duplicate result card. The existing cancellation notice still explains who clicked No or Cancel. A completed raced shred receives one result notice.
- Actual game-over screens, night retry options and file tug controls are unchanged.
- PC and Deck help now describe click-lock's initial held drag correctly.

## Files

Content: Resources/Content/strings.json and filesystem.json.

Runtime: Story/Night1Director.cs; OS/ConfirmRace.cs, SystemNotices.cs and Services.cs.

Tests: DevTools/CoreTests/StoryClarityTests.cs and PhaseRTests.cs.

## Verification

- 714 core tests passed, zero failed or skipped. The existing .NET 8 test target ran on installed .NET 10 with process-local DOTNET_ROLL_FORWARD=Major.
- RuntimeEditor compile check passed with zero warnings or errors.
- Unity Windows full build succeeded in 83 seconds; demo succeeded in 46 seconds. Both reported zero errors and three warnings: Pipeline has no player runtime configuration, and two Unity debug occlusion shaders were stripped. There were no compiler warnings in the game's scripts.
- Both rebuilt executables passed isolated headless startup checks with story initialization and no detected runtime exceptions. These checks did not exercise standalone rendering.
- The Editor was left stopped with normal input, no save override, runInBackground false, full-game content restored and no SC_DEMO define.
- Unity Play mode frame advancement confirmed through wait_for, not inferred from a screenshot.
- At the conflict checkpoint, the confirmation appeared with the compact layout and Restricted remained locked. The entity actually reached No and cancelled the shred, with one readable actor-attributed notice and no runtime errors.
- At the reveal checkpoint, handover_recovered.txt existed and Restricted was accessible.
- Screenshots reviewed: audience-race.png and audience-cancel.png under Library/SecondCursorBridge/shots.
- These were directed developer checks using an isolated save folder. They are not a fresh human playthrough or proof of mouse feel, sound quality, full three-night pacing or sales appeal.

## Manual acceptance in Unity

1. Open Assets/SecondCursor/Scenes/SecondCursor.unity and press Play. Use a fresh test profile or new night, preserving any existing personal save.
2. Start Night 1. Confirm the introduction explains your identity and immediate task without explaining the dark cursor's motive.
3. Open Documents and read handover_notes.txt. Later entries should be referenced but unavailable in locked Restricted.
4. Complete ordinary work and watch the first anomalies. There should be no notice naming session 017 for the tiny window nudge or unexplained selection.
5. Attempt to shred employee_017.dat. The first confirm teaches Yes versus No; subsequent confirms keep only their ordinary question and short actor status. Let the other cursor click No, then try again. Check the notice explains cancellation without implying game over.
6. Complete the confrontation and Jotter exchange. When Restricted unlocks, read handover_recovered.txt.
7. Enable Click lock. Begin dragging and hold for at least half a second before releasing. The file should stay held; the next click drops it.
8. As a regression check, ignore the camera warning until caught: confirm the capture and loss screen, then retry the same night. On another attempt, close the camera in time and confirm story progression. These ending behaviors were not changed in this revision.

## Next

Use UncoachedPlaytest.md with new human players. No claim that this revision is viral or worth $7 has been validated by this developer pass.
