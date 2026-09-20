# v1.0.12-preview.1 — Second-PC testing

Portable Windows x64 preview with the latest fishing reliability fixes and a consistent interface at smaller window widths.

## Changes

- Restore mouse movement before and during visual shake clicks, including repeated stationary targets; expand shake detection to screen edges.
- Keep fishing requested through supported interruptions with paced retries, input cleanup and recovery checks.
- Improve compact/dim reel recognition, cast-scenery rejection and player catch confirmation when companion rewards stack.
- Add verified Reconnect/Continue prompt handling; use mouse input for Continue.
- Make Observe and other actions consistent and usable at smaller window widths.
- Improve bounded recording retention, record the initiating stop source and use recorded viewport dimensions during replay.
- Expand actual-worker, recorded-frame, cancellation and recording regressions.

## Validation and remaining limits

The working tree passed 372 tests with one explicit unavailable private-fixture skip. The release helper reruns the full suite on the versioned build before packaging.

Two recent live runs on Rod of the Depths reported 129 confirmed catches, one Unknown and no failures across about 32 minutes, ending through user-requested queued stops. Selected transitions were independently inspected; the Unknown still needs review. The two-hour soak, end-to-end live reconnect, idle-only heartbeat acceptance, physical hotkeys and second-PC validation remain open. Character position holding/return is not implemented. Keep optional crates/aquarium actions off: reviewed workflow templates are unavailable.

## On the second PC

1. Download `FischMacroCS-v1.0.12-preview.1-win-x64.zip` and extract all files into a new folder.
2. Run `FischMacroCS.exe`; no separate .NET installation is needed.
3. Enter Fisch, stand at a fishing spot, and equip the rod in slot1 (or configure its slot).
4. Keep AFK Performance and Record sessions on. Run Check readiness, then Start Fishing and observe the first few catches.
5. F6 queues a stop during reeling; press again to force it. End is the emergency stop.

If a session gets stuck, keep its recording and note what appeared on screen. Local recordings live under `%LOCALAPPDATA%\FischAFKPro\recordings`; review them before sharing.

Release gate: versioned build passed372 tests,0 failed,1 known private-fixture skip. The99.6MB ZIP contains the portable executable, shake template and setup/release notes. Extracted executable SHA256 `5BFA7C2B57EE169DBC14323A45FBDE9569052CAD3B5D6C930610A324EA1190BD`; hash matches staging. The extracted app launched successfully, displayedv1.0.12-preview.1, remained idle and closed normally. No fishing input was started by this startup check.
