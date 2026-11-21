// P7-BB-E: a Command banner block, a flat rectangle in the team colour with a one-pixel border. The mesh is a unit quad scaled to the block.
// Instance custom data: x = width and y = height in band pixels, z = palette row + 8 if routing, w = strength 0..1 (the part of the block filled in the team colour, from the bottom).
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D ramps : filter_nearest, repeat_disable;
uniform float ramp_rows = 6.0;

varying vec2 v_px;
varying flat vec4 v_cd;

void vertex() {
	vec4 cd = INSTANCE_CUSTOM;
	v_px = UV * cd.xy;
	v_cd = cd;
}

vec3 ramp(float lvl, float row) {
	return texture(ramps, vec2((lvl + 0.5) / 4.0, (row + 0.5) / ramp_rows)).rgb;
}

void fragment() {
	float w = v_cd.x, h = v_cd.y;
	float row = mod(floor(v_cd.z + 0.5), 8.0);
	bool routing = v_cd.z >= 7.5;
	vec2 p = floor(v_px);
	vec3 col;
	if (p.x < 1.0 || p.y < 1.0 || p.x > w - 2.0 || p.y > h - 2.0) {
		col = vec3(0.102, 0.078, 0.094); // the palette's outline colour
	} else {
		bool filled = (h - p.y) <= max(1.0, v_cd.w * h);
		float lvl = filled ? 2.0 : 0.0;
		if (routing && mod(p.x + p.y, 2.0) < 1.0) lvl = max(0.0, lvl - 1.0); // a routing block is hatched
		if (filled && mod(p.y, 3.0) < 1.0) lvl = 1.0;                         // a little texture
		col = ramp(lvl, row);
	}
	COLOR = vec4(col, 1.0);
}
