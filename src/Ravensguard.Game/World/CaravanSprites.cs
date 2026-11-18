using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>
/// Draws the caravans the LODBridge holds in the bubble (<see cref="CaravanBridge"/>): the cart or ox wagon sprites of the art atlas, a draught ox or horse in front of each, and the
/// master, drivers and guards as rig figures walking beside them. Holds no game state: every frame the live list says what exists and where, and sprites of caravans that left it
/// are freed. Nodes are children of the street's y-sorted layer so they pass behind and in front of the hero and the scenery like everything else.
/// </summary>
public sealed class CaravanSprites
{
    const int Tile = 32;
    readonly Node2D _parent; readonly RigAtlas _rig; readonly ArtAtlas _art;
    readonly Dictionary<(EntityId Caravan, LivePartKind Kind, int Index), Node2D> _nodes = new();
    readonly HashSet<(EntityId, LivePartKind, int)> _seen = new();
    double _t;

    public CaravanSprites(Node2D parent, RigAtlas rig, ArtAtlas art) { _parent = parent; _rig = rig; _art = art; }

    public int Count => _nodes.Count;

    /// <summary>Places every part of every live caravan; <paramref name="originXM"/> and <paramref name="originYM"/> are the continent metres of the street's frame origin (the home region's corner).</summary>
    public void Sync(IReadOnlyList<LiveCaravan> live, long originXM, long originYM, double delta, bool visible)
    {
        _t += delta; _seen.Clear();
        foreach (var c in live)
        {
            bool moving = c.State is CaravanState.Outbound or CaravanState.Inbound;
            foreach (var p in c.Parts)
            {
                var key = (c.Id, p.Kind, p.Index);
                _seen.Add(key);
                if (!_nodes.TryGetValue(key, out var node)) { node = Make(p.Kind); _nodes[key] = node; _parent.AddChild(node); }
                float px = (float)((p.XMm / 1000.0 - originXM) * Tile), py = (float)((p.YMm / 1000.0 - originYM) * Tile);
                node.Position = new Vector2(Mathf.Round(px), Mathf.Round(py));
                node.Visible = visible;
                var facing = p.HeadX > Math.Abs(p.HeadY) ? Facing.East : -p.HeadX > Math.Abs(p.HeadY) ? Facing.West : p.HeadY > 0 ? Facing.South : Facing.North;
                if (node is RigActor actor)
                    actor.Pose(facing, moving ? "walk" : "idle", moving ? (int)(_t * 5 + p.Index) : (int)(_t / 0.7));
                else if (node is Sprite2D sp)
                    sp.FlipH = p.HeadX < 0 || (p.HeadX == 0 && false);
            }
        }
        foreach (var key in new List<(EntityId, LivePartKind, int)>(_nodes.Keys))
            if (!_seen.Contains(key)) { _nodes[key].QueueFree(); _nodes.Remove(key); }
    }

    Node2D Make(LivePartKind kind)
    {
        switch (kind)
        {
            case LivePartKind.Master: return new RigActor(_rig, "traveller");
            case LivePartKind.Driver: return new RigActor(_rig, "peasant");
            case LivePartKind.Guard: return new RigActor(_rig, "spearman");
            default:
            {
                string id = kind switch { LivePartKind.OxWagon => "object.cart_goods", LivePartKind.Cart => "object.cart_barrels", LivePartKind.PackHorse => "object.draught_horse", _ => "object.draught_ox" };
                // the wagons' sprites stand 64 x 48 with the wheels' bottom edge 4 px above the sheet's bottom: the origin is the middle of the base
                var sp = new Sprite2D { Texture = _art.Texture(id), Centered = false, Offset = new Vector2(-32, -45), TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
                return sp;
            }
        }
    }
}
