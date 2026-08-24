# Changelog

## 1.0.0 — 2026-08-24

First release.

- Configurable bubble plots over the game library: X, Y, bubble size, colour,
  shape and the hover card each take any column of the game table.
- Any number of saved plots — new, duplicate, rename, delete, and drag to reorder.
- Filters shared by every plot: range sliders for numbers and dates, tick lists
  for categories, multi-value columns de-duped per value.
- Bubble **area** carries the size value, not its radius.
- Categorical colour uses a validated all-pairs palette, separable under
  colour-vision deficiency on both the light and dark Playnite surfaces; numeric
  and date columns use a pickable colour ramp, graded the same way size is.
- Shape encodes a second category; a legend is always drawn.
- Game titles beside the bubbles, with collision handling.
- Right-click a bubble for Playnite's own game menu, borrowed at runtime rather
  than reimplemented; double-click opens the game.
- Drag a bubble along a user-score axis to set that game's score.
