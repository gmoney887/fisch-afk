### Fat Dad's Fisch AFK Pro v1.0.28

#### 🎃 Doomsekkar Rod Support (FischFright 3)
- **Zero-Config Auto-Calibration**: Added native theme and palette recognition for the Doomsekkar Rod.
- **Spooky Aesthetics Tracking**: Automatically identifies and tracks the purple/lime control bar against dark/glowing water backdrops.
- **Jack-o'-lantern Fish Tracking**: Robust detection of the pumpkin fish icon utilizing dark-stalk projection and contour verification.
- **Custom Rod Kinetics**: Dynamic velocity matching tuned specifically for Doomsekkar's rapid tension dynamics.

#### ⚔️ Ruinous Oath Minigame Tracking Stability
- **Dynamic Multi-Palette Bar Tracking**: Ruinous Oath dynamically shifts bar colors between Solid White (perfect fish alignment), Bright Red (maximum reel progress), Olive/Green (unaligned drift), and Dim Brown (losing progress). The vision engine now tracks all states seamlessly across all frame variations.
- **No False Theme Locks**: Prevents white bar alignment frames from incorrectly locking the engine into standard Default mode.
- **Particle & Blood-Slash Occlusion Filtering**: Robust filtering against stationary red blood-slash impact sparks near the catch edge, eliminating false tracking jumps and 35-second stall timeouts.
- **Immediate Re-entry Prevention**: Prevents stale catch-end frames from triggering premature false re-casts.

#### 🛡️ Quality & Performance
- Zero-config auto-calibration across standard and specialty minigame themes.
- All 838 automated regression and computer vision test cases passing.
- Single-file self-contained Windows x64 portable executable (no .NET runtime installation required).
