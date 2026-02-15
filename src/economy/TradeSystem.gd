extends RefCounted
class_name TradeSystem

## Trade System  
## Handles player trading, caravan trade networks, and market orders

# GameData and GameState are autoloads - no need to preload
const Globals = preload("res://src/core/Globals.gd")
const PricingSystem = preload("res://src/economy/PricingSystem.gd")

## Player buys resource from settlement
@warning_ignore("shadowed_global_identifier")
static func buy_resource(s_data, res_name, amount, player_obj):
	var price = PricingSystem.get_price(res_name, s_data)
	var total_cost = price * amount
	if player_obj.crowns >= total_cost and s_data.inventory.get(res_name, 0) >= amount:
		player_obj.crowns -= total_cost
		s_data.crown_stock += total_cost
		if amount > 0:
			s_data.inventory[res_name] -= amount
		player_obj.add_to_stash(res_name, amount)
		return true
	return false

## Player sells resource to settlement
@warning_ignore("shadowed_global_identifier")
static func sell_resource(s_data, res_name, amount, player_obj):
	var price = int(PricingSystem.get_price(res_name, s_data) * 0.7)
	var total_val = price * amount
	if s_data.crown_stock >= total_val and player_obj.get_stash_count(res_name) >= amount:
		s_data.crown_stock -= total_val
		player_obj.crowns += total_val
		s_data.inventory[res_name] = s_data.inventory.get(res_name, 0) + amount
		player_obj.remove_from_stash(res_name, amount)
		return true
	return false

## Update global trade network (caravans and contracts)
static func update_trade_networks(gs):
	# 1. Clean up old or expired contracts
	for i in range(gs.trade_contracts.size() - 1, -1, -1):
		var contract = gs.trade_contracts[i]
		
		# Validate that the assigned caravan is still pursuing this contract
		var caravan_active = false
		for c in gs.caravans:
			if c.get_instance_id() == contract.get("caravan_id", -1):
				if c.has_meta("contract_id") and c.get_meta("contract_id") == contract["id"]:
					if c.state != "idle":
						caravan_active = true
				break
		
		if not caravan_active:
			contract["status"] = "cancelled"

		if contract["status"] == "cancelled" or contract["status"] == "completed":
			gs.trade_contracts.remove_at(i)

	# 2. Check for new high-value "Matches"
	if gs.world_market_orders.is_empty(): return
	
	# Group caravans by origin to see who is available
	var available_caravans = []
	for c in gs.caravans:
		# If caravan lost its contract (e.g. killed/reloaded), reset it
		if c.state != "idle" and not gs.trade_contracts.any(func(con): return con.get("caravan_id") == c.get_instance_id()):
			c.state = "idle"
			c.target_pos = Vector2i(-1, -1)
			
		if c.state == "idle":
			available_caravans.append(c)
	
	if available_caravans.is_empty(): return
	
	# Process existing Buy Orders (Demands)
	var demands = gs.world_market_orders.duplicate()
	demands.sort_custom(func(a, b): return a["price_offered"] > b["price_offered"])
	
	for order in demands:
		if available_caravans.is_empty(): break
		
		# Skip if order is already fully matched by active contracts
		var matched_amt = 0
		for con in gs.trade_contracts:
			if con["buyer_pos"] == order["buyer_pos"] and con["resource"] == order["resource"]:
				matched_amt += con["amount"]
		
		if matched_amt >= order["amount"]: continue
		
		var res = order["resource"]
		var buyer_pos = order["buyer_pos"]
		
		var best_supplier_pos = Vector2i(-1, -1)
		var best_profit = -1000.0
		
		for s_pos in gs.settlements:
			var s_data = gs.settlements[s_pos]
			if s_pos == buyer_pos: continue
			if gs.get_relation(order["faction"], s_data.faction) == "war": continue
			
			var stock = s_data.inventory.get(res, 0)
			
			# RESERVATION SYSTEM: Check how much stock is already booked for pickup
			var reserved = 0
			for con in gs.trade_contracts:
				if con["seller_pos"] == s_pos and con["resource"] == res and con["status"] == "active":
					reserved += con["amount"]
			
			if (stock - reserved) < 20: continue 
			
			var buy_price = PricingSystem.get_price(res, s_data)
			var profit = order["price_offered"] - buy_price
			var dist = s_pos.distance_to(buyer_pos)
			
			var score = profit - (dist * 0.1)
			if s_data.faction == order["faction"]: score += 50 
			
			if score > best_profit:
				best_profit = score
				best_supplier_pos = s_pos
				
		if best_supplier_pos != Vector2i(-1, -1):
			var best_caravan = null
			var closest_dist = 9999.9
			
			for c in available_caravans:
				var d = c.pos.distance_to(best_supplier_pos)
				if d < closest_dist:
					closest_dist = d
					best_caravan = c
			
			if best_caravan:
				var contract = {
					"id": gs.rng.randi(),
					"seller_pos": best_supplier_pos,
					"buyer_pos": buyer_pos,
					"resource": res,
					"amount": int(min(order["amount"] - matched_amt, 200)),
					"price": order["price_offered"],
					"status": "active",
					"caravan_id": best_caravan.get_instance_id()
				}
				gs.trade_contracts.append(contract)
				
				best_caravan.target_pos = best_supplier_pos
				best_caravan.target_resource = res
				best_caravan.state = "buying"
				best_caravan.final_destination = buyer_pos
				# Use set_meta to avoid modifying class if not needed
				best_caravan.set_meta("contract_id", contract.id)
				
				available_caravans.erase(best_caravan)

## Resolve caravan trade at settlement
@warning_ignore("shadowed_global_identifier")
static func resolve_caravan_trade(gs, caravan_obj):
	var s_pos = caravan_obj.pos
	if not gs.settlements.has(s_pos):
		for k in gs.settlements:
			if k.distance_to(s_pos) < 2:
				s_pos = k
				break
	
	if not gs.settlements.has(s_pos): return

	var s_data = gs.settlements[s_pos]
	var res = caravan_obj.target_resource
	
	if caravan_obj.state == "buying":
		var price = PricingSystem.get_price(res, s_data)
		var base_cap = Globals.CARAVAN_CAPACITY_BULK if res in ["wood", "stone", "iron", "grain", "fish", "meat", "leather", "cloth"] else Globals.CARAVAN_CAPACITY_VALUE
		var buy_limit = int(base_cap / price)
		var purchased = min(buy_limit, s_data.inventory.get(res, 0))
		
		if purchased > 0:
			s_data.inventory[res] -= purchased
			s_data.crown_stock += int(purchased * price * 0.9)
			caravan_obj.inventory[res] = purchased
			
			# Proceed to sell at destination
			if caravan_obj.final_destination != Vector2i(-1, -1):
				caravan_obj.target_pos = caravan_obj.final_destination
				caravan_obj.state = "selling"
			else:
				caravan_obj.target_pos = caravan_obj.origin
				caravan_obj.state = "returning"
		else:
			caravan_obj.target_pos = caravan_obj.origin
			caravan_obj.state = "idle"
	
	elif caravan_obj.state == "selling":
		var amt = caravan_obj.inventory.get(res, 0)
		if amt > 0:
			var price = PricingSystem.get_price(res, s_data)
			var revenue = int(amt * price * 1.1)
			
			# Find the contract
			var contract_id = caravan_obj.get_meta("contract_id") if caravan_obj.has_meta("contract_id") else -1
			var contract = null
			for c in gs.trade_contracts:
				if c["id"] == contract_id:
					contract = c
					break
			
			if contract:
				revenue = int(amt * contract["price"])
				contract["status"] = "completed"
			
			s_data.inventory[res] = s_data.inventory.get(res, 0) + amt
			caravan_obj.crowns += revenue
			caravan_obj.inventory.erase(res)
			
			# Add profit to origin settlement
			var origin_data = gs.settlements.get(caravan_obj.origin)
			if origin_data:
				var profit = int(revenue * 0.2)
				origin_data.crown_stock += profit
		
		caravan_obj.target_pos = caravan_obj.origin
		caravan_obj.state = "returning"
