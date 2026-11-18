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

### 3.2 Interrupt kinds and defaults (shared `InterruptBus` with the travel spec; the Autopilot adds four)
| Kind | Fires when | Default | `Ai` means |
|---|---|---|---|
| `Attacked` | damage to the person or party, or a hostile in view moving to attack | Pause (cannot be `Ignore`) | the NPC AI chooses Flee, Shelter or fight by utility (`safety` need), like any NPC |
| `Threatened` | a hostile or unknown armed group within 60 m | Pause | Avoid/flee by utility |
| `Injured` | `HealthPm` loss >= 15% or any bleeding wound | Pause | bandage (if pack has one), rest |
| `LowFood` | food in pack and stores below 1 day | Notify | buy or eat or forage (Survive tier) within limits |
| `LowMoney` | purse below `PurseReserveMites` | Notify | switch to Work for the best-paying job in range |
| `ContractOffer` | a person offers a job (`Job` posted to you, an escort request) | Pause | accept only if the kind is in `AcceptContractKinds`, within limits; else decline politely |
| `BigChoice` | an offer flagged `big` in authored dialogue (marriage, fealty, bribe, oath, sale of property) | **Pause** | **decline and record**; irreversible choices are never accepted by the AI (hard rule) |
| `CompanionTrouble` | a squad member is hurt, lost (> 60 m for 30 s), or loyalty < 200, or a wage cannot be paid | Notify | pay the wage if the cap allows, wait for them, heal |
| `Arrived` | the destination or the post is reached | Pause | next directive in the queue |
| `DirectiveDone` | `Until` satisfied | Notify | next directive or idle at home |
| `LimitReached` | a limit blocks the plan (spend cap used, risk above `MaxRiskPm`, cannot be home by nightfall) | Pause | idle at home or the safest place; retry next day |
| `NeedMoney` | the directive needs more coin than the cap or the purse allows | Notify | switch to earning |
| `Stuck` | no progress for 20 game hours | Pause | re-plan once, then pause |
Plus all travel kinds (`Hostile, Weather, Cold, Heat, Fatigue, Dusk, Horse, CartStuck, BlockedPath, Companion, Found`) with their travel defaults; for them `Ai` means the same code that NPC travellers use (shelter in storms, make camp at dusk, rest a tired horse).
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
- **Directive to policy:** `DirectiveRunner.Plan(directive, person)` returns a short **step list** that sets `forced_place` and `bias_pm` for the brain: a step is `GoTo(place)`, `Work(job)`, `Trade(book, goods)`, `Wait(until)`, `Sleep`. While a step runs, schedule blocks that conflict (work block at another place) lose their `schedule_bonus_pm`; needs still win when urgent (hunger above 1,000 pm eats; fatigue above 800 sleeps; flee beats everything), as for NPCs.
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
- **Stepping back in:** any movement key or a click on the ground **takes control at once** (`Paused = true`, banner "Autopilot paused - Y resumes"); the player may change the directive and resume. Opening the trade or dialogue screen yourself does the same.
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
| 0x300, 0x301 | the brain's noise and job claim ties (shared with NPCs, per live-behaviour spec) | person |
| 0x380 | reserved for autopilot-only choices that are not utility ties (none in v1; tests assert it is never drawn) | person |
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
| AP-7 | Every interrupt kind fires: scripted triggers for each of the 13 AP kinds and 11 travel kinds, with the three policy values | each kind raises once, honours Pause, Notify, Ai as in 3.2, writes a log line; `BigChoice` with `Ai` takes the decline branch |
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
