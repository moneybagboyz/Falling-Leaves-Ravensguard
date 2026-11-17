# Ravensguard.Game (Godot shell)

Godot 4.7 .NET project. UI and input plumbing only; no game state lives in nodes (plan 2.1a).

## Run
```
dotnet build Ravensguard.Game.csproj
G=/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64
$G --headless --import                 # once, imports fonts and localization/en.csv
$G                                     # run the game
$G --headless -- --smoke               # boot, show main menu, quit (exit 0)
$G --headless -- --check-keymap        # print default-keymap conflicts; exit 1 if any (CI gate)
```
Flags: `--dev` enables the dev console in non-debug builds (debug builds always have it; toggle with backtick).

## Structure
- `scripts/App.cs` entry: settings, input registration, ScreenRouter, MainMenu, DevConsole, CLI flags.
- `scripts/Settings.cs` display/accessibility settings (`user://settings.cfg`), integer UI scale 1-3x.
- `scripts/L.cs` localisation helper; all strings live in `localization/en.csv` (registered in project.godot as `en.en.translation`).
- `Input/` `InputDefs` (actions per context, mirrors plan 2.8), `KeyMap` (pure model, `FindConflicts`), `InputBootstrap` (InputMap registration, `user://input.cfg`). InputMap action names are `<context>.<name>`, e.g. `world.move_up`.
- `UI/` `ScreenRouter` (stack, modals, toasts, Esc = `menu.back`), `MainMenu`, `SettingsScreen` (Controls rebinding + Display), `DevConsole`, `PixelTheme` (theme built in code).
- `assets/fonts/` Pixelify Sans (OFL, see OFL.txt).

## Adding strings or actions
Add the action to `InputDefs` (one `A(...)` line), then add `ACTION_<CONTEXT>_<NAME>` to `en.csv`. Re-run `--import` after editing the CSV.

## Notes / TODO
- Contextual mouse gestures from plan 2.8 that are not plain bindings (feint, box-drag select, shift-click, middle-drag) are not defined yet.
- The `?` overlay is bound as Shift+/ (US layout); revisit for other layouts.

## Street view (first playable)
`World/StreetView.cs` is the live view: New Game opens it. The sim (`Ravensguard.Sim/Live`, `Lighting`) owns all state; the view reads `LiveWorld.Snapshot`.
Art atlases are the baked output of `tools/Ravensguard.Art` copied to `assets/art/` (re-run `bake`, then copy `art/out/*.png` and `index.json` there).
Command line (after `--`): `--shot-street <hour> --shot-out <png> [--shot-start x,y,z] [--shot-level z] [--shot-zoom 1|2] [--shot-torch 0|1] [--shot-click px,py] [--shot-frames n]`
captures a screenshot and quits; `--street-bench <seconds>` runs a scripted walk and prints tick/frame timings.
