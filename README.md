# Fisch AFK Pro (Computer Vision Auto-Angler)

A high-frequency, purely visual auto-fishing macro for Roblox "Fisch", built in C# (.NET 10.0 WPF) and powered by `OpenCvSharp4`. 

Unlike traditional memory-reading or color-bot macros, this tool relies on 100% Computer Vision to dynamically adapt to screen sizes, lag, UI drift, and varying rod physics in real-time.

## 🚀 Key Features

*   **Sub-Millisecond Vision Loop**: Uses ultra-fast `BitBlt` ROI (Region of Interest) cropping instead of full-screen snapshots, running the vision processing loop at under 2ms.
*   **Predictive "Perfect Cast" Logic**: Tracks the rising power bar's velocity and executes an early mouse release (e.g., 25ms lead-time) to counteract game-engine delay and perfectly land in the 97–99% sweet spot on every cast.
*   **Dynamic UI Snapping**: Uses OpenCV template matching and dynamic color bounding-box tracking to auto-snap clicks to the exact center of UI elements (Aquarium claiming, crate opening).
*   **Aspect-Ratio Independent Math**: All coordinate targeting uses Center-Anchored, Height-Scaled formulas, making it bulletproof against different monitor aspect ratios (e.g., Ultrawide) and windowed modes.
*   **Fully Autonomous**: Automatically re-equips rods, unpacks inventory crates, claims Aquarium rewards, and features built-in anti-AFK movement to defeat Roblox's 20-minute idle disconnects.
*   **Session Analytics**: Live telemetry tracking win rates, streaks, uptime, and catches-per-hour directly in a WPF dashboard.

## 🛠 Prerequisites

*   **OS:** Windows 10/11
*   **Framework:** .NET 10.0 Desktop Runtime (or SDK for building)
*   **Resolution:** Any! The macro dynamically scales its coordinates based on your active window size.

## ⚙️ Building & Running

**To build a portable executable:**
```powershell
.\publish_portable.bat
```
This generates a ready-to-run `.exe` in the `publish\` directory that you can move anywhere.

**To run via CLI (development):**
```powershell
dotnet run
```

## 🎮 How to Use
1. Launch Roblox and open the "Fisch" game.
2. Ensure your Fishing Rod is assigned to slot **1** (or adjust in settings).
3. Open the **Fisch AFK Pro** app.
4. Tweak your `Settings` if desired (Rod type, auto-open crates toggle, etc).
5. Click **Start** or use the Hotkey (`F8`) to begin!

---
*Disclaimer: This is a standalone computer vision tool. It does not inject into Roblox or modify game memory. Use at your own risk in accordance with game rules.*
