# November 5 recovery failures

## In-game reeling stalls

The user redirected work to reeling stalls and asked that the running AFK instance
remain open. Do not close or replace that instance without their go-ahead. The
new candidate is staged separately; idle-disconnect testing was cancelled.

During read-only monitoring of the original installed build, session
`session_20261106_023822_098_e79c439fed794eb68c646b9f137d040a` entered a genuine reel
at frame 362. At frame 687 the hard 35-second deadline classified it Unknown and
started recovery, despite an active progress meter and visible `-80% Progress
Speed`. Frame 690 reacquired that same reel. Frame 982 later shows a real
58,702.2kg Megalodon catch. This was an in-game timeout, not a disconnection.

Unmodified full frames are in `Fixtures/reel-stall-nov05`, with source timestamps
and SHA256 in `provenance.json`. They were preserved in
`artifacts/overnight-fixes/live-20261105-203945`; its events journal contains the
original timeout and recovery. Later catch-banner recognition is a separate issue;
the original run's Unknown counter does not establish a lost fish.

The fix measures solid progress-fill width and renews the stall deadline only on
two observations confirming a new high-water mark. Bar/needle movement, frozen
footage, a single fill spike and revisiting an old high cannot renew it. Both the
workflow watchdog and scene-change guard respect verified reel progress. A reel
without advancement still reaches the configured recovery deadline.
Progress grace is cleared on leaving Reeling so it cannot delay recovery on a
subsequent cast. A worker regression combines recorded reel pixels and the recorded
unavailable screen with controlled, independently validated before/after landmark
textures outside the reel/hotbar. Omitting the engine's reel-exit reset must make
this regression fail; this is checked separately by mutation.

The real-worker replay preserves recorded frame order/times through 52 seconds,
beyond both original deadlines, then supplies the separately reviewed catch and
idle frames as a controlled result. It requires one counted catch, an actual next
cast, zero Unknowns and zero recovery attempts. A second case freezes the same
recorded frame and requires bounded recovery. Policy cases cover invalid/missing
meters, single spikes, oscillation, resets and scene changes. These are replay
checks, not live validation of the uninstalled candidate.

## Error-279 reconnect failure

The installed `1.0.30-nov05overnight` build remained in recovery for hours after
Roblox displayed **Connection Failed / Retry**, error 279. The recorder continued
capturing fresh frames; fishing did not resume. `DisconnectDetector` only knew the
older **Disconnected / Reconnect** idle-278 dialog, so the actual screen became
generic Loading and never reached the reconnect action.

The existing detector regressions exercised only idle-278 and synthetic scales of
that one dialog. Policy tests supplied the `Reconnect` enum directly, which bypassed
the perception failure. Those tests did not establish support for error 279.

## Recorded evidence

`Fixtures/disconnect-nov05` contains unchanged full frames from session
`session_20261105_185004_659_11027b9b414142a7a5fe1f3da301fc32`, viewport 3440x1369:

* `error279-initial.png`: frame 24840, preserved in
  `artifacts/overnight-fixes/live-20261105-134926`.
* `error279-heldout.png`: frame 99040, independently retained later in the same
  outage, `artifacts/overnight-fixes/live-20261105-170918`.
* `roblox-home.png`: frame 135450, preserved in
  `artifacts/overnight-fixes/live-20261105-185028`, immediately before queued stop.

These are the same PC/session, not independent real-resolution validation. Full
frames may contain player names; inspect before sharing. SHA256 and source paths
are recorded in `Fixtures/disconnect-nov05/provenance.json`.

The new embedded templates retain only title `(1638,572,164,28)` and Retry label
`(1767,762,94,24)` from the initial frame. The held-out frame supplies no templates.
Recognition requires both labels in the reviewed relative geometry. Existing
confidence thresholds and the idle-278 path remain in place.

## Regression coverage

`DisconnectRecordingTests` requires the real recovery ROI to recognize both
unmodified error-279 frames and click inside the visible Retry button. In-memory
720/1080/1440 variants test scaling, not live captures at those resolutions.
Removing the title, removing Retry, substituting Cancel, or moving Retry outside
the dialog must prevent recovery input. The recorded Home screen is negative.

`RecordedError279WorkerRetriesThenContinuesAndActuallyFishes` runs the real worker,
capture ROI selection, detector, button snap, guarded click and recovery policy.
Gameplay cannot appear until Retry and Continue are clicked correctly, except in
the explicit stale-dialog case. It checks ignored-click retry pacing, focus loss,
stop cancellation, no clicks on blank loading, real reel/catch recognition, one
counted catch, an actual subsequent cast, and released input.

The worker sequence combines the saved disconnect with the existing reviewed
Continue fixture and overnight reel/catch frames. Its timing/order are controlled
test inputs; it is not claimed to be one continuous live recording.

## Failure proof and acceptance

Before the fix, all five positive recorded detector cases failed. Four worker
cases stalled without Retry; the stale-dialog negative passed. Reports are in
`artifacts/reconnect-nov05/before`. Live rejoining and resumed fishing must be
verified separately on the rebuilt executable; passing replay is not acceptance.

## Current validation and scope audit

The final clean build passed with zero warnings/errors. The full suite passed
1,001 tests, failed zero, with one existing private-screenshot skip (1,002 total),
in14m54s. Report: `artifacts/reconnect-nov05/full-20261106-031859/full.trx`.
All470 source/fixture input fingerprints remained unchanged through the gate.
An earlier full run failed only the unchanged25ms native pointed-vision timing
gate at26.20ms; its isolated recheck and the final full run passed. The threshold
was not loosened or skipped. Earlier failing/interrupted reports remain retained.

The build is `1.0.30-nov05reelrecovery`, installed with explicit user approval at
`publish-singlefile/FischMacroCS.exe`, SHA256
`0DF14A7D7E1720761690A6B733BF55DF4C09143CE87580DE82E28AC6CD0D33D8`.
Installation was at21:44 Chicago on November5, after observing the old macro
stopped. Its folder is preserved at
`publish-rollback-reelrecovery-20261105-214425`. Roblox PID7024 remained connected;
the new macro PID17976 records session
`session_20261106_034459_822_825ae13461e24a288de5b324ef000443`.
Live verification recorded six confirmed catches, including a43.005-second
`-80% Progress Speed` Megalodon reel (entry815, exit1226), without the old35-second
timeout. Frame1171 shows advancing fill at36.56seconds; frame1238 shows the player's
52,583.1kg Megalodon banner. Outcome1229 increments the catch counter5to6.
Evidence is preserved under `artifacts/overnight-fixes/live-20261105-214623` and
`live-20261105-214733`; first-session logs/completion are copied to
`artifacts/reconnect-nov05/live-acceptance`.

Acceptance is limited: frames during that reel show the character submerged beside the
boat. Subsequent cast inputs1241/1248 produced no verified cast meter; repeated
lure timeouts reached cooldown. Cause of displacement is unproven. Macro key logs
contain only rod-slot1, which does not exclude physics or external input. This
does not establish automatic position recovery or uninterrupted unattended fishing.
The macro was stopped between catches (not closed); a manual click on the visible
Sit prompt restored the boat seat. The same installed process then resumed real
fishing in session `session_20261106_035241_606_9a63011583724d5c930d1aba7c658b30`:
confirmed catch110 increments6to7, followed by a verified cast131 and live reel137.
The bounded resumed sample then confirms catches8and9 and subsequent casts249/373;
frame352 visibly confirms the player's Tidal Sailfish banner. The macro remains
running in the boat seat. Final assessment and image hashes are in
`artifacts/reconnect-nov05/live-acceptance/assessment.json`.
The manual seating step must not be described as automatic recovery. No live
disconnect was induced, and no live reel exceeded50seconds in this observation.

The scope audit compares this repair against saved input hashes for the installed
`1.0.30-nov05overnight` build, rather than the accumulated repository diff. The
incremental production patch adds110 lines and removes19 across seven existing
files plus one new32-line helper. Reconstructed baseline source bytes match all
seven installed-build SHA256 fingerprints. Review
`artifacts/reconnect-nov05/incremental-production.patch` and `scope-audit.json`.
