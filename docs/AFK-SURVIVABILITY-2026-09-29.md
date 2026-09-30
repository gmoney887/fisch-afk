# AFK survivability repair — September 29, 2026

## Changes

- Recovery suspends the unused camera-guard fishing allowance while gameplay is unavailable. Reacquisition resumes the remaining allowance; only a confirmed catch renews it.
- Gameplay reacquisition is bounded to five minutes. A separate ten-minute confirmed-catch deadline spans repeated recovery waits, casts, and UI flicker. Both terminate with an explicit failure reason instead of silently idling indefinitely. These are failure containment, not automatic navigation.
- Aquarium cleanup gets six attempts, five seconds apart, before requiring attention. It never authorizes casting behind an unverified panel. Successful closure and a new Start reset the attempt budget.
- Rod reset tolerates missing hotbar frames within its existing one-second observation window. It still requires two consecutive visible deselected frames and sends only one unequip toggle.
- Recorder queue reserves capacity for incidents independently of routine/frame admission. FIFO ordering is retained so incident promotion includes earlier frames. Routine work is limited at 24 pending entries; total capacity is 1,024 entries and queued images remain limited to 48 MiB. Critical overload explicitly ends recording with an error rather than silently dropping the trigger.
- Recorder write failures terminate acceptance of new evidence and expose LastError. Recovery captures include a full-client snapshot every 30 seconds when focused and recording.
- Remaining native GaussianBlur calls in scene, disconnect, Continue, and death detection use managed 3x3 smoothing. Existing fixture tests check detector behavior across display heights.

- Packaging now explicitly excludes workflow PNGs from the executable bundle so they remain available at the runtime Assets/Workflows path. All 22 published PNG hashes match source.

## Remaining prerequisites

Automatic process relaunch and returning to a fishing location after respawn are not implemented. A user-selected join destination and verified return route are needed; arbitrary movement would not establish successful fishing. Hardware heartbeat delivery is logged, but Roblox accepting it for more than 20 minutes still requires a live validation run. Passing replay tests does not establish overnight reliability.

## Validation

Focused detector, recording, rod-reset, retry, and recovery-allowance tests: 61 passed.

The full Release suite ran 574 tests: 571 passed, one skipped, and two idle scenarios failed because their old assertions required fishing to resume after a 26-minute outage. Updated assertions verify the five-minute terminal failure, no invented catch, and released inputs; both passed in a targeted rerun. Two additional tests (permanently stuck aquarium and critical recorder overload) also passed. Combined final coverage: 575 passed, one skipped. Logs: artifacts/afk-fix-full.log, artifacts/afk-fix-idle.log, artifacts/afk-fix-aquarium.log, artifacts/afk-fix-overload.log.

Optimized, self-contained Windows x64 candidate: artifacts/afk-fix-20260929/FischMacroCS.exe, informational version 1.0.20-afkfix.2+cafa0abfb46098f5c9a7b93dc065b9f0100d940a. Built locally; not published or launched into a live AFK run.
