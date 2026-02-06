# Combat & Warfare Systems (Deep-Dive)

This document provides a comprehensive technical overview of the *Falling Leaves* combat engine, ranging from strategic army resolution to the granular anatomy-based tactical simulation.

---

## 1. Tactical Combat: The Anatomy System
Tactical combat is a high-fidelity simulation where units are not represented by a single "Hit Point" bar, but by a complex physiological anatomy.

### A. Body Structure & Tissue Layers
Every unit (`GDUnit`) possesses a detailed body map comprising multiple parts and layers:
*   **Body Parts**: Head, Neck, Torso, Left/Right Arms, Hands, Legs, Feet, and internal organs (Brain, Heart, Spine, Lungs, Gut, Liver).
*   **Tissue Layers**: Each limb consists of overlapping layers with unique physical properties:
    *   **Skin/Fat/Muscle**: Common external layers.
    *   **Bone**: Provides structure; breaking a bone in a limb causes it to become **non-functional**.
    *   **Nervous System (Spine/Nerves)**: Severing the spine results in immediate **Paralysis**.
    *   **Internal Organs**: Organs (like the Brain or Heart) are nested *inside* other parts (Head/Torso) and are only hit if the weapon penetrates deep enough.

### B. The Vitality Model
Units die or become incapacitated through physiological failure:
*   **Blood System**: Units have a precise blood volume (baseline 5000ml). 
    *   **Bleed Rate**: Damage to tissues causes a constant bleed rate (ml/turn).
    *   **Arterial Damage**: Hitting major arteries (in the neck or inner thigh) causes extreme bleeding that can kill a unit in just a few turns.
    *   **Visual States**: Blood loss leads to **Pale** (75%), **Faint** (50%), and eventually death.
*   **Death Conditions**:
    1.  Blood volume reaches 0 (Exsanguination).
    2.  Critical organ failure (Heart/Brain destroyed).
    3.  **Decapitation**: Immediate death.
    4.  **Vital Structure Collapse**: Total tissue HP of a critical part (Torso) reaches 0.

---

## 2. Weaponry & Damage Physics
Damage is calculated using a physics-weighted model that considers material hardness, contact area, and momentum.

### A. The "Real Damage" Formula
Unlike many RPGs where damage is a flat number, *Falling Leaves* calculates impact energy:

$$
\text{Momentum} = \text{Weight} \times \text{Velocity}
$$
$$
\text{Velocity} = \frac{1.0}{\text{Action Speed}} + (\text{Bow Tension} \text{ if ranged})
$$
$$
\text{Damage} = (\text{Base Damage} + (\text{Momentum} \times 2.0)) \times \text{Strength Multiplier}
$$

*   **Momentum Matters**: A heavy weapon swung slowly might have the same momentum as a light weapon swung fast, but the *type* of damage (Blunt vs Cut) changes how that energy is applied.

### B. Material Hardness & Matchups
The material of the weapon vs. the armor is critical. The simulation compares the `hardness` of the two materials:
*   **Inferior Material (0.4x Multiplier)**: Hitting Steel armor with an Iron weapon results in a massive 60% penalty to damage. The weapon effectively bounces off.
*   **Superior Material (1.2x Multiplier)**: Hitting Leather armor with a Steel sword grants a 20% bonus as the blade shears through easily.

### C. Specific Attacks
Most weapons have multiple attack modes. For example, a **Longsword** can:
*   **Slash**: CUT damage. High contact area (30), moderate penetration (15). Good against flesh.
*   **Thrust**: PIERCE damage. Low contact area (2), high penetration (40). Designed to punch through armor gaps.
*   **Mordhau**: BLUNT damage. Gripping the blade to hit with the pommel. Low contact area but avoids the "Cut vs Plate" penalty.

---

## 3. Armor Layering & Penetration
Armor is processed in four distinct layers from outside-in:
1.  **Cover**: (e.g., Cloaks, Surcoats).
2.  **Armor**: (e.g., Breastplate, Brigandine, Greaves).
3.  **Over**: (e.g., Gambeson, Hauberk).
4.  **Under**: (e.g., Tunic, Shirt).

### The Absorbtion Logic
When a hit occurs, the damage must pass through *each* layer sequentially.
*   **Blunt Impact**: Checks `impact_yield`. Damage is reduced based on `Contact Area`. Wide weapons (Mauls) transfer more force *through* armor than small ones (Warhammers).
*   **Shear/Pierce**: Checks `shear_yield`. Damage is reduced based on `Penetration Depth`. High penetration values (Estocs, Picks) divide the armor's effective yield, allowing them to ignore protection.

---

## 4. Siege Engines & AOE
Siege weapons function on a catastrophic damage scale.
*   **Bone Fracture**: Siege engines deal double damage to bone, almost guaranteeing fractures on impact.
*   **Structural Damage**: Engines like Trebuchets and Catapults deal massive damage to buildings, calculated by a separate `resolve_engine_damage` system that tracks specific wall HP.

---

## 5. Strategic Resolution (AI vs AI)
For battles the player isn't witnessing, the game uses a **Strength Resolution** model (`CombatManager.gd`):
*   **Army Strength**: Calculated from total unit counts * tier quality * current health.
*   **Loss Ratios**: Calculated by the difference in strength. If an army is severely outnumbered, it might suffer 80% losses in a single pulse.
*   **Lords & Caravans**: These are special entities that don't "die" easily. Lords flee to their **Home Fief** to rebuild, while Caravans have a 30% "Scatter Chance" to survive an ambush.
*   **Siege Walls**: Fortifications provide a base 10x multiplier to the garrison's strength, making even small towns extremely difficult to capture without massive numerical superiority.

---

## 6. Detailed Warfare & Military Campaigns
Warfare in *Falling Leaves* is not just a series of random skirmishes, but a coordinated effort by faction leaders to expand their borders.

### A. Diplomacy & War Declaration
The `WarManager` handles the high-level logic of state relations:
*   **Weekly Diplomacy Checks**: Every 7 days, factions check their relations. There is a base 12% chance for neutral nations to declare war based on old rivalries.
*   **Peacemaking**: Wars are long and grueling, with only a 5% weekly chance for a peace treaty to be bartered.

### B. The Marshal & Campaigns
Factions launch **Military Campaigns** to seize territory:
*   **The Gathering**: A Campaign starts in a "gathering" state at a friendly border settlement. The Marshal waits for at least 2 Lords or a combined strength of 3500 power to assemble.
*   **The March**: Once the force is gathered, they move in unison towards a target city.
*   **Targeting Logic**: The AI identifies the most "vulnerable" enemy city, calculating a score based on distance to their borders and the current garrison size.

---

## 7. Siege Mechanics: Holding the Walls
Sieges are tests of endurance, where time is as much a weapon as the sword.

### A. Strategic Fortifications
A settlement's defense is heavily influenced by its **Defense Pillar** buildings:
*   **Wall Multipliers**: Walls provide a massive base strength multiplier to the garrison (3x + 3x per level). A level 10 wall provides a crushing advantage.
*   **Garrison Quality**: The effectiveness of each defender is boosted by **Barracks** (+1.5 per level) and **Training Grounds** (+0.8 per level).

### B. Siege Milestones
As walls are upgraded, they unlock unique defensive capabilities:
*   **Level 3 (Towers)**: Reduces incoming attacker power by 25%.
*   **Level 7 (Engines)**: Defensive artillery has a 10% chance per day to deal 20% HP damage to every unit in the attacking army.
*   **Level 9 (Moat)**: A massive 50% reduction to attacker effectiveness.

### C. Breach & Capture
The `resolve_siege` system runs a daily check:
*   **Breach Chance**: Calculated as `(Attacker_Str / Defender_Str) * 0.15 * (1.0 + Days_Under_Siege / 4.0)`.
*   **Starvation**: Each day under siege increases the attacker's "Starvation Multiplier" (+5% per day), representing the defenders growing weak.
*   **The Fall**: If a breach occurs or the attacker has 2.5x the defender's modified strength, the city is captured. 
*   **Spoils & Occupation**: Half of the capturing army stays behind to form the new garrison, and the attackers loot the city's crown stock and population value.

---

## 8. Battle Clusters & Reinforcements
Combat is spatially aware. Both in the open field and during sieges, the game uses **Aggregation**:
*   **2-Tile Radius**: Any friendly army, lord, or caravan within a Chebyshev distance of 2 tiles is automatically pulled into the battle.
*   **Loot Distribution**: Spoils of war (Crowns and Renown) are split equally among all participating victors.
*   **Retreat & Respawn**: When a Lord is defeated, they aren't killed permanently. They enter a 48-hour "respawn" period and retreat to their **Home Fief** to rebuild their roster.

