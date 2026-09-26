# Cyan reeling failure

The September 26 15:32:23 UTC recording (1.0.17.0) has three unknown outcomes and
no confirmed catches. The active reel has a cyan control bar. The vision pipeline
tracks its slate needle and white progress meter but rejects the bar color, so the
controller releases input with "Bar Disappeared / Concluding...". Existing tilted
bar and view-safety changes do not resolve this color mismatch.

Added a separate cyan-mask fallback after ordinary and dim-bar detection. It requires
independent needle and progress evidence, a bounded solid rectangle in the reel
region, and an unambiguous candidate. Native OpenCV morphology bridges the needle
through the bar. The fallback retains the default needle theme and runs after theme
locking too, allowing the appearance to change during a reel.

The two original screenshots fail bar detection before the fix and pass afterward.
Tests also remove the needle/progress independently, exercise repeated automatic
theme locking, and run a worker replay that must send reeling presses, confirm a
catch, and start the next cycle. The screenshots do not establish live input
acceptance or support for every future rod.

For broader rod support, the next architectural step is geometry-led detection of
track/bar/needle/progress, learning appearance only from temporally confirmed reel
shapes and reacquiring it during effects. Rod response should likewise be estimated
from observed hold/release motion with bounded adaptation. Validate against recorded
sequences across rods, resolutions, lighting and misleading scenery before replacing
the existing masks. The current change does not automatically support unknown colors.

Validation: Release solution build passed with zero warnings/errors. Full suite:
464 passed, one existing screenshot-dependent skip, zero failures (465 total).
Report: `artifacts/cyan-reeling-tests/cyan-reeling.trx`. Local executable:
`bin/Release/net10.0-windows/FischMacroCS.exe`. Not published or live-game validated.
