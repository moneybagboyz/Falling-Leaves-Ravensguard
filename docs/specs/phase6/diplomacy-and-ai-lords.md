# Diplomacy and AI lords: opinion, treaties, war utility, lords who plan, the road from sellsword to lord, founding a faction

Plan refs: §4A.4 (diplomacy: integer opinion -100..100; treaties (truce, alliance, vassalage) with expiry days; war utility = claim + casus belli (a chronicle event) + strength ratio + treasury + season - harvest cost; peace through war score and war-weariness; every declaration a chronicle entry with its cause; AI lords and armies: utility AI with considerations in JSON, recruits are real persons, the dead stay dead, defeated lords captured and ransomed, the AI acts only on last-known positions from scouts and rumours with their age; granting a fief is a political act), §3.10 (factions react to you; you can become a vassal, then a lord, then found your own faction), the plan's progression (wanderer -> sellsword -> householder -> vassal -> lord -> founder), §4A.2 (keep the governor's utility scorer as the lord's planning brain; fix "swearing fealty hands you a settlement taken from its lord, silently"). Phase 7 owns live armies and campaigns; Phase 6 wars are fought by the abstract battle resolver v1 and the continent tick.

## 1. Purpose
Lords and rulers become people with a brain: each season they weigh what they could do (tax, marry off a daughter, call a council, demand dues, appoint, build, grant, declare war, sue for peace) by considerations in content, acting on what they know and how old that knowledge is. Their opinions of each other and of the hero are numbers with causes. The hero climbs the same ladder by the same rules: service earns fealty, fealty earns a manor, a manor and a marriage or a deed earn a fief, and a lord can renounce his oath and found a faction. **Done-when:** you can rise to lord.

## 2. Dependencies
| Needs | From |
|---|---|
| History realms, rulers, dynasties, grudges, claims, casus belli kinds, treaties (`Treaty`, `TreatyKind`), wars | Phase 1 history |
| The continent tick (realm summaries per season), abstract battles v1, captivity and ransom | Phase 2, 4 |
| Fief deeds, dues, councils, charters, officials | `rule-and-income.md` |
| Standing, renown; marriages and alliances; excommunication | `reputation-and-standing.md`, `family-and-lifespan.md`, `faith-and-church.md` |
| Contracts (service, take a fief), rumours with age | `contracts-and-rumours-v2.md` |
| Unrest (a lord's concerns), revolts | `unrest-and-revolt.md` |
New: `Sim/Politics/` (`OpinionBook`, `TreatyBook`, `WarUtility`, `LordBrain`, `Fealty`, `Factions`), content `politics/considerations.jsonc`, `politics/opinion.jsonc`. Extended: `ContinentTick` (realm diplomacy uses the same `WarUtility`), history (`history-recalibration.md`). Godot: the **Faction (F)** screen (bound since Phase 0, now built).

## 3. Data
- **`Opinion`** (saved, optional section `diplomacy`): `(person a, person b) -> { Value (-100..100), Modifiers[] (kind, amount, day, decay per season, cause entry) }` for lords, rulers, bishops, mayors and the hero (about 400 pairs a region). A realm's opinion of another is its ruler's.
- **`politics/opinion.jsonc`**: modifier kinds and sizes: kin +20, marriage alliance +30 (no decay while the marriage lasts), same religion +10, heresy -40, gift +1 per 10 crowns (max +20, decays 5 a season), unpaid due -20, broken oath -60, war -50 (decays after peace 10 a season), helped in war +25, insult -15, excommunicated -30.
- **`Treaty`** (from history, extended): `{ A, B, Kind (Truce, Alliance, Vassalage, MarriagePact, TradePact), Start, ExpiryDay (Truce 2 seasons, Alliance 2 years, TradePact 1 year, Vassalage none), Terms (tribute mites a season, customs reduced), Cause }`.
- **`LordBrain` considerations** (`politics/considerations.jsonc`): per action, a list of curves over inputs (treasury in seasons of payroll, unrest permille, opinion of the target, strength ratio, threat (enemy strength within 2 regions, by last-known age), ambition traits: greedy, pious, martial, just, cautious) multiplied into a utility 0..1000.
- **Traits** of lords (from history notables, else rolled at seeding with stream **0x670**, key person): two of greedy, pious, martial, just, cautious, cruel, generous.

## 4. Rules

### 4.1 War utility (one function, live and history)
`WarUtility(a, b) = claim (30 if a holds a claim on b's land) + casus belli (the chronicle event's weight: 10 an insult, 20 unpaid dues, 30 a broken treaty, 40 a seized fief or a raid, 50 excommunication of b) + 30 x (strength ratio - 1) (clamped -60..60; strength = levy men + retinues, last-known) + 5 x treasury seasons (max 20) + season (spring +10, summer +10, autumn -10, winter -30) - harvest cost (the levies' lost yield as permille of a's yearly receipts / 10) - 20 if allied with b - 30 if a truce holds`. A ruler declares at >= 50 (cautious >= 70, martial >= 40); the declaration is a chronicle entry naming its largest term as cause. **Peace:** each season at war, `war score` (battles and sieges won, fiefs held, from abstract battles) and `weariness` (+10 a season, + dead / 1% of population x 5, + treasury below 1 season x 10) decide: the loser sues when `weariness - war score >= 40`; terms: a truce, tribute (5% of receipts for 2 years) or a fief ceded (score >= 60).

### 4.2 Lords' seasonal planning (`LordBrain`)
At the season's first day (H6, a new slice `S10Politics` after migration, spread one fief per hour) each lord scores his options and takes the best one or two above 300: set the tax level (unrest vs treasury), appoint or dismiss officials (an audit found a thief), evict squatters, post works (the lord's planner: walls, a mill, a bridge, soil improvement contracts), arrange a child's match, give a gift, demand overdue dues, call a vassal's service, ask the ruler for a fief (petition) or a charter, raise a grievance, ask the bishop for an excommunication, call a council (rulers), declare war or sue for peace (rulers; lords join their liege's war by the vassalage treaty), grant a vacant fief (rulers: to the candidate with the best `opinion + standing / 10 + service done`), punish rebels, pardon. Inputs are **last-known**: a lord knows another's strength and treasury from rumours with their age; a fact older than 2 seasons counts at half weight. Every action is a chronicle entry with its top consideration as cause ("raised the tallage: the treasury held less than one season").

### 4.3 The hero's road to lord
| Rung | How | Rules |
|---|---|---|
| **Sellsword** | contracts for a lord (guard, escort, bounty) | `contracts-and-rumours-v2.md`; standing with the fief from each |
| **Vassal** | the lord offers **fealty** when the hero has standing with his fief >= 300, renown >= 100 and 3 completed contracts for him, and the lord's `opinion >= 30` (or the hero asks: the same test); an oath at the manor on a holy day (chronicle `fealty_sworn`) | the hero owes service (7 days a year), aids, the lord's law; the lord owes protection (opinion -60 if he does not ransom or defend) |
| **Manor** | a vassal is granted a **manor** (a village and its demesne as a sub-fief deed) as reward for service: the lord's `grant` action when the hero's opinion >= 50 and a manor is vacant (escheated, forfeited, or his demesne village); never a settlement taken silently from its holder: a grant moves only a vacant or the granter's own holding (the Phase 1 bug fixed by construction) | the hero sets policy there, appoints a reeve, collects |
| **Lord** | holding a **fief deed**: granted by the ruler (vacant fief, a reward for war service, a marriage to an heiress with the overlord's consent), inherited, or won (a revolt or a war's terms); relief paid | `rule-and-income.md` 4.2 |
| **Founder** | a lord who **renounces** his oath (a chronicle `oath_renounced`; his liege's opinion -60, a casus belli) or who founds on unclaimed land by charter of no one (a founding outside any realm, Phase 5 claims) becomes the ruler of a new realm (a faction: name, banner colours, his fiefs); vassals of his choose by their opinion of him vs the old liege | the realm joins the continent tick; it can be at war at once |

### 4.4 AI faction reactions to the hero
Every realm and lord reacts by the same opinion modifiers and standing (no special cases): a hero who robbed a ruler's caravan is a casus belli for that ruler's lords if they hear of it; a hero who freed a lord from captivity gains +25. Bandit and slaver bands keep their own standing groups and hunt a hero with standing <= -500 with them.

### 4.5 Defeated lords
The dead stay dead (succession by `Demography`); a lord defeated in an abstract battle is captured and ransomed by Phase 4 rules at the `Baron` rank price; a lord who cannot pay may cede a fief instead (the captor's option).

### 4.6 Tick placement
| Where | What |
|---|---|
| H6 S10Politics (new slice, after S9's continent summary; one fief per hour of the day) | `LordBrain` for each lord; rulers' war and peace |
| H6 S9 (continent tick) | the abstract realms' diplomacy with the same `WarUtility`; abstract battles v1 between realms |
| Barrier | the hero's fealty, petitions, gifts, renunciation; the Faction screen's actions |
| H5 hour 0 | opinion modifier decay at a season's first day; treaty expiries |

## 5. Determinism
Streams: **0x670** lord traits at seeding, **0x671** ties between equal utilities (key lord and season), **0x672** envoy road events (key envoy and day). Abstract battles use their Phase 4 streams. Lords are planned in fief id order, one per hour; the continent's realms in ascending id. `PoliticsHash` covers opinions, treaties, wars and lord actions.

## 6. UI (960 x 540)
The **Faction (F)** screen (context `Faction`, overview 6): tabs Realm (ruler, liege chain, vassals, fiefs, treasury band), Opinions (the people who matter, value, top three modifiers with links to their chronicle causes), Treaties and wars (expiry, war score, weariness), Your standing (from reputation), Actions (petition, gift, swear fealty, renounce, ask for a fief) each showing the utility terms in words ("He will say no: he thinks little of you (-20) and the land is contested (-50)").

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| DA-1 | War utility | claim 30, casus belli 40 (a raid), ratio 1.5 (+15), treasury 3 seasons (+15), spring (+10), harvest cost 10: 100 -> declares; in winter 60 -> declares only if not cautious |
| DA-2 | History equivalence | the history's war decisions call `WarUtility`: over 50 seeds x 200 years, the share of realm-pairs at war at any time <= 35% (the old dice: ~71%) |
| DA-3 | Peace | weariness 60, score 15: the loser sues; terms a truce; at score 65 a fief is ceded with a chronicle entry |
| DA-4 | Last-known knowledge | a lord whose rumour of a rival's strength is 3 seasons old weights it at half; a fresh rumour changes his plan |
| DA-5 | Lord actions with causes | 4 seasons of `LordBrain` over a region: every action a chronicle entry with a specific cause; no action without one |
| DA-6 | Fealty offered | standing 300, renown 100, 3 contracts, opinion 30: offered; at 290 standing not |
| DA-7 | Manor grant | a vassal with opinion 50 and a vacant escheated manor: granted as a sub-fief deed; no settlement leaves a living holder |
| DA-8 | **Rise to lord** (OV-2 part) | the hero is granted a vacant fief by the ruler for war service: a fief deed, relief paid, Fief tab live |
| DA-9 | Renounce and found | a lord renounces: a new realm with his fiefs; the liege's opinion -60 and a casus belli; vassals choose by opinion; the continent tick knows the realm |
| DA-10 | Gifts cap | 300 crowns of gifts give +20 at most; decay 5 a season |
| DA-11 | Ransom or cede | a captured lord with an empty treasury cedes a fief to the captor |
| DA-12 | Determinism, save | 2 years of politics: `PoliticsHash` equal across runs; save -> load -> save byte-identical with a war and a treaty under way |

## 8. Open questions (recommended defaults)
1. **How fast to lord:** fealty after about 3 contracts and 300 standing; a manor after more service; a fief from war service, marriage or a vacancy. The OV-2 bot reaches lord in about 2 game years. Default as written.
2. **Wars before Phase 7:** fought by abstract battles and sieges (results, raids, burned fields, captured lords, ceded fiefs); the hero joins as a contract (a garrison, a raid escort). Live armies are Phase 7. Default as written.

## 9. Budgets
`LordBrain` <= 2 ms a lord a season (one an hour: never more than 2 ms in an hour); continent diplomacy <= 20 ms a season at S9; opinions <= 0.1 ms at a season's decay; save: opinions about 400 pairs x 40 B, treaties and wars <= 10 KB.
