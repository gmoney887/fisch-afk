# Fisch AFK Pro - Agent Customizations & Rules

When assisting with this repository, strictly adhere to the following architectural and design rules.

## Design Philosophy: "Zero-Config"
- Do **not** add manual calibration tools, offset calculators, or UI menus requiring the user to align boxes on their screen.
- If a coordinate or timing fails, fix the underlying logic dynamically using Computer Vision, math, or velocity tracking rather than pushing the burden onto the user.

## Computer Vision Rules
- The primary vision library is `OpenCvSharp4`.
- Do **not** install or use OCR libraries (like Tesseract) unless explicitly requested. Rely on morphological operations, template matching, or color space filtering (HSV/BGR).
- Always use Region of Interest (ROI) cropping when calling `CaptureClientRegion` to maintain the sub-2ms processing loop. Do not process the entire 1080p/4k frame unless absolutely necessary.

## Coordinate Targeting
- **NEVER** use horizontal width percentages (e.g. `clientW * 0.45`) to target UI elements like Topbars, Dialogs, or Backpacks. 
- Roblox scales center-anchored UI based on viewport height. 
- **ALWAYS** use Center-Anchored Height-Scaled math. Example: `(clientW / 2) + (clientH * 0.123)`
- Before clicking a UI button, use `VisionProcessor.DynamicUISnap` to mathematically center the cursor on the target color to prevent miss-clicks due to UI drift.

## Physics & Timing
- The casting mechanism uses a dynamic predictive lead (e.g. 25ms) based on the velocity of the bar. Maintain this logic to guarantee "Perfect Casts". Do not revert to static timers.
