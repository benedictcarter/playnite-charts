# Charts for Playnite - working notes

A Playnite extension (GenericPlugin + a `View` sidebar item): configurable bubble
plots over the game table. See [README.md](README.md) and [DONE.md](DONE.md).

## This repo is self-contained

The extension compiles against **one** reference: the Playnite SDK from NuGet. It
needs no clone of Playnite itself. `Interop/DesktopGameMenu` reaches into
`Playnite.DesktopApp` purely by reflection at runtime, inside the app's own
AppDomain, so there is nothing to reference at compile time.

The Playnite fork this was extracted from lives at
<https://github.com/benedictcarter/playnite_charts> (branch `charts`) and keeps the
fork-era history. Nothing in `source/` was ever modified, so there is no core
change to send upstream - an extension reaches users through a manifest PR to
[PlayniteAddonDatabase](https://github.com/JosefNemec/PlayniteAddonDatabase), not
through a PR to Playnite.

## Toolchain

- .NET Framework **4.6.2**, WPF, old-style `.csproj` (globs `**\*.cs`, so a new
  file needs no csproj edit) with `packages.config`. Playnite SDK 6.16.0.
- MSBuild from VS Build Tools 2026:
  `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe`
- `nuget.exe` is fetched into `.tools/` by the dev scripts (git-ignored).
- Playnite Desktop is **x86**, so anything that touches every game must be lazy.

## Build, deploy, look at it

```sh
dev/deploy-extension.sh          # restore + build + close Playnite + copy + restart
dev/render.sh <out-dir>          # offscreen PNGs of every chart state, no Playnite
```

Always render and look at the output after touching colour or layout.

## Style

Follows Playnite's own conventions, so the code stays submittable: private fields
`camelCase` (no underscore), all methods `PascalCase`, 4 spaces, blank line after a
closing `}` followed by more code, always brace `if`/`for`/`foreach`/`while` bodies.
