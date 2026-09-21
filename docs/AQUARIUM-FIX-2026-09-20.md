# Aquarium automatic claim implementation

The running app attempted the configured 15-minute aquarium check but had no
recognition assets. It returned Unknown and skipped remaining automatic checks
for that session. The build now includes the four reviewed aquarium templates.

The workflow opens Aquariums, checks the Profit panel, claims when the balance is
not already zero, verifies the balance clears, and closes the panel. An empty
balance closes normally and advances LastAquariumCheckUtc without advancing
LastAquariumClaimUtc. Unconfirmed claims attempt a visually verified panel close;
Stop or focus loss prevents further clicks. Failed workflows retain the existing
session skip behavior and expose the evidence in the standalone claim dialog.

Scheduling uses the persisted last successful check (or earlier-version successful
claim), so restarting no longer starts a new full waiting interval. With no saved
check, the first safe post-catch boundary is due immediately. Subsequent intervals
use monotonic elapsed time; the configured minimum remains five minutes.

Validation includes recorded successful/empty/failed claim and failed-close
sequences, cancellation, stale reward notifications, engine input and persisted
timestamps, scheduling, and existing optional-workflow regression cases.
Geometry tests derive 1920 x 1080 and 3440 x 1369 views from the same recorded PC;
they are not independent live captures or evidence of universal layout support.

Live execution of this updated workflow remains unverified: computer-use app
inspection timed out waiting for approval. This is a local fix, not a release.

Result: all 35 targeted tests passed. A standalone win-x64 Release build was
created in ignored `publish-aquarium-fix/`; all four published template hashes
match their source assets. The currently running app was not replaced.
