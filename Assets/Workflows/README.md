# Aquarium recognition assets

These four PNGs are reviewed crops from the local manual aquarium demonstration
`session_20260918_190214_817_e8156312e4e64b15a6cb675af27e9905`, at a reference
viewport of **3424 x 1353**. They retain native capture resolution. AquariumWorkflow
specifies the 1353 reference height; unrelated workflows retain the 1080 default.

- `aquarium-navigation.png`: frame 1, Aquariums button at the top center.
- `aquarium-claim.png`: frame 4, Claim text inside the Profit section.
- `aquarium-reward.png`: frame 5, the **0 C$ 0 XP** unclaimed balance, not a toast.
- `aquarium-close.png`: frame 4, top-right aquarium close X.

Small Gaussian smoothing tolerates text rasterization differences when scaling.
Search regions remain centered on the reviewed controls. The small close icon
uses a 0.92 confidence threshold; balance uses 0.94; other controls use 0.96.
All action prerequisites and resulting states require fresh observations.

A claim is confirmed only when zero balance was absent before the click and is
present afterwards, followed by confirmed panel closure. An already empty balance
is a successful check, not a newly claimed reward. Old reward toasts are ignored.

The project copies these assets into build and publish output. Missing assets
still produce an explicit unavailable outcome without clicking.

## Crate recognition assets

The `crate-*.png` files are native-resolution crops from the manually demonstrated
`session_20260918_210223_377_bf9e92419b4e4db79dbab0632e2fbb1e`, at **3440 x 1369**:

- Frame 14: bag header, exact `crate` filter, and inventory `Crate` text.
- Frame 15: Open Crates title, quantity 1, and Yes control.
- Frame 16: `Opened` prefix and `Crate` suffix in the right-edge notification.

CrateWorkflow selects a visually recognized item, checks its selection outline,
closes the bag, activates the item, replaces the quantity with 999999 (Roblox clamps
to the available stack, as confirmed by the user), blurs the field, checks visible
quantity text, then confirms
a new reward notification after Yes. Existing notifications must clear first.
There is no Max button and no reward dismissal click. Empty results require the
verified filter, bag header, and a blank dark results grid in consecutive frames.

The saved CrateMaxTypes setting now limits stack-opening batches, as labeled in the UI.
Zero runs until verified empty, with a 999-batch per-command safety limit. The rod
is restored after the requested count or a verified empty result. Recognition
failure, focus loss, and cancellation stop the command without blind continuation.

Regression crops verify native and rescaled recognition and reject earlier
blank-quantity, fish-search, and incorrect-filter captures. Quantity 2 now passes
the batch field check; it still cannot match the legacy quantity-1 template. Resizing a
recording is not a live second-PC trial. The earlier single-crate live trials are documented below. Bulk opening has not
yet been live-verified. Visible field text is not OCR of the clamped amount, and
a reward confirms an opening without establishing an exact batch count.

## Aquarium failure recovery (2026-09-26)

An aquarium already open at startup is accepted only after both its Claim and X
controls are recognized in consecutive captures. A known-open panel requires an
explicit close action before consecutive missing controls count as closure; reward
animations can temporarily hide those controls. An unconfirmed claim whose panel closes returns Unknown and defers
the next optional attempt; it does not update claim/check timestamps or stop fishing.

If focus or input fails while the panel is open, the engine keeps a pending
close-only cleanup. Once Roblox is focused, recovery detects and clicks the X,
checks closure, and then verifies gameplay before resuming. Cleanup failures retry
at five-second intervals without clearing the user's fishing request. It never
retries the claim as part of cleanup. End/cancellation still stop immediately.
Manual aquarium actions now share deferred foreground activation and have a Stop
button; they cannot overlap a crate command.

Worker regression scenarios verify two catches across an unconfirmed claim,
focus loss immediately after opening, and a rejected close click. They assert no
invented claim timestamp, no paused engine, and released input after stopping.
Recorded-frame tests cover already-open/empty panels and partial closure. These
are automated regressions, not a live game trial.

### Live inventory validation (September 26)

G opens the item inventory; the Equipment Bag hotbar tool is unrelated and is
never used by the crate workflow. Native scan-code keyboard input opens this
inventory in the live Windows Store client. Search focus is anchored to its
recognized header rather than a fixed screen Y coordinate.

The current header/search templates come from frame 5 of the local live attempt
archived in artifacts/live-scan-attempt. The current inventory regression fixtures
retain only the inventory UI region, with other gameplay pixels masked out.
Direct control also verified selecting a Carbon Crate, closing the inventory,
opening its dialog, and receiving the reward after confirming quantity 1.
The September 26 live app run in artifacts/live-verified-crates confirmed ten consecutive individual crate openings, including Carbon and Quality Bait crates. The trial was stopped deliberately by foregrounding the app; no empty-inventory completion was asserted. Manual crate and aquarium actions minimize the macro before foregrounding Roblox because capture exclusion does not make a pinned window click-through. Current reward text and already-equipped selection borders have live-frame regressions.

The live aquarium claim awarded 115,380 C$ and 15,752 XP. That trial exposed reward-animation occlusion falsely satisfying closure. After requiring an explicit close action for a known-open panel, a live empty-balance check closed the panel successfully. A regression simulates the temporary animation occlusion. AFK continuation after claim failure remains covered by worker replays, not a live fishing trial.

The September 27 speed retry (artifacts/crate-speed-verified) confirmed six consecutive openings, 8.53–8.94 seconds apart, before a deliberate foreground interruption. The timeout now includes capture/recognition time; inventory position must settle before search input. Reward recognition isolates bright white lettering with the existing grayscale matcher retained for scaled displays. This avoids background-dependent misses observed for Big Abyssal and Big Silver Bait Crates. The workflow still waits for prior reward notifications to clear; this trial does not establish fast bulk opening or empty-inventory completion.
