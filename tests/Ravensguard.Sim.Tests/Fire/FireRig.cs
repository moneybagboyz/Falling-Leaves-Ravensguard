using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Building;

namespace Ravensguard.Sim.Tests.Fire;

/// <summary>A fire world over a room world with a chronicle of its own, the weather the test says and the people the test lists; the clock is a game-second cursor stepped in sim-seconds.</summary>
internal sealed class FireRig
{
    public required FireWorld Fw { get; init; }
    public required RoomWorld Rooms { get; init; }
    public Chronicle Chron { get; } = new();
    public IdAllocator Ids { get; } = new();
    public FireWeather Wx = FireWeather.Calm;
    public List<FirePerson> People { get; } = [];
    public List<FireFighter> Fighters { get; } = [];
    public long Sec;

    public static FireRig Make(BuildingFixture fx, ulong seed, RoomWorld? rooms = null, FireBook? book = null)
    {
        rooms ??= fx.Lib.NewWorld();
        var fw = new FireWorld(book ?? new FireBook(), rooms, FireFixture.Fuels(fx), seed);
        var rig = new FireRig { Fw = fw, Rooms = rooms };
        fw.Weather = _ => rig.Wx;
        fw.Chronicle = (when, type, key, ps, subjects, causes, data) =>
        {
            var id = rig.Ids.Allocate(IdKind.Event);
            rig.Chron.Add(new ChronicleEntry(id, when, type, subjects, causes, data.ToDictionary(d => d.Key, d => d.Value), key, ps));
            return id;
        };
        fw.PeopleNear = l => l.AddRange(rig.People);
        fw.FightersNear = l => l.AddRange(rig.Fighters);
        return rig;
    }

    /// <summary>Steps the fire <paramref name="simSeconds"/> sim-seconds on (20 game seconds each).</summary>
    public void Run(int simSeconds)
    {
        for (int i = 0; i < simSeconds; i++) { Sec += FireClock.GameSecPerSimS; Fw.AdvanceTo(Sec); }
    }

    /// <summary>Runs until nothing burns or <paramref name="maxSimS"/> pass; returns the sim-seconds it took.</summary>
    public int RunOut(int maxSimS)
    {
        int n = 0;
        while (Fw.Book.Cells.Count > 0 && n < maxSimS) { Sec += FireClock.GameSecPerSimS; Fw.AdvanceTo(Sec); n++; }
        return n;
    }

    // ---- scenery ----

    private static CellEdit S(int x, int y, int z, string? p) => new(x, y, z, CellLayer.Structure, p);
    private static CellEdit F(int x, int y, int z, string? p) => new(x, y, z, CellLayer.Fixture, p);

    /// <summary>A house of 8 x 6 m outside (6 x 4 inside), a door south, a hearth and a bed; walls and floor timber, or stone with a slate roof; roofed in thatch (or slate).</summary>
    public int House(int x0, int y0, bool stone = false, string? roof = null)
    {
        var e = new List<CellEdit>();
        string wall = stone ? "wall_stone" : "wall_timber", floor = stone ? "floor_flagstone" : "floor_plank";
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
            {
                bool ring = x == 0 || y == 0 || x == 7 || y == 5;
                e.Add(S(x0 + x, y0 + y, 0, ring ? (x == 3 && y == 5 ? "door_wood" : wall) : floor));
            }
        e.Add(F(x0 + 1, y0 + 1, 0, "hearth")); e.Add(F(x0 + 5, y0 + 1, 0, "bed"));
        Rooms.Apply(e);
        Rooms.RoofOver(roof ?? (stone ? "roof_slate" : "roof_thatch"));
        return Rooms.StructureAt(x0 + 2, y0 + 2, 0);
    }

    /// <summary>The cell the roof of a house stands on in its middle: gone once the roof burnt.</summary>
    public bool RoofStands(int x0, int y0) => Rooms.PieceAt(x0 + 3, y0 + 2, 1) != null;

    public int RoofCells(int x0, int y0) { int n = 0; for (int y = 1; y < 5; y++) for (int x = 1; x < 7; x++) if (Rooms.PieceAt(x0 + x, y0 + y, 1) != null) n++; return n; }

    /// <summary>A house stands as a house while any of its walls or roof does.</summary>
    public bool Burnt(int x0, int y0) => !RoofStands(x0, y0);

    public bool Light(int x0, int y0, FireCause? cause = null) => Fw.Ignite(x0 + 1, y0 + 1, 1, Sec, cause ?? FireCause.Test);

    /// <summary>A street of <paramref name="n"/> houses along x, 6 m apart (14 m a house), each in thatch; an optional stone house at the far end.</summary>
    public List<(int X, int Y)> Street(int n, bool stoneAtEnd = false)
    {
        var l = new List<(int X, int Y)>();
        for (int i = 0; i < n; i++) { House(i * 14, 0); l.Add((i * 14, 0)); }
        if (stoneAtEnd) { House(n * 14, 0, stone: true); l.Add((n * 14, 0)); }
        return l;
    }
}

internal static class FireFixture
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BuildingFixture, FuelTable> Cache = new();
    public static FuelTable Fuels(BuildingFixture fx) => Cache.GetValue(fx, f => new FuelTable(f.Load.Database));
    public static FuelTable Fuels(ContentDatabase db) => new(db);
}
