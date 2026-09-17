---
name: publish-release
description: >-
  Automates building, packaging, tagging, and releasing Fat Dad's Fisch AFK Pro to GitHub.
  Use this skill whenever the user asks to release, publish, create a release, bump version,
  or make the latest build downloadable on GitHub.
---

# Publish & Release Skill

This skill handles the end-to-end automated release lifecycle for **Fat Dad's Fisch AFK Pro**. It guarantees zero manual calibration or manual packaging steps.

> [!IMPORTANT]
> **Release Cadence**: Do NOT invoke this skill on internal developmental iterations or bugfix cycles. Only publish a release when the user explicitly instructs to cut a release, ship, or publish to GitHub.

## Automated Release Workflow

When the user asks to release or publish:

1. **Determine Version**:
   - Check if the user specified a new version (e.g. `1.0.1`) or a bump level (`patch`, `minor`, `major`).
   - If not specified and the current version is already tagged/released, default to a `Patch` bump.
   - If releasing the current untagged/unreleased build, keep the current version.

2. **Execute the Release Helper**:
   Run the PowerShell automation script located at [publish.ps1](./scripts/publish.ps1).

   **Examples**:
   - Release current version:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .agents/skills/publish-release/scripts/publish.ps1
     ```
   - Bump patch version (e.g. `1.0.0` -> `1.0.1`) and publish:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .agents/skills/publish-release/scripts/publish.ps1 -Bump Patch
     ```
   - Specify explicit version:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .agents/skills/publish-release/scripts/publish.ps1 -Version "1.1.0" -ReleaseNotes "Major feature release"
     ```
   - Test build & package locally without pushing:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .agents/skills/publish-release/scripts/publish.ps1 -SkipPush
     ```

3. **What the Automation Does**:
   - Updates `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in `FischMacroCS.csproj` if bumping.
   - Compiles a standalone single-file portable binary (`publish-singlefile/FischMacroCS.exe`) with native libraries embedded (zero runtime dependencies).
   - Bundles `FischMacroCS.exe` and `Assets/shake_template.png` into `dist/FischMacroCS-v{VERSION}-win-x64.zip`.
   - Commits any pending changes and creates an annotated Git tag (`v{VERSION}`).
   - Pushes branch and tags to GitHub (`origin/main`).
   - Creates or updates the GitHub Release using `gh release create` and uploads the distribution zip.

4. **Verification**:
   - Verify that `dist/FischMacroCS-v{VERSION}-win-x64.zip` exists and is non-empty (~100 MB).
   - Verify that git tag `v{VERSION}` is created.
   - Provide the user with the direct GitHub release link (`https://github.com/gmoney887/fisch-afk/releases/tag/v{VERSION}`) and the direct download URL for their friend.
