### Fat Dad's Fisch AFK Pro v1.0.29

#### 🎃 Doomsekkar Rod Obstacle & Border Tracking
- **Smart Pumpkin Bomb Bridging**: Seamlessly tracks the control bar across the 4 hanging obstacle pumpkin bombs without fragmented tracking drops.
- **Wall & Rail Separation**: Automatically extracts and tracks the control bar when pressed against the left or right minigame boundary rails.
- **Dual-Mode Fish Target Tracking**: High-confidence detection for both the swimming pumpkin/skull silhouette and bright glowing Starburst/Compass event fish.
- **Continuous 0% Progress Tracking**: Maintains continuous tracking lock on both the bar and fish throughout the minigame, even when reel progress momentarily resets to 0%.

#### 🎣 Flawless Reel Conclusion & Transition
- **Minigame Presence Gating**: Upfront minigame boundary validation prevents character glowing cosmetics or hotbar elements from falsely maintaining an active reel after the catch ends.
- **Instant Catch Hand-Off**: Immediately detects minigame conclusion and transitions cleanly to post-catch verification and the next cast.

#### 🏷️ Clean Runtime Versioning
- **Synchronized Version Badge**: Eliminates stale `-preview` metadata so the in-game badge and application window accurately display the active release version.
