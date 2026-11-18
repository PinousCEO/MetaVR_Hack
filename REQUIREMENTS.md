# REQUIREMENTS — Living AI Partner

Source: the master implementation directive for the Meta VR Start Developer Competition 2026 (Productivity) entry. This file restates the requirements as testable items and maps each to where it is implemented. Verification results are in `TEST_RESULTS.md`.

## The core that must not change

> My own character moves naturally to the position I chose, passes its body through the real monitor at that position into the PC, and comes back out into my MR room the same way.

- The PC character is an independent transparent Windows desktop app that roams all of Windows — not a web page, not tied to a browser.
- Same look, proportions, rig, personality, memory and task context in MR and on the PC.
- The user-chosen valid screen position is the portal entrance and exit.
- Head/face, upper body, lower body and legs pass in sequence along the direction of travel.
- No teleport, no full fade, no hiding behind big particles, no pre-recorded video.
- Real PC rendering and real MR rendering are connected (not a fake monitor inside the headset).
- Mobile is out of scope.

## Required verifications and where they live

| ID | Requirement | Implementation |
|---|---|---|
| R01 | Windows character runs as an independent transparent app | `Windows/PcApp.cs`, `Windows/LayeredOverlay.cs` (Unity off-screen render → per-pixel-alpha layered window) |
| R02 | Transparent parts do not block normal mouse use | overlay styles `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE`; `tools/Verify-Overlay.ps1` |
| R03 | Core operations by hand on Quest | `Quest/QuestApp.cs` (XR Hands: ray, pinch, palm), `MR/MrApp.cs` + `MR/MrUi.cs` (pinch-to-select/confirm, poke buttons, palm menu, numpad) |
| R04 | Same character asset and real task state shared | `Shared/Skeleton.cs` + `Character/PartnerRig.cs` (one generated rig); host `Core` snapshot (`CharacterInfo`, tasks, modules) |
| R05 | User selects different valid positions inside a monitor | `MrApp.UpdatePointing/Select/Confirm`; host `Desktop.Resolve` (validation, adjustment with reason) |
| R06 | Natural approach to the destination | `MrApp.Approach` (acknowledge → look → turn → walk/run → launch pose), `Character/PartnerMotor.cs` (distance-driven gait), plan variants `DiveFlip / RunLeap / BezelClimb`; PC side `PcApp.ApproachExit` |
| R07 | MR→PC: body passes sequentially and stays connected | `Shared/MotionPlan.cs` (Thread phase), clip plane in `LPPartner.shader`, shared `CrossClock` |
| R08 | PC→MR: same quality, lands | same plan, direction 1; `MrApp.LandFromPlan` |
| R09 | No two whole bodies, no gap, no sudden size/pose change | `TransitionMachine.Visibility`, ownership flips only after the last sample is through, plan continuity |
| R10 | Per-monitor position/scale and window movement handled | `Shared/ScreenMath.cs`, `Shared/Win32Native.cs` (physical px, DPI, rotation, EDID), `PcApp.UpdateWorkspace`, `Coordinator.WatchTargetWindow` |
| R11 | Minimise / restore / occlusion change what is shown | `PcApp.UpdateWorkspace` (hidden while minimised, z-order directly above the workspace window) |
| R12 | Disconnect and recovery never duplicate character or tasks | `Coordinator` watchdogs (hold / rewind / finish-alone), `HostLink` reconnect + de-dup, `Core.LoadState` (tasks not blindly resumed) |
| R13 | Pointing + utterance select the right screen area | pointer samples at 15 Hz → host `ResolvePointing(t0,t1)` over the utterance window (pinch-confirmed first, else densest dwell) |
| R14 | Real screen + project analysis with evidence | `Desktop.CaptureWindow` (the pointed window only), `TaskManager.StartAnalysis` → Claude Code CLI, evidence fields per module |
| R15 | Returns through the portal and presents MR modules from the real analysis | `MrUi.UpdateCards` renders `ProposalModule`s from the host task |
| R16 | Chosen proposal → real code change and tests | `TaskManager.ChangeFlow` (worktree + branch, harness edit, `git status` as ground truth, project test command) |
| R17 | Git publishing matches real result and approval | `TaskManager.Publish` (only after `ReviewRequired` + explicit approval, task branch only, no force) |
| R18 | Created timer is stored and re-run by natural language | `Shared/Modules.cs` (`ModuleDef`, `TimerLogic`, `IntentParser`), host persistence, `MrUi.UpdateTimers` |
| R19 | Reacts to the environment from real camera observation | `QuestApp.CameraLoop/Analyse` (Passthrough Camera API frames, on-device statistics → `Observation` with source/time/confidence) |
| R20 | Hand riding and virtual-rain shelter follow state changes | `MrUi.UpdateHandRide`, `Shared/Modules.cs` `RainShelter`, `MrUi.UpdateRain` |

## Portal sub-requirements (§3 of the directive)

| Item | Implementation |
|---|---|
| Target stores monitor, screen position, linked window, spatial plane; screen-fixed vs window-following | `TargetInfo` (`anchorKind` = `screen` / `window`, `winU/winV`, `windowRect`) |
| Invalid / cramped spot → nearest valid spot shown and confirmed, never silently changed | `ScreenMath.ClampPortal`, `TargetInfo.adjusted/reason`, orange marker + reason text, second pinch required |
| Lit area vs bezel; display id, physical pixels, DPI, rotation, virtual-desktop coordinates incl. negative | native corner markers at the lit pixel corners (`PcApp.DrawCalibrationMarkers`), `Win32.EnumerateMonitors` |
| Real layout ≠ Windows layout | per-monitor `MonitorCalibration`; nothing else links the two |
| Calibration saved / reused / redone; drift detected or guided | host `state.json`; dropped on display-mode change; ignored + prompt when the MR space epoch changed |
| State machine with Hold / Cancel / Recover; idempotent messages | `Shared/Transition.cs`, `Host/Coordinator.cs` |
| Receiver ready before the source hides | PC sends `receiverReady` only after its overlay has presented frames; MR sends it only with tracking + calibration |
| Debug mode: plane/normal/target, freeze at 0/25/50/75/100 %, slow motion, loop, both sides' state, error logging | `lpctl freeze/play/slow/rewind/loop/overlay`, debug overlays on both sides, `transitions.jsonl` |
| Optical honesty | PC half is drawn with an off-axis projection from the tracked head; documented as single-viewpoint alignment, not stereo |

## Deliberately not claimed

- Tab/document identity inside a browser (window-level binding only).
- Control of terminal sessions the user already has open (managed sessions only).
- Arbitrary C# generated and compiled on Quest (modules are compositions of allowed runtime kinds).
- An always-on MR layer over other immersive apps.
- Consciousness or feelings: the emotion system is a consistent expression layer driven by real state.
