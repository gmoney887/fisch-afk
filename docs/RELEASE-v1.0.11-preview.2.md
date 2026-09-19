# v1.0.11-preview.2 — second-PC testing preview

Updated Windows x64 testing build. Extract the entire ZIP into a new folder and run `FischMacroCS.exe`; no separate .NET installation is required. Close older macro instances first. Stable v1.0.10 remains available for rollback.

## Fixed since preview.1

- Detect an existing reel before recasting or starting non-reeling recovery. Two fresh observations of the bar and fish prevent a missed cast meter from triggering rod re-equipping during an active reel with a hidden hotbar.
- Replace a CI test's ignored local screenshot with a sanitized hotbar fixture. Add recorded reel, transient-detection and cancellation coverage.
- Exclude local build/diagnostic output folders from source compilation.

## Validation and limitations

171 Windows tests pass, including a clean-source run. Windows CI passed tests and portable packaging for the reel-entry fix. A first-PC smoke test reported 3 catches in 50.66 seconds with no failures, unknown catches or recoveries, ending on focus loss. This is not a matched throughput comparison or unattended reliability result.

This remains a testing prerelease. Shake failures and unwanted character movement reported on the second PC are not fixed or reproduced yet. Different rods also need live validation. Automatic aquarium/crate workflows remain unfinished; leave them and Trial adaptive dynamics off. Existing saved settings are preserved, so check these switches when upgrading. Real private diagnostic uploads and two-hour unattended trials on both PCs remain outstanding.

## Second-PC test

1. Join Fisch, choose a safe fishing position, and equip the rod to test.
2. Keep recording enabled. First use Observe to demonstrate a manual cast, shake and catch, then press End.
3. Use Start Fishing for a short supervised automated trial. Keep Roblox focused. Press End immediately if the character moves or casting stops working.
4. Save the recording and note the rod name and Roblox resolution. Review recordings before exporting or sharing; they can contain player names and chat. No automatic report upload is configured by this release.

The ZIP contains only the portable application, required assets and these notes. It excludes user settings, credentials and raw recordings.
