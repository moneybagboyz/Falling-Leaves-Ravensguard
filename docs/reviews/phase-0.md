# Phase 0 code and bug check

The review covered all Phase 0 code (plan §6, "Code and bug check at the end of every phase"). Two reviewers worked read-only, one on the simulation and content code and one on the game, art tools and CI. Every finding was checked against the code, and most were reproduced with a probe. Fixes were made by two separate passes, each with a regression test where the code can be tested outside Godot.

- **Result:** 28 findings. 26 are fixed. 2 are partly fixed or moved to a later phase, with the reasons below.
- **Tests:** 390 before the review, 445 after, green on three consecutive runs. `--smoke`, `--check-keymap` and the new `--selftest` pass.

## Simulation and content (16 findings, all fixed)
| # | Severity | Area | Defect | Fix |
|---|---|---|---|---|
| 1 | High | Save rotation | Older slots were rotated before the new file was published. A failure in between left no current ironman or autosave slot. | Copy the current slot, then publish with one atomic move. Fault-injection tests run after rotation. |
| 2 | Medium | Manual saves | A save name like `../x` could write outside the save folder. Background write failures were silent. | Only bare file names are accepted. An `OnError` event fires, and the task faults. |
| 3 | Medium | Mod manifest | A BOM in `mod.json` failed to parse. A `null` manifest silently dropped the mod. | The BOM is stripped. A null manifest or empty id is an error with its location. |
| 4 | Medium | Content cache | A cache write failure threw after a successful load. The fixed temp file name could collide. | Cache failures are warnings. Temp names are unique and cleaned up. |
| 5 | Medium | Pathfinding | Dirty flags lived on the shared grid, so a second pathfinder never saw changes. | Revision counters per chunk, with the last seen revision kept by each pathfinder. |
| 6 | Medium | Pathfinding | Zone ids grew without limit as doors and walls changed. | Zone ids are reused per chunk, and search buffers are pooled. |
| 7 | Medium | Level grid | No bounds checks, so x = width wrapped into the next row. | Grid mutators check bounds. `IsReachable` returns false outside the grid. |
| 8 | Medium | Fixed-point | Overflow wrapped silently: `FromInt(40000)` gave −0.39. | Range-checked arithmetic throws `OverflowException`. |
| 9 | Low | Fixed-point | Rounding depended on the sign and differed between operators. | One rule: round toward negative infinity. Tested for both signs. |
| 10 | Medium | Melee | Blows landing in the same tick resolved in id order, so the lower id won mutual exchanges. | Blows resolve against the state at the start of the tick. Melee numbers updated in `docs/spikes/02-melee.md`. |
| 11 | Low | Melee | A fighter who had fled or surrendered could be re-marked as knocked out. | Their outcome is kept. |
| 12 | Medium | Save model | The save snapshot could be changed while it was being encoded. A newer format failed as "corrupt" instead of "newer". | The header is an immutable record and sections are read-only. The format version is checked first. Duplicate mod ids are reported, and access errors are wrapped. |
| 13 | Low–medium | Chunk deltas | `SetTile` had no range check. Adding an object over an existing id dropped its data. The first visit could lose its snapshot. | Operations are validated all-or-nothing. A duplicate add is an error. The first visit keeps its snapshot. |
| 14 | Low | Content cache | The cache key didn't cover the type registry or vocabulary, so a cache hit could skip validation. | Both are fingerprinted into the key. |
| 15 | Low–medium | Melee AI | Attack choice ignored the situation and always used the first attack. | Blunt or point against armour, edge against flesh. Deterministic tie-break. |
| 16 | Low | Tests | The scale-model tests compared constants with themselves. | Pinned to plan §1 literals. |

The plan's hit-chance rule was also corrected to match resolver v1: zone factor = 0.2 + 0.8 × relative size (plan §2.6).

## Game, art tools and CI (14 findings: 13 fixed, 1 partly fixed)
| # | Severity | Area | Defect | Fix |
|---|---|---|---|---|
| 1 | High | Input | A binding also fired when extra modifiers were held: Ctrl+W both dodged and walked, and Shift+click both selected and acted. | Exact matching everywhere through `Inputs.Actions`. The key-map check reports shadowing and fails CI. |
| 2 | Medium | Input | Contexts that are active together (Build, Called shot and Combat stance on top of World) were never checked against each other. | An active-together matrix, and suppression of World actions while they are open. |
| 3 | Medium | Settings | A clashing rebind was saved silently. | Clashes are rejected or swapped. Conflicts found on load reset to defaults with a notice. |
| 4 | Medium | Settings | The automatic UI scale went to 4× at 4K and ignored width. | The scale is clamp(min(h/540, w/960), 1, 3). |
| 5 | Medium | Accessibility | The screen shake, flash and slow-motion toggles have no readers yet. | An `Accessibility` facade that future effects must use. Real readers arrive with the effects in Phase 4. |
| 6 | Medium | CI | The smoke boot could pass with broken screens. | It instantiates every screen, hooks errors, exits 1, and CI greps the log. This caught one real headless error, now guarded. |
| 7 | Medium | Art validator | Sprites clipped at the frame edge passed. | Off-frame neighbours count as holes. 11 sprites were fixed and re-baked. |
| 8 | Medium | CI | The committed game atlases could drift from the art source. | CI bakes into a temp folder and diffs it against the committed atlases. |
| 9 | Medium | Melee spike | Button releases were dropped while the called-shot menu was open. | Releases are handled first, and the command is cleared on entry. |
| 10 | Low | Crowd spike | It allocated every frame. | **Partly fixed.** The HUD is throttled, parsing uses the invariant culture, inputs are bounds-checked and the atlas width is a uniform. One float buffer copy per layer per frame remains, because Godot's C# API has no way to update a cached packed array in place. Revisit in Phase 7 (battle rendering), or use a direct `RenderingServer` path then. |
| 11 | Low | Input | Movement used layout-dependent keycodes, which broke AZERTY and QWERTZ keyboards. | Physical keycodes. |
| 12 | Low | Rebinding | Esc and button clicks were captured, and lone modifiers could be bound. | Esc cancels capture, clicks on the screen's own buttons are ignored, lone modifiers are ignored, and hint labels refresh. |
| 13 | Low | Robustness | Several smaller problems. | Typed settings reads. Console and inspector statics are cleared. Console output is capped. `--dev` is ignored in release builds. The Godot download is checksum-verified. Duplicate sprite ids are reported. Malformed hex colours are rejected. |
| 14 | Low | Bench | `tools/Ravensguard.Bench` was a stub. | A minimal harness for the combat resolver and LightGrid. **Allocation and op-count gates for CI are moved to Phase 2**, when the abstract tick gives them something meaningful to gate. |

## Moved to later phases
- **Crowd spike buffer copy:** Phase 7, battle rendering.
- **Bench allocation and op-count gates in CI:** Phase 2.
- **Accessibility readers:** Phase 4, when screen shake, flashes and slow motion exist.
- **Art Sin/Cos rounding on different platforms:** no change needed. Art bakes are byte-identical across runs and locales, and CI's drift check would catch a platform difference.

## Still open for Phase 0
- Save → load → save byte-identical on a populated world. In progress.
- Style sheet v2 approval from the user.
