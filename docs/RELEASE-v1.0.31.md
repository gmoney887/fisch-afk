# Fisch AFK Pro v1.0.31

## Appraise until mutation

Use **Loot → Appraise…** to repeatedly appraise your equipped item until the selected mutation is detected. The default target is **Poisoned Driftwood**.

1. Equip the item and close the backpack.
2. Talk to the Appraiser and leave **Can you appraise it again?** visible.
3. Open **Appraise…** in Fisch AFK Pro and enter the item and desired mutation.
4. Set a maximum number of rolls, or enter **0** to continue until the target is found.
5. Select **Start appraising**. Use your configured start/stop hotkey or **End** to stop.

Each appraisal costs the fee shown in Fisch. Plain results are valid paid rolls. The app checks the equipped item after each roll and pauses when the item, fee, or payment cannot be verified. The displayed spending total adds the quoted fees for confirmed rolls.

The app saves your target and roll limit. Fishing, rod re-equipping, and other standalone workflows are guarded while appraisal is active.

## Requirements

- Windows 10 version 2004 or later, or Windows 11, x64.
- Windows English OCR language support for appraisal.
- Roblox and the Appraiser dialogue in English.
- Keep Roblox visible with the item name and weight readable.
- Extract the ZIP and run FischMacroCS.exe. The .NET runtime is included.

## Validation

The live Driftwood run reached Poisoned on its eleventh roll for 33 C$. The final recognition update was checked against the winning live item and a recorded frame with overlapping NPC dialogue.

Regression checks cover repeated plain results, roll limits, cancellation, unreadable results, cash-counter changes, and stopping on the winning item before another click. The item and mutation fields accept custom targets. Live validation currently covers Poisoned Driftwood at the Moosewood Appraiser.

The v1.0.31 release suite passed 858 tests with one skipped.
