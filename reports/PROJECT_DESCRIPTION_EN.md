# Living AI Partner — project description (draft for submission)

> Draft text for the owner to edit. Claims are limited to what is implemented; items that have not been run on a headset are marked. See `TEST_RESULTS.md` before quoting any of this.

## One line

A personal AI partner that lives on your desk in mixed reality, climbs through your real monitor at the spot you point to, works inside your actual Windows desktop, and climbs back out with what it found.

## What it is

Living AI Partner is a productivity companion for solo developers and small teams. It is one character, "Momo", with one identity across two places:

- in your room, through a Meta Quest 3 / 3S in passthrough, operated entirely with your hands;
- on your real Windows PC, as an independent transparent desktop character that walks along your windows and never blocks your mouse.

The link between the two is the product's centre: you point at a place on your physical monitor and pinch. Momo looks at you, looks at the spot, crosses the desk, jumps, and passes through the glass — hands and head first, then body, then legs. While it is half-way, the part still in your room is drawn by the headset and the part already inside is drawn by the PC on the monitor itself, from your viewpoint, as one body. Nothing is swapped, faded or covered by an effect. It comes back the same way, at any spot you choose.

## What it does once inside

- It binds to the program window you sent it into. Move the window and it rides along; minimise it and it is gone until you restore it; put another window on top and it is covered like any other content.
- Asked to "look at this", it captures that one window, reads the linked project with a managed coding-agent session (Claude Code by default, Codex as an alternative), and returns to your room with its findings laid out as cards in space: what the part does, the evidence (file and line), a concrete proposal, and whether each claim was confirmed in source or only inferred.
- Poke a card and the change is made for real on a separate git branch and worktree, the project's own tests run, and the result waits for your approval. Only then is that branch pushed. Your checkout and your default branch are never touched.
- Asked "what do you think of this?" about a window that is not a code project - a design, a slide, a form - it looks at it, tells you the main point, and leaves real files on the PC: the capture with numbered notes, a redrawn revised version, and a PDF of the review. It also reads PDF, picture and text files (the one open in the window, or selected in File Explorer), writes notes and an improved version, and answers questions about what it read.
- It plays tennis with you through the monitor: the glass is the net, it plays in a small court inside the screen, and when its ball comes out into your room you play it back with an open hand. Both machines compute the same ball from each stroke, each drawing its own side of the glass. (Simulator-verified only so far.)
- "Make a timer and start 25 minutes" creates a stored, versioned module you can start, pause, resume, reset and reconfigure later by voice-style text or by poking its panel. It keeps correct time across restarts.
- It behaves like a small living thing with one consistent personality: it steps onto an offered palm and balances as you move, reacts to a gentle stroke or a sudden jab, and in virtual rain it shivers until your hand actually blocks the rain's path to its head — a hand that is merely nearby does not count.

## How it works

- **One Unity 6 project, two players** (Quest/Android and Windows) share the character rig, pose functions, protocol and portal logic. A .NET companion on the PC is the single authority for state, tasks and the portal's shared clock.
- **Deterministic crossing plan.** The crossing is a function of time expressed in the frame of the chosen point on the display surface. Both renderers evaluate the same plan on a synchronised clock and clip the same mesh to their side of the screen plane, so the body lines up without streaming bones.
- **Viewpoint-correct PC rendering.** The headset streams the user's head position relative to the monitor; the PC draws its half with an off-axis projection from that eye position. (A flat monitor cannot provide stereo depth; this aligns the two halves for the viewer's position.)
- **Real coordinates.** Each monitor's lit area is calibrated by touching its four corners; that is linked to Windows displays in physical pixels, including rotated displays and negative coordinates. The physical arrangement is never assumed to match the Windows arrangement.
- **One look on both sides of the glass.** Momo is a small figure in a yellow bonnet with nothing on its face but two eyes, drawn with two-tone shading and an ink outline. The model was generated with Tripo from a reference image; the rig, the skinning and the way the screen cuts the body into two solid halves are made in the project. A picture-book interface: cream paper panels, pastel pill buttons and a rounded typeface, "LPStory", that was drawn stroke by stroke for this project. The portal is a honey-coloured ring with ripples and sparkles drawn around the opening - never over the body that is crossing. The typeface and all interface art are made in the project; the character model is the only generated asset.
- **Honest state.** Every status the character shows — analysing, editing, testing, done, error, needs approval — is a real event from the task pipeline. A failed step is shown as failed.
- **Recovery.** Ownership of the character moves only after the last part of the body has crossed. If the receiving side stops drawing, the body is pulled back the way it came; if a window moves or is minimised mid-approach, the portal follows or waits.

## Hands first

No controller is needed at any point: point and pinch to choose and confirm, poke buttons on a palm menu and on spatial panels, a hand-operated number pad for pairing, fingertip calibration. Speech is optional and not required. *(Status: implemented with OpenXR hand tracking and built into the APK; not yet verified on a headset.)*

## Requirements to run

Meta Quest 3 or 3S, a Windows 11 PC on the same private network running the companion (included), and for code analysis a Claude Code CLI (or Codex CLI) login on that PC. A sample project with a local git remote is included so that reviewers need no personal repository or key.

## Built with

Unity 6 (URP), OpenXR with Meta OpenXR features (passthrough, hand tracking, passthrough camera frames), .NET 8, Win32 layered-window compositing, Claude Code CLI and Codex CLI as interchangeable harnesses. The character is an original toon mascot prefab created for this project (no third-party art); the Unity project is an ordinary scene/prefab project with serialized references.

## Current limitations (do not omit)

- The headset experience has not yet been verified on a device; alignment and latency through the optics are unmeasured.
- Voice conversation (pinch the partner and speak; it answers aloud) uses OpenAI speech and language models and needs the owner's API key on the PC; without one the partner works by hand and typed text. On the headset this is untested.
- Browser tabs are not identified individually (window-level only).
- The MR experience runs while this app is open; it is not an overlay on other immersive apps.
