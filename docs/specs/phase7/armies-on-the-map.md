# Armies on the map: standing warbands, seasonal hosts, levies, marching columns, camps, supply, foraging, desertion, winter quarters, AI campaigns

Status: design, before any Phase 7 military code, on top of b6b39e2.
Plan refs: locked decision "Armies on the map" (**hybrid**: every lord keeps a small standing warband that roams all year, Mount & Blade style, with patrols, escorts, visits to other lords, chasing bandits and border raids in war; for campaigns lords muster their levies into large seasonal hosts that march with supply, forage, and go home for the harvest and winter quarters), §1 (march speed: army column 1.3 m/s; the region levies about 520 men in peace, 1 per 10 of about 5,200 households, and about 1,040 in war, 1 per 5; field armies of 1,000-2,000 combine levies, retinues and mercenaries from across a realm and arrive by road from outside the region), §3d (marching columns: real people with a baggage train inside the bubble, an abstract record on the same road graph outside it, moved by the LODBridge; supply 1 kg of grain a man a day, 1,000 men for 15 days need 15 t, about 30 carts; foraging strips the acres and hurts the settlements; wages from the treasury chest; unpaid or starving armies desert and deserters become bandits; raiding a supply train is valid; camps as real build pieces attackable at night; recruitment; AI campaigns: value / (march days + siege days), muster after sowing, hold for the harvest unless worth the shortfall, last-known positions from scouts and rumours), §3j (purveyance, army supply, scutage, campaign finance), §4A.4 (campaigns: marching 8 km takes about 34 game hours; a season allows about one campaign and a siege; men levied during the harvest window cost their households' yield), §5 Phase 7 (done-when: "you can lead levies against an army arriving by road and see the consequences"). **Replaces** the Phase 6 realm-level war resolver (`Politics/Diplomacy.StepWar`, `diplomacy-and-ai-lords.md` Notes).
**Division with the economy writer** (Phase 7 economy specs, "army economy"): this spec owns *who is in an army, where it is, what it eats and is owed, and what it does*; the economy specs own *where food, pay and kit come from* (granaries and depots, food rents, purveyance, merchants following the army, the camp market, arms production and trade, campaign finance, scutage, loans). The seam is the provisioning interface in 4.7; its final names are the economy spec's, and the overview reconciles them.

## 1. Goals
1. Lords' standing warbands are on the map all year as real people doing visible things: patrolling their villages, escorting tax wagons and envoys, visiting other lords, chasing bandits, raiding borders in war.
2. Campaigns are seasonal and physical: summons go out after sowing, the levy is drawn from real households (and missed at harvest), contingents march to a muster, a host marches by road with a baggage train of real wagons, camps, eats 1 kg of grain a man a day, forages, is paid or not, deserts, fights, besieges, and goes home for harvest and winter.
3. Inside a live bubble a host is a column of real people on the road; outside it is a record moved on the same road graph; the LODBridge moves it in and out without a pop.
4. AI rulers plan campaigns by value / (march days + siege days) from what they know (scouts and rumours with their age), not from the truth.
5. Wars are won and lost by what armies do: Phase 6's realm-level resolver becomes a score fed by battles, sieges, raids and captures.

## 2. What exists and what is new (by file)
| File (at b6b39e2) | Today | Phase 7 |
|---|---|---|
| `Sim/Politics/Diplomacy.cs` (`StepWar`, `MakePeace`, `CaptureLord`, `Recover`), `PoliticsBook.cs` (`War` rows: score, seasons, battles, losses, gains), `WarUtility.cs`, `LordBrain.cs`, `PoliticsCtx.Strength` | the realm-level resolver: each season the stronger host gains score with a draw (0x671), hosts lose 4 or 9 percent, a decisive swing may take a lord; peace by weariness | **replaced:** `StepWar` keeps the peace test and stops rolling battles; score comes from `WarEvents` (4.11); `Strength` reads real armies; `LordBrain` gains the warband and muster actions |
| `Sim/Caravans/CaravanRoads.cs` (integer Dijkstra over `RoadEdges`, ties lowest id), `CaravanBridge.cs` (a caravan's live entities: vehicles 12 m apart, animals, driver, guards), `CaravanAmbush.cs` | caravans on the region road graph and their promotion | **reused:** routes; the column layout (4.4) extends `CaravanBridge`'s pattern |
| `Sim/Live/Bubble.cs`, `PeopleBridge.cs`, `AbstractJob.cs` | live bubbles and the LODBridge (persons <-> live actors; jobs folded back) | **extended:** `ArmyBridge` for columns and camps |
| `Sim/Continent/ContinentTick.cs`, `ContinentState` (`RegionEdge` 8 a region, `LengthKm`, `Closed` for war), `RegionLink.PendingHouseholds` | the seasonal continent tick; migrants arrive as households | **extended:** count hosts on continent edges (`abstract-war-and-calibration.md` 4.4); hosts entering a detailed region arrive by the migrants' path (4.4) |
| `Sim/Rule/RuleDues.cs` (`CallService`: a `Service` due, `ServiceMen x 1000 + ServiceDays`), `RuleBook` (`FiefRow.ServiceMen, ServiceDays`) | vassal military service as a due | **read:** a muster calls `CallService`; a vassal answers with men (the due met) or scutage (the economy spec's) |
| `Sim/Unrest/UnrestBook.cs`, `UnrestEngine.Evades`, `UnrestConcede.Trigger` | the purveyance hook (`"purveyance"`, 2 points for 30 days), evasion at rung 2 (0x682), riots at 350 | **called:** a levy's evaded draft is `Evades`; foraging on own land adds `"foraged"` (new row) |
| `Sim/Disease/DiseaseBook.ArmyCampSize`, `UnboiledWaterDays` | flux and ague onsets wait for these hooks (review phase-6-open: "the game does not feed them") | **fed** by camps (4.5) |
| `Sim/Farming/*` (household labour plans, the harvest window doy 26-35 with the 150% wage premium) | workers are a household's people aged 12+ | **read:** a person on service is not a worker (4.2) |
| `Sim/Travel/Camp.cs`, `Encounters.cs` | the hero's travel camp; road encounters (bandits as encounter casts, not bands) | **extended:** warbands and hosts as encounter rows; bandit bands become records (4.8) |
| `Sim/Trade/Hiring.cs`, `Squad.cs` | companions, squad cap `2 + Leadership / 10` | read by `soldiers-and-companies.md` (retinues) |
| **New** | | `Sim/War/`: `ArmyBook` (warbands, hosts, contingents, bands), `Warbands` (roaming tasks), `Muster` and `Levy`, `March` (routes, region and continent), `ArmyBridge` (LODBridge), `Camps`, `HostSupply` (consumption, the provisioning calls), `Forage`, `Desertion`, `WinterQuarters`, `Intel` (last-known), `CampaignBrain`, `WarEvents`, `ArmySystem` (abstract tick), `ArmyDesk` (hero commands, tag `ar:`), `ArmyView`; content `war/armies.jsonc`, `war/campaign.jsonc`, camp pieces and the `camp_*` plans; Godot: the Army tab of the Squad (P) panel, map icons |

## 3. Data (optional save section `armies`)

### 3.1 Warbands
`Warband { Id, Lord (person), Realm, Members (person ids, stable order), Horses (animal ids), Wagons (vehicle ids), Seat (settlement), Task, TaskTarget, Route (edge list), At (edge, mm along), Speed, SupplyKg (grain, fodder), ArrearsCp, Morale 0..100, LastSeen (by realm, for intel) }`.
`Task` (append only): `Idle, Patrol, Escort, Visit, ChaseBandits, Raid, Garrison, JoinHost, Return, Flee`.
**Size by holding** (`war/armies.jsonc`, the lord's paid retinue from `soldiers-and-companies.md`): a manor holder 3-6, a knight's fee 6-12, a baron 20-40, a count 60-120, a ruler's household 150-300 (each capped by his wage roll: <= 50% of the holder's receipts a season).

### 3.2 Hosts
`Host { Id, Commander (person), Realm, War, Contingents[], Baggage (vehicle ids, drivers, draught animals), Followers (camp followers: sutlers, smiths, wives, servants: persons), State, Objective, Route, At, Camp, DayRaised, OwedUntil, StockKg (by good), WageRoll, Morale 0..100, Intel }`.
`State` (append only): `Summoned, Mustering, Marching, Camped, Besieging, Engaged, Retreating, Disbanding, WinterQuarters`.
`Contingent { Kind (Warband, Levy, Vassal, Mercenary, Allied), Source (fief, company or lord), Captain, Men (person ids), OwedDays, Paid }`.

### 3.3 Levy companies and service
`LevyCompany { Fief, Men, Captain (the fief's best-skilled man-at-arms, else its reeve), Drawn (day), Home (settlement), OwedDays (10), Released }`.
Every levied man carries `ServiceRow { Person, Host, Contingent, Role, FromDay, WageCp, ArrearsCp, Deserted }` (in the `soldiers` section of `soldiers-and-companies.md`, keyed by person; `PersonTable` is unchanged).

### 3.4 Bandit bands (new records)
`BanditBand { Id, Leader, Members (person ids), Lair (a site or a camp), Region, Strength, Loot, Origin (Deserters, Outlaws, Mercenaries) }`: people of `Occupation.Bandit` (appended) who live off the roads; Phase 3 encounters draw their ambush casts from the nearest band when one exists (falling back to the Phase 3 cast when none).

### 3.5 Intel
`IntelRow { Observer (realm), About (host, warband or settlement), Strength, Pos (edge, mm), Day, Source (Scout, Rumour, Envoy, Seen) }`. Read through `RumourAge.WeightPm` (a fact older than two seasons weighs half, Phase 6). Positions also age: a host's believed position is a disc whose radius grows **4 km a game day** since the fact (its best march: 3.7 km a day, 4.3).

## 4. Rules

### 4.1 Standing warbands: the year-round life
Each lord's warband chooses a task every game day at H5 (one lord per hour of the day by fief id, as `S10Politics` does), by utility over content considerations (`war/armies.jsonc`), keeping its task until done or a better one scores 200 more:

| Task | When it scores | What it does |
|---|---|---|
| **Patrol** | default; the lord's villages not visited for 6 days | rides a loop of the fief's villages and fields (1 to 2 days); bandits met are fought; a crime reported on the way is answered as guards (Phase 6 crime) |
| **Escort** | a tax wagon, the lord's caravan, an envoy or a bride leaves the seat | travels with it (caravans spec; a warband escort lowers ambush odds by its strength in the Phase 3 table) |
| **Visit** | opinion of another lord >= 20 and none for 2 seasons, or a match or council | rides to his seat (the opinion `visit` modifier +5, decays a season) |
| **ChaseBandits** | a bandit band or a robbery rumour within 1 day's ride, <= 4 days old, and the band's known strength < 1.5x the warband | rides to the last-known place and searches (4 km radius), fights if found |
| **Raid** (war only) | an enemy village or manor within 2 days' ride; value = livestock and stores within 3 km + 20 per house burned; risk from intel | rides in, drives off livestock, takes stores (foraging 4.6 at x2 speed), burns barns and fields in ripe or cut stage (`Fire` ignition), kills or takes those who resist, rides home; a raid is a `WarEvent` |
| **Garrison** | the lord's seat threatened (an enemy host's believed disc within 2 days) | holds the seat |
| **JoinHost** | a muster called | marches to the muster |
Warbands move on the region road graph (`CaravanRoads`) at the mounted pace (trot 5 m/s for riders, so foot members slow them: a warband's speed is its slowest member's); they sleep at inns, manors or a camp. Inside a live bubble they are live people (as a caravan's guards are); their fights are Phase 4 fights or, above 60, battles.

### 4.2 Levies: who goes, and what it costs
- **The draw.** A muster's summons (4.3) asks each fief for `households / 10` men in peace or `households / 5` in war (the realm at war or declaring). Households are grouped in tens (fives) by id; each group gives **one man**: the household of the group with the most men aged 16-45 who can walk (`CanStand`, no lost leg, not blind), and from it the man aged 16-45 with the highest weapon skill, then the youngest, then lowest id. A household whose only working adult he would be is skipped in its group while another can give. Pure, no draw.
- **Evasion:** a household at unrest rung 2 (aggrieved) may hide its man (`UnrestEngine.Evades`, 0x682): the group's next household gives instead, and the fief owes one fewer man if none can.
- **Gear:** a levy comes with what his household has (the "who arms whom" rule of `soldiers-and-companies.md` 4.3).
- **Owed service:** **10 days** from the muster, fed by the lord, unpaid (the custom), then paid the levy wage (15 cp a day, `soldiers-and-companies.md` 3.1) or released.
- **The harvest:** a man on service is not one of his household's workers. Between doy 26 and 35 (the reap window) each absent worker costs his household his share of the reaping and binding (farming's labour plan simply has one fewer hand; the yield lost is what is left unreaped at the grace's end). The **expected cost** the AI weighs (4.10) is computed the same way: `absent levy men x 2.5 worker-days x the household's open acres / its workers`, priced at the grain price.
- Levied men are recorded on their household (`ServiceRow`), so a death in the field is a death in the household (`Demography.Kill` with the battle as cause), and the household's grievance rises (`"levy_death"` +4 for 60 days; content row).

### 4.3 The muster: after sowing
- **When.** Rulers plan at the season's first day (H6, a new slice `S11War` = 6,970 after `S10Politics`). A campaign for summer is called on **doy 16** (the first day after spring sowing, doy 3-14); a winter campaign only for a siege already under way (`sieges-and-engines.md` 4.7). Autumn campaigns (doy 31-45) are possible but meet harvest and winter sowing (doy 36-45) costs at full weight.
- **Summons** travel by rider from the ruler's seat (12 m/s gallop in stages, so a region's fiefs hear within a day; continent regions by the edge's length at 40 km a day).
- **Gathering:** each fief's levy assembles at its seat the day after the summons (a day's walk at most), then marches to the **muster point** (the ruler's choice: the town nearest the objective on his side, `war/campaign.jsonc`). Vassals' warbands (`ServiceMen`) and hired companies join there. The host forms when 80% of the expected men have arrived or 3 days after the first did.
- **Vassal service:** the muster calls `RuleDues.CallService`; a vassal who sends his `ServiceMen` meets the due; one who pays scutage instead (economy spec) sends none; one who does neither is in default (Phase 6: opinion -20, a grievance).

### 4.4 Marching: the road graph, columns and the LODBridge
- **Speed.** A host marches at 1.3 m/s (the plan's column speed) = 234 m a game hour, **16 hours a day** (05:00 to 21:00, camp at night): 3.7 km a game day; 8 km (one town to the next) is 34 marching hours, a little over 2 days (plan §4A.4). Rain over 4 mm/h, mud and snow on the road slow carts (vehicle rolling resistance: Phase 5), so the host walks at its baggage's pace when slower. A ford closed to wagons (Phase 5) stops the baggage.
- **Routes** are integer Dijkstra on the region road graph (`CaravanRoads`) inside a region and on continent `RegionEdge`s between regions (length, closed edges avoided). A host keeps to roads; a warband may cut across tracks.
- **The column in a live bubble** (`ArmyBridge`, the `CaravanBridge` pattern): the van (scouts, a warband), then foot **3 abreast with 1.5 m between ranks** (1,000 men: about 500 m), the commander's household, then the baggage (wagons 12 m apart: 30 carts about 360 m), the drove (cattle, remounts), the followers, a rear guard. Promotion <= 50 people a frame, entering at the bubble's edge (plan §2.3: "an arriving army appears as a column marching into view"); demotion folds each person's position back as the host's `At` and their states into the host.
- **Between bubbles** the host is the record on the same graph, moved hourly at H1 (`SystemOrder` new `Armies` = 1,100, after caravans). Live and abstract use the same speed numbers; a host's place after leaving a bubble is where its column's head was.
- **Hosts from outside the region** come by the migrants' path: a count host on a continent edge (AW 4.4) entering a detailed region becomes people at the border gate: persons made for its men in the source region's culture (names, kin as single men of households abroad, `PersonStatus.Alive`, `Occupation.Soldier`), keyed on host id and index (stream 0x7A3), with kit from the count host's quality class; when they leave, they go back to counts (`Departed`) and the dead stay dead here. Their number is exactly the count host's (no men from nothing).
- **Encounters on the road:** the hero meeting a column or warband is a Phase 3 encounter row (`war_column`, `war_warband`: hail, join, trade with the followers, flee) and an AI host meeting an enemy group within 2 km chooses battle or avoidance (4.10). Caught **in column** with an enemy within 400 m, a host deploys (battle spec 4.3: about 170 s for 1,000 men) or is ambushed in column.

### 4.5 Camps
- A host camps at 21:00 within 300 m of water on the route (a stream, river or well; else it carries water at 3 L a man a day in casks, from the baggage) and strikes camp at 05:00.
- **In a live bubble** the camp is real build pieces placed by the soldiers' jobs from the baggage: `tent_soldier` (6 men, 9 kg of canvas and poles), `tent_pavilion` (the commander), `campfire`, `horse_line`, `wagon_park` (the wagons in a ring), `latrine`, and for a stay of 2+ days a `palisade` and `ditch_shallow` ring (Phase 5 pieces; plans `camp_host_200`, `camp_host_1000` in the plan library). Pitching takes `0.25 worker-hours a tent`; a 1,000-man camp is up within an hour of arriving.
- **Outside** it is the host's `Camp { Cell, Pieces count, Palisade, Sentries, Since }` record; promoted, the plan is placed from the record (pure, by host id).
- **Sentries:** 1 in 25 men by night, rotated in 3 watches; the rest sleep (`FAct` new `Asleep`: a sleeping fighter wakes when hit, at an alarm within 30 m, or on the Phase 4 wake jitter 0x424, 5-20 s). A **night attack** on a camp is a battle (battle spec) in darkness (Phase 3 light) with sleepers.
- **Disease:** `DiseaseBook.ArmyCampSize` is fed with the camp's men (flux onset), `UnboiledWaterDays` with the days it drew from a river below another camp or a town; a camp of 2+ days without a latrine doubles flux.

### 4.6 Foraging
- **Who:** a forage party of up to 1 in 10 of the host (cavalry and light men first), out by day within **3 km** of the camp, back by dusk.
- **What:** grain and flour from stores (households' barns and stores, settlement granaries, manors, mills), standing crops in stage `Ripe` or `Cut` (a forager reaps and threshes roughly), hay, and livestock (driven to the drove). Rates a forager a day: **300 kg** from stores, **150 kg** from standing crops (and that acre's yield goes to 0: field flag `Foraged`), 1 head of cattle or 4 sheep from pasture. All mass moves through `MassLedger` into the host's stock (no new mass); livestock become the host's animal rows.
- **On enemy land** it is foraging (a `WarEvent` raid point for each village stripped); **on own land** it is purveyance by force, legal only with the ruler's warrant (the economy spec's purveyance), otherwise a crime (`theft` by the soldiers, Phase 6). Either way each household robbed gets a grievance (`"foraged"`, new row: +4 for 60 days; on own land also the `"purveyance"` row) and loses its food, so its next days meet the Phase 2 famine checks; a village stripped of more than half its stores writes one chronicle entry (section 8).
- **Strip limit:** a host that forages the same 3 km disc for 3 days finds 1/3 the rate (stores empty), so a stationary host must be supplied or move (the reason sieges need supply).

### 4.7 Supply: what a host consumes (and the provisioning interface)
- **Rations** (`war/armies.jsonc`): **1 kg of grain a man a day** (plan), plus 0.1 kg of salt meat, cheese or pulses when the stock has them (morale +5 when fed both); horses: a war horse 10 kg of hay and 2 kg of oats a day, a pack horse or riding horse 8 kg of hay, an ox 12 kg of fodder; firewood 1 kg a man a day (3 kg in winter, `sieges-and-engines.md` 4.7). Water as 4.5. Camp followers eat as people (they buy from the host's sutlers or the stock).
- **Eating** happens at H5 each day from the host's stock (baggage contents: real goods in real vehicles' cargo). Short: rations are cut pro rata; a man on under 50% of his ration for a day is **hungry** (morale -5, desertion 4.9); 3 hungry days in a row: -10 strength (Phase 3 nutrition).
- **Baggage** is real vehicles from `VehicleTable` (carts 500 kg, ox wagons 1 t from the Phase 3/5 templates), their drivers (persons) and draught animals (who eat): 1,000 men for 15 days need 15 t of grain, about 30 carts (plan). Raiding the train takes its cargo (a `WarEvent`, and the host starves sooner).
- **The provisioning interface** (owned by the economy specs; the calls this spec makes):

| Call (working names) | When | What the economy side does |
|---|---|---|
| `Provision.Plan(host, days)` | at muster and each day | returns the sources it will use (depot, granary, food rents, purveyance warrant, sutlers) and the kg and days |
| `Provision.Draw(host, good, kg, node)` | when the baggage is at a depot, a friendly town or the seat | moves goods into the baggage by transfers it makes (purchase, food rent, purveyance); returns kg delivered and the chronicle cause |
| `Provision.Sutlers(host)` | daily while followers exist | the camp market: sutlers sell to soldiers and followers from their own carts, buying where they pass |
| `Provision.PayRoll(host, rows)` | each 6-day working week and at release | pays each `ServiceRow` from the treasury chest or the lord's purse (`TransferReason` wages); returns per row paid and arrears |
| `Provision.Kit(armoury, request)` | at muster and refit | issues items from an armoury (`soldiers-and-companies.md` 4.2 asks; the armoury is the economy spec's building) |
This spec owns the **consumption**, the **foraging** (4.6) and the **effects of shortfalls** (hunger, arrears, desertion); it never makes or prices goods.

### 4.8 Deserters and bandits
- **Desertion** is rolled daily per man at H5 (stream 0x7A0): `pm = 2 x (arrears days - 3, if > 0) + 10 x (hungry today) + 30 (a levy in doy 26-35) + 5 x (days beyond owed service, levies) + 10 (host morale < 30) + 20 (a defeat in the last 3 days) - 5 x (Drill / 200)`, clamped 0..200 permille a day; retinues halve it; a mercenary company decides as a company (`soldiers-and-companies.md` 4.6).
- A deserter takes his kit and walks home if home is within 2 days and he has 50 cp or food; else, and in 500 pm of cases when he has neither, he joins or forms a **bandit band** (`BanditBand` of deserters, 3.4) near the route. Bandit bands raid travellers and farmsteads and are what warbands chase (4.1). They are people: they eat (foraging at x1 on any land), are caught and judged (Phase 6), and their numbers fall by death, capture and drift back home (each member 2% a season goes home if his household lives).
- **A deserter** is a crime against his lord (`desertion`, Phase 6 crime table row, content), with a bounty only for retinues who ran with the lord's kit.

### 4.9 After a battle, a raid or a siege on the map
- A battle (live: `battle-bubble-and-formations.md`; abstract: `abstract-war-and-calibration.md`) folds back into the host: dead and captured men out, the wounded into the baggage (wagons) or left at the nearest town; horses and loot to the field's holder (the Phase 4 loot rule; kit recycled by the economy spec's repair and refit); morale: the winner +15, the loser -25.
- A host that has lost more than half of its men in a battle **retreats** to its muster point or the nearest friendly walled town; below a quarter it disbands where it stands (levies walk home).

### 4.10 AI campaigns (`CampaignBrain`)
- **When:** at `S11War` on doy 16 (and re-planned each day at H5 for a host in the field when intel changes by more than 25% or a target falls).
- **Candidates:** enemy settlements and castles within the realm's reach (march days <= 8), enemy hosts in the field, and fiefs a claim names.
- **Score** (`war/campaign.jsonc`), all integers:
  `value = target's yearly receipts x 4 (a fief taken) or x 1 (a sack) + 30% of value if a claim + 2,000 cp x captured lord expected`
  `days = march days (route length / 3.7 km) + siege days (0 open, 2 palisade, 8 stone walls + garrison / 100; starvation days from the known stock when no engines)`
  `p_win = Lanchester expectation from known strengths (ratio^2 / (1 + ratio^2) in permille)`
  `cost = supply days x men x ration price + wages + the harvest shortfall (4.2) if the campaign overlaps doy 26-35`
  `score = value x p_win / 1000 / days - cost / days`; campaign if the best score > 0 and the war utility (Phase 6) still clears the ruler's threshold.
- **Hold or disband for the harvest:** at doy 25 a ruler keeps the host in the field only if the remaining campaign's expected score exceeds the harvest shortfall of keeping the levies; otherwise levies go home (retinues and mercenaries may stay). Each choice is a chronicle entry with its numbers as cause.
- **Last-known, not the truth:** every input about the enemy is the observer's `IntelRow` (strength, place disc, stock), weighted by `RumourAge.WeightPm`. **Scouts:** each host sends 2 light horse 10 km ahead and to its flanks; a scout within 2 km of an enemy group by day (500 m at night) makes a `Seen` fact and rides back (if he is not caught); rumours from travellers and refugees carry facts at their age; envoys (Phase 6) carry strengths. A host the enemy has never seen is not in their plan. The hero is seen only this way.
- **Battle or avoid:** two hostile hosts whose discs meet within 2 km: the side whose known ratio is under 0.7 avoids (retreats to its nearest walled place), else offers battle where it stands (prefers higher ground within 1 km, a river or wood on a flank). Both offering: battle next morning at 08:00.

### 4.11 War score from what armies do (replacing the Phase 6 resolver)
`Diplomacy.StepWar` stops computing a battle; each season it reads the war's `WarEvents` since last season:
| Event | Score to the doer (attacker's view, -100..100) |
|---|---|
| a field battle won | `+ the loser's men lost as a percent of his men in the battle / 2` (max +15) |
| a walled place taken (storm or surrender) | +20 (a castle or town), +8 (a palisade) |
| a siege lifted by relief | +8 to the relieving side |
| a raid (village stripped or burned) | +2 (max +10 a season) |
| a lord captured | +10 (and the Phase 6 ransom or cede) |
| a host destroyed (disbanded under a quarter) | +12 |
Weariness keeps Phase 6's formula with **real dead** (`WarUtility.Weariness(seasons, dead from the household deaths, treasury seasons)`), plus +10 for each harvest a levy host was kept in the field. The peace test (`Sues`) and terms (`TermsFor`) are unchanged. `PoliticsCtx.Strength(realm)` becomes the sum of its hosts' and warbands' men, levy capacity and hired companies, as **known** by the asking realm.

### 4.12 Winter quarters
From doy 46 levies are released (home by the shortest road); a host keeps only a siege (`sieges-and-engines.md` 4.7). Warbands winter at their lord's seat (garrison). A host's mercenaries in winter are paid half wage in quarters (`soldiers-and-companies.md`) or released.

### 4.13 The hero's armies
As a lord the hero has his warband (his retinue, the Squad panel's Army tab) and can: set its task (patrol, escort, garrison, raid in war, chase a band), call his levy (for his liege's muster or his own war: the same draw), lead a host (he is its commander: he sets the objective and the route on the map, the host marches; he may ride ahead), set the camp, forage (and see the grievances it costs), pay or delay wages, release levies. The road from Phase 6 (contracts, war service) leads here: war-service contracts are met by serving in a host.

## 5. Tick placement
| Where | What |
|---|---|
| H1 hourly (`SystemOrder.Armies` = 1,100, new) | hosts, warbands and bands move on the graph; scouts; encounters between groups |
| H4 daily slice | each host's foragers (one host an hour) |
| H5 hour 0 | rations eaten, hunger; desertion rolls; pay weeks; warband tasks (one lord an hour of the day); intel ageing |
| H6 `S11War` (new, 6,970) | rulers' campaigns, musters and summons; the war score's season (`StepWar`); winter quarters at doy 46 |
| Live 20 Hz | columns and camps inside bubbles (`ArmyBridge`); battles (battle spec) |
| Barrier | the hero's army commands; promotion and demotion of columns |

## 6. RNG streams (0x7A0..0x7AF)
| Stream | Use | Key |
|---|---|---|
| 0x7A0 | desertion | person, day |
| 0x7A1 | deserter to bandit; band drift home | person, day / band, season |
| 0x7A2 | warband task ties; raid target ties | warband, day |
| 0x7A3 | persons made for a host arriving from outside (names, ages, skills) | host, index |
| 0x7A4 | a scout caught or not | scout, day |
| 0x7A5 | campaign target ties | realm, season |
| 0x7A6 | camp pitch jitter (tent placement within the plan) | host, camp |
| 0x7A7..0x7AF | free |
Reused: 0x682 (levy evasion, `UnrestEngine.Evades`), 0x424 (wake), Phase 3 encounter streams for road encounters.

## 7. Saves
Optional section **`armies`** (absent until a warband or host exists, so an older save keeps its bytes until the first season of Phase 7 seeds warbands): warbands, hosts, contingents, levy companies, camps, bandit bands, intel, war events. **Additive keys:** politics `War` rows (+`Events` list, +`HarvestsKept`); fields (`Foraged` flag, an append to the `FieldFlags` byte); vehicles (a `Host` owner kind); animals (a host as owner). `Occupation` +`Soldier, Bandit` (append only). **The Phase 7 seeding step** (added to the Phase 6 seeding pass's list) makes each lord's warband from his household and hired men at the first Phase 7 season of a loaded save or at a new game, keyed on seed and ids. v8 stays. Round trip: AM-19.

## 8. Chronicle
| Type | Key | Params | Cause |
|---|---|---|---|
| `muster_called` | `ch_am_muster` | ruler, objective place, men expected | the war or its declaration (`Campaign`, new `CauseKind`) |
| `levy_drawn` | `ch_am_levy` | fief, men, evaded | the muster |
| `host_marched` | `ch_am_marched` | commander, from, to, men | the muster |
| `village_foraged` | `ch_am_foraged` | host's commander, settlement, kg taken, households | the host's march (`Campaign`) |
| `warband_raid` | `ch_am_raid` | lord, settlement, burned, taken | the war (`Raid`) |
| `host_held_for_harvest` / `host_sent_home` | `ch_am_held`, `ch_am_home` | ruler, men, shortfall, expected value | the campaign; note keys `ch_note_am_harvest_cost`, `ch_note_am_worth_it` |
| `desertion_wave` | `ch_am_desertion` | host, men, reason key (`ch_note_am_unpaid`, `_hungry`, `_harvest`, `_defeat`) | the host (`Desertion`, new `CauseKind`) |
| `bandits_formed` | `ch_am_bandits` | band leader, men, place | the desertion entry |
| `host_disbanded` | `ch_am_disbanded` | commander, men left | the battle or the season |
`CauseKind` appended: `Campaign`, `Desertion` (overview dedupes with the economy specs' additions).

## 9. UI and keys (960 x 540)
- **The Squad (P) panel's Army tab** (shown when the hero has a warband or commands a host): the warband (task, members, wages, morale, supply days), the host (contingents with men, owed days, arrears; stock in days by good; baggage; followers; desertion last 6 days with reasons), buttons Task, Call levy, Release levies, Pay now, Forage (on / off / own land with warrant), Camp here, March to... (opens the map to pick). Mirrors the Ledger's keys (arrows, Tab, Enter, `/`, Esc); no new key.
- **World map (M):** hosts as banner icons on roads with strength band (known, not true), last-seen ghosts with age, march routes of your hosts, forage discs, raid smoke markers; warbands as small pennants.
- **Toasts:** "Your levy is drawn: 52 men; 3 households hid theirs", "The host is hungry (2 days of grain)".
New verbs (Q menu, no keys): Join the host, Hail the column, Trade with the sutlers, Report to the commander, Desert.

## 10. Golden tests
| id | Case | Expected |
|---|---|---|
| AM-1 | Warband life | a baron's warband over 30 days of peace: patrols each village at least every 6 days, escorts the tax wagon, visits a friendly lord; every task has a cause |
| AM-2 | Chase bandits | a robbery rumour 1 day old 5 km away: the warband rides, searches, fights the band; a band stronger than 1.5x is not chased |
| AM-3 | Levy draw | a fief of 312 households at war: 62 men, one per group of five, the rule's man in each; a group with only one working adult gives from its next household |
| AM-4 | Evasion | 4 households at rung 2: `Evades` hides men; the fief owes fewer; grievances unchanged by the draw itself |
| AM-5 | Harvest cost | 50 levy men held through doy 26-35: their households' unreaped acres equal the farming spec's labour loss; `host_held_for_harvest` names the shortfall in cp |
| AM-6 | Muster timing | summons on doy 16; levies at the seat on 17; host formed at 80% or day +3 |
| AM-7 | March speed | 8 km by road: 34 marching hours (+-1), over 2 game days with night camps; with mud the baggage's speed |
| AM-8 | **Column in and out** | a 1,000-man host promoted at <= 50 a frame at the bubble edge (column 500 m + baggage); demoted and promoted again 10 km on: position and men conserved, no person duplicated |
| AM-9 | Host from outside | a count host of 800 entering at a gate: 800 persons made by 0x7A3 (same names on rerun); leaving with 610: 610 back to counts, 190 dead here with causes |
| AM-10 | Camp | a 1,000-man camp pitched from the plan in under 1 game hour; sentries 40; a night attack wakes sleepers by the alarm rule |
| AM-11 | Rations | 1,000 men and 120 horses eat 1,000 kg of grain, 1,200 kg of hay and 240 kg of oats a day; 15 days need 15 t (30 carts) |
| AM-12 | Foraging | 100 foragers on enemy land: 300 kg a man a day from stores, then 1/3 after 3 days on the same disc; mass conserved; each robbed household gets `foraged` |
| AM-13 | Desertion | arrears 8 days and hungry: `2 x 5 + 10 = 20` pm a day; a levy at harvest +30; retinue halved; deserters home or to a band by the rule |
| AM-14 | Bandits from deserters | 30 deserters far from home form a band; a warband chases it; the band's men are people with households abroad or none |
| AM-15 | Last-known | an enemy host seen 3 days ago: its disc 12 km wide; the AI plans against the disc; a fresh scout report changes the plan within a day |
| AM-16 | Campaign scoring | a stone-walled town 6 days away with receipts R and a palisaded village 2 days away: the scores per formula; the AI picks the higher |
| AM-17 | Score from events | a season with a battle won (loser lost 30% of 1,000), a palisade taken and 3 raids: score +15 + 8 + 6 = +29; peace by Phase 6's test |
| AM-18 | Winter | doy 46: levies released and walk home; a host besieging stays; warbands garrison |
| AM-19 | Save | a world with 4 warbands, a host on the march with a camp and a bandit band: save, load, save byte-identical; `ArmyHash` equal on Linux and Windows |
| AM-20 | **Done-when (part)** | the hero leads his levy (60 men) and warband against an enemy host of 400 arriving by road: the column seen by scouts, the battle, the consequences on the map (foraged villages, dead in households, the war score, desertion after the defeat) all with chronicle causes |

## 11. Budgets
Abstract hour: hosts and warbands moving (<= 40 groups in a region) <= 1 ms; foragers (one host an hour) <= 0.5 ms; H5: rations, desertion for 2,000 men <= 2 ms; `S11War` campaign planning <= 5 ms a realm (spread one realm an hour); intel <= 0.2 ms. Live: columns use the ordinary bubble budget (a 1,000-man column in a normal bubble is 1,000 people: the 8 ms people tick must hold with walking-only actors, <= 3 ms for them). Save: <= 300 KB for a region at war (2,000 service rows x 40 B, warbands, intel); memory <= 20 MB.

## 12. Open questions (recommended defaults)
1. **Levy service owed:** 10 days unpaid (the summer window), then paid 15 cp a day or released. (For the user: historically 40 days, scaled to the 60-day year.)
2. **Who is drawn:** the fittest man of the most-manned household of each group, never a household's only worker if another can give. Default as written.
3. **Forage on own land without a warrant is theft** (a crime); with the ruler's warrant it is purveyance (economy spec). Default as written.
4. **Hosts from outside are made as people at the gate and unmade when they leave.** Default yes (the migrants' path; counts conserved exactly).
5. **Night march:** none (16 hours of marching a day). Default as written.

## 13. Build parts and estimate
| Part | Content | Gate | Months |
|---|---|---|---|
| AM-A army book and warbands | `ArmyBook`, warbands seeded, tasks and roaming, bandit bands, the encounter rows, the `armies` section | AM-1, AM-2, AM-14, AM-19 | 1.2 |
| AM-B levies and the muster | the draw, evasion, owed service, the harvest link, summons, gathering, vassal service, release | AM-3 to AM-6, AM-18 | 1.0 |
| AM-C marching and camps | routes, the hourly mover, `ArmyBridge` columns, hosts from outside, camps (pieces, plans, sentries, sleepers), disease hooks | AM-7 to AM-10 | 1.3 |
| AM-D supply and desertion | rations, baggage, the provisioning calls (with the economy writer's stubs until their parts land), foraging, hunger, desertion, after-battle fold-back, retreat and disbanding | AM-11 to AM-13 | 1.0 |
| AM-E campaigns and the war score | intel and scouts, `CampaignBrain`, battle or avoid, hold for the harvest, `WarEvents`, `StepWar` replaced, the Army tab and map icons | AM-15 to AM-17, AM-20 | 1.3 |
| **Total** | | | **5.8 months of code** |

## Notes (P7-AM-A, built)
- **What exists:** the standing warbands of 4.1 with their tasks, the `armies` section and the bandit bands of 3.4. `ArmyBook` holds warbands (a lord, his men by person id, a task with its target, stops and cause, a place on the road graph: a settlement or a leg `From` to `To` and `DistMm` along the same `CaravanRoads` path a caravan walks), errands (the season's tax wagon), bands, the news the lords have heard and the log of tasks. All state is plain integers kept sorted, so equal worlds give equal bytes and `ArmyHash` (FNV, integers only) is the same on every machine. Three abstract systems run it: the hourly step, the day's pay at hour 18 and the season hour at `S11War`.
- **Worked example (fief 0, a baron's warband of 10 men, seed 1234, the first spring):** day 0 it is raised from the lord's household guards and the idle men of the fief and hired at 15 cp a day; it patrols the town and the two farmsteads (the town every four days, a farmstead every ten), on day 0 (with a friendly lord's opinion of 30 set up) rides 3.4 km to his seat and stays the day (his opinion of the baron +5 for the visit), on day 1 escorts the season's tax wagon to the realm's seat and comes home, and so on: a year of it is 12 patrols (the town every four days, each farmstead every ten), 4 wagons escorted (one a season, the road from the lord's seat to his realm's seat being short enough), 2 spells of garrison in the winter and a visit whenever a friend was owed one; it ends the year with 10 men, no arrears and morale 100. Every line of the log has its cause key, the wages are 150 mites a man a day from the lord's purse into the men's households (the guards of the household are on the manor's payroll and cost nothing more), and the coin audit holds.
- **Deviations:** (1) **`Sim/Armies/`, not `Sim/War/`**: a namespace `Ravensguard.Sim.War` hides `Politics.War` in every file below `Ravensguard.Sim`. (2) **Seeding is the season system's, not a `Phase7Seeding` step**: the registered pass runs at load, and a warband raised then would write an `armies` section into an older save's bytes; the lord thinks at his own hour of the first season day instead (a new game's first spring, an older save's next one), keyed on the seed and the ids. (3) **Warbands are small where the region has few men to hire**: the region's working-age men are nearly all heads of houses or on a staff, so a baron's target of 28 is 10 men here (100 men in 46 warbands at first, 179 in 47 after a year); nobody is made to fill the table. (4) **The spec's `Warband` fields `Horses`, `Wagons`, `SupplyKg` and `LastSeen`** are not kept: a warband rides by a flag (the trot), its loot is in a real baggage store, and intel is AM-E's. (5) **The tax wagon is an errand record with no cargo**; its escort is skipped when the road is over 12 km (the lord's villages would go unridden for days), and the wagon goes with its carters. (6) **A fight with bandits is the Phase 4 abstract battle with the `soldier` or `guard` kit row**, not the men's own items. (7) **A new stream 0x7A7** (a band robbing), from the free block. (8) **Patrol thresholds are 4 and 10 days**, not the 6 of the spec's trigger, so that the gate's "every village within 6 days" holds with the loop's own days.
- **Left out for later parts:** the live promotion of a warband (`ArmyBridge`, AM-C), the hero's `ar:` desk and the Army tab (AM-E), the `foraged` grievance, burning in a raid, prisoners and judgement of the bands' men, `Return` and `Flee` as tasks (the way home is the task's last stop), `JoinHost` (AM-B), the war score from raids (AM-E), `HostSupply.Kit`/`ReturnKit` for hosts, `ArmyHash` in `phase7_trail` (the close-out scripts it).

## Notes (P7-AM-B, built)
- **What exists:** the muster of 4.2 and 4.3 as `ArmyHost` records (state Summoned, Mustering, Camped, Besieging, Disbanding) with a `LevyCompany` per fief and the warbands that joined, saved as keys 10 and 11 of the `armies` section. Worked example (seed 1234, first summer, a realm of 46 fiefs at war): the ruler calls on doy 16; all fiefs hear within the day; on doy 17 at hour 18 fief 1 (420 households) draws its 84 men village by village (one a household, the only worker of a household last, no man of a warband), 46 middling, 36 poor and 2 well-off households' kit moved to them; 942 levies in 46 companies walk 0 to 11 days to the point; the first column arrives on day +1 and the host forms on day +4 (the 3-day rule); 30 warbands (83 men) ride in. Held through day 25 only if the campaign is worth more than the priced loss; otherwise released that day and back on their fields within a week.
- **Deviations:** (1) the draw is EA3d's village draw (overview 10d), not groups of ten or five; the quota is still `LevyQuota`. (2) Columns are an abstract walk by road length (no men on the road until AM-C). (3) Joined warbands stay on their lords' wages and are not in the host's ration roster (AM-D). (4) A call carries a `Worth` (mites); the AI's call has none, so the levy goes home at the harvest. (5) Households here hold goods, not item rows, so most levies bring no gear until households keep made pieces; gaps are shown. (6) The shortfall uses `HostSupply.HarvestRisk` (last year's harvest), so it is 0 in a world's first year. (7) Evasion has its own draw (0x682, draw 2).
- **Left out:** marching, camps, hunger, desertion, `levy_death`, the hero's desk, the campaign brain.

## Notes (P7-AM-C, built)
- **What exists:** hosts on the road (4.4) and their camps (4.5), in `Sim/Armies/` (`MarchTypes`, `ArmyWorld.March`, `ArmyWorld.Outside`, `CampPlan`, `CampAlarm`, `ArmyBridge`). A host is the supply book's `HostAccount` (AE1: roster by role, purse, baggage, place, mode); the march adds `HostMarch` (in `ArmyBook.Marches`, the `armies` section's keys 10 and on): a route of stops, the distance walked, the camp. The mover is `ArmyWorld.MarchHour`, run by `ArmySystem` at H1 after the warbands; `BeginMarch`, `Halt`, `EndMarch`, `EnterRegion`, `LeaveRegion`, `NightAttack` are the calls the muster (AM-B), the desertion (AM-D) and the campaign brain (AM-E) will make. Nothing is made or destroyed: tents and poles are the baggage's carried kit (pitched and struck), no coin or goods move, the people of a host from outside are the only things made (and given back as `Departed` rows).
- **Worked example (an 11 km march of a 300-man host with 10 wagons and 40 animals, 05:00 of day 1, dry):** day 1 it walks 16 hours at 234 m an hour, 3,744 m; at 21:00 it pitches camp 1 (50 tents, 17 fires, 6 latrines, 3 horse lines and the pavilion: 77 pieces, pitched in 237 s by the 288 men not on watch) with 12 sentries on the first watch (one man in 25, a different group each watch and night); at 05:00 on day 2 it strikes camp and goes on, another 3,744 m, camp 2; on day 3 it walks 3,512 m in 15 hours and reaches the town at 21:00 with 12 on watch (`host_arrived`, 3 days and 35 marching hours). At 23:14 an enemy man comes within 40 m of the sentry who stands at the edge: the sentry shouts, the sleepers within 30 m of him stir 6 s later, each man who wakes shouts in his turn, and the last of the 288 sleepers is on his feet 25 s after the alarm (`camp_attacked`). In mud the same road is 76 hours with wagons (the baggage's 450 permille) and 57 without, on a wet road 49.
- **Gate figures:** AM-7: 8 km is 34.2 hours at the column's pace, 35 marching hours here (the 35th finishes it), arrival on the third day with two night camps; in mud with 30 wagons 76 hours. AM-8: a thousand men and 30 wagons are 1,030 live parts that come into a bubble once each at 150 m (most 9 a frame at 60 game seconds a frame, 50 when a bubble opens inside the column), leave once each after 5 real seconds beyond 200 m, and again at a second bubble 10 km on: 2,060 promotions and 2,060 demotions, the same thousand persons throughout. AM-9: 800 persons made on 0x7A3 for a count host at a gate (the same names and ids on a rerun), 190 killed by wounds with their causes, 610 given back as `Departed` rows and counted by the roster. AM-10: 1,000 men pitch in 224 s (60 worker-hours), 40 sentries a watch, the alarm as above.
- **Budget:** the hourly mover for eight hosts on the road is 0.036 ms an hour (the spec's 1 ms is for 40 groups, warbands included). The bridge's frame builds the parts of a host near a bubble (1,030 parts: well under a millisecond) and skips every host that is not near one.
- **Deviations:** (1) **Constants, not content**: `MarchConfig` holds the march and camp numbers (the pattern of `SupplyConfig`); a `war/campaign.jsonc` row can replace it when AM-E needs the numbers tuned. (2) **A camp is where the host stands at 21:00**, not the nearest water on the route (`WaterCarried` and `BelowTown` say how it drank, and feed the disease rule); no search. (3) **Camp pieces are a plan and a count, not construction-world cells**: the bridge hands the placer the pieces up so far (by the hands and the time) and their offsets; placing them as real `BuiltCell`s is the street view's job when it draws a camp (the six pieces exist in `build/pieces.jsonc` with their recipes of materials, but nothing is drawn from the baggage's stores). (4) **At nightfall the column becomes the camp at once** (the men's places jump from the column to their tents); a bubble that holds the column at 21:00 sees the sleepers appear in their tents. (5) **A host's records of men are the supply book's**: levies are `OnCampaign` rows, the men from outside are `HostMarch.Outsiders` (person id to role), not `OnCampaign` rows, so AM-D's desertion reads both lists (`ArmyWorld.ColumnPersons`). (6) **Tolls and closed fords** are not charged or tested on a host's route; the route is the caravans' road graph without its toll marks. (7) **A key on the `armies` section** moved `phase6_trail` (both seeds re-pinned, see the overview).
- **Left out for later parts:** the muster that makes a host (AM-B: `HostSupply.Muster` is as AE1 left it), rations, hunger and desertion on the road (AM-D), intel, scouts, ambush of a column, battle or avoid and the campaign brain (AM-E), the hero's commands and the Army tab, the Godot drawing of columns, camps and the night, picking camp ground by water, tolls and fords, the night's own light for sentries' sight (60 m flat), `BattleBubble.Open` starting sleepers (the hook is `CampAlarm.WakeFighters`).

