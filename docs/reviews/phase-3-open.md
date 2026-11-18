# Phase 3: open items to close before the code and bug check

Gaps reported by the step agents or seen in screenshots. Each one is either fixed in the Phase 3 cleanup or moved to a named later phase in `docs/reviews/phase-3.md`.

## Gameplay gaps
- Road encounters: outcomes (pay, trade, fight) all resolve to "go on". Pay and trade can be done now; fights wait for Phase 4.
- `IEncounterCast` isn't wired to real caravan and vagrant records, so encounters use generated travellers.
- Hunting works only as a dev command. Snares have no key or sprite.
- Fairs are calendar queries only; the economy and job board don't use them yet.
- Followers don't walk behind the hero yet (it needs the LODBridge). Their orders are stored.
- Trade works only in the home region's markets.
- Encumbrance changes the gait but not the exact speed (`LiveWorld.SpeedOf`).
- NPCs sheltering from cold or rain change their exposure but not their position (positions come from the schedule).
- Cold deaths aren't recorded on person records (`DeathCause.Cold`). The hero's core temperature and wetness aren't in the save.
- Weather-driven rest, storm suspension, feast half-days, the escort follow check, shop takings and hero fatigue aren't wired (step 3).
- The live tier is still "theatre" for scheduled work: `WorkDone` is credited through `IWorkSink` for farm hours only.
- Landless idle adults rise to about 2,100 over a century. Day-labour contracts are planned for Phase 5.
- Crafting effects with no world state yet (lit fire, cart repair, garment refit) are only reported as events.

## Polish
- Trade screen: weights show too many decimals (for example 8019.999 kg). Round sensibly.
- ~~The HUD cold warning text is hard-coded English, not in `en.csv`.~~ Fixed (onboarding): `HUD_*` keys in `en.csv`, `ClothingFeed.HudLine` reads them.
- ~~The `?` controls overlay is missing lines for the travel, camp, mounted and crafting contexts.~~ Fixed (onboarding): the overlay exists, built from `InputDefs` and the live key map, with pages for world, mounted, trade, crafting, camp, travel, called shot and build; `--selftest` checks every page against the key map. `world.camp` and `world.forage` had no `ACTION_*` text and now do.
- The camp fire uses the hearth sprite.
- Sleepers in side-by-side beds are drawn upright with the fall pose; there's no top-down lying sprite.
- Hair colour and style and the apron layer aren't in the rig. Skin-tone variation isn't applied.
- The hero name label sometimes draws twice, slightly offset.

## Determinism and tests
- Auto-travel isn't recorded by the session recorder, so a trip can't be replayed.
- A path in progress at an autosave isn't saved, so a replay window starting there can diverge.
- `RegionRecordsTests.Tick_levels_are_assigned...` once threw IndexOutOfRange inside a `Parallel.ForEach` in a region tick. It passes when run alone. This could be a real race: investigate it.
- Timing tests are noisy when the machine is busy (`Daily_regions_are_staggered`, `HourlyTickTimings`). They're isolated in the Timing collection, but they still compete with other processes.
- `JobTests.Determinism` ran out of memory once while Godot screenshot runs were going.
- Vehicles face east or west only (no north/south sprites); a loaded cart bed isn't drawn with its cargo.
- Dialogue: `start_contract` and `give_item` effects only toast; no legends fallback; caravans carry no rumours between regions; per-person mood in barks, the History (H) link and the Journal map pin are missing; only the caravan master was authored (now the seven first-hour mentors too: `old_knight`, `free_farmer`, `old_sergeant`, `prior`, `steward`, `fence` and the caravan master); the camp talk hook (Travel) isn't filled.
- Autopilot: Q-menu directive verbs and a party-member directive picker in the squad screen aren't built; Tend and PursueAmbition are stubs (Phase 5 and Phase 8); AP-8 waits for dialogue's `offer_big` flag; AP-15 compares sessions rather than a literal command replay.
