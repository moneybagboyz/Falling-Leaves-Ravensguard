class_name PopulationPhase
extends WorldGenPhase

## Handles NPC generation and final cleanup

const SettlementManager = preload("res://src/managers/SettlementManager.gd")

func get_phase_name() -> String:
	return "Population"

func execute(context: WorldGenContext) -> bool:
	step_completed.emit("POPULATING SETTLEMENTS...")
	
	for s_pos in context.world_settlements:
		var s = context.world_settlements[s_pos]
		SettlementManager.refresh_npcs(s)
		
		# Link NPCs to armies
		if s.lord_id != "":
			for a in context.armies:
				if a.type == "lord" and a.pos == s_pos and a.lord_id == s.lord_id:
					for npc in s.npcs:
						if npc.id == s.lord_id:
							a.name = "%s %s's Party" % [npc.title, npc.name]
							break
					break
	
	step_completed.emit("GENERATION COMPLETE!")
	await (Engine.get_main_loop() as SceneTree).process_frame
	
	return true

func cleanup(context: WorldGenContext) -> void:
	# Final cleanup before returning to caller
	pass
