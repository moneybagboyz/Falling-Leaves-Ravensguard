# Unrest and revolt: grievances, the ladder from grumbling to revolt, riots by real people, and the fief that changes hands

Plan refs: §4A.2 (unrest is the share of households with grievances: hunger, taxes, crowding, crime, the lord's reputation; the ladder: rumours -> tax and levy evasion -> a riot by real people -> a revolt that changes who holds the fief; higher tax levels create more grievances; "rebellion is documented but doesn't exist" in the old code), §3j (purveyance raises grievances and unrest and can spark revolt: Phase 7; unpaid dues become grievances), Phase 5 prospecting (closing a common for your own business causes grievances, which feed the unrest ladder in Phase 6), `slavery.md` 4.5 (slave revolts use this machinery).

## 1. Purpose
Make a lord's choices come back to him through his people. Each household carries a small set of grievances with causes; a settlement's share of aggrieved households climbs a ladder whose rungs are things people do: grumble, evade, riot, rise. Riots are live people with a leader and a target; a revolt can take the fief. Every rung is a chronicle entry that names its grievances.

## 2. Dependencies
| Needs | From |
|---|---|
| Households, nutrition reserves, crowding, arrears, `TaxLevel` | Phase 2 |
| Crime victims, harsh sentences | `crime-and-justice.md` |
| Lord's standing, excommunication, subsidies, tallage, evictions, enclosure of commons | `reputation-and-standing.md`, `faith-and-church.md`, `rule-and-income.md`, Phase 5 prospecting |
| Live people, utility AI, fights (`NoLethalForce` guards), abstract battles v1 | Phase 3, 4 |
| Fire (rioters burn) | `fire-and-burning.md` |
| `LordBrain` concessions, rebels' claimants | `diplomacy-and-ai-lords.md` |
New: `Sim/Unrest/` (`GrievanceBook`, `UnrestLadder`, `Riot`, `Revolt`), content `society/grievances.jsonc`. Extended: `TaxSystem` (evasion), `UtilityBrain` (`Riot` action appended), the rumour kinds (`Unrest` appended).

## 3. Data
- **`Grievance`** kinds (`society/grievances.jsonc`, each with points and a decay): hunger (nutrition reserve < 50%: 2, while it lasts), tax level (High 1, Crushing 2), a subsidy or tallage paid this season (1 / 2), crowding (over capacity 150%: 1), crime victim this season (1), kin harshly punished (1, a year), lord's standing in the settlement < -300 (1), lord excommunicated or interdict (2), common enclosed (1, 2 years), unpaid wages (1), eviction of a neighbour (1, a season), slavery present in a `none`-law culture (1), purveyance (Phase 7: 2).
- **`HouseholdGrievance`** (saved sparse, optional section `unrest`): bit flags and points per household, each with its cause entry; a household is **aggrieved** at >= 3 points.
- **Settlement unrest** (permille): aggrieved households / households, recomputed at H5; the ladder rung and the days on it.

## 4. Rules

### 4.1 The ladder
| Rung | Unrest | What people do |
|---|---|---|
| 0 Quiet | < 150 | nothing |
| 1 Grumbling | >= 150 | rumours of kind `Unrest` ("the reeve takes the last sheaf"), barks, the lord's standing -5 a season |
| 2 Evasion | >= 300 | each aggrieved household pays no hearth tax with P 50% at S4 and hides from the levy (Phase 7); arrears grow; the reeve reports it |
| 3 Riot | >= 450 for 5 days, or a trigger (an eviction, a hanging of a local, a tallage) at >= 350 | a riot (4.2) |
| 4 Revolt | >= 600 for 2 seasons, or a riot put down with 3+ deaths at >= 500 | a revolt (4.3) |
Concessions lower grievances at once: tax level down one (-1 point for that grievance), a grain dole (the lord's barn feeds the hungry: hunger points cleared while it lasts), pardon (kin points cleared), dismissing an official found skimming (-1 for the lord's-standing grievance), lifting an interdict.

### 4.2 Riots (real people)
- **Leader:** the aggrieved adult with the highest `Leadership + Speech` in the settlement (ties by id; stream **0x680** only for exact ties with equal skills, key settlement and day).
- **Crowd:** 10% to 40% of aggrieved adults by unrest (linear 450 -> 10%, 800 -> 40%), gathering at the market square at the next 10:00; in the live bubble they are people with `ActionKind.Riot` (appended), carrying tools as improvised weapons.
- **Target** by the top grievance: hunger -> the lord's barn or a hoarding merchant's store (loot: goods move to the rioters' households, the crime `theft` for each who carried); taxes -> the reeve or bailiff (assault) and the tax chest; the lord -> the manor (arson: `fire-and-burning.md`); a hanging -> the gallows and the gaol (a jailbreak).
- **End:** guards disperse it (fights with `NoLethalForce`; a death makes it worse: 4.1), the lord concedes (the crowd goes home at the next hour), or it burns out after 6 game hours. The chronicle `riot` lists the grievances with counts, the leader and the target. The leader is wanted afterwards (a crime record) unless pardoned.
- The hero can join (standing with the aggrieved +, with the lord -), lead (if he is the top leader by skills and lives there), stand with the guards, or mediate (Speech check vs unrest / 10: success means the lord's concession is offered on the spot, if the lord's utility for it >= 200).

### 4.3 Revolts
A revolt picks a **claimant**: a rival with a claim from the chronicle (a dispossessed heir, a neighbouring lord with opinion of the lord <= -40), else the riot leader. Rebels: the aggrieved adult men of the fief's settlements (the levy rule, 1 in 5), armed with what they own; against the lord's retinue, guards and loyal levy. Resolved by an **abstract battle v1** (or live in the bubble; the hero can fight on either side) at the fief's seat within 3 days. **Rebels win:** the fief deed passes to the claimant (`DeedCause.Granted` by revolt, a chronicle cause chain to the grievances), the old lord flees or is captured (ransom); the liege may accept the new lord (opinion test) or treat it as a casus belli. **Rebels lose:** leaders tried (treason: the culture's sentence), unrest drops by 200 and fear holds it for 2 seasons. Slave revolts (`slavery.md`) use 4.2-4.3 with the bonded as rebels and no claimant: survivors flee as a free band.

### 4.4 NPC lords and the hero as lord
`LordBrain` reads unrest: at rung 2 a just or generous lord lowers taxes or doles grain; a greedy or cruel lord raises guards and hangs leaders (feeding the ladder). The hero as lord sees rungs and causes on the Fief tab with the concessions and their costs.

### 4.5 Tick placement
| Where | What |
|---|---|
| H4 slice | household grievance points (hunger, wages) |
| H5 hour 0 | settlement unrest, rung changes, riot and revolt triggers |
| H6 S4 | evasion at the hearth tax |
| Live tick | riot crowds as people; fights |
| Barrier | the hero's mediation, joining, concessions as lord |

## 5. Determinism
Unrest is a pure count. Draws: **0x680** leader ties, **0x681** which aggrieved adults join a riot (key person and riot id; P by the crowd share), **0x682** revolt's battle seeds (abstract battle keys) and evasion picks (key household and season). `UnrestHash` covers grievances, rungs, riots and revolts.

## 6. UI (960 x 540)
Fief tab: per settlement the rung, unrest permille, the top three grievances with counts and causes, the concessions with their costs and effects. The world map's Land overlay shades settlements by rung. A riot shows its leader's nameplate and a banner over the crowd.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| UR-1 | Grievance points | a household at Crushing tax (2) and hungry (2): 4 points, aggrieved; with a dole and High tax: 1, not aggrieved |
| UR-2 | Ladder | 100 households, 46 aggrieved for 5 days: a riot; 31: evasion only (about 15 of 31 pay no hearth tax at S4) |
| UR-3 | Riot, hunger | the crowd (10% + share) goes to the lord's barn at 10:00; goods moved are thefts by named rioters; mass conserved |
| UR-4 | The lord's law | replacing the culture's sentence by a harsher one adds 1 point to the convicted's kin households (CJ-12) |
| UR-5 | Concession | lowering tax from Crushing to High removes one point from every household with it; unrest recomputed at H5 |
| UR-6 | Riot put down with deaths | 3 rioters killed at unrest 520: revolt triggered |
| UR-7 | Revolt won | the claimant (a dispossessed heir) wins the abstract battle: the fief deed passes, the chronicle chain reaches the grievances; the liege's reaction by opinion |
| UR-8 | Revolt lost | leaders tried for treason; unrest -200 for 2 seasons |
| UR-9 | Enclosure | closing a common for a quarry: 1 point to its users for 2 years (the Phase 5 hook) |
| UR-10 | History parity | the history's `Rebellion` uses the same thresholds over its coarse unrest (HR-4) |
| UR-11 | Determinism, save | a year of a hungry fief: `UnrestHash` equal; save mid-riot -> load -> save byte-identical |

## 8. Open questions (recommended defaults)
1. **Can a revolt take the hero's own fief?** Yes, by the same rules; the Fief tab warns at rung 2 with the causes. Default as written.
2. **Riot violence:** rioters fight with tools and do not kill a downed man unless the leader is cruel (trait). Default as written.

## 9. Budgets
Grievance points <= 1 us a household an H4 slice; H5 unrest <= 1 ms a region; a riot of 60 people within the live tick's people budget (Phase 3: they are ordinary live persons); save: grievances 6 B per aggrieved household (<= 30 KB).

## Notes (P6-E1, unrest and revolt)

- **Built:** `Sim/Unrest/` (`UnrestBook` and its optional "unrest" section, `UnrestRules` the pure rules, `UnrestEngine` the daily and hourly passes, `UnrestConcede` the lord's concessions and the events others report, `UnrestDesk` and the Fief tab's rows, `UnrestSystem` and `UnrestWiring`), content `society/grievances.jsonc` and `unrest/settings.jsonc`, `HeroCommandKind.Unrest` (`ur:`), `RumourKind.Unrest`, `TransferReason.RiotLoot`, 13 chronicle types, three lord actions (`dole`, `punish`, `pardon`) in `politics/considerations.jsonc` with the `LordBrain` cases. Gate UR-1 to UR-11 in `tests/Ravensguard.Sim.Tests/Unrest/UnrestTests.cs`.
- **Conditions are read live, events are rows.** Hunger (the economy's `Grievance.Hunger` flag, not a 50% nutrition reserve), the tax level (High 1, Crushing 2), the rule book's levy rows (`levy`), crowding, crime, the lord's standing in the settlement, a ban and unlawful bondage are recomputed at each daily pass and never stored. Only what an event raised is a saved `GrievanceRow` (kin punished, wronged, enclosure, eviction, purveyance, unpaid wages), each with its chronicle entry, which is what a riot's causes point at. `UnrestBook.Add(household, kind, day, entry)` is the one call for a new source.
- **Phase 2's ladder stays** (its own `TaxState.Unrest`, rungs, evasion and the barks' `unrest_high`), but when the unrest book is attached `TaxSystem.RiotsElsewhere` stops its granary riot and its `revolt_brewing` entry, and `TaxSystem.EvasionHook` adds the E1 evasion (rung 2, aggrieved, 50% at S4, stream 0x682, key household and season). The two measures differ (Phase 2 weighs half units); E1's is aggrieved households over households (at least 10).
- **Riots** run in the abstract tier on real people: the leader (Leadership + Speech, ties by 0x680), the crowd by 0x681, the gather at the 10:00 hour, the target by the top grievance (a lord's barn is the lord household's `FarmIds.Barn`, else the parish granary), grain moved by store moves, the chest's coin by `TransferReason.RiotLoot`, a theft crime per carrier through the crime core, the manor lit through B5's `FireWorld.IgniteBuilding` with `FireCause.Riot`, the gaol opened through `Gaol.Free`. Guards disperse a crowd at the next hour without lethal force; a cruel lord's guards kill one in twelve (at least one), each by `Demography.Kill` with a cause back to the riot and a `kin_punished` row for the dead man's household. A riot burns out after six hours.
- **Revolts:** a claimant (E3's `UnrestLinks.Claimants`, else a neighbouring lord at opinion -40 or under, else the leader), the rebels (aggrieved men, one in five by 0x682), a fight on the third day at 12:00. The fight is an integer strength comparison (a man a point, a guard three, a draw of plus or minus 20% each side from 0x682), the winner losing a tenth and the loser three tenths through `Demography.Kill`; it is not the soldier-level `AbstractBattle` (the levy has no kits). Won: `FiefRow.Holder` is the claimant, the liege's reaction is the opinion test (`ur_liege_accepts`, `_refuses`, `_none`), a refusal an `insult`. Lost: treason crimes for the leaders (the court sentences by the culture's key) and fear holds the unrest down 200 for two seasons. The chain: `revolt_won` -> `revolt_rises` -> `riot_ends` -> `riot_rises` -> the grievances' entries.
- **Slave rising:** D1's `Revolts.Rise` calls `BondWorld.Rose`, which raises a revolt of the bonded (no claimant), fought the next day; the bonded are already free in D1's book, so both outcomes end with `slave_rising_won` or `_lost` (deaths by the same fight) and the survivors stay the free `RebelBand`.
- **Hero:** join, lead (his Leadership + Speech must beat the leader's), stand with the guards or mediate (his Speech against unrest / 10, and the lord's best concession utility at least 200; the concession is then made through `LordBrain.Act`), each a deed on his standing; as lord, a grain dole and a pardon. The hero can lose his own fief to a revolt by the same rules.
- **Not built:** the live tier (no `ActionKind.Riot` scoring, no crowd as people in the bubble, the hero cannot fight in a revolt), the Land overlay shading, buttons for the lord's concessions on the Fief tab (the commands and the rows exist), the `raise_guards` lord action, the nutrition-reserve hunger measure, capture and ransom of a deposed lord (he is simply no longer the holder), a casus belli for the liege, the mediator's Speech as a die (it is a threshold), unpaid wages as a grievance source (no source wired), the common's enclosure and purveyance call sites (the hooks `UnrestConcede.Enclose` and `UnrestBook.Add` exist), the history's `Rebellion` using `UnrestRules` (part F).
