# In-Depth Explanation of the Economy in Falling Leaves

The economy in *Falling Leaves* is a multi-layered simulation that connects geography, logistics, urban development, and mercenary management. It is designed to be dynamic, where player actions and world events have tangible effects on prices and the prosperity of settlements.

---

## 1. The Foundation: Production & Geography
The economy is a physical simulation based on land usage and labor allocation. Production is not abstracted into simple "+1 hourly" numbers; it is calculated based on historical agricultural and industrial yields verified in `Globals.gd`.

### 1.1 The Acreage System
Every world map tile represents a significant area of land, divided into **250 Acres** (`ACRES_PER_TILE`).

*   **Arable Acres:** Used for farming.
*   **Forest Acres:** Used for timber and hunting.
*   **Pasture Acres:** Used for livestock and wool.
*   **River/Harbor Slots:** Used for fishing and maritime extraction.
*   **Underground Deposits:** Mines and quarries.

### 1.2 The Three-Field System
Most settlements employ high-medieval crop rotation. This divides **Arable Acres** into:
1.  **Winter Field:** Sown in autumn with wheat or rye.
2.  **Spring Field:** Sown in spring with barley, oats, or legumes.
3.  **Fallow Field:** Left unplanted to recover nutrients. In our simulation, the **Fallow Field** doubles as **Pasture Land**, allowing livestock to graze and fertilize the soil.

**Seed Ratio:** Approximately **20% (1/5th)** of every harvest is automatically reserved as seed for the next cycle, effectively acting as a production overhead (`Globals.SEED_RATIO_INV = 0.20`).

### 1.3 Labor Allocation & Efficiency
Production is driven by the **Labor Pool**. Instead of players manually assigning "3 people to wood," the **EconomyManager** (`_process_labor_pool`) runs a daily heuristic for every settlement.

#### The Labor Hierarchy:
1.  **Subsistence (24h Survival):** The town first allocates labor to ensure there is enough food and wood (fuel) to prevent starvation and freezing in the next 24 hours.
2.  **Security (60-Day Buffer):** Once 24-hour survival is met, labor is assigned to build a 60-day stock of grain and fuel.
3.  **Specialization (Profit):** Any remaining labor is assigned to the "most profitable" task based on **current market prices** in that settlement. If Iron is expensive, more laborers will go to the Mines.

#### Base Industrial Yields (Per Laborer, Per 360-Day Year):
Based on `Globals.ACRES_WORKED_PER_LABORER = 10`:

| Resource | Yield Equation | Total/Worker | Labor Source |
| :--- | :--- | :--- | :--- |
| **Grain** | 120 Bushels (12/acre * 10 acres) | 120 Units | Arable/Farms |
| **Wood** | 100/acre * 10 acres | 1000 Units | Forests |
| **Meat** | 2.5/acre (Hunt) or 4.0/acre (Pasture) | 25-40 Units | Wilderness/Pasture |
| **Wool** | 15.0/acre * 10 acres | 150 Units | Pasture |
| **Fish** | Fixed Base Yield | 30 Units | Water |
| **Peat & Clay** | 40 / 25 Units per worker | 40/25 Units | Wetlands/Swamp |
| **Salt & Sand** | 15 / 60 Units per worker | 15/60 Units | Arid/Desert |
| **Ore & Stone** | Variable (300-500) | 300+ Units | Mountain/Mines |

### 1.5 Pillar-Based Infrastructure
Urban development is categorized into three strategic pillars. Buildings no longer function as isolated factories but as **stackable multipliers** to the settlement's base labor and geographic yields.

#### I. Industry Pillar (Resource & Efficiency)
These buildings amplify the output of the Labor pool.
*   **Farms/Mines/Quarries (Extraction):** Each level provides a **+50% multiplier** to the yield per acre.
*   **Processing Units (Blacksmith/Weaver/Tailor/Brewery):** Each level provides a **+100% efficiency bonus** to the conversion of raw materials into finished goods.
*   **Warehouse District:** Provides a massive increase to the settlement's storage capacity (+100% per level).

#### II. Defense Pillar (Stability & Hardness)
These buildings protect the settlement and its sovereignty.
*   **Stone Walls:** Increases the "Defensive Hardness" of the settlement during sieges.
    *   *Logic:* Walls multiply garrison strength by a base of 10x, plus 10x per level (Level 2 = 30x).
*   **Barracks & Training Grounds:** Increases the base quality of the local garrison, making each defender more effective.
*   **Castle (Citadel):** The final seat of power, providing massive stability and doubling the effect of all other defense buildings.

#### III. Civil Pillar (Happiness & Taxation)
These buildings manage the population and revenue.
*   **Market & Road Network:** Increases trade efficiency and commerce tax revenue (+25% and +15% per level respectively).
*   **Taverns:** Passively generates Happiness (+1 per level) and reduces Unrest.
*   **Cathedrals:** Provides Stability (+2 per level) and increases revenue via tithes and donations.
*   **Housing District:** Each level provides +100 to the maximum population capacity, allowing cities to grow into megapolises.

---

## 2. Social Strata & Labor
Settlements are divided into three distinct social classes, each with a specific economic role.

### 2.1 The Three Estates
*   **Laborers (The Peasantry):** The backbone of the economy. They work the land, mines, and forests. Their output is boosted by **Industry Pillar** buildings.
*   **Burghers (The Middle Class):** Skilled workers who inhabit urban centers. They man the **Micro-Workshops** (Organic Industries) and process raw materials into finished goods. Target population: **10%** of total (`Globals.BURGHER_TARGET_PERCENT`).
    *   **Consumption:** Each Burgher consumes **0.1 Ale** and small amounts of **Cloth/Leather** per day.
*   **Nobility (The Rulers):** A small percentage (**0.7%**) of the population. They provide stability and governance but consume **Luxuries (Meat, Furs, Salt)**.

### 2.2 Organic Industry (Micro-Workshops)
As a settlement grows, it naturally develops "Organic Industries" without player intervention.
*   **Industry Slots:** 1 workshop slot is created for every **50 people**.
*   **Employment:** Each workshop employs **5 Burghers**.
*   **Key Industries:** Weaver (Cloth), Tannery (Leather), Blacksmith (Steel), Brewery (Ale), Tailor (Fine Garments), Bronzesmith (Bronze).
*   **Multiplier Logic:** The output of these workshops is multiplied by the level of their corresponding pillar building (e.g., a Level 2 Blacksmith grants +200% output to all local smithing jobs).

---

## 3. Urban Construction & Scaling
Construction is a simulation of investment and labor intensity.

### 3.1 Exponential Scaling
To simulate the difficulty of building "Tall," all building costs and labor requirements follow an exponential curve:
`Cost = Base * (2.5 ^ Current_Level)`

*   **Materials:** Construction has been standardized to **Crowns-Only** to prevent resource-locking for the AI. This abstractly represents the purchasing of local tools, specialized labor, and imported materials.
*   **Labor:** Even after paying the Crown cost, the settlement must assign **Laborer/Burgher cycles** to the "Construction Project." Higher levels require significantly more labor cycles to complete.

### 3.2 Housing & Growth
Population growth is limited by **Housing Capacity**.
*   **Houses:** Basic sprawl provides a small amount of housing.
*   **Housing Districts:** Specialized urban planning (Level 1, 2, 3...) provides massive blocks of high-density housing for megacities.
*   **The Hub Model:** Settlements are prioritized by the AI based on their **Geographic Potential** (Arable land + Resource magnetism). High-potential sites will see rapid pillar investment.

---

## 4. The Market: Dynamic Price Scarcity
Pricing is the "brain" of the simulation. Every resource has a **Base Price** (`Globals.BASE_PRICES`), but the **Market Price** is calculated daily via the **Scarcity Ratio**.

### 4.1 The Supply/Demand Formula
`Price = Base_Price * (Demand / Stock)`
*   **Demand Anchors:** Food demand is based on **Population * 14 Days** of survival. Wood demand is tied to population and climate.
*   **Price Clamping:** Generally between **0.2x** and **4.0x** base, jumping to **5.0x** when stock is empty (`Globals.PRICE_ZERO_STOCK_MULT`).

### 4.2 World Market Buy Orders (The "Pull" System)
When a settlement's stock of a critical resource (Grain, Iron, Wood, Wool, Coal, Meat, Salt) falls below a survival threshold, the local Merchants' Guild places a **Buy Order** on the World Market (visible in the Management tab).
*   **Guaranteed Premium:** Buy orders offer a guaranteed price (usually **1.2x Base** or current market price, whichever is higher).
*   **Trading Magnet:** Caravans prioritize fulfilling these orders over search-based arbitrage, ensuring resources flow to starving cities and industrial hubs.

---

## 5. Logistics: The Supply Chain
Resources move through the world via two primary vectors: **Virtual Logistical Pulses** and **Physical Merchant Hubs**.

### 5.1 Virtual Logistical Pulses (Hamlets)
To optimize performance and eliminate "Entity Bloat," small settlements (Hamlets) do not spawn physical villager units. Instead, they use **Logistical Pulses**.
*   **The Virtual Pipeline:** Every day, a hamlet calculates its surplus. A "Pulse" entry is created in the global system with an `Arrival Turn` based on the distance to the Parent City.
*   **Reliability:** This system ensures that hub cities receive a steady, staggered flow of raw materials without requiring thousands of AI entities to pathfind simultaneously.

### 5.2 Physical Caravans & Merchant Hubs
Only Tier 3 (Towns) or higher settlements with a **Merchant Guild** can spawn physical Caravans. These are high-value, high-capacity units designed for long-distance trade.
*   **Super-Caravans:** As a city invests in its **Industry Pillar**, its caravans gain massive capacity bonuses (+50% per Guild level) and better guards.
*   **Staggered AI:** To save CPU, caravans only update their pathfinding and trade scanning logic every 4 turns, with their updates staggered across the global clock.

---

## 6. Population & Growth
A settlement's population is its most valuable asset, providing the workforce for both land and industry.

### 6.1 Consumption & Starvation
Each person consumes a specific amount of grain daily. If food is unavailable:
*   **Death Toll:** `(Population * 0.02) + 2` people die daily. 
*   **Social Unrest:** Increases by **10** per day.
*   **Happiness:** Decreases by **20** per day.

### 6.2 The Demographic Model (Housing Districts)
Population growth is no longer a simple "5 per house" limit. It is controlled by a multi-layered capacity formula.
*   **Base Sprawl:** Every settlement tier provides a base capacity (e.g. 50, 150, 250, 350 for Village, Town, City, Metropolis).
*   **Housing Districts (Civil Pillar):** The primary way to grow a city is investing in the Housing District building. Each level adds **200** to the population cap.
*   **Urban Efficiency:** The **Town Hall** (Civil Pillar) provides a percentage multiplier (e.g. +10% per level) to the *total* housing capacity, reflecting better urban planning.
*   **Migration:** If Happiness falls below **40**, citizens begin to leave the settlement, seeking nearby cities with higher satisfaction and available housing capacity.
*   **Overcrowding:** If population exceeds capacity, growth stops, happiness plummets, and **Unrest** increases by 1 per day.

---

## 6. Construction & Development
Settlements grow by building and upgrading structures.

### Building Slots & Tiers
Settlements have a limited number of **Building Slots** based on their Tier. You cannot build a high-tier building (like a Cathedral) in a small Village.

### Costs & Labor
- **Cost Scaling**: Building costs grow on a polynomial curve: `Base Cost * (Level + 1)^2.2`. This makes early levels highly accessible for growth, while Level 10 milestones become a prestigious long-term objective for wealthy capitals.
- **Accessible Entry**: Base training/industry costs (Walls, Blacksmiths, Markets) have been tuned downward to ensure villages can begin their climb to urban centers.
- **Labor Power**: `(Population / 100) * Happiness Modifier`.
- **Resource Import**: If a city lacks Wood, Stone, or Iron for a project, it can "Import" them from the global market at a premium (Wood: 10, Stone: 20, Iron: 40).

### Player Sponsorship & Influence
The player can **Sponsor** buildings in a settlement. This pays the upfront Crown cost and grants the player **Influence** with that settlement:
- **Sponsoring a Building**: Grants **+10 Influence**.
- **Donating Resources**: Grants Influence based on the amount donated (`Amount / 10.0`).
- **Influence Benefits**: High influence can lead to better trade prices, political support, and access to unique recruits (mechanics currently in development).

---

## 8. Macro-Economy: Caravans & Factions
### Trade Routes
Caravans seek profit by comparing prices between cities. They factor in:
- **Profit Margin**: `Sell Price - Buy Price`.
- **Distance Penalty**: `Distance / 10.0` is subtracted from the potential profit.
- **Capacity**: Caravans can carry up to **50 units** of bulk goods (Wood/Stone) or **20 units** of high-value goods.
- **Influence**: Caravans gain **+5 Influence** for their faction in every city they trade with.

### Faction Treasuries & Taxation
Factions (Red Kingdom, Blue Empire, etc.) maintain their own treasuries:
- **Caravan Tax**: When a caravan returns to a settlement owned by its faction, any Crowns it has above **500** are deposited into the Faction Treasury as "Tax".
- **Economic Stipends**: Factions inject **5% of their treasury daily** back into their Lords and Settlements. This ensures that even poor frontier fiefs can eventually afford to hire guards or build infrastructure.
- **Upkeep**: Factions use these funds to pay for army upkeep (`Roster Size * 2`). If a Lord cannot pay, **10% of the roster will desert** each day.
- **Colonization**: Factions can spend their treasury to send out **Settler Parties** to found new villages. New colonies start with 100 population and basic infrastructure.

---

## 9. World Generation & Initial Economic State

The world's economic landscape is determined by a **Geographic Potential Analysis** in [WorldGen.gd](WorldGen.gd). Instead of a temporal simulation, the game evaluates the physical capacity of every tile at the moment of creation.

### 9.1 Carrying Capacity & Magnetism
Each potential settlement site is scored based on:
- **Carrying Capacity (The Food Ceiling):** Calculated from surrounding **Arable**, **Water**, and **Forest** tiles. A tile surrounded by fertile plains has a natural capacity to support thousands, while a mountain pass is capped until trade begins.
- **Economic Magnetism (The Labor Draw):** Industrial resources like **Gold**, **Gems**, and **Iron** act as multipliers. They represent the "pull" that draws people to settle in density despite potentially low local food yields.

### 9.2 Potential-Based Tiering
Settlements are ranked by **Potential Revenue** (Food Tax Base + Industry Magnetism).
- **Metropolis (Top 5%):** Starts at 95% of its carrying capacity ceiling.
- **City (Next 10%):** Starts at 70% capacity.
- **Town (Next 20%):** Starts at 40% capacity.
- **Village:** Starts at 20% capacity.

This ensures that "High Value" geographic locations naturally emerge as large urban centers because the land *can* support and *wants* to draw a population there.

### 9.3 Territorial Assignment
Factions are assigned based on distance to distant capitals. These capitals are always selected from the highest-potential Metropolis sites to ensure major kingdoms occupy the most fertile and resource-rich deltas.

### 9.4 Roads, Connectivity, and Pathfinding

The world is wired together with **roads** `=` using an A* grid in [WorldGen.gd](WorldGen.gd):
- Terrain defines **path cost**:
    - Plains `.`, Towns: cheap.
    - Forest `#`: 5× cost.
    - Mountains `^`: 50× cost.
    - Water `~`: blocked.
- The generator builds a **minimum spanning tree** (MST) over settlements, then adds extra edges between nearby settlements to form **trade loops** and crossroads.
- Roads reduce future path costs (weight 0.5), encouraging more roads to follow existing “highways”.

These roads are only visual in the overworld, but economically they:
- Increase income during the history simulation.
- Determine where caravans can travel efficiently once the game starts.

---

## 10. Historical Simulation (Deprecated)
The 100-year history simulation has been replaced by the **Gegeographic Potential Model** (see Section 9) to ensure instant generation and geographically logical urban placement.

---

## 11. Hamlets, Villages, and Cities: Promotion Path

The economy models long-run rural–urban migration and development.

### 11.1 Hamlet → Village Promotion

Hamlets can graduate into full **Villages** via the hamlet promotion logic in GameState:

Key triggers:
- **Stability**: Each successful villager delivery from hamlet to city increases **stability** by +1.
- At **stability ≥ 50**, promotion from hamlet to village fires.

Promotion effects:
- Type becomes `village`, roughly matching **tier 2**.
- Population jumps to around 100; garrison increases.
- Radius increases, and building slots increase accordingly.
- Crown stock is boosted and houses expanded to house the larger population.
- A governor is assigned, and shops/recruit pools become available.

### 11.2 Geographic Potential WorldGen (The "Fix")
To prevent "Gilded Death Traps" (cities with high population but no food or housing), the WorldGen now uses a **Geographic Potential Model**:
1.  **Site Capacity:** Analyzes arable land and water access within an 8-tile radius.
2.  **Magnetism:** Adds bonuses for rare resource deposits (Iron, Gems) and strategic bottlenecks.
3.  **Tier Assignment:** Cities are only spawned on sites with the capacity to support them. 
4.  **Auto-Sync:** Upon generation, population classes, housing, and radius match the Tier, ensuring starting cities are economically viable.

---

## 12. Full Building Catalog

| Building | Category | Base Cost | Description |
| :--- | :--- | :--- | :--- |
| **Farm** | Industry | 500 | +50% Grain yield multiplier. |
| **Lumber Mill** | Industry | 800 | +100% Wood yield multiplier. |
| **Fishery** | Industry | 600 | +50% Fish yield multiplier. |
| **Mine** | Industry | 1500 | +50% Stone/Ore yield multiplier. |
| **Pasture** | Industry | 700 | +50% Wool/Hide/Meat yield multiplier. |
| **Blacksmith** | Industry | 4000 | +100% Steel efficiency multiplier. |
| **Tannery** | Industry | 2500 | +100% Leather efficiency multiplier. |
| **Weaver** | Industry | 2500 | +100% Cloth efficiency multiplier. |
| **Brewery** | Industry | 3000 | +100% Ale efficiency multiplier. |
| **Tailor** | Industry | 3500 | +100% Garment efficiency multiplier. |
| **Bronzesmith** | Industry | 3500 | +100% Bronze efficiency multiplier. |
| **Warehouse** | Industry | 3000 | +100% Storage Limit. |
| **Stone Walls** | Defense | 15000 | Adds +10x to Garrison Hardness. |
| **Barracks** | Defense | 5000 | Increases Garrison Quality and Capacity. |
| **Granary** | Defense | 1200 | Starvation mitigation and food storage. |
| **Housing Dist.**| Civil | 1000 | +100 Population Capacity. |
| **Market** | Civil | 2000 | +25% Commerce Tax multiplier. |
| **Road Network** | Civil | 2500 | +15% Trade Efficiency multiplier. |
| **Tavern** | Civil | 1500 | Passively boosts Happiness and Growth. |
| **Cathedral** | Civil | 12000 | Massive Stability and Nobility Loyalty. |

---

## 13. Factions, Lords, and Military Economy

Beyond towns, a large part of the macro-economy exists in **armies and lords**.

### 12.1 Faction Treasuries

Each faction (Red, Blue, Green, Purple, Orange, Bandits, Neutral, Player) has:
- A global **treasury** used to finance lords and indirectly construction.
- Starting values in GameState:
    - Player: 1000.
    - Major AI factions: around 5000.
    - Bandits/Neutral: 0.

Income sources to faction treasuries:
- **Caravan Tax**: When a caravan is in a settlement owned by its faction and has **>500 Crowns**, everything above 500 is skimmed as **tax** into the faction’s treasury.
- **Residual Wealth**: Rich settlements after worldgen history effectively act as tax bases because their city coffers fund lords and construction.

### 12.2 Lords: Creation and Maintenance

Lords are spawned in WorldGen near castles/cities:
- Each lord has:
    - `roster`: 30–100 recruits.
    - `crowns`: 1000–5000.
    - `provisions`: 500–2000.
    - `home_fief`: A settlement they draw support from.
    - `doctrine`: Conqueror/Defender/Raider/Merchant Prince.

Daily economics for lords:
- **Upkeep**: Each **lord army** pays `roster_size * 2` Crowns per day.
- If the lord’s personal `crowns` are insufficient:
    - They draw from their **home_fief’s** `crown_stock`, but only above a buffer of 1000 Crowns.
    - If they still can’t meet upkeep, **10% of their roster deserts** (roster shrinks to 90%).

Recruitment logic:
- When understrength, a lord seeks a **recruitment center** (friendly settlement).
- They attempt to spend **500 Crowns**:
    - First from the settlement’s `crown_stock`.
    - If insufficient, from the faction’s **treasury**.
- Successful recruitment adds new recruits, increasing roster size and future upkeep.

### 12.3 Sieges and Captures

While primarily military, sieges have economic consequences:
- If attackers win:
    - Settlement’s **faction** changes; its future **crown_stock**, production, and recruitment now serve the conqueror.
    - Part of the attacking army becomes the new **garrison**.
- If defenders win:
    - Attacker’s roster/strength is heavily reduced; some lords may be effectively reset.

This shifts long-run economic power by moving high-tier settlements between factions.

---

## 13. Ruins, Loot, and Player-Centric Money Injection

The **dungeon/ruin** system acts as an external money & item faucet for the player.

### 13.1 Ruin Generation

In WorldGen, ruins are placed:
- On forest, mountain, or plains tiles at a safe distance from settlements.
- Each ruin has:
    - `danger` (1–5): Difficulty proxy.
    - `loot_quality` (1–5): Determines reward quality.

### 13.2 Ruin Rewards

When a ruin is cleared:
- Player receives:
    - **Crowns**: `loot_quality * rand_range(50, 150)`.
    - **Items**: Random count between 1 and `loot_quality`.
- Items are sampled from the global equipment list and material pool:
    - Metals: Iron, Steel, Bronze.
    - Soft: Leather, Wool, Linen.
    - Quality escalates with `loot_quality` (Fine, Masterwork).

Economically, this means:
- Dungeons inject **pure currency** into the player economy (no drain on settlements).
- High-quality equipment enters circulation without requiring local production chains.
- The player can liquidate loot into crowns by selling items to shops.

---

## 14. Analytics and Simulation Tools

The code includes tools to inspect and stress-test the economy.

### 14.1 World Audit (Developer Tool)

The function that runs a world audit prints a holistic economic snapshot:
- **Demographics**:
    - Total population, total houses, housing capacity, overcrowded settlements.
- **Economy**:
    - Total wealth (sum of all `crown_stock`).
    - Average production efficiency across settlements.
- **Faction Breakdown**:
    - For each faction: population, number of settlements, total wealth (treasury + local), army strength, average happiness.
    - Top produced resources per faction.
- **Global Resource Stocks & Prices**:
    - For key goods (Grain, Fish, Meat, Wood, Stone, Iron, Steel, Leather, Cloth, Ale, Horses):
    - Total global stock and average **dynamic price** from `get_price`.
- **Logistics**:
    - Active caravan and army counts.

This is mostly for debugging and balancing, but it reflects exactly how the economy perceives itself at runtime.

### 14.2 Monthly Turbo Simulation

The **Turbo Simulation** fast-forwards **30 days**:
- Disables most logs except the monthly report.
- Tracks:
    - `production[res]`: Amount produced across all settlements.
    - `consumption[res]`: Amount consumed/burned.
    - `idle_buildings[building]`: Building-days spent idle due to missing inputs.
    - Important economic **events** (e.g., starvation) appended by other systems.

At the end, the monthly report emits a detailed summary:
- Population and player treasury deltas.
- Per-resource production vs. consumption and net surplus/deficit.
- Warnings about frequently idle buildings (signs of bottlenecks).
- Logged events (famines, etc.).

This tool lets you observe the **systemic behavior** of the simulated economy over time without manual play, making it invaluable for tuning.

---

## 15. Putting It All Together

In summary, the economy in *Falling Leaves* is the emergent result of:
- **Geography & Geology**: Climate and layers decide where resources live.
- **Production & Buildings**: Farms, Mines, Pastures, Mills, and Fisheries convert terrain into daily output, modulated by workforce efficiency.
- **Industrial Chains**: Blacksmiths, Tanneries, Weavers, Breweries, Markets, Tailors, and Goldsmiths transform raw goods into higher-value exports.
- **Logistics**: Virtual pulses and physical super-caravans fulfill global buy orders and move raw goods to industrial hearts.
- **Population Dynamics**: Food and housing capacity drive growth, migration, and sometimes starvation.
- **Construction & Governance**: AI governors and the player invest crowns into the Three Pillars (Industry, Defense, Civil), which reshape the world map.
- **Factions & Lords**: Military spending and territorial conquest continuously re-distribute wealth and economic capacity.
- **Player Actions**: Trading via the World Market, sponsoring buildings, and clearing ruins all inject shocks into the simulation.

## 16. Building Milestones & Unlocks
Buildings now feature a **Milestone System** where reaching specific levels unlocks new mechanics, unit types, or economic flavor. Costs follow a polynomial curve (`(Level+1)^2.2`) rather than exponential, making high levels achievable for wealthy kingdoms.

### Industry (The Engine)
*   **Yield & Production Multipliers**
    *   **Level 3**: +300% Yield/Efficiency Multiplier.
    *   **Level 7**: +700% Yield/Efficiency Multiplier.
    *   **Level 10**: +1000% Yield/Efficiency Multiplier (Mass Industrialization).
*   **Blacksmith**
    *   Level 1: **Village Smithy** | Level 5: **Foundry** | Level 10: **The Vulcan Complex**.
*   **Mine**
    *   Level 1: **Surface Quarry** | Level 5: **Drainage Pumps** | Level 10: **Under-Kingdom**.
*   **Farm**
    *   Level 1: **Fields** | Level 4: **Three-Field System** | Level 10: **Agricultural Revolution**.
*   **Fishery**
    *   Level 1: **Fishing Huts** | Level 6: **Deep Sea Fleet** | Level 10: **The Great Harbor**.

### Defense (The Shield)
*   **Barracks**
    *   **Structure**: Follows a "Volume vs. Quality" staggered progression.
    *   **Odds (1, 3, 5, 7, 9)**: Each level significantly increases the number of recruits generated per batch (Muster Volume).
    *   **Evens (2, 4, 6, 8)**: Each level unlocks a hardware/tier upgrade (Muster Quality).
        *   Level 2: Unlocks Tier 2 (Trained).
        *   Level 4: Unlocks Tier 3 (Men-at-Arms).
        *   Level 6: Unlocks Tier 4 (Veterans).
        *   Level 8: Unlocks Tier 5 (Royal Guard).
    *   **Level 10 (Citadel)**: The Milestone. Grants maximum muster volume and a high percentage chance for every recruit to be Tier 4 or 5.
*   **Stone Walls**
    - **Logic**: Walls provide a base defense multiplier, but odd-numbered levels unlock major tactical advantages that severely weaken attackers.
    - **Level 1**: **Palisade** (Basic wooden protection).
    - **Level 3**: **Watch Towers** (-25% Attacker Strength via archer harassment).
    - **Level 5**: **Stone Walls** (Significant increase to base defense multiplier).
    - **Level 7**: **Siege Engines** (Defensive balistas inflict 40% HP damage to random attackers).
    - **Level 9**: **The Moat** (-50% Attacker Strength via massive bottlenecking).
    - **Level 10 (Star Fort)**: **Siege Immunity**. The ultimate milestone. Doubles all existing wall status.

### Civil (The Heart)
*   **Market**
    *   **Level 1**: **Town Stalls** (Basic trade).
    *   **Level 3**: **Tax Office** (+20% tax efficiency without increasing unrest).
    *   **Level 6**: **Guild Hall** (Market price fixing and better trade margins).
    *   **Level 10**: **Grand Exchange** (Earn 1% interest on total settlement crowns weekly).
*   **Tavern**
    *   **Level 1**: **Alehouse** (Basic community gathering).
    *   **Level 4**: **Traveler's Inn** (Potential to hire Tier 4 veteran Mercenaries).
    *   **Level 7**: **Bard's College** (Propaganda: manipulate public opinion/unrest).
    *   **Level 10**: **Shadow Broker** (Full map vision and deep state counter-espionage).
*   **Housing District**
    *   Level 1: **Thatched Cottages** | Level 5: **Stone Tenements** | Level 10: **The High District** (+2000 total pop cap).
*   **Road Network**
    *   Level 1: **Dirt Paths** | Level 5: **Cobblestone Streets** | Level 10: **Imperial Highways**.
*   **Cathedral**
    *   Level 1: **Sanctuary** | Level 4: **Basilica** | Level 10: **The Seat of Divines** (Massive global stability).