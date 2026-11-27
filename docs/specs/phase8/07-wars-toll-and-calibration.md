# War's toll and calibration: AW-16's failing gates, the count battle, the AW-C scenarios, and kicks everywhere

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §5 Phase 7 done-when 4 (the war soak: dearer bread on march routes, refugees, depopulated borders, recovery in about 15 years), §4A.3 (the abstract resolver within ±10% of tactical), §2.6 (one resolver for every blow).
Carried in (phase-7 review; overview §10h): campaign intensity (7 hosts in 9 war years), AI purveyance when short, war flight and return (0 refugees), emptied borders, bread dearer on 34% of route seasons against 70%, recovery; AW-10 (31, 6 and 13 of 40); AW-C (E, G, H, I, AW-5, AW-6); AB3's bound of 18; kicks everywhere (CV-B). Decisions D2 and D11.

## 1. Goals
1. War leaves the mark the plan asks for: every AW 4.6 gate passes in all ten nightly worlds.
2. The count battle and the abstract v2 battle meet their gates, or ship as named ratchets (D11).
3. Kicks and bites follow one rule in every fight (D2).

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Armies/CampaignBrain` (via `AM-E`), `WarSupply/HostSupply*`, `People/Vital/Demography.cs`, `Sim/War/WarSoak.cs`, `WarSoakGates.cs` | **extended:** intensity, purveyance when short, the sutler's market buying, flight and return |
| `Combat/Abstract/CountBattle.cs`, `CountTables.cs`, `tools/Ravensguard.Calibrate` | **extended:** the per-pair cascade; scenarios E to I |
| `Combat/Mounted` kicks (CV 4.6) only in formation fights | **everywhere**, by D2 |

## 3. Rules

### 3.1 Campaign intensity (WT-1)
- A realm at war raises a seasonal host in a campaign season (spring to autumn) when its treasury holds **30 days of the host's payroll** and its war score is not winning by more than 300. Today the brain waits for more.
- A realm that cannot raise a host sends its warbands to raid each war season.
- **Gate:** 1.2 hosts or more a realm a war year (the soak measured 7 hosts in 9 war years over two worlds).

### 3.2 AI purveyance when short (WT-1)
- When a host's supply days fall under **10** and the depot and market steps of the supply ladder are short, an AI lord purveys by the Phase 7 rule (grain above 30 days of a household's need, at three quarters of the price, by a tally due in 30 days).
- A cruel or poor lord purveys at 7 days' shortfall rather than 10 (his traits).

### 3.3 Bread on march routes (WT-1)
- A host's sutlers **buy each day** in the nearest market within 8 km for the need its baggage does not cover. That demand enters the market's price solver like any buyer.
- Foraged grain leaves the village's stock (as today) and that village stops selling for the season.
- **Gate (AW 4.6):** +15% or more in 70% of route settlement-seasons, back within 10% two seasons after.

### 3.4 War flight and return (WT-1)
- **Flight:** each season, each household in a settlement foraged, raided, besieged or sacked that season rolls on 0x880 (keyed on the household id and season). The chance a season, by the worst event:

| Event | Chance (pm) |
|---|---|
| foraged | 50 |
| raided | 150 |
| besieged | 300 |
| sacked | 600 |

  The poorest third of households doubles the chance (capped at 900).
- They go as migrants with cause `WarFlight` to the nearest settlement of the same realm beyond 10 km that no host is within 8 km of, through `Demography.Depart` and `Arrive` (counted at both ends).
- **Return:** after peace, or after 2 seasons with no host within 8 km, each fled household rolls 0x881 at **500 pm a season** if its house stands (else 100 pm). A return is `WarReturn`.
- **Emptied borders** and **recovery** follow from flight, deaths and return; their gates are AW 4.6's (10% relative loss; 95% back in 15 years).

### 3.5 Calibration (WT-2)
- **The per-pair cascade (AW-10):** when a class routs, the morale shock falls on the classes engaged with the **same enemy block**, not the whole side. The count tables are refitted (`--count fit`) over the 40 scenarios.
- **AW-C:** scenario E (the wider line's wrap), G's duration and wound mixes, H (stakes, longbows and men-at-arms against knights), I (2,000 v 2,000, nightly, 50 seeds); AW-5 (the human hand and foot at 1.44% and 2.16%); AW-6 (plate against plate in 10 to 20 s).
- **AB3's bound** stays at 18 (the user, 26 Nov) and may only fall.
- **If a gate still misses** after the part: D11 decides. Recommended: ship it as a named ratchet in the review, which can only tighten.

### 3.6 Kicks everywhere (WT-3, by D2)
- CV 4.6's rule applies in every fight: a war-trained horse (`trained_pm` >= 900) or a panicked one (panic >= 600) kicks a foe within 1.2 m in its rear 90 degrees, at most every 3 s.
- MT-13's pinned hash moves; it is re-pinned with the reason beside it. No other golden should move.

### 3.7 Integer math notes
- Flight chances are permille integers; doubling is `min(900, 2 x p)`.
- Treasury cover is `treasury_mites >= 30 x payroll_mites_a_day`, in `long`.
- The cascade's shock uses the Phase 7 morale integers (pm).

## 4. Saves
`armies` gains `Fled` rows (household, from, to, season) as an additive key. Count tables are content. No new section.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| WT-1 | Intensity | a realm with 30 days of payroll raises a host in a campaign season; with 29 it raids |
| WT-2 | Purveyance when short | an AI host at 9 supply days with the market dry purveys; tallies and grievances as Phase 7 |
| WT-3 | Sutlers | a host's daily buying raises the route market's bread price; coin exact |
| WT-4 | Flight and return | a sacked village of 100 households sends about 60 (±15 over 20 seeds), the poor first; after peace about half a season come back; people exact |
| WT-5 | The soak | the ten-world nightly passes every AW 4.6 gate by name (OV-5); AW-15 still green |
| WT-6 | AW-10 | the count battle within the gates on win rate, casualties and duration in 40 scenarios, or the D11 ratchet as measured |
| WT-7 | AW-C E and G | E's wrap and G's duration and wound mixes within the AW-4 bands |
| WT-8 | AW-C H and I | H within bands; I's 2,000 v 2,000 in 45 ms with the bands over 50 seeds (nightly) |
| WT-9 | AW-5, AW-6, AB3 | hand 1.44%, foot 2.16%; plate duration 10 to 20 s; AB3's gaps at 18 or fewer |
| WT-10 | Kicks on the road | a war horse in a road fight kicks a man 1 m behind at most once each 3 s |
| WT-11 | Calm horses | an untrained calm horse never kicks |
| WT-12 | One re-pin | only MT-13's hash moves, with its reason |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-WT-1 war's toll | intensity, purveyance when short, sutlers, flight and return; the nightly | WT-1 to WT-5 | 1.2 |
| P8-WT-2 calibration | the cascade, AW-C, AW-5, AW-6, AB3 | WT-6 to WT-9 | 0.9 |
| P8-WT-3 kicks everywhere | CV 4.6 in every fight, the re-pin | WT-10 to WT-12 | 0.2 |

WT-1 starts in step 1 so the nightly can be tuned for months. Each tuning change is logged in `docs/reviews/phase-8.md` with its effect on the gates.
