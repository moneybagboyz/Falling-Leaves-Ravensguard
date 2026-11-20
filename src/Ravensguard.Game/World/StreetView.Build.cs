using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Content;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Game.World;

/// <summary>
/// P5-B: construction in the street view (build-mode-and-construction 6). The sim's construction world is brought up to the live clock at a barrier every two game seconds (the work engine
/// credits whole game minutes, so the cadence never changes the result), built pieces are laid into the loaded chunks as they finish (the window re-stamps them: walls, floors and stairs
/// with their portals), and a layer draws the ghosts (blue ready, amber waiting, the piece's own art at 40%), scaffolding and a progress bar on pieces in work, the crews at their pieces,
/// the piles, and a groaning floor (red, with dust) before it falls. Build mode (P5-C) adds the placing tools; until then the dev console's <c>build</c> places plans.
/// </summary>
public partial class StreetView
{
    GhostLayer? _ghosts;
    System.Threading.Tasks.Task? _buildTask;
    long _buildSec = -1_000_000, _builtApplied = -1, _buildEventSeen;
    readonly Dictionary<(int X, int Y, int Z), (string? S, string? F)> _builtShown = [];
    const long BuildStrideSec = 2;

    ConstructionWorld? BuildWorld => Runtime?.State.HomeRecords?.Sim.Get<ConstructionWorld>();

    void BuildTick()
    {
        if (Runtime is not { } rt || _world.Streamer == null) return;
        var book = rt.State.Construction; if (book == null) return;
        var cw = BuildWorld;
        var s = _world.Snapshot;
        if (cw != null)
        {
            cw.HeroCell = (s.OriginX + s.Hero.CellX, s.OriginY + s.Hero.CellY, s.Hero.Z);
            if (!cw.ViewWanted) { cw.ViewWanted = true; rt.AtBarrier(() => { cw.Publish(_world.Clock.Now.Seconds); return 0; }); }
        }
        long now = _world.Clock.Now.Seconds;
        var v = book.View;
        if (_buildTask is { IsCompleted: true } bt)
        {
            if (bt.IsFaulted) GD.PushWarning("[build] construction step failed: " + bt.Exception?.GetBaseException());
            _buildTask = null;
        }
        if (cw != null && _buildTask == null && (v == null || v.Sites.Count > 0 || v.Groans.Count > 0) && now - _buildSec >= BuildStrideSec && _world.Clock.Speed != SimSpeed.Paused)
        {
            _buildSec = now;
            _buildTask = rt.AtBarrier(() => { cw.AdvanceTo(now); return 0; });
        }
        if (v == null) return;
        if (v.BuiltVersion != _builtApplied) { _builtApplied = v.BuiltVersion; LayBuilt(v); }
        bool groaned = false, fell = false;
        foreach (var e in v.Events)
        {
            if (e.Serial <= _buildEventSeen) continue;
            _buildEventSeen = e.Serial;
            switch (e.Kind)
            {
                case ConstructionEventKind.Groan when e.Sec >= now - 15 && !groaned: groaned = true; _router?.ShowToast(L.T("BUILD_GROANS"), 4f); break;
                case ConstructionEventKind.Fell when e.Sec >= now - 15 && !fell: fell = true; _ghosts?.Shake(); _router?.ShowToast(L.T("BUILD_FELL"), 3f); break;
                case ConstructionEventKind.HeroFell: _world.DropToLevel(e.Z); _router?.ShowToast(L.T("BUILD_YOU_FELL"), 4f); break;
                case ConstructionEventKind.Finished when e.Sec >= now - 60: _router?.ShowToast(L.T("BUILD_SITE_FINISHED"), 3f); break;
            }
        }
        if (_ghosts == null)
        {
            _ghosts = new GhostLayer(_art) { Name = "GhostLayer", ZIndex = 45 };
            _street.AddChild(_ghosts);
        }
        _ghosts.Show(v, _viewZ, now, Map.OriginX, Map.OriginY);
    }

    /// <summary>Lays the built cells of the view into the loaded chunks (and clears the ones that came down), so the window re-stamps them: tiles, walking, portals.</summary>
    void LayBuilt(ConstructionView v)
    {
        var st = _world.Streamer!;
        var now = new HashSet<(int, int, int)>();
        int laid = 0;
        foreach (var b in v.Built)
        {
            var k = (b.X, b.Y, b.Z); now.Add(k);
            if (_builtShown.TryGetValue(k, out var was) && was == (b.Structure, b.Fixture)) continue;
            if (st.SetLoadedTile(b.X, b.Y, b.Z, b.Structure, b.Fixture)) laid++;
            if (b.Structure != null) st.ClearLoadedProps(b.X, b.Y, b.Z, k => PropClearing.Of(k) == PropHere.Clears);   // P5-C: building clears the trees and clutter there
            _builtShown[k] = (b.Structure, b.Fixture);
        }
        foreach (var k in _builtShown.Keys.Where(k => !now.Contains(k)).ToList())
        {
            if (st.SetLoadedTile(k.Item1, k.Item2, k.Item3, null, null)) laid++;
            _builtShown.Remove(k);
        }
        if (laid > 0) GD.Print($"[build] {laid} cells laid into the street ({v.Built.Count} built)");
    }

    // ------------------------------------------------------------------ the dev console: build ...

    string DevBuild(string[] a)
    {
        if (Runtime is not { } rt || BuildWorld is not { } cw) return L.T("BUILD_NO_WORLD");
        var s = _world.Snapshot;
        int hx = s.OriginX + s.Hero.CellX, hy = s.OriginY + s.Hero.CellY;
        long now = _world.Clock.Now.Seconds;
        string verb = a.Length > 0 ? a[0] : "help";
        T At<T>(Func<T> f) => rt.AtBarrier(f).GetAwaiter().GetResult();
        EntityId Near() => At(() => cw.Book.Sites.Values.Where(x => x.Open).OrderBy(x => Math.Abs(x.PileX - hx) + Math.Abs(x.PileY - hy)).ThenBy(x => x.Id).Select(x => x.Id).FirstOrDefault());
        switch (verb)
        {
            case "plan":
            {
                string bp = a.Length > 1 ? a[1] : "test_cottage";
                int dx = a.Length > 2 && int.TryParse(a[2], out var px) ? px : 3, dy = a.Length > 3 && int.TryParse(a[3], out var py) ? py : -2;
                // the first free spot near the asked one (not on the road or a street, not over a standing house)
                bool asked = a.Length > 3;
                var size = PlanSize(bp);
                bool Open(int x0, int y0)
                {
                    var m = Map;
                    for (int y = -1; y <= size.H; y++) for (int x = -1; x <= size.W; x++)
                        {
                            int lx = x0 + x - m.OriginX, ly = y0 + y - m.OriginY;
                            if (!m.Grid.InBounds(lx, ly, 0)) return false;
                            var g = m.GroundAt(lx, ly, 0);
                            if (g is Ravensguard.Sim.Live.GroundKind.Cobble or Ravensguard.Sim.Live.GroundKind.Dirt or Ravensguard.Sim.Live.GroundKind.Water or Ravensguard.Sim.Live.GroundKind.DeepWater || m.StructureAt(lx, ly, 0) != null) return false;
                        }
                    // no tree or prop in the way (placing does not fell trees; clearing is build mode's)
                    int ax = x0 - 1 - m.OriginX, ay = y0 - 1 - m.OriginY;
                    return !m.Props.Any(p => p.Z == 0 && p.X >= ax - 1 && p.X <= ax + size.W + 1 && p.Y >= ay && p.Y <= ay + size.H + 2);
                }
                var spots = new List<(int X, int Y)>();
                for (int ring = 0; ring <= (asked ? 0 : 20); ring++)
                    for (int oy = -ring; oy <= ring; oy++)
                        for (int ox = -ring; ox <= ring; ox++)
                            if (Math.Max(Math.Abs(ox), Math.Abs(oy)) == ring && (asked || Open(hx + dx + ox * 2, hy + dy + oy * 2))) spots.Add((hx + dx + ox * 2, hy + dy + oy * 2));
                var r = At(() => Dev(cw, () =>
                {
                    var last = PlaceResult.No("cn_nothing_placed");
                    foreach (var (x, y) in spots.Take(40))
                    {
                        last = cw.PlacePlan(EconomyState.HeroOwner, PurseKind.Person, -1, bp, x, y, 0, false, now);
                        if (last.Ok) return last;
                    }
                    return last;
                }));
                return r.Ok ? L.F("BUILD_PLACED", bp, r.Ghosts.Count) : L.F("BUILD_REFUSED", L.F(r.Key, r.Args), string.Join(", ", r.Refused.Take(3).Select(x => $"{x.Piece}@{x.X},{x.Y},{x.Z}")));
            }
            case "stock":
            {
                var id = Near(); if (id == default) return L.T("BUILD_NO_SITE");
                At(() => { cw.DevStock(id, now); return 0; });
                return L.T("BUILD_STOCKED");
            }
            case "crew":
            {
                var id = Near(); if (id == default) return L.T("BUILD_NO_SITE");
                int n = a.Length > 1 && int.TryParse(a[1], out var c) ? c : 4;
                var r = At(() => cw.Hire(EconomyState.HeroOwner, -1, id, n, a.Length > 2 && a[2] == "skilled", now));
                return L.F(r.Key, r.Args);
            }
            case "finish":
            {
                var id = Near(); if (id == default) return L.T("BUILD_NO_SITE");
                int n = a.Length > 1 && int.TryParse(a[1], out var c) ? c : 10;
                int done = At(() => cw.DevFinish(id, n, 3, 450, now));
                return L.F("BUILD_FINISHED_N", done);
            }
            case "collapse":
            {
                // a platform on one post beside the hero, then the post knocked out: the floor groans and falls
                var cells = new List<PlaceCell> { new(hx + 2, hy + 1, 0, "post_timber") };
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) cells.Add(new PlaceCell(hx + 1 + x, hy + y, 1, "floor_plank"));
                string res = At(() =>
                {
                    var p = Dev(cw, () => cw.Place(EconomyState.HeroOwner, PurseKind.Person, -1, cells, now));
                    if (!p.Ok) return L.F(p.Key, p.Args);
                    cw.DevStock(p.Site, now); cw.DevFinish(p.Site, 99, 0, 0, now);
                    var d = cw.Destroy(hx + 2, hy + 1, 0, now, EconomyState.HeroOwner, "knocked out");
                    return L.F(d.Key, d.Args);
                });
                return res;
            }
            default: return DevContracts(a) ?? L.T("BUILD_HELP");                    // P5-D: build growth [civic] [pct] | build hire
        }
    }

    (int W, int H) PlanSize(string bp)
    {
        try { var g = PieceCosts.Of(_source!.Content!).Library.Grid(bp); return (g.Width, g.Height); } catch { return (8, 8); }
    }

    /// <summary>The dev console builds wherever the hero stands: the land rule is lifted for the one call (build mode, P5-C, places through the hero's desk under the land rule).</summary>
    static T Dev<T>(ConstructionWorld cw, Func<T> f)
    {
        var may = cw.May; cw.May = null;
        try { return f(); } finally { cw.May = may; }
    }

    /// <summary>Screenshot driver hook (--ng-shot build): a cottage half built, scaffolding up close, or a floor groaning before it falls; the camera looks at the site.</summary>
    public void DebugBuild(string stage)
    {
        if (BuildWorld is not { } cw) { GD.PushWarning("[build] no construction world"); return; }
        try { DebugBuildStage(stage); }
        finally
        {
            var s = _world.Snapshot;
            var site = Runtime!.AtBarrier(() => cw.Book.Sites.Values.OrderByDescending(x => x.Id).Select(x => (x.PileX, x.PileY)).FirstOrDefault()).GetAwaiter().GetResult();
            if (site != default) _pan = new Vector2((site.PileX + 3 - s.OriginX - s.Hero.CellX) * Tile, (site.PileY + 2 - s.OriginY - s.Hero.CellY) * Tile);
        }
    }

    void DebugBuildStage(string stage)
    {
        switch (stage)
        {
            case "collapse":
                GD.Print("[build] " + DevBuild(["collapse"]));
                break;
            default:
                GD.Print("[build] " + DevBuild(["plan", "test_cottage"]));
                GD.Print("[build] " + DevBuild(["stock"]));
                GD.Print("[build] " + DevBuild(["finish", stage == "scaffold" ? "12" : "30"]));
                GD.Print("[build] " + DevBuild(["crew", "4"]));
                break;
        }
    }

    /// <summary>Ghosts, scaffolding, crews, piles and groans over the street (presentation only; reads the construction view).</summary>
    sealed partial class GhostLayer : Node2D
    {
        const int Tile = 32;
        readonly ArtAtlas _art;
        ConstructionView? _v; int _z; long _now; int _ox, _oy; double _t, _shake;
        static readonly Color ReadyCol = new(0.45f, 0.65f, 1f), Waiting = new(1f, 0.72f, 0.25f), Groaning = new(1f, 0.25f, 0.2f), Wood = new(0.55f, 0.38f, 0.2f);

        public GhostLayer(ArtAtlas art) { _art = art; }

        public void Show(ConstructionView v, int z, long now, int ox, int oy)
        {
            bool changed = !ReferenceEquals(v, _v) || z != _z || ox != _ox || oy != _oy;
            _v = v; _z = z; _now = now; _ox = ox; _oy = oy;
            if (changed || v.Groans.Count > 0 || v.Crew.Count > 0 || _shake > 0) QueueRedraw();
        }

        public void Shake() => _shake = 0.6;

        public override void _Process(double delta)
        {
            _t += delta;
            if (_shake > 0) { _shake = Math.Max(0, _shake - delta); Position = _shake > 0 ? new Vector2((float)Math.Sin(_t * 60) * 3, 0) : Vector2.Zero; }
        }

        static string? Sprite(GhostView g) => Build.BuildCatalog.Sprite(g.Piece, g.Kind);      // P5-C: one table for the ghosts and build mode's preview

        Rect2 Cell(int x, int y) => new(new Vector2((x - 0) * Tile, (y - 0) * Tile), new Vector2(Tile, Tile));

        public override void _Draw()
        {
            var v = _v; if (v == null) return;
            var font = ThemeDB.FallbackFont;
            // ghosts on this level: the piece's art faint and tinted, an outline by state; the level above as dashed outlines (a roof to come)
            foreach (var g in v.Ghosts)
            {
                if (g.Z != _z && g.Z != _z + 1) continue;
                var r = Cell(g.X, g.Y);
                bool above = g.Z != _z;
                var col = g.State == GhostState.Blocked ? Waiting : ReadyCol;
                if (g.Work == GhostWork.Deconstruct) col = new Color(0.9f, 0.4f, 0.9f);
                if (above) { DrawDashed(r, new Color(col, 0.85f)); continue; }
                if (Sprite(g) is { } id) DrawTextureRect(_art.Texture(id), r, false, new Color(col.R, col.G, col.B, 0.42f));
                else DrawRect(r, new Color(col, 0.22f));
                DrawRect(r.Grow(-1), new Color(col, 0.8f), false, 1f);
                if (g.State == GhostState.InProgress || g.ProgressPm > 0)
                {
                    // scaffolding: two poles, a ledger and a brace, and the progress bar
                    DrawLine(r.Position + new Vector2(4, 2), r.Position + new Vector2(4, Tile - 2), Wood, 2f);
                    DrawLine(r.Position + new Vector2(Tile - 4, 2), r.Position + new Vector2(Tile - 4, Tile - 2), Wood, 2f);
                    DrawLine(r.Position + new Vector2(3, Tile / 2), r.Position + new Vector2(Tile - 3, Tile / 2), Wood, 2f);
                    DrawLine(r.Position + new Vector2(4, Tile - 3), r.Position + new Vector2(Tile - 4, 4), new Color(Wood, 0.8f), 1.5f);
                    var bar = new Rect2(r.Position + new Vector2(3, Tile - 6), new Vector2(Tile - 6, 3));
                    DrawRect(bar, new Color(0, 0, 0, 0.7f));
                    DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Math.Clamp(g.ProgressPm, 0, 1000) / 1000f, bar.Size.Y)), new Color(0.4f, 0.95f, 0.4f));
                }
                else if (g.State == GhostState.Blocked)
                    DrawString(font, r.Position + new Vector2(Tile - 10, 11), g.Block == GhostBlock.Materials ? "m" : g.Block == GhostBlock.Support ? "s" : "u", HorizontalAlignment.Left, -1, 10, Waiting);
            }
            // piles and rubble
            foreach (var grp in v.Piles.GroupBy(p => (p.X, p.Y, p.Z)))
            {
                var (x, y, z) = grp.Key; long mass = grp.Sum(p => p.MassG); bool rubble = grp.Any(p => p.Rubble);
                if (z != _z) continue;
                var r = Cell(x, y);
                DrawTextureRect(_art.Texture(rubble ? "object.rocks" : "object.sack_pile"), r, false);
                string kg = $"{mass / 1000:N0} kg";
                DrawStringOutline(font, r.Position + new Vector2(0, -2), kg, HorizontalAlignment.Left, -1, 10, 3, new Color(0, 0, 0, 0.9f));
                DrawString(font, r.Position + new Vector2(0, -2), kg, HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1));
            }
            // groaning cells (any level at or above this one): flashing red, dust falling
            foreach (var gr in v.Groans)
            {
                float flash = 0.35f + 0.35f * (float)Math.Abs(Math.Sin(_t * 9));
                foreach (var (x, y, z) in gr.Cells)
                {
                    if (z < _z) continue;
                    var r = Cell(x, y);
                    if (z > _z) DrawTextureRect(_art.Texture("terrain.wood_floor"), r, false, new Color(1, 1, 1, 0.55f));     // the floor above, cut away in this view, shown as it shakes
                    DrawRect(r, new Color(Groaning, flash * (z == _z ? 1f : 0.6f)), false, 2f);
                    for (int k = 0; k < 4; k++)
                    {
                        float fx = (float)((x * 7 + y * 13 + k * 11) % Tile), ph = (float)((_t * (0.9 + k * 0.2) + k * 0.37 + x * 0.11) % 1.0);
                        DrawCircle(r.Position + new Vector2(fx, ph * Tile), 1.4f, new Color(0.8f, 0.75f, 0.65f, 0.9f - ph * 0.7f));
                    }
                }
                long left = Math.Max(0, gr.FallSec - _now);
                var at = Cell(gr.Cells[0].X, gr.Cells[0].Y).Position + new Vector2(0, -6);
                string txt = L.F("BUILD_GROAN_LABEL", left);
                DrawStringOutline(font, at, txt, HorizontalAlignment.Left, -1, 11, 4, new Color(0, 0, 0, 0.95f));
                DrawString(font, at, txt, HorizontalAlignment.Left, -1, 11, Groaning);
            }
            // the crews at their pieces (a hammer swinging while they work)
            int n = 0;
            foreach (var c in v.Crew)
            {
                if (c.Z != _z) continue;
                var feet = new Vector2(c.X * Tile + Tile / 2 + ((n++ % 3) - 1) * 6, c.Y * Tile + Tile - 2);
                string sprite = c.Class == CrewClass.Skilled ? "person.smith" : "person.peasant_hat";
                var tex = _art.Texture(sprite);
                DrawTextureRect(tex, new Rect2(feet - new Vector2(16, 60), new Vector2(32, 64)), false);
                if (c.Working)
                {
                    float sw = (float)Math.Sin(_t * 7 + n) * 0.9f;
                    var hand = feet + new Vector2(9, -30);
                    DrawLine(hand, hand + new Vector2((float)Math.Cos(sw - 1.2) * 10, (float)Math.Sin(sw - 1.2) * 10), new Color(0.35f, 0.25f, 0.15f), 2f);
                }
            }
        }

        void DrawDashed(Rect2 r, Color c)
        {
            var p = new[] { r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y), r.Position };
            for (int i = 0; i < 4; i++) DrawDashedLine(p[i], p[i + 1], c, 2f, 4f);
        }
    }
}
