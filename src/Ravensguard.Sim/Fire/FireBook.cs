namespace Ravensguard.Sim.Burning;

/// <summary>
/// The saved state of fire (the optional "fire" section, absent while nothing burns and nothing is scarred, so a world that never had a fire keeps its bytes): the burning cells, the scars they
/// left, the incidents still open, the abstract building fires, the people breathing smoke, soaked roofs, the pieces' upkeep marks and the cells whose firedamp has gone. Every collection is
/// ordered, so a save, a load and a save are the same bytes. <see cref="Hash"/> is the <c>FireHash</c> of the trail (fire cells and building fires, fire 5).
/// </summary>
public sealed class FireBook
{
    /// <summary>The last sim-second stepped (game second / 20); -1 before the first.</summary>
    public long SecDone = -1;
    /// <summary>The game hour the abstract step last ran.</summary>
    public long HourDone = -1;
    /// <summary>The game second it last rained on the fire's roofs (the wet rule, 4.2); -1 never.</summary>
    public long LastRainSec = -1;
    /// <summary>Serial for ids made when no chronicle is attached.</summary>
    public ulong NextSerial = 1;
    public SortedDictionary<long, FireCell> Cells { get; } = [];
    public SortedDictionary<long, ScarKind> Scars { get; } = [];
    public SortedDictionary<ulong, FireIncident> Incidents { get; } = [];
    public SortedDictionary<int, BuildingFire> Buildings { get; } = [];
    public SortedDictionary<ulong, Exposure> Exposed { get; } = [];
    /// <summary>Cells wetted by buckets (cell key to the game second it dries).</summary>
    public SortedDictionary<long, long> Soaked { get; } = [];
    /// <summary>Upkeep: the game second a piece was last made or mended (cell key to second); a piece with no mark is kept as new.</summary>
    public SortedDictionary<long, long> Made { get; } = [];
    /// <summary>Firedamp cells already blasted (the gas is gone).</summary>
    public SortedSet<long> Vented { get; } = [];
    /// <summary>Structures lost to abstract fires, for the faith hook and the stone-town test (structure id).</summary>
    public List<FireStop> Stops { get; } = [];

    public bool IsEmpty => Cells.Count == 0 && Scars.Count == 0 && Incidents.Count == 0 && Buildings.Count == 0 && Exposed.Count == 0 && Soaked.Count == 0 && Made.Count == 0 && Vented.Count == 0
        && Stops.Count == 0 && SecDone < 0 && HourDone < 0;

    public bool Burning => Cells.Count > 0;

    /// <summary>FNV-1a over the fire cells and building fires (fire 5), plus the scars and incidents so a fire that went another way never shares a hash.</summary>
    public ulong Hash()
    {
        ulong h = 1469598103934665603UL;
        void U(ulong v) { for (int i = 0; i < 8; i++) { h = (h ^ (v & 0xFF)) * 1099511628211UL; v >>= 8; } }
        void L(long v) => U((ulong)v);
        L(SecDone); L(HourDone); L(LastRainSec);
        L(Cells.Count);
        foreach (var c in Cells.Values) { L(c.Key); U((ulong)c.State); U(c.Intensity); L(c.StartS); L(c.FuelLeftS); U(c.Fire); U(c.Class); U(c.Knock); U(c.Buckets); }
        L(Scars.Count);
        foreach (var (k, s) in Scars) { L(k); U((ulong)s); }
        L(Buildings.Count);
        foreach (var b in Buildings.Values) { L(b.Structure); L(b.StartHour); L(b.BurnedHours); L(b.DamagePm); L(b.Flammable); L(b.Taken); U(b.Fire); }
        L(Incidents.Count);
        foreach (var i in Incidents.Values) { U(i.Entry); L(i.StartSec); L(i.CellsBurnt); L(i.GoodsMites); L(i.Deaths.Count); L(i.Structures.Count); }
        L(Exposed.Count);
        foreach (var e in Exposed.Values) { U(e.Person); U(e.Fire); L(e.SmokeS); L(e.StopSec); }
        L(Soaked.Count);
        foreach (var (k, v) in Soaked) { L(k); L(v); }
        L(Vented.Count);
        foreach (var k in Vented) L(k);
        return h;
    }
}
