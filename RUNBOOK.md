# RUNBOOK — Yonder

## 0. What you run

| Where | What | Path |
|---|---|---|
| Windows | Play Demon Gate solo with a mouse and keyboard | `dist/Yonder/Play-Demon-Gate.cmd` |
| PC | Companion Host (tray) — also starts the character client | `dist/Yonder/LivingAIPartner-Host/LivingPartner.Host.exe` |
| PC | Windows character client (started by the host) | `dist/Yonder/LivingAIPartner-Windows/LivingAIPartner.exe` |
| MacBook | Standalone companion and TV stage | `dist/Yonder/Mac/LivingPartner-Mac-arm64.zip` or `LivingPartner-Mac-x64.zip` |
| Quest 3 / 3S | MR app | `dist/Yonder/LivingAIPartner-Quest/LivingAIPartner.apk` |

Connect the TV to the MacBook and open `Yonder TV/Start-Yonder.command`. Choose **Demon Gate · Play solo** for the single-player desktop game. For two-player Gomoku, pair the Quest on the same local network, calibrate the TV and open the board fullscreen. Both people can see the turns, aiming cursor and stones on the TV. [MacBook setup and game instructions](Companion/LivingPartner.Mac/README.md)

## 1. Start the PC side

```powershell
.\Start-LivingPartner.ps1            # normal use (listens on the LAN so the headset can connect)
.\Start-LivingPartner.ps1 -Monitor DISPLAY2   # choose the start display of the PC character
```

- First run: Windows Firewall asks about `LivingPartner.Host.exe`. Allow **private networks**.
- A tray icon appears. Right-click it for: status, **Pair a headset (code …)**, Diagnostics, Portal debug overlay, Quiet mode, Stop current task / transition, Restart character client, Open logs folder, Quit.
- Local diagnostics (no secrets): `http://127.0.0.1:47610/status`, or `lpctl status`.
- With an authorized Quest on USB, the launcher forwards the connection and the headset app connects automatically. Double-click the tray icon to open screen calibration or the game menu. On Wi-Fi the Companion window displays the first-time pairing code.

## 2. Install and pair the Quest app

```powershell
adb install -r dist\Yonder\LivingAIPartner-Quest\LivingAIPartner.apk
```

1. Put the headset on the same Wi-Fi as the PC. Enable hand tracking in the headset settings. Put the controllers away — nothing needs them.
2. Start **Yonder**. Allow hand tracking; allow the headset camera permission if you want camera-based observations (optional).
3. The app finds the PC by a LAN beacon. A numpad appears: poke the 6-digit code shown in the tray menu (**Pair a headset**), then **OK**. The token is stored; you only pair once.
   - Beacon blocked on your network? Use USB: `adb reverse tcp:47610 tcp:47610` and restart the app.

### 2.1 With the USB cable in (development)

While the companion is open, connecting an authorized Quest over USB restores the connection and checks the bundled APK. If the build has changed, the companion updates the headset and reopens the Quest app if it was running. The current game session ends during an update.

```powershell
.\tools\Start-Quest.ps1 -Install -Watch 600     # install the APK, forward the port, start the host, wait until the headset is WORN, start the app, follow it
.\tools\Start-Quest.ps1                         # start it again later
```

This path needs no Wi-Fi, no firewall rule and no pairing code: the headset reaches the host through `adb reverse` and the host, started with `--usb-dev`, accepts it. The app can only be started while the headset is on your head (the system refuses from its lock screen), which is why the script waits. The screens are then found by themselves (§3).
## 3. Finding the monitors (by itself)

Nothing to touch. When the app knows of a display it has not found yet, the PC lights each display with a plain colour (red, green, blue - the colour says which display) and the headset's camera finds the four corners of each. **Glance at each lit display once**: the colour goes away the moment that display is found (three displays took 6.5 s on the first run). The partner says when all are found.

- Say **"Hey, Nilo, find my screens"** (or press **Calibrate** on the hand menu) to do it again, for example after moving a monitor.
- It happens again by itself when the headset finds its place in the room anew (recentre, boundary change).
- The colours never stay: a click or a key on a lit display removes them all, taking the headset off removes them within a second, and a round ends by itself after 22 s without a new display.
- It needs the headset's camera permission (`horizonos.permission.HEADSET_CAMERA`; the app asks, or `adb shell pm grant com.livingaipartner.quest horizonos.permission.HEADSET_CAMERA`).
- The desk is sensed with the depth sensor (permission "spatial data"), below the screens.
- The older way - touching the four lit corners with a fingertip - is still there for debugging: `lpctl debug calibrateTouch`.

## 4. Everyday use (hands only)

| You do | The partner does |
|---|---|
| Hold a palm up, steady, near it | walks over, steps onto your fingers, then onto your palm; balances as you move |
| Stroke its head gently / jab at it | happy / startled |
| Point at a spot on a monitor and **pinch** | a small ring shows the resolved spot (orange + reason if it had to move) |
| **Pinch again** on the ring | looks at you, looks at the spot, turns, walks/runs across the desk, jumps and passes through the glass — head first, feet last |
| Point + pinch twice while it is in the PC | it comes out of the screen at that spot and lands on the desk |
| Hand menu **Return** | comes out near where it currently stands |
| Hand menu **Look** (after selecting a spot, or while it is in a window) | analyses that window (screenshot + linked project) |
| Poke **Choose** on a proposal card | the chosen change is made on a separate branch and tested |
| Poke **Approve** on the task panel | pushes that branch (only if the project allows publishing) |
| Hand menu **Timer** / timer panel buttons | creates and starts a 25-minute timer module; **Go** (start / pause), **Reset**, **+5m** / **-5m**, **View** |
| Hand menu **Rain** | virtual rain; cover its head with your hand to shelter it |
| **Pinch the partner** (fingers on it, or point at it and pinch - also through the glass while it is in the PC), or hand menu **Talk** | it turns to you and listens ("I'm listening."); speak; it stops by itself when you stop (or pinch again), then answers in its speech bubble |
| Say "go in there" **while pointing** at a spot on a monitor | it goes in at the spot you pointed at while you were speaking |
| Hand menu **Quiet** / **Cancel** | quiet mode (it also stops speaking aloud) / cancel the current transition and task |
| Point at a window and say "Hey, Nilo, what do you think of this?" (or hand menu **Look**) | it goes in, looks at that window, tells you the main point, and saves the notes, a marked-up capture and a redrawn revised version on the PC (§4.1) |
| Point at a window showing a PDF or a picture and say "Hey, Nilo, read this file" | it reads the file itself, tells you what it is, and saves notes and a revised version; afterwards you can ask it about the file (§4.1) |
| Hand menu **Tennis**, or say "Hey, Nilo, let's play tennis" | it goes into the screen if it is not there, a court opens inside the monitor, and it serves (§4.2). **Stop** (the same button) or "stop tennis" ends the game |

On Windows, enable the microphone and configure transcription with `.\tools\Set-OpenAiKey.ps1`. Restart the host, then say **“Hey, Nilo”** followed by a request. The Windows host sends microphone phrases to OpenAI for transcription. Use the tray microphone or enter text with `lpctl utter "start a 25-minute timer"`. The Mac companion uses the English speech recognition model bundled in its download.

### 4.1 Looking at a screen, reading a file

Needs the OpenAI key (above). Nothing is looked at or read unless you ask; what is captured or read goes to the OpenAI API with your key.

- **Review a screen.** With the headset: point at the window and ask (or select a spot and press **Look**). Without a headset: tray icon > **Look at my window: feedback + revised version** (the window you were last working in). The partner says the most important point at once; about half a minute later the files are in `Documents\LivingAIPartner\Reviews\<time>-<window>\`: `review.pdf` (summary, the capture with numbered marks, each note with the problem and the change, the revised version, the original), `revised.png` (the screen redrawn with the changes), `feedback.png`, `original.png`. In MR, call the partner back and it brings the notes as cards; **Open** on a card or **Preview** on the panel opens `review.pdf` on the PC, **Close** puts them away. Without a headset the PDF opens by itself.
- **Read a file** (PDF, PNG / JPG / WebP / BMP / GIF, TXT / MD, up to 24 MB). Three ways to say which: point at (or be inside) the window that shows it - the file is found from the name in the window title; select it in File Explorer and point at that window; or tray icon > **Show a file to the partner...**. It saves `notes.pdf` (summary, key points, details, suggestions) and, for a document, `revised.pdf` / `revised.txt` (the improved text; where the document leaves a fact out it writes `[date?]` instead of inventing one). A picture file is treated like a screen (the three review files). If no file can be told from the window, it reads what the window shows and says so.
- Afterwards you can ask about it ("what was the budget in that file?"). It answers from the notes it took, not by reading again.
- A window that belongs to a project linked in §5 is analysed by the coding agent instead (proposal cards, **Choose**).
- Models: `gpt-5.4` to look and read, `gpt-image-2` to redraw (`LP_OPENAI_VISION_MODEL`, `LP_OPENAI_IMAGE_MODEL`, `LP_OPENAI_IMAGE_QUALITY` change them). One review costs a few cents.

Try it without a headset:

```powershell
.\tools\Make-ReviewSamples.ps1                                  # a flawed sign-up screen (PNG) and a flawed meeting note (PDF)
.\tools\Start-Dev.ps1 -LoopbackOnly                             # host + PC character
$ctl = "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
& $ctl readfile samples\review\meeting-notes.pdf "Read this file and suggest changes"     # -> notes.pdf + revised.pdf, opened when done
& $ctl utter "What was the budget in that file?"                                   # answered from what it read
& $ctl readfile samples\review\signup-form.png "Give me feedback on this design"  # -> review.pdf + revised.png + feedback.png
& $ctl review 0 0 "What do you think of this window?"            # the window you were last using (0 0), or the one at a pixel
& $ctl tasks                                                     # status, notes and the saved files
```

The whole flow through the simulator (pointing, going in, coming back with the cards): `.\tools\Start-Dev.ps1 -Sim -LoopbackOnly`, then `.\tools\Run-ReviewChecks.ps1` (writes `artifacts\review-checks.txt`).

### 4.2 Tennis

The monitor's glass is the net. The partner plays inside the monitor, in a court as wide and as high as the screen; you play in front of it.

- Start: hand menu **Tennis** or "let's play tennis". If the partner is on the desk it first goes into the screen you point at (otherwise the nearest one). The monitor has to be calibrated (§3).
- The partner serves. Its ball comes out of the screen towards you: meet it with an **open hand** - a ring shows the racket your palm is. The ball goes back in by itself; moving your hand sideways as you hit steers it, a faster hand sends it flatter. A ball you do not return is the partner's point once it has bounced twice on the desk or gone past you; a ball the partner misses is yours. First to 5.
- The score is above the monitor in the room and on the back wall of the court. **Stop** (the same button) or "stop tennis" ends the game; so does sending the partner anywhere else.
- While a game runs the court covers that whole monitor (your windows are still there underneath and clicks pass through).

Try it without a headset (the simulator's scripted hand plays): `.\tools\Start-Dev.ps1 -Sim -LoopbackOnly`, then `lpctl debug simHand 0 0 0 tennis` and `lpctl tennis start` (`lpctl tennis stop` to end; `simHand ... tennisMiss` lets every ball go), or the whole check: `.\tools\Run-TennisChecks.ps1`.
## 5. Connect a project (for analysis and code changes)

Projects are only ever added locally on the PC:

```powershell
lpctl allow-project <path> <name> <window title fragment> <preview file> <allowPush 0|1> <test command...>
# reviewer sample (own git history, LOCAL bare remote, no account or key needed):
.\tools\Setup-Sample.ps1 -Open
```

- A window is linked to a project by a fragment of its title. Without a link the partner analyses the screenshot only and says so; it never edits files.
- Code changes need a git repository. Work happens in `%LOCALAPPDATA%\LivingAIPartner\worktrees\<task>` on branch `partner/<task>`; your checkout is not touched.
- The harness is the Claude Code CLI you already have installed and logged in (`claude`). One managed task at a time, with a budget cap and a timeout. Tray → **Stop current task / transition** kills it. To use the Codex CLI instead, start the host with `--harness codex` (development: `tools\Start-Dev.ps1 -Harness codex`); it must already be installed and logged in.

## 6. Portal debug mode

Tray → **Portal debug overlay** (or `lpctl overlay 1`) shows state, owner, shared-clock time, crossing progress, frame rate and read-back latency on both sides, the monitor outlines in MR and the portal cross / floor line on the PC.

```powershell
lpctl freeze 0.5      # the next (or current) crossing stops with half the body through; also 0, 0.25, 0.75, 1
lpctl play            # continue
lpctl slow 0.25       # slow motion (1 = normal)
lpctl rewind          # pull the body back to the side it came from
lpctl loop 10 700 -650   # 10 crossings back and forth through that pixel
```

Per-transition measurements (state trace, max shared-clock error per side, heartbeat counts) are appended to `%LOCALAPPDATA%\LivingAIPartner\logs\transitions.jsonl`.

## 7. Troubleshooting

| Symptom | Check |
|---|---|
| Headset is looking for a companion | On Windows, open the launcher to bring up the Companion window. On Mac, open `Yonder TV/Start-Yonder.command` and select the TV in the browser. Check the six-digit pairing code and that Quest and the computer are on the same local network. On Windows, allow the host on private networks. |
| "I can't go there: …" / "I can't go right now: …" | the reason is shown: PC client not running, protected desktop (UAC), target window gone, partner is on the other side |
| Ring is orange | not enough room at the requested spot; the ring shows the nearest valid spot — pinch again to accept or point elsewhere |
| "The room changed - I'll find your screens again." | the headset re-centred: the screens are found again by themselves (§3) |
| Partner missing on the PC | it is in MR, or its workspace window is minimised, or a full-screen app / UAC prompt owns that display (`lpctl status` shows `mode` and `suspended`) |
| Partner not where the monitor is | re-calibrate that monitor; check the reported fit error |
| Analysis fails | `lpctl tasks` shows the real failure (window not capturable, harness not logged in, timeout) |

Logs: `%LOCALAPPDATA%\LivingAIPartner\logs\` (`host.log`, `pc-client.log`, `transitions.jsonl`, `tasks.jsonl`, `observations.jsonl`). Quest: `adb logcat -s Unity`.

## 8. Build from source

```powershell
dotnet build Companion\LivingPartner.Host -c Release
dotnet build Companion\LivingPartner.Ctl -c Release
dotnet test  Companion\LivingPartner.Tests -c Release
dotnet publish Companion\LivingPartner.Host -c Release -r win-x64 --self-contained true -o dist\LivingAIPartner-Host
dotnet publish Companion\LivingPartner.Ctl  -c Release -r win-x64 --self-contained true -o dist\LivingAIPartner-Host
```

Unity 6000.3.13f1 with Windows (Mono) and Android build support: menu **LivingPartner → Build Windows Character Client**, **Configure Quest XR**, **Build Quest APK**.

The Unity side is an ordinary scene/prefab project (`Assets/LivingPartner/Scenes`, `Prefabs`, `Art`): open `PcCharacter.unity`, `MrSimulator.unity` or `QuestMR.unity` to see and edit every object in the Hierarchy. `Main.unity` is only the Windows entry scene that picks one of the first two by `-lp-role`. **LivingPartner → Author Assets, Prefabs and Scenes** regenerates the prefabs and scenes from `Editor/LPSceneBuilder.cs` and checks that no serialized reference is left empty (result in `artifacts/scene-build.txt`); it replaces hand edits made to those generated files.

The partner and the typeface are generated by the same authoring menu (**LivingPartner → Author Assets, Prefabs and Scenes**): the partner from the model in `Art/Characters/Tripo` (`PartnerModelBuilder`; replace `TripoPartner.fbx` and `TripoPartner_Albedo.png` with a new export in the same T-pose and run the menu again), letters from `StoryFontBuilder.Define()`. Open `Scenes/CharacterGallery.unity` to look at the partner from four sides and the font specimen sheet. **LivingPartner → Render Partner Check Sheets** writes `artifacts\partner-pose-sheet.png` (every pose) and `artifacts\partner-cut-sheet.png` (the body cut by the screen at five moments).

Development without a headset: `tools\Start-Dev.ps1 -Sim -LoopbackOnly -Monitor DISPLAY2` starts the host, the character client and the **desktop MR simulator** (mouse = pointing ray, click = pinch, keys 1–4 = scripted hand). The simulator is a verification tool; it is not a Quest and its results are labelled `simulated`. Pressing Play in the Unity Editor on `MrSimulator.unity` does the same with the Editor as the MR client (Game-view mouse = pointing hand).

Scripted checks against a running `Start-Dev.ps1` set-up (each writes its result under `artifacts\`):

| Script | Checks |
|---|---|
| `tools\Run-PortalLoop.ps1` | repeated MR→PC→MR crossings at several positions, with shared-clock error |
| `tools\Run-RecoveryTests.ps1` | window moved / minimised, MR or PC client killed before and during a crossing |
| `tools\Run-MrFeatureChecks.ps1` | virtual rain and shelter, hand ride, timer by utterance, two-pinch selection (scripted simulator hands) |
| `tools\Run-TennisChecks.ps1` | the tennis game end to end: start from the room, a rally, points to 5, stopping, coming back out (scripted simulator hand) |
| `tools\Run-ReviewChecks.ps1` | review of a pointed window (feedback.png, revised.png, review.pdf), reading a PDF, a follow-up question, finding the file from a window title - real OpenAI calls, a few cents per run |
| `tools\Run-DesignChecks.ps1` | the character design is set and read back from the host, the Windows client and the MR client; an unknown design is refused |
| `tools\Run-WindowFollowChecks.ps1` | the partner follows a real window that is moved, minimised, restored |
| `tools\Run-FullscreenSuspendCheck.ps1` | the partner hides while a full-screen window is in the foreground on its display and returns afterwards (takes the foreground for about three seconds) |
| `tools\Run-PairingFlow.ps1` | first-run pairing on the hand numpad: wrong code refused, right code accepted (start the host without `-Sim` for this one) |
| `tools\Run-HarnessE2E.ps1` | real analysis → proposal → change → tests → approved push on the sample project |
| `tools\Record-RoundTrip.ps1`, `tools\Record-MonitorTransfer.ps1` | unedited screen recordings of a round trip and of a move between stacked displays |
| `tools\Capture-HalfThrough.ps1` | freezes a crossing part-way (`-Progress 0.25 / 0.5 / 0.75`) in each direction and captures what the PC and the MR side each draw |
| `tools\Show-Backdrop.ps1` | a plain non-activating window to capture and record in front of, so that evidence does not contain other open windows (`-Close` removes it) |
| `tools\Run-VoiceChecks.ps1` | voice conversation end to end with recordings from `samples\voice` fed through the microphone path: a spoken command, small talk in Korean, speaking while pointing, speaking to it inside the PC, and a pinch on the partner opening the real microphone (cancelled) |
| `tools\Run-PointingChecks.ps1` | pointed spot vs understood spot for 9 spots on every display, and two real go-ins by point + pinch + pinch |
| `tools\Capture-MrUiStates.ps1`, `tools\Summarize-Transitions.ps1` | simulator screenshots of the MR UI states; statistics over every transition the host logged |


## Marketplace, game packs and things made with words (added 2026-11-17)

- **PC shop**: tray icon > "Open the marketplace", or `http://127.0.0.1:47610/market` on the PC that runs the host.
  The box "Make something with words" makes a module (furniture, skill, game pack, ability) and publishes it.
- **Shop in MR**: left palm > Tools > Market, or say "Hey, Nilo, open the shop". Poke a card, then Get / Remove. Stars are
  the shop's own play money (300 to start); nothing is ever charged.
- Open **Play** for Tennis, Goalkeeper, Demon Gate and Gomoku. Say “Hey, Nilo, let’s play Gomoku” to open the shared board or “Hey, Nilo, make a game pack” to create a new game.
- **Things and skills**: "Hey, Nilo, make a desk", "Hey, Nilo, when I ask for a song, go to the record player and dance" (teaches a skill).
  With a Tripo key (`tools/Set-TripoKey.ps1`) things get a generated model; without one, a built-in shape.
- **A PC screen in the room**: Tools > Bring screen, or "Hey, Nilo, bring screen one into the room"; "Send back" closes it.
- **From the PC, without a headset**: `lpctl market | install <name> | remove <name> | make "<words>" | screen 1 | song export`.
- **Disk space**: an Android build needs about 3 GB free on C:. `MetaVRHack/Library/Bee` is a build cache and can be
  deleted at any time (the next build is slower once).


## A real marketplace: the hub, nicknames, selling and buying (added 2026-11-17, late morning)

The shop is no longer a list kept on one PC. A small server, the **hub** (`LivingPartner.MarketHub.exe`, beside the
host), holds accounts (a nickname, 300 stars to start), what everybody has put up for sale and who bought what. Stars
move from the buyer to the seller; there is no other way to earn them and no real money anywhere.

- **Out of the box** the host starts its own hub on this PC (`http://127.0.0.1:47620`) and registers you with a made-up
  nickname ("NiloFriend-1234"). Change it on the shop page (banner at the top, or the profile chip) or `lpctl nickname <name>`.
- **One marketplace for several PCs**: run `LivingPartner.MarketHub.exe --lan` on one machine (or a server; the hub is
  plain .NET 8 and has no Windows code) and on the others set the address: profile drawer on the shop page,
  `lpctl hub http://that-pc:47620`, or the environment variable `LP_MARKET_HUB`. No public hub is hosted by us.
- **Selling**: shop page > My shop (set a price, Start selling; price stepper and Stop selling for what is on sale), in MR the
  shop board's **Mine** tab (- / + and "Sell for N stars" / "Stop selling"), or say "Hey, Nilo, sell this for 20 stars".
- **Buying**: Buy on the page (a small confirm sheet), two taps on the MR board ("Buy" then "Tap again to buy"), or
  "Hey, Nilo, buy the cactus pot". Uninstalling keeps what you bought; there are no refunds.
- **Module files**: Export writes `Documents/LivingAIPartner/Modules/<name>.yonder`; Import (or `lpctl import <path>`)
  installs one. Only your own things can be exported.
- **Showing a sale on one PC**: `lpctl hub-as <nickname> buy "<listing>"` acts as a second TEST account on the hub
  (it prints that it is one). `lpctl hub-as <nickname> publish-sample` puts two sample listings up under that name.

## Things without Tripo: sculpted by the language model (added 2026-11-17)

When no Tripo key is stored, a thing made with words is built out of simple parts (boxes, cylinders, cones, balls,
capsules, rings - at most 40) by the language model, in the same toy look as the built-in furniture. It appears at
once in a built-in shape and changes into the sculpted one when that is ready (13-65 s). Samples:
`artifacts/sculpt-sheet.png`. To try one without starting anything: `LivingPartner.Host.exe --sculpt "guitar" --out guitar.json`
(needs the OpenAI key). A Tripo model, when a key exists, still takes precedence.


## Asking the pet to make things; the bag; games as modules (added 2026-11-17, afternoon)

- **Making**: Ask Nilo ("Hey, Nilo, make a wooden chair"). PDF and PNG jobs appear in the queue, where you can open the finished file. Nilo hammers at the room workbench when making a 3D object and goes into the screen for a game job. Finished objects are stored in the bag. When Nilo offers to list an item in the Windows marketplace, answer on the card (price - / +, Sell it / Keep it) or by voice within 25 s without its name
  ("yes", "sell it for 20 stars", "no"); `lpctl offer yes [price] | no` from the PC.
- **The menu** (headset): open the left hand towards your face or upwards. Hold at least three fingers open for a moment. A fan of stickers unfolds from the palm and follows it. Nilo can stand on your right palm. Close the left hand
  or turn it away and it folds back. While the right index fingertip is within 9 cm of it, it stands still in the room
  (so pressing is steady). During a game the palm carries only a "Stop" chip. By voice ("Hey, Nilo, open the bag") the same
  fan stands in the room until closed. Pages: **Bag** (everything you own, one list: things, game packs, skills,
  places), **Nilo**, **Tools**.
  Press a sticker: a thing comes out onto the desk / goes back, a game pack starts / stops, a skill is used / stopped,
  a place is switched on / off; the line at the bottom says what happened. A thing on the desk is also put back by
  pressing it and then "Put away" over it, or by carrying it (pinch) to the open other palm and letting go there.
- **Pressing any board**: touch a button with an index fingertip, or - from more than 30 cm away - point at the board
  (a thin ray and a round cursor appear) and pinch.
  Simulator checks: `lpctl debug menu 1|0` (the fan standing in the room), `menuTab 0-2`, `menuPage n`,
  `menuPress i [0 0 name]`, `menuRay i`, `heard 0|1 0 0 "text"`; the palm: `simHand 0 0 0 palmOpen|palmClosed|palmAway|none`,
  `palmMove x y z [drift|jitter|lost|lostLong]`, `palmFinger <dist>` / `palmFinger 0 0 0 off`, `palmPress i` (`palmPress 0 0 0 chip`),
  `palmStat 1` (follow numbers in the log), `poke 0 0 0 name` + `putAway`, `carryPalm 0 0 0 name`,
  `making 1|0 <progress> 0 "stage"`, `madeFly [slow]`, `madeCard 1|0`.
- Tennis, Goalkeeper, Demon Gate and Gomoku are the built-in games. Custom game creation is available from the workshop.
- **Icons**: every picture is drawn in one style (the object alone as a sticker). `lpctl world redraw <name>` draws
  one again.
- **After every test run** `tools/Stop-Dev.ps1` says whether processes were left behind. If it ever does: restart
  Windows before they pile up (see TEST_RESULTS / BLOCKERS for what happened on 2026-11-17).
- **Unity dialogs**: `tools/Unity-Dialog.ps1` lists (and presses) modal dialogs of the Editor; scripted calls into the
  Editor hang while one is open.

## Added on 2026-11-17 (evening)

- **Giving orders**: say "Hey, Nilo" followed by the request anywhere, no button. The menu board's Help tab lists phrases and
  shows "Heard: ..." (what the partner understood last). On the PC: the talk box of the market page, or `lpctl say`.
- **Menu**: open the left palm and touch an item in the fan with the right index finger. Say “Hey, Nilo, open the bag” to open it in the room. During a
  game the palm shows "Stop" instead.
- **Things on the desk** can be pressed: the partner goes and uses them; the record player shows its records.
- **Surroundings** that are owned are tiles in the Bag: press = on, press again = off (`lpctl world ambience <id|clear>`).
- Custom scripted games can use the hand in front of the monitor as a cursor. Use `lpctl glass` to inspect a running scripted game. Demon Gate also opens directly as a solo mouse-controlled desktop game.
- **Pointing**: "Hey, Nilo, what is this?" reads the monitor that is pointed at, round the pointed spot (hand first, then where
  the user looks). The PC frames the region on the real screen while it reads. Test without capturing a real screen:
  start the host with `LP_LOOK_TEST_IMAGE=<png>`.
- **Sound**: the headset's own media volume must be above 0 (volume + under the right side); the app raises it from 0
  once at start and says so if it cannot. The device log has `[MR] sound probe:` lines with the measured output level.

## Modules are scripts (added 2026-11-17, night)

- **Making**: "Hey, Nilo, make a Gomoku game" / "Hey, Nilo, make a stopwatch" / "Hey, Nilo, make a baseball bat that I can swing when equipped" (or
  `lpctl make "<words>"`, `lpctl world make "<words>"`). The partner WRITES the module: the request goes to the language
  model with the script API, a small Lua program comes back, the host RUNS it in a checker, and errors and warnings go
  back to the model until it passes (three repairs at most; the best version is kept). Meanwhile the work queue shows
  "Writing the code", "Trying it out", "Fixing a bug (2)", "Painting the pictures" (`--watch 300` shows it). What passes
  is installed and offered for the marketplace as before. A program that never ran is NOT installed: the partner says
  it could not get that one to work. A thing whose script failed still exists, as an ordinary thing.
- **What is a script**: a game or app = `GamePackDef.script` (template `script`, pictures in `art`), run on the glass
  stage by `Shared/GlassScript.cs`; what a thing does = `ItemDef.script`, a taught skill = `SkillDef.script`, run in the
  room by `Shared/RoomScript.cs` (headset). Tennis, Goalkeeper, Demon Gate and Gomoku have dedicated game implementations.
- **Where the API text lives**: `GlassScriptApi.Doc` (games), `RoomScriptApi.Doc` (things, skills), `LuaMotion.Doc`
  (new motions of the partner) - pasted verbatim into the writer's prompt (`Companion/LivingPartner.Host/ScriptWriter.cs`;
  the checks are `ScriptRules.cs`). Game design, code generation and AI playtest review use Claude Fable 5.2. Set `ANTHROPIC_API_KEY` on the host for custom game generation. Installed games run locally.
- **Scripted things**: `lpctl world wear <thing> [handR|handL|head|back]`, `unwear <thing>`, `act <thing> <act>`; by
  voice "Hey, Nilo, equip the bat", "Hey, Nilo, swing", "Hey, Nilo, put the bat away". `lpctl world` shows `script=N lines` and `WORN on ...`.
- **Tools**: `lpctl script check <file.lua> [game|thing|skill]` runs any script in the same checker (no host needed);
  `lpctl script show <id|name>` prints the script of a game, thing or skill in the bag; `lpctl remake <game> "<request>"`
  has one of the user's own games written again under its id, name and listing.
- **Marketplace**: the script and its pictures travel with the module (hub, `.bambi` file). A script from outside is
  run before it is installed: one that breaks is refused with the line; one with warnings is installed. A listing
  whose module is a script says "made of: a script (N lines)".
