# Disease and plague: everyday coughs, fevers and flux, and the rare great plague that follows the trade roads and reshapes labour and land

Plan refs: locked decision (Nov 2026: **plague rare but devastating**: everyday coughs, fevers and flux, plus rare great plagues; a great plague can kill a third of a town, spreads along trade roads, empties villages and reshapes the economy: labour gets dear and land gets cheap; Phase 6 to 8 events; the death rates are in §4A.2), §4A.2 (death chance by age; hunger or crowding x1.5-3; town disease x1.2; malnutrition raises disease deaths first), §1A (history: plagues; the rest of the continent keeps going with plagues), locked difficulty sliders (plague severity), Phase 5 inheritance IN-12 (a great plague can settle hundreds of estates a day).

## 1. Purpose
Phase 2 models sickness as settlement outbreaks that multiply the death rate (`Outbreak*` in `VitalRates`: winter towns 4,000 ppm a day, 10% severe). That is right for the abstract population but nobody is ever *ill*. This spec gives persons illnesses with days, symptoms and care (so the hero and his party can fall sick, be nursed or see a physician), and adds the **great plague**: a named contagion that travels the continent's roads and the region's, with the plan's mortality, and an economy that answers it.

## 2. Dependencies
| Needs | From |
|---|---|
| `VitalRates` outbreaks (settlement multipliers), `Demography.Kill` with `DeathCause.Disease` and the outbreak's entry as cause, nutrition reserves, crowding | Phase 2 |
| Exposure, wetness and warmth (clothing model), camp water | Phase 3 |
| Physicians, barbers, bed-days, `AbstractPhysic` | `medicine-and-treatment.md` |
| Caravans, migrants, the road graph, the continent tick (regions, edges, `RegionSummary`) | Phase 2 |
| `LabourMarket`, day labour wages, land prices (`land.jsonc`), `EstateWorld` | Phase 2, 5 |
| Superstition, saints (a plague saint), flagellants | `faith-and-church.md` |
| The plague severity slider | `difficulty-sliders.md` |
New: `Sim/Disease/` (`IllnessBook`, `Illnesses` (content), `GreatPlague`, `PlagueEconomy`), content `medicine/illness.jsonc`, `disease/plague.jsonc`. Extended: `PopulationSystems.Outbreaks` (a named outbreak's illness), `ContinentTick` (plague on edges), `LabourMarket` and `LandRules` (the scarcity terms in 4.5).

## 3. Data
- **Illnesses** (`medicine/illness.jsonc`; days are game days):
| Illness | Onset | Course | Effects | Death (untreated / nursed / physician) |
|---|---|---|---|---|
| cough (a cold) | cold and wet exposure, winter, a sick housemate | 5 days | stamina regen -20%, sneezing (noise 4 m in heists) | 0 |
| ague (fever) | marsh tiles in summer, a sick housemate | 7 days, may recur | strength -10, sleep +4 h | 0.5% / 0.2% / 0.1% |
| flux (dysentery) | bad water (camp without boiling, a crowded army camp), summer | 6 days | water need x2, stamina -30% | 3% / 1% / 0.5% |
| lung fever | after a cough in a starving or old person | 10 days | bed-bound | 10% / 5% / 3% |
| wound fever | (Phase 4 infection) | - | - | (Phase 4 rules) |
| plague (great plague) | contagion only | incubation 3, illness 7 | bed-bound, buboes | 60% / 55% / 50% |
- **`Illness`** (per person, saved in the optional `disease` section only for persons in the bubble, the hero's party and kin): `{ Person, Kind, StartDay, Day, Care (None, Nursed, Physician), Immune (plague survivors) }`. NPCs outside it stay in the Phase 2 abstract (their deaths carry the outbreak's illness kind as the cause note).
- **`GreatPlague`** (saved in `disease`; the continent's part in the continent section): `{ Name (from the history's names), Origin, StartDay, Infected settlements (region) with day and end day, Infected regions (continent) with season, Dead }`.
- **`disease/plague.jsonc`**: start chance per continent-year **1%** (slider: rare 0.5%, normal 1%, frequent 2.5%; off), attack rates (town 50%, village 35%, hamlet 30%), case fatality 60% (slider mortality x0.5 / x1 / x1.5), duration in a settlement 30-60 days, spread rates (4.3).

## 4. Rules

### 4.1 Everyday illness (persons)
Each H5 hour 0, for each tracked person (the hero, party, kin, persons in the bubble), the onset chance per day: cough 2% in winter with wet or cold exposure (Phase 3: exposure band Cold or worse for 4+ hours that day), 0.3% otherwise in winter; +5% for each sick housemate (contagion, any illness with contagion: cough, flux, plague); ague 0.5% a day on marsh tiles in summer; flux 3% a day for each day of unboiled water in camp, 0.5% in an army camp of 50+ (stream **0x6D0**, key person and day). The course runs its days; death checks once at the illness's worst day (`DeathCause.Disease`, cause: the illness entry, whose cause is its onset: "fell ill of the flux after drinking from the Wend at camp, day 33"). **Care:** nursed (a household member at home, or a party member who spends 2 h a day: `Nursed`) and a physician's visit (`Physician`, his fee: 100 mites a visit, daily) lower the death chance as the table and shorten the course by 1 day. Bed rest halves the stamina effects. Abstract NPCs: the Phase 2 outbreak multipliers stay the model; `AbstractPhysic` lowers a sick household's outbreak death multiplier by 25% where a physician treats.

### 4.2 The great plague starts
The continent tick rolls a start once a game year at S9 (stream **0x6D2**, key continent and year) with the slider's chance; the origin is a continental town weighted by population on a trade road (as history v0's `PlagueStep`), named from the history's plague names (or a new one). The rule is the same in history (`history-recalibration.md`): one function, two clocks.

### 4.3 Spread along the roads
- **Continent (seasonal, S9):** each infected region infects each road neighbour with P = 40% per season x (edge trade volume / the median edge volume, clamped 0.5-2) (stream **0x6D3**, key edge and season); a region stays infected 1-2 seasons.
- **Region (daily, H5):** the plague enters a detailed region through its border gates (a caravan or migrant group arriving from an infected region carries it with P 30%, stream 0x6D3 draw 1, key caravan); inside, each infected settlement infects each settlement linked by road within 12 km with P/day = 3% x (caravans and travellers that passed between them in the last 7 days, clamped 1-5) / distance factor; a quarantined settlement (4.4) x0.2. A settlement's epidemic lasts 30-60 days (stream **0x6D1**, key settlement).
- **People (daily):** in an infected settlement each susceptible person is infected with P/day = attack rate / duration x crowding (dwelling occupancy over capacity x1.5) (0x6D1, key person and day); incubation 3 days, illness 7 days, death at day 4-8 with the case fatality, survivors immune for life. Over a 45-day epidemic in a town: about 50% infected x 60% = **30% dead** ("a third of a town"); villages about 20%; a hamlet of < 60 people whose dead pass 40% is abandoned by its survivors with P 25% (they migrate; the place becomes a ruin in the history's sense, `settlement_emptied`).
- The hero and his party are persons like any: he can flee (leave the settlement before infection), avoid crowds (infection P x0.5 if he sleeps outside the town), and burn the dead's bedding (contagion in a household -50%).

### 4.4 Responses
- **Quarantine:** a lord closes a town's gates for N days (an action of `LordBrain` when a neighbour is infected and his town is not; the player as lord: a policy): no caravans in or out (spread x0.2, the market's imports stop: prices rise), people inside can still catch it from those already infected.
- **Plague pits and burials:** the parish buries the dead in pits (the funeral fee waived); the estates settle (Phase 5 `EstateSettler`, budget IN-12).
- **Faith:** superstition +30 in infected settlements; a flagellant band may pass (a road encounter); a priest who nurses the sick and survives may become a local saint in history's rule (in live play: standing +).
- **Physicians:** cannot cure it (the table's 60/55/50% only); they charge double in a plague.

### 4.5 The economy answers (labour dear, land cheap)
Two scarcity terms, owned here and read by production and land:
- **Wages:** the labour market's day wage and the production classes' wages are multiplied by `clamp(1000 + 1500 x (vacancies - idle) / max(1, workers before the plague), 1000, 2000)` permille, recomputed each S5 (a third of workers dead with no idle: about x1.5). Wages fall back 10% of the gap a season as households form and migrants arrive.
- **Land:** lot and lease prices (`land.jsonc`'s function) are multiplied by `clamp(1000 - 1500 x vacant dwellings and unworked parcels share, 400, 1000)` permille (a third of holdings empty: about x0.5), recovering 10% of the gap a season.
These are general terms (any population fall triggers them, a war's as well); the plague is their first big user. Coin and mass are conserved (they only change prices).

### 4.6 Tick placement
| Where | What |
|---|---|
| H5 hour 0 | person illnesses (onset, course, death), settlement spread and infections in the region |
| H6 S5 | the scarcity terms on wages and land |
| H6 S9 | continental start and spread |
| H1 | carriers arriving through border gates |
| Barrier | the hero's nursing, physician visits, quarantine as lord |

## 5. Determinism
Streams: **0x6D0** everyday onset, **0x6D1** epidemic length and personal infection, **0x6D2** continental start, **0x6D3** spread (edges, carriers), **0x6D4** illness death checks (key person and illness start). Settlements in id order, persons by row. `DiseaseHash` covers illnesses and the plague state.

## 6. UI (960 x 540)
The Character screen shows an illness with its day, course, care and death risk band; the paper-doll greys a bed-bound person; rumours carry plague news with their age; the world map's overlay shows infected regions and settlements as known by rumour (not truth); the Ledger shows wage and land price terms with their cause.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| DP-1 | Cough onset | the hero cold and wet 5 hours a day in winter for 50 days: onset at 2% a day (expected about 1 cough in 50 days); a sick housemate adds 5% |
| DP-2 | Flux at camp | unboiled water for 6 days: onset 3% a day each; untreated death 3%, nursed 1%, physician 0.5% over 10,000 cases +-0.3 |
| DP-3 | Course and care | a nursed fever lasts 6 days not 7; the physician's daily fee 100 mites moves to his box |
| DP-4 | Plague in a town | a town of 1,500 for a 45-day epidemic: dead 30% +-4; survivors immune; every death's cause chain reaches `plague_begins` |
| DP-5 | Hamlet emptied | 50 people, 45% dead: abandoned with P 25% over many seeds; `settlement_emptied` with the plague as cause |
| DP-6 | Continental spread | from one origin over 8 seasons on seed 1234's continent: infected regions follow the road graph only; P per edge by trade volume |
| DP-7 | Into the region | an infected neighbour region: a caravan from it carries the plague with P 30% through a gate; a quarantined town's spread x0.2 |
| DP-8 | Wages | a third of a region's workers dead, no idle: the day wage x1.5 at the next S5, falling 10% of the gap a season |
| DP-9 | Land | a third of holdings vacant: lot and lease prices x0.5, recovering 10% of the gap a season |
| DP-10 | Estates under load | 1,500 deaths in a day: settled within IN-12's 30 ms; coin, mass and deeds exact |
| DP-11 | Slider | plague off: no start in 1,000 continent-years; frequent: 2.5% +-0.5; mortality x1.5 gives a town 45% +-5 dead (attack rate unchanged, fatality capped at 90%) |
| DP-12 | Determinism, save | save mid-epidemic -> load -> save byte-identical; the epidemic continues identically (`DiseaseHash`) |

## 8. Open questions (recommended defaults)
1. **How often a great plague:** 1% a continent-year at normal (about one in a 100-year span; 0 to 2 in a 200-year history). Default as written; the slider is honest.
2. **Can physicians do anything against the plague?** Only the small care step (60 -> 50%), historically fair. Default as written.

## 9. Budgets
Person illnesses <= 0.1 ms at H5 (tracked persons only, a few hundred); settlement spread <= 0.5 ms a day; personal infections in an epidemic <= 3 ms a day for a town of 3,000 (rows of that settlement only); continent spread <= 2 ms a season; save: illnesses 16 B each (tracked persons), the plague state <= 4 KB.
