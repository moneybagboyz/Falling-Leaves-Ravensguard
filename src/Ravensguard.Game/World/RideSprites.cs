using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>
/// Draws the hero's mount and cart and every animal and vehicle standing about (horses-and-carts spec 6): the 64 x 48 animal and cart sprites of the art atlas, side-on and facing east (mirrored for west).
/// Holds no game state: each frame the <see cref="RideBook"/> says what exists and where, and nodes of rows that are gone are freed. The mounted hero is the standing rig lifted onto the horse's back
/// with the horse drawn over the legs, so no new rig frames are needed; a driven cart gets the hero on its bench and the team in front.
/// </summary>
public sealed class RideSprites
{
    const int Tile = 32;
    /// <summary>How far the rider's feet are lifted: onto the horse's back, legs behind its barrel.</summary>
    public const float RiderLift = 21f;
    public const float BenchLift = 17f;
    readonly Node2D _parent; readonly ArtAtlas _art;
    readonly Dictionary<EntityId, Sprite2D> _animals = new(), _vehicles = new();
    readonly HashSet<EntityId> _seen = new();
    readonly Dictionary<EntityId, bool> _flip = new();
    double _t;

    public RideSprites(Node2D parent, ArtAtlas art) { _parent = parent; _art = art; }

    public int Count => _animals.Count + _vehicles.Count;

    /// <summary>What the hero's sprite should do this frame: how far to lift it, and whether it is on a mount or a bench (a seated pose is not drawn; the standing one is).</summary>
    public readonly record struct HeroSeat(float Lift, bool Seated);

    public HeroSeat Sync(RideBook book, Facing heroFacing, bool heroMoving, Vector2 heroFeet, double delta, bool visible)
    {
        _t += delta; _seen.Clear();
        bool heroEast = heroFacing == Facing.East, heroWest = heroFacing == Facing.West;
        float lift = 0f; bool seated = false;
        var mounted = book.IsMounted ? book.Mounted : EntityId.None; var driving = book.DrivenVehicle;
        foreach (var a in book.Animals.Rows)
        {
            if (!a.Alive) continue;
            if (a.State == AnimalState.Hitched && book.Vehicles.Find(a.Vehicle) != null) continue; // drawn with its cart
            var node = Get(_animals, a.Id, book.Breeds.At(a.Breed).Def.Sprite);
            bool ridden = a.Id == mounted;
            if (ridden)
            {
                if (heroEast) _flip[a.Id] = false; else if (heroWest) _flip[a.Id] = true;
                node.Position = heroFeet + new Vector2(0, 1);
                lift = RiderLift; seated = true;
                // a little bob at a trot
                if (heroMoving) node.Position += new Vector2(0, (float)Math.Round(Math.Sin(_t * 14) * 1.0));
            }
            else node.Position = ToPx(a.XMm, a.YMm);
            node.FlipH = _flip.GetValueOrDefault(a.Id, (a.Id.Value & 1) == 1);
            node.Visible = visible;
        }
        foreach (var v in book.Vehicles.Rows)
        {
            var bp = book.Templates.At(v.Template);
            var node = Get(_vehicles, v.Id, bp.Def.Sprite);
            bool driven = driving != null && driving.Id == v.Id;
            if (driven) { if (heroEast) _flip[v.Id] = false; else if (heroWest) _flip[v.Id] = true; }
            bool west = _flip.GetValueOrDefault(v.Id, false);
            var pos = driven ? heroFeet + new Vector2(0, -2) : ToPx(v.XMm, v.YMm);
            node.Position = pos; node.FlipH = west; node.Visible = visible;
            if (driven) { lift = BenchLift; seated = true; }
            // the team stands in front of the cart, side by side along the road
            int n = 0;
            foreach (var id in v.Team)
            {
                if (book.Animals.Find(id) is not { } t) continue;
                var tn = Get(_animals, t.Id, book.Breeds.At(t.Breed).Def.Sprite);
                tn.FlipH = west;
                tn.Position = pos + new Vector2((west ? -1 : 1) * (38 + n * 4), 2 - n * 3);
                tn.Visible = visible; n++;
            }
        }
        foreach (var key in new List<EntityId>(_animals.Keys)) if (!_seen.Contains(key)) { _animals[key].QueueFree(); _animals.Remove(key); }
        foreach (var key in new List<EntityId>(_vehicles.Keys)) if (!_seen.Contains(key)) { _vehicles[key].QueueFree(); _vehicles.Remove(key); }
        return new HeroSeat(lift, seated);
    }

    /// <summary>Rows are in the region frame in millimetres, the same frame the hero is drawn in (window origin added): metres times the tile size.</summary>
    static Vector2 ToPx(long xMm, long yMm) => new((float)Math.Round(xMm / 1000.0 * Tile), (float)Math.Round(yMm / 1000.0 * Tile));

    Sprite2D Get(Dictionary<EntityId, Sprite2D> map, EntityId id, string sprite)
    {
        _seen.Add(id);
        if (map.TryGetValue(id, out var n)) return n;
        // the 64 x 48 sprites stand with their feet (or wheels) 4 px above the sheet's bottom: the node origin is the middle of the base
        n = new Sprite2D { Texture = _art.Texture(string.IsNullOrEmpty(sprite) ? "object.draught_horse" : sprite), Centered = false, Offset = new Vector2(-32, -45), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        map[id] = n; _parent.AddChild(n);
        return n;
    }
}
