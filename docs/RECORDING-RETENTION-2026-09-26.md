# Recording retention investigation — September 26, 2026

The user reports recent AFK sessions failing too quickly to collect useful evidence. The cause of those fishing failures is still unresolved.

Read-only inspection of the recordings currently retained on this PC found manual workflows, including zero-frame sessions ending with `Starting crate workflow recording`. The latest three actual crate recordings used app version 1.0.17.0, lasted 11.73, 59.83, and 184.86 seconds, and ended with `Roblox must be visible and focused with a valid capture.` They reported no dropped entries. These are manual workflow observations, not evidence of the cause of the earlier AFK failures.

The engine started an ordinary recording during manual startup, then immediately replaced it with a demonstration recording for crates. Both completed sessions consumed the recorder's session-count retention allowance. This unnecessarily accelerates eviction of older evidence; it does not prove which historical sessions were deleted or why.

Manual crate startup now creates a demonstration recording directly, preserving settings in its manifest and recording the workflow start as an event. A regression exercises a missing-template failure and checks that exactly one completed session retains the settings and crate outcome.

This is a source change, not a deployed AFK recovery fix. Next acceptance must identify the running binary, preserve the first failure's chronological evidence, and prove productive fishing after recovery. Existing recordings were not modified during this investigation.

Validation: the new targeted regression passed. A broader run of fishing replay, recording replay, and crate workflow tests aborted after 46 passes with native access violation `0xC0000005` in `OpenCvSharp.Internal.NativeMethods.imgproc_GaussianBlur`, called by `CrateVision.Match` → `FishingEngine.IsBagOpen` → `RecoverAndRestart`. This is an unresolved test-host crash, not established evidence of the user's live failure. Do not report the broader run as passing or deploy on that basis.

An isolated fishing replay rerun was stopped manually after remaining incomplete for several minutes. It reported 76 passes and no assertion failures before termination; it did not independently report the native access violation. Its final test-host-crash message resulted from our termination, not a second spontaneous crash. Partial results: `artifacts/recording-retention-check/recovery-check.trx`. The isolated suite remains incomplete.
