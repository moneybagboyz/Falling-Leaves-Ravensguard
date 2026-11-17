extends RefCounted
class_name PricingSystem

## Pricing System
## Handles dynamic pricing for resources and items based on supply/demand

# GameData and GameState are autoloads - no need to preload
const Globals = preload("res://src/core/Globals.gd")

## Calculate price for a resource
@warning_ignore("shadowed_global_identifier")
static func get_price(res_name, s_data):
	if s_data.cache_prices.has(res_name):
		return s_data.cache_prices[res_name]
		
	var base = GameData.BASE_PRICES.get(res_name, 10)
	var stock = s_data.inventory.get(res_name, 0)
	var pop = s_data.population
	
	# OPTIMIZATION: Use a demand mapping instead of elif chain
	var demand = 1.0
	
	if res_name in ["grain", "fish", "meat", "game"]:
		demand = pop * Globals.DAILY_BUSHELS_PER_PERSON * 14.0
	elif res_name == "wood":
		demand = (pop / Globals.WOOD_FUEL_POP_DIVISOR) + (s_data.buildings.size() * Globals.WOOD_FUEL_BUILDING_MULT)
		var temp = GameState.geology.get(s_data.pos, {}).get("temp", 0.0)
		if temp > 0.0:
			demand *= max(0.2, 1.0 - temp)
	else:
		# Use a coefficient-based demand for other resources
		var coeffs = {
			"ale": 0.1,
			"salt": 0.05,
			"peat": 0.025,
			"furs": 0.016,
			"fine_garments": 0.02,
			"jewelry": 0.0 # Handled manually
		}
		if coeffs.has(res_name):
			demand = (pop * coeffs[res_name]) + (10 if res_name in ["ale", "salt"] else 5)
		elif res_name == "jewelry":
			# Demand scales with nobility (approx 1 unit per 5 nobles daily turnover/desire)
			demand = max(2, int(s_data.nobility * 0.2))
	
	var ratio = float(demand) / max(1.0, float(stock))
	var price = base * ratio
	if stock <= 0:
		price = base * Globals.PRICE_ZERO_STOCK_MULT
	else:
		price = clamp(price, base * Globals.PRICE_MIN_MULT, base * Globals.PRICE_MAX_MULT)
	
	var final_p = int(price)
	s_data.cache_prices[res_name] = final_p
	return final_p

## Get buy price (10% markup)
@warning_ignore("shadowed_global_identifier")
static func get_buy_price(res_name, s_data):
	return int(get_price(res_name, s_data) * 1.1)

## Get sell price (10% markdown)
@warning_ignore("shadowed_global_identifier")
static func get_sell_price(res_name, s_data):
	return int(get_price(res_name, s_data) * 0.9)

## Get item price based on type, material, and quality
static func get_item_price(type_key, mat_key, qual, is_commission := false):
	var base_price = 0
	
	# Check if item exists
	if GameData.ITEMS.has(type_key):
		var item = GameData.ITEMS[type_key]
		var item_type = item.get("type", "")
		
		# Base price calculation based on item stats
		if item_type == "weapon":
			base_price = item.get("dmg", 5) * 10 + 20
		elif item_type == "armor":
			base_price = item.get("prot", 3) * 8 + 15
		elif item_type == "shield":
			base_price = item.get("prot", 5) * 6 + 25
		else:
			base_price = 10
	else:
		# Fallback to BASE_PRICES if item type not found
		base_price = GameData.BASE_PRICES.get(type_key, 10)
	
	# Material Multiplier (based on material hardness)
	var mat_mult = 1.0
	if GameData.MATERIALS.has(mat_key):
		var hardness = GameData.MATERIALS[mat_key].get("hardness", 10)
		mat_mult = hardness / 40.0 # Iron is baseline (hardness=40)
		mat_mult = clamp(mat_mult, 0.3, 3.0)
	else:
		# Fallback material multipliers
		match mat_key:
			"wood", "cloth", "linen", "wool": mat_mult = 0.4
			"leather": mat_mult = 0.6
			"copper": mat_mult = 0.8
			"bronze": mat_mult = 1.2
			"iron": mat_mult = 1.0
			"steel": mat_mult = 2.0
			_: mat_mult = 1.0
	
	# Quality Multiplier
	var qual_mult = 1.0
	match qual:
		"shoddy": qual_mult = 0.5
		"poor": qual_mult = 0.75
		"standard": qual_mult = 1.0
		"fine": qual_mult = 2.0
		"masterwork": qual_mult = 4.0
		"legendary": qual_mult = 10.0
	
	var final_price = int(base_price * mat_mult * qual_mult)
	
	# Commission markup (craftsmen need profit)
	if is_commission:
		final_price = int(final_price * 1.5)
	
	return max(1, final_price)

## Get market info for a resource
static func get_market_info(s_data, res):
	var stock = s_data.inventory.get(res, 0)
	var price = get_price(res, s_data)
	var buy_p = get_buy_price(res, s_data)
	var sell_p = get_sell_price(res, s_data)
	var demand_str = "Low"
	
	if stock <= 0:
		demand_str = "Extreme"
	elif stock < s_data.population * 0.1:
		demand_str = "High"
	elif stock < s_data.population * 0.5:
		demand_str = "Moderate"
	
	return {
		"stock": stock,
		"price": price,
		"buy_price": buy_p,
		"sell_price": sell_p,
		"demand": demand_str
	}
