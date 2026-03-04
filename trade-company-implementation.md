# Trade Company System Implementation Plan

## Overview
Replace the current greedy local trade party spawner with a hub-and-spoke network driven by persistent Trade Company agents. This creates a scalable, interactive, and historically realistic trade system.

---

## Architecture Summary

### Three-Layer Design
1. **Trade Routes** (Infrastructure) - Persistent world features connecting hubs
2. **Trade Companies** (Agents) - AI entities that make trade decisions
3. **Caravans** (Actors) - Physical units moving goods along routes

### Key Benefits Over Current System
- Scales to 500+ settlements (O(companies) not O(settlements²))
- Multi-hop routing emerges naturally
- Player can found companies, raid caravans, build infrastructure
- Strategic chokepoints and economic warfare
- Emergent supply chains and monopolies

---

## Phase 1: Core Foundation (Week 1-2)

### 1.1 Create TradeRoute Class
**File:** `src/simulation/economy/trade_route.gd`

```gdscript
class_name TradeRoute
extends RefCounted

var route_id: String = ""
var origin_hub_id: String = ""      # Settlement ID (tier 3+)
var destination_hub_id: String = "" # Settlement ID (tier 3+)
var path: Array = []                # Array of [x,y] coordinates
var distance: float = 0.0           # Total distance in tiles
var capacity: int = 5               # Max caravans per day
var danger_level: float = 0.1       # 0.0-1.0 (bandit/war risk)
var established_year: int = 0       # For historical tracking
var upgrades: Array[String] = []    # ["paved_road", "waystation"]

# Serialize for save/load
func to_dict() -> Dictionary:
    return {
        "route_id": route_id,
        "origin_hub_id": origin_hub_id,
        "destination_hub_id": destination_hub_id,
        "path": path,
        "distance": distance,
        "capacity": capacity,
        "danger_level": danger_level,
        "established_year": established_year,
        "upgrades": upgrades,
    }

static func from_dict(data: Dictionary) -> TradeRoute:
    var route = TradeRoute.new()
    route.route_id = data.get("route_id", "")
    route.origin_hub_id = data.get("origin_hub_id", "")
    route.destination_hub_id = data.get("destination_hub_id", "")
    route.path = data.get("path", [])
    route.distance = data.get("distance", 0.0)
    route.capacity = data.get("capacity", 5)
    route.danger_level = data.get("danger_level", 0.1)
    route.established_year = data.get("established_year", 0)
    route.upgrades = data.get("upgrades", [])
    return route
```

**Tasks:**
- [ ] Create the file with class definition
- [ ] Add serialization methods (to_dict/from_dict)
- [ ] Add method: `get_travel_time() -> int` (distance / speed_modifier)
- [ ] Add method: `get_effective_capacity() -> int` (base_capacity × upgrade_multipliers)
- [ ] Add method: `get_transport_cost(cargo_value: float) -> float`

### 1.2 Create TradeCompany Class
**File:** `src/simulation/economy/trade_company.gd`

```gdscript
class_name TradeCompany
extends RefCounted

var company_id: String = ""
var name: String = "Unnamed Company"
var headquarters_id: String = ""        # Settlement ID
var branch_offices: Array[String] = []  # Settlement IDs (tier 2+)
var capital: float = 1000.0             # Starting funds
var owned_caravan_ids: Array[String] = []
var reputation: Dictionary = {}         # {faction_id: int score}
var specialization: String = ""         # "grain", "timber", "iron", "luxury", "general"
var ai_personality: String = "balanced" # "opportunist", "reliable", "cautious", "aggressive"
var owner_type: String = "npc"          # "npc" or "player"
var founded_year: int = 0
var trade_memory: Dictionary = {}       # {good_id: {best_source_id, best_buyer_id, avg_profit}}

func to_dict() -> Dictionary:
    return {
        "company_id": company_id,
        "name": name,
        "headquarters_id": headquarters_id,
        "branch_offices": branch_offices,
        "capital": capital,
        "owned_caravan_ids": owned_caravan_ids,
        "reputation": reputation,
        "specialization": specialization,
        "ai_personality": ai_personality,
        "owner_type": owner_type,
        "founded_year": founded_year,
        "trade_memory": trade_memory,
    }

static func from_dict(data: Dictionary) -> TradeCompany:
    var company = TradeCompany.new()
    company.company_id = data.get("company_id", "")
    company.name = data.get("name", "Unnamed Company")
    company.headquarters_id = data.get("headquarters_id", "")
    company.branch_offices = data.get("branch_offices", [])
    company.capital = data.get("capital", 1000.0)
    company.owned_caravan_ids = data.get("owned_caravan_ids", [])
    company.reputation = data.get("reputation", {})
    company.specialization = data.get("specialization", "general")
    company.ai_personality = data.get("ai_personality", "balanced")
    company.owner_type = data.get("owner_type", "npc")
    company.founded_year = data.get("founded_year", 0)
    company.trade_memory = data.get("trade_memory", {})
    return company
```

**Tasks:**
- [ ] Create the file with class definition
- [ ] Add serialization methods
- [ ] Add method: `can_afford_trade(cargo_cost: float) -> bool`
- [ ] Add method: `add_reputation(faction_id: String, delta: int)`
- [ ] Add method: `get_preferred_goods() -> Array[String]` (based on specialization)

### 1.3 Create Caravan Class
**File:** `src/simulation/economy/caravan.gd`

```gdscript
class_name Caravan
extends RefCounted

var caravan_id: String = ""
var owner_company_id: String = ""
var current_route_id: String = ""
var cargo: Dictionary = {}              # {good_id: quantity}
var cargo_value: float = 0.0            # Total value of goods
var guards: int = 0                     # Military escort size
var position_x: int = 0                 # Current tile
var position_y: int = 0
var path_index: int = 0                 # Progress along route
var ai_state: String = "traveling"      # "traveling", "trading", "waiting", "sheltering"
var origin_settlement_id: String = ""
var destination_settlement_id: String = ""
var days_in_transit: int = 0

func to_dict() -> Dictionary:
    return {
        "caravan_id": caravan_id,
        "owner_company_id": owner_company_id,
        "current_route_id": current_route_id,
        "cargo": cargo,
        "cargo_value": cargo_value,
        "guards": guards,
        "position_x": position_x,
        "position_y": position_y,
        "path_index": path_index,
        "ai_state": ai_state,
        "origin_settlement_id": origin_settlement_id,
        "destination_settlement_id": destination_settlement_id,
        "days_in_transit": days_in_transit,
    }

static func from_dict(data: Dictionary) -> Caravan:
    var caravan = Caravan.new()
    caravan.caravan_id = data.get("caravan_id", "")
    caravan.owner_company_id = data.get("owner_company_id", "")
    caravan.current_route_id = data.get("current_route_id", "")
    caravan.cargo = data.get("cargo", {})
    caravan.cargo_value = data.get("cargo_value", 0.0)
    caravan.guards = data.get("guards", 0)
    caravan.position_x = data.get("position_x", 0)
    caravan.position_y = data.get("position_y", 0)
    caravan.path_index = data.get("path_index", 0)
    caravan.ai_state = data.get("ai_state", "traveling")
    caravan.origin_settlement_id = data.get("origin_settlement_id", "")
    caravan.destination_settlement_id = data.get("destination_settlement_id", "")
    caravan.days_in_transit = data.get("days_in_transit", 0)
    return caravan
```

**Tasks:**
- [ ] Create the file with class definition
- [ ] Add serialization methods
- [ ] Add method: `get_total_cargo_weight() -> float`
- [ ] Add method: `is_at_destination() -> bool`

### 1.4 Add Fields to WorldState
**File:** `src/simulation/world/world_state.gd`

Add to existing class:
```gdscript
var trade_routes: Dictionary = {}      # {route_id: TradeRoute}
var trade_companies: Dictionary = {}   # {company_id: TradeCompany}
var active_caravans: Dictionary = {}   # {caravan_id: Caravan}
```

**Tasks:**
- [ ] Add the three new dictionaries to WorldState
- [ ] Update `to_dict()` to serialize trade_routes, trade_companies, active_caravans
- [ ] Update `from_dict()` to deserialize them
- [ ] Update `clear()` method to reset these dictionaries

---

## Phase 2: Route Generation (Week 2)

### 2.1 Create TradeRouteGenerator
**File:** `src/worldgen/trade_route_generator.gd`

```gdscript
class_name TradeRouteGenerator
extends RefCounted

## Generates trade routes connecting tier 3+ settlements during worldgen.
## Routes follow geography (prefer roads, rivers, avoid mountains).

static func generate_all_routes(world_state: WorldState, world_seed: int) -> void:
    var hubs: Array = _find_hub_settlements(world_state)
    var rng := RandomNumberGenerator.new()
    rng.seed = world_seed + 999  # Offset from main worldgen seed
    
    # Connect each hub to its N nearest neighbors
    for hub in hubs:
        var nearby_hubs := _find_nearest_hubs(hub, hubs, 3)  # 3 nearest
        for target in nearby_hubs:
            if _should_create_route(hub, target, world_state, rng):
                _create_route(hub, target, world_state, rng)

static func _find_hub_settlements(ws: WorldState) -> Array:
    var hubs: Array = []
    for sid: String in ws.settlements.keys():
        var ss := ws.settlements.get(sid)
        if ss is SettlementState and ss.tier >= 3:
            hubs.append(ss)
    return hubs

static func _find_nearest_hubs(origin, all_hubs: Array, count: int) -> Array:
    var distances: Array = []
    for hub in all_hubs:
        if hub.settlement_id == origin.settlement_id:
            continue
        var dist := Vector2i(origin.tile_x, origin.tile_y).distance_to(
            Vector2i(hub.tile_x, hub.tile_y)
        )
        distances.append({"hub": hub, "distance": dist})
    
    distances.sort_custom(func(a, b): return a.distance < b.distance)
    
    var result: Array = []
    for i in range(mini(count, distances.size())):
        result.append(distances[i].hub)
    return result

static func _should_create_route(origin, target, ws: WorldState, rng: RandomNumberGenerator) -> bool:
    # Don't duplicate routes
    var existing_route_id := _route_key(origin.settlement_id, target.settlement_id)
    if ws.trade_routes.has(existing_route_id):
        return false
    
    # Check if reverse route exists
    var reverse_route_id := _route_key(target.settlement_id, origin.settlement_id)
    if ws.trade_routes.has(reverse_route_id):
        return false
    
    # Don't create extremely long routes (>50 tiles)
    var dist := Vector2i(origin.tile_x, origin.tile_y).distance_to(
        Vector2i(target.tile_x, target.tile_y)
    )
    if dist > 50.0:
        return false
    
    return true

static func _create_route(origin, target, ws: WorldState, rng: RandomNumberGenerator) -> void:
    var route := TradeRoute.new()
    route.route_id = EntityRegistry.generate_id("trade_route")
    route.origin_hub_id = origin.settlement_id
    route.destination_hub_id = target.settlement_id
    
    # Calculate path using existing route system or A*
    var path_data := _calculate_path(origin, target, ws)
    route.path = path_data.path
    route.distance = path_data.distance
    
    # Set danger based on terrain/factions
    route.danger_level = _calculate_danger(origin, target, ws, rng)
    
    # Base capacity scales with settlement tiers
    route.capacity = (origin.tier + target.tier) / 2  # Average tier
    
    route.established_year = ws.current_year
    
    ws.trade_routes[route.route_id] = route

static func _calculate_path(origin, target, ws: WorldState) -> Dictionary:
    # TODO: Implement A* pathfinding or use existing route system
    # For now, simple straight line placeholder
    var start := Vector2i(origin.tile_x, origin.tile_y)
    var end := Vector2i(target.tile_x, target.tile_y)
    
    return {
        "path": [start, end],  # Placeholder - needs real pathfinding
        "distance": start.distance_to(end)
    }

static func _calculate_danger(origin, target, ws: WorldState, rng: RandomNumberGenerator) -> float:
    # Base danger
    var danger := 0.1
    
    # Different faction = more danger
    if origin.faction_id != target.faction_id:
        danger += 0.2
    
    # Random variation
    danger += rng.randf_range(-0.05, 0.05)
    
    return clampf(danger, 0.0, 0.9)

static func _route_key(origin_id: String, dest_id: String) -> String:
    return origin_id + "_to_" + dest_id
```

**Tasks:**
- [ ] Create the file with route generation logic
- [ ] Integrate A* pathfinding or use existing route system from `ws.routes`
- [ ] Add terrain-based danger calculation (mountains = higher danger)
- [ ] Call `TradeRouteGenerator.generate_all_routes()` during worldgen
- [ ] Test with 20-30 settlements to verify route mesh

### 2.2 Hook Into Worldgen
**File:** `src/worldgen/world_generator.gd` (or wherever worldgen happens)

**Tasks:**
- [ ] Find where settlements are finalized (after tier assignment)
- [ ] Add call: `TradeRouteGenerator.generate_all_routes(world_state, world_seed)`
- [ ] Verify routes appear in WorldState.trade_routes after gen

---

## Phase 3: Company Generation (Week 3)

### 3.1 Create CompanyGenerator
**File:** `src/worldgen/company_generator.gd`

```gdscript
class_name CompanyGenerator
extends RefCounted

## Generates 5-10 NPC trade companies during worldgen.
## Each company gets a headquarters in a tier 3+ city.

static func generate_companies(world_state: WorldState, world_seed: int) -> void:
    var rng := RandomNumberGenerator.new()
    rng.seed = world_seed + 1337
    
    var hubs := _find_eligible_hqs(world_state)
    if hubs.is_empty():
        push_warning("No tier 3+ settlements for trade companies")
        return
    
    # Generate 5-10 companies (or 1 per 30 settlements)
    var company_count := clampi(
        world_state.settlements.size() / 30,
        5,
        10
    )
    
    for i in range(company_count):
        var hq := hubs[rng.randi() % hubs.size()]
        _create_company(hq, world_state, rng)

static func _find_eligible_hqs(ws: WorldState) -> Array:
    var eligible: Array = []
    for sid: String in ws.settlements.keys():
        var ss := ws.settlements.get(sid)
        if ss is SettlementState and ss.tier >= 3:
            eligible.append(ss)
    return eligible

static func _create_company(hq_settlement, ws: WorldState, rng: RandomNumberGenerator) -> void:
    var company := TradeCompany.new()
    company.company_id = EntityRegistry.generate_id("trade_company")
    company.name = _generate_company_name(hq_settlement, rng)
    company.headquarters_id = hq_settlement.settlement_id
    company.capital = rng.randf_range(5000.0, 20000.0)
    company.specialization = _random_specialization(rng)
    company.ai_personality = _random_personality(rng)
    company.owner_type = "npc"
    company.founded_year = ws.current_year - rng.randi_range(0, 50)  # Some history
    
    # Assign 2-5 branch offices in nearby tier 2+ cities
    var branches := _find_branch_offices(hq_settlement, ws, rng)
    company.branch_offices = branches
    
    # Start with 2-4 idle caravans
    var caravan_count := rng.randi_range(2, 4)
    for i in range(caravan_count):
        var caravan := _create_idle_caravan(company, hq_settlement, ws)
        company.owned_caravan_ids.append(caravan.caravan_id)
        ws.active_caravans[caravan.caravan_id] = caravan
    
    ws.trade_companies[company.company_id] = company

static func _generate_company_name(hq, rng: RandomNumberGenerator) -> String:
    var prefixes := ["Royal", "Imperial", "Golden", "Silver", "Grand", "Eastern", "Western"]
    var suffixes := ["Trading Co.", "Merchant Guild", "Company", "Traders", "Consortium"]
    
    var prefix := prefixes[rng.randi() % prefixes.size()]
    var suffix := suffixes[rng.randi() % suffixes.size()]
    
    return "%s %s %s" % [prefix, hq.name, suffix]

static func _random_specialization(rng: RandomNumberGenerator) -> String:
    var specs := ["grain", "timber", "iron", "luxury", "general"]
    return specs[rng.randi() % specs.size()]

static func _random_personality(rng: RandomNumberGenerator) -> String:
    var personalities := ["opportunist", "reliable", "cautious", "aggressive"]
    return personalities[rng.randi() % personalities.size()]

static func _find_branch_offices(hq, ws: WorldState, rng: RandomNumberGenerator) -> Array[String]:
    var candidates: Array = []
    var hq_pos := Vector2i(hq.tile_x, hq.tile_y)
    
    for sid: String in ws.settlements.keys():
        var ss := ws.settlements.get(sid)
        if not (ss is SettlementState):
            continue
        if ss.settlement_id == hq.settlement_id:
            continue
        if ss.tier < 2:  # Only tier 2+ get branch offices
            continue
        
        var dist := hq_pos.distance_to(Vector2i(ss.tile_x, ss.tile_y))
        if dist < 30.0:  # Within 30 tiles
            candidates.append(ss)
    
    # Pick 2-5 random branches
    var branch_ids: Array[String] = []
    var count := mini(rng.randi_range(2, 5), candidates.size())
    for i in range(count):
        var idx := rng.randi() % candidates.size()
        branch_ids.append(candidates[idx].settlement_id)
        candidates.remove_at(idx)
    
    return branch_ids

static func _create_idle_caravan(company: TradeCompany, hq, ws: WorldState) -> Caravan:
    var caravan := Caravan.new()
    caravan.caravan_id = EntityRegistry.generate_id("caravan")
    caravan.owner_company_id = company.company_id
    caravan.position_x = hq.tile_x
    caravan.position_y = hq.tile_y
    caravan.ai_state = "waiting"
    caravan.guards = 5  # Base guards
    return caravan
```

**Tasks:**
- [ ] Create the file with company generation logic
- [ ] Add more company name templates for variety
- [ ] Call `CompanyGenerator.generate_companies()` after route generation in worldgen
- [ ] Test: Verify 5-10 companies exist in WorldState after gen

---

## Phase 4: Company AI Decision System (Week 3-4)

### 4.1 Create CompanyAI
**File:** `src/simulation/economy/company_ai.gd`

```gdscript
class_name CompanyAI
extends RefCounted

## AI brain for NPC trade companies.
## Evaluates trade opportunities and dispatches caravans.

## Called weekly for each company
static func evaluate_and_dispatch(company: TradeCompany, ws: WorldState) -> void:
    if company.owner_type == "player":
        return  # Player controls their own companies
    
    # Find idle caravans
    var idle_caravans := _get_idle_caravans(company, ws)
    if idle_caravans.is_empty():
        return  # No caravans available
    
    # Evaluate all possible trades
    var opportunities := _find_trade_opportunities(company, ws)
    if opportunities.is_empty():
        return  # No profitable trades
    
    # Sort by profitability (highest first)
    opportunities.sort_custom(func(a, b): return a.profit > b.profit)
    
    # Dispatch caravans to top opportunities
    for i in range(mini(idle_caravans.size(), opportunities.size())):
        var caravan: Caravan = idle_caravans[i]
        var trade = opportunities[i]
        _dispatch_caravan(caravan, trade, company, ws)

static func _get_idle_caravans(company: TradeCompany, ws: WorldState) -> Array:
    var idle: Array = []
    for caravan_id in company.owned_caravan_ids:
        var caravan = ws.active_caravans.get(caravan_id)
        if caravan != null and caravan.ai_state == "waiting":
            idle.append(caravan)
    return idle

static func _find_trade_opportunities(company: TradeCompany, ws: WorldState) -> Array:
    var opportunities: Array = []
    
    # Check all branch offices as potential origins
    var origins := [company.headquarters_id] + company.branch_offices
    
    for origin_id in origins:
        var origin_ss := ws.settlements.get(origin_id)
        if not (origin_ss is SettlementState):
            continue
        
        # Find goods to buy at origin
        var tradeable_goods := _get_tradeable_goods(origin_ss, company)
        
        for good_id in tradeable_goods:
            # Find best destination for this good
            var best_trade := _evaluate_good_trade(
                origin_id, good_id, company, ws
            )
            if best_trade != null and best_trade.profit > 0:
                opportunities.append(best_trade)
    
    return opportunities

static func _get_tradeable_goods(ss: SettlementState, company: TradeCompany) -> Array[String]:
    var goods: Array[String] = []
    
    # Check inventory for surplus goods
    for good_id in ss.inventory.keys():
        var stock: float = ss.inventory.get(good_id, 0.0)
        if stock < 50.0:  # Minimum threshold
            continue
        
        # Filter by specialization
        if company.specialization != "general":
            if not _matches_specialization(good_id, company.specialization):
                continue
        
        goods.append(good_id)
    
    return goods

static func _matches_specialization(good_id: String, spec: String) -> bool:
    match spec:
        "grain": return good_id in ["wheat_bushel"]
        "timber": return good_id in ["timber_log"]
        "iron": return good_id in ["iron_ore", "iron_ingot"]
        "luxury": return good_id in ["cloth", "wine", "jewelry"]
        _: return true

static func _evaluate_good_trade(
    origin_id: String,
    good_id: String,
    company: TradeCompany,
    ws: WorldState
) -> Dictionary:
    var origin_ss := ws.settlements.get(origin_id)
    if not (origin_ss is SettlementState):
        return {}
    
    var buy_price: float = origin_ss.prices.get(good_id, 10.0)
    var cargo_qty: float = 50.0  # Fixed cargo size for now
    
    # Check all accessible destinations
    var best_profit: float = -999999.0
    var best_dest_id: String = ""
    var best_route_id: String = ""
    
    var destinations := company.branch_offices + [company.headquarters_id]
    for dest_id in destinations:
        if dest_id == origin_id:
            continue
        
        var dest_ss := ws.settlements.get(dest_id)
        if not (dest_ss is SettlementState):
            continue
        
        # Check if a route exists
        var route := _find_route_between(origin_id, dest_id, ws)
        if route == null:
            continue
        
        # Calculate profit
        var sell_price: float = dest_ss.prices.get(good_id, buy_price)
        var transport_cost: float = route.distance * 0.02 * cargo_qty * buy_price
        var risk_cost: float = cargo_qty * buy_price * route.danger_level * 0.1
        var profit: float = (sell_price - buy_price) * cargo_qty - transport_cost - risk_cost
        
        if profit > best_profit:
            best_profit = profit
            best_dest_id = dest_id
            best_route_id = route.route_id
    
    if best_dest_id.is_empty():
        return {}
    
    return {
        "origin_id": origin_id,
        "dest_id": best_dest_id,
        "good_id": good_id,
        "cargo_qty": cargo_qty,
        "buy_price": buy_price,
        "profit": best_profit,
        "route_id": best_route_id,
    }

static func _find_route_between(origin_id: String, dest_id: String, ws: WorldState) -> TradeRoute:
    # Check direct route
    for route_id in ws.trade_routes.keys():
        var route: TradeRoute = ws.trade_routes[route_id]
        if (route.origin_hub_id == origin_id and route.destination_hub_id == dest_id) or \
           (route.origin_hub_id == dest_id and route.destination_hub_id == origin_id):
            return route
    
    # TODO: Multi-hop routing (Phase 5)
    return null

static func _dispatch_caravan(
    caravan: Caravan,
    trade: Dictionary,
    company: TradeCompany,
    ws: WorldState
) -> void:
    var origin_ss := ws.settlements.get(trade.origin_id)
    if not (origin_ss is SettlementState):
        return
    
    # Check if company can afford cargo
    var cargo_cost: float = trade.cargo_qty * trade.buy_price
    if company.capital < cargo_cost:
        return  # Can't afford
    
    # Deduct funds and inventory
    company.capital -= cargo_cost
    origin_ss.inventory[trade.good_id] = maxf(
        origin_ss.inventory.get(trade.good_id, 0.0) - trade.cargo_qty,
        0.0
    )
    
    # Load caravan
    caravan.cargo[trade.good_id] = trade.cargo_qty
    caravan.cargo_value = cargo_cost
    caravan.current_route_id = trade.route_id
    caravan.origin_settlement_id = trade.origin_id
    caravan.destination_settlement_id = trade.dest_id
    caravan.ai_state = "traveling"
    caravan.path_index = 0
    caravan.days_in_transit = 0
    
    # Set position to origin
    caravan.position_x = origin_ss.tile_x
    caravan.position_y = origin_ss.tile_y
```

**Tasks:**
- [ ] Create the file with AI decision logic
- [ ] Add personality modifiers (aggressive = higher risk tolerance)
- [ ] Add specialization bonuses (grain traders know grain markets better)
- [ ] Test: Spawn 5 companies, run 10 weeks, verify caravans dispatch

---

## Phase 5: Caravan Movement System (Week 4)

### 5.1 Create CaravanCore
**File:** `src/simulation/economy/caravan_core.gd`

```gdscript
class_name CaravanCore
extends RefCounted

## Handles daily movement of all active caravans.
## Called once per day after SettlementPulse.

static func tick_all_caravans(ws: WorldState, tick: int) -> void:
    for caravan_id in ws.active_caravans.keys():
        var caravan: Caravan = ws.active_caravans[caravan_id]
        if caravan.ai_state == "traveling":
            _move_caravan(caravan, ws)

static func _move_caravan(caravan: Caravan, ws: WorldState) -> void:
    var route: TradeRoute = ws.trade_routes.get(caravan.current_route_id)
    if route == null:
        caravan.ai_state = "waiting"  # Route doesn't exist?
        return
    
    caravan.days_in_transit += 1
    
    # Move along path (1 tile per day, can be adjusted)
    if caravan.path_index < route.path.size() - 1:
        caravan.path_index += 1
        var next_pos = route.path[caravan.path_index]
        caravan.position_x = next_pos[0]
        caravan.position_y = next_pos[1]
    else:
        # Arrived at destination
        _deliver_cargo(caravan, ws)

static func _deliver_cargo(caravan: Caravan, ws: WorldState) -> void:
    var dest_ss := ws.settlements.get(caravan.destination_settlement_id)
    if not (dest_ss is SettlementState):
        # Destination doesn't exist? Return caravan to waiting
        caravan.ai_state = "waiting"
        caravan.cargo.clear()
        return
    
    var company: TradeCompany = _get_owning_company(caravan, ws)
    if company == null:
        return
    
    # Sell cargo at destination
    var total_revenue: float = 0.0
    for good_id in caravan.cargo.keys():
        var qty: float = caravan.cargo[good_id]
        var sell_price: float = dest_ss.prices.get(good_id, 10.0)
        var revenue: float = qty * sell_price
        
        # Add to destination inventory
        dest_ss.inventory[good_id] = dest_ss.inventory.get(good_id, 0.0) + qty
        
        total_revenue += revenue
    
    # Company receives revenue
    company.capital += total_revenue
    
    # Update trade memory (learning)
    for good_id in caravan.cargo.keys():
        company.trade_memory[good_id] = {
            "best_source_id": caravan.origin_settlement_id,
            "best_buyer_id": caravan.destination_settlement_id,
            "avg_profit": total_revenue - caravan.cargo_value,
        }
    
    # Reset caravan to waiting state at destination
    caravan.cargo.clear()
    caravan.cargo_value = 0.0
    caravan.ai_state = "waiting"
    caravan.current_route_id = ""
    caravan.days_in_transit = 0
    
    # Position at destination
    caravan.position_x = dest_ss.tile_x
    caravan.position_y = dest_ss.tile_y

static func _get_owning_company(caravan: Caravan, ws: WorldState) -> TradeCompany:
    return ws.trade_companies.get(caravan.owner_company_id)
```

**Tasks:**
- [ ] Create the file with movement logic
- [ ] Hook into daily tick (after SettlementPulse) in Bootstrap
- [ ] Add random events (10% chance per day): bandit attack, weather delay
- [ ] Test: Dispatch caravan, verify it moves and delivers

### 5.2 Hook CaravanCore Into Tick System
**File:** `src/simulation/bootstrap.gd` (or wherever hooks are registered)

**Tasks:**
- [ ] Find where `SettlementPulse.tick_all()` is called
- [ ] Add call after it: `CaravanCore.tick_all_caravans(world_state, tick)`
- [ ] Verify caravans move daily in test runs

---

## Phase 6: Company Weekly Pulse (Week 4)

### 6.1 Hook CompanyAI Into Weekly Tick
**File:** `src/simulation/bootstrap.gd`

```gdscript
# Add a new hook that fires once per week (every 168 ticks / 7 days)
func _register_company_pulse(ws: WorldState) -> void:
    TickSystem.register_hook(
        TickPhase.COMPANY_DECISIONS,
        168,  # Strategic cadence: weekly
        func(tick: int):
            _company_weekly_pulse(ws, tick)
    )

func _company_weekly_pulse(ws: WorldState, tick: int) -> void:
    for company_id in ws.trade_companies.keys():
        var company: TradeCompany = ws.trade_companies[company_id]
        CompanyAI.evaluate_and_dispatch(company, ws)
```

**Tasks:**
- [ ] Add company decisions as a new tick phase (or use existing strategic phase)
- [ ] Register weekly hook for all companies
- [ ] Test: Run 4 weeks, verify companies dispatch caravans regularly

---

## Phase 7: Integration & Testing (Week 5)

### 7.1 End-to-End Test
**File:** `tests/test_trade_companies.gd`

```gdscript
extends BaseTest

func test_company_round_trip() -> void:
    var ws := _setup_test_world()
    
    # Week 0: Companies should dispatch caravans
    _advance_week(ws)
    assert_gt(ws.active_caravans.size(), 0, "Caravans should be dispatched")
    
    # Week 1-2: Caravans travel
    for i in range(2):
        _advance_week(ws)
    
    # Week 3: Some caravans should have delivered
    var company = ws.trade_companies.values()[0] as TradeCompany
    var initial_capital := 10000.0
    assert_true(company.capital != initial_capital, "Company capital should change")

func _setup_test_world() -> WorldState:
    var ws := WorldState.new()
    
    # Create 5 tier-3 settlements
    for i in range(5):
        var ss := SettlementState.new()
        ss.settlement_id = "city_%d" % i
        ss.tier = 3
        ss.tile_x = i * 10
        ss.tile_y = 0
        ss.inventory["wheat_bushel"] = 1000.0
        ss.prices["wheat_bushel"] = 10.0 + (i * 2.0)  # Price variation
        ws.settlements[ss.settlement_id] = ss
    
    # Generate routes and companies
    TradeRouteGenerator.generate_all_routes(ws, 12345)
    CompanyGenerator.generate_companies(ws, 12345)
    
    return ws

func _advance_week(ws: WorldState) -> void:
    for day in range(7):
        for tick in range(24):
            CaravanCore.tick_all_caravans(ws, day * 24 + tick)
    
    # Weekly company pulse
    for company_id in ws.trade_companies.keys():
        var company = ws.trade_companies[company_id]
        CompanyAI.evaluate_and_dispatch(company, ws)
```

**Tasks:**
- [ ] Create comprehensive test suite
- [ ] Test worldgen with 50, 100, 200 settlements
- [ ] Verify no crashes after 100 in-game weeks
- [ ] Profile performance (should be <1ms per frame for 500 caravans)

### 7.2 Deprecate Old System
**File:** `src/simulation/economy/trade_party_spawner.gd`

**Tasks:**
- [ ] Add deprecation notice at top of file
- [ ] Comment out the call to `TradePartySpawner.try_spawn_all()` in Bootstrap
- [ ] Keep file for reference but disable functionality
- [ ] Update documentation to point to new system

---

## Phase 8: Player Interaction - Founding Companies (Week 6)

### 8.1 Add "Found Company" UI Option
**File:** `src/ui/settlement_view.gd` (or wherever city UI exists)

**Tasks:**
- [ ] Add button: "💼 Found Trading Company (5000 coin)"
- [ ] Show dialog: Enter company name, choose specialization
- [ ] Call `PlayerActions.found_company(name, specialization)`
- [ ] Update player UI to show owned companies

### 8.2 Create PlayerActions for Companies
**File:** `src/simulation/economy/player_company_actions.gd`

```gdscript
class_name PlayerCompanyActions
extends RefCounted

static func found_company(
    player_id: String,
    company_name: String,
    specialization: String,
    hq_settlement_id: String,
    ws: WorldState
) -> bool:
    # Check if player has funds (assumes player has a capital field)
    var founding_cost := 5000.0
    # TODO: Deduct from player.capital or similar
    
    var company := TradeCompany.new()
    company.company_id = EntityRegistry.generate_id("trade_company")
    company.name = company_name
    company.headquarters_id = hq_settlement_id
    company.capital = founding_cost  # Starting capital
    company.specialization = specialization
    company.owner_type = "player"
    company.founded_year = ws.current_year
    
    # Start with 2 caravans
    for i in range(2):
        var caravan := _create_player_caravan(company, hq_settlement_id, ws)
        company.owned_caravan_ids.append(caravan.caravan_id)
        ws.active_caravans[caravan.caravan_id] = caravan
    
    ws.trade_companies[company.company_id] = company
    return true

static func _create_player_caravan(
    company: TradeCompany,
    hq_id: String,
    ws: WorldState
) -> Caravan:
    var hq_ss := ws.settlements.get(hq_id) as SettlementState
    var caravan := Caravan.new()
    caravan.caravan_id = EntityRegistry.generate_id("caravan")
    caravan.owner_company_id = company.company_id
    caravan.position_x = hq_ss.tile_x if hq_ss else 0
    caravan.position_y = hq_ss.tile_y if hq_ss else 0
    caravan.ai_state = "waiting"
    caravan.guards = 10  # Player caravans get more guards
    return caravan
```

**Tasks:**
- [ ] Create player action handler
- [ ] Integrate with player resource system (deduct founding cost)
- [ ] Add to player's owned companies list
- [ ] Test: Found company, verify it appears in WorldState

### 8.3 Company Management UI
**File:** `src/ui/company_management_panel.gd`

**Tasks:**
- [ ] Create new UI scene: Company overview (capital, caravans, reputation)
- [ ] Show active trades in progress
- [ ] Show profit/loss history (last 10 trades)
- [ ] Add button: "Dispatch Caravan" (manual trade orders for player)
- [ ] Test: Open UI, dispatch caravan manually

---

## Phase 9: Advanced Features (Week 7-8)

### 9.1 Caravan Raiding System
**File:** `src/simulation/combat/caravan_interception.gd`

**Tasks:**
- [ ] Detect when player party is within 5 tiles of caravan
- [ ] Show prompt: "⚔ Intercept Caravan"
- [ ] Start tactical combat: player vs caravan guards
- [ ] On victory: Transfer cargo to player inventory
- [ ] Reduce company reputation with caravan owner's faction
- [ ] Add to company's loss ledger

### 9.2 Route Upgrades
**File:** `src/simulation/economy/route_upgrades.gd`

**Tasks:**
- [ ] Add upgrade types: "paved_road" (+50% capacity), "waystation" (-30% danger)
- [ ] Create UI button in settlement view: "📍 Upgrade Trade Route"
- [ ] Cost: 5000 coin + 500 timber for paved road
- [ ] Apply multipliers to route capacity/danger when checking upgrades

### 9.3 Multi-Hop Routing
**File:** Update `src/simulation/economy/company_ai.gd`

**Tasks:**
- [ ] In `_find_route_between()`, implement breadth-first search through routes
- [ ] Allow caravans to stop at intermediate hubs
- [ ] Split delivery: unload partial cargo at each stop
- [ ] Test: Create chain A→B→C where C needs goods from A

---

## Phase 10: Polish & Balance (Week 8)

### 10.1 Economic Balance Pass

**Tasks:**
- [ ] Run 100-week simulation on 200-settlement world
- [ ] Measure: Average company profit per week (target: 500-2000 coin)
- [ ] Measure: Caravan utilization rate (target: 60-80% busy)
- [ ] Measure: Settlement starvation rate (should drop to <5%)
- [ ] Tune: cargo_qty, transport_cost, danger_level multipliers

### 10.2 Event System

**Tasks:**
- [ ] Add caravan events: "Bandits attack!" (10% chance in high danger zones)
- [ ] Add company events: "Economic boom!" (double profit for 30 days)
- [ ] Add route events: "Bridge collapse!" (route disabled for 90 days)
- [ ] Display events in game log

### 10.3 Historical Records

**Tasks:**
- [ ] Track company lifetime earnings
- [ ] Track most profitable routes (all-time)
- [ ] Generate "Famous Merchants" (NPCs who founded successful companies)
- [ ] Add achievements: "Merchant Prince" (10k profit in one trade)

---

## Success Criteria

✅ **Phase 1-5 Complete:** System replaces old trade spawner  
✅ **Phase 6 Complete:** Companies autonomously trade without player input  
✅ **Phase 7 Complete:** 200-settlement world runs stable for 100 weeks  
✅ **Phase 8 Complete:** Player can found and manage company  
✅ **Phase 9 Complete:** Player can raid caravans and upgrade routes  
✅ **Phase 10 Complete:** System is balanced and polished  

---

## File Structure Summary

```
src/
  simulation/
    economy/
      trade_route.gd          ← Phase 1
      trade_company.gd        ← Phase 1
      caravan.gd              ← Phase 1
      company_ai.gd           ← Phase 4
      caravan_core.gd         ← Phase 5
      player_company_actions.gd ← Phase 8
      route_upgrades.gd       ← Phase 9
    combat/
      caravan_interception.gd ← Phase 9
  worldgen/
    trade_route_generator.gd  ← Phase 2
    company_generator.gd      ← Phase 3
  ui/
    company_management_panel.gd ← Phase 8
tests/
  test_trade_companies.gd     ← Phase 7
```

---

## Migration Strategy

1. **Parallel Run (Weeks 1-5):** Keep old spawner active while building new system
2. **Testing (Weeks 5-6):** Run both systems, compare results
3. **Switchover (Week 7):** Disable old spawner, activate new system
4. **Deprecation (Week 8):** Remove old code after confirming stability

---

## Notes for Future AI Instructions

- All company IDs use `EntityRegistry.generate_id("trade_company")`
- Caravan movement is 1 tile per day (can tune with route upgrades)
- Player companies are excluded from AI (owner_type == "player")
- Reputation system uses integer scores (0-100, starts at 50)
- Trade memory persists across game saves
- Route danger affects both profit calculation and event probability
- Specialization gives +20% profit for matching goods

---

**This plan is modular:** Each phase can be implemented independently and tested before moving to the next. Start with Phase 1 and work sequentially.
