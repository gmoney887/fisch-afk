# Windows continuation: reliable gameplay feedback loop

## Native Windows continuation — 2026-09-18

This task now runs natively in Windows at `D:\Dev\fisch-afk`. The dirty tree was preserved. Native restore/test passed 147 tests initially; the continuation currently passes **165 Release tests**. Local self-contained packaging succeeded; no GitHub publication or diagnostic upload occurred. Three first-PC passive sessions have now been reviewed: aquarium, crates, and an End stop. See [live observations](LIVE-OBSERVATIONS-2026-09-18.md). Crate UI differs from the current workflow and reward text is partly occluded. Actual fishing, held-input stop, and second-PC trials remain outstanding.

Read [RELIABILITY-VALIDATION.md](RELIABILITY-VALIDATION.md) for the saved acceptance requirements, implemented changes, local verification steps, and remaining gaps. It supersedes the implementation-status statements below: passive observation, replay/labeling, private-report UI, bounded journals, additional input/adaptation/report tests, and guarded post-pause recording have now been added. Reward templates and two-PC live validation remain outstanding. The implementation is still in progress and is not release-ready.

## Previous handoff (historical context)

This is an IN-PROGRESS working tree, not a release-ready implementation. Preserve all uncommitted work. The user supplied the full four-stage plan in the conversation and subsequently asked whether native Windows would be a better environment. It is preferable for this WPF/Win32/OpenCvSharp app and live gameplay testing.

Open the existing `D:\dev\fisch-afk` folder with a Windows-native agent. Do not clone over it, reset it, or assume untracked files are disposable. Use the original plan plus this note as context. Do not publish a release unless explicitly requested.

## Verification state

- Initial Windows baseline: `C:\Users\garre\.dotnet\dotnet.exe test FischMacroCS.slnx --no-restore --verbosity minimal`: **139 passed**.
- An intermediate revision containing the initial coordinator/input/catch changes also passed the existing 139 tests, with one nullable warning subsequently addressed.
- Latest app and test projects cross-compiled successfully with the Linux .NET 10 SDK and Windows targeting enabled: **0 warnings, 0 errors**. The latest changes **have not run through the Windows test suite yet**. Eight new tests were added after the last successful native test run.
- WSL Windows interop stopped working during the session: first `UtilBindVsockAnyPort: socket failed`, later `cannot execute binary file: Exec format error`, including through cmd.exe.
- A Linux SDK was installed only in `/tmp/fisch-dotnet` for cross-compilation. It cannot run the Windows WPF/OpenCV test suite. Run a Windows restore/build first; the cross-build rewrote ignored obj/project.assets.json with Linux package paths, so do NOT start with --no-restore.
- No two-PC recordings, live trial, credentials, GitHub upload, release publication, or performance improvement has been verified.

## Pre-existing work to preserve

At task start these files were already modified: App.xaml, AssemblyInfo.cs, Core/FishingEngine.cs, FischMacroCS.Tests/RodDetectorTests.cs, FischMacroCS.slnx, MainWindow.xaml, MainWindow.xaml.cs, Vision/VisionProcessor.cs, publish_portable.bat. These untracked files also already existed: Core/RecordingSubmissionService.cs, SubmitRecordingDialog.xaml/.cs, CatchVerificationTests.cs, RecordingSubmissionTests.cs, ReelTrackingRegressionTests.cs, and three reel PNG fixtures. Initial `git diff --check` issues include pre-existing CRLF/trailing whitespace. Do not indiscriminately revert or normalize them.

## Current changes

- AutomationRuntime.cs: replaceable frame/clock interfaces, timestamped observation model, tri-state outcome tracking, recovery budget, single thread workflow coordinator with bounded command queue and cancellation generations.
- GameplayInput.cs: input sink with validation at input edges, tracked mouse/key release, cancellable delays. FishingEngine routes inputs through it and manual actions through coordinator; watchdog now checked by worker rather than a separate input-producing task.
- Focus, minimization, size and DPI changes interrupt gameplay, requiring explicit restart. Hotbar estimated-coordinate fallback removed. BitBlt failure returns invalid capture.
- Catch finalization waits 1.5 seconds before updating statistics once; absent banners yield unknown. Three recovery attempts per incident, reset on confirmed catch.
- VerifiedWorkflow.cs and reward routines: prerequisite/action/expected-result checks and bounded timeouts. **No trusted workflow PNG templates exist yet**. Aquarium and crate commands therefore return unknown, and scheduled aquarium work will pause the app. This is a deliberate no-false-success guard, NOT completed reward automation. Obtain real fixtures and review/rework the workflow against actual gameplay before enabling it for unattended use. New template files need project copy/publish entries.
- FlightRecorder.cs: background bounded queue, raw PNG frames, JSONL events, schema-versioned manifest; 15-second rolling raw buffer and preservation around outcome events. Every tenth success sampled; preserved-frame cap; completed-session count/age/size retention. Raw capture wired around engine sources. No AVI playback replacement in UI yet.
- AdaptationProfile.cs: bounded normalized pull estimator and local compatible profile persistence after sufficient observations/confirmed catches. Needs tests and live comparison; not demonstrated to improve performance.
- AppDataPaths.cs: LocalApplicationData/FischAFKPro paths, legacy config read migration, temp-and-replace config writes. Existing old recording folders are not migrated/discovered yet.
- RecordingSubmissionService.cs: session metadata instead of submission-time engine statistics/global logs; raw frames included in local ZIP; 20MB reduced bundle without media; removed inaccurate anonymity claims.
- PrivateReportClient.cs: **not wired into dialog yet**. Credential Manager storage, private-repository enforcement, upload to pre-existing `diagnostics` release, persisted progress, deterministic asset names and conservative issue retry reconciliation. No requests were sent. Needs mocked HTTP tests and UI configuration/preview/clip selection before use. Existing dialog still uses the prior public GitHub/manual and Discord submission paths; replace these for the plan's private flow.
- Windows CI build/tests/portable artifact draft, and portable script uses `dotnet` from PATH rather than a personal SDK path.

## Next steps (in order)

1. Run Windows `dotnet test FischMacroCS.slnx` and fix compilation/test failures before extending scope. Review the broad engine/recorder diff carefully.
2. Audit coordinator lifecycle: rapid stop/start, queued manual commands, cancelling standalone workflows, app-close disposal vs Dispatcher callbacks, token lifetime races, UI completion errors, no input after stop. Add injected tests for focus/capture/geometry interruption and held-input release.
3. Audit recording bounds: active raw ring memory/disk size, event-journal growth, global 1GB including active/export sessions, outcome preservation on recovery/focus loss, recorder errors visible in UI, and final session statistics. Five seconds after failure is only retained while capturing; paused states currently stop immediately. Add session-time clock mapping, dropped-entry details, and session/PC identity.
4. Gather annotated ground truth on Windows for aquarium, crates, empty inventory, delayed banners, and negative UI fixtures. Missing templates must never silently restore estimated clicks. Finish aquarium and crate workflows (equipment state, quantity, bag closure, re-equipping, verified empty state).
5. Build passive observation UI and replay/labeling tool. Neither is implemented. Complete session/PC-separated evaluation.
6. Test adaptation bounds, stale/reset logic and persistence. Current changes do not normalize every controller constant, estimate release/gravity dynamics, or fully extract replayable decisions.
7. Wire/test private reporting, repository/token setup, media preview/redaction/selection, explicit reduced-bundle preview, retries and credential errors. A server issue with an uncertain POST outcome is intentionally not automatically recreated. Repackaging an existing ZIP may change its hash; ensure retries reuse the frozen bundle/progress rather than recreating it.
8. Complete legacy data migration, update notification and rollback preservation. Run Release self-contained packaging.
9. Require the original plan's two-hour unattended trial on each PC, matched catches/hour comparisons, negative-fixture checks, and immediate input release checks before any release.

## Suggested continuation prompt

Read docs/WINDOWS-HANDOFF.md and the original reliable Fisch automation plan. Preserve the entire dirty working tree. First compile and run all Windows tests, then review and finish the in-progress implementation. Treat the latest changes as unverified. Do not claim aquarium/crate success without real visual evidence, do not claim replay proves alternative input outcomes, and do not publish a release without my explicit request.
