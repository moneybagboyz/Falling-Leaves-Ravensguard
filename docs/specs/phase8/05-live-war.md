# Live war: the siege assault, the beach and the deck, forts and chariots in battle, and prisoners

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §3d (sieges with ladders, towers and rams; armies and war at scale), §3h (sea raids landing real men; naval combat), §3i (interrupts), §5 Phase 7's done-when 3 and 5, met only in the sim.
Carried in (phase-7 review): P7R-4, the ladder and tower orders, AE1 convoys and felling, the AI that rams, grapples and boards, the bell, the beach bubble, SG-E in live play, `PrisonerPens.Take`, the pen hooks, and the small gaps listed in 3.4.

## 1. Goals
1. Every Phase 7 war system the sim has is fought live when the hero is there: the storm of a breach, a landing on the beach, a fight on a deck, a wagon fort.
2. The AI uses every tool the player has: ladders, towers, rams, grapples, boarding, forts, chariots.
3. Battles take prisoners into the pens, and the small Phase 7 gaps close.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Siege/AssaultLayer*.cs` (ladders, towers, holes, sally, orders), `SiegeAssaults.cs` (`GameDrivesLiveAssaults` off), `SiegeWorld.Storm.cs` (the abstract storm) | **wired:** the game steps live assaults; waves; ladder and tower orders on the wheel |
| `Naval/ShipCombat.cs`, `SeaRaids.cs`, `Overboard.cs`, `Gangways.cs`; the beach fight is the abstract band fight | **new:** `Naval/CaptainBrain.cs`; the beach bubble; the bell interrupt |
| `Vehicles/WarWagons.cs`, `Chariots.cs` (sim only) | **wired:** fort walls in live battles, the sortie order, the AI, chariot archers, `cheval_de_frise` |
| `Soldiers/PrisonerPens.cs` (`Take` has no caller) | **called** from live and abstract battle results |

## 3. Rules

### 3.1 The live siege assault (LW-1)
- **When live:** the storm is fought in a battle bubble when the hero is inside the besieged place, in the host, or within 1 km. Otherwise it stays abstract.
- **Waves:** the bubble holds up to 300 fighters at once (the battle spec's assault cap). The rest wait as reinforcement waves at the host's camp; a wave enters when its side's live count falls under 60% of its share.
- **Orders on the wheel** (Shape ring, assault only): "ladders to this wall" (a stretch of at least 4 m), "tower forward", "ram the gate", "hold the breach". Defenders' wheel: "push ladders", "man the breach", "sally".
- **Pushing a ladder:** 2 defenders at the top for 6 s push it off unless 2 or more climbers are on it above half height; men on it fall (fall wounds by height). The chance a push fails is 0x850 keyed on the ladder id and tick: 300 pm for a ladder with a hook.
- **AI:** a host storms with ladders on 2 to 4 stretches, the tower on the widest firm approach, and the ram at the gate, in that order of what it has. Defenders put a third of their men on each threatened stretch, the rest at the breach.
- **Engines' goods:** the host's timber comes by AE1 convoy from its depot or is felled within 2 km (real trees removed through `ChunkDelta`, logs to the yard, `MassLedger` exact).
- **Save** mid-assault: the waves, ladders and tower position are in `sieges`.

### 3.2 The beach and the deck (LW-2)
- **The bell:** a raid within 3 km rings the village bell. It is the `BellRung` interrupt for the hero (default: pause), and the warband's call to ride.
- **The beach bubble:** if the hero or his warband arrives while raiders are ashore, the fight is a battle bubble at the landing, with the ships as carriers in the bubble.
- **The captain's brain** (`CaptainBrain`, each 2 game seconds):
  - approach from upwind when it can; the approach side is 0x858 keyed on the ship id and minute when both sides are equal;
  - **ram** when it has a `stem_post`, closing speed >= 2.5 m/s and the angle to the target's side <= 30 degrees;
  - **grapple** when within 6 m and nearly side by side (heading within 25 degrees);
  - **board** when grappled and its fighters are >= 0.8 x the enemy deck's; otherwise shoot from the castles;
  - **flee** when its fighters fall under 40% of the start, or the hull takes water faster than the pumps clear it.
- **Boarding a ship pushing off:** the hero may jump a gap of 1.5 m or less (Athletics check, 0x859 keyed on the person and tick); a miss is a man overboard.
- **Overboard** uses the Phase 7 breath rule. Mail drowns a man who cannot reach a hull or the shore.

### 3.3 Forts and chariots in battle (LW-3)
- **Fort walls:** a chained ring's armour boards are walls in the live `IFightWalls`, and count as braced polearms against a charge (as SG-E).
- **The sortie order:** opens one gap of 2 m for 20 s, then closes it if no enemy is inside 4 m.
- **AI forts:** a host with 6 or more war wagons forms a fort when enemy horse is 30% or more of the enemy's strength and within 2 km.
- **AI chariots:** charge the nearest unbraced foot on open firm ground; never into woods, ditches or stakes.
- **Chariot archers shoot** from the car: a moving shot's spread is x2 above 3 m/s.
- **`cheval_de_frise`:** a 2 m timber defence of 40 kg, carried by 2 men; horses refuse it; men cross it at a third of their speed.

### 3.4 Prisoners and the small gaps (LW-4)
- **Prisoners:** every live and abstract battle result calls `PrisonerPens.Take` for the yielded and the captured, with their rank class.
- **Pen hooks:** feeding (a ration a day from the captor's stores, as the gaol rule), sale (where the law allows), cede (in peace terms), parole broken (a chronicle entry and a standing loss).
- **`fire_arrow`:** a hit on thatch, a sail or dry timber lights it at 200 pm (wet: 20 pm), through Phase 6 fire.
- **Ships:** the sailor occupation (crews from coastal households); the anchor, ballast, bilge pump and oar port parts; harbours placed as generated blueprints in new worlds (old worlds use the Phase 7 seeding route).
- **Voyages, ferries and fishing** run in the running world (`FerryAndFishery.cs` attached in `WorldRuntime`).
- **A raid ship's non-grain cargo** leaves as an export, as its grain does (P7R-6).
- **Drill grows attributes:** +1 Strength or Toughness for each 30 drill days, to the person's cap.

### 3.5 Integer math notes
- Angles in whole degrees from the integer heading; speeds in mm/s; the jump gap in mm.
- Ladder fall wounds use the Phase 5 fall rule by mm of height.

## 4. Saves
Additive keys: `sieges` (waves left, ladders up, tower cell, live flag), `naval` (each captain's state and target), `battle` (fort wall cells, chariot cars), `soldiers` (the pen takes). No new section.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| LW-1 | Live storm | a 1,200-man host storms a breach live in waves of 300 or fewer; casualties and the town's fate as the abstract storm within ±10% over 50 seeds |
| LW-2 | Ladders | a ladder with one climber is pushed in 6 s; with 2 above half height it is not |
| LW-3 | Orders | ladder, tower, ram and hold orders on the wheel reach the assault layer with the battle spec's delay |
| LW-4 | Siege timber | a host fells trees within 2 km; trees gone from the chunk, logs in the yard; mass exact |
| LW-5 | Save mid-assault | save, load and save byte-identical during the second wave |
| LW-6 | The bell | a landing 2 km away raises `BellRung` and the warband rides |
| LW-7 | Beach bubble | the hero arriving with raiders ashore opens a bubble with the ships as carriers |
| LW-8 | Captains | a longship with a stem post rams at >= 2.5 m/s and <= 30 degrees; a weaker crew shoots rather than boards; a crew under 40% flees |
| LW-9 | Pushing off | the hero jumps 1.4 m and boards; at 1.6 m he cannot; a mailed man overboard in deep water drowns |
| LW-10 | Fort walls | a charge on a chained ring stops at the boards; the sortie gap opens for 20 s |
| LW-11 | AI fort | a host with 6 wagons forms a fort against 30% horse within 2 km |
| LW-12 | Chariots | an AI chariot never enters woods; its archer's spread doubles above 3 m/s; horses refuse a `cheval_de_frise` |
| LW-13 | Prisoners | a lost battle puts the yielded in pens with rank classes; people exact |
| LW-14 | Pen hooks | feeding, sale, cede and parole broken each move coin, food and people exactly and write an entry |
| LW-15 | Fire arrows | a dry thatch roof lights at 200 pm a hit over 1,000 shots, within ±20 pm |
| LW-16 | Running sea | a season of ferries, fishing and a voyage in a running world; a raid's cargo exported; mass exact |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-LW-1 the live siege assault | waves, orders, ladders, AI, timber, save | LW-1 to LW-5 | 1.2 |
| P8-LW-2 beach and deck | the bell, the beach bubble, captains, boarding, overboard | LW-6 to LW-9 | 1.1 |
| P8-LW-3 forts and chariots | fort walls, sortie, AI, chariot archers, `cheval_de_frise` | LW-10 to LW-12 | 0.8 |
| P8-LW-4 prisoners and gaps | pens, hooks, fire arrows, ship parts, sailors, harbours, the running sea, cargo, drill | LW-13 to LW-16 | 0.6 |

Art for forts, chariots, the `cheval_de_frise`, ladders and towers is P8-PP-1.
