# Recorded loss and misleading success rate

The last five completed attempts in session `session_20260928_002026_018_9b31c04ab81a47dbbdc1148819dfa842` were four confirmed catches and one Unknown at 19:22:02 local. Frame 840 explicitly says the catch streak of 3,557 ended. The cumulative counters ended at 53 catches, zero recorded failures, one Unknown. The old wins/(wins+failures) formula excluded that Unknown and displayed 100%.

The engine supplied only success observations to its outcome tracker. The new detector matches both fixed gold phrases around the variable streak count. Two consecutive observations confirm failure; the notification must disappear before another loss can be attributed to it. Failure recognition precedes reel re-entry, because controls may remain visible behind the loss notification. The worker records a confirmed loss once and resets the streak.

The dashboard now calls its metric Catch Success: confirmed catches divided by all completed attempts, including Unknown. Unknown remains separately classified rather than being asserted as a loss. No attempts display a dash. One-decimal formatting cannot round an imperfect session to 100%. Counts and interpretation appear in the tooltip. For the recorded totals the corrected value is 98.1%; the last five attempts are 80%.

The failing reel also showed a dim red bar and a red diagonal effect over its progress fill. Recorded frames reproduce a missing control target with the prior detector. The guarded dim-bar color range now includes that red rendering, and a short horizontal closing operation joins progress fill split by the effect. Existing no-progress/scenery negatives remain tests. The fishing input path now uses hardware input alone rather than adding duplicate window messages. A verified bar outside the fish target which fails to respond to a right hold for 450 ms permits a release-and-press retry, with a one-second cooldown and at most two retries per reel. Retries are recorded. This is bounded dropped-input recovery, not an assumption that every opposing movement means reversed controls.

The exact reason the initial held command did not move the bar toward the fish cannot be established retrospectively from screenshots and commanded input alone. No claim is made that every future fish loss is prevented. Recorded-frame tests, worker replay, and the full regression suite validate the changes; a new live fishing run remains necessary to assess actual input response.

Original evidence is preserved locally under `artifacts/last-five-20260927/`. Historical recordings are not rewritten. Existing running binaries are not patched in place.

Regression testing also reproduced a native GaussianBlur access violation in shared workflow recognition. Its 3x3 smoothing now uses a managed byte-pixel implementation with separate output storage and reflect-101 borders. The release candidate records an informational build version and executable path in each new recording manifest.

Final validation: Release regression suite passed 554 tests, with zero failures and one existing local-fixture skip (555 total), in 6m46s. Results: artifacts/loss-statistics-tests/loss-statistics-final2.trx. Local self-contained candidate: artifacts/loss-tracking-fix-20260927/FischMacroCS.exe, informational version 1.0.19-lossfix.1. Live validation remains outstanding.
