# Crate recognition fix — September 27

The user reported that the rod-reset build could not open even one crate despite a visibly populated filtered inventory. The five-frame session `session_20260927_190341_077_203d3077cd734b618d545c47ecaa7218` is preserved under ignored `artifacts/crate-recognition-20260927/`. Its final outcome was `No recognizable crate or verified empty result grid was found.`

The last frame independently reproduces recognition failure with a 357-item / 79-stack inventory. The existing filter template also failed on this frame's caret rendering. Added reviewed native-resolution variants for the dense Bait Crate label and exact crate filter, with its caret and trailing padding. Preserved the existing confidence thresholds and older recognition variants. The filter uses a grayscale fallback to preserve antialiasing when scaled; white-text matching handles varying item backgrounds. A tight word-only filter crop was rejected during testing because it also matched the wrong filter `crates`.

The regression retains only the inventory region; player/chat content is masked. It verifies recognition and an item point inside the result grid at 1353, 1080, and 1009 viewport heights. Existing wrong-filter, fish-search, empty-inventory, quantity, selection, reward, cancellation, and workflow deadline checks remain passing.

Validation: 38 targeted tests passed (CrateWorkflowTests, RodResetGuardTests, and ManualCrateFailureCreatesOneRecordingWithSettingsAndOutcome). The self-contained build is `artifacts/crate-label-fix-20260927/FischMacroCS.exe`, containing the rod-reset fix and the pre-existing reward workflow changes. This is recorded-image and simulated-workflow validation, not a live opening or exhaustive support for every crate label/layout. The running executable was not replaced.

## Follow-up: native crash after selecting a crate

The next live attempt (14:11:31 local) terminated with Windows Application Error 1000 / .NET Runtime 1026, exception `c0000005`, in `OpenCvSharp.Internal.NativeMethods.imgproc_GaussianBlur`, called from `CrateVision.Match` while `CloseBag` waited for inventory closure. Its incomplete three-frame recording is preserved in `artifacts/crate-full-fix/session_20260927_191128_270_9bf3e3f051664dd9988ff4fe55a913d4`. Selection alone was not an opening.

Crate matching now uses a separate, owned destination for managed 3x3 smoothing, removing the native GaussianBlur call from this path. Resized/smoothed template variants and successful dimensions are cached across stacks, and invalidated when viewport height changes. Confidence thresholds and full workflow checks remain unchanged. This targets redundant recognition work; live speed improvement has not yet been measured.

The self-contained candidate is `artifacts/crate-complete-fix/FischMacroCS.exe`. Its `--verify-crates 1` option runs the production workflow with a 90-second cancellation budget and writes `crate-verification.json`; the requested batch limit is clamped to 1–3. It does not establish success independently of the normal visual confirmation checks.

Validation: the original 38 targeted tests pass; a further repeated-closure/cache-invalidation test alternates viewport heights and forces collection between matches. All 34 crate tests pass with this test included. The attempted live verification at 14:18 captured the desktop instead of the game and returned `Inventory did not open after G`. No live opening or end-to-end speed claim is justified yet. Waiting for the user to restore the client before another live run. No release was published.

## Completed live verification

The second desktop-only capture was traced to the hidden diagnostic launch and `ForceSetForegroundWindow` calling synchronous `ShowWindow(SW_SHOW)`. Its first-call startup override can use the diagnostic launch's hidden setting. Replaced these two calls with `ShowWindowAsync`. The same Roblox process (PID 12284) then regained its window and the next verification succeeded; no new Roblox crash event was found. This activation correction is separate from the earlier native crate-recognition crash.

At 14:25, one stack completed in 13.787 seconds including rod restoration. At 14:26, three consecutive stacks completed in 33.571 seconds including rod restoration, with three distinct `ConfirmedSuccess` outcomes. Independently inspected saved frames show rewards for Big Electric, Fossilized, Darkened, and Scorched Bait Crates. Evidence is preserved under `artifacts/crate-full-fix/`, including `three-stack-result.json` and `reward-evidence.jpg`. These are four confirmed stack-opening operations, not a measured individual-crate count or overnight endurance result. Current repeated throughput is about 11.2 seconds per stack; no comparable pre-fix successful baseline exists for a percentage speedup claim.

The candidate executable remains `artifacts/crate-complete-fix/FischMacroCS.exe`; no release was published.

## Sustained failure invalidates release readiness

Session `session_20260927_192851_103_2ae0389d2aac419c9ecd06082477b4ca` ran for 130 seconds before reporting unconfirmed reward. Frame 78 independently shows an opened Sparkling Big Electric Bait Crate. The prefix falls left of the fixed notification column. A blank white mask from a colored template could also produce meaningless normalized correlation results. The full earlier release suite passed 527 tests with one skipped, but this live failure still blocks publication; the locally generated v1.0.19 ZIP is stale and must not be uploaded.

The new candidate pairs notification words on the same row, searches left of the detected suffix for long names, and rejects constant masks. White masks are now generated after template resizing, matching capture preprocessing. Prepared images are cached; high-resolution captures are normalized for recognition with returned coordinates mapped back. The compact rendered text variant comes from the failed-run reward only. All 42 crate tests pass, including the five synthetic long-notification display cases (1280x720, 1920x1080, 2560x1440, 3440x1440, and 3840x2160). These transformed images are not proof of different DPI settings or actual hardware support.

Missing confirmation now gets a second observation window. A still-visible, valid dialog can receive one verified Yes retry. A closed dialog is never clicked again at stale coordinates; the engine reacquires inventory and selects from fresh evidence, allowing at most two consecutive inventory retries before stopping unresolved. Unconfirmed attempts stay logged as Unknown. Missed-Yes and persistent-dialog simulations exercise both outcomes.

New candidate: `artifacts/crate-confirmation-fix-20260927/FischMacroCS.exe`. The bounded `--verify-crates` harness now supports up to 25 stacks and a maximum 600-second cancellation deadline. Sustained live acceptance of this candidate, full-suite revalidation, and real multi-PC/DPI validation remain outstanding. Old macro process was detected; waiting for it to be stopped before the sustained retest. Publication remains on hold.
