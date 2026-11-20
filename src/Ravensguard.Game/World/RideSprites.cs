using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>
/// Draws the hero's mount and cart and every animal and vehicle standing about (horses-and-carts spec 6): the 64 x 48 animal sprites of the art atlas, side-on and facing east (mirrored for west), and the vehicles at their footprint (<c>vehicle.*</c>, 32 px a metre, top-down with a slight tilt).
/// Holds no game state: each frame the <see cref="RideBook"/> says what exists and where, and nodes of rows that are gone are freed. The mounted hero is the standing rig lifted onto the horse's back
/// with the horse drawn over the legs, so no new rig frames are needed; a driven cart gets the hero on its bench and the team in front.
/// </summary>
public sealed class RideSprites
{
    const int Tile = 32;
    /// <summary>How far the rider's feet are lifted: onto the horse's back, legs behind its barrel.</summary>
    public const float RiderLift = 24f;
    public const float BenchLift = 17f;
    /// <summary>One team ahead of the next, in px: an ox is 2 m long, and the chain between pairs about half a metre more.</summary>
    const float PairGap = 80f;
    /// <summary>Half the gap between the two animals of a pair across the pole, in px.</summary>
    const float Abreast = 18f;
    readonly Node2D _parent; readonly ArtAtlas _art;
    readonly Dictionary<EntityId, Sprite2D> _animals = new(), _vehicles = new();
    readonly HashSet<EntityId> _seen = new();
    readonly Dictionary<EntityId, bool> _flip = new();
    readonly Dictionary<EntityId, Facing> _face = new();
    readonly Dictionary<EntityId, VehicleComposite> _composites = new();
    readonly Dictionary<EntityId, Vector2> _lastPos = new();
    readonly Dictionary<EntityId, int> _dir8 = new();

    /// <summary>Screenshots: every standing vehicle drawn in this facing of eight (-1: their own).</summary>
    public static int DebugDir8 = -1;

    static bool HasBroken(VehicleRow v) { foreach (var p in v.Parts) if (p.State == PartState.Broken) return true; return false; }

    /// <summary>A cheap change count of a vehicle's parts (their states and whether each still has hit points): the composite redraws only when it moves.</summary>
    static int Revision(VehicleRow v)
    {
        int h = v.Parts.Count;
        foreach (var p in v.Parts) h = h * 31 + (int)p.State * 7 + (p.HpMilli > 0 ? 1 : 0);
        return h;
    }
    double _t;

    public RideSprites(Node2D parent, ArtAtlas art) { _parent = parent; _art = art; }

    public int Count => _animals.Count + _vehicles.Count + _composites.Count;
    /// <summary>Vehicles drawn from their parts this frame (P5-G; smoke and screenshots).</summary>
    public int Composites => _composites.Count;

    /// <summary>What the hero's sprite should do this frame: how far to lift it, and whether it is on a mount or a bench (a seated pose is not drawn; the standing one is).</summary>
    public readonly record struct HeroSeat(float Lift, bool Seated, Facing Facing);

    public HeroSeat Sync(RideBook book, Facing heroFacing, bool heroMoving, Vector2 heroFeet, double delta, bool visible)
    {
        _t += delta; _seen.Clear();
        bool heroEast = heroFacing == Facing.East, heroWest = heroFacing == Facing.West;
        float lift = 0f; bool seated = false; var seatFacing = Facing.East;
        var mounted = book.IsMounted ? book.Mounted : EntityId.None; var driving = book.DrivenVehicle;
        foreach (var a in book.Animals.Rows)
        {
            if (!a.Alive) continue;
            if (a.State == AnimalState.Hitched && book.Vehicles.Find(a.Vehicle) != null) continue; // drawn with its cart
            var node = Get(_animals, a.Id, book.Breeds.At(a.Breed).Def.Sprite);
            bool ridden = a.Id == mounted;
            if (ridden)
            {
                if (heroEast) _flip[a.Id] = false; else if (heroWest) _flip[a.Id] = true; else if (!_flip.ContainsKey(a.Id)) _flip[a.Id] = false;
                node.Position = heroFeet + new Vector2(0, -1);                                  // a pixel behind the rider, so the horse is drawn first
                lift = RiderLift; seated = true; seatFacing = _flip.GetValueOrDefault(a.Id, false) ? Facing.West : Facing.East;
                // a little bob at a trot
                if (heroMoving) node.Position += new Vector2(0, (float)Math.Round(Math.Sin(_t * 14) * 1.0));
            }
            else node.Position = ToPx(a.XMm, a.YMm);
            node.FlipH = _flip.GetValueOrDefault(a.Id, (a.Id.Value & 1) == 1);
            node.Visible = visible;
        }
        foreach (var v in book.Vehicles.Rows)
        {
            var bp = book.Templates.At(v.Template); var st = bp.Stats;
            bool driven = driving != null && driving.Id == v.Id;
            if (driven) _face[v.Id] = heroFacing;
            var face = _face.GetValueOrDefault(v.Id, Facing.East);
            bool ns = face is Facing.North or Facing.South, west = face == Facing.West;
            bool loaded = book.Vehicles.CargoKg(v) > 0;
            // the art atlas has the vehicle facing east, a loaded one, and head-on versions facing north and south (the game mirrors east for west)
            string baseId = string.IsNullOrEmpty(bp.Def.Sprite) ? "object.draught_horse" : bp.Def.Sprite;
            string id = ns ? baseId + (face == Facing.North ? ".n" : ".s") : baseId;
            if (loaded && _art.Has(id + "_loaded")) id += "_loaded";
            else if (loaded && ns && _art.Has(baseId + (face == Facing.North ? ".n" : ".s") + "_loaded")) id = baseId + (face == Facing.North ? ".n_loaded" : ".s_loaded");
            if (!_art.Has(id)) { ns = false; id = baseId; }
            var pos = driven ? heroFeet + new Vector2(0, -1) : ToPx(v.XMm, v.YMm);
            float front = (st.LengthCells - SeatCell(st)) * Tile;
            // P5-G: a design of the player's, an unfinished vehicle, a damaged one, or a driven one heading on a diagonal is drawn from its parts (VehicleComposite)
            int dir8 = face switch { Facing.North => 6, Facing.South => 2, Facing.West => 4, _ => 0 };
            if (driven)
            {
                var mv = pos - _lastPos.GetValueOrDefault(v.Id, pos);
                if (mv.LengthSquared() > 0.25f) { int d8 = VehicleComposite.Dir8Of(mv.X, mv.Y); if (d8 >= 0) _dir8[v.Id] = d8; }
                _lastPos[v.Id] = pos;
                dir8 = _dir8.GetValueOrDefault(v.Id, dir8);
            }
            if (!driven && DebugDir8 >= 0) dir8 = DebugDir8;
            bool composite = bp.Player || !v.Finished || HasBroken(v) || (dir8 & 1) == 1;
            if (composite)
            {
                if (_vehicles.Remove(v.Id, out var old)) old.QueueFree();
                _seen.Add(v.Id);
                if (!_composites.TryGetValue(v.Id, out var comp)) { comp = new VehicleComposite(_art); _composites[v.Id] = comp; _parent.AddChild(comp); }
                comp.Set(book.Templates, v.Parts, st.WidthCells, st.LengthCells, SeatCell(st), Revision(v));
                comp.Dir8 = dir8; comp.Position = pos; comp.Visible = visible; comp.Stuck = v.Stuck;
                comp.ZIndex = 0;
            }
            else
            {
                if (_composites.Remove(v.Id, out var oldc)) oldc.QueueFree();
                var node = Get(_vehicles, v.Id, id);
                if (node.Texture != _art.Texture(id)) node.Texture = _art.Texture(id);
                const int pad = 4;
                int th = node.Texture.GetHeight(), tw = node.Texture.GetWidth();
                node.Position = pos + (v.Stuck ? new Vector2((float)Math.Round(Math.Sin(_t * 20) * 1.0), 0) : Vector2.Zero); node.Visible = visible; node.FlipH = !ns && west;
                if (ns)
                {
                    int rise = th - pad - st.LengthCells * Tile;
                    float ay = face == Facing.South ? rise + SeatCell(st) * Tile : rise + (st.LengthCells - SeatCell(st)) * Tile;
                    node.Offset = new Vector2(-tw / 2f, -ay);
                }
                else
                {
                    var (ax, ay) = Anchor(st, th);
                    node.Offset = new Vector2(west ? ax - tw : -ax, -ay);
                }
            }
            if (driven) { lift = BenchLift; seated = true; seatFacing = west ? Facing.West : Facing.East; }
            // the team stands in front: pairs side by side across the pole, further pairs one behind another ahead of them
            Vector2 fwd = composite ? new Vector2((float)Math.Cos(dir8 * Math.PI / 4), (float)Math.Sin(dir8 * Math.PI / 4))       // P5-G: a composite's team leads on its own facing, diagonals too
                : face switch { Facing.West => new(-1, 0), Facing.North => new(0, -1), Facing.South => new(0, 1), _ => new(1, 0) }, side = new Vector2(-fwd.Y, fwd.X);
            int n = 0;
            foreach (var tid in v.Team)
            {
                if (book.Animals.Find(tid) is not { } t) continue;
                var tn = Get(_animals, t.Id, book.Breeds.At(t.Breed).Def.Sprite);
                tn.FlipH = west;
                int slots = Math.Max(1, st.HitchSlots), pair = n / slots, k = n % slots;
                float reach = st.HitchKind switch { "shafts" => front - 20f, "traces" => front + 16f, _ => front - 8f } + pair * PairGap;      // the animal's shoulder at the yoke, the horse's chest in the shafts
                float across = slots == 1 ? 0f : (k - (slots - 1) / 2f) * 2f * Abreast;
                tn.Position = pos + fwd * reach + side * across + new Vector2(0, 1);
                tn.Visible = visible; n++;
            }
        }
        foreach (var key in new List<EntityId>(_animals.Keys)) if (!_seen.Contains(key)) { _animals[key].QueueFree(); _animals.Remove(key); }
        foreach (var key in new List<EntityId>(_vehicles.Keys)) if (!_seen.Contains(key)) { _vehicles[key].QueueFree(); _vehicles.Remove(key); }
        foreach (var key in new List<EntityId>(_composites.Keys)) if (!_seen.Contains(key)) { _composites[key].QueueFree(); _composites.Remove(key); }
        return new HeroSeat(lift, seated, seatFacing);
    }

    /// <summary>The cell (counted from the rear, in metres) the vehicle is placed at: the middle of its seat, or of the whole vehicle when it has none.</summary>
    static float SeatCell(VehicleStats st) => st.SeatRow >= 0 ? st.SeatRow + 0.5f : st.LengthCells / 2f;

    /// <summary>The anchor, in the sprite's own pixels, of a vehicle sprite (<c>vehicle.*</c> of the art atlas: footprint plus a 4 px margin, with the rise above the ground rectangle at the top).</summary>
    static (float X, float Y) Anchor(VehicleStats st, int spriteHeight)
    {
        const int pad = 4;
        int rise = spriteHeight - pad - st.WidthCells * Tile;
        return (pad + SeatCell(st) * Tile, rise + st.WidthCells * Tile / 2f);
    }

    /// <summary>Rows are in the region frame in millimetres, the same frame the hero is drawn in (window origin added): metres times the tile size.</summary>
    static Vector2 ToPx(long xMm, long yMm) => new((float)Math.Round(xMm / 1000.0 * Tile), (float)Math.Round(yMm / 1000.0 * Tile));

    Sprite2D Get(Dictionary<EntityId, Sprite2D> map, EntityId id, string sprite)
    {
        _seen.Add(id);
        if (map.TryGetValue(id, out var n)) return n;
        // the 64 x 48 sprites stand with their feet (or wheels) 4 px above the sheet's bottom: the node origin is the middle of the base
        var tex = _art.Texture(string.IsNullOrEmpty(sprite) ? "object.draught_horse" : sprite);
        n = new Sprite2D { Texture = tex, Centered = false, Offset = new Vector2(-32, -(tex.GetHeight() - 3)), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };      // vehicles replace the offset each frame
        map[id] = n; _parent.AddChild(n);
        return n;
    }
}
