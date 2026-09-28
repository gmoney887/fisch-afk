# Afternoon fishing failure — September 27, 2026

Evidence preserved locally under `artifacts/fishing-failure-20260927-afternoon/`: both retained sessions and current/rotated macro logs. No gameplay inputs, restart, or source changes were performed. Full screenshots contain player names; keep them local.

## Main run

Session `session_20260927_161952_927_c411d1a58039406c97a5668b80937dd3` started at 11:19:52 AM America/Chicago. It recorded 734 confirmed catches (counter 527 → 1261), one Unknown, and three recovery attempts. Recording duration was 6,453.48 seconds, including a five-second tail. The running app at inspection was `artifacts/reward-reliability-fix/FischMacroCS.exe`; manifest version 1.0.18.0 does not establish its exact source revision.

- 1:05:35 PM: last confirmed catch; begins the next cast.
- 1:05:36: cast meter not detected; waits for bite.
- 1:06:01, 1:06:27, 1:06:54: recovery attempts after 25-second lure timeouts.
- Each recovery logs `Rod is ALREADY in hand. No action taken.` Subsequent casts again have no detected meter or bite.
- 1:07:21: automatic stop: `Three recovery attempts produced no confirmed catch. Check the fishing position and restart when ready.`

This differs from the previous overnight scene-guard stop. The immediate failure is exhaustion of recovery attempts after unconfirmed casts, not an observed application crash or disconnect.

## Concrete recovery defect

Current `RecoverAndRestart` calls `EnsureRodEquipped(..., force: true)`, but `EnsureRodEquipped` never reads `force` and returns immediately when the rod appears equipped. Thus the purported forced re-equip is a no-op. The retained incident journal contains no rod-slot key edges during those recovery attempts (only the anti-idle heartbeat key). This establishes that the runs retried without the intended equipment reset; it does not prove that re-equipping would have restored fishing or explain why casting first stopped working.

The final retained full frame (`frame_00055562.png`) shows the avatar on a rocky ledge and ordinary gameplay without a reel UI. An early retained frame (`frame_00000577.png`) shows a reel UI at a similar rocky location. These do not independently establish displacement or its absence at the failure transition.

Recorder counts: 55,507 total recorded frames, 2,654 dropped entries, 504,882 routine journal entries expired, zero incident journal entries expired. Total captured frames is not retained frame count. Do not infer full chronological video coverage from these counters.

## Short restart

Session `session_20260927_185459_494_75c004ddad324b3793acedc567d05591` at 1:54:59 PM ran 9.73 seconds with zero catches and one Unknown. It initially waited for the hotbar, equipped the rod, entered a brief reel/post-catch sequence, attempted aquarium navigation without detecting the claim control, and then lost the required focus/capture state. Its recorded stop source was `Queued stop during recovery`, distinct from the earlier automatic recovery-budget stop. Do not attribute that queued stop to a specific person without further input evidence.

Next correction should verify a genuine unequip/re-equip cycle when explicitly requested, protect live reels from interruption, and exercise the recorded no-meter/lure-timeout sequence. This investigation does not establish a deployed fix.

## Local fix following the user's FIX request

`EnsureRodEquipped(force: true)` now sends an unequip key press for an already-selected rod and requires two consecutive visible deselected observations before sending the equip key. Missing hotbar, cancellation, or an unconfirmed unequip interrupts the reset. Existing equip verification and visually located click fallback remain in place. Ordinary equipped-rod checks remain no-ops. The existing live-reel entry guard remains ahead of non-reeling recovery; a timed-out reel may still be reset.

Validation: 32 targeted tests passed (RodResetGuardTests, GameplayInputTests, ReelEntryGuardTests, and worker scenarios failed-casts, missing-bites, recovery-budget, missed-meter-delayed-bite, reel-stall, slow-reel). The failed-casts replay now withholds cast readiness until two actual rod toggles occur. Worker fixtures model a visible deselected hotbar instead of assuming the rod is permanently selected. These are simulated recovery checks, not live Roblox acceptance or an overnight soak.

Self-contained build destination: `artifacts/rod-reset-fix-20260927/FischMacroCS.exe`. It includes the current working tree's pre-existing reward workflow changes. The installed/running app has not been replaced automatically.
