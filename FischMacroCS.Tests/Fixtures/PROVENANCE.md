# Reviewed regression fixtures

`hotbar_ultrawide_unequipped.png` replaces the ignored `user_screen_latest.png` dependency. The original 1024 x 425 geometry is preserved, but only the hotbar rectangle (390,375,240,45) is retained; the rest is neutral gray. Both full-frame and bottom-ROI assertions exercise the same unequipped slot. Raw desktop content is not required by CI.

`reel_active_recovery_21.png` and `reel_active_recovery_22.png` come from first-PC session `190827_923_c2dab293efc345879f4bf639831fea90`. They retain only the reel track, excluding usernames, currency, macro overlays and the rest of the desktop. Source viewport: 3424 x 1353; source frame: bottom 338 pixels; retained ROI: (968,0,1488,216), corresponding to viewport origin (968,1015). Both frames show an active reel while recovery was trying to equip a rod using the hidden hotbar.

These two frames belong to the same development session and PC, not an independent validation set. Replay establishes perception and the decision to avoid recasting; it does not establish live catch outcomes.
