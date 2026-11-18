# Demo scenario — under 3 minutes

Target length 2:50. One continuous take for the portal section (0:20–1:05), unedited, so that the crossing can be trusted. Shoot through the lens or cast the headset view with the real monitor in frame. Before recording: pair, calibrate the monitor(s), run `tools\Setup-Sample.ps1 -Open`, and do the on-device checks in `NEXT_STEPS.md` §1 — the headset experience has not been verified yet.

| Time | Shot | What happens | Line / caption |
|---|---|---|---|
| 0:00–0:10 | Desk, passthrough | Momo idles on the desk and looks up at the user | "This is Momo. It lives on my desk — and in my PC." |
| 0:10–0:20 | Hand enters | Palm held out; Momo walks over, steps onto the fingers, then the palm; the hand moves and it balances | (no narration) |
| 0:20–0:32 | Monitor + hand | User points at the sample site on the monitor, pinch; small ring appears on the real screen; pinch again | "Go in here and look at my page." |
| 0:32–0:50 | **Continuous** | Momo looks at the user, at the spot, hops off the hand, runs across the desk, jumps and passes through the glass: head and arms on the monitor, legs still in the room | Caption: "One body. Headset renders the part outside, the PC renders the part inside." |
| 0:50–1:05 | Monitor close-up | Momo lands inside the browser window, turns to face out; drag the window — it rides along; minimise and restore — it disappears and returns; status dot shows "Analyzing" | "It's a real desktop app. It never blocks my mouse." |
| 1:05–1:25 | **Continuous** | Analysis finishes; point at another spot, pinch twice; Momo comes back out head first and lands on the desk | "…and it comes back out wherever I point." |
| 1:25–1:50 | Desk | Proposal cards unfold in the air: role, evidence (file:line), proposal, confirmed / inferred | "It read the screen and the code. Each card says what it actually verified." |
| 1:50–2:15 | Hand pokes a card | Momo "works"; card status: editing → testing → passed; panel shows changed file, branch, test output; poke **Approve** | "The change is made on its own branch, tested, and only pushed when I approve." |
| 2:15–2:30 | Desk | Hand menu → Timer; a timer panel appears and counts; poke pause / resume | "Make a timer, 25 minutes." |
| 2:30–2:45 | Desk | Hand menu → Rain; Momo shivers; a hand beside it changes nothing; the hand above its head shelters it; it thanks the user | "It only feels sheltered when my hand really blocks the rain." |
| 2:45–2:50 | Wide | Momo waves from the desk | "Living AI Partner." |

## Notes for honest recording

- Keep the portal shots uncut and at normal speed. A separate slow-motion pass (`lpctl slow 0.25`) and a frozen half-through frame (`lpctl freeze 0.5`) can follow as inserts, labelled as slow motion.
- Do not present the desktop simulator footage (`artifacts/portal-roundtrip-final-build.mp4`, and the older `portal-roundtrip-unedited.mp4`) as headset footage. It shows the simulator's MR view above and the real PC window below. `tools\Record-RoundTrip.ps1` records such a clip in one take.
- If analysis is shown, it is a real Claude Code run on the sample project (about 40 s for analysis, about 20 s for the change in the session's run; with the Codex adapter about 30 s and 15 s); cut the wait, not the result.
- Say that a Windows PC companion is required.
