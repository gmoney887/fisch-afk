# First-PC passive demonstrations

These are manually demonstrated workflows, not unattended automation trials. Raw recordings and review labels remain local under ignored `artifacts/live-review/`; originals were preserved. All sessions used a 3424 x 1353 viewport at 96 DPI. Keep the sessions from this PC together when partitioning evaluation data.

| Session suffix | Retained frames | Duration | Recorded termination |
| --- | ---: | ---: | --- |
| 190214_817_e8156312e4e64b15a6cb675af27e9905 | 10 | 11.34 s | Focus loss |
| 190519_705_1b604b6d7aeb4bcfac19dfcd326bd8da | 39 | 42.52 s | Focus loss |
| 190602_344_ee75fdf86e314cce8a12f5d2d8ad3e0a | 3 | 2.34 s | Stopped |

No dropped entries were reported. The final short session supports the user's End-stop check in passive mode. It does not measure release latency with a held gameplay input. Both reward outcomes remain Unknown in the application's session statistics, as expected for passive observation.

## Aquarium

Frames 4–5 of the first session show unclaimed rewards changing from 5K C$ / 50.8K XP to zero, together with a redeemed-rewards notification. Later frames retain that notification after closing the aquarium: it must not validate a subsequent claim. Frame 3 contains an unrelated companion catch banner.

## Crates

Reviewed frames of the 190519 session:

- Frame 5: equipment search contains `carp`, with fish results.
- Frame 9: `crates` produces no results. This is **not empty-inventory evidence**: frames 11–12 show many items when searching `crate`.
- Frames 13–14: Open Crates modal for Carbon Crate, Yes / amount / No controls, quantity 1. No Max button is visible.
- Frame 21: Carbon Crate quantity 2. The automation must validate the requested quantity instead of assuming a default or selecting an invented Max control.
- Frame 22: modal closed; companion Porgy banner appears. Most right-edge reward text is covered by the macro window. Outcome remains Unknown for detector validation.
- Frame 33: Quality Bait Crate modal; the amount field displays its placeholder. Do not assume it contains a validated number.
- Frame 34: modal closed, interaction prompt remains. Closing a modal alone is not proof of a reward.
- Frames 36–37: companion Yellowfin Tuna banner, with partially occluded bait reward text on the right. Do not classify the companion banner as a fishing catch or crate confirmation.

The bag is near the bottom center, not at the existing `.298` height search location. Selecting an item and activating it may be separate steps; one-second sampling does not resolve all intervening inputs. The current `crate-max`, reward-dismissal, equipment geometry, and item-to-dialog contracts are unvalidated and inconsistent with this demonstration. Do not enable unattended crate automation merely by adding templates to those contracts.

The topmost macro window covers the right side and includes a smaller live preview of the game. Future reward captures must leave reward text unobstructed; template matching must exclude application overlays and their recursive previews. No verified empty inventory was demonstrated.

## Next evidence

The unobstructed crate reward capture was subsequently obtained (see 210223 below). Obtain a successful short actual fishing trial with reward automation and experimental adaptation disabled, including End while input is held and explicit resume after focus loss. Second-PC recordings, matched throughput comparisons, and both two-hour unattended trials remain outstanding.

## Subsequent capture: 190827_923_c2dab293efc345879f4bf639831fea90

This session contains 28 frames and no reported dropped entries. It ran active fishing rather than passive Observe: the journal contains mouse holds, rod-slot key presses, and recovery inputs. After 3.41 seconds of active session time it paused with `Rod equip was not visually confirmed`, followed by diagnostic context frames. ReleaseAll is logged before the pause event; this is not a measured End-stop latency test. One recovery and zero confirmed catches were recorded.

Reviewed frames 1, 8, 16, and 28 show gameplay/cast imagery and the final reel interface, not a crate demonstration. The macro remains visible over the right-hand reward region. Preserve this session as a rod/cast transition failure example; it does not satisfy the requested unobstructed crate capture. Investigate rod verification and cast-state transitions against these frames before claiming successful automated fishing.

## Unobstructed crate capture: 210223_377_bf9e92419b4e4db79dbab0632e2fbb1e

Successful recording: 18 retained frames over 19.34 seconds, completion `Stopped`, zero reported dropped entries, viewport 3440 x 1369. Frame 14 shows Quality Bait Crate selected in the bag; frame 15 shows its Open Crates confirmation with amount 1. Frames 16–18 show the right-edge notification `Opened Quality Bait Crate` and `x5 Seaweed (Bait)` without the macro obscuring it. This is positive human-reviewed evidence of a manually completed crate opening, not proof of automated workflow reliability. No additional repetition of this demonstration is needed.

The reward is a right-edge notification, not a central modal requiring dismissal. Background player/item labels overlap the translucent confirmation dialog, providing a useful difficult perception fixture. A desktop notification appears in earlier frames but has cleared before confirmation and reward. Preserve raw frames locally; labels on the workspace copy identify the confirmation and verified manual reward sequence. The application's own outcome remains Unknown because this was passive observation.
