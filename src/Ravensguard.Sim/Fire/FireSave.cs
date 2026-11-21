using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Burning;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "fire" section (P6-B5, fire 3, overview 7; no format bump, absent while nothing has burnt, so a world that never had a fire keeps its bytes): the burning cells at 16 bytes of
/// payload each beyond their key, the scars, the open incidents, the abstract building fires, the people breathing smoke, soaked roofs, upkeep marks and vented firedamp, each in ascending
/// order. A save, a load and a save are the same bytes.
/// </summary>
[MessagePackObject]
public sealed class FireSave
{
    public const string SectionName = "fire";

    [Key(0)] public long SecDone { get; set; } = -1;
    [Key(1)] public long HourDone { get; set; } = -1;
    [Key(2)] public long LastRainSec { get; set; } = -1;
    [Key(3)] public ulong NextSerial { get; set; } = 1;
    /// <summary>Per cell: key, state, intensity, start, fuel left, fire, class, knock, buckets.</summary>
    [Key(4)] public long[][] Cells { get; set; } = [];
    /// <summary>Per scar: key, kind.</summary>
    [Key(5)] public long[][] Scars { get; set; } = [];
    [Key(6)] public IncidentRow[] Incidents { get; set; } = [];
    /// <summary>Per building fire: structure, start hour, burned hours, damage, flammable, taken, fire, spread.</summary>
    [Key(7)] public long[][] Buildings { get; set; } = [];
    [Key(8)] public string[] BuildingCauses { get; set; } = [];
    /// <summary>Per person: person, fire, smoke seconds, stop second, last seen.</summary>
    [Key(9)] public long[][] Exposed { get; set; } = [];
    /// <summary>Per soaked cell: key, dry second.</summary>
    [Key(10)] public long[][] Soaked { get; set; } = [];
    /// <summary>Per mark: key, second made.</summary>
    [Key(11)] public long[][] Made { get; set; } = [];
    [Key(12)] public long[] Vented { get; set; } = [];
    /// <summary>Per stop: second, fire, structure, sacred.</summary>
    [Key(13)] public long[][] Stops { get; set; } = [];
    [Key(14)] public string[] StopReasons { get; set; } = [];

    public static FireSave Capture(FireBook b) { b.Sync(); return CaptureCore(b); }

    private static FireSave CaptureCore(FireBook b) => new()
    {
        SecDone = b.SecDone, HourDone = b.HourDone, LastRainSec = b.LastRainSec, NextSerial = b.NextSerial,
        Cells = [.. b.Cells.Values.Select(c => new long[] { c.Key, (long)c.State, c.Intensity, c.StartS, c.FuelLeftS, (long)c.Fire, c.Class, c.Knock, c.Buckets })],
        Scars = [.. b.Scars.Select(kv => new long[] { kv.Key, (long)kv.Value })],
        Incidents = [.. b.Incidents.Values.Select(IncidentRow.Of)],
        Buildings = [.. b.Buildings.Values.Select(x => new long[] { x.Structure, x.StartHour, x.BurnedHours, x.DamagePm, x.Flammable, x.Taken, (long)x.Fire, x.Spread ? 1 : 0 })],
        BuildingCauses = [.. b.Buildings.Values.Select(x => x.CauseKind)],
        Exposed = [.. b.Exposed.Values.Select(e => new long[] { (long)e.Person, (long)e.Fire, e.SmokeS, e.StopSec, e.LastSeenSec })],
        Soaked = [.. b.Soaked.Select(kv => new long[] { kv.Key, kv.Value })],
        Made = [.. b.Made.Select(kv => new long[] { kv.Key, kv.Value })],
        Vented = [.. b.Vented],
        Stops = [.. b.Stops.Select(s => new long[] { s.Sec, (long)s.Fire, s.Structure, s.Sacred ? 1 : 0 })],
        StopReasons = [.. b.Stops.Select(s => s.Reason)],
    };

    public FireBook Restore()
    {
        if (Buildings.Length != BuildingCauses.Length || Stops.Length != StopReasons.Length) throw new SaveException("Corrupt 'fire' section in save.");
        var b = new FireBook { SecDone = SecDone, HourDone = HourDone, LastRainSec = LastRainSec, NextSerial = NextSerial };
        foreach (var r in Cells)
        {
            if (r.Length < 9) throw new SaveException("Corrupt fire cell in save.");
            b.Cells[r[0]] = new FireCell { Key = r[0], State = (CellFireState)r[1], Intensity = (byte)r[2], StartS = r[3], FuelLeftS = (int)r[4], Fire = (ulong)r[5], Class = (byte)r[6], Knock = (byte)r[7], Buckets = (byte)r[8] };
        }
        foreach (var r in Scars) { if (r.Length < 2) throw new SaveException("Corrupt fire scar in save."); b.Scars[r[0]] = (ScarKind)r[1]; }
        foreach (var i in Incidents) { var inc = i.Restore(); b.Incidents[inc.Entry] = inc; }
        for (int n = 0; n < Buildings.Length; n++)
        {
            var r = Buildings[n];
            if (r.Length < 8) throw new SaveException("Corrupt building fire in save.");
            b.Buildings[(int)r[0]] = new BuildingFire { Structure = (int)r[0], StartHour = r[1], BurnedHours = (int)r[2], DamagePm = (int)r[3], Flammable = (int)r[4], Taken = (int)r[5], Fire = (ulong)r[6], Spread = r[7] != 0, CauseKind = BuildingCauses[n] };
        }
        foreach (var r in Exposed) { if (r.Length < 5) throw new SaveException("Corrupt fire exposure in save."); b.Exposed[(ulong)r[0]] = new Exposure { Person = (ulong)r[0], Fire = (ulong)r[1], SmokeS = (int)r[2], StopSec = r[3], LastSeenSec = r[4] }; }
        foreach (var r in Soaked) b.Soaked[r[0]] = r[1];
        foreach (var r in Made) b.Made[r[0]] = r[1];
        foreach (var k in Vented) b.Vented.Add(k);
        for (int n = 0; n < Stops.Length; n++) { var r = Stops[n]; b.Stops.Add(new FireStop(r[0], (ulong)r[1], (int)r[2], StopReasons[n], r[3] != 0)); }
        return b;
    }
}

[MessagePackObject]
public sealed class IncidentRow
{
    [Key(0)] public ulong Entry { get; set; }
    [Key(1)] public long StartSec { get; set; }
    [Key(2)] public int CellsBurnt { get; set; }
    [Key(3)] public long GoodsMites { get; set; }
    [Key(4)] public long GoodsGrams { get; set; }
    [Key(5)] public string CauseKind { get; set; } = "";
    [Key(6)] public string StoppedBy { get; set; } = "";
    [Key(7)] public int StoppedAt { get; set; }
    [Key(8)] public int[] Structures { get; set; } = [];
    [Key(9)] public ulong[] Deaths { get; set; } = [];
    [Key(10)] public ulong[] Fighters { get; set; } = [];
    [Key(11)] public ulong[] Burned { get; set; } = [];

    public static IncidentRow Of(FireIncident i) => new()
    {
        Entry = i.Entry, StartSec = i.StartSec, CellsBurnt = i.CellsBurnt, GoodsMites = i.GoodsMites, GoodsGrams = i.GoodsGrams, CauseKind = i.CauseKind, StoppedBy = i.StoppedBy, StoppedAt = i.StoppedAtStructure,
        Structures = [.. i.Structures], Deaths = [.. i.Deaths], Fighters = [.. i.Fighters], Burned = [.. i.Burned],
    };

    public FireIncident Restore()
    {
        var i = new FireIncident { Entry = Entry, StartSec = StartSec, CellsBurnt = CellsBurnt, GoodsMites = GoodsMites, GoodsGrams = GoodsGrams, CauseKind = CauseKind, StoppedBy = StoppedBy, StoppedAtStructure = StoppedAt };
        foreach (var s in Structures) i.Structures.Add(s);
        foreach (var d in Deaths) i.Deaths.Add(d);
        foreach (var f in Fighters) i.Fighters.Add(f);
        foreach (var b in Burned) i.Burned.Add(b);
        return i;
    }
}

/// <summary>
/// The optional "fire_adopted" section (S1): the generated buildings a live fire adopted into the room world, written only by a world that has a "fire" section and adopted something (so a save
/// with no fire, or an older one, keeps its bytes). The blocks, the pieces as they stood at the save (a table of piece ids, then per cell: key, structure, fixture), the structures that touch the
/// blocks (id then cells) and the next structure id.
/// </summary>
[MessagePackObject]
public sealed class FireAdoptedSave
{
    public const string SectionName = "fire_adopted";

    [Key(0)] public long[] Blocks { get; set; } = [];
    [Key(1)] public string[] Pieces { get; set; } = [];
    /// <summary>Per cell: key, 1 + index of the structure piece (0 none), 1 + index of the fixture (0 none).</summary>
    [Key(2)] public long[][] Cells { get; set; } = [];
    /// <summary>Per structure: id, then its cells.</summary>
    [Key(3)] public long[][] Parts { get; set; } = [];
    [Key(4)] public int NextId { get; set; }

    public static FireAdoptedSave? Capture(FireBook b)
    {
        b.Sync();
        if (b.AdoptedBlocks.Count == 0) return null;
        var table = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (s, f) in b.AdoptedCells.Values) { if (s != null) table.Add(s); if (f != null) table.Add(f); }
        var pieces = table.ToArray(); var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pieces.Length; i++) index[pieces[i]] = i + 1;
        return new FireAdoptedSave
        {
            Blocks = [.. b.AdoptedBlocks], Pieces = pieces, NextId = b.AdoptedNextId,
            Cells = [.. b.AdoptedCells.Select(kv => new long[] { kv.Key, kv.Value.Structure == null ? 0 : index[kv.Value.Structure], kv.Value.Fixture == null ? 0 : index[kv.Value.Fixture] })],
            Parts = [.. b.AdoptedParts.Select(p => new long[] { p.Id }.Concat(p.Cells).ToArray())],
        };
    }

    public void RestoreInto(FireBook b)
    {
        foreach (var k in Blocks) b.AdoptedBlocks.Add(k);
        foreach (var r in Cells)
        {
            if (r.Length < 3 || r[1] < 0 || r[1] > Pieces.Length || r[2] < 0 || r[2] > Pieces.Length) throw new SaveException("Corrupt adopted cell in 'fire_adopted' section.");
            b.AdoptedCells[r[0]] = (r[1] == 0 ? null : Pieces[r[1] - 1], r[2] == 0 ? null : Pieces[r[2] - 1]);
        }
        b.AdoptedParts = [.. Parts.Select(p => p.Length < 1 ? throw new SaveException("Corrupt adopted structure in 'fire_adopted' section.") : ((int)p[0], p[1..]))];
        b.AdoptedNextId = NextId;
    }
}
