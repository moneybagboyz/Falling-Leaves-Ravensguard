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

    public WorldWindow(ChunkStreamer streamer, int centerChunkX, int centerChunkY)
    {
        _streamer = streamer; CenterChunkX = centerChunkX; CenterChunkY = centerChunkY;
        var gen = streamer.Generator;
        _pieces = gen.Library.Pieces; _palette = gen.Palette;
        Grid = new LevelGrid(Cells, Cells, Math.Min(gen.MinZ, -1), Math.Max(gen.MaxZ, 1));
        Map = new HamletMap(Grid, _pieces) { OriginX = OriginX, OriginY = OriginY, ChunkVersions = _versions };
        Lights = new LightGrid(Grid);
        Sync();
    }

    public bool Contains(int chunkX, int chunkY) => (uint)(chunkX - ChunkX0) < Chunks && (uint)(chunkY - ChunkY0) < Chunks;

    /// <summary>Stamps in chunks loaded since the last call and re-stamps chunks whose content changed. True when anything moved.</summary>
    public bool Sync()
    {
        bool changed = false, lightsChanged = false;
        for (int cy = ChunkY0; cy < ChunkY0 + Chunks; cy++)
            for (int cx = ChunkX0; cx < ChunkX0 + Chunks; cx++)
            {
                var c = _streamer.TryGet(cx, cy);
                if (c == null) continue;
                bool had = _versions.TryGetValue((cx, cy), out int v);
                if (had && v == c.Version) continue;
                Apply(c, first: !had);
                _versions[(cx, cy)] = c.Version;
                changed = true; lightsChanged = true;
            }
        if (lightsChanged)
        {
            var all = new List<LightSource>();
            foreach (var l in _lights.Values) all.AddRange(l);
            Lights.SetStaticLights(all);
            LightRevision++;
        }
        if (changed) Map.StreamRevision++;
        _seenRevision = _streamer.Revision;
        return changed;
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
