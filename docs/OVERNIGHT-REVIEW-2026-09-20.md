# Overnight AFK evidence review — 2026-09-20

This is incident evidence, not a passing long-run acceptance test. No live input was sent during the review. Private metadata/journals and sampled images are saved under ignored `artifacts/overnight-review-2026-09-20/`.

## Completed sessions

| Session UTC start | Duration | Reported confirmed / Unknown / failed | Recoveries | Explicit stop source |
|---|---:|---:|---:|---|
| 2026-09-20 04:28:49 | 11h 48m 50s | 2138 / 15 / 0 | 2107 | Start/Stop control or toggle hotkey |
| 2026-09-20 17:08:54 | 1h 57m 44s | 17 / 1 / 0 | 1305 | Start/Stop control or toggle hotkey |

Session suffixes are `21fa82649a944a9e9a031ad8304263a3` and `0b8e2e9e1e3a465c968fbe340a0b7138`, respectively. Counters are application reports, not independently labeled catches. Both manifests report assembly version 1.0.11.0, initial viewport 3440x1369, 96 DPI, Visual shake, recording/AFK mode/heartbeat enabled, aquarium enabled and crates disabled. This is not evidence for the newly packaged 1.0.12 preview binary. The exact old binary hash is not established by these manifests.

Both workers remained present until explicit Stop, but fishing effectiveness failed. The overnight final recorded central ROI (frame252658, viewport2254x1353, rectangle708,406,838,541) visibly shows the Fisch WASTED death screen. Retained reconnect-attempt events exist; those events alone do not prove an actual server shutdown or successful rejoin.

The later session's frame8 shows the character on the boat edge with an E/Sit prompt. Frame9906 (~41m into the session) shows the character beside the boat in the water. The retained frames do not capture the causal transition. Later recovery messages repeatedly report missing cast bars. Retained input journals show F15 and Escape down edges, but no walking or jump down edges. The earlier event history has expired, so absence in retained logs cannot exonerate every historical input or establish physics as the cause. Click-to-move configuration, external input and boat motion remain unverified hypotheses.

## Recording and acceptance limits

The completed directories measured 148,264,334 and 147,796,971 bytes, with 278 and 348 retained images. Both report zero admission drops, but 1,607,997 and 23,036 journal entries expired. Zero drops is not complete evidence retention. The initial boat image survived, but the incident transition did not. More prioritized incident evidence is needed for displacement and repeated cast failures.

The overnight run exceeds the two-hour duration requirement but does not pass long-run acceptance: it became ineffective, has no sampled process memory time series, and lacks continuous causal evidence. It also does not isolate heartbeat efficacy because normal fishing generated input. No physical-hotkey, full rejoin, all-rod or multi-DPI acceptance gap is closed by these sessions.

## New offline regression scope

The reviewed death ROI is a required fixture. Reconnect and Continue detectors must reject it. Actual worker cases `death-wait` and `death-stop` place the unmodified crop at its recorded origin with controlled dark padding outside it. They verify repeated waiting with Start retained, no gameplay input or invented outcomes, cancellation with release, and a normal catch/next cast only after controlled gameplay returns. This is not a respawn implementation or a claim that the death screen clears automatically.

All core worker scenarios additionally reject WASD, arrow and Space down edges. Optional workflow scenarios are excluded because their text-entry tests legitimately use letter keys; these tests do not model click-to-move or hardware/user inputs.

Next acceptance work: independently identify death/position loss, preserve the preceding incident context without exhausting bounded storage, and validate recovery to an actually fishable location. Current hotbar presence is insufficient to distinguish fishable gameplay from standing in water. Automatic return to a location remains unimplemented.

## Follow-up: independent incident journal

The recorder now duplicates low-volume events and keyboard down/up edges into four rotating `incidents*.jsonl` segments of 512 KiB each. Frame records, decisions, cast observations and routine mouse/release events remain in the existing four 4 MiB segments. Routine churn therefore cannot evict incident entries. This is still bounded retention, not a complete history: a recovery storm can expire older incident entries. `completed.json` and `summary.txt` report `IncidentJournalEntriesExpired` separately from routine expiry and admission drops. The final JSON snapshots expiry after writing the completion marker, including any rotation caused by that marker.

The two MiB incident allowance fits inside the existing 224 MiB session reservation (64 MiB rolling frames, 128 MiB preserved frames and 16 MiB routine journals, plus metadata headroom). Diagnostic exports include all four incident segments with the same text sanitization as existing journals. This does not change queue admission, image retention or the currently running executable. Missing historical footage cannot be reconstructed.

New regressions feed 10,000 routine entries after early recovery/keyboard evidence and require that evidence to survive, then separately feed 10,000 recovery events and require bounded file sizes, valid JSON, newest-event retention and explicit expiry. Recorder integration verifies outcome/keyboard/completion persistence; diagnostic export verifies all incident segments survive packaging. Existing stalled-writer tests still verify nonblocking producers, reported admission drops, completion and restart.

Full verification after this change: 382 passed, zero failed, one existing missing-private-fixture skip in 3m20s. Coverage floors passed: 57.97% lines / 49.49% branches overall; FishingEngine 69.39% / 55.54%; FlightRecorder 94.15% / 88.68%; SessionEventJournal 94.74% / 100%. [Coverage report](COVERAGE-2026-09-20.md) retains the exact source report path and SHA256. TRX: `artifacts/overnight-regression-coverage/d63f0f0bf7b1435796abda75137b8181/tests.trx`. These results supersede the preceding focused test counts for current source validation. No new live trial or release was performed.

## Follow-up: distinguish death from loading

`DeathScreenDetector` requires both the reviewed death logo and WASTED title at their recorded relative geometry, with 0.90 normalized correlation after symmetric smoothing. Both identities are sampled from the unmodified death ROI embedded in the app. It returns a state, never a click target. Positives cover four scaled viewport heights; removing either identity or moving the title out of position must reject, as must Continue, disconnect, update, catch and shake screenshots. The positive is the template's source capture; this is a regression against the observed incident, not independent detector accuracy or alternate-death-screen validation.

Recovery now reports "Character died" and waits with Start retained. There is no blind respawn click or invented return-to-position behavior. A continuous observation of the death screen emits one `death-detected` event into the incident journal and prioritizes its existing rolling pre-event frames plus the next five seconds under the current bounded image policy. Starting a fresh run resets deduplication. Readiness reports death distinctly and remains failed without equipment input on the reviewed screen. The user must still restore a fishable position after respawn; visible hotbar detection does not prove that location.

The death wait/Stop worker cases now require the explanatory telemetry and exactly one recorded death event, as well as all earlier input/cancellation/catch assertions. Focused integration: 68 passed, zero failed/skipped (`artifacts/overnight-regression-tests/death-recovery-integration.trx`). This overlaps detector/recorder/readiness tests and must not be added to full-suite totals.

Full verification after death identification: 392 passed, zero failed, one existing missing-private-fixture skip in 3m43s. Coverage floors passed at 58.33% lines / 49.68% branches overall. [Exact coverage snapshot](COVERAGE-DEATH-2026-09-20.md); TRX: `artifacts/death-recovery-coverage/56b4a8bef30e40e9984295819654dcc4/tests.trx`. Production changes have not been launched or released. Live death recognition and safe restoration to fishing remain unaccepted.

Validation: 21 focused detector/death-worker cases passed with zero failures/skips (`artifacts/overnight-regression-tests/death-screen.trx`). All 72 actual-worker cases passed with zero failures/skips in 3m10s (`worker-movement.trx` in the same directory), including the new movement-key invariant. The focused and worker runs overlap; do not add their totals. No production code changed, no new full-suite coverage percentage was collected, and no live acceptance gap is declared closed. `git diff --check` passed.
