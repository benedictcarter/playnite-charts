# DONE

## 2026-08-24 - UAT round 1
- Top-right toolbar cleared: "Refresh" and the "Table" toggle are gone, and the
  table view with them (Playnite's own list view already shows the rows as text).
- "Use library filter" moved down under FILTERS, where the rest of the filtering is.
- Saved-plot list is sized by its contents: it grows a row at a time and leaves no
  empty box under "New", scrolling only past ~15 plots.
- Saved plots reorder by drag and drop.

## 2026-08-24 - released 1.0.0 and listed in the add-on database
- Tagged `v1.0.0`, packed with Toolbox and attached the `.pext` to the GitHub
  release, which is what `InstallerManifest.yaml` points the updater at.
- Manifest PR to PlayniteAddonDatabase merged as `addons/generic/benedictcarter_Charts.yaml`,
  so Charts now shows in Playnite's own add-on browser and can auto-update.
- Direct install URI: `playnite://playnite/installaddon/PlayniteCharts_8a4f2c10-5c1e-4b2a-9d3f-6e7b0a1c4d55`
- Fixed on the way out: the hover column list grew on every settings load,
  because Json.NET appends to a list a property already holds rather than
  replacing it, so a non-empty field initialiser re-appended itself each time.

## 2026-08-24 - extracted into its own repo
- The extension began life in a fork of Playnite (`benedictcarter/playnite_charts`,
  branch `charts`, deleted once the extraction was done). Nothing under `source/`
  was ever modified: the extension only
  ever referenced the SDK from NuGet, so the fork was not needed to build it.
- Split out here with its own history (18 commits replayed from the fork), the
  extension at the repo root. The fork stays on GitHub as the record of the
  fork-era work.
- Review pass before extraction: dead code removed, duplicated helpers merged,
  the legacy per-plot settings migration dropped, comments cut back to what the
  code does not already say. Net ~140 fewer lines, no behaviour change beyond one
  fix (a numeric colour column was missing from the table view).

## 2026-08-24 - bubble plot
Built as a Playnite extension, not a core change: plugin sidebar items of type
`View` already sort directly below Statistics, so the requested placement needed
nothing from upstream.

- Sidebar `Charts` tab below Statistics, saved-plot list between the tab strip and the plot
- X / Y / size / colour / shape / hover each pick any column of the game table
- Configs persist in plugin settings; new / duplicate / delete
- Custom-drawn plot: axes, ticks, legend, hover tooltip, click-through to the game
- Bubble **area** (not radius) carries the size value
- Validated all-pairs colour palette + shape encoding, light and dark surfaces
- Pickable colour ramps for numeric colour columns, graded like size
- Filters (range sliders, category tick lists) shared across plots, rescaling the channels
- Game titles beside the bubbles with collision handling
- Playnite's own game menu on right-click, borrowed by reflection
- Drag along a user-score axis to write the score back to the library
- Table view as text relief for the low-contrast palette slots
- Offscreen render harness (`DevHarness`)

## 2026-08-24 - fork setup (superseded by the extraction above)
- Cloned `JosefNemec/Playnite`, forked to `benedictcarter/playnite_charts` via the
  fork API so it sits inside upstream's fork network and can raise PRs.
- Added the `ManagedDesktopBuildTools` workload and the 4.6.2 targeting pack to VS
  Build Tools 2026, restored and built `Playnite.sln` clean (Debug/x86).
- Ran the dev build portable for UAT, with the `*_Builtin` integrations seeded from
  the installed Playnite; confirmed `%AppData%\Playnite` was untouched throughout.
