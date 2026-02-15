class_name StateManager
extends RefCounted

const GameEnums = preload("res://src/core/GameEnums.gd")

signal state_changed(old_state: GameEnums.GameMode, new_state: GameEnums.GameMode)

var _current_state: GameEnums.GameMode = GameEnums.GameMode.MENU
var _previous_state: GameEnums.GameMode = GameEnums.GameMode.MENU
var _state_history: Array[GameEnums.GameMode] = []
const MAX_HISTORY = 10

# Valid state transitions (for safety checking)
const VALID_TRANSITIONS = {
	GameEnums.GameMode.MENU: [
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.CHARACTER_CREATION,
		GameEnums.GameMode.PLAY_SELECT,
		GameEnums.GameMode.BATTLE_CONFIG,
		GameEnums.GameMode.CITY,
		GameEnums.GameMode.CODEX,
		GameEnums.GameMode.LOADING
	],
	GameEnums.GameMode.LOADING: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.WORLD_CREATION: [
		GameEnums.GameMode.WORLD_PREVIEW,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.WORLD_PREVIEW: [
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.REGION
	],
	GameEnums.GameMode.CHARACTER_CREATION: [
		GameEnums.GameMode.PLAY_SELECT,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.PLAY_SELECT: [
		GameEnums.GameMode.LOADING,
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.CHARACTER_CREATION
	],
	GameEnums.GameMode.OVERWORLD: [
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.DUNGEON,
		GameEnums.GameMode.CITY,
		GameEnums.GameMode.DIALOGUE,
		GameEnums.GameMode.MANAGEMENT,
		GameEnums.GameMode.REGION,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.BATTLE: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.BATTLE_CONFIG,
		GameEnums.GameMode.DIALOGUE,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.BATTLE_CONFIG: [
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.MANAGEMENT: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.DUNGEON: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.DIALOGUE: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.CODEX: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.CITY: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.REGION: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.CITY,
		GameEnums.GameMode.WORLD_PREVIEW,
		GameEnums.GameMode.MENU
	]
}

func _init(initial_state: GameEnums.GameMode = GameEnums.GameMode.MENU):
	_current_state = initial_state
	_previous_state = initial_state
	_state_history.append(initial_state)

func get_current_state() -> GameEnums.GameMode:
	return _current_state

func get_previous_state() -> GameEnums.GameMode:
	return _previous_state

func can_transition_to(new_state: GameEnums.GameMode) -> bool:
	var valid_states = VALID_TRANSITIONS.get(_current_state, [])
	return new_state in valid_states

func transition_to(new_state: GameEnums.GameMode, force: bool = false) -> bool:
	# Allow forced transitions (for backwards compatibility during migration)
	if not force and not can_transition_to(new_state):
		push_warning("Invalid state transition: %s -> %s" % [
			GameEnums.GameMode.keys()[_current_state],
			GameEnums.GameMode.keys()[new_state]
		])
		# For now, allow it anyway during migration
	
	if _current_state == new_state:
		return false # No change
	
	var old_state = _current_state
	_previous_state = old_state
	_current_state = new_state
	
	# Track history
	_state_history.append(new_state)
	if _state_history.size() > MAX_HISTORY:
		_state_history.pop_front()
	
	emit_signal("state_changed", old_state, new_state)
	return true

func go_back() -> bool:
	"""Transition back to previous state"""
	if _previous_state != _current_state:
		return transition_to(_previous_state, true)
	return false

func get_state_name(state: GameEnums.GameMode = -1) -> String:
	if state == -1:
		state = _current_state
	return GameEnums.GameMode.keys()[state]

func is_in_gameplay() -> bool:
	"""Check if currently in active gameplay states"""
	return _current_state in [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.DUNGEON,
		GameEnums.GameMode.CITY,
		GameEnums.GameMode.REGION
	]

func is_in_menu() -> bool:
	"""Check if in menu/config states"""
	return _current_state in [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.CHARACTER_CREATION,
		GameEnums.GameMode.BATTLE_CONFIG,
		GameEnums.GameMode.CODEX
	]
