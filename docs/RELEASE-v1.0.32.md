# Fisch AFK Pro v1.0.32

This release combines v1.0.31 appraisal with all nine previously unpublished local commits and the pending rod, reel, recovery, and recording improvements.

## Included improvements

- Ruinous Oath: recognize the equipped rod from its hotbar label in Auto mode, select its reel dynamics, and preserve detection through difficult reel effects.
- Random Rod: detect its reel and black fish marker, including wide and single-arrow bars.
- Webweaver: improve pointed-marker discovery, capture bounds, marker reacquisition, and timing diagnostics.
- Duskwire and Hammyshork: add dedicated reel detection and regression coverage for overlapping controls, clipped meters, and changing reel layouts.
- Reeling: improve Nate's Blade tracking, laser avoidance, progress-based stall recovery, rod recovery, and queued stopping.
- Recovery and catches: improve disconnect/retry recognition, catch confirmation, companion-caption handling, and idle-screen rejection.
- Recording: coordinate session ownership across processes, bound recording/report storage, and preserve failure evidence.
- Retain the broader code cleanup and vision-performance improvements from the unpublished commits.

## Appraise until mutation

Use **Loot → Appraise…** with the desired item equipped, the backpack closed, and **Can you appraise it again?** visible at the Appraiser. Enter the item and mutation, then set a roll limit; **0** continues until the target is found. The default target is **Poisoned Driftwood**.

Every appraisal costs the fee shown in Fisch and may return an item without a mutation. The app checks the equipped item after each roll and stops on the desired mutation. It pauses if the item, fee, or payment cannot be verified. Use the configured start/stop hotkey or **End** to stop.

## Requirements and installation

- Windows 10 version 2004 or later, or Windows 11, x64.
- Extract the ZIP and run **FischMacroCS.exe**. The .NET runtime is included.
- Appraisal requires Windows English OCR language support and English Roblox/Appraiser text. Keep the item name and weight visible.

## Validation

The full release regression suite passed **1,205 tests**, with **one existing skipped test** and **zero failures**, including the newer Ruinous Oath tests and recorded gameplay cases. The standalone portable build also succeeded. Recorded-frame tests validate detection and workflow regressions; they do not guarantee a successful catch for every rod or encounter.

The v1.0.32 development executable opened and displayed the correct version. The remaining desktop UI checks were stopped by the user and were not completed.

The prior live appraisal run reached Poisoned Driftwood on its eleventh roll for 33 C$. Live appraisal validation covers Poisoned Driftwood at the Moosewood Appraiser.
