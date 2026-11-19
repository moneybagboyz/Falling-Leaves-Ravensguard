# Morale, rout and surrender, defeat outcomes by winner, captivity, escapes, and Ironman succession by kinship

Plan refs: §4A.3 (morale 0-100 per soldier at 2 Hz: -6 per friendly death within 10 m decaying over 15 s, -15 flanked, -2 per second per routing neighbour, minus pain and blood loss, plus captain and banner; rout below 20, surrender below 10 when cornered, a captain can rally above 40; prisoners: ransom is paid from the captive's faction treasury, prisoners eat and can escape), §3.7 (failure: "if you're knocked out, what happens depends on who won: bandits rob you and leave you; slavers capture you; guards jail you; an army takes you prisoner for ransom"; ways out: pick the lock, work off the sentence, be ransomed, be rescued by companions (you take control of one), or escape during transfer; Ironman: continue as a companion or heir who inherits property and reputation, the run ends only when nobody is left and the world persists), §5 Phase 4 ("knock-outs and capture, with captivity by bandits and armies only (jail and slavery come in Phase 6); escapes; Ironman succession by kinship; property passes over fully in Phase 5, reputation in Phase 6"), §3.6 (loot), §3b (heists and jailbreaks later).
Code today: spike morale in `Sim/Combat/Melee/MeleeWorld.cs` (courage 70, `MeleeRules.RoutBelow` 20, `SurrenderBelow` 10, `RallyAbove` 40, `FriendDeathPenalty` 6, `FlankedPenalty` 15, `RoutingNeighbourPerS` 2, `HitTakenPenalty` 2, `LimbLossPenalty` 15, decay 0.4 per second); `PersonTable` (`Father`, `Mother`, `Spouse`, `BirthDay`, `Household`, `Status`, `DeathCause`), `HeroRecord`, `Followers` and `Hiring` (squad, Phase 3), `Site` (`Cave` sub-kind `hideout`), `EncounterOutcomes` (the stubbed `Fight` choice), `Demography.Kill` (death cause chain).

## 1. Purpose
Decide how fights end (morale), what a defeat costs (outcome by winner), how captivity plays and how to get out, and how a run continues after the hero dies in Ironman. Together they make defeat a story branch, not a game over ("brutal by design, fair through legibility", warnings in plan §3.13: the HUD shows odds before you pick a fight).

## 2. Dependencies
| Needs | From |
|---|---|
| Wounds, consciousness (KO, dying), treatment | `wounds-bleeding-pain-healing.md` |
| Fighter state, flanking, shield wall bonus, `Act` states | `melee-and-ranged.md` |
| Squad, followers, loyalty, `HeroParty` | exists (`Sim/Trade/HeroParty.cs`, `Sim/Live/Followers.cs`) |
| Factions and treasuries (ransom payer), bandit bands, army records | Phase 1 history factions; Phase 2 treasuries (`taxes-and-treasuries.md`); armies at scale are Phase 7, so Phase 4 uses **skirmish-scale army detachments** (spawned by encounters and war markers, `ActiveWarMarker`) |
| Items and the loot rules (value, mass, carry capacity, fit) | exists (`Sim/Goods`, `Sim/Inventory`, `Fit`) |
| Kinship columns, `Demography`, chronicle `Cause` | exists (`Sim/People`, `Sim/Core/Chronicle.cs`) |
| Lockpicking, Stealth, Athletics, Leadership skills | exists (`People.Skill`) |
| Sites (bandit hideouts, camps), travel encounters | exists (`WorldGen/Region/RegionModels.cs`, `Travel/Encounters.cs`) |
New code: `Sim/Combat/Morale/` (`MoraleModel`, `RoutState`, `Surrender`), `Sim/Combat/Aftermath/` (`DefeatOutcome`, `Robbery`, `Capture`, `Captivity`, `Ransom`, `EscapeTable`), `Sim/Hero/Succession.cs` (`SuccessionOrder`, `Estate`, `InheritanceRecord`), content `combat/morale.jsonc`, `combat/captivity.jsonc`, `combat/bandit_styles.jsonc`.

## 3. Data

### 3.1 Morale (per fighter, live only; abstract uses `morale_pm` of the unit)
`MoraleState` (12 bytes): `Courage` (0..100 x100, base per person), `Penalty` (x100, decaying), `FlankedUntilTick`, `State` (`Steady, Wavering, Routing, Fled, Surrendered, Rallied`), `RoutTick`. Base courage `= 60 + Melee family skill/5 + trait mods + veteran 5` (typical **70**, the spike value): traits are data modifiers (`brave` +15, `craven` -20, `drunk` +10 and aim -10; mod-able). Heroes and followers have `IsPlayer` exceptions below.
Content `combat/morale.jsonc` (all in morale points x100 unless said, seconds are sim-seconds):
```jsonc
{ "base": 70, "friend_down": { "points": 6, "radius_m": 10, "decay_s": 15 }, "flanked": { "points": 15, "for_s": 5 },
  "hit_taken": 2, "limb_lost": 15, "routing_neighbour_per_s": 2, "routing_cap": 20, "pain_k": 0.35, "blood_loss_k": 1.0,
  "captain": { "bonus": 10, "radius_m": 15 }, "banner": { "bonus": 8, "radius_m": 25 }, "shield_wall": 5, "winning": { "bonus": 8, "kill_ratio": 2.0, "window_s": 30 },
  "outnumbered": { "malus": 10, "ratio": 2.0, "radius_m": 12 }, "leader_down": { "points": 20, "radius_m": 30 }, "darkness_superstitious": 5,
  "rout_below": 20, "surrender_below": 10, "rally_above": 40, "rally_hold_s": 4, "flee_escape_m": 20, "flee_escape_s": 5 }
```
`morale = courage - penalty - pain_k x pain - blood_loss_k x blood_loss_% + captain + banner + shield_wall + winning - outnumbered`, clamped 0..100; `penalty` decays 0.4 per second (6 -> 0 in 15 s).

### 3.2 Outcome and captivity records
```
DefeatOutcome { Fight, Loser (person), Winner (faction or creature kind), Kind (Robbed, Captured, Killed, Mauled, Ignored, Yielded), Loot[], Time, Place }
Captive       { Person, Captor (faction), Place (site id or column id), Restraint (None, Bound, Chained, Caged), Since (game day), RansomMites, Payer (faction or household or none),
                Guards (count, shift schedule id), StashId (the captor's chest for the loot), Health care (None, Basic), NextTransferDay, EscapeAttempts, State (Held, Ransomed, Escaped, Rescued, Released, Dead) }
Ransom        { Captive, Mites, Payer, DueDay, Negotiated (bool), Paid (bool) }   // paid only by Ledger transfer from the payer's treasury (never created)
Estate        { Dead (person), Place, Items[], CoinMites (after funeral), Heir (person), Claimed (bool), Looted (bool) }
InheritanceRecord { From, To, Rule (Child, Spouse, Sibling, Parent, Companion), Coin, EstateId, DeedsPending[], ReputationPending, GameDay }   // the two pending fields are empty in Phase 4
```
Bandit styles (`combat/bandit_styles.jsonc`, 1 per band, from the band's leader trait): `robber` (default, 70%): takes goods, leaves you alive; `cruel` (15%): kills a wounded lone victim with `P = 50%`; `kidnapper` (15%): captures anyone whose ransom estimate >= 1,000 mites. Content table, not code.

## 4. Rules

### 4.1 Morale updates (2 Hz, every 10th tick, staggered by fighter id)
Each update: (a) decay `penalty` by 0.4 x 0.5 s = 0.2 pt; (b) neighbours within 10 m: new friendly deaths (events since last update) add 6 each (cap 24); (c) flanked check from the melee layer: -15 for 5 s (set `FlankedUntilTick`), re-applied while a flank blow lands; (d) each blow taken adds 2; limb loss adds 15 once; (e) routing neighbours within 8 m: -2 per second each, cap 20; (f) pain and blood terms recomputed; (g) bonuses by distance to the squad's `Captain` (a follower with `Leadership >= 20`, or the lord; plan: +10 within 15 m) and `BannerBearer` (item `banner_pole` held by a living fighter; +8 within 25 m; losing the banner is a `leader_down` -8 for those in 25 m); (h) `winning` for the side with at least 2:1 kills in the last 30 s; `outnumbered` with at least 2:1 local ratio within 12 m: -10.
Thresholds (plan):
- `morale < 35` and a recent loss: **Wavering**: stays in guard, 5% per second to step back one pace (`Fight` utility score x0.5).
- `morale < 20`: **Routing**. The fighter runs (sprint, 8 stamina/s) along the flow field away from the nearest enemy toward its camp or `Rally` point, drops a shield at `< 15` (30%), its weapon at `< 10` (30%); rear blows against it hit with x1.5 (resolver). After `flee_escape_s` = 5 s at `flee_escape_m` = 20 m from every enemy it becomes **Fled** and leaves the fight (becomes an abstract unit "returned to camp" with its wounds).
- `morale >= 40` and a captain within 20 m for 4 s: **Rallied**: returns to `Steady`, grants 10 pt temporary immunity to the routing-neighbour term for 10 s.
- `morale < 10` and **cornered**: **Surrender** (below). Cornered = enemies within 3 m in at least two of four quadrants, or no flee-path (the flow field to the rally point is blocked by an enemy within 6 m).
- The hero (`IsPlayer`) and followers the player directly controls never rout by morale. Under **watch mode** the hero's stance (watch-mode.md) uses blood and pain thresholds instead; a "flee when badly hurt" stance triggers at blood loss 25% or pain >= 70% of the knock-out threshold, or when allies within 10 m rout 2:1.
- Animals and monsters: no morale routing, except wolves and dogs (flee at 40% blood loss) and the cave beast (30%); draugr never.

### 4.2 Surrender
A surrendering fighter drops its weapon, state `Surrendered` (visible: hands-up pose, white flag marker). The winner's AI decides, per **faction policy**: armies (accept 90%, 10% `no_quarter` after a massacre flagged by the chronicle), bandits (accept: yes, they rob), beasts and monsters (never). Blows against a `Surrendered` person are crimes when a witness sees them (crime and witness system is Phase 6; Phase 4 raises the `Reputation` event stub). The hero can yield through the Q menu on self ("Yield", also an order-wheel entry) at any time; the same acceptance rule applies. A yield with a drawn weapon refused is just a fight.

### 4.3 When a fight ends and who "won"
A fight ends when one side has no active fighters (dead, down, fled, surrendered) or both walk away for 15 s. The **winner side** is the one with active fighters; if both are out, the one with more `Surrendered or Down` opponents. Each defeated person is classed: **Dead**, **Dying** (bleeding, still alive, treatable), **Knocked out** (`Unconscious`), **Surrendered**, **Fled** (gone). Outcomes apply at the end to every non-dead defeated person; for the hero, within 2 sim-seconds the screen fades to the aftermath ("You wake...").

### 4.4 Defeat outcomes by winner (Phase 4 table)
| Winner | Loser: hero or a party member (down or yielded) | Loser: others |
|---|---|---|
| **Bandits** (style `robber`) | **Robbed and left**: purse 100% taken (5% of it, at least 20 mites, left as "a pittance" if you have a wound that needs a doctor); then items in order of value density until their carry capacity (`15 + 2.5 x str kg` each, minus what they already carry) is full: weapons (all), mount, then armour pieces that **fit a bandit** (`Fit.Fits`) or are worth more than 400 mites, then goods and tools; left: under layer (linen shirt, braies), boots, one day's food, a **dagger** unless the hero resisted to the end ("seen"), the party's non-combat gear. Before leaving, each bandit with first-aid proficiency presses a wound on a bleeding victim with `P = 50%` (stream 0x452, "mercy") | Followers are robbed the same; hirelings flee; strangers not robbed unless wounded |
| Bandits (`cruel`) | As robber, but a **wounded lone** victim is killed with 50% (stream 0x452) | killed |
| Bandits (`kidnapper`, or any bandit if ransom estimate >= 1,000 mites) | **Captured** (4.5) | captured if worth ransom, else robbed |
| **Army or soldiers** (a faction's detachment) | **Taken prisoner**, equipment into the captor's stash, treated by their medic (Basic care), ransom per 4.6 | surrendered and KO'd soldiers become prisoners; the rest flee |
| **Beasts** (wolves, bear, cave beast) | Mauled: KO'd victims bleed on; a 50% chance per minute a feeding beast finishes a downed victim unless an ally drives it off. Party survivors may pull the hero out | killed |
| **Draugr** | Never captures. KO'd victims are killed on its next turn (a draugr attacks the nearest, awake or not), concussed and downed heroes are finished unless allies hold the line | killed |
| **Guards, jailers, slavers** | **Phase 6**. In Phase 4 a guard fight ends with a **fine and release** at the gate (stub outcome `Ignored`) | |
| **Locals** (a mob) | Beaten and robbed of the purse only (stub) | |
All steals pass through item rows and the `Ledger`: robbed coin moves to the bandit band's treasury (a household account), loot to their stash (`Captive.StashId` or the band camp). Nothing is created or destroyed (conservation test MC-12).
The same outcome functions run for NPC fights outside the bubble (abstract battle winners, abstract-battles-v1.md 4.6), so defeat is one table for hero and NPCs.

### 4.5 Captivity (bandits and armies only in Phase 4)
- **Where:** a bandit hideout (cave `hideout`, or camp site), or an army detachment column/camp. The hero is placed in the captor's camp at the end of the fight (the live bubble moves with the hero, the camp is generated or reused; captor positions by schedule: sentries, sleepers).
- **Restraint:** `Bound` (hands tied, armour removed, can walk), `Chained` (a cell or post, bandits with a lock), `Caged` (army camp wagon cage, nobles in a tent with 2 guards). Weapons never kept. Bound hands: cannot use items, cannot fight (unarmed kicks only), speed x0.9.
- **Guards:** `Guards` = count 2-3 for bandit hideouts (one awake at night), 2 guards per tent in army camps plus patrols; **shifts** at 6-hour spacing (Phase 3 schedule style: `ScheduleBook` profile `guard_watch`), noise and sight rules from Phase 3 (stealth uses `Skill.Stealth` vs `Perception`; darkness hides you).
- **Care and keep:** fed once a day; bandit care `None` (bleeding continues unless a fellow captive helps), army care `Basic` (wounds dressed within 1 game hour, 70% chance). Captives can talk to guards (dialogue: bribes, news).
- **Duration limits:** bandits hold at most **10 game days** (they sell or release: 40% released stripped after the ransom wait, 40% ransom handled, 20% "sold": a Phase 6 slavery stub: ends as release with a rumour "taken south"); armies hold until the campaign ends (<= 40 game days), then exchange or release.
- **Time handling:** the player plays captivity in the live bubble (normal speeds). Autopilot cannot run captivity (only `Wait`); allied NPC parties act abstractly.

### 4.6 Ransom
`RansomMites = rank_base x (1000 + wealth_pm)/1000`; `rank_base`: levy 1,300 (13 crowns), man-at-arms 4,000, knight 20,000, baron 100,000; a wanderer hero with no faction: `max(1,500, 2 x purse + 0.1 x property value)`. **The payer's treasury pays** (plan §4A.3: no money from nowhere): a faction's treasury (a faction the captive serves), else the hero's household, else "friends": companions each contribute up to 30% of their purse. If the payer cannot pay, the ransom is **renegotiated** (halved after 5 days, quartered after 10) or never paid. Payment is an `EconCommand` transfer on the Ledger at `DueDay` (3 game days for bandits after contact, 6 for armies). The hero may **offer a ransom** through dialogue with a guard (Speech check) for a faster release; bandits also take a **hostage price** from your party (the party delivers it).
Payment frees the captive with their stashed items returned for armies (`Captive.StashId` returned minus a 10% "keep" of non-weapon valuables), not for bandits (the loot is gone, they only return the clothes).

### 4.7 Escapes (the ways out in Phase 4)
| Way | Rule |
|---|---|
| **Slip the bonds / pick the lock** | Bonds: `Athletics` + `Stealth` check, difficulty 40 (rope) over 120 sim-s of work with noise; Chains/locks: `Lockpicking` skill vs lock difficulty 30 (hideout) / 50 (army cage) with a lockpick (tool quality `lockpicking`, 3 per crafted pick) over 90 sim-s. Each attempt each game hour rolls a guard notice check (stream 0x455): `P = 25% base x (1 - stealth/150) x dark(0.5)`; caught: beaten (pain 20, no wounds), restraint upgraded one step |
| **Ransomed** | 4.6 |
| **Rescued by companions** | Free followers (not captured) form a `RescueParty`: the player **takes control of one** (Tab cycles own people, a prompt "Take control of {name}?" appears at capture). The party plans a raid on the camp site; a successful rescue frees the captive when a free member reaches the captive's cell and unlocks or cuts it (live action). NPC-only rescue parties run abstractly with strength, stealth and a daily 5% chance per member |
| **During transfer** | When the captor moves camp or the army marches (every 2-6 game days), the captive travels `Bound` in the column; at night a sentry-shift gap gives an **escape window**: `P = 15% + stealth/5` per night to slip away (stream 0x454), with the guards giving chase for 60 sim-s (hunting rules, exploration spec) |
| **Work off the sentence** | Jail and servitude are Phase 6 |
Recovering gear after escaping: the stash (bandit camp chest or army pay tent) may be reached during the escape; loot is "still there" until `loot_days` = 10 for bandits (they split and sell), `ransom` end for armies. An escaped hero is wanted by the captors; `Reputation` stub event only.

### 4.8 Ironman succession by kinship
Trigger: the hero dies (blood loss, wounds, infection, killed) **in Ironman**. Non-Ironman: the death screen offers reload or "continue as someone else" (the same `SuccessionOrder`). `SuccessionOrder(heroPersonIdx)` returns candidates in this fixed order; the first eligible one is the **heir** (alive, age >= 12 years (`MinWorkAge` for plain work; hero minimum 16 preferred), on the continent, not themselves captive or paralysed from the neck):
1. **Child**, eldest first, age >= 16 (children found via `Father` or `Mother` equal to the hero; a 60-day year: `(day - BirthDay)/60`).
2. **Spouse** (`Spouse` column), if alive.
3. **Sibling**, eldest first, age >= 16 (shared `Father` or `Mother`).
4. **Child aged 12-15** (with the hero's most loyal follower as guardian; the guardian is placed with them).
5. **Parent** alive, age >= 16 (`Father`, `Mother`).
6. **Companion** (squad member: highest `loyalty_pm`, tie: earliest recruited, then lowest id) — plan "continue as a companion".
7. **Any recruited follower**, then **none**: the run ends; the world persists in the library for a new character (plan).
Ties inside a rank break by `EntityId`. The order is a pure function of the tables (test SU-1 to SU-4) and is shown in the character screen ("If you die, your daughter Isolde would continue").
Transfer (Phase 4): the heir becomes the hero (`HeroRecord.FromPerson(index)` builds attributes and skills from the `PersonTable` row; origin = "heir"); the **Estate** (the body's coin and items) is placed at the death location: coin goes to the heir minus a 5% funeral cost paid to the local church or lord account; **items** stay on the corpse unless robbed (looted items are gone); the heir inherits a quest-hint "recover {name}'s sword" for `named` weapons. Followers: kin and companions with `loyalty_pm >= 500` stay (kin 100%), the rest disperse. The chronicle records the death with its cause chain (Cause = fight id -> wound id -> attacker) and the succession. **Property deeds** are carried as `InheritanceRecord.DeedsPending` and become real in Phase 5; **reputation** (`ReputationPending`) becomes real in Phase 6.
The heir's start: placed where they were (their home or travelling); time passes to their first meaningful moment: if the hero died away from them, the world clock runs on and the heir learns of it via a rumour message, then the camera moves (a "two days later" transition, at most 3 game days).

## 5. Tick placement (20 Hz live versus abstract)
- Morale: 2 Hz in the live fight (every 10th tick), neighbour queries from the spatial buckets (10 m radius = at most 9 cells).
- Outcomes: applied at the fight-end tick. Captivity: live while the hero is in the bubble; captive NPCs and rescues run on the hourly abstract tick (5% per day rescue chance, ransom due days, transfers, release).
- Succession: instant at death (a barrier event at the tick end), the heir's place becomes the new bubble centre; promotion spread over frames (plan: <= 50 people per frame).
- Abstract battles: morale is a unit-level `morale_pm` (abstract-battles-v1.md); the same thresholds decide rout and surrender per group.

## 6. Determinism
Streams (0x400 block; keys: `FightTick`, or game second / 20 for captivity; all game-time keys): 0x450 `MoraleRally` (rally and wavering step-back rolls), 0x451 `Surrender` (acceptance by faction), 0x452 `Loot` (mercy, cruel kill, what is left), 0x453 `CaptiveEvent` (guard notice, shift swaps, guards' alertness), 0x454 `EscapeWindow` (transfer), 0x455 `LockPick`, 0x456 `RansomNegotiation`, 0x457 reserved (succession draws nothing: it is a pure function). Morale integer-only (x100). Hash: `MoraleHash` (states), `CaptiveHash`, `EstateHash` join the daily trail. **Stream key by game time:** captivity events key on `GameSec/20` windows (Phase 3 rule), combat events on `FightTick`.

## 7. UI and controls
- **Morale** shows as a small bar under the nameplate (Alt held, plan §2.7) for allies, a banner icon for the captain and the banner, and text lines in the log ("The militia breaks and runs").
- **Defeat screen** ("You wake in the mud. The bandits took 340 mites, your sword and your mare. You still have your shirt."): a list of what was lost, what is wounded, and **what you can do first** (treat wounds, track the bandits). For captivity: **Captive HUD** showing restraint, guards (awake/asleep by shift), time to ransom due, ransom amount and payer, and the escape actions in the Q menu (Work the bonds, Pick the lock, Talk to guard, Offer ransom, Wait). Keys: **Tab** (own people) to take control of a free companion; no new World keys. `F8` bug report still works.
- **Yield** is a Q-menu verb on self and an order-wheel (R) entry. In combat stance the key to cease fire is Esc (opens pause menu); we do not bind yield to a key to avoid accidental surrender.
- **Succession screen:** "You have died. {Heir} continues" with the heir's stats, the estate location, and the continuing party. In non-Ironman: reload, continue as heir, or pick another from the list.
- Warnings (plan §3.13): `EncounterOutcomes` shows odds before a fight ("probably lose: 3 bandits, 2 with bows"), and a captive's HUD says what is possible.

## 8. Tests (golden)
| Id | Check |
|---|---|
| MC-1 | Friendly fall within 10 m: -6, decays to 0 in 15.0 s (0.4/s); beyond 10 m 0 |
| MC-2 | Thresholds: courage 70, flanked (-15) and two friends down (-12) = 43 steady; plus 5 blows (-10) = 33 wavering; plus pain 30 (-10.5) = 22.5; plus a routing neighbour 2 s (-4) = 18.5 rout |
| MC-3 | Rally: morale back to 40 with a captain within 20 m for 4 s -> Rallied; without a captain no rally |
| MC-4 | Cornered surrender only below 10; a free flee path = rout instead |
| MC-5 | Beasts: wolf flees at 40% blood; draugr never routs or surrenders; cave beast 30% |
| MC-6 | Skirmish 10 v 10 (20 seeds): all finish; mean duration 25 +- 8 s; deaths 4-8, downs 3-6, routs 8-16 events (spike baseline 5.5 / 4.7 / 12.3 +-30%) |
| MC-7 | Bandit robbery: purse taken, 5% pittance when wounded, fit rule honoured, under layer left, conservation of coin and item mass (MC-12) |
| MC-8 | Mercy: 50% (+-1.5) of bleeding robbed heroes get pressure from a bandit (10,000 seeds) |
| MC-9 | Army capture: ransom base per rank, paid from the faction treasury at the due day; unpaid ransoms halve at day 5 and quarter at day 10; coin created = 0 |
| MC-10 | Escape: bonds difficulty 40 at skill 40 succeeds 50% (10,000 seeds +-1.5); lock difficulty 30 at skill 50 succeeds 70%; guard notice 25% at stealth 0 |
| MC-11 | Rescue: a free companion reaches the cell within a seeded run and frees the hero; control switch works |
| MC-12 | Conservation across a defeat: coin, items by mass and persons equal before and after |
| MC-13 | Time limits: bandits release by day 10; armies by campaign end (40 days) |
| SU-1 | Succession order: a family with a child 17, a child 14, a spouse and a sibling 20 -> heir the child 17 |
| SU-2 | Child dies -> next in order: spouse; no spouse -> sibling; no kin -> companion with highest loyalty; none -> run ends |
| SU-3 | Estate: coin minus 5% funeral cost to the heir; items stay on the corpse; robbed items are not recoverable |
| SU-4 | Non-Ironman option list equals the order; Ironman auto-continues; the world persists after the run ends |
| SU-5 | Determinism: same seed, same death tick, same heir and `EstateHash` on both OS |

## 9. Open questions (recommended defaults)
| # | Question | Default |
|---|---|---|
| C1 | How harsh is a bandit robbery for a hero at a bad moment? | Robbery as in 4.4 with mercy 50%, dagger left, boots and a shirt left; the unlucky case is death by bleed-out, which Ironman converts into succession |
| C2 | Can the hero kill the robbers back (revenge) with the stolen loot recoverable? | Yes: the band camp stash holds the loot for 10 game days (4.7) and bandit bands appear as targets in rumours; a bounty system is Phase 6 |
| C3 | Is a ransom for a wanderer with no faction `2 x purse`? | Yes, with a floor of 1,500 mites, so poverty is not a free pass and wealth is the risk |
| C4 | Should hero morale ever break the hero in direct control? | No; only in watch mode through stances |
| C5 | Ironman: can the heir be a child aged 12-15? | Yes, with a guardian companion, after kin 16+ (rank 4); reduces run-ends |
| C6 | Slavery for captured heroes (plan says slavers capture you) | Phase 6; Phase 4 releases a "sold" captive after 10 days stripped with a rumour |
| C7 | Do enemy soldiers loot the dead? | Yes (post-fight, 2 game hours later) by the same robbery rules; corpses carry items until looted or decayed (7 game days) |

## 10. Budgets
| Item | Budget |
|---|---|
| Morale update per fighter | <= 0.5 us at 2 Hz; 2,000 fighters = 1,000 updates/s = 0.5 ms/s |
| `DefeatOutcome` apply | <= 0.2 ms per fight end for 100 defeated |
| Captive abstract tick | <= 0.1 ms per hour per 100 captives |
| Save growth | `Captive` 64 bytes, `Estate` 32 bytes + item rows, `MoraleState` not saved (recomputed; saved with the fight record only mid-fight) |
| Content | `morale.jsonc` + `captivity.jsonc` + `bandit_styles.jsonc` <= 12 KB |
