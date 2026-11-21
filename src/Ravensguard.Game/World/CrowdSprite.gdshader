// P7-BB-E: one shader for every sprite instance of a battle crowd (Street people, Field minis, Command icons, lying bodies).
// Instance custom data: x = atlas frame index (a texel of the frames table: x, y, w, h in atlas texels), y = palette row, z = flags, w = walk phase 0..1. Every value is a small integer
// or a fraction, so nothing depends on custom data being stored at full float precision.
// Flags: 1 blood, 2 hurt, 4 leg wound, 8 moving, 16 mirror, 32 dead, 64 routing. The mesh is a unit quad whose foot is the origin, sized by the instance transform.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D atlas : filter_nearest, repeat_disable;
uniform sampler2D ramps : filter_nearest, repeat_disable; // 4 levels (dark to light) x one row per team
uniform sampler2D frames : filter_nearest, repeat_disable; // one texel per frame: (x, y, w, h) in atlas texels, RGBA float
uniform vec2 atlas_size = vec2(1024.0, 32.0);
uniform float ramp_rows = 6.0;
uniform float src_row = 1.0;      // the row the art is drawn in (red)
uniform float bob_u = 0.03125;    // walk bob, in units of the sprite's height
uniform float sway_u = 0.05;      // head sway, in units of its width

varying vec2 v_uv;
varying vec2 v_rel;
varying flat vec4 v_cd;
varying flat float v_fl;

void vertex() {
	vec4 cd = INSTANCE_CUSTOM;
	vec4 fr = texelFetch(frames, ivec2(int(cd.x + 0.5), 0), 0);
	float fl = floor(cd.z + 0.5);
	bool mirror = mod(floor(fl / 16.0), 2.0) >= 1.0;
	float moving = mod(floor(fl / 8.0), 2.0);
	vec2 q = UV;
	if (mirror) q.x = 1.0 - q.x;
	v_uv = (fr.xy + q * fr.zw) / atlas_size;
	v_rel = UV;
	v_cd = cd;
	v_fl = fl;
	float ph = TIME * 9.0 + cd.w * 6.2832;
	VERTEX.y -= abs(sin(ph)) * bob_u * moving;
	VERTEX.x += sin(ph) * sway_u * moving * (1.0 - UV.y);
}

void fragment() {
	vec4 c = texture(atlas, v_uv);
	if (c.a < 0.5) discard;
	vec3 col = c.rgb;
	float row = v_cd.y;
	if (abs(row - src_row) > 0.5) {
		for (int l = 0; l < 4; l++) {
			float u = (float(l) + 0.5) / 4.0;
			vec3 s = texture(ramps, vec2(u, (src_row + 0.5) / ramp_rows)).rgb;
			if (all(lessThan(abs(col - s), vec3(0.012)))) {
				col = texture(ramps, vec2(u, (row + 0.5) / ramp_rows)).rgb;
				break;
			}
		}
	}
	float f = v_fl;
	bool blood = mod(f, 2.0) >= 1.0;
	bool hurt = mod(floor(f / 2.0), 2.0) >= 1.0;
	bool leg = mod(floor(f / 4.0), 2.0) >= 1.0;
	bool dead = mod(floor(f / 32.0), 2.0) >= 1.0;
	if (blood && v_rel.y > 0.35 && v_rel.y < 0.6) col = mix(col, vec3(0.65, 0.04, 0.04), 0.65);
	if (hurt) col *= 0.78;
	if (leg && v_rel.y > 0.8) col = mix(col, vec3(0.5, 0.0, 0.0), 0.5);
	if (dead) col *= 0.55;
	COLOR = vec4(col, 1.0);
}
