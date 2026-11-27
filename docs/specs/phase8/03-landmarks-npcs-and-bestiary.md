# Landmarks, unique NPCs and the bestiary: the authored world at 1.0

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §1A and §3.14 (authored content only ever placed into generated worlds, each because something happened there), §3.4 (unique NPCs with 20 to 50 authored lines), §3b (rare monsters: 3 to 6 kinds from northern folklore, 1 to 3 lairs a region, always deep, tied to a legend, rumours until met), §5 Phase 8 (about 40 landmarks, about 30 unique NPCs, the full bestiary, more site templates).
Carried in: per-culture artefact names (phase-5); cellars in library plans and a walkable new mine (phase-6 overview); weather's torch, daylight and fire fields (phase-4). Decision D8 sets the monster count.

## 1. Goals
1. About 40 landmarks (12 today), each placed for a reason the chronicle gives.
2. About 30 unique NPCs (8 mentors today) who live and die like anyone.
3. Five monster kinds (2 today) through the one anatomy resolver, and 8 more site templates (26 in all).
4. Library worlds keep their bytes: new placement is a new placer version.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `content/base/landmarks/landmarks.jsonc` (12), `WorldGen/Region/LandmarkPlacer.cs` | **extended:** 28 landmarks; placer v2 with caps and spacing |
| `content/base/dialogue/unique/*.jsonc` (8 mentors) | **extended:** 22 NPCs; content `npcs/unique.jsonc` (placement and traits) |
| `content/base/sites/monsters.jsonc` (draugr, cave beast), `site_templates.jsonc` (18) | **extended:** 3 monsters with body plans and brains; 8 templates; lair rules |
| `Weather` torch and daylight fields read by nobody | **read:** carried torches and outdoor light |

## 3. Rules

### 3.1 Landmarks (placer v2)
- Each landmark keeps today's fields (`attach_events`, `biomes`, settlement distances, `lore`, `props`) and may add `site` (a template it opens on) and `keeper` (a unique NPC).
- **Caps:** 2 to 5 landmarks a region; at least 3 km between two; no template twice in one region.
- **Order:** candidates by template id, then cell; the pick is 0x820 keyed on the region id and template id.
- **Old worlds** stay on placer v1. New worlds use v2. The generated-world golden for v2 is pinned apart; v1's never moves.
- New kinds include: a drowned village, a gallows hill, a saint's well, a battle cairn, a lord's ruined hall, a boundary oak, a plague pit, a smugglers' cove, a holy island, a beacon hill.

### 3.2 Unique NPCs
- Each has: a placement rule (a landmark, a settlement kind and size, or a role), authored traits and skills, a schedule, 20 to 50 lines of dialogue, and one optional contract.
- They are **Person records** made at region generation (0x828 keyed on the region and NPC id). At most one of each per world.
- **No plot armour:** they age, fight and die. A dead NPC's lines are never used again; their landmark's lore has a "since the death of {person}" variant.
- The 22 new ones are keepers (the ferryman of a holy island, a barrow warden), wanderers (a pilgrim, a travelling surgeon, a disgraced knight), rivals for each origin, and named advisors to realm rulers.
- **Per-culture artefact names** (phase-5): name tables by culture for named weapons, relics and heirlooms.

### 3.3 The bestiary (D8 recommends five)
Every monster is a body plan, materials, a brain profile and lair rules. No special combat rule.

| Kind | Mass | Lives | What makes it a test |
|---|---|---|---|
| draugr (today) | 70 kg | barrows | no blood, no pain: limbs or head must go |
| cave beast (today) | 260 kg | deep caves | thick hide, claws |
| **bog-thing** | 140 kg | marsh sites | grapples and drags into water cells; the breath rule drowns |
| **barghest** | 90 kg | haunted ruins | 9 m/s; at night a fear shock of -150 pm morale within 8 m, once a fight |
| **lindworm** | 1,800 kg | tier-3 deep caves only | 9 m long in 6 body segments; bite and constrict (a grapple at 6,000 N on the chest); at most one a world region |

- **Lairs:** 1 to 3 a region in all; always at level -2 or deeper; each tied to a legend event in the history (a `monster_lair` cause). Placement is 0x830 keyed on the region and site ids.
- **Rumours first:** a lair exists as a rumour (`lair_rumoured`) until a person sees the monster. The Codex page fills in three stages: rumoured, met, slain.
- **Slain:** `monster_slain` with the killers named; renown +40 district for the hero (+80 for a lindworm).
- Monster brains use 0x831 keyed on the fight id and tick.

### 3.4 Site templates
- Eight new ones: the deep barrow (three levels, tier 3), a sunken chapel, a flooded mine, a bog island, a battlefield crypt, a robbers' hold, a hermit's cave, a sea cave.
- **Cellars in library plans** (phase-6): a stair down makes a cellar level under the plan, not floor.
- **A new mine as a walkable site** (phase-6): a prospected deposit opened by the hero becomes a site with a level per 10 m of depth.

### 3.5 Torches, daylight and fire (phase-4 weather fields)
- **Torches:** a carried torch outdoors goes out with a chance of `rain_pm / 20` pm a game minute (heavy rain, 1,000 pm, is 50 pm a minute), and 0 under a roof or below ground.
- **Daylight:** outdoor light at noon is `1000 - cloud_pm x 4 / 10` pm (full overcast is 600 pm); night and underground are as today.
- **Fire:** P8-LM-3 checks that Phase 6 fire reads rain (plan §6's test) and closes the row.

### 3.6 Integer math notes
- Spacing uses squared metres in `long`; no square root.
- Monster grapple force is in newtons as `int`, through the Phase 4 grapple rule.

## 4. Saves
- Additive keys in `sites`: `LairKind`, `Legend` (entry id), `Met` (none, rumoured, met, slain).
- Unique NPCs are ordinary people rows with an `Authored` id key.
- Content is mapped by name (`ContentRemap`).

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| LM-1 | Landmarks load | 40 templates; every event, biome, prop and keeper resolves |
| LM-2 | Placer v2 | 10 seeds: 2 to 5 a region, 3 km apart, each attached landmark's event exists |
| LM-3 | Old worlds | a v1 library world loads with its landmark set and generated hashes unchanged |
| LM-4 | Lore | every lore token resolves for 200 placements; no empty `{}` left |
| LM-5 | NPCs load | 30 NPCs; each has 20 to 50 lines and a placement rule that fits at least one seed of 10 |
| LM-6 | One each | no unique NPC twice in a world |
| LM-7 | Mortal | a unique NPC killed in a fight leaves a corpse, an estate and the "since the death" lore |
| LM-8 | Artefact names | 8 cultures each name a relic and a weapon from their own tables |
| LM-9 | Monsters load | 5 body plans pass the anatomy checks; every attack resolves |
| LM-10 | Bog-thing | a man grabbed in a water cell drowns by the breath rule unless freed |
| LM-11 | Barghest | at night allies within 8 m lose 150 pm morale once; by day none |
| LM-12 | Lindworm | a lindworm in a crawl kills an unarmoured man in one constrict; it is only placed at tier 3 |
| LM-13 | Lairs | 10 seeds: 1 to 3 lairs a region, all at -2 or deeper, each with a legend |
| LM-14 | Torch and daylight | heavy rain puts out an outdoor torch at 50 pm a minute; noon under full cloud is 600 pm |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-LM-1 landmarks | placer v2, 28 templates, lore variants | LM-1 to LM-4 | 0.8 |
| P8-LM-2 unique NPCs | 22 NPCs, dialogue, placement, artefact names | LM-5 to LM-8 | 0.8 |
| P8-LM-3 bestiary and sites | 3 monsters, lairs, 8 templates, cellars, walkable mines, torches and daylight | LM-9 to LM-14 | 1.2 |

Art for the monsters is P8-PP-1. Dialogue is drafted here and read by the user before the freeze (LC-1).
