# Ambitions and autopilot: the full ambition tree, and the hero's AI pursuing it

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §3.11 (ambitions with milestones; hints name where such work is found; pins are the player's), §3i (autopilot directives, "pursue an ambition step", limits and interrupts, one AI for everyone), §5 Phase 8 (the full tree).
Carried in: `PursueAmbition` is a stub (phase-3 review); decision D1 (how many great ambitions).

## 1. Goals
1. Ambitions form a tree: 8 start ambitions (one per origin, as today), about 16 middle ambitions and 6 great ones (D1).
2. Every step shows its progress, and the hero picks the next ambition from the ones a finished ambition opens.
3. Autopilot's `PursueAmbition` works: it picks real directives that move the current step, under the player's limits, and says why.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `content/base/ambitions/ambitions.jsonc`: 9 linear ambitions, steps of conditions from `BaseVocabulary` | **extended:** `tier`, `opens`, `requires`, `ending` on each ambition; 22 new ambitions |
| `Hero/HeroRecord.cs`: `AmbitionId`, `Milestone` | **extended:** a history of done ambitions and choices |
| `Autopilot/Model.cs`: `DirectiveKind.PursueAmbition` raises `Blocked` | **built:** `Autopilot/AmbitionPlanner.cs`, content `autopilot/means.jsonc` |
| `Content/BaseVocabulary.cs` | **appended:** about 12 conditions (section 3.4) |

## 3. Rules

### 3.1 The tree
- **Tiers:** `start` (an origin's), `middle`, `great`. A great ambition has an `ending` id (endings spec).
- **`opens`:** the ambitions offered when this one is done. **`requires`:** conditions that must hold for an ambition to be offered at all (for example "take a crown" requires a titled fief).
- **Offer:** when an ambition's last step is done, the hero is offered every ambition in its `opens` whose `requires` hold, plus any middle ambition whose `requires` hold and which no done ambition excludes. At most 4 are shown, in content order.
- **Choice:** the player picks one, or "none for now" (the offer stays in the Journal). Autopilot picks by the planner's score (3.3) when the interrupt is set to "let the AI handle it".
- **Switching:** the player may drop the current ambition for another offered one at any time. Progress on dropped steps is not kept; done ambitions stay done.
- **Heirs** start with no ambition and are offered the middle tier that their state allows, as an origin's start.

### 3.2 Steps and progress
- A step is done when all its conditions hold (as today). It is checked **every game hour** for the current step only, and at once after a big event (an office, a purchase, a death in the family).
- **Progress (pm, 0 to 1,000)** for a step is the **lowest** of its conditions' progress:
  - a numeric condition: `min(1000, 1000 x have / need)`, integer division;
  - a yes/no condition: 0 or 1,000.
- The Journal's Ambitions tab shows the tree, the current step's conditions with their progress, and the hints of §3.11 ("the guild meets at the hall in Ranunfield").

### 3.3 The planner (`PursueAmbition`)
- **Means table** (`autopilot/means.jsonc`): for each condition id, the directives that can move it, with a target selector. Examples:
  - `has_coin_at_least`: Work (best-paid open job), TradeRoute (if Trade >= 30), Tend (an owned business);
  - `has_proficiency`: Train (that skill);
  - `owns_building`: Work until the price is in hand, then a Buy action within the spending cap;
  - `has_followers_at_least`: Recruit at the nearest tavern or camp;
  - `completed_contract_count`: take the best contract the hero qualifies for.
- **When it plans:** at dawn each game day, when its child directive ends, and after an interrupt.
- **Score of a means** (integer): `score = rate_pm_day x weight_pm / 1000 - risk_pm x risk_weight / 1000`.
  - `rate_pm_day` is the progress the means made a day the last time it ran (a running average, 3.5); a means never tried uses its content `prior_pm_day`.
  - A means that breaks a limit (spending cap, no crimes, risk above the set level, home by nightfall) scores nothing.
- **Running:** the best means runs as a child directive for at least one game day. The parent shows "Pursuing *Join the guild*: working at the dye works for coin (best of 3 ways)".
- **Stuck:** if no means scores above 0 for **3 game days**, or progress has not moved in **10 game days**, it raises `AmbitionStuck` with the reason (for example "every way breaks your spending cap").
- **Big choices** (marriage, fealty, a bribe) stay interrupts, as §3i says; the AI's answer is the NPC AI's.
- **One AI for everyone:** companions under the directive use the same planner (plan §3i, "party members"). NPC ambitions keep their Phase 3 and 6 rules.

### 3.4 New conditions (appended to `BaseVocabulary`)
`holds_fief_rank`, `rules_realm`, `founded_faction`, `dynasty_generations`, `slew_monster`, `owns_works_tier`, `holds_guild_office`, `holds_church_office`, `owns_relic`, `wealth_at_least` (coin plus holdings at market value), `leads_men_at_least`, `won_battles`. Each has a progress rule.

### 3.5 Integer math notes
- Running rate: `rate = (3 x rate + day_delta + 2) / 4`, in pm a day, clamped to 0..1,000.
- `wealth_at_least` sums mites in `long`; holdings are valued at the last market price of the settlement, never a float.
- Ties in score break by the means' content order, then 0x800 keyed on the hero id and the day.

## 4. Saves
Additive keys in the hero record: `AmbitionsDone` (ids with the day), `Offered` (ids), `Dropped` (ids). The `autopilot` section gains `Child` (the running means) and `Rates` (id to pm). Old saves: the current ambition and milestone keep their meaning; no done history means none.

## 5. Chronicle, UI and strings
- Entries: `ambition_chosen`, `ambition_done`, `great_ambition_done`, each caused by the step's last condition's event.
- The Journal's Ambitions tab (no new key), the autopilot panel's "why" line, and the `AmbitionChoice` and `AmbitionStuck` interrupts.
- About 260 new strings (`am_*`, `ap_*`).

## 6. Golden tests
| Id | Name | Asserts |
|---|---|---|
| AT-1 | Tree loads | 30 ambitions; every `opens` resolves; no cycle; every great one names an ending |
| AT-2 | Vocabulary | every condition id in the tree is in `BaseVocabulary` and has a progress rule and a means row |
| AT-3 | Progress | a step of 300 of 1,000 sp and Trade 20 of 40 shows 300 pm (the lowest) |
| AT-4 | Offer | finishing `make_your_fortune` offers its `opens` whose `requires` hold, at most 4, in content order |
| AT-5 | Requires | "take a crown" is not offered without a titled fief |
| AT-6 | Save | a hero with 2 done ambitions and an open offer round-trips byte-identical; an old save loads with an empty history |
| AT-7 | Planner picks | coin short and Trade 35: TradeRoute beats Work by rate; with a spending cap of 0, Buy is never chosen |
| AT-8 | Limits | "no crimes" removes theft from every means; the hero never commits one in a 60-day run |
| AT-9 | Stuck | every means over the cap: `AmbitionStuck` on day 3 with the reason |
| AT-10 | Rate | after 5 days of +40 pm, the rate is within 2 of the integer recurrence |
| AT-11 | Companion | a companion under the directive trains its skill to the step's level |
| AT-12 | Soak | a 2-year autopilot run per origin (8): every hero alive and solvent in a peaceful region, at least one step done, determinism hash equal on a resumed run |

## 7. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-AT-1 the tree | tiers, offers, progress, the new conditions, 22 ambitions, the Journal tab | AT-1 to AT-6 | 1.0 |
| P8-AT-2 the planner | means table, scoring, child directives, stuck, companions, the panel's why line | AT-7 to AT-12 | 1.0 |

## 8. Technical defaults
- An ambition dropped and offered again starts its steps from 0.
- The planner never chooses a means that needs travel beyond 3 regions; it plans the first leg as Travel.
