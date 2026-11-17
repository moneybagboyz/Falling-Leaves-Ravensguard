shader_type canvas_item;
render_mode unshaded;

uniform sampler2D atlas : filter_nearest, repeat_disable;
uniform sampler2D ramps : filter_nearest, repeat_disable; // 4 levels x N palette rows
uniform float atlas_w = 960.0;
uniform float ramp_rows = 4.0;

varying vec2 v_uv;
varying flat vec4 v_cd;

void vertex() {
	vec4 cd = INSTANCE_CUSTOM;            // x frame, y palette row, z wound bits + 8*moving, w phase 0..1
	float moving = step(8.0, cd.z);
	float ph = TIME * 9.0 + cd.w * 6.2832;
	float bob = abs(sin(ph)) * 2.0 * moving;          // 2-frame bounce, whole body
	float sway = sin(ph) * 1.5 * moving * (1.0 - UV.y); // head sways, feet planted
	VERTEX.y -= bob;
	VERTEX.x += sway;
	v_uv = vec2((cd.x * 32.0 + UV.x * 32.0) / atlas_w, UV.y);
	v_cd = cd;
}

void fragment() {
	vec4 c = texture(atlas, v_uv);
	if (c.a < 0.5) discard;
	// team-colour pixels (strongly red) are remapped through the palette row by luminance
	if (c.r > 1.7 * c.g && c.r > 1.7 * c.b) {
		float l = dot(c.rgb, vec3(0.3, 0.59, 0.11));
		float lvl = clamp(floor((l - 0.08) / 0.12), 0.0, 3.0);
		c.rgb = texture(ramps, vec2((lvl + 0.5) / 4.0, (v_cd.y + 0.5) / ramp_rows)).rgb;
	}
	int w = int(mod(v_cd.z, 8.0));
	if ((w & 1) != 0 && v_uv.y > 0.35 && v_uv.y < 0.6) c.rgb = mix(c.rgb, vec3(0.65, 0.04, 0.04), 0.65); // torso blood
	if ((w & 2) != 0) c.rgb *= 0.75;                                                                      // hurt: darkened
	if ((w & 4) != 0 && v_uv.y > 0.8) c.rgb = mix(c.rgb, vec3(0.5, 0.0, 0.0), 0.5);                       // leg wound
	COLOR = c;
}
