class_name GameStateMachine
extends RefCounted

## A formal state machine for managing game mode transitions
## Provides validation, callbacks, and clear state management

const GameEnums = preload("res://src/core/GameEnums.gd")

signal state_changed(old_state: GameEnums.GameMode, new_state: GameEnums.GameMode)
signal state_enter(state: GameEnums.GameMode)
signal state_exit(state: GameEnums.GameMode)

var current_state: GameEnums.GameMode = GameEnums.GameMode.MENU
var previous_state: GameEnums.GameMode = GameEnums.GameMode.MENU
var state_stack: Array[GameEnums.GameMode] = []

# Valid state transitions (from -> [to states])
# Empty array means can transition to any state
var valid_transitions: Dictionary = {
	GameEnums.GameMode.MENU: [
		GameEnums.GameMode.WORLD_LIBRARY,
		GameEnums.GameMode.CHARACTER_LIBRARY,
		GameEnums.GameMode.GAME_SETUP,
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.CHARACTER_CREATION,
		GameEnums.GameMode.BATTLE_CONFIG,
		GameEnums.GameMode.CODEX,
		GameEnums.GameMode.PLAY_SELECT,
		GameEnums.GameMode.LOADING
	],
	GameEnums.GameMode.WORLD_LIBRARY: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.WORLD_PREVIEW
	],
	GameEnums.GameMode.CHARACTER_LIBRARY: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.CHARACTER_CREATION
	],
	GameEnums.GameMode.GAME_SETUP: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_LIBRARY,
		GameEnums.GameMode.CHARACTER_LIBRARY,
		GameEnums.GameMode.LOADING,
		GameEnums.GameMode.PLAY_SELECT
	],
	GameEnums.GameMode.WORLD_CREATION: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_LIBRARY,
		GameEnums.GameMode.LOADING,
		GameEnums.GameMode.WORLD_PREVIEW
	],
	GameEnums.GameMode.WORLD_PREVIEW: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_LIBRARY,
		GameEnums.GameMode.WORLD_CREATION,
		GameEnums.GameMode.CHARACTER_CREATION
	],
	GameEnums.GameMode.CHARACTER_CREATION: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.CHARACTER_LIBRARY,
		GameEnums.GameMode.PLAY_SELECT
	],
	GameEnums.GameMode.PLAY_SELECT: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.GAME_SETUP,
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.LOADING: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.WORLD_PREVIEW,
		GameEnums.GameMode.CHARACTER_CREATION,
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.OVERWORLD: [
		GameEnums.GameMode.BATTLE,
		GameEnums.GameMode.MANAGEMENT,
		GameEnums.GameMode.DUNGEON,
		GameEnums.GameMode.DIALOGUE,
		GameEnums.GameMode.CITY,
		GameEnums.GameMode.REGION,
		GameEnums.GameMode.PARTY_INFO,
		GameEnums.GameMode.FIEF_INFO,
		GameEnums.GameMode.HISTORY,
		GameEnums.GameMode.CODEX,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.BATTLE: [
		GameEnums.GameMode.OVERWORLD,
		GameEnums.GameMode.MENU
	],
	GameEnums.GameMode.BATTLE_CONFIG: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.BATTLE
	],
	GameEnums.GameMode.MANAGEMENT: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.DUNGEON: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.DIALOGUE: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.CODEX: [
		GameEnums.GameMode.MENU,
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.CITY: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.REGION: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.PARTY_INFO: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.FIEF_INFO: [
		GameEnums.GameMode.OVERWORLD
	],
	GameEnums.GameMode.HISTORY: [
		GameEnums.GameMode.OVERWORLD
	]
}

func _init(initial_state: GameEnums.GameMode = GameEnums.GameMode.MENU):
	current_state = initial_state
	previous_state = initial_state

func transition_to(new_state: GameEnums.GameMode, force: bool = false) -> bool:
	"""Attempt to transition to a new state"""
	# Allow forced transitions for special cases
	if force:
		_execute_transition(new_state)
		return true
	
	# Validate transition
	if not _is_valid_transition(current_state, new_state):
		push_warning("Invalid state transition: %s -> %s" % [
			GameEnums.state_to_string(current_state),
			GameEnums.state_to_string(new_state)
		])
		return false
	
	_execute_transition(new_state)
	return true

func _is_valid_transition(from: GameEnums.GameMode, to: GameEnums.GameMode) -> bool:
	"""Check if a transition is valid"""
	# Same state is always valid (re-entry)
	if from == to:
		return true
	
	# Check if transition is explicitly allowed
	if not valid_transitions.has(from):
		return false
	
	var allowed_states = valid_transitions[from]
	return to in allowed_states

func _execute_transition(new_state: GameEnums.GameMode):
	"""Execute the state transition"""
	var old_state = current_state
	
	# Exit current state
	state_exit.emit(old_state)
	
	# Update state
	previous_state = old_state
	current_state = new_state
	
	# Enter new state
	state_enter.emit(new_state)
	state_changed.emit(old_state, new_state)

func push_state(new_state: GameEnums.GameMode) -> bool:
	"""Push current state onto stack and transition to new state"""
	if transition_to(new_state):
		state_stack.append(previous_state)
		return true
	return false

func pop_state() -> bool:
	"""Pop state from stack and return to it"""
	if state_stack.is_empty():
		push_warning("Cannot pop state: stack is empty")
		return false
	
	var target_state = state_stack.pop_back()
	return transition_to(target_state, true) # Force transition when popping

func get_current_state() -> GameEnums.GameMode:
	return current_state

func get_previous_state() -> GameEnums.GameMode:
	return previous_state

func is_in_state(state: GameEnums.GameMode) -> bool:
	return current_state == state

func get_state_name() -> String:
	return GameEnums.state_to_string(current_state)

func get_stack_depth() -> int:
	return state_stack.size()
