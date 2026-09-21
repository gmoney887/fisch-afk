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
still produce an explicit unavailable outcome without clicking. Crate automation
does not gain any assets or new acceptance from this change.
