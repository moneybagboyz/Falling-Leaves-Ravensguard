using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Behaviour;

/// <summary>A cell of a dwelling a person lies in: region metres and the level (z 0 ground, 1 the floor above).</summary>
public readonly record struct BedCell(int X, int Y, int Z);

/// <summary>
/// What the live people need to know about the built world, read from the generator's placed buildings (the same ones the street view draws): where each household's beds are, and where the
/// market stalls stand. Immutable once a lot's answer is made; safe to call from the tick worker (<see cref="ChunkGenerator.StampOf"/> is itself thread-safe).
/// </summary>
public sealed class LiveFacts(ChunkGenerator gen)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, BedCell[]> _beds = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, BedCell[]> _floor = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ChunkGenerator.StallSpot[]> _stalls = new();
    public ChunkGenerator Generator { get; } = gen;

    private static long Key(int s, int lot) => ((long)s << 32) | (uint)lot;

    /// <summary>The bed cells of a dwelling in ascending (z, y, x) order, a bunk counted twice (lower first). Empty for a lot without a placed building or without beds.</summary>
    public BedCell[] BedsOf(int settlement, int lot) => _beds.GetOrAdd(Key(settlement, lot), _ => Make(settlement, lot, beds: true));

    /// <summary>The free floor cells of the level the first bed is on, ascending: where those without a bed lie.</summary>
    private BedCell[] FloorOf(int settlement, int lot) => _floor.GetOrAdd(Key(settlement, lot), _ => Make(settlement, lot, beds: false));

    private BedCell[] Make(int s, int lot, bool beds)
    {
        var st = Generator.StampOf(s, lot);
        if (st == null) return [];
        if (beds) return BedsOfStamp(st, Generator.Library.Pieces, Generator.Palette);
        var b = BedsOf(s, lot); if (b.Length == 0) return [];
        var res = new List<BedCell>(); var pal = Generator.Palette;
        if (!st.Levels.TryGetValue(b[0].Z, out var lv)) return [];
        for (int y = 0; y < st.H; y++)
            for (int x = 0; x < st.W; x++)
            {
                int i = y * st.W + x;
                if (pal.Id(lv.Fixture[i]) == null && Generator.Library.Pieces.KindOf(pal.Id(lv.Structure[i])) == PieceKind.Floor) res.Add(new BedCell(st.X0 + x, st.Y0 + y, b[0].Z));
            }
        return [.. res];
    }

    /// <summary>
    /// The beds of a placed building, ascending (z, y, x): every fixture tagged <c>bed</c>, a piece that gives two beds (a bunk) listed twice, lower first. Public so a test can feed it a building it builds by hand.
    /// </summary>
    public static BedCell[] BedsOfStamp(ChunkGenerator.Stamp st, PieceCatalog pieces, PiecePalette pal)
    {
        var res = new List<BedCell>();
        foreach (var (z, lv) in st.Levels)
            for (int y = 0; y < st.H; y++)
                for (int x = 0; x < st.W; x++)
                {
                    string? fx = pal.Id(lv.Fixture[y * st.W + x]);
                    if (fx == null || !pieces.TryGet(fx, out var def) || !def.Tags.Contains("bed")) continue;
                    for (int k = 0; k < Math.Max(1, def.Beds); k++) res.Add(new BedCell(st.X0 + x, st.Y0 + y, z));
                }
        return [.. res];
    }

    /// <summary>
    /// The cell person number <paramref name="bed"/> of the household lies in: its own bed, or (when the dwelling has fewer beds than members) a floor cell of the sleeping level picked from the
    /// person's id, so it is the same on every run. <c>false</c> when the lot has no building to sleep in.
    /// </summary>
    public bool TryBed(int settlement, int lot, int bed, ulong personKey, ulong seed, out BedCell cell)
    {
        cell = default;
        if (lot < 0 || bed < 0) return false;
        var b = BedsOf(settlement, lot);
        if (b.Length == 0) return false;
        if (bed < b.Length) { cell = b[bed]; return true; }
        var f = FloorOf(settlement, lot);
        if (f.Length == 0) { cell = b[bed % b.Length]; return true; }
        cell = f[DetRng.Range(seed, 0, personKey, RngStreams.BedFallback, 0, 0, f.Length)];
        return true;
    }

    /// <summary>
    /// The way a sleeper lies on <paramref name="bed"/>: beds standing side by side along x are lain along y (north to south), so each person fits their own cell and not their neighbour's;
    /// beds in a column are lain along x; a lone bed, or one with neighbours on both axes, is lain along x. 0 is along x, 1 along y.
    /// </summary>
    public byte BedAxis(int settlement, int lot, BedCell bed)
    {
        var all = BedsOf(settlement, lot);
        bool Has(int dx, int dy) { foreach (var c in all) if (c.Z == bed.Z && c.X == bed.X + dx && c.Y == bed.Y + dy) return true; return false; }
        bool row = Has(-1, 0) || Has(1, 0), col = Has(0, -1) || Has(0, 1);
        return (byte)(row && !col ? 1 : 0);
    }

    public ChunkGenerator.StallSpot[] StallsOf(int settlement) => _stalls.GetOrAdd(settlement, s => [.. Generator.StallsOf(s)]);
}
