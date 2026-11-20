using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Game.World;

/// <summary>
/// P6-B5: fire in the street view (fire and burning 6). The sim's fire world is brought up to the live clock at a barrier once a sim-second (nothing at all while nothing burns), and a layer draws
/// what its published view holds: flames in three intensities and four frames over burning cells (roofs too), smoke rising from the bigger ones, a warm glow, and the char, ash and rubble a fire
/// leaves. A line at the top says "Fire!" with a direction while a fire burns within 100 m. Art is the baked overlay set (tools/Ravensguard.Art, Sprites/Fire.cs).
/// </summary>
public partial class StreetView
{
    FireLayer? _fireLayer;
    Label? _fireLabel;
    Task? _fireTask;
    long _fireSec = -1_000_000;
    volatile int _fireHeroX, _fireHeroY, _fireHeroZ;
    bool _fireWired;

    FireWorld? FireOf => Runtime?.State.HomeRecords?.Sim.Get<FireWorld>();

    void FireTick()
    {
        if (Runtime is not { } rt || FireOf is not { } fw || _world.Streamer == null) return;
        var s = _world.Snapshot;
        _fireHeroX = s.OriginX + s.Hero.CellX; _fireHeroY = s.OriginY + s.Hero.CellY; _fireHeroZ = s.Hero.Z;
        if (!_fireWired)
        {
            _fireWired = true;
            fw.PeopleNear = l => l.Add(new FirePerson(HeroId.Value, _fireHeroX, _fireHeroY, _fireHeroZ));
        }
        long now = _world.Clock.Now.Seconds;
        if (_fireTask is { IsCompleted: true } t)
        {
            if (t.IsFaulted) GD.PushWarning("[fire] step failed: " + t.Exception?.GetBaseException());
            _fireTask = null;
        }
        if (_fireTask == null && (fw.Active || fw.View == null) && now - _fireSec >= FireClock.GameSecPerSimS && _world.Clock.Speed != SimSpeed.Paused)
        {
            _fireSec = now;
            _fireTask = rt.AtBarrier(() => { fw.AdvanceTo(now); fw.Publish(now); return 0; });
        }
        var v = fw.View;
        if (v == null || (v.Cells.Count == 0 && v.Scars.Count == 0 && v.Buildings.Count == 0)) { _fireLayer?.Show(null, _viewZ); SetFireLabel(null); return; }
        if (_fireLayer == null) { _fireLayer = new FireLayer(_art) { Name = "FireLayer", ZIndex = 44 }; _street.AddChild(_fireLayer); }
        _fireLayer.Show(v, _viewZ);
        SetFireLabel(FireAlarm(v));
    }

    /// <summary>The "Fire!" line: the direction of the nearest burning cell within 100 m (or "close by" within 15), null when none.</summary>
    string? FireAlarm(FireView v)
    {
        int best = int.MaxValue, bx = 0, by = 0;
        foreach (var c in v.Cells) { int d = Math.Max(Math.Abs(c.X - _fireHeroX), Math.Abs(c.Y - _fireHeroY)); if (d < best) { best = d; bx = c.X; by = c.Y; } }
        foreach (var b in v.Buildings) { int d = Math.Max(Math.Abs(b.X - _fireHeroX), Math.Abs(b.Y - _fireHeroY)); if (d < best) { best = d; bx = b.X; by = b.Y; } }
        if (best > FireAi.ShoutM) return null;
        if (best <= 15) return L.T("FIRE_HUD_ALARM_NEAR");
        double ang = Math.Atan2(by - _fireHeroY, bx - _fireHeroX);                        // y grows south
        int oct = ((int)Math.Round(ang / (Math.PI / 4)) + 8) % 8;                          // 0 east, 1 south-east, 2 south ...
        string dir = new[] { "E", "SE", "S", "SW", "W", "NW", "N", "NE" }[oct];
        return L.F("FIRE_HUD_ALARM", L.T("FIRE_DIR_" + dir));
    }

    void SetFireLabel(string? text)
    {
        if (text == null) { if (_fireLabel != null) _fireLabel.Visible = false; return; }
        if (_fireLabel == null)
        {
            var cl = new CanvasLayer { Name = "FireHud", Layer = 12 };
            _fireLabel = new Label { ThemeTypeVariation = "TitleLabel", Modulate = new Color(1f, 0.55f, 0.25f), HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(280, 8), CustomMinimumSize = new Vector2(400, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            cl.AddChild(_fireLabel);
            AddChild(cl);
        }
        _fireLabel.Text = text; _fireLabel.Visible = true;
    }

    /// <summary>Dev (--ng-shot fire): a street of thatched cottages in a row beside the hero, the first lit, a 5 m/s wind along the street, the fire stepped on 120 sim-seconds.</summary>
    public void DebugFire()
    {
        if (Runtime is not { } rt || BuildWorld is not { } cw || FireOf is not { } fw) { GD.PushWarning("[fire] no world"); return; }
        var s = _world.Snapshot;
        int hx = s.OriginX + s.Hero.CellX, hy = s.OriginY + s.Hero.CellY;
        long now = _world.Clock.Now.Seconds;
        int placed = 0;
        for (int i = 0; i < 6; i++)
        {
            string r = DevBuild(["plan", "test_cottage", (4 + i * 12).ToString(), "-3"]); GD.Print("[fire] " + r);
            if (r == L.T("BUILD_NO_WORLD")) break;
            DevBuild(["stock"]); GD.Print("[fire] " + DevBuild(["finish", "999"]));
            placed++;
        }
        int lit = rt.AtBarrier(() =>
        {
            fw.Weather = _ => new FireWeather(5000, 270, 0, false, false);
            int n = 0;
            foreach (var c in cw.Book.Built.Values.Where(b => b.Piece == "roof_thatch").OrderBy(b => b.X).ThenBy(b => b.Y).Take(1)) if (fw.Ignite(c.X, c.Y, c.Z, now, FireCause.Hearth)) n++;
            fw.AdvanceTo(now + 120 * FireClock.GameSecPerSimS);
            fw.Publish(now);
            return n;
        }).GetAwaiter().GetResult();
        GD.Print($"[fire] {placed} cottages placed, {lit} fire lit, burning cells {fw.Book.Cells.Count}, roofs {cw.Book.Built.Values.Count(b => b.Piece == "roof_thatch")}");
        var vw = fw.View; var mid = vw != null && vw.Cells.Count > 0 ? (X: (int)vw.Cells.Average(c => c.X), Y: (int)vw.Cells.Average(c => c.Y)) : (X: hx + 8, Y: hy);
        _pan = new Vector2((mid.X - hx - 4) * Tile, (mid.Y - hy) * Tile);
        _world.SetSpeed(SimSpeed.Paused);
    }

    /// <summary>Flames, smoke, glow and scars over the street (presentation only; reads the fire view).</summary>
    sealed partial class FireLayer : Node2D
    {
        const int Tile = 32;
        readonly ArtAtlas _art;
        FireView? _v; int _z; double _t;
        readonly Glow _glow;

        public FireLayer(ArtAtlas art)
        {
            _art = art;
            _glow = new Glow { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
            AddChild(_glow);
        }

        public void Show(FireView? v, int z) { _v = v; _z = z; _glow.V = v; _glow.Z = z; QueueRedraw(); _glow.QueueRedraw(); }

        public override void _Process(double delta) { _t += delta; if (_v != null && (_v.Cells.Count > 0 || _v.Buildings.Count > 0)) { QueueRedraw(); } }

        static string Scar(ScarKind k, int x, int y) => k switch
        {
            ScarKind.Char => "overlay.char." + ((x * 7 + y * 3) & 1),
            ScarKind.Ash => "overlay.ash." + ((x * 5 + y * 11) & 1),
            ScarKind.Cracked => "overlay.cracked",
            ScarKind.Smoulder => "overlay.smoulder",
            ScarKind.Scorched => "overlay.scorch",
            _ => "overlay.rubble_char",
        };

        public override void _Draw()
        {
            var v = _v; if (v == null) return;
            int frame = (int)(_t * 8) & 3;
            // what a fire left: ground scars on this level, and where a roof burnt over bare ground, its ash
            var ground = new HashSet<(int, int)>();
            foreach (var s in v.Scars) if (s.Z == _z) ground.Add((s.X, s.Y));
            foreach (var s in v.Scars)
            {
                if (s.Z != _z && !(s.Z == _z + 1 && !ground.Contains((s.X, s.Y)))) continue;
                var k = s.Z == _z ? s.Kind : ScarKind.Ash;
                string id = Scar(k, s.X, s.Y);
                if (_art.Has(id)) DrawTextureRect(_art.Texture(id), new Rect2(s.X * Tile, s.Y * Tile, Tile, Tile), false, new Color(1, 1, 1, s.Kind == ScarKind.Ash ? 0.85f : 0.95f));
            }
            foreach (var c in v.Cells)
            {
                if (c.Z != _z && c.Z != _z + 1) continue;
                int inten = c.Smouldering ? 1 : Math.Clamp((int)c.Intensity, 1, 3);
                string id = $"overlay.fire.{inten}.{(frame + (c.X * 3 + c.Y)) & 3}";
                if (_art.Has(id)) DrawTextureRect(_art.Texture(id), new Rect2(c.X * Tile, c.Y * Tile, Tile, Tile), false);
            }
            // smoke over the bigger flames, a cell up and drifting east with the wind
            foreach (var c in v.Cells)
            {
                if ((c.Z != _z && c.Z != _z + 1) || c.Intensity < 2 || ((c.X + c.Y) & 1) != 0) continue;
                string id = $"overlay.smoke.{(frame + c.X) & 3}";
                if (_art.Has(id)) DrawTextureRect(_art.Texture(id), new Rect2(c.X * Tile + 6, (c.Y - 1) * Tile, Tile, Tile), false, new Color(1, 1, 1, c.Intensity == 3 ? 0.62f : 0.4f));
            }
            // a building burning where nobody watches: one plume at its middle
            foreach (var b in v.Buildings)
            {
                string id = $"overlay.smoke.{(frame + b.X) & 3}";
                if (_art.Has(id)) DrawTextureRect(_art.Texture(id), new Rect2(b.X * Tile - 16, (b.Y - 2) * Tile, Tile * 2, Tile * 2), false, new Color(1, 1, 1, 0.55f));
            }
        }

        sealed partial class Glow : Node2D
        {
            public FireView? V; public int Z;
            public override void _Draw()
            {
                if (V == null) return;
                foreach (var c in V.Cells)
                {
                    if (c.Z != Z && c.Z != Z + 1) continue;
                    float r = 30 + c.Intensity * 14;
                    DrawCircle(new Vector2(c.X * Tile + 16, c.Y * Tile + 14), r, new Color(1f, 0.45f, 0.1f, 0.05f + 0.02f * c.Intensity));
                }
            }
        }
    }
}
