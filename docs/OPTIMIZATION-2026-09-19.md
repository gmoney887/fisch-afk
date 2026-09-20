# AFK speed and reliability changes

Default policy: maximize reliable fishing throughput. Remove redundant work and replace fixed waits with verified UI readiness. Keep the evidence required to distinguish catches, retries and unknown outcomes.

## Implemented

- AFK Performance is enabled by default, including when loading configurations that predate it. It suppresses preview rendering, On Top and timing jitter without overwriting their saved preferences or disabling recording. Switching it off restores those preferences.
- Casting enters visual lure detection immediately in AFK mode. Two consecutive catch-prefix detections and two equipped-hotbar observations allow early completion; uncertain outcomes retain a 1.5-second observation window. Predictive cast release is preserved.
- Start and Stop clear stale queued-stop intent. Physical hotkeys dispatch once per press; injected input and keyboard repeats do not issue repeated commands.
- Detection results own their native debug images until explicitly transferred. Preview and dashboard work run at at most 10 Hz for repeated states; terminal Stop delivery remains immediate. WPF brushes are cached and frozen.
- Reel masks use HSV. Auto theme selection tests individual UI color candidates and locks after two agreeing live-reel observations, rather than using scenery color averages. Needle run searches have bounded width.
- Catch confirmation matches reviewed main catch phrases, rather than counting arbitrary yellow shapes or treating companion rewards as a successful reel. The live trial exposed a font-size difference between the enlarged/fading fixture and settled banner; both observed variants now have reviewed templates and scale regressions.
- Logging has a bounded background queue, reserved capacity for non-input events, batched flushing, runtime rotation and shutdown draining. Queue saturation is observable through DroppedEntries.
- Desktop capture rejects overlapping visible top-level windows with opaque bounds and resolves child game surfaces to their root window. Click-through layered overlays are excluded: their desktop-sized transparent canvases are not evidence of full occlusion. This guard cannot detect their individual painted pixels, and is not a game-window capture backend or a guarantee against windows appearing during a capture. The live smoke test caught the computer-use cursor overlay triggering the initial overly conservative guard; the corrected guard handles that case.
- Workflow templates are decoded and resized once per viewport height within each workflow instance.

## Component benchmark

Local Release microbenchmark, existing wide-bar fixture, 30 warmups and 500 measured samples per mode. Both assemblies were loaded into the same isolated benchmark harness; no game capture, recording or WPF rendering is included.

| Measurement | Previous build | Optimized build |
| --- | ---: | ---: |
| Reel, preview off, managed bytes/frame | 13,881 | 9,320 |
| Reel, preview off, p50 / p95 ms | 2.032 / 2.906 | 1.927 / 2.525 |
| Reel, preview on, managed bytes/frame | 14,296 | 9,736 |
| Reel, preview on, p50 / p95 ms | 1.548 / 2.195 | 1.644 / 2.288 |
| Logging producer, p50 / p95 ms | 0.0043 / 0.0072 | 0.0007 / 0.0013 |

Managed reel allocations fell about 33%; logging producer latency fell about fivefold at p95. Track latency is mixed and sensitive to warmup/tiering and concurrent game load. These measurements do not establish end-to-end FPS or catches/hour gains. Native scratch-buffer reuse, recorder encoding profiling, full-controller replay extraction and multi-hour memory/AFK acceptance remain follow-up work.

## Verified Fisch appearance settings

Inspected through the running game's Menu > Appearance on September 19, 2026. Fishing was paused for inspection and then resumed at the same position. Changed:

- Show Catch Flags: on → off (description explicitly identifies rare-fish flags).
- Show Others' Held Fish: on → off.
- Show Others' Fish In Water: on → off.
- Show Others' Lanterns: on → off.
- Enable Environment Tints: on → off.
- VFX Visibility: Show All → Hide All.

Shadows Enabled and Clouds Enabled were already off. Disable Fish Cutscenes was already on (the game's description says previously seen cutscenes are skipped). Brightness, saturation and Photosensitive Mode were left as observed. Auto-Toggle Performance Settings was left off to avoid automatic appearance changes during validation.

These are game settings, separate from the macro's AFK Performance toggle. The macro does not currently automate or restore the game's appearance switches. The screen visibly lost catch flags after the change; no controlled trial has yet attributed a detection failure or measured a throughput gain to those flags.

## Verification

Release build and regression suite cover old reel fixtures, scaled catch phrases, negative colored shapes, theme selection against colored scenery, hotkey edges, queued-stop reset, frame ownership, runtime log rotation, AFK preference restoration and telemetry cadence. Live acceptance of the new executable is tracked separately from tests and benchmarks.

Final suite: 219 passed, none failed or skipped. Release build had zero warnings/errors. `git diff --check` passed with the repository's configured line-ending handling.

Final live smoke session: `session_20260919_190800_528_22570f26614a43a5b1196c468236c863`, viewport 2254 x 1353, AFK mode on, recording on, preview off. The first two completed cycles both reported ConfirmedSuccess with zero unknowns. Their PostCatch transitions took 113 and 119 ms, versus the previous roughly 1.5-second observation wait. Cast release immediately entered Luring. This short same-PC smoke trial is not a multi-hour acceptance test or an independently measured catch-rate comparison. The optimized local executable was left fishing at the existing location.

## Current paired annotation and recording comparison

Local Release benchmark on2026-09-20 using the same nine consecutive recorded track frames467..475 for every mode. Two opposite-order passes,45 warmup frames per mode,360 measured frames per pass at30Hz (2880 total). Assembly SHA256 `C424A4AF57FC450882C6D6C7F57160963B06901E5DC6AB69438EF459BE9223F9`; exact results and a source snapshot are under `artifacts/replay-performance/20260920_022705/`. Reproduce from the repository root with `dotnet run --project artifacts/replay-performance/benchmark.csproj -c Release -- D:/Dev/fisch-afk` after building the referenced replay-viewport-build output.

| Annotation generation | Recording | p95 producer time across passes | Producer managed bytes/frame |
|---|---|---:|---:|
| Off | Off |16.11–17.09ms|20631–20637|
| On | Off |17.50–17.69ms|20897|
| Off | On |16.43–18.02ms|20762|
| On | On |16.66–17.00ms|21027–21028|

All modes returned identical bar/fish/live-reel positions and catch predictions on every measured frame. Each recording run retained102–104 images, with0 drops and no writer errors; shutdown drain ranged4.73–101.37ms. Process CPU usage was6.16–7.19 seconds per12-second nonrecording run and7.66–9.00 seconds per recording run. The paired samples show recording CPU cost, but p95 ranges overlap and annotation ordering is inconsistent: no throughput improvement or percentage speedup is established.

This measures native vision plus catch recognition and producer-side recorder work on predecoded crops. Recording uses the real asynchronous PNG writer and100ms frame admission throttle. Annotation generation is requested every frame here, not the live UI's throttled cadence. Desktop capture, mouse/keyboard input, WPF presentation, full-worker scheduling, native allocations and catches/hour are excluded. Managed allocations count only the producing thread, including harness signatures; background writer allocations are excluded. It is a component comparison on the current build, not a before/after optimization result or completed A07 acceptance. Do not compare its absolute timings to the earlier reel-only microbenchmark, which used a different workload.
