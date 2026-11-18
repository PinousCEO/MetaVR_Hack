# Yonder release checks

| Area | Check performed |
|---|---|
| Shared rules and Windows companion | Ran the complete .NET Release suite. It covers wake matching, travel, game rules, the workshop and garden progression, including insufficient funds and repeat purchases. |
| Logo and star garden | Launched the Windows MR simulator, captured the startup reveal, planted and watered three pots, waited for ripe fruit, harvested nine stars, and bought Rain Bell and Sun Dial. Reopened the saved garden, grew the next crop, and purchased Twin Bloom. |
| Voice | Fed an English “Hey, Nilo” audio clip through the Mac companion's bundled speech recognizer. Verified the wake event and the optional English audio reply. Exercised the Windows open-microphone wake matching in the .NET suite. |
| Travel and games | Used the Mac companion protocol harness to exercise pairing, screen calibration, entry, return, and reconnect. Ran the goalkeeper save, tennis rally, game selection and stop checks. Played and recorded the standalone Demon Gate desktop game. |
| TV and second player | Ran the TV interface in Chromium. Passed camera test frames through the hand detector and the authenticated game connection, checked the aiming cursor, and verified one white stone was placed for one held gesture. |
| Distribution | Built the Unity Windows player and Android release APK, published the Windows companion, and packaged Mac Apple silicon and Intel companions. Retained the Android application ID so an update can preserve saves. |

The MR footage in the [three-minute walkthrough](../Movie/Yonder%20-%20Three%20Minute%20Walkthrough.mp4) is from the simulator. The TV and desktop footage uses the running game clients. Native macOS execution, an open-microphone wake on the worn Quest, and lighting and pointing accuracy with two people at a physical TV still need an in-room take. The [filming guide](../Movie/Yonder%20Filming%20Guide.md) covers those shots.

The garden's star wallet and upgrades live on the headset. The desktop marketplace has a separate account balance. Exported modules use the `.yonder` extension. The importer also accepts earlier `.bambi` files.
