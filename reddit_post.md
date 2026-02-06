# [Dev] Building a Living World: Real Economy, Anatomy-Based Combat, and Procedural History

Hello everyone! I've been working on a project called **Falling Leaves**, and I wanted to share the depth of the simulation we're building. It's a mix of a Grand Strategy economy, a Military Campaign simulator, and a Traditional Roguelike.

The core philosophy is that everything should be **interconnected**. War is driven by economic scarcity, and the economy is driven by the physical geography and labor of the people.

---

## 1. Geographic World Generation & Settlement Placement
We don't just scatter cities across a map. The world uses a **Geographic Potential Model**:
*   **Carrying Capacity:** Every tile is analyzed for its food potential (Arable, Forest, Water). A site surrounded by fertile plains naturally becomes a Metropolis, while a mountain pass stays a resilient Village.
*   **Economic Magnetism:** Rare resources like Iron, Gold, and Gems act as "pull" factors, drawing population growth to hostile environments through high industrial wages.
*   **A* Road Networks:** The world is wired together with an MST-based road system. Terrain cost determines the flow—roads reduce pathing costs, encouraging natural "highways" for trade.

## 2. A "Real" Simulated Economy
Most games use "+5 gold per turn." We use **Physical Land Management**:
*   **The Acreage System:** Each world tile has **250 Acres**. Arable land is managed via a **Three-Field System** (Winter, Spring, and Fallow/Pasture) to simulate historical crop rotation and prevent soil exhaustion.
*   **Labor Allocation:** The AI doesn't just "work." It follows a hierarchy: **Subsistence** (don't starve today), **Security** (build a 60-day buffer), and **Specialization** (produce what’s currently most profitable on the market).
*   **Dynamic Scarcity Pricing:** Prices are calculated daily based on a 14-day demand anchor. If a city is starving, Grain prices can jump to **5x base**, attracting Caravans from across the continent.

## 3. Social Strata & Organic Industry
Cities are divided into three social classes:
*   **Laborers:** Extract raw materials (Grain, Ore, Wood).
*   **Burghers:** The skilled middle class. They staff **Organic Workshops** (Blacksmiths, Weavers, Tanneries) that automatically appear as the population grows.
*   **Nobility:** Provide stability and high-end military equipment but consume luxury goods like Meat and Furs.

## 4. Warfare, Campaigns, and Sieges
War isn't just a blob moving to a target:
*   **Military Campaigns:** Faction Marshals start a "Gathering" phase at border settlements, waiting for local Lords to assemble their armies before marching.
*   **Army Upkeep:** Lords must pay their rosters. If they run out of Crowns, **10% of their army deserts** every day. They have to draw from their home fiefs or global faction treasuries to stay operational.
*   **Siege Tactics:** Fortifications are massive force multipliers. **Stone Walls** can multiply a garrison's strength by 10x or more. Attackers must use **Catapults/Trebuchets** to create breaches or simply starve the city out.

## 5. Tactical Anatomy-Based Combat
We've replaced the traditional "HP Bar" with a **Detailed Anatomy System**:
*   **Layers & Organs:** Units have Skin, Muscle, Bone, and internal Organs (Heart, Lungs, Brain).
*   **Blood Simulation:** Every wound causes blood loss. Severing an artery leads to rapid exsanguination. You don't "lose 20 HP"—you lose 500ml of blood and your vision starts to fade.
*   **Momentum Physics:** Damage is $Weight \times Velocity$. A heavy maul transfers force *through* plate armor to break the bone beneath, while a piercing thrust might bypass the armor's yield entirely.

## 6. Traditional Roguelike Ruins & Caves
When you step away from the Overworld, you enter a **Traditional Roguelike**:
*   **Procedural Dungeons:** Procedural layout with fog-of-war, vaults (Throne Rooms, Ancient Libraries), and grid-based tactical movement.
*   **Loot Injection:** Dungeons act as the primary "money faucet" for the player, injecting currency and masterwork items into the simulation without inflating the settlement-based market.

## 7. Settlement Management & Investment
Beyond adventuring, you can become a power broker or a pioneer:
*   **Infrastructure Upgrades:** Invest in three pillars: **Industry** (yield multipliers), **Defense** (fortifications), and **Civil** (happiness and trade). Costs follow a polynomial curve $(Level+1)^{2.2}$, making early growth accessible but Level 10 milestones a prestigious achievement.
*   **Building Sponsorship:** Don't just own—**Sponsor**. You can pay the upfront Crown costs for a city's new Market or Blacksmith. This grants you **Influence** with the local government, leading to lower trade prices and political support.
*   **Founding Your Own Kingdom:** Tired of serving others? Get a **Royal Charter**, gather 5,000 Crowns and a massive stock of Grain, and lead a pioneer party 10 tiles into the wilderness. Protect your settlers during the 14-day construction phase, and watch your camp grow into a bustling regional hub.

## 8. Leading & Recruiting Armies
As a player, you can transition from an adventurer to a Faction Lord:
*   **Muster Volume vs. Quality:** Improving your **Barracks** increases how many troops you can recruit and their starting Tier (from Peasant Laborers to Royal Guards).

## 9. Living Quests
Questing is tied directly to the simulation's needs:
*   **Industry Quests:** A city might ask for a **Construction Quest** to help build its first Stone Walls.
*   **Logistics Quests:** Starving cities will post **Delivery Quests** with guaranteed high prices.
*   **Extermination:** Clear bandit camps or ruins that are actively depressing the local stability score.

---

The goal is a world where if you burn a forest down, the local lumber mill shuts down, the price of wood in the neighboring city triples, the construction of their new walls halts, and a rival faction decides *now* is the perfect time to invade.

**Falling Leaves** is currently in development. I'd love to hear your thoughts on these systems!
