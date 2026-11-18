# November 2 reel exit and catch verification

The November 2 evening recordings reproduce two failures in the local v1.0.30
build: an active tinted reel was treated as finished, and a visible Clownfish
catch was left uncounted while scene recovery entered a 60-second cooldown.

## Corrections

- Recognize the ordinary progress meter from its paired edges when bright sand
  joins its white fill into a misleading color component. This fallback is
  limited to the default/automatic/Ruinous paths; other explicitly selected rod
  families retain their existing evidence requirements.
- Prefer independently measured control geometry for the changing palette and
  reject Ruinous color components outside the control's expected vertical band.
  A low-confidence body can remain absent while current fish and meter evidence
  preserve the reel. The 450ms missing-evidence timeout was not simply extended.
- Add a narrowly cropped player-catch prefix from the actual Clownfish frame,
  keeping the 0.78 match threshold. Separate recorded frames validate the match;
  active reels and existing companion/scenery negatives remain negative tests.
- Let the bounded PostCatch verification window run before scene-only recovery.
  Focus/capture validation, stop handling, and recovery deadlines remain active.
- Restore visible-hotbar requirements before rod keys and recovery admission.
  A rendered window alone is not evidence of returned gameplay. Restore the
  verified unequip/re-equip sequence when recovering a stuck fishing cycle.

Other pre-existing controller, recording, settings, and test changes were
preserved. A malformed CatchFailureTests edit was repaired; its useful velocity
test was retained and the original file is saved under
`artifacts/nov02-regression/before/`.

## Regression evidence

`FischMacroCS.Tests/November02IncidentTests.cs` adds six test cases. Required
recording fixtures carry original timestamps, crop coordinates, viewport sizes,
source session identifiers, and SHA256 hashes in `Fixtures/nov02/sequence.json`.
The catalog and provenance document identify their reviewed expectations.

The worker follows recorded time independently of its own state. It must keep
the active reel, count the Clownfish exactly once, resume Casting, and release
inputs on stop. Catch timelines run at 20ms and 70ms capture costs. The caption
test excludes the template-source frame and requires other consecutive recorded
frames to match. Sparse image holds are synthetic and are not closed-loop game
physics or live AFK acceptance.

A separate worker test supplies a controlled scene change and delayed real
caption to exercise the verification/recovery timing boundary. Removing only
the new scene guard makes it fail with zero catches. The ordinary recording
replays alone still passed that mutation after the vision fixes, which is why
the additional timing test was necessary.

Evidence retained locally:

- `artifacts/nov02-regression/rebuilt-red/rebuilt-red.trx`: five recording cases
  failed against a nonincremental rebuild of the original source.
- `artifacts/nov02-regression/boundary-red/boundary-red.trx`: the timing test
  failed with only the scene guard removed.
- `artifacts/nov02-regression/focused-green/focused-green.trx`: 28 passed,
  including all six new cases, scenery negatives, and representative loading,
  reconnect, stuck-cast, missing-needle, and menu-overlay cases.
- `artifacts/nov02-regression/full/full.trx`: initial broad run, 893 passed,
  22 failed, one skipped. Twenty-one failures exposed pre-existing recovery
  regressions; one Trident negative exposed an overly broad first version of
  the meter fallback. Both paths were corrected before final packaging.

## Why passing tests were insufficient

Existing tests covered useful components but did not include these recorded
palette/background combinations. Some worker fixtures displayed the catch only
after the worker selected PostCatch, so they could not reliably expose an early
reel exit. Immediate catch images also missed the scene-recovery timing boundary.
Several recovery regressions were already detectable by existing tests.

The local `scripts/Publish-Portable.ps1` previously compiled and replaced the
portable app without running tests. It now performs a nonincremental build and
the complete native regression suite before packaging or replacing anything;
failed tests leave the previous executable untouched. GitHub CI also runs the
test project on pushes and pull requests, but cannot validate uncommitted local
changes. No evidence establishes which checks the previous editing session ran.

## Final validation and local build

The complete serial Windows suite passed: **916 passed, zero failed, one
skipped (917 total)**. The skip is the existing private screenshot case
`RodDetectorTests.Test_UserScreenshot_Slot8AndSlot9_CorrectEquippedState`;
all six new incident cases ran and passed. The preceding nonincremental build
completed with zero warnings and zero errors.

- Test report: `artifacts/portable-validation-20261103-030347-711/portable-validation.trx`.
- Replaced executable: `publish-singlefile/FischMacroCS.exe`.
- Product version: `1.0.30-nov02fix+4914a79d1b3715aab323f5859ba098d0265ccae2`.
- Executable SHA256: `D194BC72647681749D84083ECFCA89ABF19C4348BAA2E59C630BCF5B1FD922DC`.
- Previous build: `publish-rollback-20261103-030347-711/FischMacroCS.exe`.
- Validation receipt: `publish-singlefile/build-validation.json`.

All 111 production source/asset fingerprints remained unchanged through the
test and packaging run. This validates the recorded failures and automated
worker scenarios; live gameplay with the replacement executable has not yet
been verified. No GitHub release was created.

## November 3 review follow-up

The worker now snapshots the preceding bar velocity before replacing its motion
history. Hold recovery receives two distinct observation samples, so it can
recognize rightward acceleration while the bar is still moving left.
`WorkerDoesNotRetryARespondingHoldWhenLeftwardMotionSlows` translates reviewed
reel pixels through the real capture, vision, and worker path. It verifies the
measured velocity crosses from below -40 px/s into [-40, 15] while accelerating
rightward, without a redundant mouse-down, and confirms Stop releases inputs.
This is a synthetic motion scenario, not a new recorded gameplay attempt.

Incomplete recordings now participate in the same seven-day, recording-count,
and byte-budget policy as completed sessions. A missing completion summary no
longer causes unconditional deletion after 15 minutes. Pending writers remain
excluded; preserve markers exempt age-only expiry, as for completed recordings.

The restart regression first failed because the old code deleted its two-hour-old
crash evidence. Additional tests verify age/count/byte eviction, preservation of
marked evidence, archive budget separation, and active/draining writer safety.
The original test that required premature orphan deletion was corrected.

- `artifacts/review-fixes/red/review-red.trx`: restart and retention assertions
  fail against the old code. Its initial worker scenario failed a velocity setup
  assertion and is not used as proof of the worker bug.
- `artifacts/review-fixes/worker-red/worker-red.trx`: refined worker scenario
  reaches the required velocities and fails specifically because the worker
  reasserts a responding hold.
- `artifacts/review-fixes/green/review-green.trx`: 36 focused tests pass with
  both fixes, including the refined worker and restart regressions.
- `artifacts/review-fixes/retention-safety/retention-safety.trx`: all three
  marked-evidence and active/draining writer cases pass.

Final November 3 validation: **924 passed, zero failed, one existing private
screenshot skip (925 total)** in 12 minutes 2 seconds. All eight added cases and
the corrected retention case passed. The clean build had zero warnings/errors.

- Full report: `artifacts/portable-validation-20261104-042629-659/portable-validation.trx`.
- Rebuilt executable: `publish-singlefile/FischMacroCS.exe`.
- Product version: `1.0.30-nov03fix+4914a79d1b3715aab323f5859ba098d0265ccae2`.
- Executable SHA256: `7A7C7C05BA830A71EEC7A56523FA17F132ADDBE8131A9E7B5407D9EC29829181`.
- Previous build preserved: `publish-rollback-20261104-042629-659/FischMacroCS.exe`.
- Receipt: `publish-singlefile/build-validation.json`.

All 403 fingerprinted source, test, fixture, and build files remained unchanged
during validation and packaging. The rebuilt app has not been tested in live
gameplay. The November 2 receipt remains with its executable in the rollback
directory; the receipt at the normal executable path describes this newer build.

## November 4 review follow-up

Three additional review findings are addressed:

1. Fast leftward motion has a 750ms grace period instead of suppressing hold
   retries indefinitely. Verified rightward acceleration still renews the
   responding-input protection. Tests at 20ms and 100ms sample intervals require
   a retry by 800ms during continuous leftward acceleration, at least one second
   between retries, and no more than two retries.
2. Writers hold a named operating-system mutex before creating their recording
   directory and until final writes/disposal finish. Retention must acquire the
   same per-session mutex before deleting any files. Real subprocess tests cover
   active and draining writers and abrupt process termination; expired evidence
   becomes reclaimable after ownership ends. A separate two-recorder test also
   verifies retention skips the live writer and continues pruning completed data.
3. Recording admission, retention, and diagnostic export share session/report
   storage accounting. Raw archived incidents have their independent cap; report
   exports in either export directory count toward the report budget. Tests
   reproduce 600MiB of sessions plus 450MiB of archived incidents, verify small
   session and incident reports succeed, and verify report/raw-archive limits
   remain enforced. Unrelated sibling folders are excluded for standalone exports.

Evidence:

- `artifacts/nov04-fixes/red/red.trx`: five failing cases reproduce all three
  findings before changes (two dropped-hold cadences, another recorder deleting
  a live manifest, and two report types blocked by incompatible budget checks).
- `artifacts/nov04-fixes/process-red/process-red.trx`: all three subprocess cases
  fail against the old retention code because the active manifest is deleted.
- `artifacts/nov04-fixes/green/green.trx`: the first focused run caught an overly
  broad budget implementation that charged unrelated folders; corrected before
  final validation.
- `artifacts/nov04-fixes/focused-green/focused-green.trx`: all 71 focused cases pass.
- `artifacts/nov04-fixes/previous-wiring-mutation/previous-wiring-mutation.trx`:
  the earlier worker regression still fails when only its velocity-history wiring
  bug is reintroduced. The correct source was restored before the full build.

The subprocess host lives inside the test project and is excluded from the app
and portable package. These tests use generated storage and synthetic motion;
they do not claim live gameplay validation.

Final November 4 validation: **934 passed, zero failed, one existing private
screenshot skip (935 total)** in 13 minutes 35 seconds. All ten added cases pass.
The clean Release build had zero warnings/errors. An initial run was deliberately
aborted when the new helper's missing solution entry selected Debug; after adding
the helper to the solution, the final clean run built it as Release and verified
identical application hashes in the main test host and its subprocess helper.

- Full report: `artifacts/portable-validation-20261104-140920-200/portable-validation.trx`.
- Product version: `1.0.30-nov04fix+4914a79d1b3715aab323f5859ba098d0265ccae2`.
- Executable SHA256: `9A9DAA91FE615E9BF90B1B09382D4DD4D85BD929ACAFD89878EB862736E35F2F`.
- Installed executable: `publish-singlefile/FischMacroCS.exe`.
- Receipt: `publish-singlefile/build-validation.json`.
- Previous build: `publish-rollback-20261104-140920-200/FischMacroCS.exe`.

All 409 fingerprinted source, test, fixture, and build files remained unchanged.
After the app closed, the default folder was replaced and the installed binary's
SHA256 was verified against the tested package. The previous executable's hash
was also verified in the rollback folder. Live gameplay remains unverified.

## November 4 evening gameplay regressions

Recorded aquarium frames showed a Claim outline enclosing same-colored letters.
External-only contour retrieval hid the letters; connected-component hierarchy
now retains foreground lettering inside outlines. Tests check both original
frames, reject an outline with its lettering removed, and run the workflow with
successful and failed balance clearing. The original detector fails the three
initial checks in `artifacts/aquarium-nov04-red/red.trx`.

Stationary-fish footage showed growing bar oscillation. The fresh-frame and
between-frame paths used different centering laws. Both now use the same
actuation plan, with proportional/velocity gains 1/4 and the existing stopping
envelope. Six additional closed-loop cases extend the existing input-feedback
plant with a wide bar, stronger acceleration, and 60ms delayed input response
at 10/20/30/40ms capture intervals. These retain the original mean/max/error-time
thresholds. The original code fails the new baseline case in
`artifacts/reel-wobble-red/red.trx`; the intermediate high-gain correction fails
the delayed case in `artifacts/reel-wobble-lag/lag.trx`. Delayed physics are an
explicit synthetic stress case informed by footage, not measured rod constants
or proof of perfect live catches.

The missed Shake recording showed companion reward text misclassified as a
reel, which diverted the worker away from Shake detection. An independently
captured companion prefix now rejects that false reel when no progress meter
is visible. Tests replay two untouched full frames and require the actual
worker to stay in Luring and click the recorded button. The old detector fails
both frame cases in `artifacts/shake-companion-red/red.trx`. Ordinary reel
frames without lettering bypass template matching to preserve worker latency.

Full validation also exposed a subprocess test-helper handshake race: existence
of its ready file did not mean the writer had closed it. The helper now writes
a temporary file and renames it after closing; production retention assertions
are unchanged. Validation reports are retained under `artifacts`, and the final
installed build receipt records the full passing report and executable hash.

Final evening validation: **952 passed, zero failed, one existing private
screenshot skip (953 total)** in 12 minutes 54 seconds. The clean Release build
had zero warnings/errors; all 421 fingerprinted inputs remained unchanged.

- Report: `artifacts/portable-validation-20261105-041816-985/portable-validation.trx`.
- Installed version: `1.0.30-nov04stability+4914a79d1b3715aab323f5859ba098d0265ccae2`.
- Executable: `publish-singlefile/FischMacroCS.exe`.
- SHA256: `60358AC0AB29E9E33C1BDFA335F5128009447307FF38BF7FB995118115D86F03`.
- Receipt: `publish-singlefile/build-validation.json`.
- Rollback: `publish-rollback-20261105-041816-985/FischMacroCS.exe`.

After the user closed the app, the default executable was replaced and both
the installed and rollback hashes were verified. Live gameplay is unverified.

## November 4 first-cast failure

The 23:00 Chicago recording of `nov04stability` entered Reeling before sending
its first cast. The structural progress-meter fallback accepted paired edges
from idle rod-stat lettering; an unrelated vertical water edge then satisfied
the generic fish-marker check. The previous suite lacked this idle scene, so
its passing result did not establish correct first-cast behavior on this view.

Structural meter detection now requires continuous horizontal edge runs before
Hough gap bridging. One-pixel vertical tolerance preserves real anti-aliased
meter borders without bridging horizontal gaps between letters. The correction
is to general edge evidence, not an exclusion for a specific screenshot or text.

Three untouched full-frame regressions reject the false meter in both context
and track detection. Two worker regressions use the original equipped hotbar
and require an actual 721ms hold/release followed by Luring with dynamic release
enabled and disabled. All five fail against the installed code in
`artifacts/first-cast-nov04/red/red.trx`.

The first strict-edge candidate rejected one real Ruinous Oath frame; the
existing rod regression caught that before packaging. With vertical pixel
tolerance, all 99 focused cases pass in
`artifacts/first-cast-nov04/focused-tolerant/focused.trx`. An additional audit of
all 192 saved full-context/ordinary-track frames from the failed session found
zero false reels (`artifacts/first-cast-nov04/idle-session-audit.json`). These
are recorded and synthetic-clock tests, not live gameplay acceptance.

The first full gate reported a catch-transition wall-clock timeout and a
delayed-centering assertion failure (955 passed, two failed, one existing skip).
Investigation found the worker paced its loop from a real diagnostic stopwatch
while the feedback tests advanced capture and input using an injected clock.
Native rendering cost therefore changed the simulation cadence. The worker now
paces from the same monotonic clock as its other control calculations. A
deliberately slow-rendering regression proves the old worker advances only 1ms
instead of its expected 10ms simulated interval in
`artifacts/first-cast-nov04/clock-red/clock.trx`. Existing centering thresholds
and replay timeouts remain unchanged. Ordinary frames also reuse the absence
of lettering from the player-caption check to skip the redundant companion
scan. These changes require a fresh full gate before packaging.

Final first-cast candidate gate: **958 passed, zero failed, one existing private
screenshot skip (959 total)** in 13 minutes 27 seconds. Clean build: zero
warnings/errors. All 426 fingerprinted inputs remained unchanged. Report:
`artifacts/portable-validation-20261105-052411-546/portable-validation.trx`.
Candidate: `publish-1.0.30-nov04cast/FischMacroCS.exe`; receipt alongside it.
The default executable remains `nov04stability` because it is running overnight.
The candidate has not been launched or accepted in live gameplay.

The user explicitly requires actual gameplay acceptance. An overnight heartbeat
checks recordings every 30 minutes through the following morning. Initial
monitoring found the installed build's counter stuck at three while unknown
outcomes accumulated. Saved frame 8124 shows a real Big Mastered Sawfish catch,
and the in-game chat calls it perfect; therefore the counter alone was misleading.
The candidate recognizes this saved success and rejects a live reel in that
frame, but this remains replay evidence. Notes and preserved frames are in
`artifacts/overnight-nov04/`. Desktop control failed to initialize with
`failed to write kernel assets ... os error 3`, so monitoring uses the recorder.
