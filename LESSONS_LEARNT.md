# Lessons learnt

Non-obvious things that cost real time. Append as we hit them.

## A standalone GitHub repo cannot open PRs to a repo it isn't forked from

**Incident:** `benedictcarter/playnite_charts` was created as a fresh empty repo, intending to use it as the dev repo for changes destined for `JosefNemec/Playnite`.

**Mechanism:** GitHub only allows a pull request between two repositories in the same *fork network*. A repo created independently has no parent, so the PR compare page will not offer it as a head, no matter that the git history matches. Pushing upstream's history into it does not create the relationship — the network link is metadata set at fork time.

**Fix / how to avoid:** Create the repo with `POST /repos/{owner}/{repo}/forks`, which accepts a `name` field — so you can fork *and* pick your own repo name in one call:

```sh
gh api -X POST repos/JosefNemec/Playnite/forks -f name=playnite_charts
```

Renaming a fork afterwards is also safe: the parent link survives a rename. `gh repo fork` did not accept `--clone=false --remote=false` cleanly when run from inside an unrelated git repo; the raw API call is more predictable.

## VS Build Tools 2026 CLI dropped `--wait`, and `--passive`/`--quiet` must start elevated

**Incident:** Two failed attempts to add a workload non-interactively.

**Mechanism:**
- `vs_installer.exe modify ... --wait` fails with **exit 87** (`ERROR_INVALID_PARAMETER`). `--wait` is documented for VS 2019/2022 but is not in the VS 18 (2026) installer's option list — it dumps usage and quits. The parse error is only visible in `%TEMP%\dd_installer_*.log` (`Option 'wait' is unknown`), not on stdout.
- Without elevation, `--passive`/`--quiet` fail with **exit 5007** and the log line `Commands with --quiet or --passive should be run elevated from the beginning`. The installer will *not* self-elevate in these modes.

**Fix:** launch it elevated and poll for completion yourself, since there's no `--wait`:

```powershell
Start-Process "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vs_installer.exe" `
  -ArgumentList 'modify','--installPath','"...\18\BuildTools"','--add','Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools','--includeRecommended','--passive','--norestart' `
  -Verb RunAs
```

Then poll `Reference Assemblies\Microsoft\Framework\.NETFramework\` and the process list. Always read the newest `%TEMP%\dd_installer_*.log` when the installer exits non-zero — the real reason is never on the console.

## A dev build of Playnite is automatically *portable* — it will not touch your installed library

**Mechanism:** [`PlaynitePaths`](source/Playnite/Settings/PlaynitePaths.cs#L67-L87) decides portability with a single line:

```csharp
UninstallerPath = Path.Combine(ProgramPath, "unins000.exe");
IsPortable = !File.Exists(UninstallerPath);
```

There is no `--portable` switch and no config key. Because a build output directory never contains Inno Setup's `unins000.exe`, every dev build is portable, and `UserDataDir` becomes the **bin directory itself** rather than `%AppData%\Playnite`.

**Why it matters both ways:** your real library is safe by default — good. But it also means the dev build starts with a completely empty library every time you wipe `bin`, and that a `git clean -xfd` destroys any test data you set up in it. If you want the dev build to see real data, copy `%AppData%\Playnite\library` into the bin dir; never point it at the live one.

## Only one Playnite can run at a time, dev build included

**Mechanism:** [`PlayniteApplication`](source/Playnite/App/PlayniteApplication.cs#L38-L155) takes a **global, non-namespaced** mutex `PlayniteInstaceMutex` (sic — the typo is upstream's) and a named pipe. `CheckOtherInstances()` sees the existing mutex, forwards any command-line arguments to the running instance over the pipe, and exits.

**Consequence, direction 1:** launching the dev build while your installed Playnite is running silently does nothing except focus the installed one — no error, no window, exit code 0. It looks exactly like "the build is broken". Close the installed Playnite first.

**Consequence, direction 2 — the nasty one.** It works identically in reverse, and there the symptom is genuinely alarming. With the dev instance running, clicking your normal Playnite shortcut forwards to the **dev** process and exits. You are then staring at the dev build's empty portable library — same icon, same window title, same theme — and it reads as *"Playnite has lost my entire library and every integration"*. It happened here; three launch attempts left nothing but:

```
24-08 08:25:58|INFO |PlayniteApplication:Application already running, shutting down.
24-08 08:26:19|INFO |PlayniteApplication:Application already running, shutting down.
24-08 08:26:31|INFO |PlayniteApplication:Application already running, shutting down.
```

**How to tell in one command** — check which executable is actually running, don't trust the window:

```sh
powershell -NoProfile -Command "Get-Process -Name Playnite.* | Select-Object Id,Path"
```

A path under `source\...\bin\` is the dev build; `%LocalAppData%\Playnite\` is the real one. Nothing distinguishes them at the mutex level, and `%AppData%\Playnite\playnite.log` records every one of those bounced launches, so it is also the fastest way to confirm after the fact that no data was touched.

## Build without `build/build.ps1`

`build/build.ps1` is `#Requires -Version 7` and exists to produce installers/packages. For dev, skip it entirely:

```sh
./.tools/nuget.exe restore source/Playnite.sln     # packages.config — msbuild -t:restore won't do it
"…/BuildTools/MSBuild/Current/Bin/MSBuild.exe" source/Playnite.sln -p:Configuration=Debug -p:Platform=x86 -m
```

Platform **must** be `x86` — `AnyCPU` is not a valid solution platform here. Expect a wall of `MSB3277` binding-conflict warnings from the test projects; they are pre-existing upstream noise, not something you broke.

## Dev builds point every backend URL at localhost — the first-run errors are expected

[`source/Playnite/Common.config`](source/Playnite/Common.config) is checked in with **placeholder** endpoints:

```xml
<add key="ServicesUrl" value="http://localhost:5000/" />
<add key="UpdateUrl"   value="http://localhost/update/" />
<add key="DocsRootUrl" value="http://localhost:8080/_site/" />
```

The real values are injected at package time by the release pipeline, not stored in the repo. So a dev build logs a full red stack trace on first run:

```
ERROR|FirstTimeStartupViewModel:Failed to get list of default extensions.
  SocketException: ... target machine actively refused it 127.0.0.1:5000
```

That is **not** a broken build — it's the first-run wizard trying to fetch the recommended-extensions list from a services host that only exists in Josef's environment. Update checks fail the same way and for the same reason. Don't chase either one.

## `--shutdown` only reliably works from the *same* build you want to stop

Running `--shutdown` from our dev-build exe against the installed Playnite logged `Application already running, shutting down.` (i.e. the dev process found the mutex and exited) but the installed instance stayed alive with no pipe error logged on either side. Running `--shutdown` from the **installed** exe (`%LocalAppData%\Playnite\Playnite.DesktopApp.exe`) shut it down in ~2s.

Both builds read the same `PipeEndpoint` string, so the cause isn't a config mismatch — most likely a WCF pipe/contract version difference between the release build and ours. Practical rule: stop an instance with its own executable, and never assume a silent `--shutdown` worked — poll the process list.

## The built-in library integrations are not in this repo, and a dev build starts with none

**Incident:** the first dev launch finished the setup wizard with **zero** library integrations offered — not even Steam/GOG/Epic — which reads as a broken build.

**Mechanism:** two things stack up.
1. Steam, GOG, Epic, Battle.net, EA, Ubisoft, Xbox, itch.io, Humble and the IGDB metadata provider are `*_Builtin` **extensions**, maintained outside this repository. Nothing under `source/` builds them; a release gets them at package time. `build/build.ps1` only packs the four *extension templates* (`CustomLibraryPlugin`, `CustomMetadataPlugin`, `GenericPlugin`, `PowerShellScript`) — those are project scaffolding for plugin authors, not the integrations.
2. The wizard's fallback — download the recommended-extensions list — hits `ServicesUrl`, which in the repo is the placeholder `http://localhost:5000/`. So it silently comes back empty.

**Fix:** seed them from an installed Playnite. Note they live in the **user-data** dir, not the program dir:

```sh
cp -r "$APPDATA/Playnite/Extensions"/*_Builtin \
      source/Playnite.DesktopApp/bin/x86/Debug/Extensions/
```

Check `Playnite.SDK.dll`'s file version matches between the two installs first (both were 6.16.0.0 here); a mismatch makes the extensions refuse to load. `dev/run.sh` does this automatically. Because `bin/` is gitignored, a `--clean` build throws the extensions away — always re-seed.

## Backslashes get eaten before Python sees a heredoc script (Git Bash on Windows)

**Incident:** a `python - <<'PY'` script that replaced a Windows path silently did nothing, twice, while printing its own success message.

**Mechanism:** despite the **quoted** delimiter (`<<'PY'`, which POSIX says disables all expansion), `\` arrived at the interpreter as `\`. Proof — the interpreter echoed back a literal it was never given:

```
bad = b'source\...\x08in\'
      ^ SyntaxError: unterminated string literal
```

The consequence is worse than a crash when it *doesn't* crash: `'a\b'` and `'a\b'` both collapse to the same value, so `s.replace(bad, good)` becomes a no-op that still writes the file and prints "fixed". Nothing fails.

**Rule:** never put a backslash literal in a heredoc'd script. Build them from `chr(92)`, or use `Read`/`Edit` instead of a shell heredoc. And when a script reports success, verify the *bytes* — `grep -o … | od -c` — not the script's own output. (The same class of bug produced the defect being fixed here: a `\b` in a Python string is a **backspace**, not a path separator, and `od -c` is the only way to see it — a terminal renders it invisibly.)

## A scatter/bubble plot needs an **all-pairs** colour palette, and the stock one only has 3 such slots

**Incident:** picking the data-viz skill's stock 8-colour categorical palette for the bubble plot and running its own validator with `--pairs all` gave two hard FAILs (`#008300↔#eb6834` CVD ΔE 3.2; `#e34948↔#eb6834` normal-vision ΔE 7.1). Computing all 28 pairwise ΔE showed the largest mutually-compatible subset was **three** colours.

**Mechanism:** most palettes are validated on *adjacent* pairs, which is the right gate for a bar chart or a stacked series — only neighbours touch. A scatter puts arbitrary categories next to each other, so every pair must separate. The gate is a **max-clique problem** over the compatibility graph, and cliques shrink fast: 8 adjacent-safe colours ≠ 8 all-pairs-safe colours.

**Fix:** search for a bespoke palette rather than eyeballing substitutions, then validate with the skill's script (`ALL CHECKS PASS`, both modes). The search itself has a trap: iterating light and dark palettes independently is ~1.2 billion ΔE evaluations and times out. Reformulate as a **paired-candidate graph** — a node is `(hue, lightStep, darkStep)` and an edge exists only if the light pair *and* the dark pair both clear the gates — then run a randomised greedy clique over it. 8 seconds instead of never. The palette lives in `Controls/PaletteData.cs`; don't hand-edit a hex without re-running the validator.

## Playnite's SDK already has a `GameField`, and its MVVM base classes don't behave like the usual ones

Three collisions cost a build cycle each when writing the Charts extension:

- **`Playnite.SDK.Models.GameField`** exists (it's the field-name enum used by bulk edits). Any file with both `using Playnite.SDK.Models;` and your own `GameField` gets `CS0104: ambiguous reference`. Ours is now `GameColumn`.
- **`ObservableObject.SetValue` returns `void`**, not `bool`. `if (SetValue(ref field, value)) { ... }` — the idiom from CommunityToolkit/Prism — doesn't compile, and there is no built-in "did it change" guard: compare first yourself, or every set fires a notification.
- **`RelayCommand` has no `RaiseCanExecuteChanged`.** Its `CanExecuteChanged` forwards to `CommandManager.RequerySuggested`, so WPF re-queries on its own; calling the method you expect to exist is a compile error, and there is nothing to call instead.

## A theme cached before the control is parented stays wrong forever

**Incident:** the light-mode render came out with pale text on white and navy grid lines — the *dark* palette drawn on a light surface.

**Mechanism:** `BubblePlotControl` resolves its surface colour by walking up the visual tree for the first opaque background, and cached the result on first draw. Setting the `Model` in an object initialiser (`new BubblePlotControl { Model = m }`) triggers the first `Redraw()` **before** the control has a parent, so the walk found nothing and cached the dark fallback. In Playnite the `Loaded` handler invalidates it and it self-corrects; offscreen (`RenderTargetBitmap`) `Loaded` never fires, so it never did.

**Rule:** re-resolve the ambient surface on every redraw and rebuild the theme only when the colour actually changed. It's a few parent hops — far cheaper than being wrong. The same applies to any "sample the host theme" caching: the first draw is the least trustworthy moment to sample.

## Rendering the chart offscreen is the only practical way to look at it

Playnite holds an exclusive lock on its LiteDB files while running, so a dev instance can't be seeded by copying `%APPDATA%\Playnite\library\*.db` — every file is "Device or resource busy". `extensions/PlayniteCharts/DevHarness` sidesteps the whole problem: it fakes `IGameDatabase`, generates a synthetic library, and renders the control to PNG with `RenderTargetBitmap` — no Playnite, no window, no screenshot. Two gotchas worth knowing:

- `Game`'s navigation properties (`Source`, `CompletionStatus`, `Genres`, …) resolve through `internal static Game.DatabaseReference`. Reflection sets it; without it every categorical column silently reads null.
- **.NET 4.6.2 has no `System.ValueTuple`** — `(string, Color)` tuples give `CS8179: Predefined type 'System.ValueTuple`2' is not defined`. Either add the NuGet package or use a small class. Extensions target 4.6.2, so this bites in every extension project.

Offscreen rendering also needs a nudge to show hover state: there is no mouse, so the harness reflects into the control's private `hovered` field and calls `RedrawOverlay()`.

## Never mutate a Selector's items (or its selection) from inside its SelectedItem setter

The plot list had a sentinel "New" row: `ListBox.SelectedItem` was bound two-way to a
view-model property that, on seeing the sentinel, created a plot, inserted it into the
bound collection and re-pointed the selection at it. Clicking "New" froze Playnite and
then killed the process with nothing in `playnite.log` — the signature of either a
StackOverflowException or an OOM, neither of which gets logged.

The mechanism: WPF's `Selector` is *mid selection-change* when the binding writes to the
source. Mutating `ItemsSource` and pushing a new `SelectedItem` from inside that write
re-enters `Selector`, which can bounce the selection back to the sentinel — and the
setter obligingly creates another plot. Every iteration also ran a full model rebuild
over the library, so it locked up before it died.

Rules that came out of it:

- A list row that *does something* is a `Button` styled to look like a row, never a
  selectable item with side effects in the setter. Selection means "look at this", never
  "do this".
- If a selection setter genuinely must mutate the collection, defer it
  (`Dispatcher.BeginInvoke`) and guard against re-entry — but prefer the button.
- Uncatchable crash + empty log = stack overflow or OOM. Look for a re-entrant property
  setter before anything else.

## Anything that touches every game must be lazy

`Rebuild()` also materialised the table (one string per column per game). Once hover
defaulted to *all* columns, that was 36 columns x N games on the UI thread — including
regex HTML-stripping of every description — for a table that was not even visible.
Measured in the DevHarness: 5,000 games x 36 columns = ~1.3 s. Playnite Desktop is x86,
so the string churn is also real memory pressure. The table now builds only when the
Table toggle is on, and `Rebuild()` logs its own elapsed ms so the next slowdown is
visible in `playnite.log` instead of being guessed at.

## Playnite locks its extension DLLs — deploy has to close it

`cp` into `%APPDATA%\Playnite\Extensions\...` fails with `Device or resource busy`
whenever Playnite is running: the CLR maps the loaded assembly and Windows refuses
to replace an open image (no delete-then-write trick either — the lock is on the
file, not the name). There is no extension hot-reload in P10.

`dev/deploy-extension.sh` now does it itself: graceful `taskkill`, force after ~3 s,
copy, relaunch if it had been running. Graceful first matters — Playnite's library
is LiteDB and a hard kill skips its flush.

## A ContextMenu is a separate tree — it inherits neither DataContext nor theme brushes

The "Add filter" menu hangs off a Button but renders in its own popup window, and
that costs you twice:

1. **DataContext.** `RelativeSource AncestorType=UserControl` inside the menu finds
   nothing, because the menu is not a descendant of the view. The handler has to
   assign `menu.DataContext` itself (bindings *inside* the menu can then use
   `AncestorType=ContextMenu`).
2. **Theme resources.** `{DynamicResource TextBrush}` inside the menu can fail to
   resolve — the lookup walks the popup's own tree and then the app resources, and
   Playnite's theme brushes are not necessarily reachable from there. An unresolved
   DynamicResource does **not** fall back to inheritance: the property drops to the
   MenuItem default, which is near-black text on Playnite's dark menu — legible only
   if you squint. Setting `menu.Foreground` alone does not save you either, because
   the MenuItem's own style setter beats inherited values.

The fix that actually holds: resolve the brush with `TryFindResource` **on the
button** (which really is in the themed tree), then derive the item style in
code-behind — `new Style(typeof(MenuItem), menu.ItemContainerStyle)` plus a
`Foreground` setter — and assign it once. That beats both the unresolved setter and
plain inheritance.

Rule of thumb: anything in a popup (ContextMenu, ToolTip, Popup) needs its
DataContext and its theme brushes handed to it explicitly.

## Scope a setting to the question it answers, not to the object it sits next to

Filters, hover columns and bubble sizes started life on `PlotConfig`, because that
is where the settings panel drew them. Wrong axis. What the user does is *filter to
a set of games and then flip between plots to look at that set different ways* -
and per-plot filters silently changed the data under the visualisation every time
the plot changed. The saved plot answers "what is mapped to what"; the filters
answer "which games am I looking at". Two questions, two lifetimes.

They now live on a single shared `ViewSettings` on `ChartsSettings`. The tell that
this was the right cut: `SelectedPlot`'s setter lost its `SyncFilters()` and
`SyncHoverOptions()` calls entirely — switching plots is now a pure re-map.

The migration is the part worth copying. Deleting the properties from `PlotConfig`
would make the old JSON silently unreadable (unknown members are dropped, so an
existing settings file loses the hover columns the user picked). Instead the
properties stay as plain non-notifying auto-properties marked legacy, a
`HasLegacyView` flag spots an old file, `ViewSettings.FromLegacyPlot` lifts the
values off the last-selected plot, and `DropLegacyView()` blanks them so the next
save is clean. **Serialized settings are a schema — you can move a field, but only
if something reads the old location once.**

## A zero-anchored area scale stops encoding anything once the user filters

Bubble area is anchored at zero on purpose - twice the value is twice the ink, and
that is the honest default for magnitude. But the anchor is only honest while the
data still spans its natural range. Filter playtime to 200-400 h and every bubble
maps to sqrt(0.7)..sqrt(1.0) of the max radius: the size channel silently goes
dead, all marks look identical, and nothing on screen says why. A date size column
was worse - a date's zero is December 1899, so *every* release date already came
out near-max, filtered or not.

The rule: anchor at zero while the column spans its own domain; span the range
instead as soon as the range is a **window the user chose** (a narrowed filter) or
the column has no meaningful zero (dates). Filter bounds are stored as null at the
domain edge, which makes "has the user actually narrowed this?" a null check rather
than a float comparison against a moving domain.

Generalises: any encoding whose baseline is "zero" has to ask whether zero is still
on the chart. If it is off-screen, the baseline has to move with the view.

## An extension is inside the app it is not allowed to reference

Extensions compile against the SDK, and the SDK deliberately hides the desktop app.
But an extension is *loaded into* Playnite.DesktopApp.exe - the desktop types are
sitting in the same AppDomain, fully public, just not on the compile-time
reference list. `AppDomain.CurrentDomain.GetAssemblies()` plus a bit of reflection
reaches them.

That is how the chart's right-click menu works: it instantiates
`Playnite.DesktopApp.Controls.GameMenu`, hands it the live
`GamesCollectionViewEntry` off `DesktopApplication.Current.MainModel.GamesView`,
and opens it. The menu builds its own items on every `Opened`, so anything upstream
or another plugin adds to the games-list menu appears in ours for free, forever,
with nothing to maintain. Reimplementing it would have been a permanent divergence.

The conditions this is only sane under: **probe once and degrade to nothing** (the
DevHarness and fullscreen mode have no such assembly, and an upstream rename must
mean "no menu", never a crash), and **only for a whole UI component you hand data
to** - reflecting into internal *state* would be a different, much worse bet.

Gotcha found on the way: `GameMenu` reads its DataContext as a
`GamesCollectionViewEntry`, not a `Game`. Take the live entry from `GamesView.Items`
(which is unfiltered - `CollectionView` is the filtered view over it) rather than
constructing one; a grouped view holds several entries per game, and any of them
carries the same Game.

## A ramp has two ends and both of them can disappear into the surface

The first cut of the colour ramps was textbook sequential - light to dark, one hue.
On the dark chart surface (#151d38) the bottom third of every ramp was gone: a
low-scoring game drew #123a5e on #151d38 and simply was not there, with the 2px
surface ring eating what little edge was left. The light surface (#f5f5f5) had the
identical failure at the other end, where #dfecfb is white with an opinion. The
diverging ramps failed in the middle instead - a neutral #dedede midpoint on a
#f5f5f5 surface is an invisible midpoint.

The rule a categorical palette teaches - step each colour for the surface it is
drawn on - applies per RAMP END, and a diverging ramp has three places to check,
not two. On dark, sequential runs mid-tone to bright, not near-black to bright;
the greys in a diverging ramp have to move away from the surface in whichever
direction there is room.

Only visible because the harness renders every ramp on both surfaces. Colour is the
one thing you cannot review by reading the hex - render it and look at it.


## Playnite's `ObservableObject` lives in `System.Collections.Generic`

Removing a *seemingly unused* `using System.Collections.Generic;` from a file that
had no generics left in it broke the build with

```
PlotConfig.cs(13,31): error CS0246: The type or namespace name 'ObservableObject'
could not be found (are you missing a using directive or an assembly reference?)
```

which points at the base class, not at the using that was deleted. The SDK really
does declare it there:

```powershell
[Reflection.Assembly]::LoadFrom('...\Playnite.SDK.dll').GetTypes() |
  ? { $_.Name -eq 'ObservableObject' } | % FullName
# System.Collections.Generic.ObservableObject
```

So in every file deriving from `ObservableObject`, `using System.Collections.Generic;`
is load-bearing and `using Playnite.SDK;` is not what resolves it. A tidy-up pass
that strips usings by "does this file mention `List<>`?" will break exactly those
files. Don't trust the compiler's error location to name the edit that caused it -
it names the symbol that stopped resolving.
