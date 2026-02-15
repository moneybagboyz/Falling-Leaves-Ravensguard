class_name UILibraries
extends RefCounted

## UI Rendering for World and Character Libraries
## DF/CDDA style save browsing interface

const SaveManager = preload("res://src/managers/SaveManager.gd")

# ============================================================================
# WORLD LIBRARY
# ============================================================================

static func render_world_library(worlds: Array, selected_idx: int, focus: int) -> String:
	"""
	Render the world library screen
	focus: 0 = world list, 1 = actions panel
	"""
	var parts = []
	parts.append("[center][b]═══ WORLD LIBRARY ═══[/b][/center]\n\n")
	
	if worlds.is_empty():
		parts.append("[center][color=gray]No saved worlds found.[/color][/center]\n\n")
		parts.append("[center]Press [color=yellow]N[/color] to create a new world[/center]\n")
		parts.append("[center]Press [color=yellow]ESC[/color] to return to menu[/center]\n")
		return "".join(parts)
	
	# Split screen: List on left, details on right
	parts.append("[b]Saved Worlds:[/b]\n")
	
	for i in range(worlds.size()):
		var world = worlds[i]
		var prefix = "→ " if i == selected_idx and focus == 0 else "  "
		var color = "yellow" if i == selected_idx else "white"
		
		parts.append("%s[color=%s]%s[/color] (%dx%d, %d settlements)\n" % [
			prefix, color, 
			world.get("name", "Unnamed"),
			world.get("width", 0),
			world.get("height", 0),
			world.get("num_settlements", 0)
		])
	
	# Details panel
	if selected_idx >= 0 and selected_idx < worlds.size():
		var world = worlds[selected_idx]
		parts.append("\n[b]═══ World Details ═══[/b]\n")
		parts.append("Name: [color=cyan]%s[/color]\n" % world.get("name", "Unnamed"))
		parts.append("Size: %dx%d\n" % [world.get("width", 0), world.get("height", 0)])
		parts.append("Seed: %d\n" % world.get("seed", 0))
		parts.append("Settlements: %d\n" % world.get("num_settlements", 0))
		parts.append("Factions: %d\n" % world.get("num_factions", 0))
		parts.append("Saved: %s\n" % world.get("saved_at", "Unknown"))
	
	# Controls
	parts.append("\n[b]═══ Controls ═══[/b]\n")
	parts.append("[color=yellow]↑↓[/color] Navigate | [color=yellow]ENTER[/color] Select World | [color=yellow]N[/color] New World\n")
	parts.append("[color=yellow]D[/color] Delete World | [color=yellow]ESC[/color] Back to Menu\n")
	
	return "".join(parts)

# ============================================================================
# CHARACTER LIBRARY
# ============================================================================

static func render_character_library(characters: Array, selected_idx: int, focus: int) -> String:
	"""
	Render the character library screen
	focus: 0 = character list, 1 = actions panel
	"""
	var parts = []
	parts.append("[center][b]═══ CHARACTER LIBRARY ═══[/b][/center]\n\n")
	
	if characters.is_empty():
		parts.append("[center][color=gray]No saved characters found.[/color][/center]\n\n")
		parts.append("[center]Press [color=yellow]N[/color] to create a new character[/center]\n")
		parts.append("[center]Press [color=yellow]ESC[/color] to return to menu[/center]\n")
		return "".join(parts)
	
	# Split screen: List on left, details on right
	parts.append("[b]Saved Characters:[/b]\n")
	
	for i in range(characters.size()):
		var char = characters[i]
		var prefix = "→ " if i == selected_idx and focus == 0 else "  "
		var color = "yellow" if i == selected_idx else "white"
		
		parts.append("%s[color=%s]%s[/color] (%s, %s)\n" % [
			prefix, color,
			char.get("name", "Unnamed"),
			char.get("profession", "Unknown").capitalize(),
			char.get("scenario", "Unknown").capitalize()
		])
	
	# Details panel
	if selected_idx >= 0 and selected_idx < characters.size():
		var char = characters[selected_idx]
		parts.append("\n[b]═══ Character Details ═══[/b]\n")
		parts.append("Name: [color=cyan]%s[/color]\n" % char.get("name", "Unnamed"))
		parts.append("Scenario: %s\n" % char.get("scenario", "Unknown").capitalize())
		parts.append("Profession: %s\n" % char.get("profession", "Unknown").capitalize())
		parts.append("\n[b]Attributes:[/b]\n")
		parts.append("  STR: %d  AGI: %d\n" % [char.get("strength", 10), char.get("agility", 10)])
		parts.append("  END: %d  INT: %d\n" % [char.get("endurance", 10), char.get("intelligence", 10)])
		
		var traits = char.get("traits", [])
		if not traits.is_empty():
			parts.append("\n[b]Traits:[/b] %s\n" % ", ".join(traits))
		
		parts.append("\nSaved: %s\n" % char.get("saved_at", "Unknown"))
	
	# Controls
	parts.append("\n[b]═══ Controls ═══[/b]\n")
	parts.append("[color=yellow]↑↓[/color] Navigate | [color=yellow]ENTER[/color] Select Character | [color=yellow]N[/color] New Character\n")
	parts.append("[color=yellow]D[/color] Delete Character | [color=yellow]ESC[/color] Back to Menu\n")
	
	return "".join(parts)

# ============================================================================
# GAME SETUP (Select World + Character)
# ============================================================================

static func render_game_setup(
	worlds: Array, 
	characters: Array, 
	selected_world_idx: int, 
	selected_char_idx: int,
	focus: int
) -> String:
	"""
	Render the game setup screen (select world + character)
	focus: 0 = world selection, 1 = character selection
	"""
	var parts = []
	parts.append("[center][b]═══ NEW GAME SETUP ═══[/b][/center]\n\n")
	
	# World Selection
	parts.append("[b]%s1. SELECT WORLD:[/b]\n" % ("→ " if focus == 0 else "  "))
	
	if worlds.is_empty():
		parts.append("  [color=gray]No worlds available. Create one first![/color]\n")
	elif selected_world_idx >= 0 and selected_world_idx < worlds.size():
		var world = worlds[selected_world_idx]
		parts.append("  [color=cyan]%s[/color] (%dx%d, %d settlements)\n" % [
			world.get("name", "Unnamed"),
			world.get("width", 0),
			world.get("height", 0),
			world.get("num_settlements", 0)
		])
	else:
		parts.append("  [color=yellow]Press ENTER to select...[/color]\n")
	
	parts.append("\n")
	
	# Character Selection
	parts.append("[b]%s2. SELECT CHARACTER:[/b]\n" % ("→ " if focus == 1 else "  "))
	
	if characters.is_empty():
		parts.append("  [color=gray]No characters available. Create one first![/color]\n")
	elif selected_char_idx >= 0 and selected_char_idx < characters.size():
		var char = characters[selected_char_idx]
		parts.append("  [color=cyan]%s[/color] (%s, %s)\n" % [
			char.get("name", "Unnamed"),
			char.get("profession", "Unknown").capitalize(),
			char.get("scenario", "Unknown").capitalize()
		])
	else:
		parts.append("  [color=yellow]Press ENTER to select...[/color]\n")
	
	# Status
	parts.append("\n[b]═══ Status ═══[/b]\n")
	var world_ready = selected_world_idx >= 0 and selected_world_idx < worlds.size()
	var char_ready = selected_char_idx >= 0 and selected_char_idx < characters.size()
	
	parts.append("World: %s\n" % ("[color=green]✓[/color]" if world_ready else "[color=red]✗[/color]"))
	parts.append("Character: %s\n" % ("[color=green]✓[/color]" if char_ready else "[color=red]✗[/color]"))
	
	if world_ready and char_ready:
		parts.append("\n[center][color=green][b]READY TO START![/b][/color][/center]\n")
		parts.append("[center]Press [color=yellow]SPACE[/color] to begin your adventure[/center]\n")
	else:
		parts.append("\n[center][color=yellow]Complete both selections to start[/color][/center]\n")
	
	# Controls
	parts.append("\n[b]═══ Controls ═══[/b]\n")
	parts.append("[color=yellow]TAB[/color] Switch Focus | [color=yellow]ENTER[/color] Open Library\n")
	parts.append("[color=yellow]SPACE[/color] Start Game | [color=yellow]ESC[/color] Back to Menu\n")
	
	return "".join(parts)

# ============================================================================
# WORLD SELECTION POPUP (for game setup)
# ============================================================================

static func render_world_selection_popup(worlds: Array, selected_idx: int) -> String:
	"""Simplified world selection for game setup"""
	var parts = []
	parts.append("[center][b]═══ SELECT WORLD ═══[/b][/center]\n\n")
	
	if worlds.is_empty():
		parts.append("[center][color=gray]No worlds available[/color][/center]\n")
		parts.append("[center]Press [color=yellow]N[/color] to create new world[/center]\n")
	else:
		for i in range(worlds.size()):
			var world = worlds[i]
			var prefix = "→ " if i == selected_idx else "  "
			var color = "yellow" if i == selected_idx else "white"
			
			parts.append("%s[color=%s]%s[/color] (%dx%d)\n" % [
				prefix, color,
				world.get("name", "Unnamed"),
				world.get("width", 0),
				world.get("height", 0)
			])
	
	parts.append("\n[color=yellow]↑↓[/color] Navigate | [color=yellow]ENTER[/color] Confirm | [color=yellow]ESC[/color] Cancel\n")
	
	return "".join(parts)

# ============================================================================
# CHARACTER SELECTION POPUP (for game setup)
# ============================================================================

static func render_character_selection_popup(characters: Array, selected_idx: int) -> String:
	"""Simplified character selection for game setup"""
	var parts = []
	parts.append("[center][b]═══ SELECT CHARACTER ═══[/b][/center]\n\n")
	
	if characters.is_empty():
		parts.append("[center][color=gray]No characters available[/color][/center]\n")
		parts.append("[center]Press [color=yellow]N[/color] to create new character[/center]\n")
	else:
		for i in range(characters.size()):
			var char = characters[i]
			var prefix = "→ " if i == selected_idx else "  "
			var color = "yellow" if i == selected_idx else "white"
			
			parts.append("%s[color=%s]%s[/color] (%s)\n" % [
				prefix, color,
				char.get("name", "Unnamed"),
				char.get("profession", "Unknown").capitalize()
			])
	
	parts.append("\n[color=yellow]↑↓[/color] Navigate | [color=yellow]ENTER[/color] Confirm | [color=yellow]ESC[/color] Cancel\n")
	
	return "".join(parts)
