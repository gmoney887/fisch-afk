# November 5 overnight gameplay failures

The monitored executable was `1.0.30-nov04stability+4914a79d1b3715aab323f5859ba098d0265ccae2`,
PID 16196, running from `publish-singlefile`. The previous `nov04cast` candidate
was never installed during the overnight checks. Passing its suite was not live
acceptance. Evidence is preserved under `artifacts/overnight-nov04`.

## Reproductions and changes

* Idle rod-stat text became a structural progress meter. With arbitrary scenery
  classified as a fish, this allowed false reel entry, skipped casts, blocked
  aquarium work, and held completed catches in Reeling until the 35-second
  timeout. The previously staged horizontal-edge-run requirement is retained.
  Six new overnight notification/idle fixtures reject this false meter in both
  the track and full-context detector. Reintroducing the old meter path makes
  those regressions fail (`artifacts/overnight-fixes/mutation/old-meter.trx`).
* Player catch text settled about 1% away from the reviewed template size.
  Raw correlations of .751-.777 missed otherwise clear Salmon, Barracuda and
  Yellowfin catches. Nearby raster sizes match the complete reviewed phrase
  at a stricter .85 threshold; no confidence threshold was lowered. The four
  positive image cases failed before this change; companion and idle negatives
  remain negative. Actual worker replays must count exactly once, release input,
  and dispatch a new cast without unknown outcomes or watchdog recovery.
* Ruinous Oath's slash occluded the needle, and the end-of-track outline was
  chosen instead: frame122050 reported fish2419 despite the visible marker near
  1720. The needle search excludes the end-arrow/outline area. A chronological
  ten-frame recording checks the visible stationary marker through the slash
  and ensuing bar movement. This test failed at122050 before the boundary fix.
* Nighttime blue scenery enclosed the aquarium navigation glyph. Absolute
  brightness masking also lost dim strokes. The existing blue matching path
  remains, with an independent local-contrast glyph fallback using the same
  confidence threshold. Night/day frames and a separate earlier night frame
  verify navigation identity; removing the label must remove the detection.
  Worker tests combine the overnight exterior scenes with previously recorded
  claim/empty-panel frames. They require open/claim/close and a verified claim
  timestamp, and must remain Unknown if the claim does not clear the balance.

## Evidence and test limits

Fixtures in `Fixtures/overnight-nov05` are byte-for-byte copies from the active
session `session_20261105_051105_847_8de46ee3cd9646dbb4c1ae614f096225`.
Full frames are3440x1369; track crops are1504x273 at(968,1013).
The worker replay's ordering/timing is controlled; the input frames themselves
are real recordings. Aquarium panel fixtures predate the overnight exterior
frames and are explicitly combined, not claimed to be one continuous recording.

The original suite lacked these lighting, notification-scale, and occlusion
examples. The existing first-cast fix was also absent from the running binary.
Tests now cover visible observations and the worker's subsequent cast/count/
claim behavior, but replay alone cannot prove the game accepted an input or
that live control stays centered. Full-suite, build identity, and live gameplay
results are recorded separately in `artifacts/overnight-fixes`.

Inventory counts include companion catches; player-named chat without
"perfectly" can also be a companion catch. Neither establishes a player catch
rate. Unknown outcomes are not confirmed losses. The almost-full inventory
warning requires an inventory decision, not fabricated catches or automatic
sale of the user's fish.

## Validation gate

The final focused run passed57 tests. The full suite passed974, failed0,
with1 existing private-screenshot skip (975total), in10m57s. Clean build:
0warnings,0errors. Report:
`artifacts/portable-validation-20261105-141829-883/portable-validation.trx`.
All440 source/fixture fingerprints remained unchanged throughout the gate.
Installed build: `1.0.30-nov05overnight`, SHA256
`44CD675CDC6C5159FDF15BD02E7F6A0E4C033424CAC6F81DBD543E84309EB203`.
The previous portable directory is preserved as
`publish-rollback-20261105-141829-883`.
Live validation remains pending until this installed executable is running;
the Windows desktop tool still fails during kernel initialization.
