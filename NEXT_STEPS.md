# NEXT_STEPS — Living AI Partner

Ordered by what most reduces the risk to the core experience. Nothing here was done in the session unless TEST_RESULTS.md says so.

## 1. First hour with a headset (unblocks B1)

Do these in order and write down the numbers; the software has only been driven by a simulator so far.

1. `adb install -r dist\LivingAIPartner-Quest\LivingAIPartner.apk`, start `Start-LivingPartner.ps1`, pair (RUNBOOK §2). Expect first-run surprises here: discovery beacon, permissions, the XR Origin / passthrough camera authored in `QuestMR.unity` (never run on a device).
   - If the app shows no passthrough or no hands: check `adb logcat -s Unity` for `[LivingPartner] bootstrap role=quest registered=[quest]` and OpenXR feature errors.
2. Check the hand input itself before anything else: is the pointing ray where you expect, does a pinch register once, is the palm normal the right way round (the hand menu should appear when the **left palm faces you**; the partner should climb onto an **upward** palm). `QuestApp.Read` holds the three conventions that could be wrong: palm normal sign, pinch thresholds (18 mm / 32 mm), shoulder offset of the ray.
3. Calibrate one monitor. Note the reported fit error. With the debug overlay on, the cyan outline should sit on the lit area; note the visible offset in millimetres at each corner.
4. Send the partner into the screen with `lpctl freeze 0.5`. From your normal seat, look at the frozen body:
   - Does the half on the monitor continue the half in the headset? Measure the offset at the cut (mm) and the size mismatch (%).
   - Move your head left/right: the PC half should follow your viewpoint (off-axis projection). If it swims the wrong way, the head pose sign is wrong.
5. `lpctl play`, then run it at normal speed in both directions at three different spots and on a second monitor. Watch for: a visible time offset between the halves (tune `lpctl debug mrLatency <ms>`; PC side compensates its own measured read-back), a pop at the start or end, the partner landing in the air or inside the desk (desk height calibration).
6. `lpctl loop 10 <px> <py>` and a Wi-Fi drop mid-approach and mid-crossing.
7. Hand ride, rain shelter with a real hand, timer buttons by poking, proposal cards.
8. Camera observation: grant the headset-camera permission, check `adb logcat` for `camera frame stats`, and whether what it says matches the room.
9. Record the unedited round trip (through-the-lens or casting + the monitor in view).
10. Tennis (hand menu **Tennis**): does the open palm meet the ball where you see it (if not, check the palm position / normal from the hand adapter and `Tennis.PaddleRadius`)? Does the ball look like one ball as it comes through the real glass - position, size, timing (`lpctl debug mrLatency`)? Is the court's depth convincing from your seat, and is the ball's speed playable (`Tennis.Gravity`, the flight times in `PartnerShot`)? Note the frame rate of the PC client while the court covers the monitor.
11. Review: point at a real window and ask "what do you think of this?"; check the marks on `feedback.png` against the window, and read the cards in the headset (size, wrapping, Korean text).

## 2. Most likely fixes after that

- Latency compensation value on Quest (currently a 30 ms guess).
- Where the rain lands on a real hand: splashes and the water film are placed 16 mm above the tracked palm joint, on the side of the hand that faces the rain, over an ellipse of about 10 × 19 cm (`HandTop` in `MrUi.cs`, film size in `MrCore/Rain/Water/HandAnchor */Film`). These are estimates; virtual content is drawn over the passthrough image of the hand, so check that it reads as lying on the skin and adjust.
- Pointer stability / pinch "drag" on a monitor ~60 cm away; possibly use the Meta aim pose instead of the shoulder ray.
- Calibration ergonomics (touching a glossy screen with tracked fingers is error-prone): consider pinching a ray at each lit corner as an alternative, and averaging repeated samples.
- Persisting the room relationship with a spatial anchor instead of a tracking-space epoch.
- Text legibility of the cards at arm's length. The UI is now set in the project's own typeface (LPStory, a pre-rasterised atlas at 128 px per em with mip maps, drawn by `TextMesh` through `StoryLabel`); panel body text is about 6 mm per em, which has only been judged on a monitor. If it is too small or soft in the headset: raise the label heights in `LPSceneBuilder` (wrapping follows automatically), or raise `K` in `StoryFontBuilder` for a denser atlas. LPStory has Latin letters only; Korean text falls back to the system font, so a Korean UI would need Hangul glyphs or another font.
- The character has only been seen on a monitor. In the headset, check that the ink outline (about 0.85 mm) does not shimmer at desk distance, and look at a crossing from the side: on Quest the solid cut face is switched off (`PartnerRig._drawCutFace`) because the stencil bits of the eye buffers were never checked on a device - if the inside of the cut body shows, turn it on and test.
- Poses were retuned for this body by looking at stills (`artifacts/partner-pose-sheet.png`): the arms are short and never go above the shoulders. The bezel-climb entry (hang, pull up) and sitting on a palm were not watched in motion with this character.
- Pairing numpad, task panel and proposal cards have not been looked at since the typeface and the measured line wrapping went in (`tools\Run-PairingFlow.ps1`, `tools\Run-HarnessE2E.ps1` show them).
- Where the MR panels sit (proposal cards, task panel, timers, calibration guide, caption): so far only seen from the simulator's fixed camera, where they partly overlap each other and the partner. The panels are plain objects under `MrCore.prefab`; their runtime placement is in `MrUi.cs` (`FaceUser(...)` calls).
- Pairing works end to end in the simulator with a scripted fingertip (`tools\Run-PairingFlow.ps1`), but it is the first thing a headset user sees and it has never been typed with a real tracked finger or over the LAN: check it first.

## 3. Product gaps to close

1. Voice on the headset (B4): allow the microphone when asked, pinch the partner and speak; check that a real pinch on it is recognised (and is not taken as a pinch on the screen behind it), that it does not hear its own voice, and where its voice seems to come from. Then consider streaming replies for a shorter wait (about 3 s from the end of speech to the first word now).
2. Character motion: the owner's model is rigged automatically (16 bones, no fingers, no face rig) and moved by one procedural motion set. Authored animation, or a re-export of the model with separate upper and lower arms, would improve the arms most: the elbow is effectively the wrist.
3. Tab/document identity for browsers through UI Automation (currently window-level).
4. Harness adapters: Codex has been run end to end once (analysis, change, tests, approved push) and Claude Code several times; neither has been tried on a large real project. Codex does not report cost, so there is no spend cap for it beyond the timeout.
5. GitHub publishing with real credentials (B3) and a pull-request step instead of a bare branch push.
6. Vertical display-to-display transfer and mixed-DPI displays: exercise on a machine where they occur.
7. Host: start with Windows (per-user Run key), an installer, signed binaries.
8. Hand occlusion of the partner in MR, and scene-understanding-based desk detection instead of the touched desk height.

## 4. Submission (owner)

Work through `reports/SUBMISSION_CHECKLIST.md`. The official rules page could not be read automatically; the question of whether a required PC companion is acceptable must be answered there before recording the demo.

## 5. Housekeeping left by this session

- Nothing was committed or pushed. `git status` shows the whole tree as untracked work for you to review (`.gitignore` excludes Unity caches, `dist/`, tokens).
- Local runtime data: `%LOCALAPPDATA%\LivingAIPartner\` (state, logs, captures, the sample project and its worktrees, an Edge profile used only for the sample window). Safe to delete as a whole.
- The sample project's task branches `partner/task-ff4d4e16` (Claude Code run) and `partner/task-13124526` (Codex run) exist only in the local sample remote. One more Codex change, `partner/task-d4de2ca2`, was started by accident when the simulator's scripted hand touched a proposal card during a test; it passed its tests, stopped at `ReviewRequired` and was **not** approved or pushed (it exists only in the local sample checkout). That accident led to a fix: a palm-up or carrying hand no longer presses buttons.
