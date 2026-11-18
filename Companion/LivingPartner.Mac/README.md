# Yonder on Mac and TV

Connect your MacBook to the TV over HDMI or a display adapter. In macOS Displays, choose an extended desktop if you want to keep other windows on the laptop. Use the arm64 kit for Apple silicon or x64 for an Intel Mac.

## Set up the room

1. Extract the complete kit and open **Start-Yonder.command**. Move the browser window to the TV, then select **Use this display**. To enter a known TV diagonal, open **Enter screen size** first.
2. Open **Yonder** on Quest using the same Wi-Fi as the Mac. Enter the six-digit code displayed on the TV if asked. Allow incoming connections when macOS prompts. The local display page uses TCP 47610 and headset discovery uses UDP 47611.
3. Look at the TV and follow the screen-location prompts. **Calibrate TV** repeats this step. Allow microphone access on Quest.
4. Say **“Hey, Nilo.”** When Nilo answers, speak your request, or use a single phrase such as **“Hey, Nilo, enter the TV.”** Say **“Hey, Nilo, come back”** to return to the room. A text field in TV settings also accepts commands. Spoken replies can be enabled in the TV controls.

## Star Garden

Say **“Hey, Nilo, open the star garden”** or choose **Star Garden** under **Places** in your bag. Pinch a patch of soil to plant and pinch again to water. After 45 seconds, a gold star grows above the leaves. Pinch it to collect three stars. Pinch a sign to install an upgrade. Rain Bell costs three stars and waters subsequent seeds. Sun Dial costs six and reduces growing time to 25 seconds. Twin Bloom costs nine and doubles the harvest. Your beds, stars and upgrades stay on the headset when you leave the garden.

## Play on the TV

- **Goalkeeper.** Block Nilo’s penalty shots with your hands as the ball crosses from the TV into the room.
- **Tennis.** Swing your hand to return the serve into the TV. Both displays follow the same ball and score.
- **Demon Gate.** **Demon Gate · Play solo** opens the desktop game. Aim with the mouse, hold the left button to fire, and move with A and D. Choose an augment when you level up. The headset game menu also offers an MR encounter.
- **Gomoku for two.** Calibrate the TV, then choose **Two-player Gomoku**. Both players look at the 15-line board on the TV. The headset wearer places black stones by pointing at a free intersection and pinching. For white, the wearer keeps the guest’s pointing hand and TV in view. The guest holds a pointed finger steady over the desired intersection until the marker completes. Five in a row wins. Choose **Play again** for another round or **Close** to finish.

Guest pointing uses the color camera on Quest 3 or 3S. After permission and confirmation, the headset sends frames to the companion on your local network during the guest’s turn. Detection runs in the TV browser. Frames are transient. If tracking loses the finger, bring the hand back into view and wait for the marker.

## Install or update Yonder on Quest

With developer mode enabled, connect one Quest to the Mac with a USB data cable. Open **Install-Quest.command** and accept USB debugging in your headset. The installer uses the bundled APK, checks the connected device and preserves app data on update. If ADB is missing, extract Android SDK Platform-Tools for Mac into Downloads. Open **Unknown Sources → Yonder** in the headset after installation. Wi-Fi play works once the USB cable is removed.

## Creation and privacy

Open **Work** on the TV to follow a creation job and download the result. Ask for a researched PDF, new icon options or a 3D object. Screen-based requests need macOS screen recording permission. Asking Nilo to make an object you are holding sends one camera image to your configured generation service.

For generated game rules, set the Anthropic API key in the Mac keychain under the service name `LivingPartner.Anthropic`, or pass `ANTHROPIC_API_KEY` to the companion process. New game generation requires model access. Installed games and local speech recognition work offline. Research, image generation and freeform 3D creation use the OpenAI credential under `LivingPartner.OpenAI` or `OPENAI_API_KEY`.

The Mac stores settings and pairing data in its application support directory. Close the launcher with Control-C to stop the companion.

## Development

Build with `dotnet build Companion/LivingPartner.Mac -c Release`. From the repository root, `node Companion/LivingPartner.Mac/test/protocol.mjs` exercises pairing, calibration, travel, games and wake requests. `node Companion/LivingPartner.Mac/test/webgl.mjs` checks the TV display using headless Chromium. To package both Mac architectures with their speech model and the matching headset app, run `python tools/Build-Mac.py --quest-apk path/to/LivingAIPartner.apk`.
