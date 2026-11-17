using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Game.World;

/// <summary>Somebody to mark at the far bands: position in world metres, a team colour name (art team: neutral red blue green gold iron) and whether it is the hero.</summary>
public readonly record struct FarPerson(float X, float Y, string Team, bool Hero);

/// <summary>
/// The Field and Command bands (REDESIGN_PLAN 2.7). The world is drawn from cached per-chunk images baked from chunk content, never from the live sim: Field at
/// 8 px/m (ground with roofs over buildings, a transparent top layer for crowns and shrubs) and Command at 2 px/m (one flat image per chunk). Chunks beyond the sim
/// bubble are generated on worker threads straight from the generator (nothing is recorded in the chunk store), baked, and cached; a chunk's image is rebuilt when its
/// revision moves. People are drawn as baked 8x16 minis at Field and as banner blocks at Command; the hero carries a gold ring.
/// </summary>
public sealed partial class FarLayer : Node2D
{
    sealed class Entry
    {
        public Sprite2D? G, T;
        public int Version = int.MinValue;
        public bool Busy;
        public long Used;
    }
    readonly record struct Result((int X, int Y) Key, bool Command, int Version, Rgba Ground, Rgba? Top);

    public const int MaxFieldChunks = 220, MaxCommandChunks = 2400;

    readonly ArtAtlas _art;
    readonly FarArt _far;
    readonly ChunkStreamer? _streamer;
    readonly HamletMap? _hamlet;
    readonly Node2D _fieldRoot = new() { Name = "Field" }, _cmdRoot = new() { Name = "Command" };
    readonly Dictionary<(int X, int Y), Entry> _field = new(), _cmd = new();
    readonly ConcurrentQueue<Result> _done = new();
    readonly ShaderMaterial _groundMat, _topMat;
    readonly Markers _markers;
    int _busy;
    long _tick;
    public int BakedField => _field.Count;
    public int BakedCommand => _cmd.Count;
    public int Pending => _busy;
    public int ChunkLimit => _streamer != null ? 1000 : ((_hamlet?.Grid.Width ?? 96) / 32);

    public FarLayer(ArtAtlas art, Seasonal seasons, ChunkStreamer? streamer, HamletMap? hamlet)
    {
        _art = art; _streamer = streamer; _hamlet = hamlet;
        _far = FarArt.Load(art);
        _groundMat = seasons.Create(ground: true, tilePx: 8f, far: true);
        _topMat = seasons.Create(ground: false, perSprite: false, tilePx: 8f, far: true);
        AddChild(_fieldRoot); AddChild(_cmdRoot);
        _markers = new Markers(art) { ZIndex = 20 };
        AddChild(_markers);
    }

    /// <summary>True while a Field or Command view is waiting on baked chunks (the screenshot flags wait for this to clear).</summary>
    public bool Settled { get; private set; }

    /// <summary>Lays out the layer for one frame. <paramref name="centre"/> is the camera centre in world metres, <paramref name="viewPx"/> the viewport size, <paramref name="command"/> picks the band.</summary>
    public void Refresh(Vector2 centre, Vector2 viewPx, bool command, IReadOnlyList<FarPerson> people)
    {
        _tick++;
        _fieldRoot.Visible = !command; _cmdRoot.Visible = command;
        float ppm = command ? 2f : 8f;
        _groundMat.SetShaderParameter("tile_px", ppm); _topMat.SetShaderParameter("tile_px", ppm);
        float halfX = viewPx.X / 2f / ppm + 34f, halfY = viewPx.Y / 2f / ppm + 34f;
        int limit = ChunkLimit;
        int x0 = Math.Max(0, ChunkStreamer.FloorDiv((int)(centre.X - halfX), 32)), x1 = Math.Min(limit - 1, ChunkStreamer.FloorDiv((int)(centre.X + halfX), 32));
        int y0 = Math.Max(0, ChunkStreamer.FloorDiv((int)(centre.Y - halfY), 32)), y1 = Math.Min(limit - 1, ChunkStreamer.FloorDiv((int)(centre.Y + halfY), 32));
        var dict = command ? _cmd : _field; var parent = command ? _cmdRoot : _fieldRoot;
        int px = command ? 64 : 256;

        var want = new List<(float D, int X, int Y)>();
        foreach (var (k, e) in dict) if (e.G != null) { e.G.Visible = k.X >= x0 && k.X <= x1 && k.Y >= y0 && k.Y <= y1; if (e.T != null) e.T.Visible = e.G.Visible; }
        bool missing = false;
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                if (!dict.TryGetValue((cx, cy), out var e)) dict[(cx, cy)] = e = new Entry();
                e.Used = _tick;
                int v = VersionOf(cx, cy);
                bool fresh = e.Version == v || (e.Version == -1 && v == 0);
                if (!fresh) { missing = true; if (!e.Busy) want.Add((Mathf.Pow(cx * 32 + 16 - centre.X, 2) + Mathf.Pow(cy * 32 + 16 - centre.Y, 2), cx, cy)); }
                else if (e.G != null) { e.G.Visible = true; if (e.T != null) e.T.Visible = true; }
            }
        want.Sort((a, b) => a.D.CompareTo(b.D));
        int started = 0;
        foreach (var (_, cx, cy) in want)
        {
            if (_busy >= 8 || started >= 6) break;
            Start(cx, cy, command, dict[(cx, cy)]);
            started++;
        }
        int took = 0;
        while (took < 10 && _done.TryDequeue(out var r))
        {
            took++; _busy--;
            var d = r.Command ? _cmd : _field;
            if (!d.TryGetValue(r.Key, out var e)) continue;
            e.Busy = false; e.Version = r.Version;
            var par = r.Command ? _cmdRoot : _fieldRoot; int size = r.Command ? 64 : 256;
            Place(par, ref e.G, r.Ground, r.Key.X * size, r.Key.Y * size, _groundMat, 0);
            if (r.Top != null) Place(par, ref e.T, r.Top, r.Key.X * size - FarBake.Pad, r.Key.Y * size - FarBake.Pad, _topMat, 1);
            bool vis = r.Key.X >= x0 && r.Key.X <= x1 && r.Key.Y >= y0 && r.Key.Y <= y1;
            e.G!.Visible = vis; if (e.T != null) e.T.Visible = vis;
        }
        Settled = !missing && _busy == 0 && _done.IsEmpty;
        Evict(_field, MaxFieldChunks); Evict(_cmd, MaxCommandChunks);
        _markers.Set(command, people);
    }

    static void Place(Node2D parent, ref Sprite2D? s, Rgba img, int x, int y, Material mat, int z)
    {
        var tex = ImageTexture.CreateFromImage(Image.CreateFromData(img.W, img.H, false, Image.Format.Rgba8, img.Data));
        if (s == null)
        {
            s = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest, Material = mat, ZIndex = z, Position = new Vector2(x, y) };
            parent.AddChild(s);
        }
        s.Texture = tex;
    }

    int VersionOf(int cx, int cy)
    {
        if (_streamer?.TryGet(cx, cy) is { } lc) return lc.Version;
        return _streamer == null ? 0 : -1;
    }

    void Start(int cx, int cy, bool command, Entry e)
    {
        e.Busy = true; _busy++;
        var art = _far;
        int version = VersionOf(cx, cy);
        FarInput? input = null;
        if (_hamlet != null) input = FarInput.FromMap(_hamlet, cx, cy);
        else if (_streamer!.TryGet(cx, cy) is { } lc) input = FarInput.FromChunk(lc, _streamer.Generator);
        else if (_streamer.Store.Contains(new ChunkKey(cx, cy, 0)))
        {
            // visited earlier and unloaded: rebuild from its snapshot plus deltas so a changed tile shows
            var tmp = new LoadedChunk(cx, cy);
            if (_streamer.Store.Rebuild(new ChunkKey(cx, cy, 0)) is { } content) LoadedChunk.Decode(tmp, 0, content);
            input = FarInput.FromChunk(tmp, _streamer.Generator);
        }
        var gen = _streamer?.Generator;
        var done = _done;
        Task.Run(() =>
        {
            try
            {
                var inp = input;
                if (inp == null)
                {
                    var tmp = new LoadedChunk(cx, cy);
                    LoadedChunk.Decode(tmp, 0, gen!.Generate(cx, cy));
                    inp = FarInput.FromChunk(tmp, gen);
                }
                if (command) done.Enqueue(new Result((cx, cy), true, version, FarBake.BakeCommand(inp, art), null));
                else { var (g, t) = FarBake.BakeField(inp, art); done.Enqueue(new Result((cx, cy), false, version, g, t)); }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"far bake of chunk ({cx},{cy}) failed: {ex.Message}");
                done.Enqueue(new Result((cx, cy), command, version, new Rgba(command ? 64 : 256, command ? 64 : 256), null));
            }
        });
    }

    void Evict(Dictionary<(int X, int Y), Entry> d, int cap)
    {
        if (d.Count <= cap) return;
        var old = new List<((int X, int Y) K, long U)>();
        foreach (var (k, e) in d) if (!e.Busy && e.Used < _tick) old.Add((k, e.Used));
        old.Sort((a, b) => a.U.CompareTo(b.U));
        for (int i = 0; i < old.Count && d.Count > cap * 3 / 4; i++)
        {
            var e = d[old[i].K];
            e.G?.QueueFree(); e.T?.QueueFree();
            d.Remove(old[i].K);
        }
    }

    /// <summary>Drops every cached image (a settings or season change that needs a rebake would call this; none do today, the shader recolours).</summary>
    public void Clear()
    {
        foreach (var d in new[] { _field, _cmd }) { foreach (var e in d.Values) { e.G?.QueueFree(); e.T?.QueueFree(); } d.Clear(); }
    }

    // ------------------------------------------------------------------ people and the hero

    sealed partial class Markers : Node2D
    {
        readonly ArtAtlas _art;
        bool _command;
        IReadOnlyList<FarPerson> _people = Array.Empty<FarPerson>();
        static readonly Dictionary<string, Color> Team = new()
        {
            ["neutral"] = new("8e6842"), ["red"] = new("b33a2c"), ["blue"] = new("3d6b9a"), ["green"] = new("56873a"), ["gold"] = new("e0b450"), ["iron"] = new("75747c"),
        };
        public Markers(ArtAtlas art) { _art = art; }
        public void Set(bool command, IReadOnlyList<FarPerson> people) { _command = command; _people = people; QueueRedraw(); }

        public override void _Draw()
        {
            float ppm = _command ? 2f : 8f;
            var gold = new Color("fde27a"); var dark = new Color("1a1418");
            foreach (var p in _people)
            {
                int x = Mathf.RoundToInt(p.X * ppm), y = Mathf.RoundToInt(p.Y * ppm);
                var team = Team.GetValueOrDefault(p.Team, Team["neutral"]);
                if (_command)
                {
                    // a banner block: pole, a flag in the team colour with an outline and a shape marker pixel
                    int w = p.Hero ? 7 : 5, h = p.Hero ? 6 : 4;
                    DrawRect(new Rect2(x - 1, y - h - 3, 1, h + 4), dark);
                    DrawRect(new Rect2(x, y - h - 3, w, h), dark);
                    DrawRect(new Rect2(x + 1, y - h - 2, w - 2, h - 2), team);
                    DrawRect(new Rect2(x + w / 2, y - h - 2 + (h - 2) / 2, 1, 1), p.Hero ? gold : dark);
                    if (p.Hero) Ring(x, y, 7, 4, gold);
                }
                else
                {
                    string id = $"mini.person.{(p.Hero ? "hero" : "villager")}.{(Team.ContainsKey(p.Team) ? p.Team : "neutral")}";
                    if (_art.Has(id)) DrawTexture(_art.Texture(id), new Vector2(x - 4, y - 16));
                    if (p.Hero)
                    {
                        Ring(x, y, 9, 5, gold);
                        // an arrow above the head
                        for (int r = 0; r < 4; r++) DrawRect(new Rect2(x - 3 + r, y - 24 + r, 7 - 2 * r, 1), r == 0 ? dark : gold);
                        DrawRect(new Rect2(x - 1, y - 20, 3, 1), dark);
                    }
                }
            }
        }

        void Ring(int cx, int cy, int rx, int ry, Color c)
        {
            for (int a = 0; a < 48; a++)
            {
                double t = a / 48.0 * Math.PI * 2;
                DrawRect(new Rect2(cx + (int)Math.Round(Math.Cos(t) * rx), cy + (int)Math.Round(Math.Sin(t) * ry), 1, 1), c);
            }
        }
    }
}
