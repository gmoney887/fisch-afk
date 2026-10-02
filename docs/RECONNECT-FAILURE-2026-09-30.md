# Overnight reconnect failure — September 30, 2026

Source: session_20260930_035840_304_2a08667e8eed43f898229a57685c50b8, released v1.0.21, launched from Downloads. Complete retained evidence copied to artifacts/idle-disconnect-20260930 before retention can rotate it.

All times America/Chicago. The session started September 29 at 22:58:40. It recorded 580 confirmed catches, zero losses, and zero unknown catches. The last confirmed catch was September 30 at approximately 00:34:05 (95 minutes 25 seconds into the session).

At approximately 00:34:32 the recording shows a connection-error dialog: “Please check your internet connection and try again. (Error Code: 277)”. This is visible in frames 49076 and 49079. The macro logged a Reconnect click at frame 49079. At 00:36:33 it reacquired gameplay and equipped/cast the rod. The retained frames show the character near the appraiser/shops after rejoining, rather than at the prior fishing spot. That cast timed out. Rod reset failed visual confirmation and the five-minute recovery deadline paused automation at 00:42:01. The last recorded heartbeat was 00:40:06.

The user's subsequent screenshot shows error 278, idle for 20 minutes. That later screenshot is consistent with inactivity after automation paused, but its capture time is not established from the recording. It does not establish that idle detection caused the original fishing interruption. The initial failure is directly recorded as error 277.

The reconnect click succeeded at returning to gameplay; fishing-location recovery is absent. A visible hotbar is insufficient evidence that the character has returned to a usable fishing position. Heartbeats cease when the worker pauses, so the stopped state does not maintain the connection or monitor another disconnect.

This investigation makes no code changes and does not claim the missing navigation is fixed. A verified route or destination-aware navigation is required to restore fishing after rejoining. Blind movement or continuing heartbeat alone would not establish fishing recovery.
