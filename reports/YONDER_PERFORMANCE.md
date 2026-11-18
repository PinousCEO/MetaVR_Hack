# Yonder performance audit

Measured on 18 November 2026 with a Core i5-12400F and GeForce RTX 5060 on Windows. The Unity MR simulator runs at a 60 FPS limit. The comparison uses the same executable settings, local script, resolution and fresh save per run. The baseline was commit `c25fb96` with the frame sampler added.

## MR simulator at 1280 × 720

| Scene | Average FPS, before → after | 95th percentile frame, ms | Longest frame, ms | Generation 0 collections |
|---|---:|---:|---:|---:|
| Idle desk | 59.99 → 59.99 | 16.72 → 16.75 | 16.96 → 16.98 | 0 → 0 |
| Open menu | 60.00 → 59.99 | 16.76 → 16.76 | 16.87 → 16.95 | 0 → 0 |
| First garden open | 59.92 → 59.99 | 16.76 → 16.76 | 35.26 → 25.06 | 2 → 0 |
| Three growing plots | 59.99 → 59.95 | 16.75 → 16.75 | 16.98 → 22.99 | 0 → 0 |
| Demon wave and first upgrades | 52.22 → 59.95 | 17.12 → 16.79 | 1588.30 → 24.58 | 1 → 0 |
| Ripe fruit | 59.99 → 59.99 | 16.74 → 16.73 | 16.98 → 17.00 | 0 → 0 |

A second run of the released player measured 22.60 ms for the longest frame at first garden open and 17.20 ms in the demon wave. Neither sample triggered a generation 0 collection.

The garden now loads its five growth meshes and star fruit from the Unity build. All three plots share them. The baseline demon run showed Korean upgrade names and Unity font warnings as the first cards appeared. The release shows English names using the bundled font. The character also skips redundant renderer updates once it has fully crossed a display.

## Three minutes with the recorder running

Windows Graphics Capture recorded the isolated simulator window at 1280 × 720 and 30 FPS with the NVIDIA H.264 encoder. The capture used a fixed simulator camera and recorded only the game window. During the measured interval the scenario opened the menu and garden, tended three plots, harvested fruit, bought Rain Bell and Sun Dial, and ran two demon sessions with upgrades.

| Wall time | Game frames | Average FPS | P95 | P99 | Longest | Frames over 25 ms |
|---:|---:|---:|---:|---:|---:|---:|
| 180.002 s | 10,797 | 59.98 | 16.78 ms | 16.99 ms | 38.82 ms | 3 |

## Desktop games and TV at 1920 × 1080

Ten-second samples used Chromium with hardware D3D11 rendering. The TV test keeps the ball in a fixed position and runs the normal animation loop. The separate protocol test exercises actual serves, returns and saves. Static court markings now stay on their own canvas between layout changes. Racket, ball and character animation still update each frame.

| TV view | Average FPS, before → after | P99, ms | Canvas strokes per frame, before → after | Mean JS render callback, ms |
|---|---:|---:|---:|---:|
| Keeper | 60.00 → 60.00 | 16.90 | 28 → 0 | 0.165 → 0.124 |
| Tennis | 60.00 → 60.00 | 17.60 | 55 → 2 | 0.176 → 0.147 |

With the Android compiler also active, a separate TV run measured 60.00 FPS for goalkeeper and 58.90 FPS for tennis.

Standalone Demon Gate ran with the published Windows companion, live simulation messages, movement, firing, pause, resume and an upgrade selection. It averaged 60.00 FPS across 1,587 frame intervals, with P95 16.80 ms and P99 16.90 ms. Interpolation now indexes previous poses once per simulation update and reuses draw rows.

## Functional and device coverage

- All 2,070 .NET Release tests passed in the isolated run. When Android compilation was running simultaneously, the wall-time assertion for the script benchmark failed. The isolated rerun used the same threshold.
- The companion protocol covered pairing, screen calibration, entry and return, a goalkeeper save, tennis, game selection, disconnect and an English wake audio clip. The TV suite exercised all six meshes, three upgrades and loss and recovery of the WebGL context. A recorded pointing-hand image sent over an authenticated test connection placed exactly one white Gomoku stone.
- Unity built the Windows player and Quest APK. The APK was staged over USB with a matching SHA-256. The attached Quest 3 was asleep with the previous version installed. Worn-headset application FPS, compositor drops, thermal throttling during recording, live pointing with two people, and frame pacing on native Apple silicon or Intel Mac hardware remain unmeasured. At 72 Hz the application has 13.89 ms per frame. [Meta performance guidance](https://developers.meta.com/vr/resources/vrc-quest-performance-1/)

## Repeating the measurements

On Windows, run `tools/performance/Measure-MrFrames.ps1` with the built player. It starts a private local simulator, uses a fresh save, prints all six scene samples and leaves its log path. The [recording sequence](../tools/performance/mr-recording.txt) can be passed as the simulator's `-lp-local-script` while recording that simulator window.

For the TV test, build `Companion/LivingPartner.Mac` in Release, then run `node Companion/LivingPartner.Mac/test/webgl.mjs` with `YONDER_FRAME_AUDIT=1`. For a paired, worn Quest, `lpctl debug frameSample 30 72 0 quest-wave` samples 30 seconds of application frame intervals. Compare those with the headset's compositor, GPU, CPU and refresh-rate metrics during an actual headset recording.
