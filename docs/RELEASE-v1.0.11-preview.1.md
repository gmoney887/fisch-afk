# v1.0.11-preview.1 — testing prerelease

Shareable Windows x64 preview of the reliability and diagnostics work. This is an in-progress testing build, not the stable unattended release. Stable v1.0.10 remains available for rollback.

## Download and run

Download `FischMacroCS-v1.0.11-preview.1-win-x64.zip`, extract the entire ZIP into a new folder, and run `FischMacroCS.exe`. No separate .NET installation is needed. Close other macro instances before starting. Keep the previous build folder.

Join Fisch, choose a fishing position, equip your rod, and keep Roblox visible and focused. Use **Observe** for passive demonstrations; **Start Fishing / F6** runs automation. Press **End** to stop. Returning to another application pauses capture and automation; resume explicitly.

## Changes

- Serialized gameplay input, cancellation checks, input release, and controlled pauses on focus/capture/geometry changes.
- Delayed catch outcome verification, unknown-outcome tracking, and bounded recovery.
- Bounded local recordings, passive observation, frame replay/labels, and diagnostic export.
- Private diagnostic submission UI with media preview, optional masking, Windows Credential Manager storage, and retry tracking.
- Optional bounded rod-dynamics adaptation, disabled by default.
- App-data storage, configuration backups, Windows CI, and local portable-build rollback support.

## Known limitations — read before testing

- Aquarium and crate automation are unfinished: reviewed detection templates are not included, and the crate workflow needs revision against the recorded game UI. Leave both automatic reward options off and use manual game controls. New settings default them off; existing saved settings are preserved, so upgrading users must check these options.
- One live fishing attempt paused because rod equipment could not be visually confirmed. Fishing reliability and improved catches/hour have not been established. Test while present before relying on automation.
- Experimental adaptation should remain off unless intentionally collecting a comparison trial.
- Both two-hour unattended PC trials, second-PC validation, and a live held-input End-stop latency check remain outstanding.
- Private GitHub reporting requires a private repository, a diagnostics release, and repository-scoped credentials. Its HTTP behavior has automated coverage, but real credentialed uploads have not been exercised. Manual export is available. Review captures before sharing; recordings are not anonymous.
- Desktop capture includes visible overlays. Keep the macro and other windows clear of relevant game UI while recording.

## Verification

165 native Windows Release tests passed. Passive live demonstrations captured an aquarium claim and a Quality Bait Crate opening with five Seaweed, plus recording stop/focus-loss behavior. Those demonstrations validate recorded evidence, not automated reward completion. The portable ZIP contains the application and assets only, with this note; it excludes local settings, credentials, logs, and raw diagnostic recordings.
