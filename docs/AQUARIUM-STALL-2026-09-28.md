# Aquarium stall, 2026-09-28

Recorded session 20260928_233009 ran the local 1.0.19-lossfix.1 candidate. At frame 31779 aquarium closure was unconfirmed. Cleanup then repeated about every five seconds until queued stop. The preserved final frame shows an open panel with unclaimed rewards.

Replay of frame 38013 reproduced claim confidence 0.998 and close confidence 0.694 (required 0.92). The close template included changing scenery around the red X. Close detection now matches red glyphs within the existing restricted search region, ignoring background color. The saved regression fixture contains only claim/balance and close regions; other pixels are black.

Each close operation allows three freshly verified clicks at least one second apart. Claim allows one retry after an unconfirmed result. Cleanup now uses one hardware input path. Loss of focus and cancellation still interrupt input; missing visual evidence never triggers a guessed click. Live game acceptance remains to be checked.

Validation: 42 aquarium recognition/workflow tests and 11 aquarium worker replay scenarios passed. Close-button tests cover synthetic 720p, 1080p, 1353-high, and 4K scaling, missed clicks, permanently stuck controls, and red scenery negatives. Local candidate: artifacts/aquarium-fix-20260928/FischMacroCS.exe (1.0.19-aquariumfix.1). This candidate has not been published or live-validated.
