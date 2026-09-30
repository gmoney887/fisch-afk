# AFK rod-reset failure, 2026-09-29 afternoon

Session 20260929_175752 used 1.0.20-idlefix.1. It recorded 254 catches, 13 unconfirmed outcomes and three watchdog recoveries. The final interruption was a 25-second lure timeout followed by rod reset. The preserved frame before pressing 1 (24276) has selected-rod geometry; frame 24277 immediately afterward shows the same hotbar without selection. The old detector reports GeometryConfirmed=false on that exact frame, causing reset to abort and recovery to wait for controls already on screen. Later recognition flickers briefly and the scene guard eventually stops. The retained failure frames show gameplay rather than an idle-disconnect dialog.

The recording improvements retained this exact transition. Replaying it reproduced the defect before the change.

Fix: when selection and legacy dark-container checks fail, detect the centered row of unselected slot borders in the fresh image. Require at least four aligned complete square contours plus border support for seven of nine slots; reject blank frames and isolated squares. This confirms geometry without asserting the rod is selected. Recorded-frame and rod-reset regressions cover the unequip transition.

The initial lure interruption is handled through the existing rod-reset recovery; this change fixes the demonstrated failure of that recovery. Live sustained acceptance remains outstanding. The local build also includes the preceding idle/recovery/recording changes.

Validation: 74 targeted rod, hotbar, reset and recovery tests passed; one existing local-fixture test skipped. Local candidate: artifacts/hotbar-fix-20260929/FischMacroCS.exe (1.0.20-hotbarfix.1). Not published or live-validated.
