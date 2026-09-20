# Fat Dad's Fisch AFK Pro

A Windows fishing assistant for Roblox Fisch, built with C#, WPF and OpenCvSharp. It observes the game window and sends mouse/keyboard input; it does not read or modify game memory.

## Download and start

Download the Windows x64 ZIP from [GitHub Releases](https://github.com/gmoney887/fisch-afk/releases). Preview releases are for testing; read their validation limits.

1. On Windows 10/11 x64, extract the entire ZIP into a new folder.
2. Run `FischMacroCS.exe`. The portable package includes the .NET runtime; no SDK installation is needed.
3. Open Fisch in Roblox, move to a fishing spot, and put the rod in slot **1**, or select its slot in Fishing settings.
4. Keep **AFK Performance** and **Record sessions** enabled for the first trial. Choose the appropriate rod profile if desired.
5. Click **Check readiness**, then **Start Fishing**. Keep Roblox visible and watch the first few catches.

Controls:

- **F6 / Start–Stop button:** toggle fishing. During a reel, the first stop queues completion of the catch; another stop forces it.
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

End-to-end reconnect, idle-only heartbeat acceptance, physical hotkeys and broader PC/display configurations still need live validation. Position holding/return after drift or respawn is not implemented. Aquarium claiming and crate opening lack reviewed visual templates; leave those optional actions off for the baseline trial.

## Development

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
