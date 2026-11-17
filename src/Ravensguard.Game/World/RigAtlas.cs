using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Ravensguard.Sim.Live;

namespace Ravensguard.Game.World;

/// <summary>The baked paper-doll rig (tools/Ravensguard.Art `rig`): rig_32x64.png + rig_index.json, copied to assets/art.
/// A figure is a stack of layer sprites per frame; see <see cref="RigActor"/>.</summary>
public sealed class RigAtlas
{
    public readonly record struct Piece(int Plane, int X, int Y, int W, int H, int Ox, int Oy);

    public string[] Order = System.Array.Empty<string>();
    public readonly Dictionary<string, string[]> Outfits = new();
    public readonly Dictionary<string, string> OutfitStance = new();
    public readonly Dictionary<string, int> Frames = new();
    public readonly int[] AnchorX = { 16, 16, 15 }; // front, back, side
    ImageTexture _tex = null!;
    readonly Dictionary<string, Piece[][]> _sprites = new();
    readonly Dictionary<Piece, AtlasTexture> _cache = new();

    static RigAtlas? _shared;
    public static RigAtlas Shared => _shared ??= Load();

    public static RigAtlas Load()
    {
        var a = new RigAtlas();
        string dir = ArtAtlas.Dir;
        using var doc = JsonDocument.Parse(ArtAtlas.ReadText(dir + "rig_index.json"));
        var r = doc.RootElement;
        var img = ArtAtlas.ReadImage(dir + r.GetProperty("atlas").GetString());
        a._tex = ImageTexture.CreateFromImage(img);
        a.Order = r.GetProperty("order").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var anc = r.GetProperty("anchorX");
        a.AnchorX[0] = anc.GetProperty("front").GetInt32(); a.AnchorX[1] = anc.GetProperty("back").GetInt32(); a.AnchorX[2] = anc.GetProperty("side").GetInt32();
        foreach (var f in r.GetProperty("actions").EnumerateObject()) a.Frames[f.Name] = f.Value.GetInt32();
        foreach (var o in r.GetProperty("outfits").EnumerateObject())
        {
            a.Outfits[o.Name] = o.Value.GetProperty("layers").EnumerateArray().Select(e => e.GetString()!).ToArray();
            a.OutfitStance[o.Name] = o.Value.GetProperty("stance").GetString()!;
        }
        foreach (var layer in r.GetProperty("sprites").EnumerateObject())
            foreach (var view in layer.Value.EnumerateObject())
                foreach (var stance in view.Value.EnumerateObject())
                    foreach (var act in stance.Value.EnumerateObject())
                        a._sprites[$"{layer.Name}/{view.Name}/{stance.Name}/{act.Name}"] = act.Value.EnumerateArray()
                            .Select(fr => fr.EnumerateArray().Select(p => { var q = p.EnumerateArray().Select(n => n.GetInt32()).ToArray(); return new Piece(q[0], q[1], q[2], q[3], q[4], q[5], q[6]); }).ToArray())
                            .ToArray();
        return a;
    }

    public static readonly string[] ViewNames = { "front", "back", "side" };

    /// <summary>Pieces of one layer for a frame (empty when the layer draws nothing there).</summary>
    public Piece[] Get(string layer, int view, string stance, string action, int frame)
    {
        if (!_sprites.TryGetValue($"{layer}/{ViewNames[view]}/{stance}/{action}", out var fr) || fr.Length == 0) return System.Array.Empty<Piece>();
        return fr[frame % fr.Length];
    }

    public AtlasTexture Texture(Piece p)
    {
        if (_cache.TryGetValue(p, out var t)) return t;
        t = new AtlasTexture { Atlas = _tex, Region = new Rect2(p.X, p.Y, p.W, p.H) };
        _cache[p] = t;
        return t;
    }

    /// <summary>Facing to (view index, mirrored). West is the side view mirrored.</summary>
    public static (int View, bool Mirror) ViewOf(Facing f) => f switch { Facing.South => (0, false), Facing.North => (1, false), Facing.East => (2, false), _ => (2, true) };
}

/// <summary>A paper-doll figure: one Sprite2D per (plane, layer) of an outfit, restacked each frame from the atlas.
/// The node origin is the feet position (the 32x64 frame sits at (-16,-64)); west mirrors the whole stack.</summary>
public sealed partial class RigActor : Node2D
{
    readonly RigAtlas _rig;
    readonly string _stance;
    readonly Node2D _flip = new();
    readonly List<(string Layer, int Plane, Sprite2D Node)> _sprites = new();
    readonly string _outfit;

    public RigActor(RigAtlas rig, string outfit)
    {
        _rig = rig; _outfit = outfit; _stance = rig.OutfitStance[outfit];
        AddChild(_flip);
        void Add(string layer, int plane)
            => _sprites.Add((layer, plane, MakeSprite()));
        Sprite2D MakeSprite() { var s = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Visible = false }; _flip.AddChild(s); return s; }
        Add("outline." + outfit, 0);
        for (int p = 0; p < 3; p++)
            foreach (var l in rig.Order)
                if (rig.Outfits[outfit].Contains(l)) Add(l, p);
    }

    readonly record struct Slot(bool Visible, Texture2D? Tex, Vector2 Pos);
    readonly Dictionary<(Facing Facing, string Action, int Frame), Slot[]> _poses = new();
    (Facing, string, int)? _last;

    /// <summary>Sets the figure's pose. Resolved slots are cached per (facing, action, frame), so a steady pose costs a dictionary lookup, and an unchanged one nothing.</summary>
    public void Pose(Facing facing, string action, int frame)
    {
        int n = _rig.Frames.TryGetValue(action, out var c) ? c : 1;
        frame = ((frame % n) + n) % n;
        var key = (facing, action, frame);
        if (_last == key) return;
        _last = key;
        if (!_poses.TryGetValue(key, out var slots)) _poses[key] = slots = Resolve(facing, action, frame);
        var (_, mirror) = RigAtlas.ViewOf(facing);
        _flip.Scale = new Vector2(mirror ? -1 : 1, 1);
        for (int i = 0; i < slots.Length; i++)
        {
            var node = _sprites[i].Node;
            node.Visible = slots[i].Visible;
            if (!slots[i].Visible) continue;
            node.Texture = slots[i].Tex; node.Position = slots[i].Pos;
        }
    }

    Slot[] Resolve(Facing facing, string action, int frame)
    {
        var (view, _) = RigAtlas.ViewOf(facing);
        int ax = _rig.AnchorX[view];
        var res = new Slot[_sprites.Count];
        string? lastLayer = null; RigAtlas.Piece[] pcs = System.Array.Empty<RigAtlas.Piece>();
        for (int i = 0; i < res.Length; i++)
        {
            var (layer, plane, _) = _sprites[i];
            if (layer != lastLayer) { pcs = _rig.Get(layer, view, _stance, action, frame); lastLayer = layer; }
            var piece = pcs.FirstOrDefault(p => p.Plane == plane && p.W > 0);
            res[i] = piece.W == 0 ? new Slot(false, null, default) : new Slot(true, _rig.Texture(piece), new Vector2(piece.Ox - ax, piece.Oy - 64));
        }
        return res;
    }
}
