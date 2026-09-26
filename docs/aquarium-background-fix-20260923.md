# Aquarium navigation over blue scenery

The v1.0.15 recording `session_20260923_031817_693_bd3540f8ad0a440a93bf1744c8d1038c`
shows another navigation failure at about 22:21:45–22:21:50 local time. The label is
visible over blue scenery; confidence remains roughly 0.60–0.72, below 0.94. No reward
inputs were sent, and the deferred retry policy correctly allowed fishing to continue.

The color mask introduced to remove translucent scenery fails when the scenery itself
shares the lettering's blue hue. This is distinct from the one-pixel scaling failure
seen over the lava environment.

Navigation now has a local-contrast fallback. Native OpenCV morphological top-hat
filtering on HSV brightness isolates thin lettering from the broader background.
The reviewed glyph template, existing confidence threshold, narrow scale search and
blue-color evidence are retained. The reproduced native-size match rises to 0.972.
The fallback applies only to blue navigation text; reward confirmation is unchanged.

Three cropped navigation strips from the actual failed interval are positive
regression cases. Replacement Shop/Menu labels and a blank blue button are negative
cases. A composed recorded-image workflow also verifies open, fresh claim and closure
starting from this navigation rendering. This is not a new live in-game claim test.

Validation: clean Release build (zero warnings/errors); full suite passed 446 tests
with one existing screenshot-dependent skip (447 total). The fix is local and has
not been published.
