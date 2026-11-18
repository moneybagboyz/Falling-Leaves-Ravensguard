# Autopilot v1: directives, limits, interrupts and zero-player watching

Plan refs: §3i (the hero can be handed to the same AI that runs every NPC, "a convenience and a way to watch the world, not a cheat"; **directives**: work a job, run a trade or caravan route, tend your business or fields,
train a skill, pursue an ambition step, guard a place, escort someone, travel to a destination, rest and recover; **limits**: a spending cap, no crimes, avoid danger above a chosen risk, come home by nightfall; **interrupts**:
attacked or threatened, injured, low food or money, a contract offer, a big choice (an offer of marriage, fealty, a bribe), a companion in trouble, reaching the destination, each set to pause, notify or let the AI handle it;
"one AI for everyone": same decision code and data, so it makes the same mistakes and its outcomes stick; allowed in Ironman; the chronicle records it; **zero-player watching**; party members use the same directives and stances),
§2.5 (utility AI for needs and schedules; one job and reservation system for NPCs, your people and you), §5 Phase 3 (autopilot v1: the hero and party members on the NPC utility AI with directives (work, trade route, travel, train,
guard, escort, rest), limits and interrupts; zero-player watching), §6 (verification: "directives and limits are obeyed (spending cap, no crimes); every interrupt kind fires; a year-long autopilot soak keeps the hero alive and
solvent in a peaceful region"; watch mode for fights is Phase 4 and out of scope here), §2.11 (deterministic replay).

## 1. Purpose
Let the player step back. A directive tells the hero (or a party member) what to do with their days; limits say what the AI must never do; interrupts say when to give control back. There is **no second AI**: the autopilot is
a thin layer that feeds the same `UtilityBrain`, job system and travel code that run every NPC, with data (a directive, limits, a policy) in place of keyboard input. That is what makes zero-player watching a real view of the world.

## 2. Dependencies
| Needs | From |
|---|---|
| `UtilityBrain`, `NeedState`, schedules, jobs and reservations, `Action`, `Job` kinds | `live-behaviour-and-utility-ai.md` |
| `TravelAuto`, `InterruptBus`, road encounters, camp, danger (`RegionDanger`) | `travel-encounters-camp.md` |
| Trade quotes, `PriceNotebook`, trip decision (`TripPlanner` shared with Phase 2 caravans/merchants), hire and squad | `trade-inventory-character.md`, Phase 2 `caravans-and-logistics.md` 3.3 |
| Crafting jobs and recipes | `basic-crafting.md` |
| Horses and carts | `horses-and-carts.md` |
| Ambitions (`ambitions.jsonc` steps with typed conditions) | exists |
| Commands with an `Origin` tag, command log and replay | Phase 2 `EconCommand` (extended: `Origin = Human | Autopilot | Npc`) |
| Crime vocabulary | **not built before Phase 6**. Phase 3 defines an `ActionTag.Crime` flag on actions (theft, trespass, assault of non-hostiles, breaking locks, using others' beds uninvited) so the limit works as soon as such actions exist; Phase 3 content has a handful of tagged actions and a test action |
New code: `Ravensguard.Sim.Autopilot` (`AutopilotTable`, `Directive`, `LimitSet`, `LimitGuard`, `DirectiveRunner`, `InterruptRouter`, `AutopilotLog`, `ActivityCatalog`); UI `AutopilotPanel`, HUD chip.

## 3. Data

### 3.1 Records (saved; only hero and party members have rows, at most 13)
```
AutopilotState { Person, Enabled, Paused (taken back), Queue: Directive[<=8], ActiveIdx, Limits: LimitSet, Policy: InterruptPolicy, LastSummaryDay, Spent: (day, mites), SessionLogStart }
Directive      { Kind, TargetKind, Target (place id | person id | recipe id | skill | route id), Params (up to 4 longs), Until (UntilKind: Done | Days(n) | CoinAtLeast(m) | SkillAtLeast(s, lvl) | Healed | Forever), StartDay, State (Pending, Active, Done, Failed, Blocked) }
LimitSet       { SpendCapMitesPerDay (-1 = none), SpendCapTotalMites (-1 = none), PurseReserveMites (never spend below), NoCrimes (default true), MaxRiskPm (0..1000, default 500), HomeByNightfall (bool) + HomePlace, NoLethalForce (default true; Phase 4), AcceptContractKinds (set) }
InterruptPolicy{ per InterruptKind: Pause | Notify | Ai }
```
`DirectiveKind` (Phase 3): `Work, TradeRoute, Travel, Train, Guard, Escort, Rest` plus two stubs reserved for later phases: `Tend` (own fields and business; Phase 5) and `PursueAmbition` (Phase 6 content; v1 maps only `has_coin_at_least` to Work or TradeRoute and `has_proficiency` to Train, and raises `Blocked` for other step conditions).

### 3.2 Interrupt kinds and defaults (the canonical list)
This is the **only** list of interrupt kinds. `travel-encounters-camp.md` and every other spec refer to it. There is one append-only `InterruptKind` enum (**23 kinds**, in the order below) and one policy type `Pause | Notify | Ai`. A kind may be set to `Notify` with the toast off (`quiet`); `quiet` is not allowed for `Attacked`. `Ai` is offered only for autopilot persons; in manual play a kind is Pause or Notify. The "Source" column says which spec raises the kind. Names were merged: `Lost` into `BlockedPath`, `CompanionTrouble` into `Companion`, `ContractOffer` into `Contract`, `LowFood` into `Hunger`.
| # | Kind | Source | Fires when | Default | `Ai` means |
|---|---|---|---|---|---|
| 1 | `Hostile` | travel | a hostile or unknown-armed person in view, or an encounter spawned (clock drops to 1x) | Pause | avoid or flee by utility |
| 2 | `Attacked` | travel | damage to the person or party, or a hostile in view moving to attack | Pause (never `quiet`) | the NPC AI chooses Flee, Shelter or fight by utility (`safety` need), like any NPC |
| 3 | `Weather` | travel | state becomes thunderstorm, blizzard, heavy rain with a ford ahead, heatwave | Notify | shelter in storms |
| 4 | `Cold` | travel | `cold_stress_pm > 400` or core below 36.00 | Notify, Pause at the clothing spec's `moderate` state | find shelter, add clothing, light a fire |
| 5 | `Heat` | travel | `heat_stress_pm > 400` | Notify, Pause at `moderate` | rest in shade, drink |
| 6 | `Hunger` | travel | `hours_since_meal >= hunger_eat_hours` (13, `live-behaviour-and-utility-ai.md` 4.1) with food in the pack: the person auto-eats (10-minute meal break); also when food in the pack and stores fall below 1 day | Notify | buy, eat or forage (Survive tier) within limits |
| 7 | `Fatigue` | travel | `FatiguePm > 800` | Pause ("camp needed") | make camp or go to the nearest bed |
| 8 | `Dusk` | travel | sunset minus 60 min | Notify (HUD setting: continue / stop / make camp) | make camp |
| 9 | `Horse` | travel (horses) | mount stamina below 20% or lame | Pause | rest a tired horse |
| 10 | `CartStuck` | travel (horses) | a cart cannot move for 20 game seconds | Pause | push, unload or wait, as NPC drivers do |
| 11 | `BlockedPath` | travel | ford closed, tree down, gate closed | Pause (re-plan offered) | re-plan once |
| 12 | `Companion` | travel | a squad member is hurt, lost (more than 60 m for 30 s), has loyalty below 200, or a wage cannot be paid | Notify | pay the wage if the cap allows, wait for them, heal |
| 13 | `Found` | travel | landmark, shrine, corpse or forage ground within 25 m | Notify | ignore, or forage if the directive allows |
| 14 | `Arrived` | travel | the destination or the post is within 3 m | Pause | next directive in the queue |
| 15 | `Contract` | travel, dialogue | a person offers a job (a `Job` posted to you, an escort request), a toll, a ford ferry | Pause | accept only if the kind is in `AcceptContractKinds` and within limits; else decline politely |
| 16 | `Threatened` | autopilot | a hostile or unknown armed group within 60 m | Pause | avoid or flee by utility |
| 17 | `Injured` | autopilot | `HealthPm` loss >= 15% or any bleeding wound | Pause | bandage (if the pack has one), rest |
| 18 | `LowMoney` | autopilot | purse below `PurseReserveMites` | Notify | switch to Work for the best-paying job in range |
| 19 | `BigChoice` | autopilot (dialogue) | an offer flagged `big` in authored dialogue (marriage, fealty, bribe, oath, sale of property) | **Pause** | **decline and record**; irreversible choices are never accepted by the AI (hard rule) |
| 20 | `DirectiveDone` | autopilot | `Until` satisfied | Notify | next directive, or idle at home |
| 21 | `LimitReached` | autopilot | a limit blocks the plan (spend cap used, risk above `MaxRiskPm`, cannot be home by nightfall) | Pause | idle at home or the safest place; retry next day |
| 22 | `NeedMoney` | autopilot | the directive needs more coin than the cap or the purse allows | Notify | switch to earning |
| 23 | `Stuck` | autopilot | no progress for 20 game hours | Pause | re-plan once, then pause |
Travel's old `ignore` value is `Notify` with `quiet`. Unknown or later-added kinds default to Pause.
**Semantics:** `Pause` pauses the game (speed 0), opens a small alert panel with the cause and buttons (Take control, Continue autopilot, Open Journal) and records an entry; `Notify` shows a toast, logs and keeps going (speed unchanged but drops from 8x to 4x for 5 real seconds so you can see); `Ai` logs and lets the brain handle it.

### 3.3 Content (`autopilot/activities.jsonc`, `autopilot/settings.jsonc`)
```jsonc
{ "type": "activity", "id": "train_smithing_30", "skill": "smithing", "uses": [ { "recipe": "practice_billet", "d_x100": 45, "cap_level": 30, "needs": [ "forge" ] } ], "then": "work_smithing" }
{ "type": "autopilot_settings", "id": "default", "default_max_risk_pm": 500, "stuck_game_hours": 20, "summary_hour": 6, "log_lines_max": 2000, "notify_speed_drop_s": 5, "home_by_nightfall_offset_min": 0 }
```
`ActivityCatalog` maps a skill to ways of training it: practice recipes and the practice yard up to skill 30; above 30, the best real work (`Work` in the occupation whose `Skills` includes it, via the labour market and jobs) with the highest `d x gain` that fits the limits.

## 4. Rules

### 4.1 One AI: how the layer plugs in
`UtilityBrain.Choose(PersonCtx)` is the only function that decides an action. `PersonCtx` has an optional `Policy` (null for plain NPCs): `bias_pm[ActionKind]` added to scores, `forced_place` for the current directive step, `filter_mask` of actions removed (limits), and `claim_filter` for jobs. **No other code path moves the hero or a party member while autopilot is on.** Everything the AI does goes through the same `Action -> Intent -> path/job` code and the same commands, tagged `Origin = Autopilot` so logs and replays can tell them apart.
- **Equivalence (test AP-1):** a person row cloned from an NPC (same record, needs, schedule, jobs) run with `Policy = null` as an NPC and as the hero with autopilot `Enabled` and a directive equal to "follow my schedule" produce identical action logs (kinds, places, times) for 3 game days under the same seed. The hero's results (wages, deaths, mistakes) stick like anyone's.
- **Directive to policy:** `DirectiveRunner.Plan(directive, person)` returns a short **step list** that sets `forced_place` and `bias_pm` for the brain: a step is `GoTo(place)`, `Work(job)`, `Trade(book, goods)`, `Wait(until)`, `Sleep`. While a step runs, schedule blocks that conflict (work block at another place) lose their `schedule_bonus_pm`; needs still win when urgent (a person at or past `hunger_eat_hours` (13) eats; fatigue above 800 sleeps; flee beats everything), as for NPCs.
- **Planning cadence:** the planner runs for each autopilot person at the daily slice (hour 0 plus at step completion and on interrupt), never per tick; the brain's own cadence is the live 1 Hz (abstract at block boundaries). When the hero is far from any bubble (travelling at 8x or on a long trip) the hero is still a person record with `AbstractJob` and `PathPlan`, so autopilot works the same in either tier.

### 4.2 Directives
| Directive | What it makes the AI do (using existing systems) | Ends when | Typical limit use |
|---|---|---|---|
| `Work(source, filter)` | each morning claims the best matching job within `max_commute_m 1,350` from the board, or applies to a business vacancy (`HireWorker` to a business with `OpenSlots`); does the work (`Haul`, `FarmWork`, `TendShop`, `Guard`, `Craft` of own recipe); eats, shops (inside the cap), sleeps | `Until` (days, coin) | spend cap, home by nightfall |
| `TradeRoute(A, B, goods[], min_margin_pm)` | uses `TripPlanner` (the Phase 2 merchant logic) with the hero's `PriceNotebook`: buy goods at A within the cap, load a cart or pack animal, travel by `TravelAuto` (camp or inn at dusk), sell at B for at least `min_margin_pm` over cost and travel cost; replans each trip; may hire a guard (cap and risk permitting) | margin below `min_margin_pm` twice (raises `NeedMoney` or `LimitReached`), or `Until` | spend cap, max risk (route choice), home by nightfall (short routes only) |
| `Travel(dest, via?)` | `TravelAuto`: route, interrupts, camp rules; arrival raises `Arrived` | arrival | max risk, home by nightfall (avoid starting too late) |
| `Train(skill, to_level)` | `ActivityCatalog`: practice yard, practice recipes, or real work up to `to_level`; never above caps | skill reached or time | spend cap |
| `Guard(place, shifts)` | claims `Guard` jobs at the post, stands watch, sleeps in off hours | `Until` | max risk (can be raised by the player for dangerous posts) |
| `Escort(target)` | follows a person or caravan within 30 m, sleeps and eats with them, fights per policy (Phase 4) | target arrives or dies | max risk |
| `Rest(until)` | goes to the nearest own/rented/inn bed (cost within cap), sleeps, eats, dresses wounds (bandages from the pack), waits | `Healed` (no open wounds, fatigue < 100 pm) or days | |
| `Tend`, `PursueAmbition` | stubs for Phase 5 and 6 | | |
**Queue:** up to 8 directives run in order; when one ends the next starts; an empty queue means "live like an NPC of your occupation" (the default schedule), so autopilot never leaves the hero standing still.

### 4.3 Limits (enforced twice)
- **At planning** (`LimitGuard.Filter`): actions that would violate a limit are removed from the brain's candidate set before scoring. **At execution** (the barrier): every command with `Origin = Autopilot` passes `LimitGuard.Check`; a violating command is **rejected** with `LimitViolation`, logged in `AutopilotLog`, and counts for test AP-12 (zero violations ever). The player's own commands are never filtered.
| Limit | Rule |
|---|---|
| `SpendCapMitesPerDay` | the sum of `Buy`, `PayCoin`, `HireWorker`, wages, tolls and ferries issued by the autopilot for the current game day (00:00 to 24:00) must stay below the cap; unspent allowance does not carry over; `PurseReserveMites` is never spent. A purchase that would exceed the cap is refused and `NeedMoney` may fire |
| `SpendCapTotalMites` | a lifetime budget for the session of this autopilot run (reset when the player edits it) |
| `NoCrimes` (default on) | any action with `ActionTag.Crime` is removed; the AI will not take unowned goods, enter closed private rooms uninvited, use others' beds, or attack non-hostile people (a starving hero then begs or works or forages); **it does not know about witnesses** (the plan's seen/unseen belongs to humans) |
| `MaxRiskPm` | a plan is refused if the path or place has `risk_pm = RegionDanger x night factor (x2 at night) x 1000/1000 > MaxRiskPm` anywhere along it; candidates are other routes (roads), later departures (day), or an escort (cuts risk by 40%, cost within cap); presets: Low 200, Medium 500, High 800 |
| `HomeByNightfall` | a plan that starts away from `HomePlace` is allowed only if its expected arrival back is at or before `sunset` of that day (`Daylight.SunsetMinute`): `start + round trip time <= sunset` (round trip from path length and gait); if not possible the AI waits, picks a shorter plan or raises `LimitReached`; already away at sunset - 1 h, it heads home (or to the nearest inn if closer and the cap allows) |
| `NoLethalForce` (Phase 4) | placeholder: stance `non-lethal` of watch mode |
| `AcceptContractKinds` | contract offers outside the set are declined |

### 4.4 Interrupts: routing
`InterruptRouter` receives interrupt events from the sim (`InterruptBus`), looks up the policy by kind, and applies Pause, Notify or Ai. An interrupt is raised **only for autopilot persons** (the hero and party members with autopilot on) and is identical to what manual play sees on the same bus. Unknown kinds default to Pause. The router de-duplicates (the same kind within 10 real seconds raises once) and every interrupt writes an `AutopilotLog` line and, for Attacked, Injured, BigChoice and Arrived, a chronicle entry.
`BigChoice` is raised by dialogue content: an effect `offer_big` on a choice node (marriage, fealty, bribe, oath, sale of property) blocks the AI from advancing past it; with policy `Ai` it picks the "decline" branch authored for it (every `big` node must have a decline branch: content validation).

### 4.5 Zero-player watching
- **Mode:** autopilot on and all interrupts set to Notify or Ai (a preset "Watch the world": Attacked and Injured stay `Pause` by default but can be set to `Ai` by the player). The camera follows the hero (lock key `Home` re-centres); speed controls 1 to 8x work as always; **8x respects the same gate** (no hostile in view; at 8x the bubble shrinks to the party and path ahead).
- **Daily summary:** at `summary_hour` (06:00) a toast and a Journal tab "Autopilot log" show yesterday: hours by activity, coin earned and spent, items made, distance travelled, interrupts and how they were handled, skills gained (levels with 1 decimal), people met, rumours heard. Entries are generated from the log, not narrated by an LLM.
- **Stepping back in:** any movement key or a click on the ground **takes control at once** (`Paused = true`, banner "Autopilot paused - Y opens the panel"); the player may change the directive and resume. Opening the trade or dialogue screen yourself does the same.
- **Death and Ironman:** when the hero dies, autopilot pauses; in Ironman the heir or companion becomes the hero and autopilot is **off** for them until you turn it on. Autopilot is allowed in Ironman under the same rules.
- **Chronicle:** `AutopilotStart` and `AutopilotStop` entries with the directive; significant acts (wounds, arrivals, deals over 10 crowns, deaths) are ordinary chronicle entries, flagged `Data["autopilot"] = "1"`.

### 4.6 Party members
Each `Follower` (squad) can have its own `AutopilotState` (own directive, same limits type, the policy table is the same). Defaults: a new follower has `SquadOrder = Follow` (the trade spec) which is just a directive `Escort(hero)`. A follower on `Work` earns wages into **their own purse** (`remit_pct = 0`, field `remit_pct` per directive, open question 4); their squad wage from the hero continues unless the hero sets "off duty". Limits apply to *that member's* spending (they use their own coin and the hero's `PayCoin` only for wages). A member on a trade route uses the squad cart and a share the hero configures (Phase 5 partnership). Interrupts of members raise `CompanionTrouble` for the hero if the member's policy says Notify or Pause.

### 4.7 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | `InterruptRouter` and `LimitGuard.Check` run inside the command pump at barriers, not per tick; `DirectiveRunner.Advance` for <= 13 persons on step events (cheap), the brain as for NPCs |
| H4 slice (hero's hour) and hour 0 | `DirectiveRunner.Plan` for the day (route and job choice, `TripPlanner`), summary at 06:00 |
| Barrier | execute autopilot commands after `LimitGuard.Check` in `(Frame, Seq)` order |
| H5 | `Stuck` detection, directive `Until` evaluation, daily spend reset |
LODBridge: autopilot persons are persons; a hero beyond any bubble (8x travel, waiting) stays on `AbstractJob` + `PathPlan`; the same plan and limits apply, so autopilot continues without a live entity.

## 5. Determinism
| Stream | Use | Key |
|---|---|---|
| 0x300 | the brain's noise and ties (shared with NPCs, per live-behaviour spec) | `GameSec / 20`, person, `draw = actionIndex` |
| 0x301 | job claim tie-break (shared with NPCs) | game hour, **job**, `draw = claimant person index` |
| 0x380 | reserved for autopilot-only choices that are not utility ties (none in v1; tests assert it is never drawn) | person |
0x301 is keyed by the **job** (as in `live-behaviour-and-utility-ai.md` section 5), not by the person. A claim is a contest for one job: each claimant's rank is `Draw(0x301, hour, jobId, claimantIdx)` and the lowest wins. That gives the same winner whether the hero, a party member or an NPC is in the contest, and does not depend on processing order. Keying by person would give the same person a different roll for every job and make the result depend on who else claims, so the same claim could resolve differently with autopilot on and off (AP-1).
`Origin` is part of the command log; replaying a recorded session (human plus autopilot commands) with the same seed reproduces the hash. No wall clock: summary hours and caps use game time. The planner iterates candidates in ascending ids.

## 6. UI and controls
- **HUD chip** next to the clock: `[Auto: Work at the smithy | Spent 120/300]`, click to pause or resume; hotbar button. **Y** opens the Autopilot panel (new world-context binding; CI-checked). **Q on a place or person** adds directives: "Work here", "Guard this", "Escort", "Travel here", "Rest here".
- **Panel:** left, the queue (reorder, delete, add); middle, the selected directive's target and `Until`; right, **Limits** (spend per day in crowns, reserve, risk Low/Medium/High, No crimes, Home by nightfall with a home picker) and the **Interrupts** table (one row per kind with Pause / Notify / AI selectors and a "Reset to defaults"). Presets: *Cautious*, *Worker*, *Watch the world*.
- A **warning panel** before starting shows what the AI will not do given your limits ("It cannot buy food above 3 crowns a day"), and lists provisions needed for trade routes.
- Esc closes; movement keys take control.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| AP-1 | Equivalence: NPC row cloned as hero with "follow schedule" directive, 3 game days, same seed | identical action logs (kind, place, minute) and identical coin and stock results |
| AP-2 | Spend cap: cap 300 mites/day, hero tries 5 purchases of 100 | first 3 accepted, 4th and 5th rejected by `LimitGuard` (planning removes them; the barrier check also rejects a forced one); at 00:00 the allowance resets |
| AP-3 | Reserve: purse 500, reserve 400, cap none | no purchase that leaves less than 400 |
| AP-4 | No crimes: hero hungry with no coin next to a stall; test content adds an `ActionTag.Crime` "take bread" at utility 1,200 | the action is never chosen over 365 game days; with `NoCrimes` off it is chosen within 2 game days |
| AP-5 | Risk: two routes, danger 450 and 300 along their cells, `MaxRiskPm = 400` | the 300 route is chosen; if both are above 400 the plan is refused with `LimitReached`; at night (x2) the 300 route (600) is refused too |
| AP-6 | Home by nightfall at lat 480: 2 km each way at jog 3 m/s (3.70 game hours each way, 7.41 h round trip); midwinter sunset 15:58 | leave 08:30 accepted (back 15:55); leave 09:00 refused; midsummer (sunset 20:01) leave 11:00 accepted |
| AP-7 | Every interrupt kind fires: scripted triggers for each of the 23 kinds in 3.2 (the count is read from the `InterruptKind` enum, so a kind added without a test fails), with the three policy values | each kind raises once, honours Pause, Notify, Ai as in 3.2, writes a log line; `BigChoice` with `Ai` takes the decline branch |
| AP-8 | `BigChoice` content validation | a `big` node without a decline branch fails content load |
| AP-9 | Take control: movement key during autopilot | `Paused` within 1 tick; commands from the player are not filtered; resuming continues the directive |
| AP-10 | Zero-player: 30 game days, `Work` directive, all interrupts Notify, labourer in a peaceful region, 8x when allowed | hero alive, no `Stuck`, daily summaries exist (30), no violation; wall time at 8x about 1.2 real hours |
| AP-11 | **Year soak** (60 game days x 1 year x 3 seeds, peaceful region): `Work` then `Train(smithing 30)` then `Work`, spend cap 200 mites/day, no crimes | alive at the end, purse never below 0 and never below the reserve, `NutritionCd >= 1,000` every day, income >= 1.05 x spending, 0 `LimitViolation` events, 0 crimes, skill gain consistent with the character spec |
| AP-12 | Violation counter | across all tests `LimitViolation` from planning errors is 0 (the barrier check only fires for injected violations) |
| AP-13 | Party: 3 members, directives Work, Guard, Rest | each does its own; the hero's autopilot off; wages paid to the right purses; conservation exact |
| AP-14 | Trade route: A to B, flour, `min_margin_pm = 100`; book prices give a 14% margin after costs | the hero buys within cap, travels, sells; with the margin at 8% the route pauses with `NeedMoney` after 2 trips |
| AP-15 | Replay: record 10 game days (human plus autopilot) and replay | identical `EconomyHash` and `TalkHash` per day on two machines |
| AP-16 | Save and load in the middle of a travel directive | resumes the same step, caps and spent so far intact |
| AP-17 | Budget: 13 autopilot persons for 1 game day | planner <= 2 ms per person per day; `LimitGuard` <= 5 microseconds per command |

## 8. Open questions for the user
1. **Default limits when you turn autopilot on:** spend cap 3 crowns per day, no crimes on, risk Medium, home by nightfall off. Alternative: ask every time. Default as stated, shown in a one-screen confirm.
2. **Do big choices always pause?** The AI declines and notes irreversible choices (marriage, fealty, bribes, oaths, sale of property) even with policy `Ai`. Default: yes, a hard rule.
3. **Autopilot in dangerous play before Phase 4 combat:** for Phase 3 the `Attacked` response uses flee/shelter and the Phase 0 melee spike; the full watch-mode comes in Phase 4. Default accepted.
4. **Do followers pass earnings to the hero?** `remit_pct` default 0 (their own purse). Alternative 50%. Default 0.
5. **Autopilot achievements and Ironman:** allowed, no special marks. Default as the plan.

## 9. Budgets
| Item | Budget |
|---|---|
| Brain evaluation | same as NPCs (<= 15 us per eval); autopilot adds <= 0.01 ms per tick for 13 persons |
| Planner (per person per day) | <= 2 ms (including `TripPlanner` over <= 40 goods x 6 known markets) |
| `LimitGuard.Check` | <= 5 microseconds per command; 0 allocations |
| Log | <= 2,000 lines kept (ring buffer), <= 100 KB in the save; summaries keep 30 days |
| Interrupt panel | opens in <= 2 ms |

## 10. Notes (as built, Phase 3 autopilot v1)

**Where it lives.** `Sim/Autopilot/` (`Model`, `AutopilotContent` with `ActivityCatalog`, `LimitGuard`, `InterruptRouter`, `AutopilotLog`, `AutopilotBook`, `AutopilotWorld` in four partial files, `AutopilotView`), `Sim/Save/AutopilotSave.cs` (the "autopilot" section, absent until autopilot was used), `Sim/Behaviour/ActionPolicy.cs` and `PersonDecision.cs`, content `content/base/autopilot/{activities,settings}.jsonc` (embedded in the Sim assembly like the behaviour content; defs in `Content/Defs/AutopilotDefs.cs`), Game `UI/AutopilotPanel.cs` and `World/StreetView.Autopilot.cs`. Tests: `tests/Ravensguard.Sim.Tests/Autopilot/` (`ApWorld` is the headless world: the real economy, markets, population, odd jobs and the hero's desk at the hour barrier).

**One AI, literally one function.** `PersonDecision.Decide` (needs, schedule block, gates, the optional `ActionPolicy`, then `UtilityBrain.Choose`) is the code `PeopleBridge.Decide` now calls for every live NPC (the bridge was changed to call it; for an NPC the policy is null and the result is bit for bit what it was) and the code the hero and party members call. `ActionPolicy` is the spec's `bias_pm` and `filter_mask`; `ChoiceContext.Policy` is null for NPCs. AP-1 compares 3 game days of 20-second windows (4,320 choices) of the hero against the NPC path. The hero is modelled on a stand-in population row (`AutopilotBook.StandIn`, default the first adult labourer) because the hero is not a population person; party members use their own rows. Noise and ties stay on stream 0x300; stream 0x380 is reserved and a test asserts no autopilot source names it.

**Cadence.** `AutopilotWorld.Advance(toSec)` runs on the tick worker at a barrier, window by window (20 game seconds, keyed on the game second), so a run at 1x, 8x or in hour-sized chunks is identical (test: chunks of 1 s, 8 s and 3,600 s give one hash). 13 persons cost 6 us per sim tick at 1x (10 us per person-window); `LimitGuard.CheckSpend` 0.007 us and no allocation.

**Choices made where the spec was open.**
- Directive to policy: `Work`, `Guard`, `Train` bias `TakeJob` and `Work` (+400) and forbid `Socialise` and `Worship`; `Rest` forbids work and biases `Idle`; `Travel`, `TradeRoute` and `Escort` take the walk whenever the brain does not choose Eat, Sleep, Flee or Shelter, so needs still win and a traveller sleeps at night where they stand (the camp). The spec's `StepKind` list is saved (`AutopilotState.Step`) but the step is the brain's action plus the directive's own state (`Aux0`..`Aux3`); nothing else is needed yet.
- Hero's meals are three a day from `NeedModel`'s own meal clock, eaten from the pack (a third of `MealMeasuresPerDay` each); the pack is topped up at the nearest market inside the cap with a `Buy` command (`Origin = Autopilot`, a limit of 115% of the quote, the barrier re-checks it). `NutritionCd` counts 1,000 for a day of three full meals.
- Spend accounting: planning reserves the order's limit, the barrier (`HeroDesk.Gate`) re-checks against the price at that moment (never above the order's own limit: above it the desk refuses with "price moved" and no coin moves, which is not a violation), `HeroDesk.Settled` trues the reserve up. A forced autopilot command with no reservation still counts. `HeroCommand.Origin` and `ClaimRequest.Origin` carry who asked.
- Directives implemented: Work (best wage per hour within 1,350 m, risk and nightfall checked, or the NPC rule when the queue is empty), Guard (Guard jobs only), Train (practice uses via `Progress.Use` up to the activity cap, the practice yard supplies the material; above it real work), Rest (heals 1 permille an hour, ends `Healed`), Travel (a `TravelState` journey when the host gives one, else a plain walk; risk, dawn departure, nightfall; `Arrived` and `DirectiveDone`), TradeRoute (buy at A, walk, sell at B, judged on the live quote and the real coin, `NeedMoney` and Blocked after two thin trips), Escort (follows a person's position when the host has one). `Tend` and `PursueAmbition` are Blocked stubs that raise `DirectiveDone`.
- `Hunger`, `Injured`, `Threatened`, `LowMoney` are raised from the person's own state (edge triggered); travel's 15 kinds are read off the bus (`Observe`), the router logs and chronicles them and the brain answers the `Ai` ones (`AiResponse`). `BigChoice` set to Notify reads as Pause (hard rule); with `Ai` it declines and counts. Contracts: `Ai` accepts only kinds in `AcceptContractKinds`.
- Take control: `TakeControl` is instant, keeps the directive, and no windows are made up for the time away; any movement key, a click on the ground, E beside a person or opening the trade screen calls it in the game. The HUD chip under the clock shows `[Auto: Work for pay | Spent 120/300]`; click pauses or resumes; Y opens the panel.
- In the game the autopilot has no `TravelState` (the main thread owns journeys) and uses a private interrupt bus; its Pause alerts pause the clock and show a small panel (Take control, Continue autopilot, Open journal).

**Found and fixed on the way.** The hero's id is `EconomyState.HeroOwner`, `Person#0`, which is `EntityId.None`; `JobTable.End` skipped removing the worker of a finished job when the worker equalled `None`, so the hero could never take a second job (`AlreadyHasJob` for ever). It now removes the entry that points at the job. `JobTable.Restore` accepts a claimed job of worker 0.

**Key table.** Y is the autopilot panel (World). `cart_unload` was also on Y (horses spec); it moved to Backslash. `--check-keymap`: 175 actions, 0 conflicts.

**Test status.** AP-1 to AP-17 are in `Autopilot/` (AP-8 validates the rule on a descriptor: the dialogue content has no `big` flag yet, so the content-load check is wired when dialogue adds `offer_big`; AP-15 compares two full sessions with human and autopilot commands, day by day, and checks the command tape tags both origins; AP-17 timing). AP-11 (`Category=Soak`, three seeds, one 60-day game year each, `Work` 20 days, `Train(smithing 30)`, `Work`, cap 200 a day, a crime on offer all year): alive, never below 0, 3 meals and 1,000 cd a day, income 15,400 against spending 3,300, 0 violations, 0 crimes, smithing 0 to 30, mass and coin audits exact.

**Not done / later.** Q-menu verbs ("Work here", "Guard this") and giving a party member a directive from the squad screen (the sim supports it, AP-13); the watch-mode preset does not yet speed the clock by itself; `NoLethalForce` and fights are Phase 4; `Escort` needs the live tier's person positions; health and bleeding are a stub (`AutopilotState.HealthPm`, set by tests) until Phase 4; the journal tab "Autopilot log" shows the last log lines in the panel only.
