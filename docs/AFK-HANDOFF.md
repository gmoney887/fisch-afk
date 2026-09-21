# AFK continuation handoff

Updated 2026-09-20. Start here in future sessions, then inspect the current working tree and referenced evidence. This handoff supersedes old status statements in WINDOWS-HANDOFF.md; dated test reports remain snapshots, not proof for later edits.

## Objective and user expectations

Extend test results to cover **all primary AFK scenarios so we prevent regressions**. The goal remains incomplete. Start Fishing should keep trying until the user stops it, with guarded input and safe recovery. Optimize catching speed by default without skipping observed readiness. Keep the UI consistent and simple, with details available as needed. Do not treat unit-test coverage or rising catch counters as proof of effective unattended fishing.

## Release and local state

Release update: v1.0.13 packages the combined UI, preview, aquarium, death-screen, and session-diagnostics changes described below. Its release gate passed 413 tests with zero failures and one existing private-fixture skip on 2026-09-20. The portable package includes the four aquarium templates. Historical unpublished-state statements below describe earlier snapshots; live and second-PC acceptance gaps remain open. See [v1.0.13 release notes](RELEASE-v1.0.13.md).

- Published preview: [v1.0.12-preview.1](https://github.com/gmoney887/fisch-afk/releases/tag/v1.0.12-preview.1), commit `af9800f`. The user's son is testing on another PC; no second-PC acceptance evidence has been verified in this task.
- Local, unpublished changes from this task: death-screen recognition and explanatory readiness/recovery status; death incident recording and frame prioritization; separate bounded incident journals and export support; death/walking-key/retention regressions.
- Latest full verification **for that snapshot**: 392 passed, zero failed, one missing private-image fixture skipped; overall coverage 58.33% lines / 49.68% branches. [Exact coverage report and hash](COVERAGE-DEATH-2026-09-20.md). TRX: `artifacts/death-recovery-coverage/56b4a8bef30e40e9984295819654dcc4/tests.trx`.
- The working tree now also contains aquarium workflow/scheduling/templates/tests, UI/icon and telemetry changes. Preserve all of them. This task has not verified the combined tree with those changes. [Aquarium handoff](AQUARIUM-FIX-2026-09-20.md) reports 35 targeted passes and a local portable build, but no live workflow acceptance. Do not repeat the older blanket claim that no aquarium templates exist; crates remain separately incomplete.
- No new version has been published with these local fixes. Do not assume the app currently running on either PC matches source or the published preview. Inspect its path/version and session manifest before testing or replacing it. The overnight manifests reported 1.0.11.0, not the new preview.

## Prioritized remaining work and acceptance evidence

| Priority / task | Current gap | Evidence needed before closing |
|---|---|---|
| P0 — Position loss and ineffective retries | Boat-to-water cause unknown. No position hold, displacement detector or return-to-spot behavior. Visible hotbar is not proof that fishing is possible. | Preserve and inspect an actual displacement sequence and its inputs; check movement mode, external inputs, respawn and boat motion without assuming a cause. Reproduce shore/boat cases. Verify guarded response to an unfishable location, no blind movement, persistent Start and immediate Stop. Validate any proposed return/position protection in the game. |
| P0 — Death recovery | The recorded WASTED screen is recognized and waits without blind input; it does not respawn or restore a fishing location. | Independently captured death variants; live status/incident validation; determine actual available respawn controls, verify them before acting, and prove return to a fishable location followed by a catch. No fabricated recovery success from hotbar presence alone. |
| P0 — Disconnect/update/rejoin | Controlled Reconnect/Continue replays pass, and partial live probes exist. Full unattended return to fishing is not accepted. | Real disconnect/update → reconnect → loading → Continue → fishable gameplay → confirmed catch; include delayed transitions, focus loss, stale controls and Stop. Preserve chronological evidence. |
| P1 — Long-run acceptance on both PCs | Nearly12-hour overnight uptime ended in ineffective operation. No memory/latency time series or complete incident footage. | At least two hours per PC with identified binary/settings/viewport; independently sampled catches and Unknown review; bounded memory/storage; capture/loop latency; no silent stop or sustained ineffective retry loop. Diagnose failures rather than accepting duration alone. |
| P1 — Idle prevention | Simulated heartbeat delivery passes; ordinary fishing input confounds live idle evidence. | More than25 minutes without ordinary fishing/user input; verify Roblox stays connected and heartbeat cadence/release/Stop behavior. |
| P1 — Desktop, rods and gameplay variants | Coverage is strongest on controlled replays and selected screenshots. | Full chronological cast/lure/reel/catch cycles across supported rods/themes; live resolutions/DPI/window resizing, minimization, occlusion, client replacement and focus changes; physical F6/F7/End behavior including held-input release. Verify second-PC behavior independently. |
| P1 — Recording and incident evidence | Separate 2 MiB incident journal survives routine churn, but both journals and images remain bounded. Displacement footage previously expired. | A sustained real incident preserves useful before/after images plus inputs/recovery reasons; expiry/drop counters remain accurate; unreadable/full storage does not stop fishing; long-run resource measurements. Test incident frame prioritization independently of event presence. |
| P1 — Integration and release | New changes are local; aquarium/UI/telemetry work was added after this task's full-suite snapshot. | Review combined diff, run appropriate Windows tests/coverage and portable startup checks, identify exact source/build, then publish when requested. Preserve settings/recordings; do not interrupt an active AFK run just to replace its executable. |
| P2 — Optional workflows | Aquarium local implementation has targeted evidence but no live acceptance; crates incomplete. | Aquarium actual successful/empty/failed claim, verified close, schedule persistence and return to fishing; crates reviewed controls/rewards and equivalent failure/Stop coverage. Keep optional workflows out of the core baseline until validated. |
| P2 — Performance and remaining inventory | Component benchmark is not full-worker throughput evidence; detailed feature ledger has additional gaps. | Comparable full-worker before/after measurements for catches/hour, capture/input/UI latency and CPU/memory. Review every open row in FEATURE-ACCEPTANCE.md, including settings/hotkeys, statistics semantics and optional adaptation. |

## Incident facts to preserve

See [overnight review](OVERNIGHT-REVIEW-2026-09-20.md) for precise scope and limitations.

- Overnight session `session_20260920_042849_142_21fa82649a944a9e9a031ad8304263a3`: 11h48m50s, reported 2138 confirmed / 15 Unknown / 0 failed, 2107 recoveries. Final saved central crop shows **WASTED**. Reconnect events do not by themselves prove server shutdown.
- Later session `session_20260920_170854_292_0b8e2e9e1e3a465c968fbe340a0b7138`: 1h57m44s, reported 17 confirmed / 1 Unknown / 0 failed, 1305 recoveries. Early image shows standing near boat edge with E/Sit; later image shows the character beside the boat in water. Exact transition missing.
- Retained keyboard logs show no walking/jump down edges, but expired history prevents a definitive causal conclusion. Click-to-move, game physics and external input are hypotheses, not findings.
- Both sessions ended with recorded Start/Stop-control-or-toggle requests. Distinguish explicit worker Stop from earlier loss of effective fishing.
- Private metadata/journals and sampled images: ignored `artifacts/overnight-review-2026-09-20/`. Source recordings: `%LOCALAPPDATA%\FischAFKPro\recordings`. Retention can remove source files. Do not publish raw recordings without reviewing their contents and authorization.

## Resume procedure

1. Read this handoff, [feature inventory](FEATURE-ACCEPTANCE.md), [regression matrix](AFK-REGRESSION-MATRIX.md), and any newer dated handoffs. Inspect `git status`/diff and running-app state; preserve concurrent work.
2. Prioritize position loss and ineffective recovery. Keep known facts separate from hypotheses. Do not induce a fall/death or interrupt the user's ongoing fishing without an agreed live trial.
3. For code changes, add a regression that exercises the actual failure where possible. Use recorded identity evidence for UI actions, revalidate before input, and test cancellation/release. Replays simulate desktop/input delivery; do not label them live acceptance.
4. Run tests appropriate to the combined changes. Windows SDK available here: `C:/Users/garre/.dotnet/dotnet.exe`. Full coverage command: `./scripts/Test-WithCoverage.ps1 -DotNet 'C:/Users/garre/.dotnet/dotnet.exe' -ResultsDirectory 'artifacts/<new-run-name>'`. Private-image skips must stay explicit. Never reuse old coverage percentages as current verification.
5. Update this handoff and the acceptance ledger with exact artifacts, build identity, outcomes and remaining gaps. Keep the goal open until the full primary AFK scope is verified. Documentation alone does not complete it.

Useful entry points: `Core/FishingEngine.cs`, `Core/AfkRecovery.cs`, `Core/GameplayInput.cs`, `Core/FlightRecorder.cs`, `Core/SessionEventJournal.cs`, `Vision/DeathScreenDetector.cs`, `Core/PreFlightDiagnostic.cs`, and `FischMacroCS.Tests/FishingEngineReplayTests.cs`. Historical live/performance evidence: [live ledger](LIVE-TESTING-2026-09-19.md), [optimization review](OPTIMIZATION-2026-09-19.md).
