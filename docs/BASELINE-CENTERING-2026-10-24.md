# Isolated 1.0.26 centering experiment — October 24

This checkout is `codex/centering-baseline-126`, initially exact tagv1.0.26 at a274d18. Main post126 work is preserved **as-is** on `codex/archive-post-126-20261024-625`, commite583dedc6ebde01c615bdb0dbe98e0fa8626c070. Do not rewrite or merge over that archive. Recordings/packages/frozen sources remain in the main ignored artifacts and dist folders. User prohibits all PC/app control; only offline work is authorized. No replacement or publication.

The latest live625 session failed centering despite Fast1511/1511 and Full1954passed/1reviewed optional skip. Actual retained frame158 took4889.66ms vision;3153139.35ms. Frozen625 replay1585071.84ms without tracing,3152483.73ms. Preserved legacy126 assembly processed identical crops in7.19/41.23ms. These individual offline comparisons establish a latency regression; they do not establish live centering or good progress-meter validation.

This experiment keeps the126 detector unchanged. Its color/viewport-dependent detection remains migration debt and cannot establish final all-rod acceptance. Source625 tests review bounds/width/needle and current meter on four fresh production observations. 150passes. 158/315find the reviewed bar/needle quickly but fail current-meter assertions: the visible tilted meter must be supported before this prototype can be called ready. Do not remove those assertions or certify it from speed alone. Fixtures, origins, source clock and limits are in Fixtures/PROVENANCE.md and RegressionCatalog.json.

## Implemented controller changes

- Timestamp at capture before readback. Compute motion between capture-stage times. An invalid velocity interval resets motion without discarding fresh current positions; images older than250ms still release input.
- Elapsed held-time pulses use actual successfully guarded input edges. Pulse balance survives ordinary velocity resets. A rejected hardware edge does not update `_isMouseDown` or motion input history.
- The small ReelInputMotion component integrates input edges within each capture interval. It corrects average secant velocity into a control estimate and accounts for capture age. Observed coordinates remain separate in telemetry.
- Existing archived ReelActuationPlan and pulse components are selectively reused. Short16ms commands execute on the original guarded coordinator while a persistent read-only capture/vision worker runs. A command expires250ms after its fresh decision, is reset on missing body/needle/meter or state transition, and validates focus/client/cancellation on every poll. Estimates never create observations or outcomes. Native work is joined before state/frame teardown.
- RecordingFrameSource retains public before/after validation; its read-only capture path is only dispatched after coordinator validation and checked again on join. Optional cooperative capture supports deterministic test clocks. Native frame waits use the archived bounded timer helper.

## Evidence and limits

The original126 fresh-input reproduction failed all70/120/200ms cases (`../baseline-slow-before`). The timing correction passed all three plus existing Oath/Nate pursuit cases (`../baseline-slow-after`,5passes). Elapsed-time pulse checks plus those cases passed15 (`../baseline-pulses`).

Independent synthetic feedback initially failed after timing/pulse changes; original failed TRXs and CSVs are preserved. Initial harness bugs were corrected without changing geometry or centering bounds: physics starts at live entry, capture association is limited to the primary region, and assertions collect outside the engine callback so worker recovery cannot swallow them. The model does **not** validate acquisition. It uses a constant-acceleration plant driven by actual guarded edges, synthetic capture delay, and unchanged native detection; native cost is measured separately. No raw recorded timing was changed. Final bounds remain mean<=4.032px,max<=10.08px,>=90%of a full ten-second scored interval within5.04px, for168px AND1111px controls.

The first passing Seraphic experiment (`../baseline-actuation3`,12passes) had:

|Capture delay|Mean absolute error|Maximum|Within5.04px|
|---|---:|---:|---:|
|70ms|2.695px|3.839px|100%|
|120ms|2.870px|4.324px|100%|
|200ms|2.814px|4.729px|100%|
|40/120/70/210ms|2.688px|4.755px|100%|

The later expanded `../baseline626-focused` has79total:77passes,2current-meter failures above,0skips/errors/aborts. Includes every shipped profile at all four cadences with narrow168px and wide1111px bodies and the two original Oath/Nate pursuit assertions. No existing assertion was relaxed. Profile cases are fixed-target simulations; moving fish, raster jitter, force mismatch, hazards and in-game Perfect awards remain unproven.

Affected engine/input group `../baseline-engine` completed133cases:131passed,2action-label failures (RIGHT absent from a correct rightward command). Directional labels were corrected in production; the two unchanged tests pass in baseline626-focused. No Full/Fast acceptance is claimed for this prototype. A competing focused build attempted before that runner exited was rejected by the DLL lock; `../baseline-profile-feedback.log` is a failed build, not a test result. Native work must stay sequential; class concurrency is now bounded to4.

Next: review the pending native-boundary/recorder results, fix current tilted-meter evidence using current geometry without color or viewport tables, then test moving/jitter/hazard feedback and preserved production families. Carry forward the required Fast/Full catalog coverage deliberately before packaging; the older126 suite alone cannot replace those requirements. No validated new local package exists. Goal50independently observed Perfect awards under Frightfest is incomplete; ordinary catches/simulations/labels do not count.

Native boundary follow-up: `../baseline626-boundaries2`19/19passed,0failures/errors/skips,17s. Includes a deliberately blocked, noncooperative native frame after four fresh inputs: Stop, focus loss and resize all release the actual guarded mouse-up edge **before** capture is unblocked. First boundary run had3harness timeouts because it waited for hardware.ReleaseAll, which GameplayInput does not invoke; the corrected harness observes actual SendHardwareMouseUp. Original deadlines are unchanged and the failed run is retained. Recorder and original pursuit/input cases passed alongside it. A final stale-evidence reset/recheck is included in the following combined frozen-source run; assess its actual final TRX rather than importing earlier counts.

Final frozen-runtime combined result: `../baseline626-combined/baseline626-combined.trx`93total,91passed,2failed,0errors/timeouts/aborts/skips,65seconds. The only failures are the original158/315 current-meter assertions. Seven runtime-file hashes match the pre-run freeze. All56profile/width/cadence feedback cases passed; worst mean3.065px,maximum5.225px,minimum time within5.04px97.93%. Three blocked-native stop/focus/resize cases and recorder checks pass on this same snapshot. Retain the red meter cases and do not package/replace. No running test handle remains from this experiment. The archived main worktree remains clean at e583ded.
