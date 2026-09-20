# Reliability and performance review — 2026-09-19

Current acceptance ledger and measured coverage: [FEATURE-ACCEPTANCE.md](FEATURE-ACCEPTANCE.md). The earlier component review below did not establish unattended reliability. The subsequent long run failed recovery after a server update.

Implementation follow-up: see [OPTIMIZATION-2026-09-19.md](OPTIMIZATION-2026-09-19.md) for fixes, measurements and remaining validation. The findings below preserve the original review snapshot.

Scope: current working tree after the dark-bar, persistent-retry and clipped-scenery fixes. Reviewed fishing state transitions, input ownership/cancellation, vision, capture, recording, logging, settings, workflow verification and dashboard dispatch. This is a static review with a targeted catch-detector probe, not full runtime profiling or release acceptance. Production code was not changed during this review.

Earlier work is documented in RELIABILITY-VALIDATION.md and LIVE-TESTING-2026-09-19.md. The latest isolated build passed 200 tests. Those tests cover individual components and selected frames; the full fishing controller is not yet replayable without Windows/game dependencies. Multi-hour, independently labeled AFK trials remain outstanding.

## Prioritized findings

### P1 — A queued stop survives emergency stop and affects the next run

Locations: MainWindow.xaml.cs:252, MainWindow.xaml.cs:986; Core/FishingEngine.cs:542, Core/FishingEngine.cs:601, Core/FishingEngine.cs:2083.

F6 during Reeling sets IsStopQueued=true. Pressing End then calls Stop without clearing that flag; Start also leaves it untouched. The next session reaches PostCatch, sees the previous session's flag and stops unexpectedly. This directly conflicts with the requested persistent fishing behavior. Reset queued intent atomically at both session boundaries and add a queue-stop → End → Start regression test. Evidence: traced source paths; no live inputs sent during review.

### P1 — Catch confirmation accepts arbitrary colored shapes

Locations: Vision/VisionProcessor.cs:1391–1449; Core/FishingEngine.cs:2055–2078.

The catch detector counts twelve appropriately sized bright/yellow contours in a broad region; it does not establish a catch banner's identity or layout. A local probe using twelve plain yellow rectangles returned true. The recorded no-reel scenery fixture returned false, so this probe does not establish that this particular scenery inflated catches. Any false positive increments catch/streak counters, resets recovery budget and can confirm adaptive samples or trigger crate scheduling. Replace the letter-count rule with reviewed banner geometry/template evidence across frames, using OpenCV, and test negative scenery/bait text/UI fixtures. Do not interpret the existing counters as independently verified catch results.

### P1 — Automatic theme selection depends on scenery

Location: Vision/VisionProcessor.cs:153–178.

AutoCalibrate selects the bar/needle color rules from the mean BGR color of the entire track crop on every call. Colored terrain and lighting can change that mean even when the reel UI is unchanged. This creates both missed bars and accepted scenery; rejecting clipped contours only fixes one manifestation. Infer theme from verified reel geometry, require temporal agreement and retain that theme for the current reel. Validate with the same UI against several backgrounds. Evidence: confirmed selection logic; broad failure rates have not been measured.

### P2 — PostCatch does not dispose the track debug image

Locations: Core/FishingEngine.cs:1524, 1710, 2045–2177; Vision/VisionProcessor.cs:467–490.

ProcessTrack clones an annotated Mat whenever the 33 ms debug cadence is due, even when preview is disabled. In PostCatch, the pending-outcome continue and normal completion paths do not dispose that annotated track frame or transfer it to telemetry; telemetry instead receives a separate full-frame preview. Native image buffers therefore depend on eventual finalization rather than deterministic disposal, causing avoidable memory pressure during long runs. Disable unused annotations and give every detection result scoped ownership, transferring only the image actually delivered to the UI. Measure private bytes over a long replay before/after; an unbounded live leak has not been measured.

### P2 — Logging adds synchronous disk work to input timing, and its size cap only runs at startup

Locations: Core/SessionLogger.cs:49–93; Core/FishingEngine.cs:1126.

Each mouse transition writes through a locked AutoFlush StreamWriter on the control thread. The 10 MB check runs only when the writer is initialized, so a single long AFK session can exceed that intended limit. A slow filesystem can also delay control and recovery, since the watchdog checks run on the same worker. Use a bounded background logger with batched writes, runtime rotation and explicit flush at stop. Preserve urgent events under saturation. This is a confirmed synchronous path and retention defect; its latency contribution needs measurement.

### P2 — Holding a hotkey repeats commands

Location: Native/GlobalKeyboardHook.cs:64–83.

Every WM_KEYDOWN/WM_SYSKEYDOWN invokes the handler; the hook tracks neither key release nor an already-down state. Keyboard auto-repeat can turn one sustained F6 press into queued-stop followed by forced-stop, or enqueue repeated re-equip operations. Dispatch only on a fresh down edge and reset held state on key-up/uninstall. Test held keys, rapid separate presses and injected input handling.

### P2 — Foreground validation does not prove the captured pixels belong to Roblox

Locations: Capture/ScreenCapture.cs:28–63; Core/FishingEngine.cs:122–133.

Capture uses the desktop DC, so a topmost window covering Roblox becomes part of the image. Roblox can still be foreground while a non-activating overlay covers the detection area. The current checks validate foreground/window geometry, not occlusion. Consider a game-window capture backend with freshness checks, or reject frames with verified overlay occlusion. This is a capture limitation inferred from the implementation, not a new reproduced failure in this review.

## Optimization candidates

1. Remove unused debug frames and decouple dashboard updates from controller frequency. Reuse frozen WPF brushes instead of constructing many brushes per telemetry delivery. Start here because the work is clearly redundant.
2. Move synchronous logging off the control thread and rotate during the run. Benchmark p50/p95/p99 loop time with recording and preview independently enabled/disabled.
3. Reuse OpenCV scratch Mats, masks and morphology kernels. Replace repeated managed column scans with native projection/connected-component operations where equivalent. The current peak search rescans neighboring runs for candidate columns; do not substitute a faster detector without checking the recorded positives and negatives.
4. Cache reviewed workflow templates and scaled variants by viewport height. TemplateWorkflowVision currently reads and resizes from disk on each Find. This matters once the incomplete reward workflows are implemented; it is not a current fishing throughput priority.
5. Profile PNG encoding and frame cloning before changing recorder fidelity. The recorder already writes asynchronously with bounded memory/disk buffers; retain that evidence quality unless measurements justify a different sampling/encoding policy.

No speedup percentages are claimed: this review did not benchmark the whole pipeline. Capture, native processing, logging, UI and encoding should be measured separately before optimizing by guesswork.

## Suggested implementation order

First fix stale stop intent and hotkey edge handling. Then make frame ownership deterministic and remove unnecessary previews. Next strengthen catch/theme evidence and extract a pure state/controller replay runner that can execute whole recorded sequences, including false-reel recovery and Stop/restart. Finally optimize measured bottlenecks and run multi-hour AFK trials, with process memory, loop latency and independently checked catches recorded. Keep reward automation out of the acceptance run until its workflow contracts and reviewed templates are complete.
