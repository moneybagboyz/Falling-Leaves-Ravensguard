# Phase 5: prospecting and extraction businesses

Status: design (decided with the user, Nov 2026). Plan references:
- §4A.1: resources are physical stocks; ore bodies of 2,000 to 20,000 t; seasonal extraction; businesses are buildings with workstations, an owner and a cash box.
- §3j: the crown's mint and mineral rights.
- §5 Phase 5: land and businesses.

The user asked for two things:
- the player can **prospect** to find what resources a place has;
- the player can **build businesses where the resources are** (mines, quarries, timber mills and the like).

## 1. Purpose
Today the world already holds the resources:
- the Phase 2 `ResourceLayer` keeps per-tile stocks of wood, stone, clay, sand, peat, salt, fish, game, forage and pasture;
- mines exist only where world generation placed a mine site (`MineWorks.FromSite`, with ore bodies from the mineral-vein layer).

What is missing is **knowledge** and **founding**. A player cannot learn what lies under a hillside, and cannot open a new mine, quarry or mill where the resource is.

This spec adds:
- prospecting (what you know, and how you learn it);
- extraction businesses founded by anyone with the right to the land;
- mineral rights, which are not the same as owning the surface.

## 2. Dependencies
- Phase 2: `ResourceLayer`, `Extraction`, `MineWorks`, the mineral-vein and strata layers from the continent and region generators, the soil fertility layer.
- Phase 4 sites: mines and caves, hazards (flooding, bad air, cave-ins).
- Phase 5:
  - land-and-deeds (deeds, licences, claims, founding charters);
  - build-mode-and-construction (hands-on building of the workings);
  - businesses-stewards-ledger (the business, its cash box, its workers);
  - crafting-full (tools: pick, shovel, pan, auger).

## 3. What a place holds, and what you know about it
- **The truth is already in the sim:** tile stocks (`ResourceLayer`) and ore bodies, which are hidden and have tonnage and grade. Nothing about the truth changes. Prospecting only adds a per-person **knowledge** record.
- **Knowledge** (`ProspectBook`, one row per person × tile × resource): the estimated amount, the estimated grade, a confidence from 0 to 1000 pm, and the game day surveyed.
  - Estimates are the truth plus an error whose spread falls with skill and method.
  - Knowledge goes stale as stocks are worked; regrowing stocks such as wood drift.
- **Surface resources are visible.** Woodland, open stone, clay banks, peat bog, salt flats and fish water show on the map and in the street view. Walking a tile gives a rough estimate at low confidence for free.
- **Hidden resources need a survey.** Ore veins, deep clay, building stone below the soil, and good quarry beds.

## 4. Prospecting
**Who can prospect:** anyone, using the Prospecting skill. It is new; it trains by use and starts from Perception.

**Methods** (each is an action with a duration in game hours; the tools are tool qualities from §3g):

| Method | Tools | Time | What it finds |
|---|---|---|---|
| Walk and look | none | as travel | Surface resources, rough estimate, confidence 200 |
| Read the land | none; skill 20+ | 1 h per acre tile | Rock outcrops, the plants that indicate ore, springs. Hints at a hidden ore body within 200 m (direction only) |
| Pan a stream | pan | 2 h | Ore types upstream in the catchment (from the stream's flow network): which mineral, not where |
| Dig a test pit | shovel, pick | 6 h, 1 m deep | Clay, sand and stone beds under the tile, at good confidence |
| Bore or sink a trial shaft | auger or pick, a helper | 2 days | The ore body under the tile: type, grade and a tonnage estimate (±30% at skill 50, ±10% at skill 90) |
| Hire a prospector | coin | contract | An NPC does the above and hands you a written survey (an item, which can be sold, copied or stolen) |

- **Knowledge is property.** A survey report is an item. Selling one, or keeping a find secret, is a real choice. Rumours of a strike spread through the rumour system, which brings other prospectors and the lord's interest.
- **Old workings and history:** a ruined mine site from history (§1A) hints that its vein may not be worked out.

## 5. Rights: owning land is not owning what is under it
- **Surface rights:** a deed, lease or claim (land-and-deeds) lets you cut wood, dig clay, quarry stone, cut peat and fish **your own water**.
- **Mineral rights:** in most cultures, iron, copper, lead and tin belong to the lord of the land; silver and gold belong to the **crown** (the regalian right, §3j: silver feeds the mint).
  - To open a mine you need a **mining licence** from the right holder: a fee plus a royalty share of the output (default 10%, by culture).
  - You can also hold the rights yourself, through a founding charter or as the lord.
  - Mining without a licence is a crime with witnesses (Phase 6 enforces it; Phase 5 records it).
- **Common rights:** villagers may hold rights to gather wood and graze on common land. Closing a common for your own business causes grievances, which feed the unrest ladder in Phase 6.

## 6. Extraction businesses (founded where the resource is)
Each is a business (businesses-stewards-ledger) whose workplace sits on the resource tiles. It is built hands-on like any building (Kenshi style, as decided), and it works the stock through the existing `Extraction` rates:

| Business | Needs | Built from | Output | Notes |
|---|---|---|---|---|
| Woodcutters' camp | woodland tiles and surface rights | a hut, a saw-horse | logs, poles, bark (for tanners) | coppice regrows (§4A.1); clear-felling turns the tile to waste |
| Sawpit, then a water sawmill | logs on hand; for the mill, a stream with fall | the pit; the mill needs a wheel, a race and a frame | planks, beams | the mill is about 8x a sawpit's output per worker |
| Charcoal clamp | wood and an open site | turf and wood | charcoal | an unattended step (§3g), which can be ruined |
| Quarry | a surveyed stone bed and surface rights | a crane or shear-legs, a track | rough and dressed stone, lime | carting is the cost: stone is heavy, so nearness to a road or river matters |
| Clay pit and kiln | a surveyed clay bed | a pit, a drying shed, a kiln | bricks, tiles, pots | |
| Peat cuttings | bog | none | peat (fuel) | cut in spring, burned in winter (§4A.1) |
| Salt pan | a coastal salt flat | pans, a boiling house | salt | summer only |
| Mine (adit or shaft) | a surveyed ore body and a mining licence | an adit into a hillside, or a shaft with a windlass; timbering | ore | hazards from the Phase 4 sites: flooding (needs pumping or drainage), bad air, cave-ins; it becomes an underground site that can be walked |
| Bloomery or smelter | ore and charcoal | a furnace, bellows | blooms and bars, lead, copper | the chain in §4A.1; silver goes to the mint |

- **Placement rules:** the business's footprint must touch the resource tiles it works. Build mode shows the known resource overlay (from your `ProspectBook`), so you place it on what you know, not on the truth.
- **Exhaustion:** stocks deplete. A mine's output falls as the ore body runs low, and the Ledger shows the remaining estimate with its confidence.
- **Labour and logistics:** extraction needs workers (wages and housing nearby) and carting to market. That is why roads, rivers and a nearby settlement matter, and why a founding charter near a good vein is worth petitioning for.

## 7. Tick placement and determinism
- **Prospecting actions** run on the live tier at 20 Hz while you do them, and abstractly for hired prospectors.
  - Estimates draw from a counter-based stream keyed on (person, tile, method, game day).
  - The truth is never drawn at random after world generation.
- **Extraction** runs in the hourly abstract tick through the existing `Extraction` and `MineWorks` code, extended to businesses the player founded.

New streams in the Phase 5 block:

| Stream | Use |
|---|---|
| 0x5C0 | estimate error |
| 0x5C1 | prospecting hints |
| 0x5C2 | hazards at a new mine |

## 8. UI
- **A resource overlay** on the map and in build mode, showing what *you* know: icons with a confidence shade, and "unsurveyed" hatching.
- **Prospect from the Q menu** (Read the land, Pan, Test pit, Trial shaft) where you stand.
- **Survey reports** as readable items.
- **A licence petition** at the lord's or crown's officer.
- Readable at 960×540.

## 9. Tests (golden)
- **PX-1:** estimates converge on the truth as skill rises: the mean absolute error at skill 90 is under one third of that at skill 20, over 500 seeded surveys.
- **PX-2:** a survey never changes the truth. The world hash is identical before and after 1,000 surveys.
- **PX-3:** panning names only minerals that are really upstream in the catchment.
- **PX-4:** a mine opened without a licence is recorded as unlicensed. A licensed mine pays its royalty to the right holder through the ledger, with coin conserved.
- **PX-5:** an extraction business placed off its resource is refused, and one placed on it extracts at the `Extraction` rates.
- **PX-6:** an ore body depletes, and output falls with it, at the `MineWorks` rates.
- **PX-7:** a survey report item carries its knowledge to whoever reads it.
- **PX-8:** save, load and save is byte-identical with `ProspectBook` rows.

## 10. Open questions (recommended defaults)
- Mining royalty: 10%, varied by culture content.
- Does a hired prospector lie or sell your find? Not in Phase 5; possible in Phase 6 with corrupt officials.
- Should surface resources also need a survey? No; walking past gives a rough estimate.
