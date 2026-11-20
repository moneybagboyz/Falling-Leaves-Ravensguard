using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Game.World;

/// <summary>
/// P5-G (vehicle-designer 6): a vehicle drawn from its parts, for any design the player makes, an unfinished vehicle on its trestles (ghost parts faint, parts being fitted half seen) and a damaged one
/// (broken parts red): each 1 m cell's stack of <c>vpart.*</c> sprites ground up, the wheels last at the outer side, in eight facings (four drawn views and four baked diagonals). Holds no game state:
/// <see cref="Set"/> gives it the parts each frame they may have changed.
/// </summary>
public sealed partial class VehicleComposite : Node2D
{
    const int Tile = 32;
    static readonly string[] ViewOf = ["e", "se", "s", "sw", "w", "nw", "n", "ne"];
    readonly ArtAtlas _art;
    VehicleCatalog? _cat;
    readonly List<PartRow> _parts = new();
    int _width = 1, _length = 1, _dir8;
    float _seat;
    int _rev = -1;
    public bool Stuck { get; set; }
    double _t;

    public VehicleComposite(ArtAtlas art) { _art = art; TextureFilter = TextureFilterEnum.Nearest; }

    /// <summary>The facing as eight directions: 0 east, 1 south-east, 2 south ... 7 north-east (screen y down).</summary>
    public int Dir8 { get => _dir8; set { if (_dir8 != (value & 7)) { _dir8 = value & 7; QueueRedraw(); } } }

    /// <summary>The parts to draw (copied), the grid's size and the cell (from the rear) the node's position is at.</summary>
    public void Set(VehicleCatalog cat, IReadOnlyList<PartRow> parts, int width, int length, float seatCell, int revision)
    {
        if (revision == _rev && ReferenceEquals(cat, _cat)) return;
        _rev = revision; _cat = cat; _width = Math.Max(1, width); _length = Math.Max(1, length); _seat = seatCell;
        _parts.Clear(); _parts.AddRange(parts);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Stuck) { if (Rotation != 0) Rotation = 0; return; }
        // stuck in the mud: the team strains and the wagon rocks in its ruts
        _t += delta; Rotation = (float)(Math.Sin(_t * 18) * 0.02);
    }

    static int Layer(Ravensguard.Content.VehiclePartDef p)
    {
        var t = p.Tags;
        if (t.Contains("wheel") || t.Contains("axle") || t.Contains("tyre") || t.Contains("brake")) return 3;
        if (t.Contains("frame") || t.Contains("runner") || t.Contains("hull") || t.Contains("keel") || t.Contains("body")) return 0;
        if (t.Contains("deck") || t.Contains("bed") || t.Contains("gunwale")) return 1;
        return 2;
    }

    public override void _Draw()
    {
        if (_cat == null) return;
        double a = _dir8 * Math.PI / 4;
        var f = new Vector2((float)Math.Cos(a), (float)Math.Sin(a)); var s = new Vector2(-f.Y, f.X);
        string view = ViewOf[_dir8];
        bool diag = (_dir8 & 1) == 1;
        var order = new List<(int Layer, float Y, int I, Vector2 C)>(_parts.Count);
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i];
            float along = (p.Y + 0.5f - _seat) * Tile, across = (p.X + 0.5f - _width / 2f) * Tile;
            var c = f * along + s * across + new Vector2(0, -p.Deck * 12);
            var pd = _cat.PartList[p.Part];
            if (_width > 1 && (pd.Tags.Contains("wheel") || pd.Tags.Contains("tyre") || pd.Tags.Contains("brake"))) c += s * (p.X >= _width / 2 ? 5 : -5);   // a wheel stands proud of the body
            order.Add((Layer(_cat.PartList[p.Part]) + p.Deck * 4, c.Y, i, c));
        }
        order.Sort((x, y) => x.Layer != y.Layer ? x.Layer.CompareTo(y.Layer) : x.Y != y.Y ? x.Y.CompareTo(y.Y) : x.I.CompareTo(y.I));
        foreach (var (_, _, i, c) in order)
        {
            var p = _parts[i]; var d = _cat.PartList[p.Part];
            string id = $"vpart.{d.Id}.{view}";
            if (!_art.Has(id)) continue;
            var r = _art.Get(id); var tex = _art.Atlas(r.Atlas);
            var mod = p.State switch { PartState.Ghost => new Color(0.75f, 0.85f, 1f, 0.28f), PartState.InProgress => new Color(1f, 1f, 1f, 0.6f), PartState.Broken => new Color(1f, 0.45f, 0.4f), _ => Colors.White };
            // a wheel sits at the vehicle's outer side: the right-hand column's is mirrored across
            bool flip = !diag && Layer(d) == 3 && _width > 1 && p.X >= _width / 2 && (d.Tags.Contains("wheel") || d.Tags.Contains("tyre") || d.Tags.Contains("brake"));
            var size = new Vector2(r.W, r.H);
            if (flip) DrawSetTransform(c, 0, (_dir8 is 0 or 4) ? new Vector2(1, -1) : new Vector2(-1, 1));         // mirrored across the vehicle about the cell's centre
            DrawTextureRectRegion(tex, new Rect2((flip ? Vector2.Zero : c) - size / 2, size), new Rect2(r.X, r.Y, r.W, r.H), mod);
            if (flip) DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>The facing of a movement: the nearest of the eight directions (0 east, clockwise with y down).</summary>
    public static int Dir8Of(float dx, float dy)
    {
        if (dx == 0 && dy == 0) return -1;
        double ang = Math.Atan2(dy, dx); int k = (int)Math.Round(ang / (Math.PI / 4));
        return ((k % 8) + 8) % 8;
    }
}
