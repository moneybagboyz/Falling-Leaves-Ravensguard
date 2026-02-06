# Settlement & Fief Management (Deep-Dive)

Settlements are the engine of the world civilization. They are simulated using a mix of historical land-use models and industrial supply chains.

---

## 1. Social Classes & Internal Demographics
A settlement's population is not a monolith; it is split into three social classes with precise target percentages:
*   **Laborers**: The working class (Baseline: ~90-95%). They perform all raw resource extraction.
*   **Burghers**: The middle class (Baseline: ~3-5%). They staff professional buildings (Blacksmiths, Weavers) and handle trade. Their performance is highly sensitive to **Happiness** and **Stability**.
*   **Nobility**: The upper class (Baseline: ~1-2%). They generate tax stability, determine political alignment, and provide the high-quality gear for the garrison.

---

## 2. The Acre & Land-Use System
Every settlement exists on a "radius" of influence that converts map tiles into **Acres** (Baseline: 250 acres per tile).
*   **Arable Land**: Plains and hills are cleared for crops.
*   **Three-Field System**: Settlements simulate a historical crop rotation. Only 2/3rds of land is planted; 1/3rd is left **Fallow** (pasture) to recover nutrients. This prevents soil exhaustion.
*   **Wilderness/Forest**: Provides the "Wilderness" yield (Hunting/Foraging) and raw Timber.
*   **Extraction Potential**: Certain biomes allow unique slots:
    *   **Mountains**: Mining slots (Iron, Gold, Stone).
    *   **Rivers/Swamps**: Fishing and Sifting (Salt, Peat, Clay).
    *   **Deserts**: Salt and Sand extraction.

---

## 3. Industrial Supply Chains
Settlements move beyond raw extraction through specialized buildings:
*   **Food Chain**: Arable Acres -> Grain -> **Brewery** (Ale) or Consumption.
*   **Equipment Chain**: Mine (Iron) -> **Blacksmith** (Tools/Weapons).
*   **Textile Chain**: Pasture (Wool/Hides) -> **Weaver** (Cloth) or **Tannery** (Leather) -> **Tailor** (Garments).
*   **Luxury Chain**: Mining (Gold/Gems) -> **Goldsmith** (Jewelry).

### Production Efficiency
Efficiency is calculated as: `(Workforce Count) * (Settlement Happiness) * (Social Stability) * (Workforce Efficiency Attribute)`.

---

## 4. Supply, Demand, and Price Modeling
Prices are not fixed but dynamic based on 2-week demand anchors:
*   **Food Demand**: Calculated as `Population * Daily_Consumption * 14 days`.
*   **Fuel Demand**: Based on population and the **local temperature** (colder climates require significantly more wood/peat).
*   **Scarcity Pricing**: If stock is 0, the price hits a `PRICE_ZERO_STOCK_MULT` (usually 5x base). High stock causes prices to drift toward a floor of 20% base value.

---

## 5. Governance & AI Personalities
Each settlement is managed by a Governor (NPC) whose personality dictates their construction priorities:
*   **Builder**: Obsessed with **Housing Districts** and **Warehouses**. They aim for high population growth.
*   **Greedy**: Focuses on **Markets**, **Mines**, and **Road Networks**. They generate high tax revenue but often ignore defense.
*   **Cautious**: Heavily invests in **Stone Walls**, **Granaries**, and **Watchtowers**. They maintain high food buffers (60+ days) and are hard to starve out.
*   **Balanced**: Maintains a mix of industry and defense.

---

## 6. Stability, Loyalty, and Unrest
*   **Unrest**: Increases with starvation, high taxes, and proximity to war. High unrest causes a "Work Stoppage" penalty to all industry.
*   **Happiness**: Derived from Ale availability, food diversity, and social events (Tournaments).
*   **Loyalty**: Determines how likely a fief is to defect or rebel if its parent faction is weak.

---

## 7. Recruitment & Military
Settlements refresh their "Recruit Pool" daily:
*   **Villages**: Provide mostly Laborers and basic Recruits.
*   **Castles**: Provide higher-tier professional soldiers and have larger **Garrison Capacity**.
*   **Training Grounds**: Investing in this building increases the average Tier and experience of recruits in the pool.

---

## 8. Empire Building: Founding Your Own Settlement
As a player, you can transition from an adventurer to a Lord by establishing your own settlements in the wilderness.

### Founding Requirements
To found a new settlement, move your character to an empty tile and press **[B]**. You must meet the following criteria:
*   **Royal Charter**: You must obtain a document from your King authorizing you to settle new lands (Request one from your Faction Leader).
*   **Capital Investment**: Founding a colony costs **5,000 Crowns** to hire pioneers and transport supplies.
*   **Logistics**: You must carry at least **500 units of Grain** in your inventory to feed the settlers during the construction phase.
*   **Wilderness Distance**: Your site must be at least **10 tiles** away from any existing settlement to ensure sufficient land for farming and logging.
*   **Site Evaluation**: The tile must be fertile/productive. If the soil score is too low (e.g., deep desert or high peaks), the pioneers will refuse to stay.

### The Construction Phase
Once founded, your camp enters a **14-day construction phase**.
*   **Settler Party**: A pioneer camp is vulnerable. You should stay nearby or garrison troops to protect it from bandits.
*   **Promotion**: After 14 days, the camp automatically promotes to a **Hamlet** of a specific type (Mining, Farming, Fishing, or Lumber) based on the surrounding terrain.

---

## 9. Settlement Upgrades & Infrastructure
Once you control a settlement, you can commission improvements. Construction work occurs between **06:00 and 18:00** and is performed by the settlement's Labor pool.

### Tier 1: Basic Infrastructure
*   **Housing District**: Increases population capacity by 100.
*   **Farm/Lumber Mill/Fishery/Mine/Pasture**: Increases raw resource yield by 50-100% per level.
*   **Granary**: Increases starvation resistance (food buffers) and storage cap by 50%.
*   **Wooden Tower**: Increases vision range on the overworld (+2 tiles) and provides raised archer platforms.
*   **Watchtower**: Reduces bandit loot success chance and provides a bonus to local stability.
*   **Tavern**: Increases civilian happiness and attracts faster migration (new settlers).

### Tier 2: Advanced Industry & Defense
*   **Market**: Unlocks more industrial slots and increases trade income/tax efficiency.
*   **Stone Tower**: Replaces Wooden Towers. Greatly increases Stability and unlocks the **Ballista** slot.
*   **Ballista**: A stationary siege engine mounted on towers. Devastating against individual high-value targets and small groups.

### Tier 3: Military Bastions
*   **Grand Bastion**: The ultimate defensive upgrade. Integrates multi-level towers and unlocks the **Catapult** slot.
*   **Catapult**: A heavy siege engine capable of dealing massive area damage and destroying enemy siege equipment (Rams, Towers).
*   **Stone Walls**: Hardens the settlement against raids, making it significantly harder to capture in a siege.
*   **Barracks**: Increases the maximum Garrison capacity (troops held inside).
*   **Blacksmith/Tannery/Weaver/Brewery**: Enables the conversion of raw materials into high-value goods (Tools, Armor, Clothes, Ale).
*   **Road Network**: Increases trade throughput and improves tax collection efficiency.

### Tier 3: Masterworks
*   **Cathedral**: Massively increases the loyalty of the Nobility and local stability.
*   **Merchant Guild**: Increases the number of active Caravans and extends the trade reach of the settlement.
*   **Goldsmith**: Allows the production of Jewelry, the most valuable trade good in the game.

### Promotion Path
Settlements grow organically as they meet certain milestones:
1.  **Hamlet**: The starting level. Focuses on a single raw resource.
2.  **Village**: Promoted from Hamlet once Stability reaches **50**. Unlocks more building slots.
3.  **City**: Promoted from Village once Population exceeds **500**. Becomes a regional trade hub with massive industrial potential.
