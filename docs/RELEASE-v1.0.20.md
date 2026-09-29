# Fat Dad's Fisch AFK Pro v1.0.20

Fixes auto-aquarium getting stuck on an open panel when scenery behind the red close X changes.

- Matches the red close symbol independently of its background, within the aquarium close-button region.
- Retries an unconfirmed claim once and allows up to three freshly verified close clicks, spaced at least one second apart.
- Removes duplicate input from aquarium cleanup.
- Adds recorded-control regression coverage across display scales and worker recovery scenarios.

Validation: 53 targeted aquarium and worker recovery tests passed. The recorded stall is reproduced and corrected in replay. Sustained live and overnight validation remain outstanding.

Extract the entire Windows x64 ZIP into a new folder, close the old macro, and launch FischMacroCS.exe. Keep the Assets folder beside the executable. Settings and recordings remain in local application data.
