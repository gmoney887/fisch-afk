# Reviewed regression fixtures

`reel-stall-nov05`: unchanged full frames481..1012 from session
`session_20261106_023822_098_e79c439fed794eb68c646b9f137d040a`, viewport3440x1369,
preserved in `artifacts/overnight-fixes/live-20261105-203945`. They show a slow
active reel crossing the original35s timeout and its later Megalodon banner.
Source timestamps and hashes are in `reel-stall-nov05/provenance.json`. Worker
tests replay the first52seconds, then use a separately reviewed catch result;
the frozen-frame case deliberately holds the first frame. Full frames may include
player/chat content; inspect before sharing. No candidate live-acceptance claim.

`disconnect-nov05`: unchanged full recorder frames for the November 5 error-279
outage and subsequent Roblox Home screen. Source session, paths and SHA256 are
in that directory's `provenance.json`; detailed review and template crop geometry
are in `docs/REGRESSION-2026-11-05-RECONNECT.md`. The held-out frame is later in the
same outage, not a separate PC. Resized test variants and worker sequencing are
controlled derivatives, not live recovery acceptance.

`Assets/catch_prefix.png` is the main catch phrase cropped from the existing `reel_catch_banner.png` fixture at (434,93,257,29), reference viewport height 1353. It excludes the companion's separate "Extra!" reward line. Scale tests derive from this same source and do not constitute independent captures at each resolution.

`reel_catch_live.png` is frame 699 from the local smoke test `session_20260919_190312_364_cb9ee378d290456cbb0747f63f881768`, viewport 2254 x 1353. It retains the central 1488 x 270 game ROI, excluding desktop, chat, usernames and inventory. The settled "You just caught a" phrase is smaller and brighter than the enlarged/fading earlier fixture. `Assets/catch_prefix_live.png` retains only (398,101,214,31) from this ROI. This fixture prevents the initial single-template implementation from marking every live catch unknown.

`hotbar_ultrawide_unequipped.png` replaces the ignored `user_screen_latest.png` dependency. The original 1024 x 425 geometry is preserved, but only the hotbar rectangle (390,375,240,45) is retained; the rest is neutral gray. Both full-frame and bottom-ROI assertions exercise the same unequipped slot. Raw desktop content is not required by CI.

`reel_active_recovery_21.png` and `reel_active_recovery_22.png` come from first-PC session `190827_923_c2dab293efc345879f4bf639831fea90`. They retain only the reel track, excluding usernames, currency, macro overlays and the rest of the desktop. Source viewport: 3424 x 1353; source frame: bottom 338 pixels; retained ROI: (968,0,1488,216), corresponding to viewport origin (968,1015). Both frames show an active reel while recovery was trying to equip a rod using the hidden hotbar.

These two frames belong to the same development session and PC, not an independent validation set. Replay establishes perception and the decision to avoid recasting; it does not establish live catch outcomes.

`reel_dim_bar_524.png` and `reel_dim_bar_525.png` retain only the central reel and progress region from session `session_20260919_173013_810_7aa2023c7a6c498c9fb841f7fbe32dc9`. Source viewport: 3440 x 1369; bottom capture origin: (0,1027); retained rectangle: (968,0,1504,260), so fixture viewport origin is (968,1027). The crops exclude the desktop, usernames, inventory and macro overlay. They reproduce a dark/translucent control bar with a visible fish needle and progress indicator. Scaled variants are generated in memory for geometry regression checks and are not independent live-resolution validation.

`reel_absent_edge_glow.png` is frame 754 from `session_20260919_182419_651_c4262421fa0b428c96a25911893ebfc0`. It retains the recorded central ROI (968,1013,1504,273) from a 3440 x 1369 viewport. It contains scenery and bait text, but no reel UI, desktop, usernames or inventory. AutoCalibrate falsely classified a clipped bright shape at the right capture edge as the control bar (absolute x2417..2472) with a fish at x2441. Regression assertions reject reel entry in all five theme modes.

- shake_lower_right.png: user-supplied Roblox screenshot, 2026-09-19, original 2240x1383. Required regression fixture for lower-right shake detection; translated button crops test both edges of a wide viewport.
- reel_live_false_exit.png: retained raw ROI frame 5202 from session_20260919_224102_986_9fd2c1ae3afe4ee1939573a3f0c5642e at the 971-second Unknown outcome. Viewport 2254x1353, ROI (383,1001,1488,270). Reviewed as active: compact progress strip and dim control bar. Required detector/worker regression; no visual edits.
- companion_bonus_only.png: raw bottom viewport strip from frame 102, session_20260919_230725_672_f654dfd13aa0447f9fe25289ac64f10a, 20-second Unknown outcome. Viewport 2254x1353, ROI (0,1015,2254,338). Shows Beak Bill bonus catch/bait notifications, not the normal player-catch prefix. Required negative catch fixture and worker replay.
- catch_maximized.png and catch_maximized_second.png: unmodified central ROI frames 1343 and 1438 from session_20260919_232711_998_b482295d3159485a89e63651bbc6c0bf, viewport3440x1369, ROI(968,1013,1504,273). Both visibly show player Mother Nature Deep-sea Hatchetfish catches (2.3kg and2.6kg), incorrectly finalized Unknown at frames1350 and1445. Assets/catch_prefix_maximized.png is the reviewed player prefix at(249,100,246,32) from frame1343. Frame1438 is a separate catch from the same session, not an independent-PC validation set.
- companion_maximized_only.png: raw frame1350 bottom strip (0,1027,3440,342) from that same session. Companion catch and bait notifications only; negative confirmation fixture. Scaled derivatives are generated in memory and are not independent resolution captures.
- server_update_wait.png: unmodified 2254x1353 client capture preserved as artifacts/last-context.png during the earlier interrupted AFK run. Visually reviewed: fish logo and "Game is shutting down for an update.. -Please stand by-", with no playable hotbar/reel and no Reconnect button. SHA256 0CEAEFBC439D3188795AFA1E614412416BEAE3EAB6D92F4666D461A608FA20E9. Required actual-worker waiting/cancellation fixture; it is not a reconnect positive or proof of live rejoining.
- disconnect_idle_278.png: reviewed 401x250 dialog crop at (927,583) from the2256x1385 native Roblox window screenshot saved as artifacts/reconnect-idle-278.png after the UI/readiness testing pause. Client viewport2254x1353. Shows Disconnected, idle20 minutes/error278, Leave and Reconnect. Crop excludes desktop/macro overlay and player details. Assets/disconnected_title.png retains dialog rectangle(128,12,146,28); Assets/reconnect_button.png retains the complete button label and white interior at(248,201,94,24). Scale variants are synthetic derivatives, not independent captures at each resolution. Negative variants remove the title/button or replace Reconnect with the captured Leave control; these are deliberate test mutations.
- fisch_continue.png: reviewed408x320 crop at(926,498) from artifacts/reconnect-continue-screen.png, a2256x1385 native window capture during session_20260920_003418_722_307e7ddfedad4817b1b1eaf17398b9cf after automatic Reconnect. Client viewport2254x1353. Assets/fisch_loading_logo.png is crop(0,0,408,242); Assets/fisch_continue_prompt.png is crop(47,281,310,36) within this fixture. Recognition uses an HSV white mask to exclude the colored background and pointer glow, and requires both logo and complete prompt at the observed relative position. Synthetic resizes and erased-logo/prompt negatives do not establish independent live-resolution acceptance.

Required cast-region fixtures, visually reviewed on2026-09-19:

| Fixture | Original local screenshot | Original viewport | Raw crop (x,y,w,h) | Independent label |
|---|---|---|---|---|
| cast_absent_boat.png | RobloxScreenShot20260913_230717375.png | 3426x1360 | 1580,640,100,320 | No cast meter: spawn-shield edge over white boat hull |
| cast_full_bright.png | RobloxScreenShot20260914_222622072.png | 3440x1369 | 1870,450,40,300 | Real full cast meter, bright scene with overlapping game text |
| cast_partial_large.png | RobloxScreenShot20260914_222714368.png | 3440x1369 | 1947,505,40,420 | Real partially filled cast meter, larger apparent scale |
| cast_partial_small.png | RobloxScreenShot20260914_224729353.png | 3440x1369 | 1815,600,40,235 | Real partially filled cast meter, smaller apparent scale |

These are unmodified narrow crops, excluding desktop, chat, full player names and inventory. They preserve the original cap/fill pixels and local occlusions; no synthetic replacement or rescaling. Source files remain private/ignored. The full-screen local test previously assumed every filename except222731751 was a casting positive. Direct review showed230717375 has no cast meter, so its expected label was corrected; the test was not removed or disabled. The boat crop reproduces the same false100% detection, which used a sparse green arc over a white hull as a meter. These required crops make the three positives and this negative available in CI; they do not replace full-screen clutter coverage or an independent-PC validation set.

SHA256: cast_absent_boat.png=3059FE5371DB89EBC80A5E2C36720D107DE7617664076F7D6CAF315261368553; cast_full_bright.png=381F78DEFA9137B716D046EA78B936656B616C931B4087A9480CE6D10EAE43EB; cast_partial_large.png=65E3B10256A1FEE418F14E5C056D2531150D8A66CA7D832A8D2C22A95D96C236; cast_partial_small.png=2070EB394517E243F5DF113E78BA806DB75D4A88559480306E691F3A23D0773C.

### Stacked player, companion fish, and bait rewards

`catch_stacked_rewards.png` is the unmodified recorded frame 5000 from local session `session_20260920_012625_077_908560d8b2a64a5f82bc177edbdb49d5`. Viewport 2254x1353; capture rectangle (383,1001,1488,270). Direct review shows the player's Mother Nature Destroyed Fossil 3.6kg catch above companion fish and bait rewards. No player names, chat, or inventory are present. SHA256: `0DFCF7F3884E9DB2ACEB32394AA1B9B12C08834E9D49755BADD1D6E50766A908`.

The detector previously discarded the first eight rows at this viewport height, cutting the player prefix and producing Unknown. The required fixture regression fails before removing that inset and passes afterward; cropping away the player row retains a companion-only negative. The actual worker replay places the raw crop at its recorded client origin and requires a confirmed catch followed by the next cast.

### Overnight death screen

Production `Assets/death_wasted.png` is a byte-identical copy of the fixture described below. Detection extracts logo rectangle(275,30,290,150) and title rectangle(220,190,410,95) in memory and requires both at the recorded geometry. Positive scaled tests derive from this same capture; independent variants and live acceptance remain open.

`death_wasted.png` is unmodified frame252658 from session `session_20260920_042849_142_21fa82649a944a9e9a031ad8304263a3`. Viewport2254x1353, capture rectangle(708,406,838,541). Direct inspection shows the Fisch death logo, WASTED text and return-to-action subtitle; no player names or chat are present. SHA256 `EDAA338C9630653E2E861190BB7581E1D7AEFB4FF86934DA71FA255BEF2B7FBD`. It is a negative for Reconnect/Continue and a worker waiting/Stop fixture, not proof of server shutdown or automatic respawn.

### Consecutive reel-to-player-catch animation

`catch_sequence/` contains unmodified frames467..475 from session `session_20260920_015859_519_4c5a71f8c4fe430ba8b91b1204cd2f21` on the stacked-catch corrected build. All nine were directly inspected. Frames467..470 show the moving active reel,471 shows its downward exit animation,472..475 show the actual player Negative Scrap Metal3kg reward fading in. No chat or player identifiers are in these track crops. `sequence.json` retains each original frame ID, timestamp, capture/viewport geometry and SHA256; expected labels are from direct image review, not app outcomes. The test uses one VisionProcessor across the sequence, verifies no premature player catch, retains active reel detection before animation, and requires two separately recorded banner matches by the settled frames. It covers this chronological transition, not a whole cast/lure cycle or native input delivery.

Aquarium fixtures `aquarium_1.png`, `aquarium_4.png`, `aquarium_5.png`, and `aquarium_9.png` derive from the correspondingly numbered frames of first-PC session `190214_817_e8156312e4e64b15a6cb675af27e9905` (3424 x 1353). They preserve only the top Aquariums control, Profit/Claim/balance region, close X, and central reward notification excerpt; other pixels are black. Desktop, macro preview, player list, and unrelated inventory are removed. Frame 4 is unclaimed; frame 5 has zero balance after the manual claim; frame 9 has the aquarium closed while the old redeemed toast remains. Assets/Workflows contains native-resolution control crops from frames 1, 4, and 5. Scaled/center-cropped variants are derived geometry regressions from this same development recording, not independent live validation.

### Seraphic Rod Tilted Catch Bar
`reel_seraphic_tilted_bar.png` is unmodified frame 29 from session `session_20260926_034650_040_30e89b1894a94c94860a42549f5dde63` (viewport 3440 x 1369, track ROI 968, 1013, 1504, 273). It captures the Seraphic Rod's 764px wide catch bar tilted at -5.8 degrees due to "The Divine Judgement" screen shake. Used in `ReelTrackingRegressionTests` to verify rotated rectangle (`Cv2.MinAreaRect`) detection under visual rotation without losing track.

### Cyan control bar, September 26

`reel_cyan_left.png` and `reel_cyan_center.png` are unmodified frames 160 and 172 from
`session_20260926_153223_142_af9f0c0fbc324f3db404bd36aa1e0caf`, app 1.0.17.0,
viewport 3440 x 1369, ROI (968, 1013, 1504, 273). Direct inspection shows a cyan
control bar, slate needle, and white progress meter over blue scenery. No chat or
player identifiers appear in these crops. The journal repeatedly reports
"Bar Disappeared / Concluding..." with zero bar bounds while the needle is tracked.
Frame 160 SHA256: `0DFAA119DDC3CC6E1EEF8D8AE024F40A44F1DB2CA38FBB33BBB84D146C437D4E`.
Frame 172 SHA256: `4A491014D4AE0F9261CC7575294105F69DEEA7DBBB54B7689C5C12F0FABA78EB`.
Negative variants erase the needle or progress meter. The worker replay resizes
frame 160 to its test viewport and verifies reeling presses and a subsequent catch;
this is simulated input verification, not a new live game run.
# Crate workflow fixtures (2026-09-26)

Loss/reel regressions (September 27, evening): `reel_streak_ended.png`, `reel_losing_control.png`, and `reel_losing_control_dim.png` are the captured reel-only strips from frames 840, 820, and 826 of session `session_20260928_002026_018_9b31c04ab81a47dbbdc1148819dfa842`. Their viewport is 3440x1369 and capture region is (968,1013,1504,273). They contain reel controls, bait text and a streak-ended message, with no player names or chat. `Assets/catch_loss_prefix.png` and `Assets/catch_loss_suffix.png` are crops (449,87,319,40) and (877,87,180,40) from the streak-ended strip. Scaled variants are synthetic recognition checks, not multi-device acceptance.

`crate_reward_long_name.png` preserves only the reward region (2750,945,690,115) from frame 78 of local session `session_20260927_192851_103_2ae0389d2aac419c9ecd06082477b4ca` (3440x1369). The frame visibly says Opened Sparkling Big Electric Bait Crate despite an unconfirmed workflow outcome. Other pixels are black. Compact prefix/suffix templates crop (3070,994,58,24) and (3368,993,57,26). Synthetic aspect-ratio/scale variants are regression probes, not real-device acceptance evidence.

`crate_ui_14.png`, `crate_ui_15.png`, and `crate_ui_16.png` retain UI regions from
the matching frames of local passive demonstration
`session_20260918_210223_377_bf9e92419b4e4db79dbab0632e2fbb1e` (3440 x 1369).
Other pixels are blacked out. These show selected inventory, quantity 1, and an
unobstructed manually opened Quality Bait Crate reward respectively.

`crate_wrong-filter.png`, `crate_quantity-two.png`, `crate_quantity-blank.png`, and
`crate_fish-search.png` retain central UI regions from frames 9, 21, 33, and 5 of
`session_20260918_190519_705_1b604b6d7aeb4bcfac19dfcd326bd8da` (3424 x 1353).
The wrong-filter frame searches `crates`, which is deliberately NOT accepted as
empty inventory. These are regression examples, not automated live acceptance.

Current inventory fixtures preserve the UI band (1200,950,1020,419) on a 3440x1369 canvas. crate_inventory_current.png comes from the September 26 computer-control screenshot (title bar removed); crate_inventory_native.png comes from frame 5 of the local live-scan-attempt recording. Current header and search templates are native crops (1413,985,91,15) and (1725,1026,43,18). Other pixels are masked; these are two captures of the current layout, not independent resolution validation.

Current reward fixture crate_reward_current.png retains (3100,960,324,100) from frame 7 of artifacts/live-fix6 (3424x1353). Current reward templates crop Opened (3220,984,59,20) and Crate (3348,982,54,21). crate_selected_current.png retains (1200,950,1020,403) from frame 2 of artifacts/live-fix7. These regression fixtures come from live app runs on September 26; all other pixels are masked.

crate_reward_mutated.png and crate_reward_silver.png are masked reward-only regions from local September 27 crate retries (artifacts/crate-speed-live/frame_00000006.jpg and artifacts/crate-speed-live3/frame_00000007.jpg). crate-opened-mutated.png is the Opened text crop at (3143,983)-(3201,1006) from the first frame. Player/chat content is excluded. These cover transparent reward text over changing game scenery.

`aquarium_nav_missed_click.png` retains only the 460x70 top-center navigation strip from frame 1 of local session_20260927_043417_927_7a772cc4e6eb46d999fcdc6dfdd6c208 (3440x1369). Navigation was recognized and clicked once, but all six recorded frames remained in gameplay. The strip contains no player names or chat. The regression protects recognition while the engine test verifies that aquarium clicks no longer enable duplicate window-message input and wait for pointer settling.

`crate_inventory_dense.png` retains only the inventory band (1260,910)-(2160,1369) from frame 103 of session_20260927_043459_187_30a7045798db40ccb4a50622b79c5c5f (3440x1369). The release reported no recognizable crate despite many visible stacks. The rest of the image is black. This reproduces the missed dense white labels without retaining player names or chat.

Aquarium claim animation fixtures 2�8: cropped controls and balance from the user�s local session_20260927_044137_594_ff27fd8daa454fb4b4dd6632510edd41 recording. All other pixels masked; retain native 3440�1369 coordinates. Frames 2�3 contain unclaimed rewards; frames 4�8 show zero cash/XP over changing scenery and reward animations.

crate_inventory_79_stacks.png retains only (1250,890,940,390) from frame_00000005.jpg of session_20260927_190341_077_203d3077cd734b618d545c47ecaa7218 (3424x1353). Everything outside the inventory panel is black; player/chat content is excluded. Assets/Workflows/crate-item-dense.png is the Bait Crate label crop (1410,1231,54,15); crate-search-dense.png is the exact crate filter including caret and right padding (1722,950,56,23). Both retain a 1353-pixel reference height. Source evidence is preserved locally under artifacts/crate-recognition-20260927. Tests include wrong-filter rejection and scaled 1353/1080/1009-height variants.

- aquarium_stalled_controls.png: claim/balance and panel close regions from local session 20260928_233009 frame 38013. All other pixels blacked out, including player list and gameplay. Reproduces scenery-sensitive close recognition.

- hotbar_stall_24276.png and hotbar_stall_24277.png: bottom 342-pixel strips immediately before/after rod unequip in session 20260929_175752. Reproduce loss of hotbar geometry when its selection outline disappears.

- catch_ultrawide_3424.png and catch_ultrawide_3424_next.png: bottom 338 pixels of frames 72 and 74 from session_20260930_030756_983_e243a180f14148fbaf4b080f503144d1 (3424x1353 viewport). The player Pelagic Cod catch is visible but afkfix.2 reported Unknown. First image is cropped at y=1015; second is the original recorded hotbar capture. Player-name and chat regions are excluded. Reproduces unchanged glyph size between 1369- and 1353-pixel viewport heights.

- aquarium_red_label_overlap.png: only close-button and claim/balance rectangles from frame 140293 of session_20260930_123055_602_6e297351b60f49c99b091edcde7c6313, native 3440x1369. All other pixels are black. Reproduces red world-label interference behind the aquarium close X. Player-name/chat regions excluded.

reel_false_coral.png: unchanged gameplay track crop, frame 3230 from session_20260930_172914_139_97e4f1425344462daf2dbaa5d4035663, desktop origin (968,1013), 1504x273, viewport3440x1369. Retains coral and rod-description text; no player names or chat. Reproduces a bar-shaped scenery candidate with no fish needle.

aquarium_navigation_1920.png: top navigation only from user-supplied son's-PC screenshot codex-clipboard-d924607e-fa84-4c31-bfd6-222a6aa990b4.png, confirmed release v1.0.21. Actual image width1919; fixture height1032. Everything outside rectangle(790,0,340,65) is masked black to remove player information. Tests retain native pixels and pad to1080 as a second capture-height case.

## October24 source625 Seraphic latency

Original application crops copied byte-for-byte from session_20261024_221716_409_2c651cc4f2af4b6fa46d7685e1e41e54, build1.0.26-local.20261024.625+a274d18, viewport3440x1369,DPI96. User identified poor centering; Herculean Seraphic and Krill visible. Source session starts2026-10-24T22:17:16.4101847Z (17:17CDT). ThemesAutoCalibrate,profileStandard,AFKPerformance/recordingtrue,preview/adaptationfalse. Actual application crops were adaptive.

| Frame | Capture origin | Size | Monotonic capture-completion timestamp | Reviewed body X | Target X |
|---|---|---|---|---|---|
|150|917,920|1607x382|7225914798289|1165..2276|1720|
|158|686,825|2015x544|7225930322024|1032..2144|1655|
|315|917,920|1607x382|7226789166739|1087..2199|1689|

Frequency10000000. Body bounds were reviewed from the visible original shoulders/walls, target from the capsule axis; original raster/tilt permits the stated4px body/3px target test tolerance. No resizes, palette changes, padding or rotations. Frame158 and315 retained clear body, needle and meter despite app vision latency4889.66/3139.35ms. Original625 offline frame158 took5071.84ms without tracing. These three nonconsecutive observations test geometry/cost, not a chronological catch or Perfect award. Complete raw journals and additional surviving frames remain private in the main ignored artifacts directory. The independent flow recording lacks a completion summary after interruption; do not certify it as complete video. Tests: Seraphic625RecordingTests.OriginalSlowFramesKeepReviewedGeometryAndBoundedRepeatedCost.

## Webweaver638

Ported reviewed evidence from archive e583ded; originals remain in ignored
artifacts/webreaver-review-20261021. Session
session_20261022_003331_788_db8de98de7ae48ad970e5dbb02fa1de9,
build1.0.26+4b35889c3dd6e379ad1f90763489d5389fdff0fa,
viewport3440x1369,DPI96,AutoCalibrate/Standard (profile does not identify rod).
All five webweaver PNGs are unchanged1760x569 crops at840,800. No transformations
were made when porting. Native reviewed body left/right-exclusive and target:
21=1354..1807/1611;24=1159..1612/1317;29=1481..1934/1724 (2px tolerance).
Frame16 has a currently observed marker1534 and meter but a hidden body endpoint;
it must not publish a bar. Frame25 visibly contains a player Haddock catch and a
separate Extra Webweaver Spider reward, with no live reel.

Original relative frame times (ms):16=0,21=5219.2672,24=8284.8082,
25=9369.2784,29=13675.6749. The worker uses21->24->25->29 with unchanged relative
differences. Sample holds at70/120ms and neutral35 padding outside retained pixels
are synthetic. They do not establish instantaneous acceleration, continuous
centering or Perfect. No pixels outside this retained ROI are represented as
known gameplay. The separate flow-video timeline is reference-only, not an added
recorded attempt or fixture in this candidate.

Six Assets/ReelShapes references are inherited unchanged from the same reviewed
archive. Pointed patch: frame21-relative754,220,34x126; left cap509,241,94x90;
right cap878,241,96x90. Each has an original grayscale reference and a stable edge
mask. These are local UI reference coordinates, not desktop detection positions.
Nomination requires original current tips, cap/shoulder evidence and an independent
meter. Ornamental ridges do not establish full TrackBounds.

regular_914.png,boat_catch_25.png,styx_6376.png are unchanged inherited archive
negatives for this pointed family. The regular914 source viewport is3440x1369,
crop origin968,1013; the needle erasure1105,80,40x140 is synthetic. Resizes to
720/1080/1369/2160 and grayscale preserve that absence. Boat/Styx source session
metadata is not duplicated in this port; archive e583ded retains its provenance.
Those images only assert that the pointed reader rejects other structures; they
do not certify baseline boat safety, Styx steering, or new ownership of missing
original metadata. File hashes are pinned in RegressionCatalog.json.

Tests: WebweaverRecordingTests, FishingEngineReplayTests.PointedLayoutAcquiresOutsideLegacyCropAndKeepsGuardedInput,
FishingEngineReplayTests.PointedRecordedOrderConfirmsPlayerCatchAndReentersNextReel,
and ReelTimingInstrumentationTests. Repeated benchmark clones are fresh image
observations, not additional game attempts. No personal recordings ship.

The returning-marker regression translates the same frame21 to origin840,625
(-175px vertically), erases only the marker rectangle744,214,52x142, and exercises
four fresh original observations then four bar-only observations. Missing stays
missing. The next measured capture must retain the original marker reference
extent754,220,34x126; restoring the original pixels must reacquire the unchanged
body1354..1807 and target1611. Padding outside the retained crop is neutral35,
not known gameplay. This is a synthetic geometry/reacquisition test, not a new
attempt or measured acceleration. The original failing crop849..1286 and red TRX
remain in returning-marker-red; no coordinate or timing expectation was loosened.

## November 2 reel exit and catch cooldown

`nov02/sequence.json` records original source session, frame ID, monotonic timestamp
(10,000,000 ticks/second), viewport, crop coordinates, and SHA256 for every PNG.
The renamed PNGs are byte-identical to the local recordings. They are required
fixtures: missing files or changed hashes fail the tests. These gameplay fixtures
are test inputs only and are excluded from the application package.

The `oath` sequence is from `session_20261103_021009_646_7b18ee3bfb6141a7832be94b49d453ca`
(November 2, 8:10 PM Central). Frame 176 shows the reel entrance animation with its
meter below the normal capture band. Frames 177–212 show the continuing reel;
the live app incorrectly transitioned to PostCatch at 198 and recorded Unknown
at 212. Frame 194 has a pink body at approximately 1509–1937 and marker 1720;
frame 208 has an olive body at approximately 1056–1588 and marker 1720. Tests allow
10px boundary tolerance and 4px marker tolerance. Frame 201 has a dark effect
crossing the marker and meter and a low-contrast control boundary; current
fish+meter evidence must preserve the reel even when body geometry is unavailable.

The `catch` sequence is from `session_20261103_021658_192_5359b16c59ee416faf6145c6312a2393`
(November 2, 8:16 PM Central). Full context includes the camera change that
triggered scene recovery during catch verification. Frame 99 visibly shows a
Clownfish player catch although the recorded summary reported zero catches.
`Assets/catch_prefix_sand.png` is the unscaled 246x32 prefix at full-frame
coordinate 1414,1113 in frame 99. The caption test excludes 99 and requires two
consecutive other recorded frames to match; active-reel frames must not match.
The existing 0.78 matching threshold remains unchanged.

The worker replay advances by original timestamps, independent of worker state.
All recorded full-context frames from the catch session are included, including
frames 11 and 12 during camera-baseline learning.
It holds sparse saved pixels until the next recording and retains the previous
full context outside subsequent ROIs. Those holds are synthetic; no missing
intermediate gameplay or closed-loop physics is claimed. The catch is replayed
at 20ms and 70ms capture costs and must count exactly once, return to Casting,
and stop with all inputs released. The active-reel sequence must never enter
PostCatch or recast after entering Reeling. This is regression evidence, not a
replacement for live validation.

`November02DelayedCatchDuringSceneChangeFinishesVerificationBeforeRecovery`
separately tests the timing boundary: full recorded frames 74 and 99 supply the
reel and catch pixels, synthetic landmark textures change at 5s, the reel
disappears at 10s, and the caption appears at 11.2s. The 1.2s empty transition
and scene textures are explicitly synthetic. Removing only the PostCatch scene
guard makes this worker test report zero catches; restoring it must count one
and resume Casting. The unmodified-timeline tests alone did not expose removal
of that guard after the detector fixes, so this boundary test is required too.

The `nov04-reel` fixtures are unmodified 1504 × 273 track crops from the
November 4 evening run of `1.0.30-nov04fix`. The `blade` frames came from
the 00:39:18 UTC session on November 5 and the `oath` frames from 00:39:57
UTC. The filename suffix is the recorder frame ID. The sequence tests check
the visible fish and both bar ends against annotated absolute coordinates
with the original capture offset (968, 1013). The worker tests also check the
actual held input on the misleading scenery and slash frames.

`aquarium_nov04_96.png` and `aquarium_nov04_99.png` are unmodified
3440 × 1369 frames from
`session_20261105_033210_179_4f1b81e3291440b59f709e8a17c2a960`.
The Claim lettering is enclosed by a border of the same color. Both frames
show an unclaimed 545.2K C$ and 30.5K XP balance. Tests require the click
to land inside the visible Claim button and reject the uncleared balance.
The workflow test switches to the existing independent cleared-balance
capture only after a claim click, then removes the panel only after a
close click. Its failure variant retains the original unclaimed balance.
The empty-outline negative masks only the lettering in the recorded image.

`shake_companion_18.png` and `shake_companion_26.png` are unmodified
3440 × 1369 frames from
`session_20261105_034628_686_6131ee49851544d2badbfeb03549cba3`.
They show a Shake button near (2616, 475) while a companion reward caption
was incorrectly classified as a reel. The reviewed prefix template
`Assets/companion_catch_prefix.png` is the rectangle (1265, 1113, 168, 31)
from frame 1 of the later 03:46:36 UTC session. Neither test image supplied
the template. The worker test supplies a synthetic equipped hotbar while
retaining the recorded game view and verifies the actual Shake click.

`idle_stats_18.png`, `idle_stats_84.png`, and `idle_stats_111.png` are
unmodified 3440 × 1369 full frames from
`session_20261105_050025_082_c0c29ec171e5490ba040554162a0a6eb`.
The user reported the first cast never happened. Rod-stat lettering below the
idle avatar supplied false paired meter edges while water supplied a false
needle. Tests require both full-context and track detection to reject a reel.
The worker replays frame 18 unchanged, including its equipped hotbar, and must
dispatch an actual 721ms cast hold/release then reach Luring, with dynamic
release both enabled and disabled. No live game response is simulated.

## November 5 overnight acceptance regressions

`overnight-nov05/frame_*.png` are unmodified copies from session
`session_20261105_051105_847_8de46ee3cd9646dbb4c1ae614f096225`, running
`1.0.30-nov04stability`. Frames68679,122050,122060,174184,174219,200836,
200874,213756,213799,226397,226432 are full3440x1369 context captures;
122054-122059 and122061-122062 are1504x273 track captures at(968,1013).
The corresponding timestamped logs and process/build manifests are preserved
under artifacts/overnight-nov04. The stationary needle annotation is1720 +/-8px
across122050-122062; this is independent of the recorded detector output.
Player banners213756,226397,226432,174219 are positive catches;200836 is a
companion-only banner and213799 is idle. Navigation68679 is an earlier held-out
night capture. See docs/REGRESSION-2026-11-05.md for sequence construction,
red/green evidence, and limits. No fixture alone establishes live acceptance.

## November 6 Duskwire

`duskwire-nov06/frame_*.png` are fifteen unmodified3440x1369 full frames from
`session_20261106_152812_841_3f5b95cef5364fa1b6b9cb5516297d7c`, build
`1.0.30-nov05reelrecovery`. The user explicitly identified this9:28AM recording
as Duskwire. `provenance.json` records source frame IDs, timestamps and SHA256.
Ten frames show its black/gradient body and white capsule;1/11/53/69/80 are
idle or vanished-reel negatives. Pixel annotations are independent of recorded
telemetry.720/1080 variants are generated scaling checks, not separate captures.
Component-removal tests copy idle pixels over one control in memory; they are
explicit controlled negatives. No recorded player catch succeeded in this clip.

## November 6 Duskwire pool failure

Three unmodified3440x1369 frames768/769/770 from session
`session_20261106_163043_380_b7282b2309f042b59f2b63895d55750f`
are saved in `duskwire-pool-nov06`. Frames768/769 show a70px body while
the installed macro remained Luring;770 shows the reel gone. Hashes and
timestamps are in provenance.json. Earlier pool attempt reel images expired,
so its identical cause is not independently proven. Tests check measured
geometry, disappearance and real worker entry/input. Scaled variants are
synthetic resolution checks.

## November 6 Megalodon pool

`duskwire-megalodon-nov06` contains ten unchanged frames from session
`session_20261106_164941_702_ca49fb7d1e8f41c99e499113dced1dac`, running
the original Duskwire build. It shows an ongoing reel at a Megalodon pool,
while the macro repeatedly concluded and re-entered it.1507/1536/1555/1580/
1597/1617 are full3440x1369.1566/1620 are1504px active track captures at
(968,1013);1574 is3440x342 at(0,1027), captured during PostCatch.1821 is
unavailable gameplay. Dimensions, actual offsets, timestamps and SHA256 are
in provenance.json. Eight geometry cases use manual pixel annotations;
the worker sequence uses these full frames at controlled700ms intervals,
not a continuous live clip or proof of a completed Megalodon catch.

`duskwire-live-failures-nov07`: four unchanged November 7 failure originals.
Frame 226511 preserves the short capture that cuts off the meter while retaining
the bar/marker; 226508 has no reel, 226524 includes the shifted meter, and 143238
shows a bar far left of the fish before loss. Exact sessions, SHA256, regions
and independently reviewed coordinates are in its provenance.json. Worker
replay positions a two-second clipped interval at entry and after twenty seconds,
and repeats an unresponsive image to test recovery. These controlled scenarios
do not establish delivered live input or an Apex catch.

`duskwire-dual-meter-nov07`: six unchanged full frames from the reported Greater
White Shark failure on November7. At35.0062188seconds, no accepted progress
had been recorded and the macro released input for recovery. Independent RGB
annotations locate the upper fill at x1346 and row1265, growing170..444px.
The saturated dark red left end was excluded by the old low-saturation mask.
The replay uses original timestamps and a deliberate1.6x slow playback to
exercise64seconds of continued progress; a frozen original still times out.
All sources, regions and hashes are in provenance.json. This is controlled
replay evidence, not a reconstructed continuous catch or Apex acceptance.

`random-rod`: reel-region crops at (968, 1013) on a 3440×1369 viewport.
Frames 26, 28, 40, 45, 54, 59 and 62 are from
`session_20261116_024836_363_9d610db806794f3dba6885f2ef82a82a`. They cover
entry, the black marker crossing an arrow, the dark red bar at the left stop,
and the reel leaving the screen. The binary right-arrow reference comes from
frame 28. Frame 324 is a wider, single-arrow bar from
`session_20261116_022519_210_ed117f18e7da462a87a2d89bafe89e84`.
Auto detection measured both the fish and the bar on every frame from 26
through 59 of the last reel. Frames 23–25 and 60–64 did not report a live reel.

`ruinous_hotbar.png` shows the equipped rod in the November 16 dragon session
`session_20261116_170635_354_4faa9b2b8a7448f2a04a3502bd658c57`. It is the
720 × 90 crop at (1360, 1279) in viewport coordinates from frame 744. The
reference `Assets/ruinous_hotbar_label.png` is the 44 × 11 crop at
(1423, 1335) from frame 735 of the same recording.
