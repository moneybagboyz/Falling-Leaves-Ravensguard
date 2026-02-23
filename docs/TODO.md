# Ravensguard — TODO (FMG Gap Items)

Missing systems identified by comparing Azgaar's Fantasy Map Generator feature set
against the current implementation. Items are ordered by dependency — earlier items
unblock later ones.

---

## 1. Name Generation

**What FMG does**: Markov-chain syllable training sets (`nameBases`) keyed per culture.
All burgs, rivers, provinces, states, and rulers get procedurally generated names from
the training set of their culture.

**What we need**:
- `scripts/world/name_generator.gd` — Markov bigram model trained on a name corpus
- At minimum one corpus per expansion type: `generic`, `highland`, `naval`, `nomadic`
- Hook into `ProvinceGenerator` (province names), `place_settlements` (settlement names),
  `FactionGenerator` (faction + ruler names), river naming pass in `Hydrology`
- Settlement names currently hardcoded placeholders — replace with generated names

---

## 2. Cultures

**What FMG does**: Culture zones spread from seed cells outward using `expansionism`
multiplier and biome `cost`. Each culture has a `type` (Generic, Nomadic, Naval,
Highland, Hunting) that drives expansion AND is inherited by states that spawn within it.
Cultures own a `nameBase` — so all names in a region share a linguistic flavour.

**What we need**:
- `scripts/world/culture_generator.gd` — Poisson seeds + Dijkstra flood-fill (same
  pattern as provinces but culture-specific terrain costs)
- `WorldData.culture_id: Array` — per-tile culture index (parallel to `province_id`)
- `Culture` resource: `id`, `name`, `type` (matches `expansion_type` on Faction),
  `name_base: String`, `expansionism: float`, `center: Vector2i`
- Cultures generated BEFORE provinces so province capitals inherit culture → faction
  `expansion_type` and name corpus are assigned automatically at world gen

---

## 3. Religions

**What FMG does**: Religions spread from temple/holy-site seeds; type is Folk, Organized,
Heresy, or Cult. `expansion` is either `culture` (stays within one culture) or `global`
(spreads across all). Religions drive population happiness, war justification between
factions, and faction diplomatic posture.

**What we need**:
- `scripts/world/religion_generator.gd` — seed placement weighted by settlement score,
  flood-fill with culture boundary penalty for non-global religions
- `WorldData.religion_id: Array` — per-tile religion index
- `Religion` resource: `id`, `name`, `type`, `form`, `deity`, `expansion`, `culture`,
  `expansionism: float`, `center: Vector2i`
- Faction AI uses shared religion as a positive `adjust_relation()` bonus and different
  state religion as war justification weight
- Settlement `happiness` modifier: +10 if majority religion matches province religion,
  -15 if under religious persecution

---

## 4. Zones / Markers (World-Level POI Layer)

**What FMG does**: Markers are named POIs on the world map (ruins, shrines, dungeons,
mines, camps). Zones are named special-rule regions (danger zone, sacred land, trade
territory) with hatching-pattern overlays.

**What we need**:
- `scripts/world/poi_generator.gd` — scatters world-level POIs after terrain generation
  using RegionFeature as seed: `MINE_ENTRANCE` → mine marker, `RUINS` → ruin marker,
  `CAMP` → bandit camp, `FORD` → river crossing waypoint
- `WorldData.poi_list: Array[POIData]` — unsorted array of POI objects
- `POIData` resource: `id`, `type`, `world_pos: Vector2i`, `name: String`, `notes: String`
- Zones: named regions (Sacred Forest, Blighted Wastes, Pirate Coast) placed around
  POI clusters; stored as `ZoneData` with `cell_list: Array[Vector2i]` and `color`
- Render in a new ZONES/MARKERS view mode in `map_renderer.gd`
- Local map generator should check for POI at world tile and add appropriate feature
  overlay (dungeon entrance, shrine building, etc.)

---

## 5. Sea Routes

**What FMG does**: A separate route group (`searoutes`) connects coastal burgs via
ocean/coast cells rather than land. Naval culture states use sea routes exclusively for
trade and army movement.

**What we need**:
- Extend `RoadGenerator` with a `generate_sea_routes(data, settlements)` method:
  - Source = all settlements with `TerrainType.COAST` within 2 tiles
  - Dijkstra over ocean/coastal tiles only (movement cost inverted: water=1, land=INF)
  - Output stored in `data.sea_route_network: Dictionary` (same format as `road_network`)
- `Settlement.sea_port: bool` — true if within 2 tiles of coast and connected to sea net
- Faction `expansion_type == "naval"` uses `sea_route_network` for army pathfinding
  instead of land `road_network`
- Connectivity rate for naval settlements counts sea-route-connected settlements, not
  land-connected ones

---

## 6. Street-Level Layout in Local Map

**What FMG does**: Each burg links to the Medieval Fantasy City Generator (MFCG) via a
seed value for street-level layout — districts, walls, market square, temple, castle.

**What we need** (replacing MFCG with our own Local map tier):
- `LocalMapGenerator` currently fills a settlement tile with the surrounding biome.
  It needs a `_generate_settlement_layout()` method that stamps a real town grid.
- Layout rules by tier:
  - **Hamlet** (tier 1): 2–3 house clusters around a well, dirt paths, no walls
  - **Village** (tier 2): central market cross, inn, chapel, palisade wall, 1–2 farm fields
  - **Town** (tier 3): stone walls, keep or citadel, market square, guild hall, temple,
    residential blocks
  - **Metropolis** (tier 4): concentric wall rings, palace ward, merchant quarter,
    slum district, harbour / grand temple
- Building footprints stored in `settlement.buildings: Array[BuildingData]` with
  `local_pos: Vector2i` and `type: BuildingType`
- `LocalMapData.feature[gy][gx]` uses `SettlementFeature` values: `HOUSE`, `KEEP`,
  `MARKET`, `TEMPLE`, `GUILD`, `WALL`, `GATE`, `ROAD`, `FIELD`, `WELL`
- Roads inside the layout align to the nearest `road_network` entry tile so the
  internal street grid connects visually to the world road at the local map border
- Settlement layout is generated once at world gen and cached; re-generated on
  building upgrade or tier change

---

## Dependency Order

```
NameGenerator
    └── CultureGenerator  (needs name corpora)
            └── ReligionGenerator  (needs culture zones)
                    └── FactionGenerator updates  (inherits culture → expansion_type)
                            └── SeaRoutes  (needs factions/settlements finalised)
POIGenerator  (independent, runs after terrain)
    └── ZoneGenerator  (clusters POIs into zones)
StreetLayout  (needs settlement.tier + buildings finalised by Phase 2 Economy)
```
