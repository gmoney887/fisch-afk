# Primary AFK regression matrix

Scope: protect the actual fishing worker against regressions, with explicit boundaries between automated behavior and live acceptance. The active goal remains open while primary gaps below are unresolved. See [FEATURE-ACCEPTANCE.md](FEATURE-ACCEPTANCE.md) for the complete product inventory and measured coverage.

## Worker scenarios

All rows below execute `FishingEngine.Start`, the coordinator, actual OpenCV detection and the production `GameplayInput` guard. Only desktop APIs, time and final hardware delivery are simulated. Required images are asserted present; these tests never silently skip. The fixture composition is controlled, not a complete chronological game recording.

| Scenario in FishingEngineReplayTests | Required result |
|---|---|
| Confirmed catch | Cast → lure → reel → PostCatch → next cast; exactly one confirmed catch |
| Unknown catch | Same cycle without a catch banner; one Unknown, no invented success or failure |
| Focus/capture interruption | Deny two activations; interrupt a recovery capture; resume only after gameplay returns |
| Stop in Casting | No subsequent gameplay input |
| Stop in Luring | No subsequent gameplay input |
| Stop in Reeling | Release owned input; no subsequent down/click |
| Stop in PostCatch | No further cycle or gameplay input |
| Stop in recovery | Cancel the persistent wait without resuming |
| server-update / server-update-stop | Repeatedly observe the required real server-update waiting screen; retain Start and send no gameplay input. Resume and finish a catch only after gameplay returns, or release input and stop when cancelled during the wait. Does not test reconnect clicking. |
| reconnect / reconnect-retry / reconnect-stop / reconnect-stale | Detect the recorded disconnect, freshly recheck identity and snap to the white Reconnect control before guarded input. Failed attempts are at least10 seconds apart; no keypresses or outcomes before gameplay returns. Complete a catch after returning gameplay, release on Stop during the click, or send no click if the dialog disappeared before revalidation. |
| continue / continue-retry / continue-stop / continue-stale | Require the recorded Fisch logo plus continue prompt, freshly recheck both, then center on its lettering and send a guarded mouse click. At least2 seconds between attempts; no equipment keys or invented outcomes while waiting. Complete a catch after gameplay returns, release on Stop during the click, or reject a vanished prompt before input. Enter-based replay previously passed but failed the real screen; this replacement still needs live acceptance. |
| reconnect-chain / reconnect-chain-focus / reconnect-chain-stop | In one worker run, require Reconnect on the recorded disconnect, blank loading, Continue on its recorded screen, another blank loading interval, and then a confirmed catch and next cycle. Gameplay cannot return merely because time elapsed: the Continue click must occur. Verify action order and coordinates, prohibit loading clicks and pre-gameplay keyboard/outcome activity, recover two denied focus activations between stages, or Stop during Continue and release all input. This is a controlled sequence of recorded crops, not a live rejoin or chronological recording. |
| resize | Reacquire width, resume visible reel without recasting |
| dpi | Reacquire DPI, resume visible reel without recasting |
| queued-restart | Finalize the catch, stop before casting, restart the same instance and complete another cycle; clear stale queued intent |
| reel-stall | Record one Unknown and one recovery, then complete another catch |
| slow-reel | Repeated 120 ms captures cannot bypass the configured reel deadline; recover and complete another catch |
| missing-start | Retain Start while no window exists; send no gameplay input; resume when it appears |
| replace-window | Release input when the old window disappears; reacquire the replacement handle and finish fishing |
| minimize | Restore the minimized window and resume fishing |
| idle-enabled | During 26 simulated minutes of unavailable gameplay, send balanced F15 pulses at the bounded cadence; afterward complete a cycle |
| idle-disabled | Same persistent wait with no heartbeat key input; afterward complete a cycle |
| navigation-shake | During the lure wait, send balanced navigation/Enter edges, then finish fishing |
| visual-shake | Repeated stationary targets receive relative hover and held-button movement before each completed click; click the detected center and finish fishing |
| visual-shake-stop | Stop during held-button movement releases input and prevents subsequent motion or clicks |
| visual-missing | No visible button means no shake click |
| visual-occluded | A button covered by the macro window receives no shake click |
| disabled-shake | A visible button receives no keyboard or mouse shake input when disabled |
| stop-shake | Stop during navigation-key hold releases that key and prevents the subsequent Enter press |
| failed-casts / missing-bites / recovery-budget (3) | Retry failed casts or missing bites; exhaust three recoveries, observe cooldown, then catch without clearing Start |
| reject-move/down/up/key-down/key-up/up-twice (6) | Recover from rejected hardware delivery, including repeated release failure, then catch with no held input |
| crates-unavailable / aquarium-unavailable (2) | Missing prerequisites produce Unknown; attempt once per session, inject no workflow input, and finish two catches |
| recording-recovery / disabled / storage-failure / stop / pressure (5) | Preserve readable frames and recovery events, match completion statistics, flush on Stop, create no files when disabled, continue fishing after writer failure or oversized-frame drop |
| crates-unconfirmed / aquarium-unconfirmed (2) | Detect prerequisite using template matching, click once, time out missing result as Unknown, skip remaining session, complete two catches |
| crates-input-error / aquarium-input-error (2) | Rejected first click leaves optional workflow disabled; core fishing recovers and completes two catches |
| crates-stop / aquarium-stop (2) | Stop during the first workflow press releases input, sends no further down/click, and leaves outcome Unknown |
| recording-reset / recording-reset-unknown (2) | Reset after first catch/Unknown clears the display; second cycle completes; recording retains both outcomes and positive duration |
| crates-overlay / aquarium-overlay (2) | Open menu, attempt second action, time out without result; no gameplay input while hotbar is hidden; resume after menu clears and complete second catch |
| compact-reel | Actual frame from false Unknown remains Reeling, completes a confirmed catch, then starts next cast with no invented Unknown |
| companion-bonus | Recorded companion catch notification remains Unknown for player catch, counts no success/failure, and worker starts the next cycle |
| maximized-catch | Separate recorded maximized player catch confirms success and starts next cycle; detector also rejects maximized companion-only rewards at three scales |
| stacked-catch | Recorded player catch at the upper track edge, above companion fish/bait rewards, confirms across frames and starts the next cycle; companion-only crop remains negative |
| ShutdownCancelsAnUnavailableWindowWaitAndJoinsTheWorker | Dispose cancels the live wait, joins the worker, balances timer setup/teardown, and prohibits restart after disposal |

Cycle scenarios also call Start twice and assert the same completion task, preventing duplicate workers. Stop assertions allow only release edges after cancellation. A bounded test timeout is failure, not successful termination. Heartbeat tests assert both enabled and disabled behavior and balanced down/up edges; they do not assert that Roblox's idle timer accepted those keys. The visual-shake positive uses the shipped template to exercise controller action selection; it is not an independent detector accuracy benchmark.

## Primary gaps still to cover

Shake-motion regression: the old native click helper retained relative hover/held-button jiggles, but the guarded engine click path bypassed them. The strengthened visual-shake worker case failed with66 expected motion events versus0; a new Stop-during-held-jiggle case also failed because that boundary was never reached. Restored guarded relative motion before and during each shake click; both focused tests now pass (`artifacts/shake-jiggle-tests/shake-before.trx`, `shake-after.trx`). Repeated stationary targets must receive the complete movement sequence each time. This restores the previous timing rather than assuming a faster unverified sequence is equivalent; live click registration still needs confirmation.

Clean-source verification before the shake correction:128 tracked/nonignored working-tree files copied outside repository ancestry, with per-file hashes in `artifacts/ci-source-verification/manifest.json`.359 passed,0 failed,4 explicit private-fixture skips in `artifacts/ci-source-verification/results/039c323125b44e14a4206f28da34050c/tests.trx`. Overall56.90% lines/48.65% branches; FishingEngine68.98%/55.17%. CI's self-contained win-x64 packaging command succeeded locally, executable SHA256 `93615F3B2687D0906AA9218AB0C0CDF771BCC29DA1D77DA34A518B503B2BEFDB`. This verifies the source snapshot, not a remote GitHub run or portable runtime acceptance. It predates the shake fix.

Prior full-suite result after the chained rejoin scenarios:362 passed,0 failed,1 existing missing-private-fixture skip (`artifacts/reconnect-chain-full-tests/reconnect-chain-full.trx`), duration2m54s. Release build has0 warnings/errors; diff check passed. The three new cases also passed independently (`artifacts/reconnect-chain-tests/reconnect-chain.trx`). Coverage percentages remain the359-test snapshot because production code did not change. The live Enter failure and manual click probe are recorded in the live-testing ledger. Green controlled replay does not establish that the replacement build recovers the real screen.

| Gap | Required additional evidence |
|---|---|


| Optional workflow recovery and success | First/second-action failures and waiting through a persistent overlay covered; automatic safe menu dismissal, final reward/close steps and real reward recognition still need coverage |
| Recording under sustained pressure | Worker recovery, Stop flush, disabled recording, storage failure and oversized-frame rejection now covered; a stalled writer with 4096 produced events now verifies bounded nonblocking admission, explicit drops, preserved completion and restart; multi-hour retention still needs evidence |
| Window/process/live focus variations | One live Roblox UWP minimize/restore case passes with subsequent catch. Native maximize trial exposed missing cursor placement on reel recovery; strengthened resize/DPI tests require move-before-press. Combined build now passes a short3440x1369/96DPI catch-recognition retest:3 confirmed/0 Unknown while maximized, independently sampled Frilled Shark banner, then5 confirmed/0 Unknown after restore; broader live validation remains; desktop/multiwindow discovery, occluders, physical hotkeys and other DPI/resolutions remain |
| Disconnect and rejoin | Reconnect action passed live; worker retries, stale identity and cancellation are covered for Reconnect and Continue. Enter failed the real Continue screen; clicking succeeded in an isolated manual probe. Replacement prompt-click workflow, post-join readiness and resumed fishing still require live acceptance. |
| Idle acceptance | >25-minute live run showing Roblox accepts the heartbeat, not just OS delivery or an incrementing counter |
| Long-run behavior | At least two hours with recorded outcomes, memory/storage/latency evidence and independently sampled catches; no silent stop or stuck state |

Do not mark primary AFK acceptance complete from this table's automated rows alone. Coverage floors protect exercised behavior; they do not substitute for missing scenarios or live evidence.

## Readiness diagnostic execution

`PreFlightExecutionTests` runs `RunDiagnosticAsync` with the production vision and
guarded input code. Only the desktop, capture source and final hardware sink are
substituted. Sixteen cases cover missing/minimized/undersized windows, cancellation
at all five steps and before the final snapshot, focus/resize/DPI changes before
the equipment probe, black/missing-hotbar failure verdicts, confirmed equipment
after a single keypress, and cancellation during that keypress with key release.
The equipment transition combines a recorded hotbar with an artificial highlight;
it is not an independent capture of a real equip action. Caller-owned capture
sources must remain reusable after diagnostic disposal.

Two cases failed before the fix: cancellation at the first progress callback still
looked up a window, and cancellation after coordinate validation still captured a
full diagnostic snapshot. Cancellation/context guards now precede both actions.
The older report-step test now obtains the actual report from the diagnostic.
Native delivery, denied activation and interrupted capture variations still need
broader coverage; this suite does not establish unattended readiness.

Shake detector regression: the recorded lower-right button and four placements at wide-viewport edges are required fixtures in ShakeDetectionTests. The old 18–66% vertical search excluded the reported button; full-width coverage now spans 10–90% height. Existing negative screenshots remain part of the full suite.



Optional first-action tests use generated, test-only templates and the production template matcher. They exercise controller sequencing and failure handling, not recognition of real Roblox menus. Rejected crate input reproduced two attempts before the fix; setting the session skip flag before execution prevents retries after thrown exceptions.


Statistics reset also has a stopped-engine regression: reset cannot start the elapsed-time clock. Recording totals use worker-owned cumulative counters rather than resettable display values; a statistics-reset event explains discontinuities in the journal.


Callback checks for missing-window and unavailable-gameplay input now collect violations and assert after worker completion, preventing recovery from swallowing a failing assertion.



Test-host isolation: a module initializer sets the process-local FischAFKPro.DataRoot before application types load. Default settings, logs and recordings are checked against a unique temporary directory, avoiding shared live-session files during regression runs.



Evidence retention follow-up: preserved frames now evict routine recovery/sampled-success evidence before Unknown/failure evidence. A failure promotes the preceding15 seconds already in the preserved pool, plus rolling pre-event frames; five seconds after the failure remain prioritized. The pool remains capped at128MiB/5000 frames and oldest failures eventually expire when failures alone fill it. Component tests flood1000 routine entries, verify preceding-frame promotion, and enforce byte/count limits including oversized evidence. Existing recorder integration tests verify files and replay; multi-hour on-disk behavior still needs live validation.

Recorded server-update follow-up: both new scenarios passed in the40-case primary worker group. Full suite316 passed,0 failed,1 existing private-fixture skip; artifacts/server-update-full-tests/server-update-full.trx. The fixture is required in CI, with provenance and hash recorded. Waiting retains Start without gameplay input; cancellation prevents subsequent input; returning verified gameplay produces a confirmed catch and next cycle. This is a recorded-screen/controlled-transition replay, not a real server restart or reconnect trial. No production fishing code changed.

Reconnect detector verification:8 new cases,340 total passed/0 failed/1 existing private-fixture skip; artifacts/disconnect-full-tests/disconnect-full.trx. Build artifacts/disconnect-detector-build has0 warnings/errors. Detection requires the captured Disconnected title and Reconnect label on its white background at the same relative layout, both at confidence0.88. Symmetric3x3 Gaussian smoothing handles resampling phase; no confidence relaxation. Required positive fixture passes synthetic720/1080/1353/1440 heights; title/button absence, Leave substitution and4 existing screen negatives reject. It is not yet invoked by FishingEngine, and no live reconnect or new coverage percentage is claimed.

Reconnect integration supersedes detector-only status:4 actual-worker scenarios pass for successful return, three attempts spaced at least10 seconds apart, Stop during press, and a disappeared dialog rejected before input. A readiness execution test permits recovery for the reviewed disconnect while keeping OverallPass=false. Full suite345 passed/1 existing skip; no coverage percentage remeasured. Live PID29328 clicked Reconnect automatically and reached Fisch's continue screen; end-to-end fishing resumption remains open.

Cast scenery follow-up: required raw cast fixtures cover three real meters and a reviewed boat/spawn-shield negative. The prior detector mistook a thin green arc plus white hull for a full meter. Requiring the green cap contour to occupy at least30% of its bounding box rejects that negative while retaining all recorded positive meters. The new negative failed before the fix; all16 cast tests pass afterward. Full suite367 passed,0 failed,1 existing private-fixture skip (`artifacts/cast-scenery-full-tests/cast-scenery-full.trx`). Release build `artifacts/cast-scenery-build` has0 warnings/errors. This build was not launched; no new coverage percentage or live cast acceptance is claimed.

Latest full suite:369 passed,0 failed,1 existing missing-private-fixture skip in3m32s (`artifacts/stacked-catch-full-tests/stacked-full.trx`). This includes shake movement/Stop regression, cast scenery rejection, and stacked-reward player catch recognition. The stacked fixture failed before removing the detector's top inset; all11 focused catch/worker cases pass afterward. Companion-only rewards still reject. Release build `artifacts/stacked-catch-build` has0 warnings/errors; DLL SHA256 `D4C068F549CDF64AE4BF7F536DAE54914B0F8E712A10BBB206F81D02724271B6`. Diff check passes. New build is not launched; PID30696 continues the earlier jiggle soak. Coverage remains the older359-test snapshot, not a percentage for this source state.

Coverage refreshed after all369 tests:57.60% lines/49.04% branches overall, FishingEngine69.29%/55.17%, PreFlightDiagnostic96.00%/89.78%, AfkRecovery100%/97.44%.369 passed,0 failed,1 existing private-fixture skip in3m33s. Source `artifacts/stacked-catch-coverage/eb186bef082d4fa2b4f71640998b96e9/tests.trx`; Cobertura SHA256 `45751B2AC65ADDBCB55A9459D63EBECE82E390360A3FCBFFC67C942A9EAD979F` independently checked against generated summary. The current coverage document and feature table now use this snapshot; prior percentages above remain historical. Floors remain unchanged. This does not establish live Continue, idle prevention, long soak acceptance or other missing desktop configurations.

Chronological vision transition: `ChronologicalCatchTests` replays nine consecutive raw frames467..475 from the corrected live session, preserving detector state and original client geometry. Directly reviewed labels distinguish four moving active reels, the exit animation and four fading-in player reward frames. Required fixtures carry timestamps and verified SHA256 hashes; missing/modified frames fail. The focused replay passes: no catch before a banner, live reel retained before exit, no live reel during player rewards, and two separate banner matches by the settled frames. This adds a genuine chronological transition but does not replace the still-open full-cycle/rod/theme/native-input matrix. Production code is unchanged.

Latest full suite after chronological fixture replay:370 passed,0 failed,1 existing missing-private-fixture skip in3m13s; `artifacts/chronological-catch-full-tests/chronological-full.trx`. Focused sequence replay passes independently; Release compilation and diff check pass. Only tests/fixtures/docs changed, so the active production build remains the already verified stacked-catch build. Coverage percentages remain the369-test measurement. Current live trial was intentionally stopped by the user after337.914s with21 confirmed/0 Unknown; no autonomous stop defect inferred. Fishing remains off; long-run and three-notification live acceptance remain open.

Stop attribution follow-up: the user-ended live trial exposed that completed recordings only said Stopped. FishingEngine now records the first stop source as a stop-request event and in completion statistics, preserving it across subsequent Stop/Dispose calls and clearing it on Start. Sources distinguish toggle control/hotkey, emergency stop, window close, queued catch/recovery stop, automation interruption and disposal. A normal worker exit without a request is labeled explicitly. Existing recording/restart scenarios are strengthened to assert completion attribution, cleanup preservation and no stale reason on restart. All8 focused cases pass (`artifacts/stop-source-tests/stop-source.trx`). This is diagnostic evidence, not a change to stop/retry policy. Build `artifacts/stop-source-build` has0 warnings/errors, DLL SHA256 `6C64A0E2AF9624384D9D83CBD408404A5B9DF618E8493A861DCA97F02255EE24`; not launched. The already-open app remains stopped at the user's request.

Stop-source full regression:370 passed,0 failed,1 existing private-fixture skip in3m8s (`artifacts/stop-source-full-tests/stop-source-full.trx`). Release build and diff check pass. Coverage remains the prior369-test source snapshot; no updated percentage claimed for stop attribution. No live launch or input occurred during this offline change.

Current clean-source verification:144 tracked/nonignored files copied outside repository ancestry to an isolated temporary tree; per-file hashes are in `artifacts/ci-current-verification/manifest.json` (manifest SHA256 `4C658EDFBFCB07657A7512090CAB0CB3D6FFD0B5671B5F22A1848FE978A31B04`). No private recordings, screenshots, build outputs or settings were copied.367 passed,0 failed,4 explicit private-fixture skips in3m12s (`artifacts/ci-current-verification/results/59eb7891c39243d3bd94cf5724991cde/tests.trx`). The shake/cast/stacked/chronological fixtures are required and included. Clean-source coverage57.06% lines/48.76% branches; FishingEngine69.39%/55.54%. Report hash independently checked against summary; regression floors unchanged. CI's self-contained win-x64 publish command also succeeded locally to `artifacts/ci-current-verification/portable`. `result.json` records executable/report hashes and scope. This is not a remote CI run, portable runtime acceptance, or release. The existing local370-test result includes three available private-image tests absent here; the difference is explicit skips, not failures.

Replay viewport regression: ReplaySession.Predict called catch recognition without recorded viewport height, causing the detector to infer scale from the ROI height. The required stacked player-catch fixture reproduced a false negative while companion-only remained negative (`artifacts/replay-viewport-tests/replay-before.trx`:1 failed/1 passed). Replay now passes frame.Viewport.Height and disposes its DetectionResult. Both focused cases pass afterward. This corrects replay/export perception evidence; live worker code is unchanged. Performance comparison work remains open, because replay correctness must precede timing claims.

Replay viewport full regression:372 passed,0 failed,1 existing private-fixture skip in3m20s (`artifacts/replay-viewport-full-tests/replay-viewport-full.trx`). Release build `artifacts/replay-viewport-build` has0 warnings/errors, DLL SHA256 `C424A4AF57FC450882C6D6C7F57160963B06901E5DC6AB69438EF459BE9223F9`; not launched. Diff check passes. The earlier clean-source367-pass run predates this two-test replay correction, and coverage has not been remeasured for this change. Fishing remains off.

A07 evidence added: paired annotation/recording on-off component benchmark uses nine chronological frames, two reverse-order passes and2880 measured frames. Predictions identical across modes;102–104 retained images per recording run,0 drops, no writer errors. Raw report/source snapshot: `artifacts/replay-performance/20260920_022705/`; detailed ranges and limitations in OPTIMIZATION-2026-09-19.md. Recording raises process CPU work, but p95 overlap does not establish faster fishing or preview savings. Capture/input/WPF/full-worker performance and actual before/after gains remain open. No production code changed or live fishing input was sent for this measurement.
