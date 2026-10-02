# Aquarium close missed behind world text

Released v1.0.21 session_20260930_123055_602_6e297351b60f49c99b091edcde7c6313 ended after 4h36m50s and 1,762 catches recorded in that session. Aquarium navigation and claim controls were recognized. The panel remained open; six cleanup attempts ended in the explicit cleanup limit at 12:07:40 America/Chicago.

Preserved evidence: artifacts/aquarium-overlay-20260930. Frame 140293 shows the bright red close X overlapping dimmer red world text. The prior red-mask matcher scored the actual X 0.8968497, below the existing 0.92 threshold. The new cropped/masked regression fixture reproduces that failure without player names or chat.

The detector now tries an additional bright-red mask when the original mask fails. Both candidates use the same shape-matching threshold and bounded close-button search region. This separates the UI glyph from dimmer world text without requiring a new resolution-specific template or lowering confidence. The original mask remains available for dim UI variants.

Tests cover the recorded overlap at heights 720, 1080, 1353, 1369, and 2160; actual close-click followed by verified absence; background text with the close X removed; solid red scenery; and prior aquarium workflow fixtures. Scaled fixtures test image-transform behavior, not independent physical displays. Cleanup failures now record best close confidence, click count, and whether the claim control was seen.

Local candidate: artifacts/aquarium-fix-20260930/FischMacroCS.exe, informational version 1.0.21-aquariumfix.1. Live operation and publication have not been performed.

Validation: 53 targeted Release tests passed, zero failed or skipped, including aquarium workflow and worker recovery scenarios. Candidate publish succeeded; all 22 external workflow template hashes matched their source files. This is replay/build validation, not an overnight live test.
