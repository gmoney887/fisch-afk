# AFK timeout investigation, 2026-09-29

Session 20260929_034450 ran 1.0.19-aquariumfix.1 from artifacts/aquarium-fix-20260928. Its implementation matches the aquarium fix published in v1.0.20. Session results: 2,310 confirmed catches, one unconfirmed attempt, one watchdog recovery. Aquarium completed successfully.

A 35-second reel stall triggered recovery; rod reset then lost hotbar recognition. Recovery remained active for about 2h48m. On gameplay return, the scene guard stopped after roughly three seconds against its prior view. The user reports Roblox's 20-minute idle disconnect. The heartbeat was only an F15 key pulse: Windows input delivery did not establish that Roblox recognized activity. No reconnect attempts were retained. The original timeout screen is unavailable, so its exact UI recognition failure and the cause of fishing initially stopping cannot be established retrospectively.

Changes:
- Replace F15 with a stationary right mouse pulse, move to a validated game coordinate before pressing, and release on interruption. No camera drag or character movement. Delivery is still reported as unconfirmed game acceptance.
- After at least five seconds of unavailable gameplay, permit one bounded fishing cycle when gameplay is actually recognized. Retain the original view comparison. Repeated waits cannot renew this allowance until a catch is confirmed.
- Preserve the first substantive recovery instead of the first routine scene suspicion, limit pinned evidence to half of the failure pool when competing with newer failures, and record recovery status/context every 30 seconds.

The old recording pinned an early scene-change window which consumed essentially the entire failure pool. Later timeout evidence was discarded. The preserved source recording remains under artifacts/timeout-20260929 and has not been rewritten.

A live idle-prevention test exceeding 20 minutes remains necessary. These changes do not establish the root cause of the initial fishing interruption or guarantee recovery from an unseen disconnect dialog.

Validation: 54 targeted recovery, input, recording, and worker replay tests passed, including simulated 26-minute idle scenarios. An earlier 28-test policy/input subset also passed. Self-contained candidate: artifacts/idle-fix-20260929/FischMacroCS.exe, informational version 1.0.20-idlefix.1. This is a local build, not a published release or a completed live idle test.
