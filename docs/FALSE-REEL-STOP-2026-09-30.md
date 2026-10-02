# Latest fishing stop: 2026-09-30 12:35 America/Chicago

Session: session_20260930_172914_139_97e4f1425344462daf2dbaa5d4035663. Released v1.0.21 from Downloads; not the aquariumfix.1 candidate. Preserved under artifacts/latest-stop-20260930.

Run lasted 368 seconds and recorded 10 confirmed catches, 1 confirmed failure, and 8 unknown outcomes. Aquarium closure and subsequent aquarium check both completed successfully. Final pause followed three recovery attempts without a confirmed catch.

At the end, decisions repeatedly reported Reeling / Searching for Needle, FishX=0, and a stationary bar at desktop X2165..2341. Recorded frame 3230 covers desktop (968,1013), size1504x273. It shows terrain, bright cyan coral, and rod-description text, with no reel UI. The detected bar coordinates coincide with bright cyan coral near the right edge. Full context frame3236 confirms no aquarium panel or reel UI. The terminal failure is a false reel-bar detection that survives rod resets, times out after35 seconds per cycle, and exhausts the recovery budget. The earlier aquarium close fix does not address this detector path.

No recording entries were dropped. This diagnosis is supported by retained frames and decisions; no live fix or validation performed in this turn.

Implemented recovery changes: luring now requires live reel evidence on two consecutive frames rather than accepting a bar candidate alone. While reeling, three continuous seconds of a detected bar without a fish needle releases the mouse and invokes the existing verified rod reset/recast path. Short occlusions reset when the needle returns; disappearing bars continue through outcome verification. Existing recovery budget remains bounded. A missing-needle-recovery event records the reason and candidate coordinates.

Validation: 31 detector/guard/reel regression tests passed; six related worker scenarios passed; new missing-needle worker scenario passed after correcting the synthetic test bar height to the detector's supported geometry. The worker test verifies rod toggles, resumed fishing and a confirmed catch before the normal35-second timeout. Candidate artifacts/recovery-fix-20260930 includes the earlier aquarium fix. Not published or live validated.
