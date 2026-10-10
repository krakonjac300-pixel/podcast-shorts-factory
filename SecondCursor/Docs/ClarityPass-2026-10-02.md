# SECOND CURSOR: player identity and outcome clarity

This pass is followed by `CapturePass-2026-10-02.md`, which adds a real Night 1 capture branch and explicit retry choices. Refer to that document for the current failure behavior.

## Project and scope

Updated the existing three-night game in `D:/Downloads/Podaci/Project 1`, using Unity 6000.6.3f1, URP 17.6.0, uGUI 2.6.0 and Input System 1.20.0. The matching source changes are in `_work/2026-09-29/repo/SecondCursor`. The older scratch-workspace foundation was not the game under review.

Reused the custom PixelText renderer, window system, cursor router, story directors, save system and ending sequences. No new desktop implementation, external AI, personal-data access or operating-system mouse manipulation was introduced.

## Installed tools

Official source: https://github.com/Unity-Technologies/skills

Installed `unity-cli`, `ui-ugui`, `project-auditor-fixes` and `optimize-audio` into the user's Codex skills directory. Applied the CLI play-mode verification guidance and uGUI layout guidance. The other two skills are available for later work; this pass did not perform a Project Auditor scan or an audio optimization pass.

Installed Unity CLI 1.0.0-beta.12 and the live project's editor connection package `com.unity.pipeline` 0.8.0-exp.1. The package supports editor tooling; it does not add game AI. These installed skills become available automatically in a fresh Codex session and were read directly during this pass.

## Changes

- Quick Start identifies Casey Rourke, WS-04, Office B-7 and the white player arrow. It distinguishes session 017 from optional company assistance.
- The opening email explains that Camera 03 watches the player's room from behind their chair.
- The player pointer keeps its YOU label throughout scenes with another visible cursor.
- Camera 03 identifies the seated person as YOU / CASEY ROURKE / WS-04. The empty-chair scene says YOUR EMPTY CHAIR / WS-04.
- Ellen's Batch 46 help waits for the first archived file instead of starting automatically after 30 seconds. She attempts to archive one file, leaving the other work for the player. The existing physical interaction and interruption rules remain in use.
- Ellen explains that she knows the job and can sort, while the player chooses who gets erased. Company task completion remains opt-in.
- File fights say FILE CONTEST WON or FILE CONTEST LOST, separating local contests from game outcomes.
- A five-second result screen follows shutdown before the epilogue. It is repeated on the final card. Nights 1 and 2 say NIGHT COMPLETE. The two trapped Night 3 endings say YOU LOST. Log-off says YOU ESCAPED, specifying escape from WS-04 while preserving the unresolved company mystery.
- End-card paragraphs measure their wrapped height. The outcome, details, thanks and ending slots no longer share fixed vertical positions. Buttons sit at the bottom of the card.

## Verification

- 707 core regression tests passed, zero failed or skipped, including new ending classification, orientation and pointer-label tests.
- Full and demo C# compilation checks passed before deployment.
- The live Unity Editor compiled the deployed changes with zero game compiler warnings or errors.
- Play-mode frame progression was confirmed through the Unity CLI.
- Used a separate save directory at `C:/Users/Kostarika/AppData/Local/Temp/sc-clarity-save-20261002`; restored normal save selection and real input afterward.
- Inspected screenshots of Quick Start, seated and empty camera labels, shared cursors, Night 1 and Night 2 completion cards, both losing endings and the escape ending.
- Exercised the real file drag: player moved batch46_a; Ellen moved batch46_b; batch46_c and batch46_d remained in Intake more than 40 seconds later. Ellen had remained invisible before the player's move, beyond the previous 30-second automatic start.
- Verified the YOU tag remained fully visible during that shared-cursor scene.
- Verified Return to Title, Continue to Night 2 and the retention-record Continue button.
- No game runtime errors were reported during these targeted checks. Debug jumps and accelerated time were used for ending coverage; this was not a fresh uninterrupted playthrough of every branch.
- Full Windows build succeeded in 72 seconds (76.9 MB); demo succeeded in 68 seconds (76.6 MB). Both reported zero errors and three warnings: the editor Pipeline connection has no player runtime configuration, and two unused Core debug shaders were stripped. No game-script warnings were reported.
- Both built executables passed separate 12-second headless startup checks with isolated saves and no detected exceptions. Headless checks do not validate rendering; rendering was checked in the Editor screenshots.
- Restored the Editor to stopped play mode, full-game compilation symbols, real input, default save selection and its original background setting.

Playable outputs:

- `D:/Downloads/Podaci/Project 1/Builds/Windows/SecondCursor.exe`
- `D:/Downloads/Podaci/Project 1/Builds/WindowsDemo/SecondCursorDemo.exe`

## Main files

- `Scripts/Core/Game/EndingResult.cs`: result classification, separate from ending names.
- `Scripts/Runtime/Story/EndingSequence*.cs`, `EndCard.cs`: shutdown result and final card layout.
- `Scripts/Runtime/Story/Night2Director.cs`: one-file, player-triggered help.
- `Scripts/Runtime/Input/PointerTags.cs`, `Core/Game/WhoIsWho.cs`: persistent player identification.
- `Scripts/Runtime/Apps/CameraApp.cs`: player and empty-chair labels.
- `Resources/Content/strings.json`, `full/strings.json`, `emails.json`, `night2/dialogue.json`: authored story copy.
- `DevTools/CoreTests/StoryClarityTests.cs`: regression coverage.

## Manual acceptance pass

1. Start a new Night 1 and read Quick Start. Identify who and where you are without opening Help.
2. During a shared-cursor scene, move your pointer and identify YOU and SESSION 017 at a glance.
3. On Night 2, wait at Batch 46. Ellen should not start sorting. Move one file to Archive and let her assist once. Finish the remaining files yourself.
4. Open Camera 03 and read the label above the seated operator. Check the empty-chair scene later.
5. Complete Night 1 or 2. Confirm NIGHT COMPLETE and use Continue.
6. On Night 3, stay logged in past the deadline for the capture route. After the monitor dies, confirm YOU LOST and its explanation. Use Title or Night Select to start again.
7. Test the log-off route and confirm YOU ESCAPED. Shredding 017 frees her at the player's expense and must display YOU LOST.

Backups of changed source files are in the scratch workspace's `second-cursor-clarity/originals`. Previous Windows builds are retained under the live project's `_work/backups/pre-clarity-20261002`.

Next: a fresh player should play without coaching and explain who they are, what each cursor wants, and whether each ending was a chapter completion, capture or escape. That validates comprehension beyond automated correctness.
