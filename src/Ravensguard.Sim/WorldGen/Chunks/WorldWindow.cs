using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.WorldGen.Chunks;

/// <summary>
/// The live sim's view of the streamed world: a fixed 7 x 7 chunk block (224 x 224 m) centred on a chunk, as a <see cref="LevelGrid"/>,
/// a <see cref="HamletMap"/> and a <see cref="LightGrid"/> in window-local cells. Loaded chunks are stamped in as they arrive; cells of
/// chunks not yet loaded stay closed. The window is rebuilt around the hero when the hero's chunk changes (see LiveWorld).
/// </summary>
public sealed class WorldWindow
{
    public const int Chunks = 7;
    public const int Cells = Chunks * ChunkContent.Size;

    private readonly ChunkStreamer _streamer;
    private readonly PieceCatalog _pieces;
    private readonly PiecePalette _palette;
    private readonly Dictionary<(int X, int Y), int> _versions = [];
    private readonly Dictionary<(int X, int Y), List<LightSource>> _lights = [];
    private readonly HashSet<(int X, int Y)> _portalsDone = [];
    private int _seenRevision = -1;
    /// <summary>Where chunks come from: the streamer, or (while the window is built on a worker) a fixed set of chunks copied on the main thread.</summary>
    private Func<int, int, LoadedChunk?> _source;

    public int CenterChunkX { get; }
    public int CenterChunkY { get; }
    /// <summary>World chunk of local chunk (0,0).</summary>
    public int ChunkX0 => CenterChunkX - Chunks / 2;
    public int ChunkY0 => CenterChunkY - Chunks / 2;
    public int OriginX => ChunkX0 * ChunkContent.Size;
    public int OriginY => ChunkY0 * ChunkContent.Size;
    public LevelGrid Grid { get; }
    public HamletMap Map { get; }
    public LightGrid Lights { get; }
    /// <summary>Bumps when static lights change (a chunk arrived), so the live sim recomputes the light frame.</summary>
    public int LightRevision { get; private set; }

    public WorldWindow(ChunkStreamer streamer, int centerChunkX, int centerChunkY) : this(streamer, centerChunkX, centerChunkY, null) { }

    /// <summary>
    /// Builds the window from <paramref name="chunks"/> (the loaded chunks of the block, copied on the main thread) so the work can run on another thread;
    /// the streamer itself is not touched until the first <see cref="Sync"/> after the hand-over. Null reads the streamer directly.
    /// </summary>
    public WorldWindow(ChunkStreamer streamer, int centerChunkX, int centerChunkY, IReadOnlyDictionary<(int X, int Y), LoadedChunk>? chunks)
    {
        _streamer = streamer; CenterChunkX = centerChunkX; CenterChunkY = centerChunkY;
        _source = chunks == null ? streamer.TryGet : (x, y) => chunks.GetValueOrDefault((x, y));
        var gen = streamer.Generator;
        _pieces = gen.Library.Pieces; _palette = gen.Palette;
        Grid = new LevelGrid(Cells, Cells, Math.Min(gen.MinZ, -1), Math.Max(gen.MaxZ, 1));
        Map = new HamletMap(Grid, _pieces) { OriginX = OriginX, OriginY = OriginY, ChunkVersions = _versions };
        Lights = new LightGrid(Grid);
        Sync();
    }

    /// <summary>The loaded chunks of the block around a centre, for building a window on another thread.</summary>
    public static Dictionary<(int X, int Y), LoadedChunk> CopyLoaded(ChunkStreamer streamer, int centerChunkX, int centerChunkY)
    {
        var d = new Dictionary<(int X, int Y), LoadedChunk>();
        for (int cy = centerChunkY - Chunks / 2; cy <= centerChunkY + Chunks / 2; cy++)
            for (int cx = centerChunkX - Chunks / 2; cx <= centerChunkX + Chunks / 2; cx++)
                if (streamer.TryGet(cx, cy) is { } c) d[(cx, cy)] = c;
        return d;
    }

    /// <summary>Main-thread hand-over of a window built on a worker: from now on chunks come from the streamer.</summary>
    public void Attach() => _source = _streamer.TryGet;

    public bool Contains(int chunkX, int chunkY) => (uint)(chunkX - ChunkX0) < Chunks && (uint)(chunkY - ChunkY0) < Chunks;

    /// <summary>
    /// Stamps in chunks loaded since the last call and re-stamps chunks whose content changed, at most <paramref name="maxChunks"/> of them (nearest the
    /// centre first), so a big catch-up is spread over several ticks. True when anything moved; <see cref="Pending"/> says whether more is waiting.
    /// </summary>
    public bool Sync(int maxChunks = int.MaxValue)
    {
        bool changed = false;
        int done = 0;
        Pending = false;
        foreach (var (cx, cy) in _order)
        {
            var c = _source(cx, cy);
            if (c == null) continue;
            int version = c.Version; // read before stamping: a change made meanwhile leaves the stored version behind, so the chunk is stamped again
            bool had = _versions.TryGetValue((cx, cy), out int v);
            if (had && v == version) continue;
            if (done >= maxChunks) { Pending = true; continue; }
            Apply(c, first: !had);
            _versions[(cx, cy)] = version;
            changed = true; done++;
        }
        _lightsDirty |= changed;
        if (changed) Map.StreamRevision++;
        if (_lightsDirty && !Pending)
        {
            // the static lights are recast once the catch-up is complete (walls of late chunks block light from early ones, so a partial recast would be redone anyway)
            var all = new List<LightSource>();
            foreach (var l in _lights.Values) all.AddRange(l);
            Lights.SetStaticLights(all);
            LightRevision++;
            _lightsDirty = false;
            changed = true;
        }
        _seenRevision = _streamer.Revision;
        return changed;
    }

    /// <summary>True when the last <see cref="Sync"/> stopped at its chunk limit with chunks still to stamp.</summary>
    public bool Pending { get; private set; }
    private bool _lightsDirty;

    private (int X, int Y)[] _order => _orderArr ??= BuildOrder();
    private (int X, int Y)[]? _orderArr;
    private (int X, int Y)[] BuildOrder()
    {
        var o = new List<(int X, int Y)>();
        for (int cy = ChunkY0; cy < ChunkY0 + Chunks; cy++) for (int cx = ChunkX0; cx < ChunkX0 + Chunks; cx++) o.Add((cx, cy));
        o.Sort((a, b) =>
        {
            int da = (a.X - CenterChunkX) * (a.X - CenterChunkX) + (a.Y - CenterChunkY) * (a.Y - CenterChunkY);
            int db = (b.X - CenterChunkX) * (b.X - CenterChunkX) + (b.Y - CenterChunkY) * (b.Y - CenterChunkY);
            return da != db ? da.CompareTo(db) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
        });
        return [.. o];
    }

    private void Apply(LoadedChunk c, bool first)
    {
        int bx = (c.X - ChunkX0) * ChunkContent.Size, by = (c.Y - ChunkY0) * ChunkContent.Size;
        var lights = new List<LightSource>();
        if (!first)
        {
            // a changed chunk: forget its props, open cells are rewritten below
            Map.Props.RemoveAll(p => p.X >= bx && p.X < bx + ChunkContent.Size && p.Y >= by && p.Y < by + ChunkContent.Size);
        }
        for (int z = Grid.MinZ; z <= Grid.MaxZ; z++)
        {
            c.Levels.TryGetValue(z, out var level);
            for (int ly = 0; ly < ChunkContent.Size; ly++)
                for (int lx = 0; lx < ChunkContent.Size; lx++)
                {
                    int code = level == null ? 0 : level.Tiles[ly * ChunkContent.Size + lx];
                    if (code == 0 && first) continue;
                    int x = bx + lx, y = by + ly, wi = Grid.Index(x, y, z);
                    var sId = _palette.Id(TileCode.Structure(code)); var fId = _palette.Id(TileCode.Fixture(code));
                    Map.Structure[wi] = sId; Map.Fixture[wi] = fId;
                    var g = TileCode.Ground(code);
                    if (z == 0) { Map.Ground[wi] = g; Map.GroundVariant[wi] = (byte)TileCode.Variant(code); }
                    var kind = _pieces.KindOf(sId);
                    var fk = _pieces.KindOf(fId);
                    bool solidFixture = fk is { } f && PieceCatalog.IsSolidFixture(f);
                    bool open = kind == null ? z == 0 && TileCode.Walkable(g)
                        : (kind == PieceKind.Ground ? TileCode.Walkable(g) : PieceCatalog.IsWalkable(kind.Value)) && !solidFixture;
                    if (open != Grid.IsOpen(x, y, z)) Grid.SetOpen(x, y, z, open);
                    Lights.SetOpaque(x, y, z, kind == PieceKind.Wall);
                    Lights.SetIndoor(x, y, z, kind is { } kk && PieceCatalog.IsRoomKind(kk));
                    if (fId == "hearth") lights.Add(new LightSource(x, y, z, 6, 215));
                    else if (fId == "forge") lights.Add(new LightSource(x, y, z, 7, 255));
                }
            if (level == null) continue;
            foreach (var p in level.Props.Values)
            {
                var info = p.Info;
                int x = bx + p.X, y = by + p.Y;
                Map.Props.Add(new Prop(x, y, z, info.Sprite, info.Wide, info.Blocks, info.LightRadius, info.LightIntensity));
                if (info.Blocks)
                {
                    if (Grid.IsOpen(x, y, z)) Grid.SetOpen(x, y, z, false);
                    if (info.Wide && Grid.IsOpen(x + 1, y, z)) Grid.SetOpen(x + 1, y, z, false);
                }
                if (info.LightRadius > 0) lights.Add(new LightSource(x, y, z, info.LightRadius, info.LightIntensity));
            }
        }
        _lights[(c.X, c.Y)] = lights;
        if (_portalsDone.Add((c.X, c.Y))) AddPortals(c, bx, by);
        else Ravensguard.Sim.Construction.ConstructionPortals.Sync(Grid, (x, y, z) => _pieces.KindOf(Map.StructureAt(x, y, z)), bx, by, ChunkContent.Size, ChunkContent.Size);   // P5-B: a re-stamped chunk (a piece built or fallen) keeps its stairs and ladders in step
    }

    /// <summary>Stairs and ladders become portals to the cell above when the piece above is their other end (same rule as the blueprint placer).</summary>
    private void AddPortals(LoadedChunk c, int bx, int by)
    {
        foreach (var (z, level) in c.Levels)
        {
            if (z + 1 > Grid.MaxZ || z < Grid.MinZ || !c.Levels.TryGetValue(z + 1, out var up)) continue;
            for (int i = 0; i < level.Tiles.Length; i++)
            {
                int s = TileCode.Structure(level.Tiles[i]);
                if (s == 0) continue;
                var kind = _pieces.KindOf(_palette.Id(s));
                if (kind is not (PieceKind.StairsUp or PieceKind.Ladder)) continue;
                var above = _pieces.KindOf(_palette.Id(TileCode.Structure(up.Tiles[i])));
                var here = new Cell3(bx + i % ChunkContent.Size, by + i / ChunkContent.Size, z);
                var to = new Cell3(here.X, here.Y, z + 1);
                if (kind == PieceKind.StairsUp && above == PieceKind.StairsDown) Grid.AddPortal(PortalKind.Stairs, here, to, BlueprintPlacer.StairCost);
                else if (kind == PieceKind.Ladder && above == PieceKind.Trapdoor) Grid.AddPortal(PortalKind.Ladder, here, to, BlueprintPlacer.LadderCost);
            }
        }
    }
}
