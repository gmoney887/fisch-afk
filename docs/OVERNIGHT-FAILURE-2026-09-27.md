# Overnight failure: September 27, 2026

The user reported only 106 catches overnight. The latest retained fishing session explains the count and the automatic stop. This is a failed overnight acceptance run.

## Preserved evidence

Local, ignored evidence is copied under `artifacts/overnight-failure-20260927/`, including the complete retained session `session_20260927_045337_445_ab17c32056af4f80bf2aecfe7694f375`, macro event log, and extracted `timeline.json`. Original recordings were not changed. Do not publish raw full-context frames; they include player names and chat indicators.

- Manifest version: 1.0.18.0; 3440 × 1369 viewport; AFK Performance, recording, watchdog, and anti-idle enabled.
- Start: September 26, 11:53:37 PM America/Chicago (September 27 04:53:37 UTC).
- Starting counter: 42 catches. Session delta: 64 confirmed, 8 Unknown, zero confirmed failures, 9 recoveries. Thus 106 displayed catches, with 9 cumulative Unknown including one at startup.
- Stop: September 27, 12:14:46 AM local, after approximately 21 minutes 9 seconds. Recording continued for a five-second tail; total recording duration 1,274.40 seconds.
- Exact reason: `Fishing view changed persistently without confirmed fishing progress. Check the player position and camera before restarting.` Stop source: `Automation interruption`.
- Recorder reported 11,410 total recorded frames, 100 dropped entries, 69,299 routine journal entries expired, and zero incident journal entries expired. Total recorded frames is not a count of retained frames.

## Lead-up and visual evidence

The last counted catch (106) was at session +1,145.2 seconds. Subsequently the engine repeatedly transitioned from Reeling to PostCatch (`Verifying catch outcome`), then back to Reeling (`Existing reel detected; skipping cast/re-equip`). It never finalized another catch before the guard stopped it at +1,269.39 seconds.

Retained `frame_00011406.png` (+1,269.27 seconds), immediately before the stop, visibly contains a real reel bar, fish needle, and partially filled progress meter. The full-context tail frame `frame_00011411.png` (+1,273.75 seconds) shows a player catch banner for a Blessed Black Scabbardfish at 87.2 kg. This independently shows an active fishing sequence at shutdown; the counter alone missed its eventual result. It does not prove every preceding reel re-entry was correct or every reported catch was independently verified.

The camera orientation differs between the early and final retained full frames. This is not proof of displacement, death, or an unfishable location. The evidence does not support a disconnect or native crash as the immediate cause of this stop.

## Code path and next correction

`FishingViewSafety.Observe` can stop a persistently changed view despite live reel evidence after the reel timeout, once the confirmed-catch grace period expires. `CheckFishingView` raises `FishingSafetyException`, causing the worker to stop. Meanwhile, PostCatch resumes a detected reel before finalizing the catch, explaining the recorded transition loop but not yet its underlying detection/timing cause.

Reproduce this sequence using the retained reel frames before changing thresholds. The correction must distinguish live reel progress from a stalled or falsely detected reel, preserve catch finalization, and retain position-loss protection. Do not simply disable the scene guard or claim an overnight fix from this diagnosis.

At inspection, the running executable was `artifacts/reward-reliability-fix/FischMacroCS.exe`, not the previously prepared `artifacts/overnight-20260926/FischMacroCS.exe`. Both can report 1.0.18.0; the session manifest does not identify an executable hash. The current tree contains additional uncommitted reward-workflow changes, which were left intact. No app restart, gameplay input, or code change was performed during this investigation.
