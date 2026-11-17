class_name ClimatePhase
extends WorldGenPhase

## Handles atmospheric moisture simulation
## Updates moisture_map based on wind and elevation

func get_phase_name() -> String:
	return "Climate"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	
	step_completed.emit("SIMULATING ATMOSPHERE...")
	for y in range(h):
		if y % 20 == 0:
			step_completed.emit("SIMULATING ATMOSPHERE [%d%%]" % [int((float(y) / h) * 100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		var wind_moisture = 0.0
		for x in range(w):
			var e = context.elevation_map[y][x]
			if e < 0.35:  # Water
				wind_moisture += 0.25 * context.moisture_bias
				wind_moisture = clamp(wind_moisture, 0.0, 5.0)
			else:  # Land
				# Mountain rain shadow
				if e > 0.58:
					var dump = wind_moisture * 0.5
					context.moisture_map[y][x] += dump
					wind_moisture -= dump
				
				context.moisture_map[y][x] += wind_moisture * 0.12
				wind_moisture *= 0.982
			
			wind_moisture = clamp(wind_moisture, 0.0, 5.0)
	
	return true

func cleanup(context: WorldGenContext) -> void:
	# elevation_map still needed for hydrology
	pass
