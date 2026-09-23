# Second-PC session fixes

The supplied recordings show real catch banners logged as unknown, repeated lure
timeouts, and the avatar already in water in retained late-session images. They do
not contain the moment of displacement. Companion effects remain a possible cause,
not a confirmed root cause.

Changes:

- Match the reviewed 1009-high window's catch-prefix rendering, preserving existing
  templates and the requirement for player catch text (not companion bonus text).
- Preserve two-frame catch evidence across the transition to post-catch verification.
- Search shake lettering across the whole viewport, verify several coarse candidates,
  and use an HSV lettering fallback for busy backgrounds. Click the detected position
  without the old 40-pixel inward offset.
- Detect aquarium navigation using both reviewed renderings and a blue-text mask
  insensitive to the scenery behind the translucent control. Reward confirmation
  and closure still use visual evidence; this recording contains no open aquarium.
- Stop after three unsuccessful recovery attempts. Only a confirmed catch or an
  explicit new session resets that allowance. Avoid blindly pressing Escape during
  recovery, which can open the game menu. Report attempts rather than successes.
- Sample context once per second. Learn three stable observations of peripheral
  landmarks outside the avatar/reel/UI regions; release macro inputs when three
  landmarks disappear and require persistent change before stopping. This detects
  a changed view, not swimming or world coordinates. Camera motion can also stop it;
  featureless scenes cannot arm it. No automatic walking or return-to-shore is sent.
- Pin the first position/recovery/death incident's 15-second lead-in and 5-second
  tail within the existing recording budget. Later unknown catches cannot evict it
  ahead of ordinary evidence. Record input coordinates, macro state and recovery
  context. The report dialog can select the first incident's context images.

New reviewed image assets come from the September 21–22 recordings. Catch fixtures
retain the notification area; the navigation fixture contains only the central
topbar and the shake fixture only the button. These are perception regression cases,
not proof of game input acceptance or companion behavior.

Live acceptance still requires testing the smaller window, full aquarium claim and
closure, shake buttons over moving effects, and scene changes with companions on/off.
The guard intentionally stops for review rather than attempting blind navigation.

Validation: Release build completed with zero warnings and errors. The complete
suite passed 429 tests, with one existing screenshot-dependent test skipped and
zero failures (430 total). This includes recorded-image perception cases, engine
input-release and recovery-budget replays, and first-incident recorder selection.
These fixes are included in v1.0.14. The portable ZIP includes the reviewed
aquarium workflow templates as well as the executable and shake template.
