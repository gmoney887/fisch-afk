# Fisch AFK Pro v1.0.13

## What's new

- Refreshed fishing dashboard with a night-fishing illustration, navy panels, larger session stats, clearer switches, and player-focused controls.
- Catch tracking stays visible beneath fishing status: catch bar, fish needle, and vision timing no longer require expanding a panel.
- Quick access to split screen, windowed mode, aquarium claims, crates, recordings, and diagnostic sharing.
- Clearer hook-and-wave application icon and corrected Reset button spacing.

## Fixes and reliability

- Live camera now saves its setting, reconnects its image after preview/AFK toggles, and receives frames independently of text-only telemetry.
- Enabling Live camera turns off AFK Performance and opens the camera panel.
- Aquarium claims include recognition templates, verify the claim and panel close, and preserve check scheduling across restarts.
- Added death-screen recognition and replay coverage for waiting, stopping, and recovery.
- Improved bounded session diagnostics so important recovery evidence survives long runs.

## Download and run

Download **FischMacroCS-v1.0.13-win-x64.zip**, extract the entire ZIP, then run **FischMacroCS.exe**. Keep the included **Assets** folder next to the executable. No separate .NET installation is required.

Close an older copy before starting this version. To watch the camera, enable **Live camera** and start fishing or select **Record my play**.

## Validation scope

Automated regression tests and UI layout checks cover this release. The updated aquarium workflow and death recovery have replay coverage; fresh live, second-PC acceptance remains outstanding. See the repository's acceptance documentation for details.
