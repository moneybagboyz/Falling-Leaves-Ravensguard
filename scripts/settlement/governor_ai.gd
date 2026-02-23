class_name GovernorAI

## Stateless governor decision-making, called once per daily tick per settlement.
## The governor spends treasury gold on buildings and adjusts taxation.

static func decide(settlement: Settlement) -> void:
	match settlement.governor_personality:
		"balanced":  _balanced(settlement)
		"greedy":    _greedy(settlement)
		"militant":  _militant(settlement)
		"builder":   _builder(settlement)
		_:           _balanced(settlement)


# ── Personalities ─────────────────────────────────────────────────────────────

static func _balanced(settlement: Settlement) -> void:
	# Prioritise food security, then general growth
	if not _has_building(settlement, "farm"):
		settlement.add_or_upgrade_building("farm")
		return
	if settlement.market.get_stock("grain") < settlement.population * 1.2 * 30.0:
		settlement.add_or_upgrade_building("farm")
		return
	if settlement.happiness < 60.0 and not _has_building(settlement, "tavern"):
		settlement.add_or_upgrade_building("tavern")
		return
	_build_next_priority(settlement, ["farm", "lumber_mill", "mine", "forge", "market"])


static func _greedy(settlement: Settlement) -> void:
	# Maximise treasury income — build markets and high-value extractors first
	_build_next_priority(settlement, ["market", "mine", "forge", "fishery", "farm"])


static func _militant(settlement: Settlement) -> void:
	# Always maintain a barracks; save treasury for army upkeep (Phase 3)
	if not _has_building(settlement, "barracks"):
		settlement.add_or_upgrade_building("barracks")
		return
	_balanced(settlement)


static func _builder(settlement: Settlement) -> void:
	# Upgrade everything systematically rather than focusing on one type
	var lowest_level_building: Building = null
	var lowest_level: int = 999
	for b: Building in settlement.buildings:
		if b.can_upgrade() and b.level < lowest_level:
			lowest_level = b.level
			lowest_level_building = b
	if lowest_level_building != null:
		settlement.add_or_upgrade_building(lowest_level_building.building_type)


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _has_building(settlement: Settlement, btype: String) -> bool:
	return settlement.get_building(btype) != null


## Attempts buildings in priority order; stops at the first affordable one.
static func _build_next_priority(settlement: Settlement, priority: Array) -> void:
	for btype: String in priority:
		var b: Building = settlement.get_building(btype)
		if b == null:
			# Try to build new
			if settlement.treasury >= 80.0:
				settlement.add_or_upgrade_building(btype)
				return
		elif b.can_upgrade():
			if settlement.treasury >= b.upgrade_cost():
				settlement.add_or_upgrade_building(btype)
				return


## Accumulate daily tax revenue into treasury.
## Called after the daily tick so it reflects the current-day population.
static func collect_taxes(settlement: Settlement) -> void:
	var tax_rate: float = _tax_rate(settlement)
	# Unhappy burghers withhold half their taxes.
	var burgher_rate: float = tax_rate * (0.5 if settlement.burgher_unhappy else 1.0)
	var revenue: float = settlement.burghers * 0.05 * burgher_rate \
					  + settlement.nobility  * 0.20 * tax_rate
	settlement.treasury += revenue
	# High taxes erode happiness over time.
	if tax_rate > 0.5:
		settlement.happiness = maxf(0.0, settlement.happiness - (tax_rate - 0.5) * 2.0)
	# Unhappy nobility destabilises faster.
	if settlement.nobility_unhappy:
		settlement.unrest = minf(100.0, settlement.unrest + 1.0)


static func _tax_rate(settlement: Settlement) -> float:
	match settlement.governor_personality:
		"greedy":   return 0.80
		"militant": return 0.60
		_:          return 0.40
