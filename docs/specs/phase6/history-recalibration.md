# History recalibrated to the live rules: history v1, frozen v0 for library worlds, faith, law, slavery and lifespans in the legends, and the continent tick's politics

Plan refs: §1A stage 2 (history v0: realms make war and peace "with the same utility rules as the live game"; disasters; notable people with lifespans; artefacts; output is the world's legends with typed, caused entries; budget <= 60 s), §5 Phase 1 (history v0 is a deliberately simplified rule set, **recalibrated to the live rules in Phases 6-7**), §5 Phase 6 (faith: history v0 is recalibrated to include it; "history rules recalibrated to the live rules"), §4A.4 (wars were dice: 12% a week to declare, ~71% of pairs at war), Phase 5 load report (a save is refused for a layer that no longer regenerates identically).

## 1. Purpose
The history that seeds every world should come from the rules the player then lives under. History v1 replaces v0's political and social dice with the live functions of this phase (war utility, opinion, the unrest ladder, the great plague's spread and mortality, lifespans, faith, law and bondage) at the history's seasonal scale, so a ruin "abandoned after the plague of 301" was emptied by the same rule that can empty a village in play. Worlds already in the library keep the history they were made with.

## 2. Dependencies
| Needs | From |
|---|---|
| `HistoryGenerator` (passes, its own stream table `S`, `Ev`, grudges, casus belli, treaties, wars, plague, famine, saints, reliquaries, rebellion, coup) | Phase 1 |
| `WarUtility`, opinion modifiers | `diplomacy-and-ai-lords.md` |
| Unrest thresholds | `unrest-and-revolt.md` |
| Plague start, spread, mortality | `disease-and-plague.md` |
| Religions, saints, heresy, excommunication | `faith-and-church.md` |
| Culture law: crimes, slavery | `crime-and-justice.md`, `slavery.md` |
| Lifespans (the hazard table), marriages as alliances | `family-and-lifespan.md` |
| Chronicle keys and specific causes | `chronicle-full.md` |
| `ContinentTick` | Phase 2 |
New: `WorldGen/History/HistoryRules` (a version switch), `HistoryGenerator.Faith.cs`, `HistoryGenerator.Society.cs`. Extended: the history's passes (Politics, Land, People) call the shared functions under v1; `WorldLibrary` records the rules version; `ContinentTick` uses `WarUtility` and the plague functions.

## 3. Data
- **`HistoryRulesVersion`** in the world record (`WorldLibrary`), the save header and `HistorySettings`: `0` (v0, frozen) or `1` (Phase 6). Absent means 0.
- **History stream ids** stay the generator's own table `S` (local to history, not the 0x6xx block): appended `Faith`, `Heresy`, `Slavery`, `Law`, `Marriage`, `Unrest`, after the last v0 id; v0 never draws them.
- **Calibration targets** (`worldgen/history_targets.jsonc`, read by the calibration test, not the generator).

## 4. Rules

### 4.1 Versioning (old worlds load unchanged)
`HistoryGenerator.Run` dispatches by version: v0 is the Phase 1 code path, frozen (its tests and hashes pinned forever); v1 is new. A library world made before Phase 6 regenerates v0 identically, so the Phase 5 load report accepts its saves. Its Phase 6 state is **derived at the seeding pass** (overview 7) from cultures and the v0 history: religions (`FaithSeeder.FromCultures`, stream 0x650), offices, lord traits, honesty, physicians. New worlds use v1 (overview Q5).

### 4.2 What v1 changes (seasonal, continental)
| Area | v0 | v1 |
|---|---|---|
| War and peace | dice per pair with grudges | `WarUtility` per realm pair each season on the history's coarse inputs (claims, casus belli from its events, strength from population, treasury from a tax proxy, the season, harvest cost); peace by war score and weariness |
| Opinion | grudges only | the opinion modifiers (kin, marriage alliance, religion, broken treaties, war) between rulers |
| Marriages of notables | heirs only | dynastic matches by the arranged-match utility: alliances between realms (treaties) |
| Rebellion and coups | dice | a coarse unrest per realm (taxes, famine, plague, war weariness, an excommunicated ruler) and the ladder's thresholds (UR-10) |
| Plague | `PlagueStep` with its own rates | the great plague's start (1% a continent-year at normal), spread on trade routes (the 40%-a-season edge rule), and mortality (attack x fatality: towns about 30%, villages about 20%, hamlets emptied 25%) |
| Faith | saints and reliquaries | a religion per culture founded at the start (founders, holy site), saints from plagues and famines (kept), **heresies** (from a plague or famine and an aggrieved realm: 0-2 a religion a century), **schisms** (a heresy that holds a realm for 20 years becomes a religion), **excommunications** of rulers (a casus belli), church land grants (abbeys founded by rulers' piety) |
| Law and bondage | outlaws as notables | culture law tables read for outlaws and executions; slave raids by `chattel` and `war_captives` realms on weaker neighbours (captives to markets; a chronicle line); a ruler's abolition act (rare: a `forbid` religion's pious ruler) |
| Lifespans | ages from a table | the vital table plus the old-age hazard (the hero's table, `family-and-lifespan.md` 4.6) for notables |
| Text | English | keys and parameters (`chronicle-full.md`) |

### 4.3 The continent tick in play
The seasonal continent tick (Phase 2) runs the same v1 functions for the realms outside the detailed regions: war utility and peace, abstract battles v1, ceded fiefs as ownership changes of cells, the great plague on edges, councils and subsidies as treasury proxies, excommunications. The detailed region's lords are the live `LordBrain`; their realm's ruler, when not in a detailed region, is the continent's proxy using the same function.

### 4.4 Calibration (measured, 50 seeds x 200 years, the reference settings)
| Measure | Target |
|---|---|
| Share of realm-pairs at war at any time | <= 35% (v0's old dice gave ~71%) |
| Wars per neighbouring pair per century | 0.5-1.5 |
| Great plagues in 200 years | 0-4 (mean about 2) |
| Realms fallen in 200 years | 10-30% |
| Ruins per 32 km region | 8-15 (Phase 4's table) |
| Saints per religion | 4-12 |
| Heresies per religion per century | 0-2; schisms 0-1 in 200 years |
| Specific cause coverage | >= 0.90 (`chronicle-full.md`) |
| History time on the reference PC | <= 60 s (v1 adds <= 8 s over v0) |

### 4.5 Tick placement
World generation only (history), and the continent tick at S9 (Phase 2 placement) for the shared functions in play.

## 5. Determinism
Every v1 draw is keyed by (seed, tick, entity, stream, draw) on the history's own appended stream ids; v0 draws are untouched. Same seed and version give the same history on Windows and Linux (`WorldTool hashes` adds a `history_v1` line; the v0 line stays pinned).

## 6. UI (960 x 540)
The legends viewer and the in-game Chronicle (H) show v1 entries with the new families (faith, law, bondage, plague); the loading screen's scrolling chronicle formats keys. The world library shows "History rules: Phase 1" or "Phase 6" for each world.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| HR-1 | v0 frozen | for seeds 1, 7, 1234 the v0 history hash equals the Phase 1 pins; a Phase 5 library world loads (no "layer changed" refusal) |
| HR-2 | Derived Phase 6 state for a v0 world | religions, offices, physicians and lord traits derived at load equal those of a fresh derivation (pure) |
| HR-3 | War share | v1, 50 seeds x 200 years: realm-pairs at war <= 35%; wars per neighbouring pair per century 0.5-1.5 |
| HR-4 | Rebellion thresholds | v1 rebellions start only past the ladder's thresholds on the coarse unrest (UR-10) |
| HR-5 | Plague | 0-4 great plagues in 200 years (mean 1.5-2.5 over 50 seeds); town mortality 26-34% |
| HR-6 | Faith | every culture's religion founded at year 0; saints 4-12; heresies 0-2 a century; each excommunication a casus belli used within 10 years in >= 50% of cases |
| HR-7 | Bondage | slave raids only by `chattel` and `war_captives` realms; their captives appear as market entries |
| HR-8 | Coverage | specific cause coverage >= 0.90 for every v1 history in the 50-seed set |
| HR-9 | Budget | v1 history <= 60 s on the reference PC (<= 68 s allowed on the CI box) |
| HR-10 | Cross-OS | the `history_v1` hash line equal on Windows and Linux for 3 seeds |
| HR-11 | Continent tick in play | 25 years of the continent tick after embark with v1 functions: wars, peaces and plagues within the same targets scaled to 25 years; coin and population summaries conserved |

## 8. Open questions (recommended defaults)
1. **Old library worlds** (overview Q5): keep their v0 history, gain Phase 6 state derived at load. Default as written.
2. **Recalibration is two-step:** Phase 6 the society and politics rules; Phase 7 the war rules (live armies, sieges, supply). The v1 war uses abstract battles v1 until then. Default as written.

## 9. Budgets
History v1 <= 60 s (v0 + 8 s); the continent tick's politics <= 20 ms a season; library record +1 byte.
