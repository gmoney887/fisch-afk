# Aquarium, pinned dashboard and camera regressions

The v1.0.14 session `session_20260923_013212_489_49b53d6974e0448ba99a09eebfd7a0dd`
exposed three gaps in verification:

- The aquarium navigation button was visible, but matching a 1353-reference template
  rescaled to 1369 scored 0.874; its native-sized rendering scored 0.950. Tests had
  resized the screenshot and template together, concealing this rasterization mismatch.
- AFK Performance explicitly suppressed the saved Pin on top setting. Full-window
  scene capture also treated the dashboard itself as an obstruction. Pure geometry
  tests did not exercise desktop composition or physical input targeting.
- Fixed scene tiles interpreted the normal fishing camera zoom as displacement.
  Synthetic unrelated-scene tests did not cover actual fishing camera transitions.

Corrections:

- Aquarium controls search a narrow +/-2% scale neighborhood without reducing their
  existing confidence thresholds. An untouched, absent aquarium UI defers retry with
  1, 2, 4, 8, then 15 minute backoff while fishing continues. Missing assets and
  uncertain UI after input retain conservative recovery behavior. Claim success still
  requires a fresh zero balance followed by visual closure.
- Pin on top is independent of AFK Performance. The dashboard registers Windows
  capture exclusion; visibility validation only exempts that registered window while
  the OS reports full exclusion. Other windows remain blockers. Before a game press
  under the dashboard, it temporarily yields its position in the window stack and is
  restored on release. A different window covering the target cancels the press.
- The scene guard compares textured grayscale landmarks at a consistent centered
  zoom, retaining the original baseline. Translation and unrelated scenes still
  trigger the persistent-change guard. This remains a view safeguard, not world-position
  tracking; featureless scenery cannot arm it, and large camera rotations may stop it.

New perception fixtures contain only the navigation strip or masked peripheral scenery
from the failed session. They cover both directions of the recorded reel/cast zoom.

A local Windows integration check used two temporary WPF windows: ordinary overlap
was rejected, the excluded blue dashboard remained visible while BitBlt captured the
red scene behind it, and the dashboard yielded/restored physical click targeting.
No game inputs were injected. Windows API reference:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity

Repeat that check with `scripts/verify-capture-overlay.ps1` on an interactive Windows
desktop. It creates and closes its own small test windows, restores their state in a
finally block, and sends no clicks. The release helper runs it before packaging, and
now copies the complete published Assets directory instead of only the shake template.

The full aquarium reward flow is covered by previously reviewed open/claim/close image
sequences. A new live claim on the user's game has not been performed. These changes
are included in v1.0.15.

Final validation: Release build completed with zero warnings/errors; the full suite
passed 439 tests with one existing screenshot-dependent skip (440 total). The Windows
integration script passed separately. The unchanged persistent-displacement engine
replay also caught an overly permissive two-landmark match during development; the
final guard requires three agreeing landmarks at every accepted zoom.
