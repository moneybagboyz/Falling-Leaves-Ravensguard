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
    readonly Node2D _parent; readonly RigAtlas _rig; readonly ClothingFeed? _clothes;
    readonly Dictionary<EntityId, Figure> _nodes = new();
    readonly HashSet<EntityId> _seen = new();
    double _t;

    sealed class Figure { public RigActor Actor = null!; public Label? Name; public string Outfit = ""; public string Look = ""; }

    public PeopleSprites(Node2D parent, RigAtlas rig, ClothingFeed? clothes = null) { _parent = parent; _rig = rig; _clothes = clothes; }

    public int Count => _nodes.Count;

    /// <summary>Which rig outfit stands for a person (art stand-ins until the clothing spec dresses people by what they own).</summary>
    public static string OutfitOf(Occupation o, SocialClass c) => o switch
    {
        Occupation.Guard => "spearman",
        Occupation.Lord or Occupation.Steward or Occupation.Mayor or Occupation.GuildMaster or Occupation.Merchant or Occupation.Priest => "traveller",
        _ => c == SocialClass.Gentry ? "traveller" : "peasant",
    };

    static readonly Color[] PeasantTunic = { new(0.60f, 0.46f, 0.32f), new(0.52f, 0.51f, 0.48f), new(0.60f, 0.32f, 0.22f), new(0.75f, 0.68f, 0.52f), new(0.44f, 0.46f, 0.28f), new(0.40f, 0.33f, 0.27f) };
    static readonly Color[] BurgherTunic = { new(0.20f, 0.32f, 0.60f), new(0.20f, 0.48f, 0.28f), new(0.64f, 0.20f, 0.18f), new(0.76f, 0.60f, 0.20f), new(0.42f, 0.24f, 0.46f), new(0.18f, 0.44f, 0.50f) };
    static readonly Color[] GentryTunic = { new(0.44f, 0.20f, 0.52f), new(0.62f, 0.12f, 0.16f), new(0.14f, 0.24f, 0.62f), new(0.80f, 0.64f, 0.22f), new(0.12f, 0.12f, 0.16f), new(0.10f, 0.40f, 0.34f) };
    static readonly Color[] Hose = { new(0.28f, 0.22f, 0.17f), new(0.35f, 0.35f, 0.34f), new(0.16f, 0.15f, 0.15f), new(0.40f, 0.28f, 0.20f), new(0.22f, 0.26f, 0.20f) };
    static readonly Color[] Skin = { new(1.00f, 1.00f, 1.00f), new(0.90f, 0.82f, 0.74f), new(0.80f, 0.70f, 0.62f), new(1.08f, 1.02f, 0.96f) };

    /// <summary>A person's clothes, the same every time: dyes by class (undyed browns and greys for peasants, dyed blues, greens and reds for burghers, richer for gentry), keyed on the person's id.</summary>
    static Dictionary<string, Color> Clothes(LivePerson p)
    {
        ulong h = DetRng.Mix(p.Id.Value ^ 0x41505052UL);
        var tunics = p.Class == SocialClass.Gentry ? GentryTunic : p.Class == SocialClass.Burgher || p.Occupation is Occupation.Merchant or Occupation.Smith or Occupation.Baker or Occupation.Innkeeper ? BurgherTunic : PeasantTunic;
        var d = new Dictionary<string, Color>
        {
            ["tunic"] = tunics[(int)(h % (ulong)tunics.Length)], ["hose"] = Hose[(int)((h >> 8) % (ulong)Hose.Length)],
            ["hood"] = tunics[(int)((h >> 16) % (ulong)tunics.Length)], ["cloak"] = tunics[(int)((h >> 24) % (ulong)tunics.Length)],
            ["body"] = Skin[(int)((h >> 32) % (ulong)Skin.Length)],
        };
        return d;
    }

    /// <summary>
    /// Where the origin of a lying figure goes so that the body is centred on its bed cell: turned a quarter turn, the 64 pixel body runs from the origin along +x with the visible body (about 58 pixels, from 3
    /// to 61) centred 32 pixels along, so the origin is 32 pixels back. Along x the head is to the east. A bed in a row of beds side by side is lain across the row's other axis (<paramref name="axis"/> 1), so each
    /// sleeper keeps to their own bed: the body is then turned to run north to south, the head to the north.
    /// </summary>
    public static Vector2 BedOffset(int axis) => axis == 1 ? new Vector2(0, 32) : new Vector2(-32, 0);

    /// <summary>Places every live person. <paramref name="originXM"/>, <paramref name="originYM"/> are the continent metres of the street's frame origin (the home region's corner); <paramref name="alpha"/> blends the last two sim steps.</summary>
    public void Sync(IReadOnlyList<LivePerson> live, long originXM, long originYM, float alpha, double delta, bool visible, bool names, Vector2? hero = null, int viewZ = 0)
    {
        _t += delta; _seen.Clear();
        foreach (var p in live)
        {
            _seen.Add(p.Id);
            // what the person wears decides the figure: the layers of the pieces on them (a cloak or hood only if one is worn), tinted by each piece's dye or material (clothing spec, plan 2.7)
            ClothingFeed.Look? look = _clothes != null && p.Body != null ? _clothes.LookOf(p.Body, p.Occupation) : null;
            string outfit = look?.Outfit ?? OutfitOf(p.Occupation, p.Class);
            string lookKey = look?.Key ?? "";
            if (_nodes.TryGetValue(p.Id, out var f) && (f.Outfit != outfit || f.Look != lookKey))
            {
                // they put a cloak on or took one off: the figure is made again (the stance and the nameplate go with it)
                f.Actor.QueueFree(); _nodes.Remove(p.Id); f = null!;
            }
            if (f == null)
            {
                f = new Figure { Actor = look is { } lk && lk.Layers.Length > 0 ? new RigActor(_rig, outfit, lk.Layers) : new RigActor(_rig, outfit), Outfit = outfit, Look = lookKey };
                var tints = look is { } l2 ? l2.Tints : Clothes(p); tints.Remove("body");
                if (look != null && lookKey != "spearman") tints.Remove("linen");
                f.Actor.Dress(tints);
                _nodes[p.Id] = f; _parent.AddChild(f.Actor);
            }
            double x = p.PrevXMm + (p.XMm - p.PrevXMm) * (double)alpha, y = p.PrevYMm + (p.YMm - p.PrevYMm) * (double)alpha;
            var pos = new Vector2(Mathf.Round((float)((x / 1000.0 - originXM) * Tile)), Mathf.Round((float)((y / 1000.0 - originYM) * Tile)));
            if (hero is { } hp && p.Action != JobKind.Sleep && p.Z == viewZ)
            {
                // nobody stands on the hero: a person within a metre and a half is eased out to that distance, the way they would step aside (view only)
                var away = pos - hp; float len = away.Length();
                if (len < Tile * 1.5f) pos = hp + (len < 1f ? new Vector2(1, 0) : away / len) * Tile * 1.5f;
            }
            f.Actor.Position = pos;
            // a person on another floor than the one on screen is not drawn (the view shows one level at a time)
            bool here = visible && p.Z == viewZ;
            f.Actor.Visible = here;
            var facing = p.HeadX > Math.Abs(p.HeadY) ? Facing.East : -p.HeadX > Math.Abs(p.HeadY) ? Facing.West : p.HeadY > 0 ? Facing.South : Facing.North;
            int salt = (int)(p.Id.Counter % 7);
            // a sleeper lies down on the bed: the figure is turned on its side (head to the left, feet toward the foot of the bed) and eased onto the cell
            bool lying = p.Action == JobKind.Sleep && !p.Walking;
            bool across = lying && p.LieAxis == 1;
            f.Actor.RotationDegrees = lying && !across ? 90 : 0;
            if (lying) f.Actor.Position += BedOffset(p.LieAxis);
            if (across) f.Actor.Pose(Facing.South, "fall", 2);
            else if (lying) f.Actor.Pose(Facing.South, "idle", 0);
            else if (p.Walking && p.Action != JobKind.Sleep) { f.Actor.Pose(facing, "jog", (int)(_t * 8) + salt); }       // a walk is a brisk 5 m/s (abstract spec decision A2)
            else switch (p.Action)
            {
                case JobKind.Sleep: f.Actor.Pose(facing, "fall", 2); break;
                case JobKind.Work or JobKind.Haul or JobKind.GuardCaravan: f.Actor.Pose(facing, "idle", (int)(_t / 0.45) + salt); break;
                default: f.Actor.Pose(facing, "idle", (int)(_t / 0.7) + salt); break;
            }
            if (names && here)
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
