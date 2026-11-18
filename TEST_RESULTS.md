# TEST_RESULTS — Living AI Partner (session of 2026-11-16, 00:53–09:00 KST)

**Read this first.** No Quest headset was connected during this session (`adb devices` empty). Nothing below was observed through a headset. The MR side was exercised with a **desktop MR simulator** that runs the same `MrApp` code against the real Windows client and the real host; such results are marked `sim`. They prove protocol, state machine, shared-clock timing, clipping and PC-side behaviour. They do **not** prove hand-tracking usability, optical alignment through the lenses, or perceived latency. Unit-test success is never reported as cross-device success.

Verification levels: UNIT · EDITOR · WINDOWS_BUILD (standalone Windows player on the real desktop) · QUEST_DEVICE · CROSS_DEVICE. Results: PASS · FAIL · NOT_TESTED · BLOCKED_EXTERNAL.

## Environment

Windows 11 Home 26100 · RTX 5060 · Unity 6000.3.13f1 (Windows Mono + Android modules) · .NET SDK 8.0.419 · Claude Code CLI 2.1.325 (logged in) · three displays: `DISPLAY1` 1080×1920 portrait at (−1080,−832), `DISPLAY2` 1920×1080 primary, `DISPLAY3` 1920×1080 at (0,−1080); EDID sizes 598×336 mm / 336×598 mm. A full-screen game was running on the primary display, so desktop tests used `DISPLAY3` and `DISPLAY1`.

## Summary by requirement

| ID | Implementation | Highest level reached | Result | Evidence / note |
|---|---|---|---|---|
| R01 independent transparent Windows app | IMPLEMENTED | WINDOWS_BUILD | PASS | `artifacts/gate1-crop.png`, `gate3-bound.png`, `pc-bound-portrait-monitor.png` (no black rectangle, anti-aliased edge over a game, a browser and an app window) |
| R02 transparent parts do not block the mouse | IMPLEMENTED | WINDOWS_BUILD | PASS | `artifacts/gate1-overlay-verify.txt`: layered + click-through + no-activate; hit-test at the character's centre returns the window underneath; overlay never foreground |
| R03 core operation by hand on Quest | IMPLEMENTED | — | NOT_TESTED (BLOCKED_EXTERNAL) | APK builds with hand tracking; no headset. Mouse-as-ray in the simulator is not evidence |
| R04 same asset and real shared task state | IMPLEMENTED | WINDOWS_BUILD (sim) | PASS at this level | one prefab (`Partner.prefab`) in both the Quest scene and the Windows scene; activity/tasks/modules come from host snapshots. On device NOT_TESTED |
| R05 different valid positions selectable | IMPLEMENTED | WINDOWS_BUILD (sim) | PASS at this level | 25 distinct portal pixels on two displays; an exit chosen on a different display makes the partner walk there inside the PC first; invalid spots are moved with a stated reason. Hand pointing NOT_TESTED |
| R06 natural approach | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim) | PASS at this level | stance-foot slip ≤ 1.2 mm (unit); acknowledge → look → turn → walk/run seen in `portal-roundtrip-unedited.mp4`. Judged in the simulator view only |
| R07 MR→PC sequential, connected pass | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim) | PASS at this level | 30 completed entries; `enter50-pc.png` (upper body + cut cross-section inside the screen at 50 %), unit order head ≤ torso ≤ legs ≤ feet for 48 geometries. Through the headset NOT_TESTED |
| R08 PC→MR same quality + landing | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim) | PASS at this level | 27 completed exits; `exit50-pc.png` (legs still inside at 50 %). Through the headset NOT_TESTED |
| R09 no two wholes, no gap, no pop | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim) | PASS at this level | state-machine test "never two whole characters"; continuity test (largest step < 32 mm / 8.3 ms, largest rotation step < 14°); recovery runs below |
| R10 per-monitor position/scale, window moves | IMPLEMENTED | UNIT + WINDOWS_BUILD | PASS | portrait display at negative coordinates used for 12 crossings; window move of (+500,+140) px moved the partner by exactly (+500,+140) px (`gate3-window-tests.txt`); portal re-anchored when the window moved during the approach. Mixed DPI scaling could not be exercised: all three displays are at 100 % |
| R11 minimise / restore / occlusion | IMPLEMENTED | WINDOWS_BUILD | PASS | minimised → `mode=Hidden`, overlay not visible, binding kept; restored → same window, same position; another window raised above the workspace covers the partner (`gate3-occluded.png`, z-order `other > overlay > workspace`) |
| R12 disconnect / recovery without duplication | IMPLEMENTED | WINDOWS_BUILD (sim) | PASS at this level | `artifacts/recovery-tests.txt` A–E |
| R13 pointing + utterance | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim; spoken recording fed through the microphone path) | PASS at this level; with a real voice and a real tracked hand NOT_TESTED | while the scripted simulator hand pointed at pixel (1382,−583) of `DISPLAY3`, the recording "모모야, 저기 화면 안으로 들어가 볼래?" was sent; it was transcribed, understood as *go into the screen*, and the partner went in at (1382,−583), 0.2 px from the pointed pixel (`artifacts/voice-checks.txt`, V3). The pointed spot is the centre of the dwell during the utterance (5 unit tests). Earlier, with a typed sentence: `DISPLAY3` uv (0.30,0.40) → pixel (576,−648) |
| R14 real screen + project analysis with evidence | IMPLEMENTED | WINDOWS_BUILD (real harness) | PASS | `artifacts/harness-e2e.txt`: window capture + Claude Code session read the screenshot and 3 source files; 4 proposals with file:line evidence; 1 honestly marked "inferred" |
| R15 returns and presents MR modules from the analysis | IMPLEMENTED | WINDOWS_BUILD (sim) | PASS at this level | `artifacts/mr-sim-modules.png` (4 cards from the real task + timer panel). In the headset NOT_TESTED |
| R16 chosen proposal → real change + tests | IMPLEMENTED | WINDOWS_BUILD (real harness) | PASS | branch `partner/task-ff4d4e16`, `styles.css` +11/−9 (`artifacts/sample-change-task-ff4d4e16.diff`), `node test.js` exit 0 |
| R17 publishing matches result and approval | IMPLEMENTED | WINDOWS_BUILD (local remote) | PASS for a local bare remote; GitHub NOT_TESTED | before approval the remote had only `main`; after approval it had the task branch at `8100fdb`; `main` unchanged; user's checkout clean |
| R18 timer stored and re-run by language | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim) | PASS at this level | create → "아까 만든 타이머 25분 시작" reuses the same module; pause/resume/reset/set-duration/display; correct after host restart (9:55 → 4:22 after 5 min 34 s); completion fires once |
| R19 reacts to real camera observation | IMPLEMENTED | — | NOT_TESTED (BLOCKED_EXTERNAL) | needs Quest 3/3S camera access. The simulator never fabricates observations |
| R20 hand ride + rain shelter | IMPLEMENTED | UNIT + WINDOWS_BUILD (sim, scripted hand) | PASS at this level | `artifacts/feature-tests.txt`: beside the head → still EXPOSED; above → SHELTERED; removed → EXPOSED. Ride: on-hand, carried, held 0.4 s after tracking loss then steps down, slides off a tipped hand. Real hands NOT_TESTED |

## Gate status

| Gate | Status |
|---|---|
| 1 — transparent Windows character + Quest character | Windows half PASS (WINDOWS_BUILD). Quest half: APK builds, **not run** |
| 2 — chosen-position two-way portal | PASS at WINDOWS_BUILD with the simulator as MR side. **Not passed on device** |
| 3 — robust to positions, monitors, window changes, failures | PASS at WINDOWS_BUILD with the simulator |
| 4 — point → real analysis → return → MR proposals | PASS at WINDOWS_BUILD with the real harness and the simulator |
| 5 — proposal → real change → tests → approved git | PASS against a local bare remote; GitHub push not tested |
| 6 — observation, hand interaction, rain, timer | timer PASS; hand ride and rain PASS with scripted simulator hands; camera observation NOT_TESTED |

## Re-verification after the redesign and the scene/prefab refactor (03:26 → 04:10 KST)

Three things changed after the results above were first recorded, all on the owner's feedback during the session: the character became a simple toon mascot (eyes only), the UI and portal got a motion-graphics look, and every Unity object moved out of runtime code into scenes and prefabs with serialized references. Because that touched the character proportions, every renderer and all UI code, the checks below were run again on the new builds. Captures taken before 03:26 (`gate1-*`, `enter50-pc.png`, `exit50-pc.png`, `gate3-*`, `mr-sim-modules.png` …) show the **old** character and UI; they remain valid for what they demonstrate (transparency, clipping, z-order), not for the current look.

| Check | Level | Result | Evidence |
|---|---|---|---|
| Unit tests with the new proportions (crossing passes within the clearance of the chosen point, sequential passage, continuity) | UNIT | PASS 280 / 280 | `dotnet test`; clearance radius is now 0.50 H |
| Authored assets complete: 6 prefabs, 4 scenes, every `[SerializeField]` on LivingPartner components assigned | EDITOR | PASS (0 unassigned) | `artifacts/scene-build.txt`, `artifacts/hierarchy.txt`, `artifacts/hierarchy-mrsimulator-editor.png` (edit-mode camera capture of the authored scene) |
| No runtime object creation or lookups in player code (`Find*`, `GetComponent`, `new GameObject`, `AddComponent`, `Instantiate`, `Resources.Load`, `new Material`) | static check | PASS: 1 match in 32 files, and it is inside `#if UNITY_EDITOR` | `artifacts/no-runtime-lookups.txt` |
| Portal loop, new look (before the refactor) | WINDOWS_BUILD (sim) | PASS 10 / 10, clock error PC ≤ 1.5 ms, sim ≤ 1.9 ms, 60 fps | `artifacts/portal-loop-new-look.txt` |
| Portal loop, scene/prefab build | WINDOWS_BUILD (sim) | PASS 10 / 10 at 5 positions (RunLeap 5, DiveFlip 5), clock error PC ≤ 1.8 ms, sim ≤ 2.1 ms; second run after the last authoring pass 6 / 6 | `artifacts/portal-loop-hierarchy.txt`, `portal-loop-hierarchy-2.txt` |
| Portal loop on the portrait display with negative coordinates (04:18 build) | WINDOWS_BUILD (sim) | PASS 6 / 6 at 5 positions, clock error PC ≤ 1.4 ms, sim ≤ 1.4 ms; no exception in the PC client's `Player.log` or in the simulator's `mr-sim.log` for that run | `artifacts/portal-loop-final-build.txt` |
| Regression on the last Windows build (05:05, toon water) | WINDOWS_BUILD (sim) | PASS: MR feature checks 4 / 4 (rain / shelter decisions unchanged, hand ride, timer, two-pinch selection); portal loop 4 / 4 (clock error PC ≤ 1.5 ms, sim ≤ 1.0 ms); unit tests 280 / 280; no exception in either log. Recovery cases, window follow, pairing and the display moves were not repeated on this build: it differs from the 04:42 build only in the rain drawing code, the shader and the new `Rain/Water` objects | `artifacts/mr-feature-checks.txt`, `portal-loop-water-build.txt` |
| Full regression on the 04:42 Windows build (display-move fix, pairing check hooks) | WINDOWS_BUILD (sim) | PASS: portal loop 6 / 6 (clock error PC ≤ 1.5 ms, sim ≤ 1.8 ms); MR feature checks 4 / 4; recovery cases A–E as expected; no exception in either log | `artifacts/portal-loop-final-build-display3.txt`, `mr-feature-checks.txt`, `recovery-tests-hierarchy.txt` (`mr-feature-checks.txt` holds the latest run only; `recovery-tests-hierarchy.txt` holds both the 04:05 and the 04:45 run, one after the other) |
| Recovery cases A–E, scene/prefab build | WINDOWS_BUILD (sim) | PASS all five: window moved (re-anchored, completed), minimised (Hold → Resume → completed), MR killed during approach (Hold → reconnect → completed once), MR source killed at 50 % (PC completed alone), PC destination killed at 30 % (Recovering → Cancelled, owner MR, client restarted) | `artifacts/recovery-tests-hierarchy.txt` |
| Shipped copies as a user would start them: `dist\Start-LivingPartner.ps1 -LoopbackOnly` (published host → Windows client), R01 / R02 again | WINDOWS_BUILD | PASS: host ran from `dist\LivingAIPartner-Host`, client connected, the character stood on the real desktop of the portrait display at 60 fps (wallpaper and a desktop icon visible around it, no rectangle); overlay is layered + click-through + no-activate, a hit-test at its centre returns the desktop underneath, and the foreground window was unchanged | `artifacts/shipped-copy-smoke.txt`, `artifacts/shipped-copy-pc-character.png` |
| MR features on the scene/prefab build, scripted simulator hands (`tools\Run-MrFeatureChecks.ps1`) | WINDOWS_BUILD (sim) | PASS all four: rain on → `exposed`; hand 11 cm beside the head → still `exposed`; palm up-wind of the head → `sheltered`; hand removed → `exposed`. Hand ride: on the palm after 7 s, stays while carried, slides off a tipped hand. Timer by utterance: start → pause → resume on the pooled panel. Pointing: first pinch only previews, second pinch on the marker starts the transition, completes in the PC, returns. (Two earlier runs of this script reported the rain part as failed because the script sent the command before the simulator had connected, and then a wrong command name; the product was not changed for that.) | `artifacts/mr-feature-checks.txt` |
| A carrying hand must not operate the UI | WINDOWS_BUILD (sim) | Found in the first run of the script above: the scripted hand that was carrying the partner brushed a proposal card and its fingertip pressed "이걸로", which started a real change task (it stopped at `ReviewRequired`; nothing was pushed). Fixed: a palm-up or carrying hand cannot poke buttons. Re-run: no task was started | `artifacts/mr-feature-checks.txt`, NEXT_STEPS §5 |
| First-run pairing through the hand numpad (`tools\Run-PairingFlow.ps1`): the simulator is started without any token, like a device that was never paired | WINDOWS_BUILD (sim, scripted fingertip) | PASS: the numpad appeared; six wrong digits + "연결" → host answered `wrong code` and the numpad stayed; the six digits of the host's current code + "연결" → `pair attempt: ok`, token issued, client connected 12 ms later. Each key was pressed by moving the scripted fingertip onto the button object (the same poke test as a real fingertip). The test pairing was removed afterwards. This was the first time the numpad ran in this session | `artifacts/pairing-flow.txt`, `artifacts/pairing-numpad.png` |
| WorkspaceBound on the last build, real window on the portrait display (`tools\Run-WindowFollowChecks.ps1`), R10 / R11 again | WINDOWS_BUILD | PASS: bound to the window it entered; window moved by (−300, +120) px → partner moved by exactly (−300, +120) px; minimised → `mode=Hidden`, not visible, binding kept; restored → same place; moved back → same pixel as at the start. (The first run of this script reported the last comparison as failed: it took long enough for the partner to start its idle wander, 41 px sideways. The script now finishes its comparisons before that; the product was not changed.) Occlusion by another window was not repeated on this build | `artifacts/window-follow-checks.txt` |
| Body frozen 50 % through the display, both directions, new character, last build (`tools\Capture-HalfThrough.ps1`) | WINDOWS_BUILD (sim) | PASS at this level: while the host reports `Crossing` with the clock stopped, the real desktop shows only the half that is inside the screen with its cut cross-section (entering: head and upper body; leaving: lower body and feet) and the simulator shows only the other half in front of the glass, at the same portal. After `play` both crossings completed. How well the two halves line up through a headset is NOT_TESTED | `artifacts/half-through-enter-pc.png`, `-enter-mr.png`, `-exit-pc.png`, `-exit-mr.png`, `half-through.txt` |
| Redesign after the owner saw the captures (10:50): English UI, picture-book look, thin natural rain | EDITOR play mode + WINDOWS_BUILD (sim, scripted hand) | PASS at this level. No Korean text is left in any user-facing string of the clients or the host (search for Hangul in the UI / host sources finds only the accepted input phrases in `IntentParser`); panels, buttons, captions and the partner's lines are English (host log: "03:00 timer started!", "I paused the timer."). Panels are cream paper with a thin brown outline, pastel pills and ink lettering; the portal ring is honey-coloured. Rain is drawn as thin soft streaks with fine ripples where it lands; the hand that blocks it still gets the ripples, droplets and a nearly clear sheet of water on top. MR feature checks 4 / 4, portal loop 4 / 4 (clock error PC ≤ 0.9 ms, sim ≤ 3.2 ms), unit tests 280 / 280, no exception in either log, the shader compiles without messages (a first version used `pow` on values that can be negative, which Unity warned about and which can break on mobile GPUs; replaced before these builds). Recovery cases, window follow, pairing, display moves and the harness runs were not repeated after this redesign. Whether this is the "fairy-tale" feel the owner meant is their call; the font is still Unity's built-in one | `artifacts/storybook-rain-hand-shelter.png`, `storybook-rain-exposed.png` (Editor play mode, 1920×1080), `sim-ui-*.png`, `half-through-*.png` (built player), `mr-feature-checks.txt`, `portal-loop-storybook-build.txt` |
| (Designs superseded at 14:50 by the owner's model, next row; the typeface stands) Six character designs and the project's own typeface (owner's request at about 11:15; builds of 11:54 / 12:05) | UNIT + EDITOR + WINDOWS_BUILD (sim) | PASS at this level. **Designs:** `tools\Run-DesignChecks.ps1` sets each of `momo`, `bunny`, `cat`, `fox`, `sprout`, `penguin` and reads back three values — the host state, the design the Windows client reports it is rendering and the design the MR client reports: all equal for all six; `nextDesign` (what the hand-menu **Friend** button sends) walks the full cycle and wraps; an unknown id (`dragon`) is refused and nothing changes. A crossing frozen at 50 % with the fox look shows the fox's head and ears inside the real screen and its legs and dark feet in front of the glass in the simulator; both crossings then completed. Regression on the same build with the penguin look: MR feature checks 4 / 4, portal loop 6 / 6 (clock error PC ≤ 1.5 ms, sim ≤ 1.1 ms), unit tests 283 / 283 (3 new: design cycle). **Typeface:** 108 glyphs in two weights generated without warnings; every label in the built player uses it (hand menu, calibration panel, caption, PC speech bubble); a caption containing Korean switches that label to the system font and the Korean glyphs appear; bubbles and panel bodies are now sized and wrapped from measured glyph advances. The search for runtime object creation / lookups in player code was repeated: still only the one editor-only line, now over 35 files. Two defects were found by looking at the captures and fixed before these builds (defects 20 and 21 below). Not repeated on this build: recovery cases, window follow, pairing, display moves, harness runs — pairing, task panels and proposal cards therefore have **not** been seen with the new font and measured wrapping. How the designs and the letters look through a headset is NOT_TESTED | `artifacts/character-gallery.png`, `font-specimen.png` (Editor, 1920×1080), `design-checks.txt`, `design-*-mr.png`, `half-through-fox-*.png`, `portal-entry-fox.mp4` + `portal-entry-fox-sheet.png` (one unedited 16 s screen capture of an entry at normal speed, recorded 12:12: simulator window on the left, the real desktop with the transparent character on the right), `pc-say-bubble.png` (real desktop), `sim-ui-*.png`, `sim-ui-caption-fallback.png`, `sim-ui-caption-long.png`, `mr-feature-checks.txt`, `portal-loop-designs-font-build.txt` |
| The owner's own character (a Tripo model, static T-pose mesh + texture, placed in `artifacts/Character_Reference` at about 13:55) as the partner; builds of 14:35 (Windows), 14:41 (Quest APK), host published 14:37 | UNIT + EDITOR + WINDOWS_BUILD (sim) | PASS at this level. **Rig:** the skeleton was re-measured on the model and the mesh skinned automatically (10 shells, 30 openings closed, 523 of 5 348 positions marked as enclosed; `artifacts/scene-build.txt`); 283 unit tests pass with the new proportions (crossing order head → torso → legs → feet, no foot slip, clearance). Every shared pose was rendered in the Editor and looked at (`artifacts/partner-pose-sheet.png`). **Cut by the screen:** in the Editor the body was cut at five moments of a crossing and the two parts are complementary, with a solid cut face (`artifacts/partner-cut-sheet.png`). In the running apps a crossing was frozen at 25 %, 50 % and 75 % in each direction (`tools\Capture-HalfThrough.ps1`): the real desktop shows only what is behind the glass, closed by the cut face, and the simulator shows only what is in front of it (`artifacts/tripo-half-through-sheet.png` and the twelve `tripo-half-through-*-{enter,exit}-{pc,mr}.png`). **Round trip:** one unedited recording, room → PC → room, in front of a plain backdrop (`artifacts/tripo-portal-roundtrip.mp4`, frames of the entry in `tripo-portal-entry-sheet.png`). **Regression on this build:** design check PASS (`design-checks-tripo.txt`), portal loop 10 / 10 crossings complete with shared-clock error PC ≤ 1.6 ms, sim ≤ 2.2 ms (`portal-loop-tripo.txt`), MR feature checks 4 / 4 - rain shelter, hand ride, timer, two-pinch selection (`mr-feature-checks-tripo.txt`); MR UI states and the partner standing on the desktop were captured (`artifacts/tripo-ui/`, `tripo-pc-standing.png`); the status light on the clasp was rendered in the Editor (`partner-status-light.png`). **Not repeated on this build:** recovery cases, window follow, full-screen suspension, pairing, calibration flow, display-to-display moves, harness runs. The bezel-climb entry was not triggered. The APK was rebuilt and not installed. | files named in the previous cell |
| Voice conversation, pointing check and motion pass (owner's request at about 14:48; builds of 15:29 Windows, 15:36 host, 15:37 Quest APK) | UNIT + EDITOR + WINDOWS_BUILD (sim) | PASS at this level. **Was there an AI system before?** Only the code-analysis harness and a keyword parser; there was no conversation and no voice. They were built now with the owner's OpenAI key (`gpt-5.4-mini`, `gpt-4o-mini-transcribe`, `gpt-4o-mini-tts`; the key is stored DPAPI-encrypted outside the repository). **Voice** (`tools\Run-VoiceChecks.ps1`, the user's voice = recordings from `samples\voice` fed through the microphone path, 5 / 5): V1 an English spoken command created and started a 5-minute timer and the MR client played the spoken reply; V2 Korean small talk was answered in Korean with no action; V3 speaking while pointing went in 0.2 px from the pointed pixel; V4 spoken to inside the PC it came back out and the PC client played the reply; V5 a scripted pinch on the partner put it into `listening` with the real microphone open, cancelled with nothing sent. From the end of speech to the start of the spoken reply: about 3 s (transcription 0.5–1.0 s, reply 0.8–1.4 s, speech 1.2–2.6 s). **Typed turns** on the host alone: a timer command, pause, a question it cannot answer ("what is on my screen?" → it says it has to look first) and a request outside its abilities (declined). **Pointing** (`tools\Run-PointingChecks.ps1`): 27 / 27 spots on the three displays (one rotated, two with negative coordinates) understood within 0.05 px of where the scripted ray pointed; point + pinch + pinch went in at exactly the pointed pixel on `DISPLAY3` and on the portrait `DISPLAY1`. The Quest's own hand ray is now shared code with 9 unit tests. **Motion**: 297 unit tests pass (stance foot still does not slide with the new waddle and bounce); frames of every motion were rendered and looked at (`artifacts/partner-motion-sheet.png`, `partner-pose-sheet-v2.png`). **Regression on this build**: MR feature checks 4 / 4, portal loop 6 / 6 (clock error PC ≤ 1.5 ms, sim ≤ 5.7 ms), design check PASS. The APK manifest now has `RECORD_AUDIO` (`artifacts/quest-apk-badging-voice-build.txt`); it was not installed. **Not done**: nobody spoke into the real microphone in a check; nothing ran on a Quest; recovery, window-follow, pairing and harness checks were not repeated; the motion was judged from still frames and one recording, not by watching it for long. | `artifacts/voice-checks.txt`, `pointing-checks.txt`, `voice-listening-mr.png`, `voice-speaking-mr.png`, `voice-reply-sample.wav`, `mr-feature-checks-voice-build.txt`, `portal-loop-voice-build.txt`, `design-checks-voice-build.txt` |
| (Superseded look) Virtual rain first redrawn as toon water, and its splashes landing on top of the hand that blocks it (owner's request at about 05:00) | EDITOR play mode + WINDOWS_BUILD (sim, scripted hand) | PASS at this level. With the scripted hand held over the partner: drops stop at the hand, splash crowns, beads and glints appear on its upper surface, a rippling film of water with an ink rim lies on it and drops fall from its edge; no drop reaches the partner below, the desk around shows splash crowns. Without the hand: drops reach the partner and the desk, the partner cowers. Shelter decisions are unchanged (`exposed` / `exposed` beside the head / `sheltered` above it / `exposed`). On a real hand in passthrough the film and splashes are placed 16 mm above the tracked palm joint on the side facing the rain; whether that sits on the skin is NOT_TESTED | `artifacts/toon-rain-hand-shelter.png`, `toon-rain-exposed.png` (1920×1080 captures of the simulator camera in Editor play mode), `sim-ui-rain-sheltered.png`, `sim-ui-rain-exposed.png` (built player), `mr-feature-checks.txt` |
| MR UI states rendered from the authored objects (simulator's own screenshots) | WINDOWS_BUILD (sim) | rain drops with the partner exposed and sheltered, pointing marker with its label and the display outlines, calibration guide panel (hand menu hidden meanwhile), caption bubble above the head: all present. Layout was only judged in the simulator's camera view; several panels overlap there (see Known gaps) | `artifacts/sim-ui-rain-exposed.png`, `sim-ui-rain-sheltered.png`, `sim-ui-pointing-marker.png`, `sim-ui-calibration-panel.png`, `sim-ui-caption.png` |
| Pressing Play in the Unity Editor on `MrSimulator.unity` (the Editor acting as the MR client against the running host) | EDITOR | PASS: scene starts, connects to the host, partner is placed on the desk, hand menu / cards / timers appear; no error logged during play. In the Editor the Game-view mouse is the pointing hand (not exercised by a script) | `artifacts/editor-playmode-mrsimulator.png` |
| Second real harness: Codex CLI adapter (`--harness codex`), same pipeline, same sample | WINDOWS_BUILD (real harness, sim as MR) | PASS on the third attempt. Run 1 FAIL: prompt was not valid UTF-8 for the CLI (defect 15). Run 2 FAIL, reported honestly as `TaskFailed: the harness finished but no file was changed`: the prompt forbade shell commands, which is how Codex edits (defect 16). Run 3: analysis in 30 s with 3 proposals marked `confirmed`; change on branch `partner/task-13124526` (`styles.css`, 4 insertions / 5 deletions, commit `cf9ecf0`), `node test.js` exit 0, `ReviewRequired`; remote had `main` + the earlier task branch before approval and additionally `partner/task-13124526` after it; `main` untouched; user's checkout clean | `artifacts/harness-e2e-codex.txt`, `artifacts/sample-change-codex-task-13124526.diff` |
| Look on the real desktop and in the simulator | WINDOWS_BUILD | character, status pill, panels, timers and proposal cards render from the authored objects | `artifacts/hierarchy-pc-character.png`, `hierarchy-sim-window.png`, `hierarchy-sim-working.png`; `portal-roundtrip-final-build.mp4` + `portal-roundtrip-final-build-sheet.png` (one continuous 34 s screen capture of the last build, recorded 04:51: simulator window above, the real sample window with the transparent character below; MR→PC, then PC→MR); `portal-entry-slowmo-quarter-speed.mp4` + `portal-entry-slowmo-sheet.png` (the entry with the host's crossing clock at 0.25×: both renderers follow the slowed clock, the video itself is not slowed) |

## Measurements

**Portal crossings (host log, `artifacts/transitions.jsonl`, summary in `artifacts/transition-stats.txt`)** — 200 transitions recorded in the session: **194 Complete** (100 MR→PC, 94 PC→MR), 6 not completed. 107 of them (105 Complete) were made after 03:26 with the new character; 90 of those (88 Complete) on the scene/prefab builds from 04:01 on.

- Displays: `DISPLAY3` 148, `DISPLAY1` (portrait, negative coordinates) 52. Anchors: window 103, screen 97. Variants chosen from geometry: RunLeap 104, DiveFlip 93, BezelClimb 3 (once early in the session with the old character, twice at 04:55–04:56 with the new one). 52 distinct portal pixels.
- Scripted loops: 10/10 at 5 positions early in the session (`artifacts/portal-loop.txt`); 10/10 with the new look; 10/10 and 6/6 on the scene/prefab builds; 6/6 on the portrait display and 6/6 on the last build (`artifacts/portal-loop-final-build.txt`, `portal-loop-final-build-display3.txt`).
- Shared-clock error (largest difference between the plan time a renderer reported and the host clock; 187 completed crossings, i.e. all except 7 debug slow-motion / long-freeze runs): **PC max 6.8 ms / mean 0.93 ms; MR simulator max 3.6 ms / mean 0.88 ms**. The 6.8 ms is a single crossing, `tr-e1579fc9d2` at 04:45, in which the target window was being moved by the recovery test during the approach; every other crossing stayed at or below 2.4 ms on the PC. For the 104 such crossings since 03:26: PC max 6.8 ms (the same one) / mean 0.96 ms, simulator max 2.6 ms / mean 0.90 ms. Before the clock-sync fix (pongs time-stamped on the network thread instead of once per frame) the measured round trip was 18.5 ms; after it 0.5 ms.
- PC presentation latency (render → layered window): 32 ms (two read-back frames) at 60 fps. From 02:47 the PC renders the pose for *now + measured read-back + 8 ms*, so that latency is compensated in the plan time; the compositor's own delay is an estimate, not a measurement.
- The 6 non-completions: 3 × "approach timed out" early in the session (two defects, both fixed: the hidden render host never rendered the receiver, so `ReceiverReady` was correctly never sent and the partner stayed in MR; the walk stopped 4 mm short of the launch point), 3 × intentional renderer kill (recovery test E, run at 02:29, at 04:06 and at 04:47 on the last build; all ended `Cancelled` with the partner whole in MR, which is the expected result).
- In 3 of the completed transitions no MR client was connected at the end: the simulator had been killed mid-crossing on purpose (recovery test D, three runs) and the PC finished alone. No Quest took part in any transition.

**Recovery (`artifacts/recovery-tests.txt`, simulator as MR)**

| Case | Result |
|---|---|
| A. Target window moved during the approach | portal re-anchored to the window's new position, crossing completed there |
| B. Target window minimised during the approach | `Hold (target window minimised)`; after restore `Resume`, completed |
| C. MR client killed during the approach | `Hold (sim disconnected)`; after reconnect resumed, completed once, no duplicate |
| D. MR (source) killed with the body 50 % through | PC finished alone, owner = PC; returning MR client stayed hidden |
| E. PC (destination) killed with the body 30 % through | first run **FAIL** (crossing completed instead of rewinding: the grace period after a clock restart also delayed reaction to a disconnect). Fixed. Re-run: `Recovering` → `Cancelled`, owner = MR, host restarted the client |

**Unit tests** — `dotnet test Companion/LivingPartner.Tests`: 280 passed, 0 failed (coordinate chain incl. negative coordinates, rotation, different pixel pitch, physical layout ≠ Windows layout; 48 crossing geometries × start/end, continuity, sequential passage, passes within the clearance of the chosen point, freeze points, rewind; gait slip; state machine idempotency and visibility; timer; intents; rain shelter; clock sync; protocol).

**Builds**

| Artefact | Result |
|---|---|
| `dist/LivingAIPartner-Windows/` | last rebuilt 11:54 (six designs, LPStory typeface; picture-book look, English UI, natural rain) from the scenes `Main` + `PcCharacter` + `MrSimulator` (≈100 MB), runs |
| `dist/LivingAIPartner-Host/` (self-contained, incl. `lpctl.exe`) | last republished 12:05 with the design commands (`setDesign`, `nextDesign`), both harness adapters and English lines (≈170 MB); this publish was not started from `dist\` afterwards. The scripted checks ran the same code from `Companion\…\bin\Release`; the published copy was started with `dist\Start-LivingPartner.ps1` for the smoke test in the table above (the 04:17 publish; the 04:43 publish adds only the loopback pairing-code query and the cost wording) |
| `dist/LivingAIPartner-Quest/LivingAIPartner.apk` | last rebuilt 12:05 from the single scene `QuestMR` (49 MB: six designs, LPStory typeface, picture-book look, English UI, natural rain); manifest last checked with aapt2 on the 11:07 build (nothing that affects the manifest changed since) (`artifacts/quest-apk-badging.txt`): `com.oculus.permission.HAND_TRACKING`, `com.oculus.feature.PASSTHROUGH`, `horizonos.permission.HEADSET_CAMERA`, arm64-v8a, minSdk 32, supported devices include quest3 / quest3s. **Never installed or run.** OpenXR's own project-validation step throws a NullReferenceException in `MetaQuestFeature` during the build; the build still succeeds |

**Other behaviour checked on the real desktop (`artifacts/feature-tests.txt`)**

| Item | Result |
|---|---|
| Display-to-display move inside the PC, side by side (`DISPLAY3` → `DISPLAY1` and back), own `MonitorTransfer` state | PASS |
| Display-to-display move, stacked (`DISPLAY3` ↔ `DISPLAY2`, through the shared edge) | First recorded at 04:33 once the lower display was free: it arrived, but the recording showed a defect (19 below). After the fix, recorded again at 04:38: PASS in both directions: the body crouches, travels straight through the shared edge while both displays draw it, and lands (down) or makes a small hop above the floor (up). `artifacts/monitor-transfer-stacked-down.mp4`, `-up.mp4`, the `-sheet.png` frame strips and `monitor-transfer-stacked.txt` |
| Exit requested on another display than the partner is on | PASS: walks there first, then comes out there |
| Bezel-climb variant | chosen and completed once early in the session (requested (−800,900) was moved to (−800,856) with a reason, anchored to a window), and twice more at 04:55–04:56 on the last build with the new character (same request, resolved to (−800,855), `variant=BezelClimb total=3.99s`, both completed and returned). The PC side of one of them is recorded: the body comes through, rises above the opening, turns over and lands on the window's floor. `artifacts/bezel-climb-new-character.mp4`, `-sheet.png`. How the climb looks on the MR side was not captured |
| Analysis finishing while the workspace window is minimised | PASS with the **labelled test double**: partner stays hidden, announces that the result is ready, returns only when called |
| Starting the clients does not take the keyboard focus | PASS (foreground window identical before and after) |
| Hand selection flow through the real pointing code (scripted simulator hand): point → pinch → preview only → pinch again on the marker → go | PASS: nothing started after the first pinch; the second pinch started the transition at the pointed spot |
| Monitor calibration flow (4 lit corners + desk) driven by a scripted simulator fingertip (`artifacts/calibration-flow.txt`) | PASS for the flow and the PC markers: four 26 px native markers sat exactly on the lit pixel corners of the display being calibrated (e.g. (−1080,−832) … (−26,1062) on the portrait display), the calibration was saved (336×598 mm, residual 0.0 mm), a display could be skipped, markers were removed afterwards. Fingertip accuracy on a real screen is NOT_TESTED |
| PC half follows the viewer's head (off-axis projection), body frozen 60 % through | PASS at this level: moving the simulated head +0.42 m / −0.43 m sideways shifted the PC half +32 px / −26 px in the same direction, consistent with a depth of about 2.5 cm behind the glass |
| Speech caption above the PC character | PASS (`artifacts/pc-caption.png`) |
| Suspension while a full-screen app owns the display (`tools\Run-FullscreenSuspendCheck.ps1`, 04:55, once the game was no longer running) | PASS: with a borderless window covering `DISPLAY3` in the foreground the client reported `mode=Suspended`, not visible, reason "full-screen app in the foreground on this display"; two seconds after it closed the partner was visible again in the same place. The stand-in was a plain black window, not a game in exclusive full-screen mode. `artifacts/fullscreen-suspend-check.txt` |
| Suspension on the secure desktop (UAC prompt, lock screen) | implemented, **NOT_TESTED** (cannot be triggered from this session) |

## Defects found and fixed during the session

1. Unity does not render cameras for a hidden player window → overlay camera is rendered explicitly.
2. Walk stopped within 4 mm instead of on the target → exact arrival.
3. Clock sync sampled once per frame (±8 ms) → network-thread timestamps.
4. Tilted monitor: the path inside the PC could poke back through the glass → plan re-defined in the portal frame (surface is exactly z = 0); 280 tests.
5. Perpendicular entry hid the PC half behind the MR half from in front → oblique (37–49°) entry.
6. PC destination loss during a crossing completed instead of rewinding → fixed (case E).
7. Feet cut off by the window edge due to parallax → viewer-aware floor correction.
8. After a build-target switch Unity dropped the `RuntimeInitializeOnLoad` registrations → first worked around by registering apps by name; later removed altogether: each client now has its own authored scene and nothing is registered or created at start-up.
9. MR home position computed before calibrations arrived (partner and panels on the floor) → recomputed when monitors/desk become known.
10. Simulator window re-centred itself over other windows → position re-applied after the resolution change.
11. Starting the players took the keyboard focus from the app in use → focus is handed back.
12. The headset side acted on a stale PC position (PC status was not forwarded) → forwarded on meaningful change.
13. With the new toon proportions the body no longer passed within the clearance of the chosen point (unit test failed) → dive pose and clearance radius (0.50 H) adjusted; 280 tests pass.
14. The ink outline width was applied in world units and swallowed a 10 cm character → scaled by the character height.
15. Harness prompts were written to the child process in the console code page; Korean text made the Codex CLI reject its input ("input is not valid UTF-8") → stdin is UTF-8 for both adapters.
16. The change prompt forbade shell commands, which is how Codex reads and edits files, so a Codex change ended with no file changed (reported honestly as `TaskFailed`) → the read / edit rules in the prompts are now supplied by each adapter, and a "no file was changed" failure quotes what the harness said.
17. Structural, not functional: every Unity object was created by runtime code in an almost empty scene → rebuilt as scenes and prefabs with serialized references (see the re-verification table).
18. The fingertip of a hand that was carrying the partner could press a panel button it brushed against (it started a real change task in a test) → a palm-up or carrying hand cannot poke buttons.
19. Stacked display-to-display move: at the hand-over the body was drawn for one frame about 400 px away on the display it had just left (the clip rectangle still belonged to the old display), its shadow stayed behind on the old floor, and coming up it overshot the floor by more than its own height → the move is now continuous in desktop pixel space (same pixel before and after the hand-over, both displays drawn while the body straddles the edge), the shadow is hidden while the body is below a floor, and the hop above the floor is about 0.4 body heights. The same stale-clip frame was removed from the side-by-side move.
20. Character mesh assets were not really updated when a design's shape changed: the authoring step overwrote an existing mesh with `EditorUtility.CopySerialized`, which kept the OLD vertex buffer when the vertex count differed, so the penguin's new face marking was drawn with stale vertices (stripes across the face in the first gallery capture) → meshes are now copied through the Mesh API, and the build of the partner prefab fails if a saved mesh does not have the vertex count its definition gives.
21. With the owner's model (found while looking at renders, before any build was kept): (a) cutting the body showed the hollow between bonnet and head and the outlines of the shells inside it → the cut is now closed by one flat face drawn where a stencil count says the line of sight enters the body, and enclosed surfaces are drawn in the same colour without ink lines; (b) the model's texture is black on surfaces its generator never saw, which showed under the body in a crouch → those surfaces and the fans that close the openings take the colour of the visible surface next to them; (c) the ink outline showed as a black strip where the body sinks into the feet in a crouch → the outline is set back along the line of sight; (d) an outline was left behind by a part that had not reached the glass yet → the outline is cut where the surface is cut; (e) colours for the new materials were converted to linear twice, which made the cut face orange instead of cream → colour properties are now given in sRGB.
22. Found while building voice and looking at motion: (a) the spot for "go in there" was taken from one sample at the edge of the pointing dwell and could be 30 px off → it is now the centre of the dwell (unit-tested); (b) the reply drifted to the language used earlier in the conversation → the language of the latest message is decided by the host and stated to the model; (c) while listening or speaking the partner looked at the screen the hand happened to point at and showed the user its back → in a conversation it looks at the user and turns to them; (d) standing was almost motionless and the short legs showed no stepping → the body now carries the motion (breathing, weight shift, waddle, bounce, steps when turning); (e) the somersault after a crossing read as tumbling with this big-headed body → replaced by a simple righting.
21. With the new measured line wrapping, a panel that was still popping open (scale animating from zero) measured its text as zero wide, so the calibration text ran out of the panel in one line → widths are measured in the label's parent units, independent of the animation.
22. Found while updating the documents: `RUNBOOK.md` still named the hand-menu and panel buttons in Korean although the UI has been English since 10:50 → corrected to the real labels.

## Tennis and review / reading (owner's requests of about 16:10 and 16:50; builds: Windows 17:01, host 17:13, Quest APK 17:13)

All of this ran on the Windows build with the desktop MR simulator as the headset. Nothing here ran on a Quest.

**Review of a screen and reading files** — `tools\Run-ReviewChecks.ps1` (17:02, `artifacts/review-checks.txt`), real OpenAI calls (`gpt-5.4` to look and read, `gpt-image-2` to redraw). The sample screen and the sample PDF carry flaws put in on purpose (`tools\Make-ReviewSamples.ps1`), so the answers can be checked.

| # | What | Result |
|---|---|---|
| R1 | A window is "pointed at" (the simulator's scripted selection): the partner crosses into THAT window, the host captures it and reviews it | PASS — task `review`, 5 notes after 49 s from the request (crossing included; the look itself 16 s, the redraw 21 s). Saved `review.pdf` 516 kB, `revised.png` 1065 kB, `feedback.png` 67 kB. The notes name the planted flaws: the "Creat your acount" headline, the pale labels, the invisible fields, the "Sumbit" button and the mismatched buttons |
| R2 | Called back out, it brings the notes | PASS — owner back in MR; the simulator view shows five note cards (each with **Open**) and the "Review · saved on the PC" panel (`artifacts/review-mr-cards.png`) |
| R3 | A PDF given outright is read | PASS — task `read` in 16 s; `notes.pdf` and `revised.pdf` saved; the facts are found (budget 4,200,000 KRW, launch 2026-12-03) and the planted contradiction is raised (the "one week before the launch" meeting dated 2026-12-10, after the launch) |
| R4 | A question about that file afterwards | PASS — "how much of the budget is for marketing?" → "1,500,000 KRW is for marketing." |
| R5 | The file is found from the title of the window showing it | PASS — window "meeting-notes.pdf - Sample Viewer" → the file found by name under Documents and read |

Also run by hand on the host alone: the sample picture given as a file (`lpctl readfile …signup-form.png`: 35 s, same three outputs), and one Korean request (`artifacts/read-sample-notes-ko.pdf`: Korean summary, key points and suggestions; the follow-up "방금 읽은 파일에서 출시일이 언제야?" → "2026-12-03이야."). Evidence: `artifacts/review-window.pdf`, `review-window-feedback.png`, `review-window-revised.png`, `read-sample-notes.pdf`, `read-sample-revised.pdf`, `review-sample-*.png`.

What the review did NOT get right, as seen in these runs: the redrawn `revised.png` is an image model's version of the screen - in R1 it fixed the headline and the button text but left two label typos ("Email adress", "Confirm pasword") as they were, and its proportions differ a little from the original (it is drawn at 1536x1024 whatever the window's shape). The marks on `feedback.png` are placed by the vision model and are approximate (a box can sit a little off its element). The PDFs are pages drawn as pictures: the text in them cannot be selected.

**Tennis** — `tools\Run-TennisChecks.ps1` (16:45 and again 17:07 on the final build, `artifacts/tennis-checks.txt`). The user's hand is the simulator's scripted open hand.

| # | What | Result |
|---|---|---|
| T1 | Asked from the room while the partner is on the desk | PASS — it goes into the nearest screen by itself, then the game starts (court 598x336x508 mm on DISPLAY3) |
| T2 | The hand returns every ball | PASS — strokes by both sides alternate (first run: 12 by the hand, 15 by the partner in 40 s, longest rally 17; second run: 7 / 10 in 30 s, longest 9); the PC client reports mode `Tennis`; the partner misses now and then and the point goes to the user |
| T3 | The hand stops playing | PASS — every serve bounces twice or flies past, the partner reaches 5, "game over", the court closes and the partner is back on the desktop floor |
| T4 | A second game stopped from the room in mid-rally | PASS — ended within 5 s |
| T5 | Afterwards | PASS — the partner comes back out to the desk |

Seen in captures: the court on the real monitor with the eye above it (`artifacts/tennis-pc-court-2.png`), a 12 s recording of a rally on the PC side (`artifacts/tennis-pc-court.mp4`: the ball, its shadow, the swing, the ball leaving through the glass as a cut disc), the room side in the simulator with the ball in front of the screen, its shadow on the desk and the ring on the hand (`artifacts/tennis-mr-sim.png`), the swing poses (`artifacts/partner-tennis-sheet.png`). Unit tests: 9 on the ball's path (through the opening, off the frame, bounces, continuity, the same answer whenever asked).

Regression on the same builds (17:07-17:13): MR feature checks 4/4, portal loop 3 round trips, pointing 27 spots worst 0.0 px and both go-ins exact. Not repeated on these builds: voice checks, recovery, window-follow, pairing, harness end-to-end.
## First run on a headset (Quest 3, Android 14, 2026-11-16 17:35-17:52, USB cable)

The first time anything of this project ran on a device. What is listed is what the logs of the headset and of the host show; nobody has yet reported what it LOOKED like, and no screenshot of the app itself was taken.

| What | Result |
|---|---|
| Install (`adb install -r`, 50.6 MB) and start | PASS — the app starts, the OpenXR session reaches FOCUSED, the hand-tracking and passthrough extensions are listed, no exception in the log in any of the runs |
| Connection to the PC over the cable (`adb reverse tcp:47610`, host with `--usb-dev`) | PASS — `role=quest device=Quest 3` connected; the pairing numpad was NOT exercised (the cable path skips it) |
| Tracking as the app reports it | head tracked, both hands tracked, 72 fps |
| Hand UI | buttons were pressed on the device and reached the host: the timer panel (pause, duration twice, start), and on the hand menu **Calibrate** (calibration began on DISPLAY1), then **Skip** (DISPLAY2) and **Debug**. Whether those presses were all intended is not known |
| Calibration | NOT completed — begun twice, no corner was committed before the headset was taken off |
| Portal, pointing, voice, review cards, tennis on the device | NOT_TESTED (they need a calibrated monitor first) |

Found and fixed during this run:
- The runtime announces the tracking origin every time a session starts or resumes (three times within two seconds of the first start). Each announcement used to start a new "space epoch", which would have invalidated the saved calibration every time the headset was taken off and put back on. Announcements within 6 s of a start or a resume now keep the epoch; only one in the middle of a running session (a recentre, a redrawn boundary) starts a new one. Seen working in the next run ("calibrations kept").
- An app cannot be started from the PC while the headset is not being worn: the system is at its lock screen and the start request times out ("Timeout while requesting window placement"). `tools\Start-Quest.ps1` waits until the headset is worn, then starts the app and follows the session.
- With no calibrated screen the app showed nothing but the passthrough and the hand menu, and nothing said what to do. It now says "Hold your LEFT palm up towards your face to open the menu, then press Calibrate." until a screen is calibrated.
- Buttons were pressed by fingers that were only near them. A button fired when an index fingertip was within its radius, from any side. In the headset the hand menu sat 11 cm above the left palm, among the fingers of that same hand, and its bottom row (Talk) was pressed over and over by the hand holding it (three "listening ... nothing was heard" in a minute); fingers on their way to another button pressed what they passed. A press is now the fingertip going THROUGH the face of the button from the user's side; the hand a hand menu sits on cannot press it; the menu is 18.5 cm above the palm. (Installed 18:10; not yet tried on the device.)
- The calibration panel with Skip / Finish floated in front of the user, in the path of the hand reaching for the screen corners: Skip was pressed three seconds after calibration began. On the headset the panel is now above the left palm (only while that palm is turned to the face), the instruction for the current corner is a line of text with nothing to press, each captured corner is confirmed with a sparkle and "Got the top-left corner.", one pinch counts once (0.7 s), and calibration starts with the primary display. (Installed 18:10; not yet tried on the device.)
- The first capture of the app's own picture on the device (`artifacts/quest-first-ui.png`, app layer only - the room is not in it): the hand menu with all ten buttons and the timer panel are drawn correctly in both eyes, and the caption ("I'm listening.") lies across the menu, which still needs moving.
- The host did not log a client it turned away for not being paired; it does now. `--usb-dev` (development) accepts a headset that arrives through `adb reverse` without the pairing code.
## Evening on the headset (Quest 3, 2026-11-16 18:30-21:25, USB cable, the owner wearing it)

What the logs of the headset and of the host show, plus what the owner said while using it. One capture of the headset's view was looked at to check the screen outlines and deleted (it shows the owner's room and screens).

| What | Result |
|---|---|
| Passthrough | PASS after a fix. The room was black: Meta OpenXR hands passthrough to the compositor as a layer, and the OpenXR feature "Composition Layers Support" was off for Android. With it on, 80-86 % of a capture of the view is not black |
| Finding the screens | PASS with the camera method (21:17): three displays (two landscape, one portrait; one above another) found in 6.5 s from 27 camera pictures; three pictures of each agree to 3-6 mm; the distance agrees with the depth sensor to 0-3 cm; in a capture of the view the outlines lie on the lit areas of all three. The earlier methods FAILED on the device: touching four corners was never completed, and feeling out the screens with the depth sensor found one or two of three, in the wrong place |
| Called by name ("모모야 안녕?") through the open ears | PASS - answered at once (prepared audio: 0 ms after the transcript; the transcript takes about a second) |
| Spoken commands, no menu, no pinch | PASS - "모모야, 이번엔 이쪽으로 한 번 들어가 봐." went in, "모모야, 이쪽으로 나와봐." came out, each on the first try |
| Portal crossing on the device | PASS as far as the state machine shows: five crossings completed in both directions (clock error between the two sides 0.5-42 ms). How the cut at the glass LOOKS has not been reported |
| Review of the screen by voice ("이 화면 좀 봐볼래?") | PASS - went in, looked, saved feedback and a revised picture as PDF, said so |
| Tennis started by voice | started; how it plays on the device has not been reported |
| Desk height | sensed (a level 15 of 24 readings agree on, below the screens); in the capture the partner stands at keyboard height. Not confirmed by the owner |

Found on the device and changed (the last four are in a build made at 21:20 that had not run on the device when this was written):
- Speech was not understood because it was not recorded: the microphone opened only on a pinch or the Talk button. The ears are now always open; the host decides whether something overheard is for the partner.
- A Quiet button pressed by accident silenced every answer. Quiet now only silences what the partner says unasked.
- "Momo!" alone was thrown away as too short, and anything said while an earlier sentence was being worked on was dropped. A name call is now always answered, and what is said meanwhile waits its turn.
- The partner ran under a pointing hand to shelter from the rain; switched off. A film of water on the hand; removed (drops still splash on it).
- A stored desk height from an early, wrong measurement switched the desk sensing off for good; on a headset the desk is now always sensed, below the screens.
- Ten seconds after two screens had been found the headset announced its tracking origin again and both were discarded ("go in there" then had nowhere to go). The screens are now found again by themselves when that happens.
- For 12-20 s after an answer everything overheard was treated as addressed, so exclamations got answers ("와, 대박이다." started a review). Such remarks are now left alone unless they continue the talk.
- The host's own lines (tennis start, refusals) were said in English to a Korean speaker; they are now said in Korean.

## Known gaps that affect the result

- Everything in the headset (see BLOCKERS B1).
- Tennis has only been played by the simulator's scripted hand, which is placed on the ball's path by the program. Whether a real tracked palm meets the ball reliably, whether the ball looks like ONE ball as it passes the real glass, and how the PC court's depth looks through a headset are all unknown. There is no sound. The court replaces the whole monitor during a game; on a 4K display its per-frame copy is four times larger and was not tried.
- Review / reading: checked with two small samples made for the purpose, in English and once in Korean. Not exercised: a real pointing hand, the notes as seen through a headset (and whether Korean text shows there), a file selected in File Explorer, a file found through Windows' recent-files shortcut, the two tray items, the automatic opening of the document when no headset is connected, large or scanned PDFs, protected windows. A window that has a code project linked to it is still handed to the coding agent, not to this review.
- Voice conversation has only been exercised with recordings fed through the microphone path and with the simulator as MR client (B4). The pointing checks use a scripted ray: they show that the chain from ray to pixel is exact, not how well a real hand produces the ray.
- Secure-desktop suspension (UAC prompt, lock screen) was not exercised; full-screen suspension was checked with a stand-in window only, not with a game in exclusive full-screen mode.
- Side-by-side display move between displays whose floors are at very different heights (here the landscape `DISPLAY3` and the portrait `DISPLAY1`, 1088 px apart): the partner walks into the shared edge and comes in on the other display at that display's own floor. That is a designed hand-over out of sight, not a continuous walk; recorded after the fix with no stray frame (`artifacts/monitor-transfer-side-out.mp4`, `-back.mp4`).
- Mixed DPI scaling not exercised (all displays at 100 %).
- The 30 ms display-latency compensation on Quest is a starting estimate, not a measurement.
- Preview of a change (`TaskCommand preview`) is implemented but was not exercised, to avoid opening windows over the user's work.
- Spatial placement of the MR panels (proposal cards, task panel, timers, calibration guide, caption) has only been seen from the simulator's fixed camera, where they partly overlap each other and the partner. Where they should sit relative to a seated user, the desk and the monitor needs tuning in the headset; they are ordinary objects in `MrCore.prefab` and their placement code is in `MrUi.cs`.
- Pairing was exercised only with the simulator's scripted fingertip over loopback (see the re-verification table). Pairing a real headset over the LAN (discovery beacon, firewall prompt, typing with a tracked finger) is NOT_TESTED.
- The Quest scene (`QuestMR.unity`: XR Origin, tracked camera, AR Session) replaced a rig that was built in code; neither version has run on a device.
- The owner's character was rigged automatically and its poses were tuned from still renders. Its motion has been watched only in one recorded round trip and in the scripted checks; the bezel-climb entry and sitting on a palm were not watched with it. On Quest the solid cut face is switched off (the stencil bits of the eye buffers were never checked on a device), so there a view from the side into the cut body shows a flat tint instead.
- The licence terms of the generated character model (Tripo) were not checked here; this is on the submission checklist.


## Night of 2026-11-16 to 17: world, game packs, marketplace (SIMULATOR ONLY)

Everything in this section was run in the desktop MR simulator and on the PC. None of it has run on a Quest: the APK
built for it was not on a headset when this was written. Captures: `artifacts/gallery/index.html`.

| What | How it was run | Result |
|---|---|---|
| Hand menu, 14 columns, five pages (Play = installed game packs, Tools = Market / Bring screen) | `lpctl debug menuPage n` + captures | all five pages drawn; slots filled from the world |
| Game "survival" (creatures close in, fingertip sends them off, gems level the star pulse up) | `lpctl debug game 0 0 0 survival` | runs to both endings (the simulator's scripted hand lets one in four through) |
| Marketplace on the PC | headless Chrome on `http://127.0.0.1:47610/market` | page drawn from live state, icons arrive |
| Marketplace in MR (shop board) | `lpctl debug shop 1`, `shopTab`, `shopPick`, `shopAct` | "Vampire Night" bought from the board: 300 -> 240 stars, pack appears on the Play page |
| A game pack made with words | `lpctl make "... ghost survival pack ..."` | pack written by the model in 4 s, two icons drawn in ~28 s, published and installed, played |
| A pack's own tune and surroundings | Vampire Night | tune (128 bpm) and night sky while it is played |
| Conversation by text | `lpctl utter` "상점 열어줘", "128 곱하기 37은?", "뱀파이어 게임 하자" | shop opens in MR, the calculator ability answers with its working, the pack starts |
| Item made with words, skill taught with words | `lpctl world make`, `lpctl world teach` | record player made; skill "goto record player > music > dance" with its own icon on the Skills page |
| Activities with things | `lpctl world do study|nap|read|play` | poses fitted to desk + chair, bed, book, ball (nap position corrected after the first capture) |
| Surroundings | `lpctl world ambience rain|Aurora`, Dance skill | drawn outside the work area only; rain redrawn as thin streaks |
| A display brought into the room | `lpctl screen 1` | picture of the display on a board in the room, refreshed; closed with `screen close` |
| Tune saved as a file | `lpctl song export` | `Documents/LivingAIPartner/Music/Lo-fi.wav` |
| Unit tests | `dotnet test` | 511 passed |

Not run anywhere: a Tripo model (no Tripo key is stored - items use built-in shapes), pinch-and-drag turning of the
menu, poking the shop board with a real finger, the screen board's "analyze" path, any of the above on a headset.
The first Quest build of the night failed because drive C: was full (Win32 error 112).


## Late morning of 2026-11-17: hub marketplace, sculpted things, intro (SIMULATOR ONLY)

| What | How it was run | Result |
|---|---|---|
| Hub started by the host, automatic account | start the published host | hub child process up, nickname generated, 300 stars, 34 official listings |
| Selling to another person | `lpctl publish "사탕 유령" 20`, then `lpctl hub-as 테스트이웃 buy "사탕 유령"` (second TEST account) | hub moved 20 stars; host showed 320 stars, sales 1, earned 20; the partner said that somebody bought it |
| Buying from another person | `lpctl hub-as 테스트이웃 publish-sample`, `lpctl buy "선인장 화분"` | 10 stars paid, the thing (sculpted from parts) arrived in the bag and was put on the desk |
| MR shop board | `lpctl debug shop 1`, `shopTab 6`, `shopPrice`, `shopAct` | Mine tab with price stepper and Sell / Stop selling; paid things need two taps; seller and nickname shown |
| A thing sculpted from words | `lpctl world make "빨간 우산"` | 9 parts in 42 s (gpt-5.4, low effort), shown on the desk; reads as a closed umbrella, paler than "red" |
| Sculpt samples | `--sculpt` mode + Editor preview sheet | 18 things in `artifacts/sculpt-sheet.png`; weak ones: umbrella, tent |
| Bag / skill / game icons | menu captures | pictures of the user's own things now sit inside a badge, the size of other icons |
| Intro | `lpctl debug intro` + captures | glow, spreading ring, stars, eight badges circling the word mark, about 5 s |
| Bring a screen and analyse | sample backdrop on one display, `lpctl screen 1 analyze` | "살펴보는 중 ..." then the review's summary as the board's caption after 28 s, PDF saved (it reviews the front window of that display) |
| Unit tests | `dotnet test` | 590 passed |

Not run: the conversation actions for prices (`setPrice`, `unpublishModule`) with the real model, buttons of the new
shop page clicked by a person, the hub on another machine or on Linux, anything of this on a headset.


## Afternoon of 2026-11-17: one bag, workbench and offer, games as modules, icon family (SIMULATOR ONLY)

After two code reviews (headset code, host + hub) and their fixes. Nothing here has run on a Quest; the APK of 13:50
contains all of it and was not yet installed when this was written (the headset showed "unauthorized" on adb).

| What | How it was run | Result |
|---|---|---|
| Unit tests | `dotnet test` | 666 passed |
| One bag (things, then game packs, then skills; sticker pictures as the badge) | `lpctl debug menuPage 0..2` + captures | 16 slots filled in that order |
| Asking the pet to make something | `lpctl utter "밤비야 작은 선인장 화분 하나 만들어볼래?"` | makeItem; sculpted from 7 parts in 10.8 s; "짠! ... 다 만들었어! 이거 마켓플레이스에 공개할까?"; offer #1 and the card |
| Answering without the name | `lpctl utter "응 15별에 올려줘"` | published at 15 stars |
| Workbench (hammering, hammer in hand, progress ring, label) | `lpctl debug craft 1` + captures | drawn as intended; in the real make above the job was too short (11 s) to capture it |
| No game comes with the partner | start after the migration; `lpctl utter "밤비야 테니스 하자"`, then "응 받아줘" | the four games gone from the bag; the pet offers to get Tennis for free; it is in the bag after the yes |
| Icon family | start of the new host | 4 of 27 pictures flagged as having a round plate and drawn again, 1 missing picture backfilled; the bag's stickers match in the capture |
| Tennis through the screen | partner inside first, `lpctl tennis start`, scripted hand | rallies up to 11 strokes, points for both sides |
| Tennis on the desk | partner out, `lpctl tennis start`, scripted hand | the game runs and scores, but the simulator's scripted hand does not return a ball on the desk court - NOT verified |
| Regression pass after the reboot | crossing both ways, a game pack, workbench, shop board, intro | 0 exceptions in the simulator log, no error lines in the host log |
| Leftover processes | `tools/Stop-Dev.ps1` after each run | none after the reboot |

`tools/Run-TennisChecks.ps1` still expects the partner to go INTO the screen when tennis is started from the room; since
games can be played on the desk that expectation (T1, and T2 which depends on it) is out of date - it fails by design.

## 2026-11-17 evening - round after the user's headset session (16:17 / 16:40 feedback)

Everything in this section was run on the PC: unit tests, the published host, the Windows PC client and the desktop MR
simulator. Nothing in it is a headset result unless the line says "Quest".

| What | How it was checked | Result |
|---|---|---|
| Unit tests | `dotnet test Companion/LivingPartner.Tests -c Release` | 916 passed, 0 failed (741 before the round; +64 glass games, +look, +glass host, +shows, +records) |
| Bought surroundings can be applied | menu board > Bag > "Mountain top", again to switch off (`i-01-place-on-sim.png`, host log) | on / off / on by name, no model call |
| Pressing a thing on the desk | scripted fingertip and `debug poke` on the chair | squash + label at once; host `use ... '의자' (for: sit) -> study`, again -> stop |
| Record player -> records | `debug records 1`, `recordPick 1`, Stop | Lo-fi put on and marked, stopped |
| Menu board | launcher on the palm, board by touch, tabs, pages, ray + pinch, no board during tennis, Stop chip | simulator captures `m1-*.png`; thresholds on real hands NOT verified |
| "Feedback" badge and review board | removed from MR | not in the scene any more |
| Pointing -> what is read | simulator points at DISPLAY3 (0.22, 0.35), "밤비야, 여기 어떤 폴더가 있어?" with `LP_LOOK_TEST_IMAGE` (a made-up desktop picture; no real screen captured) | host: `look: source=hand monitor=DISPLAY3 ... close-up=[22,78 800x600]`; the folder names of that region were read; frame + ring drawn on the real monitor by the PC client (`i-07-look-pc.png`) |
| Headset hand -> shadow inside the PC | simulator hand held in front of DISPLAY3 (`debug glassHand ... DISPLAY3`), PC client log `hands=1.0`, no PC-side fake hand | shadow at the right place, fog wiped under it (`i-12-wipe-b-pc.png`) |
| Glass games inside the PC | Shadow Umbrella from the menu tile, Foggy Window by voice text, with the partner sent in through a real crossing | side=pc on DISPLAY3, phases/score reported to the host, over -> result line -> cleared; 60 fps on a 1920x1080 monitor |
| Glass games on the desk | "밤비야, 뱀파이어 서바이벌 하자", bot hand, `glassXp` | side=mr, level-up cards, ability taken, stop |
| Publish show | `lpctl publish item-1cc0b355 12` with the partner in the room | going (11 s, real crossing) -> stall -> listed + "올렸어! ... 별 12개!" -> back in the room (`i-08-pub-*.png`); "listed" lengthened from 2.5 s to 4.5 s afterwards |
| Deliver show | `lpctl install item-cup` (removed again afterwards) | `show: deliver '머그잔' out of DISPLAY3`, parcel on the desk |
| Short orders | "밤비야, 나와" / "그만" / "춤 춰" | BUG found and fixed: anything of two characters after the name was answered with "응, 왜?"; now returnToMr / stop / dance |
| Regression | tennis through the screen with the scripted hand (rallies, 4:1), shop board, workbench, pinch-carry | no exception in `mr-sim.log` / `pc-client.log` |

Not verified by this round (needs a person in the headset): every hand-tracking threshold (launcher, poke, ray + pinch,
pointing feel), the on-desk stage's comfort, sound by ear, the tennis rally on the desk court (the scripted simulator
hand does not return a ball there: 0:5), the look with the real three-monitor desk.

Quest, 2026-11-17 19:07: the APK of this round (built 19:05, 59 MB) was installed (`adb install -r`: Success). It was
NOT run: off the head the headset stays on the system lock screen even with the proximity sensor overridden, so the
app cannot be started from the PC. The device's media volume still reads 0. Nothing of this round is verified on the
device; the Quest-only code (finger ray / aim pose, reading and raising the media volume) has never executed.

## 2026-11-17 night - modules are scripts (after the user's correction)

All on the PC (unit tests, published host with the real model, parked PC client, off-screen simulator). Nothing here is a
headset result; the Lua machine has NOT yet run on the Quest (IL2CPP) - the app logs `[MR] lua: ok ...` at start.

| What | How it was checked | Result |
|---|---|---|
| Unit tests | `dotnet test Companion/LivingPartner.Tests -c Release` | 1264 passed, 0 failed |
| The partner writes a game on request (real model, gpt-5.4) | "밤비야, 풍선 터뜨리기 게임 만들어 줘" typed to the simulator | workbench show, a 130-line Lua program, checker passed on the first attempt, in the bag 36 s after asking; started by voice text, stopped by "게임 멈춰" |
| The writer's acceptance (H4) | eight fresh requests through the published host | all eight passed the checker: omok (re-make of the user's pack, 1 repair), tic-tac-toe, breakout (1 repair), dice app (1 repair), stopwatch, baseball bat with its own swing motion, yoyo, drum; 12 writer calls |
| A written omok plays | `lpctl glass start "오목놀이"` + bot / simulated hand | inside the PC: board, stones, "Bambi thinks", Bambi answers (i-29-omok-pc-pc.png); on the desk: flat board, result "Bambi wins!" |
| A written thing with its own motion | the made '야구배트': `roomAct wear`, `swing` | bat in the right hand, script motion `bat_swing` played, "hit" + sparks (i-23-bat-*.png); host kept `wornOn` |
| Fairness | the balloon game was lost 8 s after it began when nobody played | writer rule 8 and a serious check added ("lost within 15 s when nobody plays"); the shipped Star Catcher made gentler |
| Places | seaside in the simulator | animated water, foam, glitter (m5-place-*.png, i-27) |
| Stall on the desk, fan menu, carrying / stacking | simulator | captures m4-*, m5-*, i-20, i-28 |
| Leftover processes | after the 22:52 reboot | clients exit cleanly again (0 left after the run) |

Open: the mountain place is the weakest of the three; the offer card stayed up after a typed "가방에만 둬" in one run;
the old template games tennis / rps / pop / redlight are still native code.

## 2026-11-18 01:35 - round 3 (after the user's session with the 23:24 build)

Quest (from the device log of the user's session, 23:26-23:45): `[MR] lua: ok (2000 steps a call, 6.4 million steps a
second)`, 0 exceptions, 72 fps - the Lua machine runs under IL2CPP. Everything below is PC-only again (unit tests,
published host with the real model, parked PC client, off-screen simulator); the build of 01:4x has not run on a headset.

| What | How it was checked | Result |
|---|---|---|
| Unit tests | `dotnet test Companion/LivingPartner.Tests -c Release` | 1615 passed, 0 failed |
| Name mis-heard | "밤미야, ... 만들어 줘" typed to the simulator | taken as the name; making started |
| No duplicate making | asked for a game that exists | "이미 있어. 새로 만들까, 그걸 틀까?" |
| Fan menu on the open palm | simulator hand modes (M7) + my run | follows (4 mm behind at 0.28 m/s), ignores 2 mm jitter, freezes while the right finger is near; `i-30-palm-fan-sim.png` |
| Making = light gathering | voice text, real writer | motes + ball over the partner, no workbench (`i-32-report-board-sim.png` shows it in the background) |
| Research report | `lpctl doc report "How to take care of a small cactus"` | web research, 3 pages, PDF 631 kB in `Documents\Bambi\...`, opened by itself on the board in the room, 98 s |
| Report into the PC | `lpctl doc deliver`, partner in the room | crossing with the file (12 s), copy on the Desktop, file shown in the partner's hands in the PC client, click (`docClick 1`, Windows hit test) -> host `reveal -> open ...pdf` (not opened: `--no-auto-open`) |
| Design options | H6's live run on a made-up desktop picture | the pointed icon cut out, four distinct options, PNG + PDF sheet |
| Through the glass | the user's remade '골키퍼' inside the PC, simulator as headset with a blocking hand | 3 balls left the monitor, 3 blocked, PC score 3; host: 3 out / 3 verdicts / 0 dropped |
| A game follows the partner | "밤비야, 나와" during that game | run paused, went on at the desk with score 3, level 2 |
| Calculator in the app's look | R2: example script, PC and desk | 7 + 8 = 15, 12 x 3 = 36 by ring + pinch and by resting hand |
| PC actions | H5: `pcDo openApp notepad` once | Notepad opened and was closed again; keys only sent to that window |
| Sea swims when the head turns | V1: Editor render at head yaw 0 / 20 / 40 degrees | difference 3.35 / 5.05 -> 2.08 / 2.20 (= the floor of the test) |
| Play without the PC | M8: simulator with no host process | Play now card, three first-step hints, local bag, scripts, games, saved and restored |
| Leftovers | after each run | 0 processes left |

Writer timing: a game at medium effort took 152 s + critic + repair = 267 s in my run; the default is low again
(about 40 s first writing) and the critic no longer reports the intended fairness rules. Not re-measured after that.
Not verified by anyone: everything of this round on the headset (palm thresholds with real tracking, things flying
at the face, the shield, reading a page on the board, the local mode's desk finding), PC actions aimed by pointing,
the screen board refreshing after an action, a real mouse click on the file.

## 2026-11-18 02:35 - device-risk review and fixes

A read-only review of the night's headset code (never run on a Quest) listed 19 concrete risks; 17 were fixed and
checked in the simulator (two launches, 0 exceptions): welcome card enters by itself after 20 s, the first hint and its
fallback fan, no permission prompts in local mode, the Lua machine cap, textures decoded off the main thread, the
palm test loosened and explained in the log (`[MR] palm:`), `ZTest LEqual` for the places, the hit flash inside the
lenses, MSAA 4. Not done: foveated rendering. Added by the main session after two simulator runs showed a pressed
thing vanishing at once: the "Put away" pill stands beside the thing and, like the scripted thing's button strip,
only comes up when the pressing fingertip is 9-10 cm away (`i-41-bat-strip-sim.png`: bat pressed by the scripted
finger, both come up, nothing pressed by accident). Host: 1648 tests; writer re-measured at 35-80 s per game.
None of this has run on the headset.

## 2026-11-18 03:55 - last build of the night

Second read-only review (post-review changes): no breaking finding; two two-hand problems (rock-paper-scissors and
red light read a resting hand; a scripted thing's buttons could be toggled off before they were shown) and four small
ones were fixed. 1670 tests pass. Final Windows build, local mode with NO host process: Rock Paper Scissors, Star Pop
(score 3 with the bot) and Red Light ("Red light! Freeze!") started one after another from the fan, 0 exceptions
(`artifacts/tmp/fin-sim.log`). Design feature end to end in the simulator at 03:24: pointed icon -> four options in
69 s -> picked by voice text. APK built 03:52 and installed 03:52; it has not run on the headset.
