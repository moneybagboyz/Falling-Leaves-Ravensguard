extends RefCounted

const CATEGORIES = {
	"WORLD": ["Terrain", "Layouts", "Provinces", "Resources"],
	"FACTIONS": ["Dynamic:Factions"],
	"NPCs": ["Dynamic:NPCs"],
	"SETTLEMENTS": ["Dynamic:Settlements"],
	"EQUIPMENT": ["Materials", "Weapon Types", "Armor Layering"],
	"BESTIARY": ["Wildlife", "Humanoids", "Abominations"],
	"DEVELOPMENT": ["Social Estates", "Industrial Pillars", "Defense Pillars", "Civil Pillars"],
	"CHROME": ["Combat", "Wound System", "Attributes", "Skills", "Leveling", "Embarkation", "Damage Types", "Economy", "Diplomacy", "Resting", "Leadership"]
}

const ENTRIES = {
	"Terrain": {
		"title": "Natural Landscapes",
		"icon": "^",
		"content": "The world of Aequor is a varied landscape of mountains, forests, and seas.\n\n[color=gray]^  Mountains[/color]: Impassable barriers often rich in ores.\n[color=green]#  Forests[/color]: Provide cover and lumber resources.\n[color=blue]~  Water[/color]: Rivers and seas, vital for trade and life.\n[color=yellow]\"  Plains[/color]: Ideal for farming and rapid travel.\n\n[b]Rivers[/b]: Flow from mountains to the sea. Crossing them generally incurs a movement penalty unless a bridge in a settlement is present."
	},
	"Layouts": {
		"title": "World Geometry",
		"icon": "⦿",
		"content": "Aequor can be found in several distinct tectonic configurations:\n\n[b]Pangea[/b]: A single massive supercontinent surrounded by ocean.\n[b]Continents[/b]: Several large landmasses providing distinct geographic isolation.\n[b]Archipelago[/b]: A world of many islands, where naval trade and coastal ports are paramount."
	},
	"Provinces": {
		"title": "Regions and Borders",
		"icon": "▣",
		"content": "Aequor is divided into distinct provinces, each with its own climate and history.\n\nProvinces determine the [color=cyan]cultural heritage[/color] of recruits and the base productivity of settlements. Controlling all settlements in a province grants significant tax bonuses and political legitimacy."
	},
	"Resources": {
		"title": "Trade and Materials",
		"icon": "$",
		"content": "Settlements produce surpluses based on their location.\n\n[b]Grain[/b]: The lifeblood of armies. High demand in cities.\n[b]Iron/Coal[/b]: Required for manufacturing weapons and armor.\n[b]Lumber[/b]: Essential for building expansions and siege engines.\n[b]Spice/Silk[/b]: Luxury goods that drive Merchant wealth."
	},
	"The Red Legion": {
		"title": "The Red Legion",
		"icon": "L",
		"content": "A militaristic empire from the southern reaches. They value strength and discipline above all else.\n\n[color=red]Ideology[/color]: Might is Right.\n[color=red]Preferred Tactics[/color]: Dense formations and heavy infantry.\n[color=red]Opinion of You[/color]: Based on your deeds in their territory."
	},
	"Wildlife": {
		"title": "Flora and Fauna",
		"icon": "f",
		"content": "The wilds are home to dangerous creatures that can be hunted for meat and hides.\n\n[color=brown]Boars[/color]: Aggressive and tough. Good source of food.\n[color=gray]Wolves[/color]: Travel in packs. Threat to lone travelers.\n[color=white]Deer[/color]: Skittish. High value pelts but difficult to catch."
	},
	"Humanoids": {
		"title": "Men and Kin",
		"icon": "@",
		"content": "The most common inhabitants of the world.\n\n[b]Peasants[/b]: The backbone of the economy. Vulnerable to raids.\n[b]Soldiers[/b]: Professional fighters belonging to factions.\n[color=red]Bandits[/color]: Outlaws who prey on trade routes and weak warbands."
	},
	"Abominations": {
		"title": "The Unnatural",
		"icon": "&",
		"content": "Horrors born of dark paths or ancient curses.\n\n[color=purple]Undead[/color]: Skeletons and ghouls that do not tire.\n[color=red]Mutants[/color]: Twisted beings found near ancient rifts.\nThese entities generally ignore diplomacy and seek only destruction."
	},
	"Combat": {
		"title": "Tactical Combat",
		"icon": "⚔",
		"content": "Combat in Aequor is deadly and focused on positioning.\n\n[b]Distance[/b]: Melee attacks require being adjacent.\n[b]Facing[/b]: Attacks from behind deal significantly more damage.\n[b]Wounds[/b]: Damage isn't just HP; it affects limbs and functions.\n[b]Rewards[/b]: Winning battles grants XP to all participating units and yields loot based on the enemy's wealth."
	},
	"Attributes": {
		"title": "Natural Ability",
		"icon": "👤",
		"content": "Every being in Aequor is defined by six core attributes:\n\n[b]Strength[/b]: Affects raw damage and carry weight.\n[b]Endurance[/b]: Determines total HP and fatigue resistance.\n[b]Agility[/b]: Influences hit chance, dodging, and speed.\n[b]Intelligence[/b]: Crucial for experience gain and tactical options.\n[b]Balance[/b]: Reduces the chance of being knocked prone.\n[b]Pain Tolerance[/b]: Determines how many wounds a unit can suffer before collapsing."
	},
	"Skills": {
		"title": "Martial Prowess",
		"icon": "🛡",
		"content": "Skills are improved through practice and combat.\n\n[b]Melee Skills[/b]: Swordsmanship, Axe Fighting, Spear Use, etc. These directly increase hit chance and damage with specific weapons.\n[b]Defensive Skills[/b]: Shield Use and Dodging reduce the chance of taking clean hits.\n[b]Armor Handling[/b]: Reduces the speed and agility penalties of wearing heavy gear.\n\nYou can spend [color=yellow]Skill Points[/color] earned via leveling in the [b]Management > Training[/b] screen."
	},
	"Leveling": {
		"title": "Progression and Power",
		"icon": "★",
		"content": "Victories in battle grant Experience (XP) to the entire warband.\n\n[b]Leveling Up[/b]: Units gain levels at fixed XP thresholds (Level^2 * 100).\n[b]Points[/b]: Each level grants [color=cyan]2 Stat Points[/color] and [color=yellow]5 Skill Points[/color].\n[b]Training[/b]: Access the Training Counsel in the Management screen (TAB) to spend these points on your Commander."
	},
	"Embarkation": {
		"title": "Starting Your Journey",
		"icon": "⚓",
		"content": "The world is wide, and where you start matters.\n\n[b]Location Select[/b]: After creating your character, you choose a starting settlement. Choosing a [b]Capital[/b] provides safety and trade, while starting in a [b]Hamlet[/b] or the [b]Wilds[/b] offers a more rugged challenge.\n[b]Archetypes[/b]: Your starting stats and gold are determined by your chosen background (Mercenary, Noble, Outcast, etc)."
	},
	"Damage Types": {
		"title": "The Art of Killing",
		"icon": "☠",
		"content": "Weapons deliver damage in three distinct forms:\n\n[color=red]Cut[/color]: High flesh damage, causes significant bleeding. Weak against plate armor.\n[color=cyan]Pierce[/color]: Focuses force on a tiny point. Best for bypassing mail and thick hides.\n[color=orange]Blunt[/color]: Delivers concussive force. Can fracture bones and cause pain through the thickest armor, even without penetrating."
	},
	"Materials": {
		"title": "The Substance of War",
		"icon": "⛓",
		"content": "Item effectiveness is largely determined by its material.\n\n[b]Cloth/Leather[/b]: Lightweight but offers minimal protection.\n[b]Bronze/Iron[/b]: The standard for early professional gear. Heavy and durable.\n[b]Steel[/b]: The pinnacle of metallurgy. Offers the best hardness-to-weight ratio.\n[b]Wood[/b]: Primarily used for shafts and early shields."
	},
	"Weapon Types": {
		"title": "Instruments of Death",
		"icon": "⚔",
		"content": "Choosing the right tool for the job is essential.\n\n[b]Swords[/b]: Versatile weapons with good cutting and piercing capabilities.\n[b]Axes[/b]: Heavy hitters that excel at destroying shields and delivering massive chops.\n[b]Maces/Hammers[/b]: The answer to heavy armor. Focus on blunt force.\n[b]Daggers[/b]: Short and fast. Ideal for finding gaps in armor during close-quarters grappling."
	},
	"Armor Layering": {
		"title": "Protection Layers",
		"icon": "🛡",
		"content": "Aequor uses a realistic layering system.\n\n[b]Under-Layer[/b]: Soft padding (gambesons) to absorb blunt force.\n[b]Main-Layer[/b]: The primary defense (Chainmail, Plate).\n[b]Over-Layer[/b]: Surcoats or leather wraps for utility and extra protection.\nProper layering can make a unit nearly invincible against light weaponry."
	},
	"Social Estates": {
		"title": "The Three Estates",
		"icon": "👥",
		"content": "Society is divided into three distinct roles:\n\n[b]Laborers[/b]: The backbone. They work the land, mines, and forests to provide raw materials.\n[b]Burghers[/b]: Urban dwellers who process raw goods into finished tools and weapons.\n[b]Nobility[/b]: The ruling 1%. They provide stability and governance but consume the finest luxuries."
	},
	"Industrial Pillars": {
		"title": "Economic Production",
		"icon": "⚒",
		"content": "Infrastructure that drives the economy.\n\n[b]Farms/Mines/Quarries[/b]: Multipliers to raw resource yields. Higher levels allow more intensive extraction from the land.\n[b]Processing Units[/b]: Blacksmiths, Weavers, and Tanneries that convert raw materials into profit."
	},
	"Defense Pillars": {
		"title": "The Walls of Aequor",
		"icon": "🏰",
		"content": "Protection for the populace.\n\n[b]Stone Walls[/b]: Massive multipliers to garrison strength and siege 'hardness'.\n[b]Barracks[/b]: Increases the training quality and recruitment pool of local defenders.\n[b]Castles[/b]: The final bastion of a lord's power, doubling all other defensive bonuses."
	},
	"Civil Pillars": {
		"title": "Urban Prosperity",
		"icon": "🏛",
		"content": "Infrastructure for the people.\n\n[b]Markets[/b]: Drive trade volume and commercial tax revenue.\n[b]Housing[/b]: Determines the maximum population capacity of the settlement.\n[b]Cathedrals/Taverns[/b]: Manage stability and happiness, preventing unrest and rebellion."
	},
	"Economy": {
		"title": "Supply and Demand",
		"icon": "⚖",
		"content": "A physical simulation of production and logistics.\n\n[b]Acreage System[/b]: Tiles are divided into 250 acres of arable, forest, or pasture land.\n[b]Three-Field Rotation[/b]: Winter, Spring, and Fallow fields allow for sustainable farming and livestock grazing.\n[b]Market Prices[/b]: Values fluctuate daily based on local stock and caravan activity."
	},
	"Wound System": {
		"title": "Death and Disfigurement",
		"icon": "!",
		"content": "Unlike simple RPGs, health is split across body parts.\n\n[color=red]Bleeding[/color]: Fatal if not treated with bandages.\n[color=orange]Fractures[/color]: Reduce effectiveness of limbs.\n[color=gray]Pain[/color]: Can cause units to flee or collapse."
	},
	"Diplomacy": {
		"title": "Politics and Relations",
		"icon": "⚖",
		"content": "Aequor's factions are in a constant state of flux.\n\n[b]Relations[/b]: Range from -100 (Total War) to 100 (Solid Alliance).\n[b]Bribes[/b]: Can be used to improve standing or secure peace.\n[b]Raiding[/b]: Attacking caravans or villages will rapidly degrade relations."
	},
	"Resting": {
		"title": "Camp and Recovery",
		"icon": "Z",
		"content": "Rest is vital for recovering from exhaustion and healing wounds.\n\n[b]Setting Camp[/b]: Press [color=yellow]Z[/color] on the overworld. Time passes faster.\n[b]Safety[/b]: Resting in the open can lead to ambushes. Forest tiles provide more safety but slower healing than inns."
	},
	"Leadership": {
		"title": "Command and Morale",
		"icon": "★",
		"content": "Your presence on the battlefield inspires your troops.\n\n[b]Aura[/b]: Nearby units gain bonuses to attack and defense.\n[b]Morale[/b]: If you fall, or if losses are too high, your army may rout.\n[b]Skills[/b]: Investing in Charisma and Tactics unlocks special battle orders."
	}
}

static func get_categories() -> Array:
	return CATEGORIES.keys()

static func get_entries_for(category: String, gs = null) -> Array:
	var list = CATEGORIES.get(category, [])
	if list.size() > 0 and list[0].begins_with("Dynamic:"):
		var type = list[0].split(":")[1]
		return _get_dynamic_list(type, gs)
	return list

static func _get_dynamic_list(type: String, gs) -> Array:
	if not gs: return ["Error: No World Data"]
	match type:
		"Factions":
			var names = []
			for f in gs.factions:
				if f.id != "neutral":
					names.append("Faction:" + f.id)
			return names
		"NPCs":
			var names = []
			for pos in gs.settlements:
				for npc in gs.settlements[pos].npcs:
					names.append("NPC:" + npc.id)
			return names
		"Settlements":
			var names = []
			var s_list = []
			for pos in gs.settlements:
				s_list.append(gs.settlements[pos])
			# Sort by population or name
			s_list.sort_custom(func(a,b): return a.name < b.name)
			for s in s_list:
				names.append("Settlement:" + s.name)
			return names
	return []

static func get_entry(title: String, gs = null) -> Dictionary:
	if title.contains(":"):
		var parts = title.split(":")
		var type = parts[0]
		var id = parts[1]
		return _get_dynamic_entry(type, id, gs)
	
	return ENTRIES.get(title, {
		"title": title,
		"icon": "?",
		"content": "An entry for this topic has not yet been transcribed into the Great Archive."
	})

static func _get_dynamic_entry(type: String, id: String, gs) -> Dictionary:
	if not gs: return {"title": id, "icon": "!", "content": "World data unavailable."}
	
	match type:
		"Faction":
			var f = gs.get_faction(id)
			if f:
				var content = "The %s\n" % f.name
				content += "Treasury: %d Crowns\n" % f.treasury
				content += "Borders: %d Controlled Fiefs\n" % _count_fiefs(gs, f.id)
				content += "Diplomacy:\n"
				for other_id in f.relations:
					var rel = f.relations[other_id]
					var rel_str = "Neutral"
					var col = "gray"
					if rel <= -50: rel_str = "WAR"; col = "red"
					elif rel >= 50: rel_str = "Ally"; col = "green"
					content += "- %s: [color=%s]%s (%d)[/color]\n" % [gs.get_faction(other_id).name, col, rel_str, rel]
				return {"title": f.name, "icon": "F", "content": content}
		
		"NPC":
			var npc = gs.find_npc(id)
			if npc:
				var loc = _find_npc_location(gs, id)
				var content = "%s %s\n" % [npc.title, npc.name]
				content += "Faction: %s\n" % gs.get_faction(npc.faction_id).name
				content += "Location: [color=yellow]%s[/color]\n" % loc
				content += "Wealth: %d Crowns\n" % npc.gold
				if npc.fief_ids.size() > 0:
					content += "Lord of: %s\n" % ", ".join(_get_fief_names(gs, npc.fief_ids))
				return {"title": npc.name, "icon": "L", "content": content}
				
		"Settlement":
			for pos in gs.settlements:
				var s = gs.settlements[pos]
				if s.name == id:
					var content = "%s (%s)\n" % [s.name, s.type.capitalize()]
					content += "Faction: %s\n" % gs.get_faction(s.faction).name
					content += "Population: %d\n" % s.population
					var lord = gs.find_npc(s.lord_id) if s.lord_id != "" else null
					content += "Lord: %s\n" % (lord.name if lord else "None")
					content += "Garrison: %d/%d\n" % [s.garrison, s.garrison_max]
					return {"title": s.name, "icon": "H", "content": content}
					
	return {"title": id, "icon": "?", "content": "Information unknown."}

static func _count_fiefs(gs, f_id):
	var count = 0
	for pos in gs.settlements:
		if gs.settlements[pos].faction == f_id: count += 1
	return count

static func _get_fief_names(gs, ids):
	var names = []
	for pos in ids:
		if pos in gs.settlements: names.append(gs.settlements[pos].name)
	return names

static func _find_npc_location(gs, id) -> String:
	for a in gs.armies:
		if a.lord_id == id:
			return "In the field near (%d, %d)" % [a.pos.x, a.pos.y]
	var npc = gs.find_npc(id)
	if npc and npc.settlement_pos in gs.settlements:
		return "Visiting %s" % gs.settlements[npc.settlement_pos].name
	return "Location Unknown"

static func find_entry_indices(entry_title: String, gs) -> Vector2i:
	var categories = get_categories()
	for c_idx in range(categories.size()):
		var entries = get_entries_for(categories[c_idx], gs)
		for e_idx in range(entries.size()):
			if entries[e_idx] == entry_title:
				return Vector2i(c_idx, e_idx)
	return Vector2i(-1, -1)
