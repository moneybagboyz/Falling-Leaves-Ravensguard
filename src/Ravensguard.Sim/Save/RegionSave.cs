using MessagePack;

namespace Ravensguard.Sim.Save;

/// <summary>
/// One generated region's records, as a single section (seamless-regions spec 3.7): people, households, lot bindings, economy, farming (fields, parcels, herds),
/// businesses and markets, the tick state, the region's chronicle and id counters, and the chunk entries (first-visit snapshots and deltas) that lie in it. The
/// terrain is not stored: it is a pure function of seed and content. The section is written to the save file under <see cref="WorldSave.RegionSectionName"/>
/// and to the pager's file when the region is paged out; the same bytes either way, so paging never changes a save.
/// </summary>
[MessagePackObject]
public sealed class RegionSectionData
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public int RegionId { get; set; }
    [Key(2)] public PeopleData People { get; set; } = new();
    [Key(3)] public RegionLotsData Lots { get; set; } = new();
    [Key(4)] public EconomySave Economy { get; set; } = new();
    [Key(5)] public AbstractSave Abstract { get; set; } = new();
    [Key(6)] public FarmingSave Farming { get; set; } = new();
    [Key(7)] public ProductionSave Production { get; set; } = new();
    [Key(8)] public MarketsSave Markets { get; set; } = new();
    [Key(9)] public ChronicleData Chronicle { get; set; } = new();
    [Key(10)] public ulong[] IdState { get; set; } = [];
    /// <summary>The region's chunk entries (<see cref="ChunkStore.ToSection(Func{ChunkKey, bool})"/> bytes).</summary>
    [Key(11)] public byte[] Chunks { get; set; } = [];
    /// <summary>Hash of the regenerated region the people are diffed against (people, households, lots and terrain; <see cref="BaselineGuard"/>). Empty when the people are stored whole, and in saves of format v1.</summary>
    [Key(12)] public string BaselineHash { get; set; } = "";
    /// <summary>The region generator version the baseline was made with (<see cref="SaveFormat.RegionGeneratorVersion"/>); 0 when the people are stored whole.</summary>
    [Key(13)] public int GeneratorVersion { get; set; }

    /// <summary>Makes the baseline population this region's people are diffed against (the populator's output again). Set by <c>RegionWorld.Capture</c>; not stored.</summary>
    [IgnoreMember] public Func<Ravensguard.Sim.People.Population>? Baseline { get; set; }

    /// <summary>Replaces the people by their patches over the baseline (spec N24) and stamps the baseline guard; call before encoding, on any thread. A section with no baseline stays whole.</summary>
    public void Compact()
    {
        if (Baseline == null || People.Diff != null) return;
        var bp = Baseline();
        People = PeopleDiffer.Compact(People, WorldSave.EncodePeople(bp));
        BaselineHash = BaselineGuard.HashOf(bp); GeneratorVersion = SaveFormat.RegionGeneratorVersion;
        Baseline = null;
    }
}
