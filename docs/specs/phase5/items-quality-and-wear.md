# Items: one quality scale, tool qualities, wear, condition and the repair caps

Plan refs: §4A.1 ("one 5-tier quality scale"; "four item-price formulas and three quality scales" listed as broken), §2.10 (edge = material maximum edge x quality; items are shape x materials x quality x condition; wear levels worn, threadbare, tattered; rust in damp storage; repair consumes materials and needs tool qualities; tool qualities: cutting, sawing, hammering, chiselling, digging, prying, sewing, lockpicking, cooking; a heavy rock hammers a nail, badly), §2.6 (the resolver tests every layer's material), §3g (result quality from skill, tools, materials and proficiencies). User request (Nov 2026): **higher-quality weapons and armour perform better in combat, DF-style** (quality changes the layer tests, not flat damage).

## 1. Purpose
Phase 4 ended with two quality enums: `Ravensguard.Content.Quality` (DF's seven: Standard ... Artifact, multipliers 1.0 to 3.0, used only by `CombatTables`/`CombatTablesV2` keys, `WeaponInstance`, `WornItem` and the fight record) and `Ravensguard.Sim.Goods.ItemQuality` (Crude, Poor, Common, Fine, Masterwork, used by item rows, crafting, value and `CombatTablesV2.QualityPmOf`). This spec makes `ItemQuality` the **only** scale, states every number each tier drives, adds tool wear and the repair caps that crafting-full uses, and migrates content and saves without moving any Phase 4 calibration number.

## 2. Dependencies
| Needs | From |
|---|---|
| `ItemRow` (`Quality`, `ConditionPermille`, `Wear`, `Flags`, components), `ItemQualityRules` (`ValuePermille`, `FromScore`, `Score`, stream 0x223), `ItemMath.ValueMites` | exists (`Goods/`) |
| `CombatTablesV2` (weapons precomputed per shape x material x quality, `QwPm`), `WornV2.Of(stats, qualityPm, conditionPm, fit)`, `LayerTest.CutNeedMilli(..., QwPm, ..., QualityPm, ...)`, `BluntArmour` | exists (Phase 4 resolver v2) |
| `ClothingArmour.ApplyWear` (cut and crush wear per layer), the Phase 4 open item 4 (blunt dents plate) | exists; item 4 lands at Phase 4 close-out |
| `CraftMath` score parts (effective skill, tool bonus, material bonus, light), `craft_tool` qualities | exists (Phase 3 crafting) |
| Fit rule +-1/7 of body size, `FitCm3` | exists (clothing) |
New: `Goods/QualityTiers.cs` (the table below as integer arrays), `Goods/ToolWear.cs`, `Goods/Corrosion.cs`; content `goods/quality.jsonc` (one `quality_tiers` row), `goods/tool_wear.jsonc`. **Removed:** `Content.Quality`, `QualityTable` (an obsolete alias stays for one content load: see 6).

## 3. Data: the scale (`goods/quality.jsonc`, one row, every number permille)
| Tier (byte) | Name shown | Score from (`FromScore`) | Combat Qw / Qa | Value | Wear taken | Base condition cap after the best repair | Typical maker |
|---|---|---|---|---|---|---|---|
| 0 Crude | "crude" | < 15 | 800 | 500 | 1,600 | 700 | a novice, an improvised tool |
| 1 Poor | "poor" | 15-39 | 900 | 800 | 1,250 | 800 | an apprentice |
| 2 Common | (no adjective) | 40-69 | **1,000** | 1,000 | 1,000 | 900 | a journeyman; all generated and Phase 4 kit |
| 3 Fine | "fine" | 70-89 | 1,400 | 1,600 | 800 | 950 | a master on a good day |
| 4 Masterwork | "masterwork" | >= 90 and inspired (4.2) | 2,000 | 3,000 | 600 | 1,000 (only by a maker of the same tier) | rare: a master with fine tools |
- Combat values are exactly `CombatTablesV2.QualityPmOf` today (DF's standard 1.0, fine 1.4, masterwork 2.0; Crude and Poor below DF's floor because our scale has them). Value values are exactly `ItemQualityRules.ValuePermille`. Thresholds are exactly `FromScore`. Nothing that Phase 3 or 4 tested moves.
- **No artifact tier.** DF's x3.0 artifact is dropped (grounded world). A masterwork can become a **named artefact** (`ItemFlags.Named`, bit 32, appended): a name, a maker and a history line in the chronicle; it fights as a masterwork (overview Q4).
- Content row: `{ "type": "quality_tiers", "id": "default", "combat_pm": [800,900,1000,1400,2000], "value_pm": [500,800,1000,1600,3000], "wear_pm": [1600,1250,1000,800,600], "repair_cap_pm": [700,800,900,950,1000], "score_from": [0,15,40,70,90] }`. `CombatTablesV2.QualityPm` reads it (index = `ItemQuality`); the five rows replace the seven precomputed per weapon (table shrinks by 2/7).

## 4. Rules

### 4.1 What a tier does (DF-style: the layer tests, never flat damage)
- **Weapons:** `QwPm` enters the edge: `edge_eff = material.max_edge x Qw x condition_factor` through `LayerTest.CutNeedMilli` (cut need falls as edge rises), exactly where Phase 4 reads `s.Weapon.QwPm`. Blunt blows are unchanged by Qw (DF: quality sharpens, it does not add mass). **New:** `condition_factor = (400 + 600 x ConditionPermille / 1000) / 1000` for edged attacks only (a dull blade at condition 0 cuts at 40%); Phase 4 kit is at 1,000, so its numbers do not move. `sharpen_blade` (Phase 3) restores condition and so the edge.
- **Armour:** `QualityPm` (Qa) multiplies the layer's yield in the cut and blunt tests (`CutNeedMilli(..., w.QualityPm, ...)`, `BluntArmour(s, st, w.QualityPm, ...)`), as Phase 4 does; condition already scales protection through `WornV2.ConditionPm`.
- **Shields** use Qa on the board's material in the block test (melee-and-ranged 4.4), same table.
- **Wear taken:** every wear amount (a blow's `WearPm` from `ApplyWear`, a tool's use, clothing tear, vehicle-part and building-piece hp loss) is multiplied by `wear_pm[tier]/1000`, rounded down, minimum 1 when the base was >= 1.
- **Value:** `ItemMath.ValueMites` keeps `ValuePermille`; repairs then take `-100 pm` each (cap -300 pm; the repair count is the top 4 bits of `Wear`, 4.3), as Phase 3 wrote and did not apply.
- **Fit:** quality does not widen the +-1/7 fit band. A refit by a crafter whose expected tier (`FromScore(effective_skill)`) is two or more below the item's drops it one tier (crafting-full 4.6).

### 4.2 Crafted quality (extends basic-crafting 4.3; numbers in crafting-full 4.2)
`score = effective_skill + Range(-20, +20) (0x223) + tool_bonus (-10..+10) + station_bonus (-5..+5) + material_bonus + mastery_bonus (0 or +5) + light_penalty (0 or -5) + process_bonus (smithing, -15..+5)`, then `FromScore`.
- **Masterwork is rare.** A score of 90 or more gives Masterwork only if the inspiration roll passes: `inspire_pm = clamp(50 + 10 x (score - 90), 50, 300)` (stream 0x530, key crafter, job id x 64 + unit); otherwise Fine. A master (skill 75, best tools +10, nothing else) makes **Masterwork 4.9%, Fine 82.9%, Common 12.2%** of weapons (test IQ-5).
- **Named artefacts:** a Masterwork weapon, armour piece or jewel rolls `named_pm = 100` (stream 0x531, key item id). A named item gets a name from the maker's culture name pool (`{epithet} of {maker}` patterns, content `names/artefacts.jsonc`), a chronicle entry `artefact_made` (Cause = maker, workshop), and its history lines (owners, kills of named people) are appended later by Phase 6. Selling, theft and inheritance keep the flag and name.

### 4.3 Tool qualities and tool wear (`goods/tool_wear.jsonc`)
Qualities are the plan's list plus what Phase 3 shipped: `cutting, chopping, sawing, hammering, chiselling, digging, prying, drilling, planing, scraping, sewing, lockpicking, cooking, boiling, fire_starting, milling, whetting, spinning, gripping, soaking, storage, lifting` (appended: chiselling, digging, prying, drilling, planing, lifting). A `craft_tool` row gains `wear_pm_per_hour` per quality used (axe chopping 2, saw sawing 1.5, chisel 3, spade digging 2, needle 0.5, hammer 1, crowbar prying 1; units: permille of condition per attended game hour at Common). Hourly use subtracts `wear_pm_per_hour x wear_pm[tier] / 1000` (the fraction is kept in milli-permille in the low 12 bits of `Wear` and carried into `ConditionPermille` at 1,000, so 0.5 per hour is exact; the top 4 bits are the repair count). At condition 0 the tool **breaks**: its components go to the owner's store as scrap (`scrap_iron`, `shavings`), mass exact. A tool below 300 gives `tool_bonus -3` (a blunt chisel).
- **Abstract businesses** keep `wear_ppm_per_batch` on the `tools` good (stream 0x221) while out of the bubble; a business the hero owns hands-on holds real tool rows at its stations (businesses spec 4.2), worn by the rule above.
- **Improvised level 0** (a rock for hammering, a knife for prying) stays as Phase 3: time x1.5, `tool_bonus -10`, and wears the improvised item at x3.

### 4.4 Condition bands, rust and rot
| Condition (pm) | Metal | Cloth and leather | Wood |
|---|---|---|---|
| 700-1,000 | (none) | (none) | (none) |
| 400-699 | worn | worn | worn |
| 150-399 | battered | threadbare | cracked |
| 1-149 | ruined | tattered | rotten |
| 0 | falls apart (components to scrap) | falls apart (rags) | falls apart (shavings) |
- **Rust:** iron and steel items stored in a room that RoomDetector v2 says is not `Indoor`, or carried with the `Wet` flag, lose **2 pm a game day**; oiled items (`oil_item`, 0.01 kg tallow, 5 min, flag `Oiled` for 30 days, bit 64) none. Mail rusts at x2 (rings).
- **Rot:** leather and wood items wet for over 24 game hours lose 1 pm a day until dried. Goods keep their Phase 2 spoilage clocks.
- Corrosion and rot run once a game day per store (H4 slice), only over stores flagged damp (cached when the room's state changes), never per item per tick.

### 4.5 Repair caps (used by every repair recipe in crafting-full 4.5)
`cap = min(repair_cap_pm[item tier], repair_cap_pm[maker tier] + 50)` where `maker tier = FromScore(repairer's effective skill)` (no roll). Phase 3's `mend_garment` cap 700 and `patch_garment` 800 stay as the recipe caps and are lowered further by this rule. A failed repair (the Phase 3 failure roll at the critical step) drops the item one tier and still uses the materials. Restoring to 1,000 only by reforging (a new item from the old components: crafting-full 4.5).

## 5. Tick placement and determinism
| Where | What |
|---|---|
| Live 20 Hz | nothing per item; combat reads precomputed Qw and Qa and the row's condition |
| Barrier | wear and breakage at the end of a blow, an attended crafting hour or a finished piece |
| H4 daily slice | rust and rot over damp stores |
| Load | `QualityTiers` and `CombatTablesV2` five quality rows |
Streams 0x530 (inspiration), 0x531 (named); the existing 0x223 (score) and 0x221 (abstract tool wear) are unchanged. Integer only.

## 6. Migration (content and save)
- **Content:** fields that held `Content.Quality` names (`"quality": "standard" | "well_crafted" | "finely_crafted" | "superior" | "exceptional" | "masterwork" | "artifact"`) are read through a fixed map for one release with a load warning: standard and well_crafted -> `common`; finely_crafted, superior and exceptional -> `fine`; masterwork -> `masterwork`; artifact -> `masterwork` plus `named: true`. Base content uses none of them today (checked: only the default Standard is ever used), so `content/base` needs no edit; mods get the warning.
- **Code:** `WeaponInstance(..., ItemQuality Quality = ItemQuality.Common)`, `WornItem` likewise; `CombatTables` (v1 adapter) and `CombatTablesV2` key on `ItemQuality`; `V1Adapter` uses `QualityPm[(int)ItemQuality]`.
- **Save v7 -> v8** (`SaveMigrator`, overview 8): the fight record's `WeaponQuality` (key 6), `NowQuality` (key 25) and `WornQualities` bytes are rewritten through the same map (byte 0 = Standard becomes 2 = Common; **without this step byte 0 would read as Crude**). Item rows already store `ItemQuality`: unchanged. Fight kits in content default to Common.

## 7. UI
Item names carry the tier adjective and the band ("fine steel arming sword, worn"); a named artefact shows its name in gold and "Named" in the tooltip with its history lines. The inventory tooltip lists `Edge 82% of the steel's best` and `Protection x1.4 (fine)`. The crafting screen shows the expected tier distribution including the Masterwork chance (CF screen, Phase 3 4.7 extended).

## 8. Tests (golden)
| id | Case | Expected |
|---|---|---|
| IQ-1 | Table: content row loads; arrays equal `QualityPmOf`, `ValuePermille`, `FromScore` | 800/900/1000/1400/2000; 500/800/1000/1600/3000; 15/40/70/90 |
| IQ-2 | Calibration unchanged: Phase 4 fixed-seed hashes (`FightHash` of ML-15, OV-1, WM-9) and the 2,000-duel harness after the enum swap | identical hashes; win rates identical to the last decimal |
| IQ-3 | DF ordering: steel arming sword cuts mail-over-padding at Common vs Fine vs Masterwork, 2,000 duels each, same seeds | cut-through rate strictly rises with tier (C < F < M); blunt-only outcomes unchanged in count |
| IQ-4 | Armour: Fine mail (Qa 1,400) vs Common under the same arrows | penetrations strictly fewer; no flat damage field changed (the resolver input diff is Qa only) |
| IQ-5 | Masterwork rarity: score base 85 (skill 75, tools +10), the 41 rolls | Common 5/41 = 12.2%; Masterwork = (sum of `inspire_pm` over scores 90-105 = 2,000) / 41,000 = 4.88%; Fine (20 + 16 - 2.0)/41 = 82.9% |
| IQ-6 | Dull edge: condition 500 | `condition_factor` = 700 pm; blunt attacks unaffected |
| IQ-7 | Wear: a Fine breastplate takes a 10 pm wear blow | 8 pm; a Crude one 16 pm |
| IQ-8 | Tool wear: Common chisel, 100 attended hours at 3 pm/h | condition 1,000 -> 700; a Masterwork chisel -> 820 |
| IQ-9 | Breakage: a hammer reaches 0 | row freed; its components' mass appears as scrap in the owner store; `MassLedger` exact |
| IQ-10 | Rust: steel sword in an unroofed shed 30 days | 1,000 -> 940; oiled: 1,000 |
| IQ-11 | Repair cap: a Fine sword (cap 950) re-edged by a skill-45 smith (expected tier Common, cap 900 + 50) | 950; by skill 20 (Poor: 800 + 50) 850 |
| IQ-12 | Save migration: a v7 save with a fight in progress (weapon byte 0, worn bytes [0,0]) | loads as Common; the resumed fight's next 600 ticks hash as the v7 run |
| IQ-13 | Content alias: a mod item with `"quality": "superior"` | loads as Fine with one warning naming file, line and field |
| IQ-14 | Named artefact: 10,000 masterworks | 1,000 +- 95 named (3 sigma); each has a chronicle `artefact_made` with the maker as cause |

## 9. Open questions (recommended defaults)
1. **Artifact tier:** none; named masterworks only (overview Q4). 2. **Masterwork rate** ~5% of a master's work with the best tools: default as written; tune `inspire_pm` after testers. 3. **Rust rate** 2 pm a day damp: default; a sword forgotten in a wet shed is worn after two and a half game years (150 days).

## 10. Budgets
`QualityTiers` lookups are array reads; `CombatTablesV2` shrinks from 7 to 5 quality rows (about -0.8 MB). Daily rust and rot: <= 0.3 ms per region (damp stores only). Item row unchanged (flags and the repair nibble fit existing fields).
