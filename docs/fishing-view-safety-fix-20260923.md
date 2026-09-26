# Fishing view false stop in 1.0.15

The September 23 morning session ran for roughly 15 minutes and confirmed 96 catches without recovery attempts. Its final interruption came from the fishing-view guard. The recording shows a fresh active reel when the guard stopped the engine. A visual scene change alone does not establish that the player can no longer fish.

Previously, three unmatched scene observations triggered an unconditional stop. The confirmation loop also released inputs and delayed the active reel controller.

The guard now treats a changed view as suspicion and checks the live reel using a separate vision processor. It continues controlling a visually confirmed reel while waiting for a confirmed catch. A confirmed catch clears the suspicion and relearns the scene. Reel visibility alone cannot reset the baseline or defer the stop indefinitely: continuation is bounded by the configured reel timeout. A missing reel gets a short catch-finalization grace period; persistent changed views without fishing evidence still stop and release inputs. While unresolved and without a visible reel, fresh casting and luring inputs are withheld.

This is a fishing-progress safety check, not proof of physical position. A player who moves but can still catch fish may continue. The macro does not attempt blind movement back to shore.

Regression coverage includes the actual reel crop from the morning interruption, an engine replay that finishes a catch across a changed scene and resumes casting, the existing displacement/input-release replay, and policy tests for lost/frozen reels, timing boundaries, and return to the original view. The aquarium navigation background fix remains included in the local working tree.

Validation: Release build passed with zero warnings/errors. Full suite passed 453 tests with one existing screenshot-dependent skip (454 total); report: `artifacts/view-safety-tests/view-safety.trx`. This fix has not been published or validated in a new live game session.

## Follow-up: view change immediately after a successful catch

Local build 1.0.16 session `session_20260923_230338_587_6cd6199bd29545c5b4daece3a2825667` confirmed four catches. Catch four finalized at 46.59 seconds; the next view suspicion arose in PostCatch about 0.77 seconds later. The engine entered Casting, withheld the cast because no reel was visible, and stopped roughly four seconds after the catch. It was blocking the opportunity to obtain fresh fishing evidence. The original replay ended on the first return to Casting and missed this sequence.

Every confirmed catch now grants a bounded next-cycle window, covering the configured post-catch delay, cast hold, post-cast delay, lure timeout, and reel timeout. A suspicion arising in this window does not block fresh casts or stop the engine. Only a confirmed catch renews the window; stable scenes and live-reel detections do not. Startup/reset clears it. Once expired, the existing persistent-change policy applies. This intentionally permits an attempted cycle after a recent success rather than treating missing reel UI between catches as loss of fishing capability.

The expanded engine replay fails on the original 1.0.16 logic and requires a second confirmed catch after a post-catch scene change. Policy regressions also check expiry with absent/frozen reel evidence and ensure stable frames cannot renew the deadline.
