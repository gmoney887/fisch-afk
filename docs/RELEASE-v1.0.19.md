# Fat Dad's Fisch AFK Pro v1.0.19

Fixes missed fishing losses, misleading catch statistics, and crate recognition and recovery.

- Recognizes the catch-streak-ended notification, counts the loss once, and resets the streak.
- Shows Catch Success across all completed attempts, including unconfirmed outcomes, with a count breakdown. Imperfect sessions no longer round up to 100%.
- Tracks dim red reel bars and progress obscured by red effects. Removes duplicate fishing inputs and adds bounded retries for an unresponsive right hold.
- Recognizes dense crate inventories and long reward notifications. Adds an extra confirmation window and bounded inventory reacquisition when a reward is unconfirmed.
- Replaces crashing native smoothing in crate and shared workflow recognition.
- Verifies rod deselection before re-equipping, improves aquarium recognition, and fixes window activation hiding Roblox.
- Records the build version and executable path in new recording manifests.

Validation: 554 regression tests passed, zero failed, one existing local-fixture test skipped. Includes recorded failure frames, worker replay, and synthetic display-scale tests. The latest combined build has not completed sustained live or overnight validation; multi-PC/DPI acceptance remains outstanding.

Extract the entire Windows x64 ZIP into a new folder, close the old macro, and launch FischMacroCS.exe. Keep the Assets folder beside the executable. Existing settings and recordings remain in local application data.
