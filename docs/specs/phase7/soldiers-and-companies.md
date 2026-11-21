# Soldiers and companies: troop roles, loadout templates and per-person refit, skills by fighting and drilling, promotions and wages, who arms whom, retinues, mercenary companies, prisoners and ransom at scale

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: locked decision "Soldiers' equipment and growth" (loadout templates per troop type filled from your armoury with real items, best available, gaps shown; any single soldier opened and re-equipped piece by piece, Dwarf Fortress style; **no XP levels**: weapon skills by fighting and drilling; promotions levy -> spearman -> man-at-arms -> knight, each with a loadout, a wage and a skill requirement; who arms them: a historical mix, levies bring gear by their wealth, retinues and mercenaries are armed from their lord's armoury; loot recycled through repair and refit), §3d (recruitment: levies, retinues as paid professionals, mercenary companies as hired groups of real people), §4A.3 (progression: skill gain per meaningful use = 0.4 x difficulty x (1 - s/100)², a recruit 20 to 40 in about 6 fights; prisoners: ransom from the captive's faction treasury, prisoners eat and can escape), §4A.4 (recruits are real persons; existing mercenary companies; the old contracts' expiry bug), §2.10 (items as a table with parts; fit and refitting), §5 Phase 7 ("levies and mercenaries"). **Division with the economy writer:** the armoury building, its stockpiling in peace, munition-grade production, the arms trade and loot recycling are the economy specs'; this spec owns the templates, the filling rule, the soldier's own kit and the wage roll's rows. Money in copper (cp).

## 1. Goals
1. A soldier is a person with a **role**: a loadout template, a wage and a skill requirement; he grows by fighting and drilling and is promoted when he qualifies, never by XP.
2. Kitting an army is real logistics: a template is filled from the real items in an armoury (best first), the gaps are shown, and any one soldier can be opened and refitted piece by piece.
3. Who arms whom follows history: levies come with their household's gear; retinues and hired companies are armed (and their gaps filled) from their lord's armoury.
4. Mercenary companies are groups of real people with a captain, a purse, a reputation and a contract (Phase 6 contracts), who leave or turn on an employer who does not pay.
5. Prisoners after a battle of thousands are handled by rules that scale: the few worth ransom are held and ransomed from real treasuries, the many are paroled, put to work, exchanged or sold where the law allows.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Combat/Melee2/FightSetup.cs`, content `combat/fight_kits.jsonc` (`bandit_light`, `guard`, `soldier`, `traveller`, `default`, beasts) | kits rolled for encounter casts (weapon from a list, skill range) | **kept** for casts and bandits; soldiers of armies fight with their **real items** (`FighterKit` from the person's worn and carried items, already how the hero and companions fight) |
| `Sim/Goods/ItemTable.cs`, `ItemQuality.cs`, `Corrosion.cs`, `Inventory/Carried.cs`, `Encumbrance.cs`, `HeroOutfit.cs` | item instances with parts (condition, quality, fit, stains), carried and worn, encumbrance | **read:** templates match item rows; issuing moves rows |
| `Sim/Clothing/ClothingBook.cs` (fit sizes, refit P6-B6), crafting refit recipes | garments' fit; resizing armour as a craft | **read:** fit is part of the filling score; refit jobs for misfits |
| `Sim/Trade/Progress.cs` (`SkillUse`, `Progress.Use`: the plan's gain formula with an hourly cap), `Onboarding/PracticeYard.cs` (`DummyHit` cap 30, `SparBout`) | skill growth by use; the practice yard | **extended:** drill uses (4.4) |
| `Sim/People/Types.cs` (`Skill`: Blades, Axes, Hafted, Polearms, Shields, Throwing, Archery, Riding, Tactics, Leadership; `Occupation`) | | `Occupation` +`Soldier` (armies spec), +`Sailor` (ships spec) |
| `Sim/Trade/Hiring.cs` (`Recruiting.SquadCap = 2 + Leadership / 10`, 2..12), `Squad.cs` (followers, `FollowerOrigin`) | the hero's companions | **extended:** a lord's retinue beyond the squad (4.5) |
| `Sim/Contracts/*` (Phase 6 C2: `ContractSystem.Post`, escrow, deadlines, completion from events; stream 0x690 company ties) | contracts from need | **extended:** the `mercenary_service` contract kind (4.6) |
| `Sim/Combat/Aftermath/*` (`CaptiveBook`, `Captivity`, `Ransom`, `DefeatTable`; `captivity.jsonc`: rank prices levy 1,300 mites, man-at-arms 4,000, knight 20,000, baron 100,000; army hold 40 days), `Combat/Abstract/BattleRansom.cs`, `Politics/Diplomacy.Ransom` (lords, `TransferReason.LordRansom`) | capture and ransom for skirmishes and abstract battles | **extended:** prisoners at scale (4.7) |
| `Sim/Bondage/*` (Phase 6 slavery, `CaptorKind.Slavers`, war-captive law) | | **called:** prisoners sold where the culture's law allows war captives |
| **New** | | `Sim/Soldiers/`: `TroopRoles`, `Loadouts`, `ArmouryFill`, `Refit`, `Drill`, `Promotions`, `WageRoll`, `Retinues`, `MercCompanies`, `PrisonerPens`, `SoldierBook` (optional section `soldiers`), `SoldierDesk` (tag `so:`), `SoldierView`; content `war/troops.jsonc`, `war/loadouts.jsonc`, `war/companies.jsonc`; Godot `UI/ArmouryScreen.cs` (context `Armoury`), the Inventory screen's Refit mode |

## 3. Data

### 3.1 Troop roles (`war/troops.jsonc`)
| Role | Skill requirement | Default loadout | Wage (cp a day) | Ransom class |
|---|---|---|---|---|
| `levy` | none | `levy_by_wealth` (4.3) | 0 within owed service, 15 after | levy (130 cp) |
| `spearman` | Polearms 25, Shields 15 | `spearman` | 25 | levy |
| `archer` | Archery 30 | `archer` | 25 | levy |
| `crossbowman` | Archery 20 | `crossbowman` | 30 | levy |
| `sergeant` (light horse) | Riding 35, a weapon family 30 | `light_horse` | 50 | man-at-arms (400 cp) |
| `man_at_arms` | a weapon family 45, Shields 30 (or Polearms 45) | `man_at_arms` | 75 | man-at-arms |
| `knight` | a weapon family 55, Riding 45, **dubbed** by a lord (4.5) | `knight` | 150 (and his horses' fodder) | knight (2,000 cp) |
| `engineer` | Carpentry 30 | `engineer` | 40 | levy |
| `sailor`, `marine` | (ships spec) | | 25, 30 | levy |
Wages are the Phase 2 `soldier_wage_mites_day` 250 (= 25 cp) as the spearman's, with the historical ratios (archer 1, man-at-arms 3, knight 6). A mercenary's wage is **x1.5** the role's.

### 3.2 Loadout templates (`war/loadouts.jsonc`)
A template is a list of **slots**, each an ordered list of acceptable item kinds with a floor:
`{ slot: "weapon", accept: ["spear", "bill", "pike"], min_quality: "poor", min_material: ["iron", "steel"], required: true }`.
Slots (the §2.6 layers and the separate slots): `weapon, alt_weapon, shield, helmet, under, over, armour, plate, cover, legs, hands, feet, launcher, ammo (count), throwables (count), horse, barding, saddle, tool (stakes, spade)`.
Examples: `spearman`: spear (or bill), round or heater shield, kettle hat or cap, gambeson, linen, cloak; `archer`: longbow or short bow, 24 arrows, a sword or axe or dagger, cap, gambeson or jack, a stake (cavalry spec); `man_at_arms`: arming sword or axe or mace, heater, helm, gambeson, mail hauberk, mail chausses or leg harness, gauntlets; `knight`: as man-at-arms with plate where it exists, a lance, a courser with saddle and (optional) barding.

### 3.3 The soldier's rows (optional section `soldiers`, keyed by person)
`SoldierRow { Person, Role, Lord (who pays him), Company (or 0), Template (id, may be a custom copy), Issued (item ids given from an armoury, with the armoury), OwnKit (item ids he owns), DrillHours (by family), FormationDrill (0..1000, his unit's), PromotedDay, WageCp, ServiceRow (armies spec) }`.
`MercCompany { Id, Name, Captain, Members, Purse (a Ledger purse), Reputation (-1000..1000), Culture, Speciality (Crossbows, Pikes, LightHorse, Foot), Contract, Region, Lair (between contracts), Kit (owned items) }`.
`PrisonerRow { Person, Captor (realm, lord or company), Pen (settlement or camp), Since, Class (Common, Ransom), Value cp, Parole (bool, until day), Work (job), Exchange (war id) }`.

## 4. Rules

### 4.1 Becoming a soldier
A person becomes a soldier by: the levy draw (armies spec 4.2: role `levy`), hiring (a lord's retinue: `Hiring` as companions, any person 16+ who accepts the wage and passes the role's requirement), promotion (4.5), joining a company (4.6), or the hero's squad (companions take roles too). Every soldier is still his household's member; his death, wounds and wages are real for them.

### 4.2 Filling a template from an armoury (`ArmouryFill`)
- **Input:** a list of soldiers (a formation, a contingent, a company), each with a template, and the armoury's item rows (the economy spec's armoury store; the hero's own chests count as an armoury).
- **Order:** soldiers by role priority (knight, man-at-arms, sergeant, crossbowman, archer, spearman, levy), then skill in the slot's family descending, then person id; slots in template order. Pure; no draw.
- **Choice per slot:** among acceptable items, the highest score: `quality tier x 100 + condition / 10 + material rank x 50 - misfit penalty (300 if the fit size differs by more than 15%, armour only; 0 if a refit can fix it within the armoury's craftsmen's queue) + 20 if the soldier already wears it`. A soldier's **own** kit counts first when it scores at least as well (he keeps what is his).
- **Gaps:** a required slot with nothing acceptable is a **gap**, shown in the Armoury screen per soldier and summed per template ("12 spearmen without helmets, 3 without shields"); optional slots left empty are listed greyed.
- **Issuing** moves the item rows from the armoury's store into the soldier's worn or carried (`Issued` records which armoury it came from); discharge returns them (a job: the soldier hands them in at the armoury, or the quartermaster collects in camp). Items lost (on corpses, deserters' backs, captured) are real losses; corpses are looted by the field's holder (Phase 4 loot rule: one item per item, not per slot covered).
- **Refit** (`Refit`): misfit armour gets a refit job (Phase 6 refit recipe: an armourer's time and skill) before issue when an armourer of the armoury's business is free; else it is issued misfit (Phase 4 fit penalties apply).

### 4.3 Who arms whom
- **Levies bring their household's gear**, by wealth class (the household's wealth tier, Phase 2): **poor**: a tool-weapon from the household (axe, billhook, flail, club, or a dagger), his tunic; **middling**: + a spear, a round shield, a padded cap or kettle hat, a gambeson; **well-off**: + a mail shirt or a helmet, a sword or axe. They are real items of the household (moved to him); if the household has none of a kind, he goes without. The lord may fill a levy's gaps from his armoury (a choice in the Army tab, default: shields and spears only).
- **Retinues** are armed from their lord's armoury (their template, filled by 4.2); what they buy themselves stays theirs.
- **Mercenaries** come with their own kit (the company's items, as real people do); the employer's armoury **fills their template's gaps** if the contract says so (default: yes, issued items returned at the contract's end). (The locked table says mercenaries are armed from the lord's armoury; this reads it as "their gaps are", since a company of real people already owns what it carries: open question 1.)

### 4.4 Skills by fighting and drilling (no XP levels)
- **Fighting:** every meaningful use in a fight (a landed or blocked blow, a parry, a shot that hits, a braced strike) is a `Progress.Use` of the weapon family at the opponent's skill as the task difficulty (Phase 4 rule, unchanged): a recruit goes 20 to 40 in about 6 fights.
- **Drilling** (`Drill`): a drill job of up to 2 hours a game day at a practice yard, a camp's drill ground (a cleared 20 x 20 m area) or a barracks yard (economy spec building). Each drill hour is a `SkillUse("drill_<family>", difficulty 40, cap 40)` for the drilled family (Phase 3 rule: gains stop at the cap), and a `SkillUse("drill_shields", 40, 35)` for shield bearers. **Sparring** with a better partner (`SparBout`) trains to `partner's skill - 5`, at most 60. Drill costs stamina and 0.2 kg of extra food a man a day (fatigue: Phase 3).
- **Formation drill:** each drill hour in formation adds `+20` to the unit's `FormationDrill` (0..1000), less `-5` a day without; the battle spec reads it (closing ranks, deployment, reform speed) and the desertion roll (`-5 x Drill / 200`).
- **Attributes** grow as Phase 3 (+0.02 a day of matching activity: strength from drill with weapons, endurance from marching).

### 4.5 Promotions, dubbing and retinues
- **Promotion** is a lord's (or the hero's) act: offered at the season's first day to every soldier who meets the next role's requirement (levy -> spearman or archer -> man-at-arms -> knight; spearman -> sergeant with Riding), or at any time by the hero in the Army tab. The new role brings its template (filled from the armoury: the promotion is shown with its gaps) and wage. AI lords promote while the wage roll stays within 50% of their receipts a season.
- **Knighting (dubbing):** a lord dubs a man-at-arms who meets the knight's skills and has (or is given) a war horse; a chronicle entry; standing +100 with the lord's fief (Phase 6 standing); the knight's wage or a manor (Phase 6 manor grant) as his fee. The hero can be dubbed by his lord on the same test, or dub his own men as a lord.
- **Retinues:** a lord's paid professionals (his warband, armies spec 3.1). The hero as a lord: the retinue cap is his holding's (`war/troops.jsonc`: manor 6, knight's fee 12, baron 40, count 120, ruler 300), replacing the squad cap for soldiers (companions stay under the squad cap). Retinue members keep `FollowerOrigin.Hired` or `Companion`; a promoted levy kept on becomes `Hired`.
- **Discharge:** a soldier released returns issued kit and goes home (or stays a wandering sellsword: Phase 3 hiring pool).

### 4.6 Mercenary companies
- **Who they are:** groups of 20-200 real people (`war/companies.jsonc`: a name pattern per culture, speciality, size band), seeded by the Phase 7 seeding step (2-4 a region, more in rich or warring lands) and by the continent tick between regions (count companies; made into people at the gate as hosts are, armies spec 4.4). Each has a captain (Leadership and Tactics best), a purse (Ledger), kit, a reputation (fidelity) and a lair (an inn or a camp) between contracts.
- **Hiring** is a Phase 6 contract of kind `mercenary_service`: terms `{ days (15-60), wage cp a man-day (role wage x 1.5), advance (half of the first 6 days, escrowed), plunder share (1/3 of loot taken by them), ransom share (1/3 of ransoms of prisoners they took), fill gaps (bool), end place }`. Pay runs through `Provision.PayRoll` (armies spec 4.7) to the company's purse, which pays its men (a real transfer to each member: wage less the captain's 10%).
- **Loyalty:** the company's daily choice (stream 0x7B0): arrears up to 5 days: grumble (morale -5); 6+ days: leave the host (the contract broken, the employer's standing with companies -200, a chronicle entry) or, if the employer's lands are near and the company's reputation < 0, **plunder** them on the way out (raids as a warband's, armies 4.1); a better offer from the enemy at x1.5 the wage with reputation < 300: change sides (once, chronicle; reputation -300).
- **Between contracts** companies roam to where wars are (rumours), take guard contracts, or live off the land (reputation < 0: bandit raids, armies spec 4.8). Reputation rises +50 for each contract kept, -200 for one broken by them.
- The old bug (contracts' expiry against the day of the month, §4A.4) cannot recur: deadlines are absolute game days (Phase 6 C2).

### 4.7 Prisoners and ransom at scale
- **Taking:** after a battle, the field's holder takes the yielded and the downed as prisoners (Phase 4 aftermath), up to what he can guard (1 guard per 10 prisoners, taken from his men; the rest are let go or, by the commander's traits, killed: `cruel` 500 pm, Phase 4's `cruel_kill_pm`).
- **Sorting** (pure, by person): `Ransom` class: knights, lords, men-at-arms of a ransomable rank (Phase 4 rank prices: man-at-arms 400 cp, knight 2,000 cp, baron 10,000 cp, more by holding: the Phase 6 `Baron` row), anyone whose kin or lord have the means; `Common` class: everyone else (levy price 130 cp, rarely paid).
- **Ransom:** asked of the captive's faction treasury, his lord, or his household (Phase 4 `Ransom` and `BattleRansom`: from real purses; halves after 5 days, quarters after 10; a lord unable to pay may cede a fief: Phase 6). The captor's share and the companies' third go by the contract.
- **Common prisoners** (the many): **paroled** (sworn not to fight this war for a season, released; breaking parole is a crime and standing -200), **put to work** (camp labour, siege works; fed 1 kg a day from the host's stock), **exchanged** at the peace or by truce (a war's prisoners lists), or **sold** where the captor's culture allows war captives (Phase 6 `Bondage`, `CaptorKind.Slavers`, the nearest market). The commander chooses by traits and law (AI: just: parole; greedy: sell where legal; cruel: work or kill), each choice a chronicle entry for 20+ prisoners.
- **Pens:** a host holds prisoners in a pen in camp (a palisade ring; escapes by the Phase 4 window 0x454 with 1 guard per 10), a town in its gaol (Phase 6). Prisoners eat; unfed 2 days they are released (Phase 6 CJ-16 rule).

## 5. Tick placement
| Where | What |
|---|---|
| H5 hour 0 | drill hours credited; wage rows due; mercenary loyalty (one company an hour of the day); prisoners fed, ransom terms halve |
| H6 season's first day | promotions offered; company seeding growth and drift |
| Barrier | issuing and refits (the hero's commands; AI at muster); prisoners sorted after a battle |
| Live | drill jobs in the bubble are visible (men at the yard in formation) |

## 6. RNG streams (0x7B0..0x7BF)
| Stream | Use | Key |
|---|---|---|
| 0x7B0 | a company's loyalty choice | company, day |
| 0x7B1 | company seeding (size, speciality, names via the culture's pools) | region, index |
| 0x7B2 | a company changing sides | company, war |
| 0x7B3 | prisoners killed by a cruel captor (who) | battle, prisoner |
| 0x7B4 | parole broken | person, season |
| 0x7B5..0x7BF | free |
Reused: 0x454 (escape window), 0x456 (ransom), 0x690 (company ties in contracts).

## 7. Saves
Optional section **`soldiers`** (absent until a soldier row exists): soldier rows, companies, prisoners, custom templates. Items stay in the item table (issued rows are ordinary items with an owner). Additive keys: contracts (`mercenary_service` terms), followers (`Role`). v8 stays. `SoldierHash` joins the trail once non-empty.

## 8. Chronicle
| Type | Key | Params | Cause |
|---|---|---|---|
| `soldier_promoted` | `ch_so_promoted` | person, role key, lord | his fights or drill (note `ch_note_so_skill`) |
| `knight_dubbed` | `ch_so_dubbed` | knight, lord, place | the promotion |
| `company_hired` / `company_left` / `company_turned` | `ch_so_hired`, `ch_so_left`, `ch_so_turned` | company, employer, men, terms | the contract; the arrears (`ch_note_so_unpaid`) or the offer |
| `prisoners_taken` | `ch_so_prisoners` | captor, count, ransom class count, fate key (`ch_so_fate_parole`, `_work`, `_sold`, `_killed`, `_exchanged`) | the battle |
| `ransom_paid` | `ch_so_ransom` | captive, payer, cp | the capture (existing `LordRansom` entries keep their type) |

## 9. UI and keys (960 x 540)
- **The Armoury screen** (context `Armoury`, modal, alone with Global; opened from the Army tab, a Q verb at an armoury building or chest, or the Squad panel): left the soldiers grouped by formation and role with a gap count each; centre the selected template's slots with what is filled and what is missing; right the armoury's stock by slot with counts and quality. Keys: Up / Down soldiers, Left / Right slot, Tab pane, Enter assign or open, **F** fill all (the 4.2 rule), **G** show gaps only, **T** edit the template (a custom copy), `/` search, Esc back.
- **Refit one soldier** (Dwarf Fortress style): Enter on a soldier opens the Inventory screen in Refit mode: his paper-doll with each layer, the template's slot beside each, and the armoury's acceptable items; swap a piece with Enter; the refit job for a misfit is offered.
- **Army tab additions:** promotions due (accept / refuse each), the wage roll (role, wage, arrears), companies under contract (terms, days left, loyalty mood), prisoners (class, value, fate buttons: Ransom, Parole, Work, Exchange, Sell where legal, Release).
| Context | Keys | Origin |
|---|---|---|
| `Armoury` (new, modal) | Up / Down, Left / Right, Tab, Enter, F fill all, G gaps only, T template, `/` search, Esc back | new |
New verbs without keys (Q menu): Enlist, Offer a role, Dub, Hire this company, Pay the company, Take prisoner, Parole, Ransom.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| SC-1 | Roles and wages | every role in `war/troops.jsonc` has a template, wage and requirement; a spearman's wage 25 cp, a mercenary spearman's 37 cp (x1.5 rounded down) |
| SC-2 | Fill order | 30 spearmen and 10 men-at-arms against an armoury of 25 mail shirts: men-at-arms first, then spearmen by skill then id; gaps listed per soldier |
| SC-3 | Best first | two helmets (good, worn 40%; fine, 90%): the formula picks per score; own kit kept when it scores as well |
| SC-4 | Fit | a mail shirt 20% too small scores -300; with an armourer free a refit job is queued before issue |
| SC-5 | Issue and return | issue then discharge: every item row back in the armoury; mass and item counts exact |
| SC-6 | Levies by wealth | poor, middling and well-off households give the listed kinds from their own items; a household without a spear gives none; the lord's fill gives spears and shields only by default |
| SC-7 | Fighting growth | a 20-skill recruit in 6 fights at the plan's rate reaches 38..42 |
| SC-8 | Drill cap | 60 drill hours take a 20 to 40 and no further; sparring with a 55 takes him to 50 |
| SC-9 | Formation drill | +20 an hour, -5 a day without; a unit at 600 closes ranks in 16 ticks |
| SC-10 | Promotion | a spearman reaching Polearms 45 and Shields 30 is offered man-at-arms at the season's start; the new template's gaps shown |
| SC-11 | Dubbing | a man-at-arms with 55 and Riding 45 and a courser dubbed: entry, standing +100, wage 150 |
| SC-12 | Retinue cap | a baron hero: 40 soldiers beyond his squad of companions; the 41st refused with the reason |
| SC-13 | Company contract | a 60-man company hired for 30 days: advance escrowed, wages to its purse weekly, each man paid less 10%; coin exact |
| SC-14 | Company leaves | 6 days of arrears: the company leaves (or plunders with reputation < 0); chronicle with the cause |
| SC-15 | Company turns | an enemy offer of x1.5 wage, reputation 200: turns once; reputation -300 |
| SC-16 | Prisoners sorted | 400 prisoners after a battle: 12 ransom class by rank price, 388 common; fates by the commander's traits; every common prisoner fed or released |
| SC-17 | Ransom at scale | 12 ransoms asked of 3 treasuries: paid from real purses with halving at 5 and 10 days; a lord who cannot pay cedes (Phase 6 DA-11) |
| SC-18 | Save | soldiers, companies, prisoners: save, load, save byte-identical; `SoldierHash` equal across runs |

## 11. Budgets
`ArmouryFill` for 1,000 soldiers against 5,000 items <= 20 ms (at a barrier, the muster or the hero's F); H5 hour 0: drill, wages, loyalty, prisoners for 2,000 soldiers <= 1.5 ms; save <= 120 KB for 2,000 soldier rows and 10 companies; content <= 40 KB.

## 12. Open questions (recommended defaults)
1. **Mercenaries' kit:** they keep their own and the employer fills their gaps from his armoury (the locked table read with real people's own items). (For the user.)
2. **Levy wealth classes:** poor, middling, well-off as above. Default as written.
3. **Wages:** spearman 25 cp a day (the Phase 2 soldier wage), archer 25, crossbowman 30, sergeant 50, man-at-arms 75, knight 150; mercenaries x1.5. Default as written; the economy audit may move the base.
4. **Drill cap 40**, sparring to the partner's skill - 5 up to 60; beyond 60 only by real fights. Default as written.
5. **Killing prisoners** is possible for cruel AI commanders and for the hero (an atrocity: standing -300 with every group that hears, the chronicle). Default yes.

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| SC-A roles, templates and the armoury | `war/troops.jsonc`, `war/loadouts.jsonc`, `ArmouryFill`, issuing and return, refit jobs, who arms whom, the Armoury screen and Refit mode | SC-1 to SC-6 | 1.3 |
| SC-B growth and the wage roll | fighting uses, drill and sparring, formation drill, promotions, dubbing, retinues and their cap, wage rows (paid through the provisioning call) | SC-7 to SC-12 | 0.9 |
| SC-C mercenary companies | seeding, the contract kind, pay through the company purse, loyalty, leaving, plunder, turning, roaming between contracts | SC-13 to SC-15 | 1.0 |
| SC-D prisoners at scale | sorting, ransom with real purses, parole, work, exchange, sale where legal, pens, feeding | SC-16, SC-17, SC-18 | 0.7 |
| **Total** | | | **3.9 months of code** |

## Notes (P7-SC-A, built)
- **What exists:** the roles and templates are content (`war/troops.jsonc`, `war/loadouts.jsonc` with the `soldier_rules` row: score terms 100 a quality tier, condition / 10, 50 a material rank, -300 misfit, +20 worn, fit within 15%, mercenary x1.5). `ArmouryFill.Plan` is pure and order-free in its input; `Apply` moves items only (armoury store to the man through the issue book; an item is in the store or on one man). `Discharge` returns issued kit with the wear it has, a lost item is simply gone from the book. `LevyGear.Bring` moves the household's own items by wealth class (poor/middling/well-off from 0, 20,000 and 100,000 mites; the 100,000 is a tunable, the 20,000 is AE's floor). The lord's default fill for a levy is the `levy_lord_fill` template (spear and round shield). The hero is person 0: `SoldierRow.Lord` is a long with -1 for nobody, and the soldier book is keyed by the id value, so 0 is a real person and a real lord.
- **Worked example (SC-2/SC-3 style):** an armoury holds 12 spears, 12 round shields, 12 kettle hats of mixed quality and condition and 12 mail shirts; 20 soldiers, 5 of them men-at-arms. The five men-at-arms are kitted first and take the shirts (their template requires one), then the 15 spearmen take what is left by skill, then person id, each slot by score (a Fine hat at 90% scores 490, a Common one at 60% 360). Soldiers the shirts run out for list the optional `armour` slot as a greyed gap; a required slot nothing fills (say 30 spearmen and no spears) is summed per role and slot ("spearman/weapon: 30").
- **Deviations:** (1) **Clothing slots (`under`, `over`, `cover`) are `own: true`**: LB2's armoury holds only weapons, shields, helmets and body armour (`Armouries.KindOf`), so a gambeson or a cloak can never be stocked; those slots are filled from what the man already owns and are never an armoury gap. Legs and hands pieces (chausses, gauntlets) are optional for the same reason. (2) The spearman's `armour` slot is an optional one that takes mail left over (SC-2 needs spearmen to compete for shirts). (3) Man-at-arms requires Shields 30 and a weapon family 45; the spec's "or Polearms 45" exemption from shields is not modelled. (4) Refit: a refit job moves the piece to the wearer's size in one step (the crafting recipe moves a seventh a time and uses wire), and `CompleteRefit` is the hook the crafting job calls; no labour or wire is spent here (T3). Plate can be taken in, never let out. (5) A soldier's "worn" slots are the `Worn` map of his `SoldierRow` (items owned by him); the hero's `Carried`/paper doll is not touched, and horses, barding, saddles, arrows and stakes are counted goods or animals, not item rows, so they are not slots yet.
- **Left out for later parts:** the Armoury screen, the Refit mode and the Army tab, the `Armoury` input context (Godot, with a 1280 x 720 fit check); `HostSupply.Kit`/`ReturnKit` over `ArmouryFill` (AM-A); `SoldierHash` into `phase7_trail`; drill, promotions, dubbing, retinue caps, wage rows and `PromotedDay`'s use (SC-B); maintenance of issued kit (rust and repair of kit in the field would consume goods or labour, so after M-E0).
