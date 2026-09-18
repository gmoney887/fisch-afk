# Fisch AFK Pro - Agent Customizations & Rules

When assisting with this repository, strictly adhere to the following architectural and design rules.

---

## Design Philosophy: "Zero-Config"
- Do **not** add manual calibration tools, offset calculators, or UI menus requiring the user to align boxes on their screen.
- If a coordinate or timing fails, fix the underlying logic dynamically using Computer Vision, math, or velocity tracking rather than pushing the burden onto the user.

---

## Computer Vision Rules (MANDATORY - NO EXCEPTIONS)

### Library & Pipeline
- The **ONLY** vision library is `OpenCvSharp4`. Use native OpenCV operations (`Cv2.InRange`, `Cv2.Reduce`, `Cv2.FindContours`, `Cv2.MinMaxLoc`, `Cv2.MatchTemplate`, `Cv2.MorphologyEx`, etc.).
- Do **not** install or use OCR libraries (like Tesseract) unless explicitly requested.
- All visual detection (UI elements, bar positions, equipped state, dialogs, crates, buttons) **MUST** use OpenCV Computer Vision. There are **zero exceptions**.

### Prohibited Patterns (HARD BAN)
These patterns have repeatedly caused regressions. **NEVER** use them:
- ❌ **Manual pixel loops** — `mat.Get<Vec3b>(row, col)` in a `for` loop. Use `Cv2.InRange` + `Cv2.CountNonZero` / `Cv2.Reduce` / `Cv2.FindContours` instead.
- ❌ **Hard-coded pixel coordinates** — e.g. `if (x == 540 && y == 960)`. All positions must be derived from viewport height math or OpenCV contour/template matching.
- ❌ **Width-percentage targeting** — e.g. `clientW * 0.45` for centered UI. See Coordinate Targeting rules below.
- ❌ **`System.Drawing` or `Bitmap`** — Always use OpenCvSharp `Mat`. Never import `System.Drawing` for image processing.
- ❌ **Blind pixel color checks** — e.g. `if (pixel.Item0 > 200)` without HSV conversion or `Cv2.InRange` masking.
- ❌ **Sleeping instead of detecting** — e.g. `await Task.Delay(2000)` to "wait for animation". Use a CV polling loop that detects the target state.

### Performance
- Always use **Region of Interest (ROI) cropping** when calling `CaptureClientRegion` to maintain sub-2ms processing. Do not process the entire 1080p/4K frame unless absolutely necessary.
- Prefer `Cv2.Reduce` column/row projections over full-frame scans.
- Use HSV color space (`Cv2.CvtColor(..., ColorConversionCodes.BGR2HSV)`) for color filtering — never raw BGR thresholds.

---

## Coordinate Targeting (MANDATORY)
- **NEVER** use horizontal width percentages (e.g. `clientW * 0.45`) to target UI elements like Topbars, Dialogs, or Backpacks.
- Roblox scales center-anchored UI based on **viewport height**.
- **ALWAYS** use Center-Anchored Height-Scaled math. Example: `(clientW / 2) + (clientH * 0.123)`
- Before clicking a UI button, use `VisionProcessor.DynamicUISnap` to mathematically center the cursor on the target color to prevent miss-clicks due to UI drift.

---

## Input Emulation Rules
- **NEVER** use legacy `mouse_event` for mouse input — Roblox ignores it.
- **ALWAYS** use `Win32.SendInput` with `MOUSEINPUT` and absolute virtual desktop coordinates for mouse movement and clicks.
- For critical clicks (casting, crate dialogs, UI buttons), use `Win32.SendHardwareClick` which combines `SendInput` mouse move + button press with `WM_LBUTTONDOWN`/`WM_LBUTTONUP` messages for maximum reliability.
- Before any mouse action, call `Win32.ForceSetForegroundWindow` to ensure Roblox has input focus.

---

## Physics & Timing
- The casting mechanism uses a dynamic predictive lead (e.g. 25ms) based on the velocity of the bar. Maintain this logic to guarantee "Perfect Casts". Do not revert to static timers.

---

## Release Cadence & Development Policy
- **Do NOT cut a release on every change**: Do not bump versions (`1.0.x`), create Git tags, or publish GitHub releases during active development, iteration, and debugging.
- **Local Verification First**: Iterate using local builds (`dotnet build`) and automated test suites (`dotnet test`).
- **Explicit Release Only**: Only invoke `publish-release` to tag and publish to GitHub when the user explicitly requests a release (e.g. "publish", "release", "ship build") or when an agreed-upon milestone is thoroughly verified and approved for release.

---

## Regression Prevention Checklist
Before submitting **any** code change, verify:

1. **`dotnet build`** compiles with zero errors.
2. **`dotnet test`** passes all existing tests (never disable or delete passing tests to make a change work).
3. All visual detection uses **OpenCV native calls only** (no manual pixel loops, no System.Drawing).
4. All coordinates use **center-anchored, height-scaled** math (no `clientW * fraction` for centered UI).
5. All mouse input uses **`SendInput`/`SendHardwareClick`** (no `mouse_event`).
6. Any new `await Task.Delay()` over 500ms has a comment explaining **why** CV polling isn't feasible.
7. ROI captures use the **smallest bounding region** necessary for the detection task.
