using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Ravensguard.Sim.Core;

namespace Ravensguard.Game.World;

/// <summary>What the world looks like right now, as far as colour goes: the season and how far through it we are, snow cover, ice, wet ground and leaf fall.</summary>
public readonly record struct SeasonState(int Season, float Frac, float Snow, float Ice, float Wet, float LeafLoss)
{
    public static SeasonState Of(GameTime t, float snow = 0, float ice = 0, float wet = 0)
    {
        float frac = ((t.DayOfSeason - 1) * 24f + t.Hour + t.Minute / 60f) / (TimeConst.DaysPerSeason * 24f);
        // far bands have no bare-tree sprites: the canopy thins out in autumn, is almost gone in winter and returns through spring
        float loss = t.Season switch { Ravensguard.Sim.Core.Season.Spring => 0.92f * (1f - frac), Ravensguard.Sim.Core.Season.Summer => 0f, Ravensguard.Sim.Core.Season.Autumn => 0.92f * frac * frac, _ => 0.92f };
        return new SeasonState((int)t.Season, frac, snow, ice, wet, loss);
    }
}

/// <summary>
/// The season palette swap (REDESIGN_PLAN 3c, art README "Seasons") as one shader. Sprites are authored in the summer colours; the shader finds a
/// texel equal to one of the 16 summer keys (grass, foliage, leaf, conifer x 4) and outputs the current season's colour, or the next season's once the
/// fraction of the season elapsed passes that tile's hash, so the change creeps in tile by tile (the first half of a season is pure, the second half blends toward the next). The ground material also lays snow over grass
/// (full, patchy or a dusting by depth, grass-coloured pixels only, so road and hedge edges blend), dusts roads and fields, freezes water (the water ramp
/// swaps to the ice ramp, tile by tile) and darkens wet ground. Everything works on any texture drawn in the summer palette: street tiles, props and the
/// baked Field / Command chunk images.
/// </summary>
public sealed class Seasonal
{
    const string ShaderCode = @"
shader_type canvas_item;
uniform sampler2D snow_tex : filter_nearest, repeat_disable;
uniform vec3 key[16];
uniform vec3 cur[16];
uniform vec3 nxt[16];
uniform vec3 wkey[4];
uniform vec3 ikey[4];
uniform float frac = 0.0;
uniform float snow = 0.0;
uniform float ice = 0.0;
uniform float wet = 0.0;
uniform float leaf_loss = 0.0;
uniform float tile_px = 32.0;
uniform float per_sprite = 0.0;
uniform float ground = 0.0;
uniform vec4 shade = vec4(1.0, 1.0, 1.0, 1.0);
varying vec2 vpos;
varying vec2 vorg;

void vertex() {
	vpos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
	vorg = MODEL_MATRIX[3].xy;
}
float h21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
int fam(vec3 c) { for (int i = 0; i < 16; i++) { vec3 d = c - key[i]; if (dot(d, d) < 0.00008) return i; } return -1; }
int wat(vec3 c) { for (int i = 0; i < 4; i++) { vec3 d = c - wkey[i]; if (dot(d, d) < 0.00008) return i; } return -1; }
bool isgrass(vec3 c) { int f = fam(c); return f >= 0 && f < 4; }

void fragment() {
	vec4 t = texture(TEXTURE, UV);
	vec3 rgb = t.rgb;
	float a = t.a;
	if (a > 0.01) {
		vec2 cell = per_sprite > 0.5 ? vorg : floor(vpos / tile_px);
		float h = h21(cell);
		float h2 = h21(cell + vec2(31.7, 5.1));
		float h3 = h21(cell + vec2(7.3, 19.9));
		int f = fam(rgb);
		bool grass = f >= 0 && f < 4;
		bool water = false;
		if (f >= 0) {
			rgb = max(0.0, frac * 2.0 - 1.0) > h ? nxt[f] : cur[f];
			if (leaf_loss > 0.0 && f >= 4 && f < 12 && h21(floor(vpos) + vec2(5.0, 9.0)) < leaf_loss) a = 0.0;
		} else {
			int w = wat(rgb);
			water = w >= 0;
			if (water && ice > h2) rgb = ikey[w];
		}
		if (ground > 0.5 && a > 0.0) {
			if (wet > 0.0 && !water && !grass) rgb *= 1.0 - 0.22 * wet;
			if (snow > 0.04 && !water) {
				bool g = grass;
				if (!g && f < 0) {
					vec2 px = TEXTURE_PIXEL_SIZE;
					int n = (isgrass(texture(TEXTURE, UV + vec2(px.x, 0.0)).rgb) ? 1 : 0) + (isgrass(texture(TEXTURE, UV - vec2(px.x, 0.0)).rgb) ? 1 : 0) + (isgrass(texture(TEXTURE, UV + vec2(0.0, px.y)).rgb) ? 1 : 0) + (isgrass(texture(TEXTURE, UV - vec2(0.0, px.y)).rgb) ? 1 : 0);
					g = n >= 2;
				}
				float s = snow * (0.7 + 0.6 * h3);
				int fr = -1;
				if (g) fr = s > 0.75 ? int(h2 * 2.0) : (s > 0.4 ? 2 + int(h2 * 2.0) : (s > 0.1 ? 4 + int(h2 * 2.0) : -1));
				else if (f < 0 && s > 0.1) fr = 4 + int(h2 * 2.0);
				if (fr >= 0) {
					vec2 u = fract(vpos / tile_px);
					vec4 sn = texture(snow_tex, vec2((float(fr) + u.x) / 6.0, u.y));
					if (sn.a > 0.5) rgb = sn.rgb;
				}
			}
		}
	}
	COLOR = vec4(rgb * shade.rgb, a * shade.a);
}
";

    const string DecalCode = @"
shader_type canvas_item;
uniform float wet = 0.0;
uniform float hide = 0.0;
uniform vec4 shade = vec4(1.0, 1.0, 1.0, 1.0);
varying vec2 vpos;
void vertex() { vpos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
float h21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
	vec4 t = texture(TEXTURE, UV);
	float h = h21(floor(vpos / 32.0) + vec2(3.1, 8.7));
	float vis = step(h * 0.7 + 0.15, wet) * (1.0 - hide);
	COLOR = vec4(t.rgb * shade.rgb, t.a * vis * 0.92 * shade.a);
}
";

    readonly Shader _shader = new() { Code = ShaderCode };
    readonly Shader _decalShader = new() { Code = DecalCode };
    readonly List<ShaderMaterial> _mats = new(), _decals = new();
    readonly HashSet<ShaderMaterial> _far = new();
    readonly Vector3[] _key = new Vector3[16], _water = new Vector3[4], _ice = new Vector3[4];
    readonly Vector3[][] _season = new Vector3[4][];
    readonly ImageTexture _snowTex;
    SeasonState _state;

    public SeasonState State => _state;

    public Seasonal(ArtAtlas art)
    {
        for (int s = 0; s < 4; s++) _season[s] = new Vector3[16];
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(ArtAtlas.Dir + "season_ramps.json")));
        var root = doc.RootElement;
        string[] seasons = { "spring", "summer", "autumn", "winter" }, fams = { "grass", "foliage", "leaf", "conifer" };
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            int f = Array.IndexOf(fams, row.GetProperty("family").GetString()), s = Array.IndexOf(seasons, row.GetProperty("season").GetString());
            var cols = row.GetProperty("colors");
            for (int i = 0; i < 4; i++) _season[s][f * 4 + i] = Hex(cols[i].GetString()!);
        }
        for (int i = 0; i < 16; i++) _key[i] = _season[1][i];
        if (root.TryGetProperty("water", out var w) && root.TryGetProperty("ice", out var ic))
            for (int i = 0; i < 4; i++) { _water[i] = Hex(w[i].GetString()!); _ice[i] = Hex(ic[i].GetString()!); }
        _snowTex = ImageTexture.CreateFromImage(art.Strip("overlay.snow.0", "overlay.snow.1", "overlay.snow.2", "overlay.snow.3", "overlay.snow.4", "overlay.snow.5"));
    }

    static Vector3 Hex(string h) => new(Convert.ToInt32(h.Substring(1, 2), 16) / 255f, Convert.ToInt32(h.Substring(3, 2), 16) / 255f, Convert.ToInt32(h.Substring(5, 2), 16) / 255f);

    /// <summary>A material on the season shader. <paramref name="ground"/>: terrain (snow, ice, wet); <paramref name="perSprite"/>: hash by the node origin; <paramref name="tilePx"/>: pixels per metre-cell of the texture; <paramref name="far"/>: also thins the canopy (no bare-tree sprites at Field and Command).</summary>
    public ShaderMaterial Create(bool ground, bool perSprite = false, float tilePx = 32f, Color? shade = null, bool far = false)
    {
        var m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("snow_tex", _snowTex);
        m.SetShaderParameter("key", _key);
        m.SetShaderParameter("wkey", _water);
        m.SetShaderParameter("ikey", _ice);
        m.SetShaderParameter("ground", ground ? 1f : 0f);
        m.SetShaderParameter("per_sprite", perSprite ? 1f : 0f);
        m.SetShaderParameter("tile_px", tilePx);
        m.SetShaderParameter("shade", shade ?? Colors.White);
        _mats.Add(m);
        if (far) _far.Add(m);
        Push(m);
        return m;
    }

    public ShaderMaterial CreateDecal(Color? shade = null)
    {
        var m = new ShaderMaterial { Shader = _decalShader };
        m.SetShaderParameter("shade", shade ?? Colors.White);
        _decals.Add(m);
        PushDecal(m);
        return m;
    }

    public void Apply(SeasonState s)
    {
        if (s == _state) return;
        _state = s;
        foreach (var m in _mats) Push(m);
        foreach (var m in _decals) PushDecal(m);
    }

    void Push(ShaderMaterial m)
    {
        var s = _state;
        m.SetShaderParameter("cur", _season[Math.Clamp(s.Season, 0, 3)]);
        m.SetShaderParameter("nxt", _season[(Math.Clamp(s.Season, 0, 3) + 1) % 4]);
        m.SetShaderParameter("frac", s.Frac);
        m.SetShaderParameter("snow", s.Snow);
        m.SetShaderParameter("ice", s.Ice);
        m.SetShaderParameter("wet", s.Wet);
        m.SetShaderParameter("leaf_loss", _far.Contains(m) ? s.LeafLoss : 0f);
    }

    void PushDecal(ShaderMaterial m)
    {
        m.SetShaderParameter("wet", _state.Wet);
        m.SetShaderParameter("hide", _state.Snow > 0.15f || _state.Ice > 0.5f ? 1f : 0f);
    }
}
