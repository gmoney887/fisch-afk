# Fat Dad's Fisch AFK Pro

A Windows fishing assistant for Roblox Fisch, built with C#, WPF and OpenCvSharp. It observes the game window and sends mouse/keyboard input; it does not read or modify game memory.

## Download and start

Download the Windows x64 ZIP from [GitHub Releases](https://github.com/gmoney887/fisch-afk/releases). Preview releases are for testing; read their validation limits.

1. On Windows 10/11 x64, extract the entire ZIP into a new folder.
2. Run `FischMacroCS.exe`. The portable package includes the .NET runtime; no SDK installation is needed.
3. Open Fisch in Roblox, move to a fishing spot, and put the rod in slot **1**, or select its slot in **My fishing setup**.
4. Keep **AFK Performance** and **Save clips** enabled for the first trial. Enable **Live camera** to watch the preview instead; this turns off AFK Performance.
5. Click **Check readiness**, then **Start Fishing**. Keep Roblox visible and watch the first few catches.

Controls:

- **F6 / Start–Stop button:** start fishing, or queue a stop after the current cast/bite/reel cycle. Press again to stop immediately. A timed-out cycle stops without recasting.
- **End:** emergency stop.
- **F7:** re-equip the selected rod.

Each PC keeps its own settings and local recordings under `%LOCALAPPDATA%\FischAFKPro`. Recordings may contain player names and chat; review them before sharing. The download does not include developer settings or recordings.

## Current behavior

- Predictive cast release, visual shake clicks with mouse movement, and bar/fish tracking.
- Start remains requested through supported interruptions, with guarded input and paced recovery retries.
- Confirmed catches and Unknown outcomes are tracked separately; companion rewards do not count as the player's catch.
- AFK Performance reduces preview and interface overhead while retaining stored preferences.
- Bounded local recordings and replay support diagnosis of missed detections and stopped sessions.

## Preview limitations

Recent live trials reported 129 confirmed catches, one Unknown and no failures across about 32 minutes. These are application counters, with selected transitions independently inspected; the Unknown still needs review. This is not a completed two-hour soak or a guarantee of unattended operation on another PC.

End-to-end reconnect, idle-only heartbeat acceptance, physical hotkeys and broader PC/display configurations still need live validation. Position holding/return after drift or respawn is not implemented. Aquarium claiming is best effort: unconfirmed claims close the panel and defer, while interrupted closes retry during recovery so AFK fishing can resume. Reviewed visual templates and replay tests cover these paths. A September 26 live claim awarded 115,380 C$ and 15,752 XP. A subsequent live check verified the corrected panel closure; failure recovery while fishing remains replay-tested, not live-verified. Crate opening now includes reviewed recognition assets and regression tests: it selects a crate stack, replaces the quantity with a high upper bound (999999) for Roblox to clamp to the available amount, checks for visible quantity text and a new reward notification, and repeats up to the configured stack-batch limit. Zero means until verified empty, capped at 999 batches per command. Batch opening is implemented and regression-tested; live batch validation is pending. The app does not OCR the clamped count or claim an exact number opened. A September 26 live app trial confirmed ten consecutive crate openings with new reward notifications; empty-inventory completion and other display configurations remain unverified. Manual loot actions minimize the macro to prevent its pinned window from intercepting game clicks.

## Development

Continuing development in a new session? Read the [AFK handoff and prioritized remaining work](docs/AFK-HANDOFF.md) first. It distinguishes the published preview, local changes, verified test snapshots and unfinished live acceptance.

Requires the .NET 10 SDK on Windows:

```powershell
dotnet build FischMacroCS.slnx -c Release
dotnet test FischMacroCS.slnx -c Release
./scripts/Test-WithCoverage.ps1
```

For a local self-contained build:

```powershell
dotnet publish FischMacroCS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-local
```

Required regression fixtures are included. Tests depending on optional private images explicitly skip when those images are unavailable. See [the regression matrix](docs/AFK-REGRESSION-MATRIX.md) and [feature acceptance ledger](docs/FEATURE-ACCEPTANCE.md) for evidence and open checks.
