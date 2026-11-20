using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Building;

namespace Ravensguard.Game.World;

/// <summary>
/// P5-A3: the room overlay (dev console <c>rooms</c>, or <c>--shot-rooms</c>): RoomDetector v2 run over the built cells near the hero, each recognised room outlined in a colour by what it is for,
/// with its name, beds and stations at the middle. Generated buildings are finished, so their roofs are laid first (the "Roof over" step). Build mode (step 2) draws the same thing live.
/// </summary>
public partial class StreetView
{
    RoomsOverlay? _roomsOv;
    BuildingLibrary? _roomsLib;
    const int RoomsRadius = 56;

    string DevRooms(string[] a)
    {
        if (a.Length > 0 && a[0] == "off")
        {
            _roomsOv?.QueueFree(); _roomsOv = null;
            return L.T("ROOMS_OFF");
        }
        int n = ShowRooms();
        return n < 0 ? L.T("ROOMS_NO_CONTENT") : L.F("ROOMS_ON", n, _viewZ);
    }

    /// <summary>Detects the rooms around the hero and draws them. Returns the number of rooms found (-1: no content).</summary>
    public int ShowRooms()
    {
        var w = RoomsNearHero(RoomsRadius); if (w == null) return -1;
        var m = Map;
        _roomsOv?.QueueFree();
        _roomsOv = new RoomsOverlay(w, m.OriginX, m.OriginY, () => _viewZ) { Name = "RoomsOverlay", ZIndex = 50 };
        _street.AddChild(_roomsOv);
        int count = w.Rooms.Count(r => r.Primary != null);
        GD.Print($"[rooms] {w.Rooms.Count} rooms near the hero, {count} recognised, {w.StructureIds.Count()} structures");
        return w.Rooms.Count;
    }

    /// <summary>RoomDetector v2 over the built cells within <paramref name="radius"/> cells of the hero, roofs laid (generated buildings are finished). Null without content.</summary>
    RoomWorld? RoomsNearHero(int radius)
    {
        try { _roomsLib ??= LoadLibrary(); } catch (Exception e) { GD.PushWarning("[rooms] " + e.Message); return null; }
        var w = _roomsLib.NewWorld();
        var m = Map; var g = m.Grid; var s = _world.Snapshot;
        int x0 = Math.Max(0, s.Hero.CellX - radius), x1 = Math.Min(g.Width, s.Hero.CellX + radius);
        int y0 = Math.Max(0, s.Hero.CellY - radius), y1 = Math.Min(g.Height, s.Hero.CellY + radius);
        var edits = new List<CellEdit>();
        for (int z = g.MinZ; z <= g.MaxZ; z++)
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (m.StructureAt(x, y, z) is { } sid) edits.Add(new CellEdit(x, y, z, CellLayer.Structure, sid));
                    if (m.FixtureAt(x, y, z) is { } fid) edits.Add(new CellEdit(x, y, z, CellLayer.Fixture, fid));
                }
        w.Apply(edits);
        w.RoofOver("roof_thatch");
        return w;
    }

    /// <summary>P5-E (crafting-full 4.1): the stations of the hero's room and within 6 m, read from the map for the crafting screen. Null without content.</summary>
    Ravensguard.Sim.Crafting.ReachScan? CraftReachHere(int light)
    {
        if (_source?.Content is not { } content) return null;
        var w = RoomsNearHero(12); if (w == null) return null;
        _craftDb ??= Ravensguard.Sim.Crafting.RecipeDb.Build(content, new Ravensguard.Sim.Goods.GoodCatalog(content));
        var h = _world.Snapshot.Hero;
        return Ravensguard.Sim.Crafting.CraftReach.Scan(w, _craftDb, h.CellX, h.CellY, h.Z, null, [], light);
    }
    Ravensguard.Sim.Crafting.RecipeDb? _craftDb;

    /// <summary>Smoke hook: rooms found around the hero (the overlay is left on).</summary>
    public int DebugRoomCount() => ShowRooms();

    sealed partial class RoomsOverlay : Node2D
    {
        const int Tile = 32;
        readonly record struct Drawn(int Z, Color Fill, Color Line, List<Vector2I> Cells, string Label, Vector2I At);
        readonly List<Drawn> _rooms = [];
        readonly Func<int> _viewZ;
        readonly int _ox, _oy;
        int _shownZ = int.MinValue;

        public RoomsOverlay(RoomWorld w, int ox, int oy, Func<int> viewZ)
        {
            _ox = ox; _oy = oy; _viewZ = viewZ;
            foreach (var r in w.Rooms)
            {
                var cells = r.Cells.Select(c => new Vector2I(c.X, c.Y)).ToList();
                // the cell closest to the middle carries the label
                var mid = new Vector2((float)cells.Average(c => c.X), (float)cells.Average(c => c.Y));
                var at = cells.OrderBy(c => ((Vector2)c - mid).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X).First();
                var col = ColourOf(r);
                _rooms.Add(new Drawn(r.Z, new Color(col, 0.20f), new Color(col, 0.95f), cells, LabelOf(r), at));
            }
        }

        static Color ColourOf(RoomV2 r) => r.Primary == null ? new Color(0.62f, 0.62f, 0.62f) : r.Primary.Purpose switch
        {
            "sleeping" or "solar" => new Color(0.45f, 0.65f, 1.0f),
            "hearth" => new Color(1.0f, 0.62f, 0.25f),
            "storage" or "cellar" => new Color(0.75f, 0.58f, 0.35f),
            "gathering" or "market" or "shop" => new Color(0.78f, 0.5f, 0.95f),
            "livestock" or "dovecote" => new Color(0.55f, 0.85f, 0.4f),
            "worship" or "burial" or "belfry" => new Color(0.95f, 0.9f, 0.55f),
            "yard" => new Color(0.9f, 0.4f, 0.4f),
            var p when p.StartsWith("work:", StringComparison.Ordinal) => new Color(0.4f, 0.85f, 0.7f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        static string LabelOf(RoomV2 r)
        {
            if (r.Primary == null) return "";
            string name = r.Primary.Name;
            if (r.Primary.Purpose.StartsWith("work:", StringComparison.Ordinal) && r.Stations.Count > 0)
                return L.F("ROOMS_STATIONS", name, string.Join(", ", r.Stations.Select(s => $"{s.Value} {s.Key}")));
            if (r.Beds > 0 && r.Indoor) return L.F("ROOMS_BEDS", name, r.Beds);
            return name;
        }

        public override void _Process(double delta)
        {
            int z = _viewZ();
            if (z != _shownZ) { _shownZ = z; QueueRedraw(); }
        }

        public override void _Draw()
        {
            var font = ThemeDB.FallbackFont;
            foreach (var r in _rooms)
            {
                if (r.Z != _shownZ) continue;
                var set = r.Cells.ToHashSet();
                foreach (var c in r.Cells)
                {
                    var p = new Vector2((c.X + _ox) * Tile, (c.Y + _oy) * Tile);
                    DrawRect(new Rect2(p, new Vector2(Tile, Tile)), r.Fill);
                    if (!set.Contains(new Vector2I(c.X, c.Y - 1))) DrawLine(p, p + new Vector2(Tile, 0), r.Line, 2);
                    if (!set.Contains(new Vector2I(c.X, c.Y + 1))) DrawLine(p + new Vector2(0, Tile), p + new Vector2(Tile, Tile), r.Line, 2);
                    if (!set.Contains(new Vector2I(c.X - 1, c.Y))) DrawLine(p, p + new Vector2(0, Tile), r.Line, 2);
                    if (!set.Contains(new Vector2I(c.X + 1, c.Y))) DrawLine(p + new Vector2(Tile, 0), p + new Vector2(Tile, Tile), r.Line, 2);
                }
                if (r.Label.Length == 0) continue;
                var size = font.GetStringSize(r.Label, HorizontalAlignment.Left, -1, 10);
                var at = new Vector2((r.At.X + _ox) * Tile + Tile / 2f - size.X / 2f, (r.At.Y + _oy) * Tile + Tile / 2f + 4);
                DrawStringOutline(font, at, r.Label, HorizontalAlignment.Left, -1, 10, 4, new Color(0.05f, 0.04f, 0.04f, 0.95f));
                DrawString(font, at, r.Label, HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1));
            }
        }
    }
}
