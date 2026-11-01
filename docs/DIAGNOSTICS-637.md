# AGY diagnostics local candidate 637

Baseline: AGY v1.0.27, `4f96f456b4f9a8f9f349780f2e6dc4936d5d4912`.
Local informational version: `1.0.27-diagnostics.20261025.637`.

Reeling, vision, capture cadence, gameplay input, rod profiles and settings are
unchanged. The regression script rejects changes outside this diagnostics port
and separately checks the capture adapter embedded in FlightRecorder.cs.
No running application is replaced or launched by this work.

## Changes

- Decision journals retain existing observed coordinates, controller estimates,
  observation age, signed center error and error divided by measured bar width.
  Missing observations remain null. This does not establish a Perfect award.
- Existing failure/recovery events and a stop request during Reeling preserve a
  bounded incident in the recordings/incident-archive directory. Routine catches
  do not generate clips. Stopping after the reel has ended does not trigger this
  preservation; the normal rolling session remains available.
- Each incident requests five seconds before and three seconds after its first
  trigger, at most 48 sampled originals (ROI interval at least 250 ms; one full
  client context before and one after), up to 32 MiB. The archive holds at most
  64 incidents and 512 MiB. Existing clips are never automatically evicted.
- Source build, regions/viewport, chronological events, guarded input events,
  original frame SHA256 hashes and missing/dropped/truncated evidence are retained.
  Interrupted windows are marked incomplete; every runtime incident is Unreviewed.
- Preserved incidents appear in the existing Share diagnostics list. Review and
  select/mask images before explicit export or submission, as with normal sessions.
- Archive quota/write warnings are separate from actual recorder failure. Rolling
  recording continues, and the next successfully admitted clip clears the warning.

These are sampled incident snapshots, not continuous video. No new image capture
is added to the reel loop. Copies, hashes and archive writes run on the existing
background recorder writer. A decision's FrameId can refer to the most recently
sampled frame, rather than the precise observation that produced that decision.
Existing telemetry timing semantics are retained; no new capture timestamp is
invented. Preserved input events only describe edges already logged by AGY.

## Validation

Run `scripts/Test-Regressions.ps1 -Tier Fast`, then `-Tier Full` on the frozen
source. Tests exercise the actual writer, retention, quota recovery, blocked
archive, observed/control separation, interrupted stop, replay and export.
Generated temporary images test storage mechanics; they are not additional game
attempts. No personal recording fixtures are added. Final counts and package
checks belong in the external validation report for the exact committed snapshot.
Passing these gates does not establish a live catch rate or centering improvement.
Portable assets/version/content are checked without launching a second instance;
interactive portable startup remains for the user's manual trial.
