using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Worlds;

/// <summary>
/// The records of every generated region, by region id (seamless-regions spec 2.2, 3.7): the resident ones as <see cref="RegionWorld"/>s, the paged ones as
/// section bytes in an <see cref="IRegionPager"/>. Regions are built (and restored from a paged section) on <see cref="RegionManager"/> workers and join the set at
/// <see cref="Sync"/>, a barrier step on the caller's thread. The home region (the frame's origin, held by the world state) is never paged.
/// </summary>
public sealed class RegionRecordSet
{
    public RegionServices Services { get; }
    public RegionManager Manager { get; }
    public IRegionPager Pager { get; }
    public ChunkStore Chunks { get; }
    private readonly SortedDictionary<int, RegionWorld> _resident = [];
    private readonly object _lock = new();

    public RegionRecordSet(RegionServices services, RegionManager manager, ChunkStore chunks, IRegionPager? pager = null)
    {
        Services = services; Manager = manager; Chunks = chunks; Pager = pager ?? new MemoryRegionPager();
        manager.RecordsFactory = MakeRecords;
    }

    public RegionCoord Home => Services.Home;
    public RegionWorld? HomeWorld { get { lock (_lock) return _resident.GetValueOrDefault(Home.Id); } }

    /// <summary>Resident regions, ascending id.</summary>
    public IReadOnlyList<RegionWorld> Resident { get { lock (_lock) return [.. _resident.Values]; } }
    public int ResidentCount { get { lock (_lock) return _resident.Count; } }
    public RegionWorld? TryGet(RegionCoord at) { lock (_lock) return _resident.GetValueOrDefault(at.Id); }
    public bool IsPaged(RegionCoord at) => Pager.Has(at.Id) && TryGet(at) == null;

    /// <summary>Runs on a worker: the records of a just-built region, restored from its paged section when it has one, fresh otherwise.</summary>
    private RegionWorld? MakeRecords(RegionCoord at, RegionData terrain, People.Population pop)
    {
        if (pop.Households.Count == 0) return null; // a sea or wilderness region: terrain only, no records to tick (spec 7, Empty)
        var bytes = Pager.Read(at.Id);
        if (bytes != null) return RegionWorld.Restore(Services, at, terrain, SaveCodec.DeserializeCompressed<RegionSectionData>(bytes));
        return RegionWorld.CreateFresh(Services, at, pop);
    }

    /// <summary>Puts the home region's world state in the set (the embark region, built outside the manager).</summary>
    public void AdoptHome(RegionWorld home) { lock (_lock) _resident[home.Coord.Id] = home; }

    /// <summary>Barrier step: takes in regions the manager finished (their paged sections are now stale and go, their chunk entries return to the store). Returns how many joined.</summary>
    public int Sync()
    {
        int n = 0;
        foreach (var g in Manager.Ready)
        {
            if (g.Records == null) continue;
            lock (_lock) { if (_resident.ContainsKey(g.Coord.Id)) continue; _resident[g.Coord.Id] = g.Records; }
            Pager.Delete(g.Coord.Id);
            if (g.Records.PendingChunks is { Length: > 0 } c) Chunks.Absorb(c);
            g.Records.PendingChunks = null;
            n++;
        }
        return n;
    }

    /// <summary>Writes a region's section to the pager and drops it from memory (records, terrain, chunk entries). False for the home region or one that is not resident.</summary>
    public bool PageOut(RegionCoord at)
    {
        if (at == Home) return false;
        RegionWorld? rw;
        lock (_lock) { if (!_resident.TryGetValue(at.Id, out rw)) return false; }
        Pager.Write(at.Id, rw.CaptureBytes(Chunks));
        Chunks.RemoveWhere(k => ChunkRegions.Of(Home, k) == at);
        lock (_lock) _resident.Remove(at.Id);
        rw.Dispose();
        Manager.Evict(at);
        return true;
    }

    /// <summary>Asks for a paged region to come back (built and restored on a worker).</summary>
    public void PageIn(RegionCoord at, double priority = 0) { if (TryGet(at) == null && Pager.Has(at.Id)) Manager.Request(at, priority); }

    // ------------------------------------------------------------------ save sections

    /// <summary>The sections of every generated region but the home region: resident ones captured now, paged ones as stored. Call at a barrier (nothing ticking).</summary>
    public SortedDictionary<int, RegionSectionData> CaptureResident()
    {
        var d = new SortedDictionary<int, RegionWorld>();
        lock (_lock) foreach (var (id, rw) in _resident) if (id != Home.Id) d[id] = rw;
        var r = new SortedDictionary<int, RegionSectionData>();
        foreach (var (id, rw) in d) r[id] = rw.Capture(Chunks);
        return r;
    }

    public SortedDictionary<int, byte[]> PagedSections()
    {
        var r = new SortedDictionary<int, byte[]>();
        foreach (int id in Pager.Ids) { if (TryGet(RegionCoord.FromId(id)) != null || id == Home.Id) continue; if (Pager.Read(id) is { } b) r[id] = b; }
        return r;
    }

    /// <summary>Hands the section bytes of a loaded save to the pager: those regions come back, restored, when approached.</summary>
    public void AttachSections(IEnumerable<KeyValuePair<int, byte[]>> sections) { foreach (var (id, b) in sections) Pager.Write(id, b); }

    /// <summary>One digest of all resident regions' records, ascending id.</summary>
    public string ComputeHash()
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var rw in Resident) { h.AppendData(System.Text.Encoding.ASCII.GetBytes(rw.Coord.Id + ":" + rw.RecordsHash() + ";")); }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}

/// <summary>Where the hero is among the regions. <see cref="Region"/> follows the hero at once (it is the hero's id in the save); <see cref="RingRegion"/> (spec 3.5) changes only once the hero is deeper than the hysteresis inside the new region.</summary>
public sealed class HeroRegionTracker(RegionCoord start, int hysteresisM = 1000)
{
    public RegionCoord Region { get; private set; } = start;
    public RegionCoord RingRegion { get; private set; } = start;
    public int HysteresisM { get; } = hysteresisM;

    /// <summary>Continent metres. Returns true when <see cref="RingRegion"/> changed.</summary>
    public bool Update(long xM, long yM)
    {
        var at = RegionCoord.OfMetres(xM, yM);
        if (!at.IsValid) return false;
        Region = at;
        if (at == RingRegion) return false;
        long x = xM - at.OriginXM, y = yM - at.OriginYM, m = RegionCoord.RegionM;
        long depth = Math.Min(Math.Min(x, m - 1 - x), Math.Min(y, m - 1 - y)); // distance to the nearest edge of the region the hero is in
        if (depth < HysteresisM) return false;
        RingRegion = at;
        return true;
    }

    /// <summary>The region holding a world position of the streaming frame (cells = metres, origin at the north-west corner of <paramref name="home"/>).</summary>
    public static RegionCoord OfFrameCell(RegionCoord home, long cellX, long cellY)
        => RegionCoord.OfMetres(home.OriginXM + cellX, home.OriginYM + cellY);
}
