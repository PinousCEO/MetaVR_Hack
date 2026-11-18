# ARCHITECTURE — Living AI Partner

One partner character ("Momo") that lives both in your mixed-reality room (Quest) and on your real Windows desktop, and moves between them by physically passing its body through the real monitor at the point you chose.

```
        Quest 3 / 3S (Unity, OpenXR)                       Windows PC (logged-in user session)
 ┌────────────────────────────────────┐        ┌──────────────────────────────────────────────────────────┐
 │ QuestApp  (XR rig, passthrough,    │        │  Companion Host  (.NET 8 tray app + launcher)            │
 │           hands, camera frames)    │  WS    │   Core        single-threaded authority: state, snapshot │
 │      │ IMrInput                    │◄──────►│   Coordinator portal state machine + shared clock        │
 │ MrApp (approach, crossing, pointing│ token  │   Desktop     monitors / windows / permitted capture     │
 │        hand ride, rain, modules,   │ paired │   TaskManager analysis → change → test → approval        │
 │        calibration, pairing)       │        │   Harness     Claude Code / Codex CLI adapters (managed) │
 └────────────────────────────────────┘        │   Auth        local token + 6-digit pairing              │
                                               └───────────────▲──────────────────────────▲───────────────┘
 ┌────────────────────────────────────┐   loopback WS + token   │                          │ loopback WS
 │ MrSim (desktop MR simulator —      │◄────────────────────────┘        ┌─────────────────┴──────────────┐
 │ verification tool, NOT a Quest)    │                                  │ Windows character client       │
 │ runs the same MrApp                │                                  │ (Unity standalone)             │
 └────────────────────────────────────┘                                  │  PcApp + LayeredOverlay        │
                                                                         │  transparent click-through     │
              Shared core (pure C#, no engine) compiled into ALL of them │  window over the real desktop  │
              protocol · coordinate chain · motion plan · state machine  └────────────────────────────────┘
```

## 1. One Unity 6 project, two players

`MetaVRHack/` (Unity 6000.3.13f1, URP) builds both clients from one project. Everything a client shows is authored in the Hierarchy as scenes and prefabs, and scripts reach their objects only through serialized references (see 1.1). Assemblies:

| Assembly | Platforms | Content |
|---|---|---|
| `LivingPartner.Shared` | all (no engine references) — also compiled into the host, lpctl and the tests | protocol, `ScreenMath`, `MotionPlan`, `TransitionMachine`, skeleton + pose library, timer/intent/rain logic, Win32 interop |
| `LivingPartner.Character` | all | `PartnerRig` + `PartnerDesign`, `StoryLabel` (text), `FxQuad`, `PortalFx`, `SparkFx`, `WaterFx`, `UiPanel` / `UiButton`, locomotion, `HostLink`, palette, `AppBootstrap` |
| `LivingPartner.MR` | all | `MrApp` — everything the MR side does, written against `IMrInput`; panel views (`HandMenuView`, `PairingView`, `CalibrationView`, `TaskPanelView`, `TimerPanelView`, `ProposalCardView`), `MonView` |
| `LivingPartner.Quest` | Android (+Editor) | `QuestApp`: XR Hands input, passthrough-camera frames, host discovery |
| `LivingPartner.Windows` | Windows (+Editor) | `PcApp`, `LayeredOverlay`, `MrSim` |
| `LivingPartner.Editor(.XR)` | Editor | `LPSceneBuilder` / `LPQuestSceneBuilder` (author assets, prefabs, scenes), `MeshBuilder` (character meshes), `StoryFontBuilder` (the typeface), build + OpenXR configuration, Android manifest patch |

### 1.1 Scenes and prefabs (what is in the Hierarchy)

| Asset | Content |
|---|---|
| `Scenes/Main.unity` | Windows entry scene: one `AppBootstrap` object that loads `PcCharacter` or `MrSimulator` by `-lp-role` |
| `Scenes/PcCharacter.unity` | `PcCharacter [PcApp]` → `OverlayCamera`, `Partner` (prefab), `Portal/` (`PortalFx`, `SparkFx`, `PortalRing`), `Status/`, `Caption/`, `Debug/` |
| `Scenes/QuestMR.unity` | `QuestMR [QuestApp]`, `XR Origin / Camera Offset / Main Camera` (TrackedPoseDriver, ARCameraManager, clear alpha 0), `AR Session`, `MrCore` (prefab) — the only scene in the Quest build |
| `Scenes/MrSimulator.unity` | `MrSimulator [MrSim]` → `SimHead` camera, `SimRoom/` (desk, hand, screen backdrops), `MrCore` (prefab) |
| `Prefabs/MrCore.prefab` | `MrApp` + `Partner`, `Portal/`, `Pointing/`, `Captions/`, `Monitors/` (pool of 6), `Rain/` (`Drops/` 110 rain streaks; `Water/` = `WaterFx` with 40 splash ripples, 60 droplets, 10 glints and one anchor + sheet of water per hand), `Panels/` (HandMenu, Pairing, Calibration, TaskPanel), `Timers/` (pool of 4 `TimerPanel`), `Cards/` (pool of 5 `ProposalCard`) |
| `Prefabs/Partner.prefab` | the character: `Visual/Root/…` 16 bones, skinned `Body`, two eyes (`EyeL`, `EyeR`), `ContactShadow`, `CutFace` (the quad that closes the body where the screen cuts it); `PartnerRig` lists the `PartnerDesign` asset |
| `Prefabs/PortalFx.prefab`, `SparkFx.prefab`, `TimerPanel.prefab`, `ProposalCard.prefab` | effects and panel templates |
| `Scenes/CharacterGallery.unity` | not in any build: the partner from four sides (`GalleryCamera`) and a specimen sheet of the typeface (`FontSpecimen` / `SpecimenCamera`), for looking at them in the Editor |
| `Art/Meshes`, `Art/Materials` | `LPQuad` mesh; `PartnerBody`, `PartnerFace` (eyes), `PartnerCut` (cut face), `LPFx`, `LPEncode` materials |
| `Art/Characters` | `Tripo/TripoPartner.fbx` + `TripoPartner_Albedo.png` (the owner's model as delivered: static, unrigged, T-pose); generated from it: `PartnerBody_momo.asset` (skinned mesh), `PartnerEyeL_momo` / `PartnerEyeR_momo` (eye meshes) and `Design_momo.asset` (`PartnerDesign`: id, name, mesh, eye placement) |
| `Art/Fonts` | `LPStory-Regular` / `LPStory-Bold`: `.fontsettings` (Unity font asset with glyph metrics), atlas texture, material |

Rules the runtime code follows (checked by grep, see TEST_RESULTS): no `GameObject.Find` / `transform.Find`, no `GetComponent` in player code, no `new GameObject` / `AddComponent` / `Instantiate` / `Resources.Load`. Counts that vary at runtime (calibrated monitors, timers, proposal cards) use the fixed pools above. Per-object looks (colour, mode, clip plane) go through `MaterialPropertyBlock`s on shared material assets, so material assets are never modified.

The assets are produced by `LPSceneBuilder.BuildAll()` (menu *LivingPartner ▸ Author Assets, Prefabs and Scenes*, or through Unity MCP). It wires every `[SerializeField]`, then walks all LivingPartner components in the prefabs and scenes and reports any reference left unassigned (`artifacts/scene-build.txt`). After that the objects are ordinary scene objects: they can be moved, restyled or re-wired by hand in the Inspector.

Pinned packages: `com.unity.xr.openxr 1.19.0`, `com.unity.xr.meta-openxr 2.7.0`, `com.unity.xr.hands 1.10.0`, `com.unity.xr.arfoundation 6.7.1`, `com.unity.xr.management 4.7.0` (the versions Unity 6000.3.13f1 resolved; nothing else was upgraded). The Quest build uses Unity's OpenXR + Meta OpenXR feature set (passthrough, hand tracking); it does not need the Meta XR Core SDK.

**Look.** A picture-book style: cream paper panels with a thin drawn outline and a soft shadow, pastel pill buttons with brown ink lettering, a honey-and-rose portal ring; nothing glows and nothing is saturated. All colours come from `Ui.Pal`. Every text the user sees (panels, buttons, captions, what the partner says) is English; Korean phrases are still understood as input (`IntentParser`).

**The character is one asset.** `Partner.prefab` is used by both the Quest scene and the Windows scene, so both show the same model, proportions and rig, and it is animated by pure pose functions (`PoseLib`): the two processes cannot drift apart, because a pose is a function of (motion, time).

**The model is the owner's; the rig is ours.** The character is a model the owner generated with Tripo (a small figure in a yellow bonnet and romper, eyes only). It arrives as a static T-pose mesh with one texture and no bones. `PartnerModelBuilder` (Editor, part of the authoring menu) turns it into the partner:

- `Skeleton` holds the joints measured on this model (a head more than half of the height, very short legs, short arms that rest 35° out from the body), and it is the same definition the host and the tests use, so the crossing plan measures the real body.
- The model is ten separate shells. Feet, hands, cuffs and the clasp each follow one bone; the big shell (body, bonnet, face, sleeves) gets smooth weights by fixing the vertices that clearly belong to a bone and relaxing the rest over the surface. The bind pose is the T-pose the model was made in.
- The eyes become two meshes on the head bone, so blinking and looking still work.
- The 30 openings between shells are closed, and every surface that can never be seen from outside is marked (523 of 5 348 positions). Both are needed for the cut face below. Marked surfaces, the closing fans and the leg stubs also take the colour of the visible surface next to them, because the model's texture is dark and meaningless there.

The shader (`LPPartner`) draws the model's texture under the project's two-tone shading with an ink outline, so it sits in the same picture-book look as the UI. The clasp at the neck is the status light: it takes the activity colour while a task runs. The licence of a generated model is not something the code can settle; it is on the owner's submission checklist.

**Cut by the screen.** While the body passes through the display, each renderer draws only its side of the display plane (`_ClipPlane` in `LPPartner`): the PC draws what is behind the glass, the room side what is in front of it, and the two parts are one whole body at every moment (`artifacts/partner-cut-sheet.png` in the Editor, `artifacts/tripo-half-through-sheet.png` from the running apps). Where the body is cut it must look solid, not like a hollow shell. A pass of the partner shader counts the body's surfaces behind every pixel (+1 seen from inside, −1 from outside) in the stencil buffer; the count is non-zero exactly where the line of sight enters the body through the cut, and there `CutFace` - one quad lying on the display plane (`LPPartnerCut`) - is drawn in a flat cream. Enclosed surfaces get the same colour and no ink line, so the gap between bonnet and head does not show as a hole in the cut. On the headset the cut always faces the screen, away from the viewer, and the stencil bits of the eye buffers have not been checked on a device, so there the quad is off and the inside is only tinted.

**One look.** `PartnerDesign` (a body mesh and where the eyes sit) and the shared `CharacterInfo.design` remain, with one entry, `momo`. Both clients still report the design they render (`PcStatus.design`, `MrStatus.design`). An earlier set of six looks built from round shapes was removed when the owner supplied the character; the files as they were are kept in `artifacts/backup-before-tripo-character-2026-11-16/`.

**The typeface is ours too.** `StoryFontBuilder` holds the whole alphabet as pen strokes — lines and elliptical arcs on a grid (x-height 5, cap height 7, 10 units per em) — for ASCII 32–126 plus `· • … – — ‘ ’ “ ” → × ° ✓`, rasterises them with a round nib at two weights into an Alpha8 atlas and saves classic Unity font assets with per-glyph metrics on a 64-per-em grid. Figures share one width so a running timer does not jitter, and the capital I has bars so "AI" cannot read as "Al". Every text object is a `StoryLabel` next to a `TextMesh`: it shows text in LPStory, switches that one label to the system font when a string contains a character LPStory has no glyph for (a Korean window title, harness output), and measures and wraps text from the real glyph advances — speech bubbles and panel bodies are sized by the text, not by a character count.

## 2. The portal

### 2.1 Coordinate chain (each step separately tested)

```
MR tracking space ──MonitorFrame (manual 4-corner calibration of the LIT area)──► display frame (m)
display frame (m) ──size from calibration ▸ EDID ▸ labelled default──► normalised (u,v)
(u,v) ──MonitorDesc.bounds (physical px, may be negative, per-monitor DPI aware)──► Windows pixel
Windows pixel ──window visible frame (DWM extended frame bounds)──► window-relative position
```

The physical arrangement of the monitors is never assumed to equal the Windows arrangement: only the per-monitor calibration links the two. A calibration is tied to the display mode (resolution change invalidates it) and to the MR tracking-space epoch (a recentre makes the Quest ask for re-calibration instead of trusting stale corners).

### 2.2 One plan, two renderers

`CrossingPlan` is expressed in the **portal frame**: origin at the chosen point on the glass, +x screen right, +y screen up, +z into the screen. The display surface is exactly `z = 0`, whatever the monitor's tilt. The MR side obeys real gravity (`mrUp` = world-up seen from that frame), the PC side is screen-aligned.

`PlanEvaluator.Evaluate(plan, tau)` returns root transform + full pose. Phases: Prepare (crouch) → FlightIn → **Thread** (the body, aligned with its travel direction, slides through the chosen point: hands/head, torso, legs, feet in that order) → FlightOut (forward flip) → Land → Recover. Variants are chosen from real geometry (`DiveFlip`, `RunLeap`, `BezelClimb`), and the plan is finalised only when both sides have reported where they really are, so nothing pops.

Both renderers evaluate the same plan at the same `tau` and draw the **same mesh in the same pose**, each clipped to its side of the plane (`LPPartner.shader`: `_ClipPlane`, `_ClipSign`). The cut is drawn as a solid cross-section. Nothing is swapped, faded or hidden behind an effect; the thin ring only marks the boundary.

`tau` comes from `CrossClock` (anchor host time, rate, optional stop) on the host clock. Clients estimate the host clock NTP-style with network-thread timestamps. Rate 1 = play, 0 = hold, negative = rewind (recovery), <1 = slow motion; a stop time freezes at an exact body-progress percentage for inspection.

### 2.3 What the PC actually shows

The Windows client renders its half with an **off-axis projection from the viewer's real eye position** (the Quest streams the head position in that display's frame at 30 Hz), so the part behind the glass lines up with the part still in front of it from where the user is standing. Without a tracked head it falls back to a viewpoint straight in front of the character. A flat monitor cannot give stereo depth; this is perspective alignment for one viewpoint, not holography.

### 2.4 State machine and ownership

`Idle → TargetSelected → Approaching → ReceiverReady → Crossing → Settling → Complete`, plus `Hold`, `Recovering`, `Cancelled`. The authoritative copy is in the host (`Coordinator` + `TransitionMachine`).

- The source keeps the whole character until the receiver has really presented frames (`ReceiverReady` is sent by the PC only after its overlay window is on screen).
- Ownership flips only when the last body sample has passed the plane.
- Every event carries the `transitionId` and is idempotent; duplicate or stale complete/cancel messages cannot change state.
- Watchdogs: target window moved (portal re-anchors, MR re-routes), minimised or closed (hold / cancel), renderer heartbeat lost during the crossing (destination lost → rewind to source; source lost after entry → destination finishes alone), client disconnect (hold, resume on reconnect with re-confirmation), timeouts.

## 3. Windows character client

- **Compositing:** Unity renders the character off-screen into a character-sized buffer; `LayeredOverlay` pushes pixels to a borderless Win32 layered window with `UpdateLayeredWindow` (per-pixel premultiplied alpha, position + size + pixels in one atomic call). The window is `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`: it never takes focus and all input falls through. The Unity player window itself is hidden and only hosts rendering.
- **DesktopRoaming:** topmost, stands on the work-area bottom of a monitor, suspended when a full-screen app owns that display or the secure desktop (UAC / lock screen) is up.
- **WorkspaceBound:** bound to a program window chosen as portal target. Follows move/resize, is cropped to the window, is hidden while it is minimised and returns with the same binding, and sits directly above that window in z-order so anything above the window also covers the partner. No code is injected into other programs: only read-only window queries and control of our own window. Binding is window-level; tab/document identity inside a browser is not claimed.

## 4. Companion Host

A per-user tray process in the interactive session (never a Session 0 service, never elevated). It is also the launcher: starting it starts the character client. `Core` is a single-threaded authority (action queue + 15 ms tick) that owns character state, calibrations, modules, tasks and settings, persisted to `%LOCALAPPDATA%\LivingAIPartner\state.json`.

- **Connections:** one WebSocket endpoint. Loopback clients present a local secret file; LAN clients need a token obtained once with a 6-digit code shown on the PC (rate-limited). Protocol version check, per-message ids for de-duplication, full snapshot on (re)connect. `--loopback-only` disables the LAN listener.
- **Tasks:** `TaskManager` runs the real pipeline (analysis → evidence → proposals → user choice → change on a dedicated branch/worktree → project tests → review → approved push) through `IHarnessAdapter`. The default adapter is the installed, authenticated Claude Code CLI in print mode as a *managed* session (own cwd, tool allow-list, budget cap, timeout, kill). A second real adapter drives the Codex CLI (`codex exec --json`, sandbox `read-only` for analysis and `workspace-write` for changes) and is selected with `--harness codex`. Each adapter supplies its own wording for how files may be read and edited, because the two tools work differently (Claude Code has Read/Edit tools; Codex reads and patches through its shell). One task at a time. A labelled stub exists only behind `--stub-harness`.
- **Virtual rain.** Drawn to look natural: thin soft streaks, fine ripples where a drop lands, small clear droplets and an almost transparent sheet of water on a wet hand (four modes of the `LPFx` shader; a first toon version with thick outlined drops was replaced at the owner's request). Each drop ends where it really lands - the test is the drop's path against the hand's rain-facing surface (an ellipse on the back or the palm of the hand), then the partner's head, then the desk - and the splash is drawn on that surface. A splash that lands on a hand is stored relative to the hand's anchor, so it stays on the hand while the hand moves; a hand that keeps catching drops gets wetter, shows the film and drips from the lowest point of its rim. The shelter decision itself is still the separate `RainShelter` geometry test.
- **Modules:** stored artefacts (`ModuleDef`) composed from allowed runtime kinds; the timer runs on absolute UTC time.
- **Conversation and voice** (`Ai.cs`). The MR client (or the PC client when no headset is connected) records ONE utterance after the user asks to talk - a pinch on the partner, the Talk button, the tray - and stops by itself on silence (`VoiceIo`: 16 kHz mono, at most 14 s). The host transcribes it (OpenAI speech-to-text), looks up what the user pointed at during those seconds (`PointingDwell`: the centre of the densest dwell in the pointing history, or the pinch if there was one), and asks a language model for a short reply and at most ONE action from a fixed list that the host already implements (structured output; the model cannot invent an action). The host carries the action out through the same code as a typed command and, if it could not be done, says that instead of the model's line. The reply is turned into speech and sent with its text, so bubble and voice start together; the side the partner is on plays it. The model is told where the partner is, whether the user was pointing, the timers, rain and quiet mode and the time - not window titles, file names or screen content. `CharacterInfo.talk` (listening / thinking / speaking) is shared state, so both clients show the same pose and status light. Without a key, or when a call fails, the deterministic `IntentParser` answers in text.

- **Looking and reading** (`Review.cs`, part of the task manager). A request to look at a window with no code project behind it, or to read a file, becomes a task with the same life cycle the clients already show (queued, started, evidence, ready / failed). One call to a vision model with a strict JSON shape returns the notes (each with a region of the picture) and the line to say; the host then draws `feedback.png` itself (GDI+), asks an image model for the redraw, and lays the PDF out page by page with GDI+ - pages are pictures inside a minimal PDF, which is why any language the system has a font for can be written and why the text cannot be selected. A PDF is sent to the model as a file (text and pages); which file a window shows is worked out from the name in its title (recent-files shortcut, then Desktop / Documents / Downloads / Pictures by exact name) or from the selection of a File Explorer window. What was seen or read is kept as notes in the conversation's system prompt (the last three), so later questions are answered from them.
- **Tennis** (`Shared/Tennis.cs`, `TennisGame.cs`). The court is the display frame of one monitor: a box behind the glass (z > 0) for the partner, the room in front of it (z < 0) with the desk as its floor for the user. A stroke is `(position, velocity, host time)`; where the ball is at any time is a pure function of the stroke (`Tennis.Evaluate`: gravity along the room's up - the display may lean - bounces on the desk and on the court floor, the walls of the box, and the glass, which lets the ball through only inside the lit opening). Nothing about the ball is streamed: the host broadcasts each stroke and the two renderers evaluate it on the shared clock, the PC keeping z > 0 and the headset z < 0 with the same clip plane the partner's body uses. The host is the referee and the opponent: it serves, plans the partner's return (`PlanReturn`: the first moment the ball is low enough and the partner can be under it), lets it miss now and then, and scores. The user's stroke is found on the headset (the palm as a disc, tested at four moments between frames), shown at once with the same function the host uses (`UserShot`: the stroke is helped so that it goes back through the opening; the hand's motion steers it) and then replaced by the host's copy. On the PC a game takes a second layered window the size of the monitor; the camera's frustum goes through the monitor's edges from the tracked head position, which is what gives the court its depth.
## 5. Privacy and safety decisions

- Only the window the user pointed at / bound is captured, on demand; captures are downscaled and pruned.
- Looking at a screen and reading a file happen only when asked. The capture, or the file, is sent to the OpenAI API with the owner's key; the results are written to `Documents\LivingAIPartner\Reviews` on the PC and are not sent anywhere else. Which file a window shows is decided from the window title and file names only - no file is opened to see whether it matches. Text in a capture or a document is treated as material, with an instruction not to obey it.
- Text found on screen or in repositories is passed to the harness as data with an explicit instruction not to obey it; analysis runs read-only tools.
- Passthrough camera frames are analysed on the headset; only a short text observation with its source and confidence leaves it, and only to the paired PC.
- The microphone is open only after the user asks to talk and closes by itself; that recording goes to the PC and from there to OpenAI for transcription, and is not stored. The newest spoken reply is kept as one local file (`captures\last-reply.wav`) for checking the voice. Transcripts are in the local host log.
- The OpenAI key is read from the environment or from a file only the owner's Windows account can decrypt (DPAPI); it is never logged, never in a snapshot, never in the repository.
- Tokens are never logged or committed (`.gitignore`), pushes are never forced and never target the default branch, projects are added only locally by the user.

## 6. Scope statements

- MR experience is supported while our Quest app is running; it is not an always-on layer over other immersive apps.
- Mobile is out of scope; the protocol only leaves room for more roles.
- The desktop MR simulator exists to exercise the cross-process logic without a headset. Results obtained with it are labelled as such and are never reported as Quest results.
