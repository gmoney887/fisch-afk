# Fat Dad's Fisch AFK Pro v1.0.24

Adds Blade of Glorp support to automatic minigame detection, with an optional Blade of Glorp theme selection.

- Tracks the opaque green fish icon instead of matching the translucent needle against scenery, preventing the false fish positions observed in the failed trial.
- Detects bright lime, translucent brown, and dim green control bars, including changing bar widths and white highlight effects.
- Recovers progress-meter evidence when screen shake tilts or fragments the thin outline, helping keep active reels from being mistaken for completed catches.
- Keeps the Windows hotkey fallback active when a low-level hook stops delivering input, while coalescing duplicate notifications so one F6 queues a stop and the next forces it. Records the initial queued-stop request for diagnosis.

Validation: 16 Glorp regression cases, including 720p/1080p scaling and failed-trial frames, pass. A local replay detected the fish and control bar in all 51 retained reel frames from the failed trial. The final Release suite passed 677 tests across complementary engine/hotkey and remaining-test batches, with one existing private-fixture skip. All five new hotkey fallback regression cases pass. The application builds with zero warnings or errors, and the real Windows capture/overlay check passes without injecting input.

Live Glorp catch performance remains unverified. Rejoin Roblox, leave the theme on Auto, and press F6 to start fishing. Opening the application alone does not start the macro.

Download and extract the Windows x64 ZIP, then run FischMacroCS.exe. Settings and recordings remain in the per-PC local application data folder.
