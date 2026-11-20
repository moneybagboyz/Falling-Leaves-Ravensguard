# Contracts v2 and Rumours v2: work generated from need with escrow and deadlines, tournaments as fair events, slander, legends and the rumour's age

Plan refs: §3.10 (contracts are this game's quests: escort, guard, hunt, deliver, collect a debt, take a fief), §4A.4 (contracts generated from real deficits and chronicle events; pay held in escrow from the issuer's purse; deadlines in game days; completion verified from events; failure has consequences; the old quests were never completed, failed or expired), §5 Phase 6 (contracts v2 generated from need, paid from escrow, with game-day deadlines; Rumours v2; about 40 contract types), §3.11 (contracts and ambitions can be pinned to the map: a soft marker), §4A.3 (tournaments: blunted weapons, yield at pain knock-out, the purse from the town treasury), Phase 3 dialogue Q1 (slander with reputation in Phase 6). Carried here: Phase 3 AP-8 (`offer_big`) and AP-15, the dialogue's legends fallback, mood in barks, the History link and the Journal pin; Phase 4 S8 (the tournament purse on the game's word) and the jousting approach angle; watch's autopilot road encounters not using the odds rule; Phase 5's construction market (NPCs commissioning builders, the hero and builders bidding, a builder's expected days, the builder's cart as a vehicle row), day-labour posts from sites, farms and haulage, and the `Construct` job on the board; landless idle adults (Phase 3 growth item 5).

## 1. Purpose
Phase 3's job board posts paid odd jobs and Phase 5 posts construction contracts; neither knows why anyone needs anything. Contracts v2 are generated from the world's real needs (a deficit, a crime, a debt, a bandit band, a lord's plan), escrowed from a real purse, given a deadline in game days, checked against what actually happened, and failed with consequences. Rumours v2 lets people lie, makes reputation and AI knowledge read rumours with their age, and gives dialogue a past to talk about.

## 2. Dependencies
| Needs | From |
|---|---|
| `JobTable` (escrow in a `Chest` purse, phases, claims), `OddJobKind`, Phase 5 `ConstructionWorld.Contracts`, `ContractPanel` | Phase 3, 5 |
| `RumourTable`, hearings, distortion flags, `TalkSystem`, `BarkSystem`, `LineBook` | Phase 3 |
| The event bus and chronicle (completion checks) | Phase 0 |
| Bounties, standing, the lord's planner, faith (pilgrimage, relics), slavery (runaways), family (rescue, escort a bride), heists | this phase |
| `WatchBook` bout records | Phase 4 |
New: `Sim/Contracts/` (`ContractBook`, `NeedScanner`, `ContractKinds`, `Verifier`), content `contracts/*.jsonc` (about 40 kinds), `Sim/Rumours/Slander`, `Sim/Dialogue/Legends`. Extended: `JobTable` (a contract is a job with a kind row and a verifier), `RumourTable` (false flag, age), `BarkSystem` (mood), the Journal.

## 3. Data
- **`ContractKindDef`** (content): `id`, `family` (escort, guard, hunt, deliver, collect, bounty, rescue, recover, clear, message, build, repair, political, criminal, faith, event), `need` (the scanner rule that posts it), `issuer` (who pays: household, business, fief, church, guild, band), `pay` (formula in mites: e.g. deliver: `distance_km x mass_kg x 0.15 + 300`; escort: `days x 450 x guards`), `deadline_days`, `verify` (an event pattern), `fail` (consequences), `crime` (true for criminal kinds: posted by fences and band leaders, visible only to people they trust).
- **The ~40 kinds** (base): escort a caravan, escort a pilgrim party, escort a bride, guard a caravan, guard a house, guard a harvest, garrison duty (service), hunt a beast, hunt a man (bounty alive), bounty dead-or-alive, catch a runaway, deliver goods, deliver a message, carry a tax wagon, collect a debt, collect rents (as bailiff), recover stolen goods, recover a relic, rescue a captive, ransom delivery, clear a cave of bandits, clear a lair (monster), find a missing person, scout a road, survey (prospect) for a lord, build (Phase 5), repair a building, soil improvement (marl a field), fight a fire's aftermath (clear rubble), harvest hands, day labour, haul for a site, smuggle (criminal), steal to order (criminal), heist a vault (criminal), break someone out (criminal), kidnap (criminal), slander a rival (criminal), take a fief (political: a lord's war aim), carry an oath or a treaty (political), tournament entry (event).
- **`Contract`** (saved, the `contracts` section; Phase 3 jobs stay in `jobs`): `{ Id, Kind, Issuer, EscrowPurse, PayMites, PostedDay, DeadlineDay, Target (person, place, goods, site), State, Taker (person or company), Evidence (chronicle ids), CauseEntry (the need that posted it) }`.
- **Rumour** gains `False` (bit), `AgeDays` read at use; **`SlanderRow`** `{ Rumour, Author, Victim, Exposed }`.

## 4. Rules

### 4.1 Needs that post contracts (`NeedScanner`, a few each hour of the day)
| Need (from the world) | Kind | Issuer |
|---|---|---|
| a household's or business's store below 3 days of an input it buys, with no merchant stock within 8 km | deliver | that household or business |
| a caravan leaving with goods worth > 5,000 mites | guard, escort | the caravan's owner |
| a reported crime with a recognised culprit | bounty | the fief (`crime-and-justice.md` 4.3) |
| a bandit band seen within 3 km of a road (last-known <= 5 days) | clear a cave | the fief or a merchant guild |
| a debt 2 seasons late | collect a debt | the creditor |
| a captive held by bandits or a lord | rescue, ransom delivery | the captive's household |
| a runaway bonded person | catch a runaway | the owner |
| the lord's planner (walls, mill, bridge, soil improvement) | build, soil improvement | the fief |
| harvest window short of hands, a site short of hauling | harvest hands, haul, day labour | farm holders, site owners (Phase 5 day labour as contracts) |
| a stolen relic, a missing person, a monster rumour >= 50% believed | recover, find, clear a lair | the church, the household, the settlement |
| a fence's buyer, a band leader's plan | criminal kinds | posted only to persons with trust >= 300 with the poster |
A posting needs the issuer's purse to hold the pay: it is moved into escrow at once (nothing is posted that cannot be paid). Each posting is a chronicle entry whose cause is the need's entry (the shortage, the crime, the band).

### 4.2 Taking, deadlines, verification, failure
Taking is a claim (Phase 3 rule); NPC companies (mercenaries, guards, builders) take contracts by the same scorer as the hero's autopilot (pay per hour, risk by the odds rule, distance), so the hero competes. **Deadlines** are game days (the old bug of comparing with the day of the month cannot recur: `DeadlineDay` is an absolute day). **Verification** is by events only: a delivery is the goods in the target store (mass ledger), a bounty is the person held by or dead at the hands of the taker's side, a clearing is the band's members dead, fled or captured, a rescue is the captive released into the taker's party. **On completion** the escrow pays the taker (`TransferReason.Wage`; a company splits by its own rule), standing with the issuer's group + the kind's delta. **On failure or expiry** the escrow returns to the issuer, the taker's standing with the issuer -2x the success delta, and kinds with a penalty (escort: goods lost) charge it from a bond the taker posted (10% of pay for escorts and guards).

### 4.3 Construction market and day labour (Phase 5 carry-overs)
NPC households and businesses commission builders through contracts (the `build` kind) when the growth system wants a cottage or a workshop in a visited place; builders bid (the lowest `bid = materials + hours x crew wage x (1,000 + margin) / 1,000`, the margin 1,450 pm as Phase 5, ties by builder id); the hero bids with his own crew by the same formula (`expected days` counts each hand's own skill, not 50); the builder's cart is a real vehicle row that carries the materials. Day labour (Phase 5) is posted as `day labour` contracts at the hiring hour from sites, farms and haulage; the board shows `Construct` jobs. Landless idle adults take these (gate: idle adults < 800 in the 100-year soak, kept from Phase 5).

### 4.4 Tournaments as fair events
A town with a market charter holds a tournament at its great fair (one a year) when its treasury holds the purse (content: 20 crowns, from the treasury into escrow at announcement, 15 days before). Entry is a contract of kind `tournament entry` (fee 1 crown into the purse). Bouts are Phase 4 practice fights (blunted: x0.5 momentum, edges blunt; yield at pain knock-out) drawn in a bracket (stream **0x692**, key tournament and round); the jousting approach is a lance run at a closing angle of 0-15 degrees off head-on (the Phase 4 open item: the angle from the riders' line, not a fixed head-on), and the **purse is paid from the sim's own bout records** (`WatchBook` results: the winner of the final), never on the game's word (closes Phase 4 S8).

### 4.5 Rumours v2
- **Slander:** a person with a `Rival` flag toward another and Speech >= 30 may tell a **false** rumour about him (a fabricated deed of a kind in `deeds.jsonc`, never a fire or a death that did not happen) with P 5% a season (stream **0x620**, key author and season); the hero can do it (talk verb "Speak ill of", Speech check vs the listener's trust in the victim). A false rumour spreads like any other and moves standing (`reputation-and-standing.md`) while believed. It is **exposed** when a hearer also holds a `Witnessed` hearing that contradicts it, or the victim answers it in court (defamation, a crime in Valeholt and Seaboard: fine 300); exposure reverses its standing effect and moves the author's standing by -2x. Phase 3's rule stands otherwise: rumours degrade, they do not invent.
- **Age:** every use of a rumour (AI knowledge, contract posting, standing) reads its age; AI weights a fact older than 2 seasons at half (`diplomacy-and-ai-lords.md` 4.2).
- **Hue and cry** is a rumour kind spoken at once within 30 m (crime 4.2).
- **Dialogue:** a **legends fallback** (when a person has no fresh rumour, he tells a legend of a place within 10 km from the history: the Codex legend's line, Phase 1); **mood in barks** (each person's mood band from needs, grievances and the weather selects the bark variant; Phase 3 open item); a rumour card's **History (H)** link opens its chronicle entry; the Journal's **map pin** (a soft marker the player sets on a contract or a rumour's place).
- **Autopilot:** dialogue raises `offer_big` for a contract worth >= 5x the hero's daily wage (AP-8 now testable); autopilot road encounters use the odds rule (watch mode's risk estimate) before accepting a fight.

### 4.6 Tick placement
| Where | What |
|---|---|
| H7 (every hour, a slice of needs: one settlement per hour) | `NeedScanner` posting; expiries |
| Event bus (any tick) | verification of completions |
| H5 hour 0 | slander rolls at a season's first day; tournament announcements |
| Barrier | the hero's takes, bids, turns-in, slander, pins |

## 5. Determinism
Streams: **0x620** slander, **0x690** NPC companies' choice ties (key company and day), **0x691** pay rounding jitter where a kind asks for it (key contract), **0x692** tournament brackets. Contracts are posted in (settlement, need kind, id) order. `ContractHash` covers contracts and escrows; `TalkHash` (Phase 3) gains the false flag.

## 6. UI (960 x 540)
Notice boards (the reeve, the lord's door, the guild, the inn) list contracts with pay, deadline, issuer and **why** ("The smithy at Oakford has no charcoal: deliver 300 kg by day 42"); the Journal's Contracts tab with pins and the evidence of completion; rumour cards with the confidence label and, once exposed, "a lie, told by Alda". Criminal contracts appear only when the poster trusts the hero.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| CR-1 | Need -> contract | a smithy with 2 days of charcoal and none for sale within 8 km: a `deliver` contract of 300 kg posted with its pay escrowed; the cause is the shortage entry |
| CR-2 | No coin, no contract | the same smithy with an empty box posts nothing |
| CR-3 | Deadline | an absolute game day; expiry returns the escrow to the issuer on that day, never earlier or later |
| CR-4 | Verification by events | a delivery completes only when the target store's mass ledger rises by the goods; a bounty only with the culprit held or dead by the taker's side |
| CR-5 | Failure consequences | an escort that loses the goods: bond charged, standing -2x, escrow back |
| CR-6 | NPC companies compete | a mercenary company takes a guard contract the hero ignores; the scorer uses the odds rule |
| CR-7 | Kind count | the base content loads about 40 kinds, each with a need, a verifier and a failure rule (validator) |
| CR-8 | Construction market | an NPC household's cottage contract gets bids from two builders and the hero; the lowest wins; expected days by each hand's skill |
| CR-9 | Day labour as contracts | 100-year soak: idle adults < 800 (the Phase 5 gate holds) |
| CR-10 | Tournament purse | the purse moves from the treasury to escrow at announcement and to the final's winner by the bout record; a forged result changes nothing |
| CR-11 | Slander | a false theft rumour about the hero lowers his standing while believed; exposed by a witness: reversed, the author -2x |
| CR-12 | Legends fallback and mood | a villager with no fresh rumour tells the local legend; a hungry villager's bark is the hungry variant |
| CR-13 | AP-8 | autopilot pauses on `offer_big` for a contract worth >= 5x the daily wage |
| CR-14 | Determinism, save | 30 days of postings and completions: `ContractHash` equal; save -> load -> save byte-identical with escrows |

## 8. Open questions (recommended defaults)
1. **Do rival companies take contracts the hero has his eye on?** Yes, first come by the claim rule; a pinned contract is not reserved. Default as written.
2. **Criminal contracts' visibility:** only from posters who trust the hero >= 300. Default as written.

## 9. Budgets
`NeedScanner` <= 2 ms an hour (one settlement a slice); verification <= 0.01 ms per event; slander <= 0.2 ms a season; content: contract kinds <= 40 KB; save: contracts 64 B each (about 500 open a region: <= 40 KB).

## 10. Notes (P6-C2, built)
Built as `Sim/Contracts/` (`ContractBook`, `ContractSystem`, `NeedScanner`, `Companies` with the construction market and the AP-8 offer rule, `Tournaments`, `Slander` with `RumourAge`, `ContractDesk`, `ContractLink`, `ContractWiring`, `ContractsView`) and `Sim/Dialogue/Legends`; the type is `WorkContract` (Phase 5's `Construction.Contract` keeps its name). Where the spec and the build part, the build is as follows.
- **Deadline:** `DeadlineDay = posted day + deadline_days`, and the contract lapses as that day begins (hour 0, the first pass of the day): "by day 42" means before day 42 starts. A taken contract not done by then fails (a verification pass at the same hour comes first, so what was done in time counts).
- **Escrow:** three `TransferReason`s appended (`ContractEscrow`, `ContractPay`, `ContractRefund`); a taker's bond (10 percent for escorts and guards) is a second chest, given back on success and charged to the issuer on a failure. Pay is `base + rate x units / 100` from the kind row; stream 0x691 jitters it only for a kind with `jitter_pct` (none of the base kinds).
- **Bounties stay Phase 3 jobs.** B2's `OddJobKind.Bounty` jobs already escrow the fief's coin and C1's `Bounties.Collect` pays them, so a second escrow would double-hold the coin: the board and the Journal show them as `bounty` rows over the job (`ContractsView`), and nothing was migrated (older saves load as they were).
- **Verification** is by what happened: a delivery by the target store's inflow (the hero's hand-over command counts the grams, and a rise of the store's count since the contract was taken counts too), a bounty by the culprit held or dead, a rescue by the captive no longer held, a clearing by a `bandit_cleared` entry, an escort or guard by its service days with the goods safe (a `caravan_lost` or `caravan_raid` entry naming the caravan after the take fails it), a report by the taker's own command. No world object exists for a band yet: the scanner reads fresh road-bandit rumours (three hearings or more) and the companies write `bandit_cleared`.
- **Needs built:** a workshop idle three days with a batch short of an input and no merchant stock within 8 km (deliver; a household's basket is not fed in yet), a caravan with goods worth over 5,000 mites (guard; the continent's caravans are not fed in yet), a believed bandit rumour (clear), a bandit-held captive (rescue, which closes B3's kidnap hook), a household's wanted cottage (build, hook), a farm or site short of hands (day labour, hook), and the fair's tournament. A need whose contract closed unanswered is not posted again for 30 days, and a delivery nobody took is taken down when the shortage is made good. The other kinds are content with their needs marked `hook` (debts and rents C3, runaways D1, criminal kinds D3, pilgrims and relics E2, war aims D2).
- **Companies:** the off-duty guards of a settlement with four or more are its company (strength 12 each); builders' companies come with the construction feed. They take by the hero's scorer (pay an hour, the odds rule, distance) and resolve by a stream 0x690 draw after the work's days.
- **Construction market:** bids use Phase 5's margin as written in its code (`labour x 1,450 / 1,000`, not `1,000 + 1,450`); a hand's pace is `500 + 10 x skill` permille. The market works on a `BuildWanted` feed and a builders feed that the runtime does not supply yet (the Phase 5 growth cottages stay with Phase 5's own contracts); the hero's bid is the `ct:bid` command.
- **Tournaments:** the bracket (0x692), the lance angle (0 to 15 degrees) and the bout records are in; the purse is paid from the final's record only. The hero's own bouts wait for the sim's fight record (`ContractWorld.BoutRecord`), which no live fight writes yet: unfought, they are lost by forfeit at the day's end.
- **Slander:** the lie is its own entry type (`slander_told`, subjects the victim then the author), so it never passes for a crime; its rumour carries the false flag in the high bit of the facts byte (no change to old save bytes) and the deed rows read it as the crime it claims. Exposure is by a witness (a hearer who also holds a Witnessed hearing of another deed of the victim's within a day of the lie) or the victim's answer in court; the author's `slander_exposed` row is twice the lie's.
- **Mood and legends:** the street view reads each near person's mood (needs, the house's grievances, the weather) through a barrier task; six mood bark variants; the legends fallback is `Dialogue.Legends` over the Codex's landmarks within 10 km, told through `TalkLink.AskNews`.
- **UI:** the Contracts screen (Journal tab, Q "Notice board"), with the evidence of how a contract went, the pin (a compass direction and distance, set only by the player) and the fair's entry.
