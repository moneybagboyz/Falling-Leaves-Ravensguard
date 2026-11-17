# Falling Leaves: Ravensguard

A medieval open-world sandbox in the spirit of Kenshi, Dwarf Fortress and Cataclysm: DDA, being rebuilt in C# on Godot .NET.

- **Design plan:** [`docs/REDESIGN_PLAN.md`](docs/REDESIGN_PLAN.md) (the single source of truth, including the phase schedule in §5).
- **Look:** [`docs/redesign/concept/ravensguard-tileset.html`](docs/redesign/concept/ravensguard-tileset.html) (open in a browser).
- **Old GDScript version:** [`legacy/`](legacy/), kept as design notes only.

## Status
Phase 0 (foundations, art toolchain, risk spikes) is in progress.

## Layout
```
src/Ravensguard.Sim/       simulation rules, no Godot dependency
src/Ravensguard.Content/   JSON content loading, validation, mods
src/Ravensguard.Game/      Godot 4.7.2 .NET project (rendering, input, UI)
tests/Ravensguard.Sim.Tests/
tools/Ravensguard.Bench/
content/base/              the base game, loaded as a mod
mods/                      user mods
legacy/                    the old GDScript project
```

## Build and run
Requirements: .NET 10 SDK, Godot 4.7.2 (.NET build).

```bash
dotnet build Ravensguard.slnx
dotnet test Ravensguard.slnx

cd src/Ravensguard.Game
godot --headless --import      # first time
godot                          # run the game
godot --headless -- --smoke    # boot test used by CI
```
