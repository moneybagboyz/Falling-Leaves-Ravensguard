# Modding for players: the guide, the example mod, and the player blueprint editor through the loader

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.9 (all content is data; the base game is a mod; load order, overrides by id, patches, readable conflicts; JSON with comments checked by schemas; migration tables; one blueprint format for world generation, the build tool, contracts and mods; checked in CI), §5 Phase 8 (documentation, an example mod, the player-facing blueprint editor). C# script mods stay after 1.0.
Carried in: Phase 5 S13 (the player's blueprints read outside the loader), S10 (swallowed plan and table exceptions), a general water check in `PlanFitter` and taking down generated buildings (phase-6 overview).

## 1. Goals
1. A player can write a mod from the guide alone and see it load, with readable errors.
2. Blueprints the player draws are mods, loaded through the same `ModLoader` and checks as the base game's.
3. Every load failure names its file, line, field and cause.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Content/ModLoader.cs`, `ModManifest.cs`, `ContentValidator.cs`, `ContentReport.cs`, the schemas, `content/base/mod.json` | **extended:** schema export, a validate command, the cause in every report row |
| `Game/World/StreetView.BuildMode.cs` reads `user://blueprints/player/*.jsonc` directly (S13) | **changed:** a player mod folder loaded by the `ModLoader` |
| `Construction/PlanFitter.cs` (a water check for powered plans only), `Save/IdTables.cs` | **extended:** the general water check; named causes |
| none | `docs/modding/` (the guide); `mods/example_harvest_fair/`; `Game/UI/BlueprintEditor.cs` |

## 3. Rules

### 3.1 The guide and the example (MD-1)
- **`docs/modding/`:** getting started; the folder and `mod.json`; load order and overrides; patches; migrations; each content type with its schema and one example; the condition and effect vocabulary (generated from `BaseVocabulary`); blueprints; localisation keys for mods; testing a mod; what is not moddable yet (C# scripts).
- **Schema export:** `tools/Ravensguard.WorldTool --export-schemas <dir>` writes the JSON Schemas for editors.
- **Validate:** `--validate-mod <dir>` runs the loader and every content check on one mod over the base game, and exits non-zero on an error.
- **The example mod** `example_harvest_fair`: a new item, a recipe, a landmark, a blueprint, an override of a base value, a migration of a renamed id, and its strings. It is in CI.

### 3.2 Player blueprints as a mod (MD-2)
- "Save as blueprint" writes to `user://mods/player_blueprints/` with a generated `mod.json`. The folder is loaded by the `ModLoader` after the base game and enabled mods.
- **The editor** (`BlueprintEditor`): opens a saved blueprint as its text (`rows`, `legend`, `levels`) beside a live preview; edits are validated on each change (ids resolve, rooms reachable, purposes satisfiable, `RoomDetector` agrees); errors list file, line and field.
- A player blueprint is shared by copying its folder; the save records it in the mod manifest like any mod.

### 3.3 Named causes (Phase 5 S10)
- A plan or table that fails to parse or build no longer reads as "unknown plan". The load report row says which file, line and field, and the exception's message.
- A content table that fails to build saves its name table anyway, so a later load can map by name.

### 3.4 The general water check (phase-6)
- Any plan with a well, a mill, a dye works or a tannery checks for water: a river or lake cell within the plan's `water_m` (content), or a well's dig depth to the water table. The fitter refuses a plot that fails, with the reason.

### 3.5 Taking down generated buildings (phase-6)
- The deconstruct tool works on generated buildings the hero owns or has the right to clear. It is a construction site in reverse: pieces come down by labour, materials go to a pile, mass exact.
- Reusing a lot's standing walls: a plan placed over a lot keeps any wall cell that matches its own.

### 3.6 Integer math notes
- Distances in the water check are squared metres in `long`.
- Deconstruction returns each piece's recorded materials in grams, less a content `salvage_pm` loss to waste.

## 4. Saves
The save's mod manifest lists `player_blueprints` when used. Deconstruction is a construction site (Phase 5 section). No new keys.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| MD-1 | Example loads | `example_harvest_fair` loads, overrides one value, migrates one id, adds its five entries |
| MD-2 | Validate | `--validate-mod` exits 0 on the example and non-zero with file, line and field on a broken copy |
| MD-3 | Schemas | exported schemas validate every base content file |
| MD-4 | Guide examples | every JSON snippet in `docs/modding/` parses and validates (a test reads the guide) |
| MD-5 | Player mod | a blueprint saved in game is in the player mod folder and is loaded by the `ModLoader`, not read directly |
| MD-6 | Editor checks | a legend id that does not resolve and an unreachable room are both reported with line and field |
| MD-7 | Missing mod | a save with a player blueprint loads without the folder and the report names what is affected |
| MD-8 | Named causes | a broken plan file gives a report row with its file, line and message |
| MD-9 | Water check | a tannery plot 200 m from water is refused with the reason; 20 m is accepted |
| MD-10 | Deconstruct | a generated cottage taken down returns its materials less salvage; mass exact; the lot is free |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-MD-1 the guide and the example | the guide, schema export, validate, the example mod | MD-1 to MD-4 | 0.6 |
| P8-MD-2 the editor and causes | player blueprints through the loader, the editor, named causes, the water check, deconstruction | MD-5 to MD-10 | 1.0 |

No Steam Workshop at 1.0: mods are folders. Workshop support is an after-1.0 item.
