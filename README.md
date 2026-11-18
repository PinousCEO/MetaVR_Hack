# Yonder

Meet Nilo, an AI teammate who can see the mug on your desk, make a 3D copy and carry it into your computer.

## Download and play

Install **Yonder on Quest**, then choose the **companion for your computer**.

<p>
  <a href="https://github.com/PinousCEO/MetaVR_Hack/releases/download/yonder-0.1.0/Yonder-Quest.apk"><img src="reports/media/download-quest.svg" width="188" alt="Download the APK for Meta Quest 3 or 3S"></a>
  <a href="https://github.com/PinousCEO/MetaVR_Hack/releases/download/yonder-companion-0.1.1/Yonder-Windows-Setup.exe"><img src="reports/media/download-windows.svg" width="188" alt="Download the Windows x64 installer"></a>
  <a href="https://github.com/PinousCEO/MetaVR_Hack/releases/download/yonder-companion-0.1.1/Yonder-Mac-Apple-Silicon.zip"><img src="reports/media/download-apple-silicon.svg" width="188" alt="Download the Apple Silicon Mac companion"></a>
  <a href="https://github.com/PinousCEO/MetaVR_Hack/releases/download/yonder-companion-0.1.1/Yonder-Mac-Intel.zip"><img src="reports/media/download-intel.svg" width="188" alt="Download the Intel Mac companion"></a>
</p>

On a Mac, open **Apple menu → About This Mac**. Choose **Apple Silicon** for an Apple M-series chip or **Intel** for an Intel processor. Both Mac kits include the Quest APK.

[Windows setup](#windows-setup) · [Mac setup](#mac-setup) · [Updated companions](https://github.com/PinousCEO/MetaVR_Hack/releases/tag/yonder-companion-0.1.1) · [Competition build](https://github.com/PinousCEO/MetaVR_Hack/releases/tag/yonder-0.1.0)

<p align="center"><img src="reports/media/yonder-cover.jpg" width="580" alt="Nilo stepping through a glowing doorway in a real monitor toward an outstretched hand"></p>

I built Yonder for the days I spend working alone. When I need a new tool, I can describe it out loud and put it to work during the same session.

### Mac setup

1. Extract the entire ZIP. In the **Yonder TV** folder, open **Start-Yonder.command**. Move the browser window to the Mac display or an HDMI-connected TV and choose **Use this display**.
2. If Yonder is new to your headset, use **Install-Quest.command** to sideload the bundled APK by USB. After installation, disconnect the cable.
3. Open **Yonder** on Quest while the Mac and headset are on the same Wi-Fi. Allow incoming connections on the Mac if prompted. Enter the six-digit code on Quest if asked, then look at the display to calibrate it. [Full Mac and TV guide](Companion/LivingPartner.Mac/README.md)

### Your first session

Open **First session guide** in the Windows connection window or Mac TV settings. Start by locating one monitor, pointing at it and saying **"Hey, Nilo, enter the screen."** Then say **"Hey, Nilo, come back."** Once you have tried the crossing, use the guide for object creation, screen assistance, games and reconnecting after sleep. The guide is included with the companion and opens before pairing.

### Windows setup

1. Run **Yonder-Windows-Setup.exe**. Put the PC and Quest on the same local network. Open **Yonder Companion** from the Start menu and allow it through Windows Firewall on your private network.
2. Open **Yonder** in the headset. For the first wireless connection, enter the six-digit code displayed by the PC companion. Give Yonder access to the headset camera for monitor and object recognition, and the microphone for voice.
3. Glance at each monitor when it lights up so Yonder can locate the display in your room. After moving a screen, use **Calibrate** in the hand menu or say **“Hey, Nilo, find my screens.”**
4. Point at a spot on the monitor and send Nilo inside. Watch his feet as he crosses the glass. Try a game of tennis and return his serve with your hand.

### Install the Quest APK

Use the competition release channel if invited, or sideload on a developer-enabled Quest. With Android platform tools installed and USB debugging authorized in the headset, run the following from the folder containing the APK. Once the app is installed, the two devices can pair over Wi-Fi.

~~~powershell
adb install -r Yonder-Quest.apk
~~~

The competition APK is release-signed. If you previously installed the developer-signed demo, Android will reject an in-place update because the signatures differ. Back up any headset data you want to keep before removing the old build. [Connection and troubleshooting guide](RUNBOOK.md)

### Enable AI features

Bring your own service credentials for the features you want to try. The Windows installer includes the two key setup scripts under %LOCALAPPDATA%\Programs\Yonder\tools. The scripts prompt privately and store keys for the current Windows user. Restart Yonder Companion after setup.

| Feature | Setup on Windows |
| --- | --- |
| Voice conversations and screen assistance | Run [Set-OpenAiKey.ps1](tools/Set-OpenAiKey.ps1) or set OPENAI_API_KEY. |
| Turn a physical object into a textured 3D model | Run [Set-TripoKey.ps1](tools/Set-TripoKey.ps1) or set TRIPO_API_KEY. |
| Custom game design, scripting and AI playtest review | Set ANTHROPIC_API_KEY in the companion process environment. Installed games play locally. |

## What to try

| In your room | On the other side of the screen |
| --- | --- |
| Hold up your mug and say **“Hey, Nilo, copy this cup.”** Pick up the model or place it on your desk. | Ask Nilo to export it as a **GLB** and open the same object in a desktop 3D tool. |
| Point at the logo on a page and ask for new ideas. Compare the options together in MR. | Choose a design to receive the PNG. |
| Ask Nilo to make a Meta stock chart. Watch the tool appear in the work queue. | Open the chart on your PC and select a button from outside the screen. Publish a module in the marketplace so other people can use it. |
| Play tennis against Nilo in the monitor, or catch a ball kicked into the room. | Play [Demon Gate](reports/DEMON_GATE_GAMEPLAY.mp4) on the desktop, or share a [Gomoku board](reports/GOMOKU_PLAY.png) on a TV. |

## How it works

**One character across two displays.** Yonder maps the intersection of a pointing ray and the physical monitor to a desktop pixel. The headset and PC share a transition clock. During the crossing, each renderer shows the part of Nilo on its side of the glass. [Architecture](ARCHITECTURE.md)

**New tools during a session.** Unity runs the room and character. Lightweight Lua scripts define new module behavior, so Nilo can build and run a tool while you work, without reinstalling the app.

## For developers

| Directory or guide | Contents |
| --- | --- |
| [Unity project](MetaVRHack/) | Unity 6000.3.13f1, C#, OpenXR, XR Hands, AR Foundation and URP. The Quest and Windows players share character assets and protocol code. |
| [Companion](Companion/) | .NET 8 Windows host, marketplace hub, diagnostics, Mac companion and tests. |
| [Runbook](RUNBOOK.md) | Build commands, simulator, pairing, calibration and troubleshooting. |
| [Device audit](reports/DEVICE_AUDIT_KO.md) · [Test results](TEST_RESULTS.md) | Device observations and test coverage. |
| [Filming guide](Movie/Yonder%20Filming%20Guide.md) | Spoken demo cues for the coffee sequence. |

To package an exported Windows build, run **python tools/Build-WindowsInstaller.py** with NSIS 3 on PATH. To sign an exported Quest APK, use [Sign-QuestRelease.ps1](tools/Sign-QuestRelease.ps1) on the Windows account that owns the release key. [Back up the signing identity](tools/Backup-QuestReleaseKey.ps1) to a private location with a password you choose before moving to another PC.
