using System;
using Godot;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Game.World;

/// <summary>
/// Rain, snow, fog and lightning drawn over the world (REDESIGN_PLAN 3c). Rain and snowfall are the baked 32 px overlay loops (four frames, 8 px of fall each)
/// tiled over the screen and scrolled with the wind; fog is the dithered fog tiles at low alpha drifting at their own pace. Lightning is a full-screen white flash
/// that goes through <see cref="Accessibility.FlashScale"/>, so it never fires when the player has switched flashes off. It lives in a CanvasLayer of the world
/// viewport, so it ignores the camera.
/// </summary>
public sealed class WeatherOverlay
{
    readonly CanvasLayer _layer = new() { Name = "Weather", Layer = 5 };
    readonly Control _holder = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly ImageTexture[] _rain = new ImageTexture[4], _snow = new ImageTexture[4], _fog = new ImageTexture[2];
    readonly TextureRect _rainA, _rainB, _snowA, _snowB, _fogA, _fogB;
    readonly ColorRect _flash = new() { Color = new Color(0.92f, 0.95f, 1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly ColorRect _mist = new() { Color = new Color(0.72f, 0.76f, 0.8f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly Random _rng = new(7);
    double _t, _nextBolt = 4, _boltT = -1;
    float _boltPeak;
    Vector2 _scrollA, _scrollB, _scrollFog;
    public float FlashAlpha => _flash.Color.A;

    public Node Node => _layer;

    public WeatherOverlay(ArtAtlas art)
    {
        for (int i = 0; i < 4; i++) { _rain[i] = ImageTexture.CreateFromImage(art.Crop($"overlay.rain.{i}")); _snow[i] = ImageTexture.CreateFromImage(art.Crop($"overlay.snowfall.{i}")); }
        for (int i = 0; i < 2; i++) _fog[i] = ImageTexture.CreateFromImage(art.Crop($"overlay.fog.{i}"));
        TextureRect Tile(Texture2D tex)
        {
            var r = new TextureRect { Texture = tex, StretchMode = TextureRect.StretchModeEnum.Tile, MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Visible = false };
            _holder.AddChild(r);
            return r;
        }
        _holder.AddChild(_mist);
        _fogA = Tile(_fog[0]); _fogB = Tile(_fog[1]);
        _rainA = Tile(_rain[0]); _rainB = Tile(_rain[1]); _snowA = Tile(_snow[0]); _snowB = Tile(_snow[1]);
        _holder.AddChild(_flash);
        _layer.AddChild(_holder);
    }

    /// <summary>The ambient light multiplier the weather imposes (1 = none): cloud, rain, storm and fog all darken the day.</summary>
    public static float Dim(WeatherView w)
    {
        float d = 0.20f * w.Cloud + 0.16f * w.Precip + 0.07f * w.Fog + (w.Thunder ? 0.12f : 0f);
        if (w.Snowing) d *= 0.7f; // snow light is brighter than rain light
        return 1f - Math.Clamp(d, 0f, 0.5f);
    }

    /// <summary>Advances the loops and the lightning, and lays the layers over a viewport of <paramref name="vp"/> physical pixels at pixel scale <paramref name="scale"/>.</summary>
    public void Update(double dt, WeatherView w, Vector2I vp, int scale)
    {
        _t += dt;
        _holder.Size = new Vector2(vp.X, vp.Y);
        float wind = Math.Clamp(w.WindMps, 0f, 14f);
        float dir = (w.WindFrom - 270) * MathF.PI / 180f; // blowing from the west: streaks drift east
        var drift = new Vector2(MathF.Cos(dir), 0.1f * MathF.Sin(dir)) * (6f + wind * 5f);
        _scrollA += drift * (float)dt; _scrollB += drift * 0.8f * (float)dt; _scrollFog += new Vector2(MathF.Cos(dir), 0) * (3f + wind * 2f) * (float)dt;

        bool rain = w.Raining && w.Precip > 0, snow = w.Snowing && w.Precip > 0;
        Layer(_rainA, rain, _rain[(int)(_t * 14) % 4], w.Precip * 0.85f, _scrollA, vp, scale, 0);
        Layer(_rainB, rain && w.Precip > 0.6f, _rain[(int)(_t * 14 + 2) % 4], (w.Precip - 0.4f) * 0.7f, _scrollB + new Vector2(13, 7), vp, scale, 1);
        Layer(_snowA, snow, _snow[(int)(_t * 5) % 4], 0.5f + w.Precip * 0.45f, _scrollA * 0.3f + new Vector2(0, (float)_t * 24f), vp, scale, 2);
        Layer(_snowB, snow && w.Precip > 0.5f, _snow[(int)(_t * 5 + 1) % 4], 0.55f, _scrollB * 0.45f + new Vector2(17, (float)_t * 40f + 9), vp, scale, 3);
        float fog = Math.Clamp(w.Fog, 0f, 1f);
        Layer(_fogA, fog > 0.02f, _fog[0], fog * 0.55f, _scrollFog, vp, scale * 2, 4);
        Layer(_fogB, fog > 0.02f, _fog[1], fog * 0.4f, _scrollFog * -0.6f + new Vector2(21, 11), vp, scale * 2, 5);
        _mist.Size = _holder.Size;
        _mist.Color = new Color(0.72f, 0.76f, 0.8f, fog * 0.28f);

        Lightning(dt, w);
        _flash.Size = _holder.Size;
    }

    static void Layer(TextureRect r, bool on, Texture2D tex, float alpha, Vector2 scroll, Vector2I vp, int scale, int salt)
    {
        r.Visible = on && alpha > 0.01f;
        if (!r.Visible) return;
        r.Texture = tex;
        r.Modulate = new Color(1, 1, 1, Math.Clamp(alpha, 0f, 1f));
        int tile = 32 * scale;
        r.Scale = new Vector2(scale, scale);
        // the rect is one tile larger than the view on each side and slides back by the scroll offset modulo one tile
        r.Size = new Vector2((vp.X + 2 * tile) / scale, (vp.Y + 2 * tile) / scale);
        float ox = ((scroll.X * scale) % tile + tile) % tile, oy = ((scroll.Y * scale) % tile + tile) % tile;
        r.Position = new Vector2(-tile + Mathf.Round(ox), -tile + Mathf.Round(oy));
    }

    void Lightning(double dt, WeatherView w)
    {
        float peak = Accessibility.FlashScale(0.6f);
        if (!w.Thunder || peak <= 0f)
        {
            _boltT = -1; _nextBolt = 3 + _rng.NextDouble() * 6;
            _flash.Color = new Color(_flash.Color, 0f);
            return;
        }
        _nextBolt -= dt;
        if (_boltT < 0 && _nextBolt <= 0) { _boltT = 0; _boltPeak = peak; _nextBolt = 5 + _rng.NextDouble() * 9; }
        if (_boltT >= 0)
        {
            _boltT += dt;
            // a bright strike, a dip, a second softer strike, then a fade
            double a = _boltT < 0.06 ? 1.0 : _boltT < 0.14 ? 0.15 : _boltT < 0.22 ? 0.7 : Math.Max(0, 0.7 * (1 - (_boltT - 0.22) / 0.35));
            _flash.Color = new Color(0.92f, 0.95f, 1f, (float)(a * _boltPeak));
            if (_boltT > 0.6) { _boltT = -1; _flash.Color = new Color(_flash.Color, 0f); }
        }
    }

    /// <summary>Starts a flash now (used by the screenshot flags). Does nothing when flashes are off.</summary>
    public void Strike() { if (Accessibility.FlashScale(1f) > 0f) { _boltT = 0; _boltPeak = Accessibility.FlashScale(0.6f); } }
}
