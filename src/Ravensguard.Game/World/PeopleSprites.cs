using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;

namespace Ravensguard.Game.World;

/// <summary>
/// Draws the people the LODBridge holds in the bubble (<see cref="PeopleBridge"/>): one layered rig figure per live person, positioned from the 20 Hz snapshot (interpolated to the frame),
/// walking along their plan, working or standing by their job, or lying down while they sleep. Holds no game state: every frame the live list says who exists and where, and figures of people
/// who left it are freed. Nodes are children of the street's y-sorted layer so people pass behind and in front of the hero and the scenery. Names show while the nameplate key (Alt) is held.
/// </summary>
public sealed class PeopleSprites
{
    const int Tile = 32;
    readonly Node2D _parent; readonly RigAtlas _rig;
    readonly Dictionary<EntityId, Figure> _nodes = new();
    readonly HashSet<EntityId> _seen = new();
    double _t;

    sealed class Figure { public RigActor Actor = null!; public Label? Name; public string Outfit = ""; }

    public PeopleSprites(Node2D parent, RigAtlas rig) { _parent = parent; _rig = rig; }

    public int Count => _nodes.Count;

    /// <summary>Which rig outfit stands for a person (art stand-ins until the clothing spec dresses people by what they own).</summary>
    public static string OutfitOf(Occupation o, SocialClass c) => o switch
    {
        Occupation.Guard => "spearman",
        Occupation.Lord or Occupation.Steward or Occupation.Mayor or Occupation.GuildMaster or Occupation.Merchant or Occupation.Priest => "traveller",
        _ => c == SocialClass.Gentry ? "traveller" : "peasant",
    };

    /// <summary>Places every live person. <paramref name="originXM"/>, <paramref name="originYM"/> are the continent metres of the street's frame origin (the home region's corner); <paramref name="alpha"/> blends the last two sim steps.</summary>
    public void Sync(IReadOnlyList<LivePerson> live, long originXM, long originYM, float alpha, double delta, bool visible, bool names)
    {
        _t += delta; _seen.Clear();
        foreach (var p in live)
        {
            _seen.Add(p.Id);
            string outfit = OutfitOf(p.Occupation, p.Class);
            if (!_nodes.TryGetValue(p.Id, out var f))
            {
                f = new Figure { Actor = new RigActor(_rig, outfit), Outfit = outfit };
                _nodes[p.Id] = f; _parent.AddChild(f.Actor);
            }
            double x = p.PrevXMm + (p.XMm - p.PrevXMm) * (double)alpha, y = p.PrevYMm + (p.YMm - p.PrevYMm) * (double)alpha;
            f.Actor.Position = new Vector2(Mathf.Round((float)((x / 1000.0 - originXM) * Tile)), Mathf.Round((float)((y / 1000.0 - originYM) * Tile)));
            f.Actor.Visible = visible;
            var facing = p.HeadX > Math.Abs(p.HeadY) ? Facing.East : -p.HeadX > Math.Abs(p.HeadY) ? Facing.West : p.HeadY > 0 ? Facing.South : Facing.North;
            int salt = (int)(p.Id.Counter % 7);
            switch (p.Action)
            {
                case JobKind.Commute: f.Actor.Pose(facing, "jog", (int)(_t * 8) + salt); break;       // a commute is a brisk 5 m/s (abstract spec decision A2)
                case JobKind.Sleep: f.Actor.Pose(facing, "fall", 2); break;
                case JobKind.Work: f.Actor.Pose(facing, "idle", (int)(_t / 0.45) + salt); break;
                default: f.Actor.Pose(facing, "idle", (int)(_t / 0.7) + salt); break;
            }
            if (names && visible)
            {
                if (f.Name == null)
                {
                    f.Name = new Label { Text = p.Name, Position = new Vector2(-60, -84), CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "HintLabel", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 50 };
                    f.Actor.AddChild(f.Name);
                }
                f.Name.Visible = true;
            }
            else if (f.Name != null) f.Name.Visible = false;
        }
        if (_nodes.Count == live.Count) return;
        foreach (var id in new List<EntityId>(_nodes.Keys))
            if (!_seen.Contains(id)) { _nodes[id].Actor.QueueFree(); _nodes.Remove(id); }
    }
}
