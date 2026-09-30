# Aquarium followed by scene-guard stop

## Recorded failure

The 22:07:56 local session (session_20260930_030756_983_e243a180f14148fbaf4b080f503144d1) ran 1.0.20-afkfix.2. It visibly caught a Pelagic Cod, classified the result Unknown, successfully claimed aquarium rewards and verified panel closure, then paused about 0.48 seconds later for a changed fishing view. No recording entries were dropped. A second run also stopped after an unrecognized catch without another aquarium claim, showing that aquarium navigation alone was not the root cause.

Evidence is preserved locally under artifacts/aquarium-stop-20260929. Cropped frames 72 and 74 are portable regression fixtures, excluding player-name/chat regions.

## Causes and fixes

1. Catch template scaling assumed text dimensions track viewport height continuously. At 3424x1353, the rendered phrase retained the native size of a template reviewed at height 1369. Resampling that template caused a miss; its native match is 0.969. Matching now checks both native glyph size and the height-scaled candidate, retaining the existing 0.78 threshold and the actual player-catch prefix requirement. Existing negative companion/reward and loss fixtures remain part of validation.
2. Verified aquarium closure did not grant the scene guard a chance to resume fishing when the preceding catch was Unknown. Closure now permits one bounded fishing cycle using the existing nonrenewable recovery allowance. It does not count rewards as fish, renew catch statistics, or replace the saved scene reference. Unverified closure still blocks casting.
3. The old scenario runner could call a transition to Casting successful resumption before any cast input occurred. The new aquarium/scene scenarios require a real cast press after the workflow. With the closure allowance removed, the Unknown-catch scenario reproduces the reported scene-guard pause; this is an observed failing regression, not just a test of the implementation.

## Limits

This repairs the reproduced failures. It does not establish overnight reliability, all display/DPI combinations, or automatic return to a fishing location. Replay input is a test sink, not a live Roblox action.

## Completed validation

Full Release suite: 579 passed, one skipped, zero failures (580 total), 9m19s, artifacts/aquarium-fix-full.log. Both consecutive recorded Pelagic Cod frames are recognized. Both aquarium/scene worker regressions require an actual subsequent cast press; the Unknown-catch regression fails with the old closure behavior and passes with the fix. Existing negative catch, companion reward, loss, multi-resolution, and workflow checks passed in the full run.

Local optimized self-contained candidate: artifacts/afk-fix3-20260929/FischMacroCS.exe. Informational version: 1.0.20-afkfix.3+cafa0abfb46098f5c9a7b93dc065b9f0100d940a. All 22 external workflow template hashes match source. Executable SHA-256: 01956DF3F910F3EC2A56C8CB94370C4665EF1DCD84B9F97637DB3153FB2112AE. Not published or launched into a live run.
