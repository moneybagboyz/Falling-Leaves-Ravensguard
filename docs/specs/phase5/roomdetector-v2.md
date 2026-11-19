# RoomDetector v2: roofs, incremental detection, dwellings, workshops, storage and room value

Plan refs: §5 Phase 5 ("RoomDetector, full version"), §2.9 (rooms recognised by the furniture inside; blueprints declare room purposes; contents come from who lives there; CI checks that the detector recognises each room as the type it is meant to be), §4A.2 (housing capacity is beds in dwellings recognised by `RoomDetector`: cottage 4-7, longhouse 6-10, burgage townhouse 6-12; overcrowding gives health and mood penalties; a granary is storage volume; building levels become extra workstations), §3g (the workstation's qualities are supplied by placed furniture; nearby furniture is used automatically).

## 1. Purpose
v1 (`Sim/Building/RoomDetector.cs`) flood-fills each level of a whole `BuildingGrid`, decides `Enclosed`, counts tags and beds, finds doors and matches room types by priority. It runs on blueprints. v2 runs on the **live world** after every built or destroyed piece, and answers the questions the rest of Phase 5 asks: is this room under a roof, is this building a dwelling and of what kind, how many beds, which workstations does this workshop hold, how much can this barn store, how fine is this hall. v1's results for every existing blueprint stay the same (golden RD-1).

## 2. Dependencies
| Needs | From |
|---|---|
| v1 detector: `DetectedRoom` (`Cells`, `Enclosed`, `Tags`, `Beds`, `Doors`, `Matches`, `Primary`, `DeclaredPurposes`), `RoomTypeDef` (`Purpose`, `Priority`, `Enclosed`, `MinArea`, `MaxArea`, `Requires`) | exists |
| Structures and piece changes | `build-mode-and-construction.md` 3.3, 4.10 |
| `PieceKind.Roof`, `Beam` | build spec |
| Material base value per kg (room value), culture styles | exists |
| `craft_station` rows (pieces -> provided qualities) | exists (Phase 3) |
New: `Sim/Building/RoomDetectorV2.cs` (wraps v1's flood fill), `BuildingFacts` (the per-structure summary), content additions to `build/rooms.jsonc` (about 45 room types).

## 3. Data

### 3.1 `RoomTypeDef` additions (append-only fields, defaults keep v1 behaviour)
| Field | Type | Default | Meaning |
|---|---|---|---|
| `roofed` | bool | true for enclosed types | the room must be `Indoor` (4.1) |
| `min_value` | int (mites per m2) | 0 | room value floor (4.5): a gentry hall needs Fine |
| `storage_kind` | string | none | `general`, `grain`, `hay`, `cellar`, `cold`: storage volume use (4.4) |
| `dwelling_part` | bool | false | counts toward a dwelling (sleeping, hearth, solar, hall) |
New room types (to about 45): `granary` (grain bin x2, roofed, `storage_kind: grain`), `barn` (hay stack or stall x2), `shop` (shop counter), `smithy` already exists as `workshop_smithing`, `wheelwright_yard` (`station:wheelwright`, not enclosed), `slipway` (`station:slipway`, not enclosed, must touch water), `stable_inn`, `byre`, `loft` (z >= 1, under a roof, no hearth), `taproom` (table x2 + cask rack), `guest_room`, `kitchen_large`, `bakehouse`, `malt_house`, `bloomery_shed`, `mint` (Phase 6 content), `yard` (enclosed by walls or fences, unroofed: `roofed: false`).

### 3.2 `DetectedRoomV2` (extends `DetectedRoom`)
`Indoor` (bool), `RoofCells` / `Cells`, `ValuePerM2` (mites), `ValueTier` (`Mean, Plain, Decent, Fine, Grand`), `Stations` (station id -> count), `StorageL` (litres), `StorageKind`, `Building` (structure id), `Level` (z), `Owner` (deed holder), `Assigned` (bed -> person, from the household).

### 3.3 `BuildingFacts` (one per structure, cached; what other systems read)
`{ Structure, IsDwelling, DwellingType (None, Cottage, Longhouse, Burgage, Manor, Tower), Beds, HearthRooms, Workshops (room -> station counts), StorageL by kind, Rooms[], BestValueTier, Entrances (exterior doors), Levels }`.

## 4. Rules

### 4.1 Indoor (roofs)
A room cell is **roofed** when the cell directly above (z + 1) holds a floor-bearing piece or a `Roof` piece, or the room is at z < 0 (underground). A room is `Indoor` when it is `Enclosed` and every cell is roofed. Indoor rooms: rain does not reach (exposure and wetness use it), stored goods are dry (rust and rot, items spec 4.4), fire and smoke rise (Phase 6). An enclosed unroofed area is a `yard`; an unroofed house under construction is a `yard` until its roof is built, so a household cannot move in.

### 4.2 Incremental detection
When a piece is built, removed or collapses, only the **structure** that holds it is re-detected: its bounding box plus a 1-cell margin, every level it spans, plus the level above for roofing. Structures are joined when a new piece touches two (a shared wall), split when a removal disconnects them (structure ids: the lower id survives, the new part takes a new id and its own structure deed rows follow the land rule: same holder). Results are cached in `BuildingFacts`; a v1 full detection of a blueprint is the same function run over the whole grid. Doors, windows and arches stay boundaries (v1); posts stay solid but do not seal (v1).

### 4.3 Dwellings and beds
A structure **is a dwelling** when it has at least one `Indoor` room whose primary purpose is `hearth` (or a `hall` with a hearth), at least 2 beds in `Indoor` rooms, and an exterior door. The type follows the footprint and beds, as `BlueprintValidator` already checks for blueprints: **cottage** (1-2 storeys, footprint <= 60 m2, 4-7 beds), **longhouse** (length >= 2.5 x width, a byre or stable room in the same structure, 6-10 beds), **burgage** (on a town lot, frontage <= 12 m, 6-12 beds), **manor** (a `great_hall` or `hall` with `high_seat`), **tower** (3+ levels, footprint <= 64 m2). Beds count only in `Indoor` sleeping, solar, loft and guest rooms. Housing capacity (marriage's `FreeDwelling`, overcrowding at > 130% of beds) reads `BuildingFacts.Beds`; a dwelling the hero built is offered to NPC households when he leases it (land spec 4.3).

### 4.4 Workshops, stations and storage
- **Workshops:** each room whose primary type is `work:<x>` lists its stations (the `craft_station` rows whose `pieces` are inside). A business's station count is the sum over its recognised workshop rooms (plan: building levels become extra workstations, never free output). A station outside any recognised workshop room (a forge in the street) still works for crafting by hand but does not count for a business (businesses spec 4.1).
- **Nearby furniture for crafting:** the crafting screen reads stations, containers and tools in the hero's current room and within 6 m (fixes the Phase 3 "stations in reach are not read from the world").
- **Storage:** `StorageL = area_m2 x 2,000 L x 500 pm` for rooms with `storage_kind` (a 6 x 8 barn: 48,000 L); a `grain` room halves grain spoilage; `cellar` (z < 0) and `cold` keep ale and cheese at the Phase 2 cellar rates; `hay` keeps hay dry. Mass cap per land spec 4.5.

### 4.5 Room value and tier
`ValuePerM2 = (sum of the room's furniture and fixture piece values + its floor and wall pieces' material value / 2) / area`, values at the book's reference prices (materials spec). Tiers: `Mean < 30`, `Plain 30-99`, `Decent 100-299`, `Fine 300-999`, `Grand >= 1,000` mites per m2. Uses: room types with `min_value` (a `great_hall` needs Fine), a gentry household's mood (a mean house: -5 mood a day), the plan library's wealth label of a player-saved blueprint, and inn prices (Phase 6). No randomness.

### 4.6 Purpose gaps and furnishing (contents come from who lives there)
For each room with a declared purpose (from a blueprint or a player's saved plan), v2 reports `PurposeGaps` (tags missing to satisfy it: "needs a bed"). The household furnisher (the chunk generator's `Furnish` step, `ChunkGenerator.Buildings.cs`, lifted into a runtime `HouseholdFurnisher`) fills gaps for NPC households that move in, from the household's occupation, wealth and goods (Hedda's tanning racks), as real items bought or moved from the old home; the hero's own rooms are never furnished for him.

## 5. Tick placement and determinism
Barrier only (after piece finish, removal or collapse): re-detect the structure, update `BuildingFacts`, raise `RoomChanged(structure)` for housing, businesses and storage. At load, facts are rebuilt for every structure in the save and for generated buildings on first need (lazy). Pure and deterministic: flood fill order by `(z, y, x)`, ties by room type id (ordinal), as v1. No random stream (0x520 reserved, asserted never drawn).

## 6. UI
Build mode shows room outlines when a room is recognised (a thin line in the room type's colour and its name at the centre: "Smithy (2 forges, 1 anvil)", "Bedroom, 2 beds", "Yard: no roof"); `PurposeGaps` show as an amber note on the room. Q on a floor cell > Room: type, tier, beds, storage, owner. Readable at 960 x 540 (labels 10 px, hidden below zoom 50%).

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| RD-1 | v1 parity: every content blueprint | v2 results equal v1's for `Enclosed`, `Tags`, `Beds`, `Doors`, `Primary` (roofing aside); CI's existing blueprint checks pass |
| RD-2 | Roofs: `cottage_two_room` with its loft | ground rooms `Indoor` only where the loft or roof covers them; with the roof ghosts unbuilt the hearth room is a `yard` |
| RD-3 | Incremental equals full: 1,000 random build and remove steps on a 20 x 20 x 3 area | after each, incremental results equal a full detection |
| RD-4 | Join and split: build a shared wall between two sheds, then remove it | one structure, then two; deed rows follow; ids lower-survives |
| RD-5 | Dwelling: `test_cottage` finished with a hearth and 2 beds | `IsDwelling`, Cottage, 2 beds; without the roof: not a dwelling |
| RD-6 | Longhouse: `longhouse` blueprint | Longhouse, beds within 6-10 |
| RD-7 | Workshop count: a smithy room with 2 forges and 1 anvil | `Stations` {forge 2, anvil 1}; a forge outside the room is not counted |
| RD-8 | Storage: barn 6 x 8 m | 48,000 L, kind hay |
| RD-9 | Value: a 5 x 6 hall with table, high seat, flagstone floor, stone walls | `ValuePerM2` and tier computed by the formula (hand-checked number in the test); `great_hall` needs Fine |
| RD-10 | Purpose gaps: a `sleeping` room with no bed | gap `bed`; an NPC household moving in buys or brings one |
| RD-11 | Collapse re-detection: BC-13's floor falls | rooms below lose `Indoor`; facts updated in the same barrier |
| RD-12 | Determinism | identical `BuildingFacts` hash on Linux and Windows for the RD-3 sequence |

## 8. Open questions (recommended defaults)
1. **Room value from material prices** moves when prices drift: use the reference price, not the market price (default), so a hall does not become "Mean" in a boom.
2. **Partial roofs:** a room is `Indoor` only when every cell is roofed (default); alternative 90%.

## 9. Budgets
Incremental detection of a 12 x 20 x 3 building <= 0.3 ms; a town-wide rebuild at load (only saved structures) <= 30 ms; `BuildingFacts` 200 B per structure. 0 allocations on the cached path.

## Notes (P5 step 1, A3: what was built)
**Code.** `Sim/Building/RoomWorld.cs` (+ `.Detect.cs`), `RoomDetectorV2.cs` (types), tests `RoomDetectorV2Tests.cs` (RD-1 to RD-12 and extras). v1 `RoomDetector` is untouched except one line in `Satisfies` (a `roofed: false` type never matches in v1, so the yard is v2's). `BuildingLibrary.NewWorld()` gives an empty live world with the library's rules; `MaterialMitesPerKg` and `PieceStation` (craft_station rows) are new library properties.
**API for step 2.** `RoomWorld` holds the sparse cells of a region (structure piece, fixture, declared purpose per `(x, y, z)`). `Apply(edits)` (or `SetPiece`, `SetFixture`, `SetPurpose`) changes cells and returns a `RoomChange`: structure events (`Created`, `Joined(survivor, other)`, `Split(old, new)`, `Removed`), rooms added and removed, and the touched structure ids (the `RoomChanged(structure)` the spec's barrier raises). `Facts(structure)` and `FactsAt(x, y, z)` return the cached `BuildingFacts`; `RoofOver(roofPiece, structures?)` is the "Roof over" tool; `LoadBlueprint(grid, ox, oy, roofed)` writes a finished plan; `RebuildStructures()` + `RebuildAll()` are the load path and the full detection; `FullTwin()` and `Dump()` are what RD-3 compares; `FactsHash()` is the `RoomHash` for the daily trail. `NextStructureId` is the counter the build step saves so ids stay stable across a load.
**Incremental rule.** An edit re-floods the rooms within 2 cells of it on its level (walls, then the exterior flag of a door beside the wall) plus the room directly below (roofing). Structures are the 6-connected parts of non-ground pieces: a join relabels to the lowest id, a split keeps the id on the part holding the lowest `(z, y, x)` cell and gives the other part a new id (the land rule follows the events). Measured on a 12 x 20 x 3 building: median 0.27 ms for an edit plus fresh facts (budget 0.3 ms; the test allows 1.5 ms for a loaded machine).
**Content.** `PieceKind.Roof` appended, with `roof_thatch` and `roof_slate`; optional `kg` on build pieces (the room-value mass; defaults per kind in `RoomValue.DefaultKg`; stone walls, flagstone, table, high seat set explicitly); `RoomTypeDef` gained `roofed` (null: Indoor when enclosed; false: enclosed but unroofed), `min_value`, `storage_kind`, `dwelling_part`; new room types `yard`, `granary`, `barn`, `malt_house`, `taproom`, `kitchen_large` (31 to 37); `great_hall` needs Fine. Not built, left to content scaling: wheelwright yard, slipway (needs a water test: `RoomWorld.WaterAt` is the hook), stable inn, byre as its own type, loft, guest room, bakehouse, bloomery shed, mint.
**Decisions where the spec was open.**
- A cell is roofed when the cell above holds any structure piece (a floor, a roof, or a wall of the level above), not only floors and roofs: three blueprints put walls of the upper level over lower rooms, and those rooms are covered. Underground (z < 0) counts as roofed, as written.
- A blueprint is a finished building: `LoadBlueprint(roofed: true)` lays roofs, so every content blueprint's enclosed rooms are `Indoor` and RD-1 parity with v1 holds room by room (cells, enclosure, tags, beds, doors, primary type, declared purposes). With `roofed: false` the same plan shows its yard (RD-2).
- Beds count only in `Indoor` rooms whose primary purpose is sleeping, solar, loft or guest, as the spec says. A one-room hovel with a hearth and pallets is therefore not a dwelling; `BedCapacity` (placed beds plus the free declared floor of sleeping rooms, 3 cells a bed, what `BlueprintValidator` counts) is there for plans and the hovel question is open for step 2.
- `DwellingKind` (not `DwellingType`, which is the region plan's enum): manor (a great hall, or a hall with a high seat), tower (3 or more storeys above ground, footprint 64 m2 or less), longhouse (length 2.5 x width or more and a livestock room, declared or recognised), burgage (the `OnTownLot` hook says so and the frontage is 12 m or less), otherwise cottage. Bed ranges (4-7, 6-10, 6-12) stay a blueprint check, as before, because RD-5 wants a 2-bed cottage to be a Cottage.
- Room value: `(furniture and workstations + (floor pieces + the wall, window, door and post pieces touching the room) / 2) / area`, piece value = `kg` x the material's reference price in mites per kg (`value_per_kg` x 100, never the market price). With the numbers in content the RD-9 hall (30 m2, flagstone, stone walls, table, high seat) is 730 mites per m2, Fine; the same hall in wattle and planks is a plain `hall`. Poor plans come out Mean to Plain, middling Decent, stone plans Fine; tune `kg` rows, not code.
- Storage: 1,000 L per m2 (the spec's 48,000 L barn), kind from the primary room type's `storage_kind`.
- Purpose gaps: for a room with exactly one declared purpose the detector names what the nearest type lacks (`bed`, `roof`, `walls`, `area`, `value`, a tag).
- Stations come from `craft_station` rows (forge, anvil, hearth, oven ...), counted per room; a business reads `WorkshopStations()` (only rooms whose primary type is `work:*`), so a forge in the street counts for nobody (RD-7).
- RD-12: the hash is integer-only and stable; the test pins the value of the 1,000-step sequence.
**Overlay.** Dev console `rooms` / `rooms off`, or `--shot-rooms`: the street view runs the detector over the built cells within 56 m of the hero (roofs laid), outlines each room in a colour by purpose with its name, beds and stations in 10 px text. `--selftest` checks the cottage against v1.
**Left for later.** The runtime `HouseholdFurnisher` (RD-10's "an NPC household moving in buys or brings a bed": only the gap is reported), `Owner` and `Assigned` (need deeds and households), the build-mode Q > Room panel, culture-style effects on value, a `Loft` type with `min_z`, and the save of structure ids (step 2 owns the structure records; this part keeps nothing in the save and rebuilds from cells at load).
