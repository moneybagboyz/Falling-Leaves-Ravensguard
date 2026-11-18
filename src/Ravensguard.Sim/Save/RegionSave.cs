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
}
