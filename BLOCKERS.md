# BLOCKERS — Living AI Partner (as of 2026-11-16)

Each item says what is blocked, why, what was done instead, and exactly what unblocks it.

## B1. No Quest headset connected — all on-device verification (BLOCKED_EXTERNAL)

- `adb devices` listed no device at any point of this session (Unity's SDK adb and the user's Android SDK adb were both checked; last check 04:54 KST). The PC shows "Meta Virtual Monitor" / "Oculus Virtual Audio Device" drivers only.
- Consequence: nothing was run on a headset. **Every Quest-side result is NOT_TESTED**: hand tracking, pinch/poke input, passthrough, monitor calibration by touch, MR/PC visual alignment through the real optics, perceived latency, comfort, the passthrough-camera observation, pairing over Wi-Fi.
- What exists: the Quest APK builds (`dist/LivingAIPartner-Quest/LivingAIPartner.apk`, manifest verified with aapt2: hand tracking, passthrough, headset camera, arm64, minSdk 32), and the MR logic (`MrApp`) is exercised through a desktop simulator against the real Windows client and host. Simulator results are labelled as such and are **not** evidence for R03, R07–R09 visual quality, R13, R19, R20 on device.
- To unblock: connect a Quest 3/3S in developer mode, `adb install -r dist\LivingAIPartner-Quest\LivingAIPartner.apk`, follow RUNBOOK §2–§4, then run the checklist in NEXT_STEPS §1.

**Update 2026-11-16 17:52:** a Quest 3 was connected by USB and the app was installed and run for the first time (`TEST_RESULTS.md`, "First run on a headset"). It starts, connects, tracks head and hands at 72 fps and its buttons respond. B1 is therefore no longer "no headset" but "the on-device checks of `NEXT_STEPS.md` §1 are still to be done": no monitor has been calibrated on the device yet, so the portal, pointing, voice, the review cards and tennis are still untested there, and nobody has yet described how the app looks through the headset.
**Update 2026-11-16 21:25:** a Quest 3 has been connected since 17:35 and the owner used the app on it for an evening. Passthrough, hand tracking, finding three screens with the camera, voice (name call, spoken commands), portal crossings in both directions, a screen review and the start of a tennis game all ran on the device (TEST_RESULTS, "Evening on the headset"). Still open on the device: how the cut at the glass looks, how tennis plays, the review cards in the room, the pairing numpad (the cable path skips it), Wi-Fi instead of the cable, and a measured display latency.

## B2. Official competition rules could not be read (BLOCKED_EXTERNAL)

- `https://start-developer-competition-26.devpost.com/rules` (and the overview/dates pages) returned HTTP 403 to automated fetching. The official rules were therefore **not verified**.
- Read instead (Meta's own announcements, not the rules text): hands-first experience that works end-to-end without controllers; open to Start program members only; tracks Entertainment / Gaming / Productivity; divisions New / Adapted; submissions close 2026-11-18.
- Not verified at all: whether a required PC companion is acceptable, build delivery method, video length/format, language, required SDKs, pre-existing work rules, AI-assistance disclosure, judging criteria.
- To unblock: open the rules page in a browser (signed in to Devpost) and check the list in `reports/SUBMISSION_CHECKLIST.md`.

## B3. GitHub publishing is not authenticated (BLOCKED_EXTERNAL)

- `gh auth status`: not logged in. The repository has a remote (`origin`), but nothing was pushed, and nothing may be pushed without the owner's approval anyway.
- What was verified instead: the approved-publish step against a **local bare git remote** of the sample project (real `git push` of the task branch only, default branch untouched). A push to GitHub itself is NOT_TESTED.
- To unblock: `gh auth login` (or a configured credential helper), mark a project `allowPush`, approve in the app.

## B4. Voice conversation is implemented; on the headset it is untested (was: voice input not implemented)

- Since 2026-11-16 15:40 the partner can be talked to: a pinch on it (or the hand-menu **Talk** button, or the tray's Talk item on a PC without a headset) opens the microphone for one utterance, the PC turns it into text, a reply and at most one action, and the reply is spoken (OpenAI speech-to-text, chat and text-to-speech; the key is the owner's, stored encrypted on the PC with `tools\Set-OpenAiKey.ps1`).
- Verified on the PC with the simulator as MR client, the "user's voice" being recordings fed through the microphone path (`tools\Run-VoiceChecks.ps1`, `artifacts/voice-checks.txt`). The real microphone was only opened and closed; nobody spoke into it in a check.
- NOT_TESTED: everything about it on a Quest - the microphone permission prompt, recording through the headset microphone, how reliably a real pinch on a 10 cm character is detected, spatial playback, echo of the partner's own voice into the microphone.
- Without a key the partner still works by hand and by typed commands (`lpctl utter`), in text only.

## B5. No real mocap / authored art (scope decision)

- No licensed character asset existed in the repository and none was downloaded or bought. The character is an original toon mascot (`Partner.prefab`; its meshes were generated once in the Editor from the shared skeleton definition) with procedural animation. It is the same prefab on both platforms, and deliberately simple: eyes only, two-tone shading, ink outline.
- Paid/quoted generation tools were available and deliberately not used (no budget was authorised).

## B6. Things the session could not judge

- Real optical alignment and latency (needs B1).
- Whether the user's network lets the Quest receive the UDP discovery beacon (fallback: `adb reverse`).
- Long-run stability (hours), battery/thermal behaviour on Quest.

## B7. Tennis and the review cards are untested on the headset (follows from B1)

Both were added on 2026-11-16 afternoon and verified with the desktop MR simulator only (`TEST_RESULTS.md`, "Tennis and review / reading"). For tennis the open question is the real hand: the simulator's hand is put on the ball's path by the program. For the review the open questions are the pointing hand and how the note cards read in the headset. Neither blocks the PC side: the review and file reading work from the tray and `lpctl` without a headset.


## 2026-11-17: the PC ran out of disk and memory during the night's and the morning's work

- Stopped test clients stayed behind as processes that could not be ended (58 in the end, 43 GB of committed memory);
  the page file grew to 58 GB, drive C: fell to 0.2 GB, a Quest build failed with "Win32 IO returned 112", Explorer
  crashed in the user's Documents folder. A restart of Windows cleared it; on the fresh system clients exit normally.
  WHY the machine got into that state is not known (first leftover: 19:39 on 2026-11-16). Not the microphone and not
  Direct3D 12 (both ruled out by test).
- A full Android build needs about 10 GB free on C:; `tools/Watch-BuildDisk.ps1` guards it.
