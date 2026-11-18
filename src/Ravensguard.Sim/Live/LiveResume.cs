using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Live;

/// <summary>
/// The region tiles (64 m, 500 x 500) the hero has been near: the map's fog of war. <see cref="LiveWorld"/> marks tiles from its tick; the set is saved
/// in the "live" section and restored by <see cref="LiveResume"/>.
/// </summary>
public sealed class ExploredTiles
{
    public const int Size = RegionData.Size, TileM = RegionData.TileM, Tiles = Size * Size;
    /// <summary>How far around the hero's walked position tiles count as seen: 256 m.</summary>
    public const int RadiusM = 256;
    private const int RadiusTiles = RadiusM / TileM;

    private readonly bool[] _bits = new bool[Tiles];
    public int Count { get; private set; }
    /// <summary>Bumps whenever a tile is added, so a view knows to redraw its fog.</summary>
    public int Revision { get; private set; }

    public bool IsExplored(int tx, int ty) => (uint)tx < Size && (uint)ty < Size && _bits[ty * Size + tx];
    public bool IsExploredAt(int worldX, int worldY) => IsExplored(FloorDiv(worldX, TileM), FloorDiv(worldY, TileM));

    /// <summary>Marks the tiles within <see cref="RadiusM"/> of a region position (metres). True when any tile was new.</summary>
    public bool Mark(int worldX, int worldY)
    {
        int tx = FloorDiv(worldX, TileM), ty = FloorDiv(worldY, TileM);
        bool added = false;
        for (int dy = -RadiusTiles; dy <= RadiusTiles; dy++)
            for (int dx = -RadiusTiles; dx <= RadiusTiles; dx++)
            {
                int x = tx + dx, y = ty + dy;
                if ((uint)x >= Size || (uint)y >= Size || dx * dx + dy * dy > RadiusTiles * RadiusTiles + 1) continue;
                int i = y * Size + x;
                if (_bits[i]) continue;
                _bits[i] = true; Count++; added = true;
            }
        if (added) Revision++;
        return added;
    }

    private static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }

    public ExploredTiles Clone() { var c = new ExploredTiles(); Array.Copy(_bits, c._bits, Tiles); c.Count = Count; return c; }

    /// <summary>Packed bits (row-major, least significant bit first): 31 250 bytes.</summary>
    public byte[] ToBytes()
    {
        var b = new byte[(Tiles + 7) / 8];
        for (int i = 0; i < Tiles; i++) if (_bits[i]) b[i >> 3] |= (byte)(1 << (i & 7));
        return b;
    }

    public static ExploredTiles FromBytes(ReadOnlySpan<byte> b)
    {
        var e = new ExploredTiles();
        for (int i = 0; i < Tiles && (i >> 3) < b.Length; i++) if ((b[i >> 3] & (1 << (i & 7))) != 0) { e._bits[i] = true; e.Count++; }
        return e;
    }

    public bool SameAs(ExploredTiles o) => _bits.AsSpan().SequenceEqual(o._bits);
}

/// <summary>
/// The live play state that a save keeps (plan 2.2, section 3): the hero's position in region cells (Q16.16, so a save resumes exactly), facing and torch,
/// the live clock, the unlocked Codex entries and the explored map tiles. <see cref="LiveWorld.CaptureResume"/> makes one; <c>WorldSave.Restore</c> returns one
/// and <see cref="LiveWorld"/> resumes from it. <see cref="RegionId"/> is the grid region the hero stands in (-1 unknown), set by the save code. <see cref="Codex"/> is null when the sim had no Codex attached (the save then keeps the world's own progress).
/// </summary>
/// <para><see cref="Motion"/> is the walk in progress (Phase 3 cleanup: a path at an autosave was lost, so a replay window starting there diverged) and <see cref="Body"/> the hero's core
/// temperature, exposure counters and the wetness of what they wear; both null in a save made before they were kept.</para>
public sealed record LiveResume(long WorldXRaw, long WorldYRaw, int Z, Facing Facing, bool Torch, GameTime Time, long MsRemainder, SimSpeed Speed,
    ExploredTiles Explored, CodexProgress? Codex, int RegionId = -1, HeroMotion? Motion = null, Ravensguard.Sim.Clothing.HeroBody? Body = null)
{
    public Fixed WorldX => Fixed.FromRaw((int)WorldXRaw);
    public Fixed WorldY => Fixed.FromRaw((int)WorldYRaw);
    /// <summary>The cell the hero stands in, in region cells.</summary>
    public Cell3 Cell => new((int)(WorldXRaw >> Fixed.FracBits), (int)(WorldYRaw >> Fixed.FracBits), Z);
}

/// <summary>
/// The hero's walk in progress, as a save keeps it: the planned route in region cells (x, y, level triples) with the index of the next cell, the stuck counter, the portal the hero just came
/// through (empty when none) and the gait. With it a loaded game walks on exactly as the uninterrupted one does.
/// </summary>
public sealed record HeroMotion(int[] PathXyz, int PathIdx, int Stuck, int[] PortalLock, MoveMode Mode)
{
    public static readonly HeroMotion Idle = new([], 0, 0, [], MoveMode.Jog);
    public bool IsIdle => PathXyz.Length == 0 && PortalLock.Length == 0 && Stuck == 0 && Mode == MoveMode.Jog;
}
