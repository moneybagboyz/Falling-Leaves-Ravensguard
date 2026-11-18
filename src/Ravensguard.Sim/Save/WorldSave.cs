using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Save;

/// <summary>
/// A populated world plus its mutable runtime state (the unit that is saved). Continent, history and region terrain are
/// derived from the settings and are NOT stored; everything else is.
/// </summary>
public sealed class WorldState
{
    public required ContinentSettings ContinentSettings { get; init; }
    public required HistorySettings HistorySettings { get; init; }
    public required RegionSettings RegionSettings { get; init; }
    public required int EmbarkCellX { get; init; }
    public required int EmbarkCellY { get; init; }
    public required Continent Continent { get; init; }
    public required WorldHistory History { get; init; }
    public required Population Population { get; init; }
    public required SimClock Clock { get; init; }
    public required IdAllocator Ids { get; init; }
    public required ChunkStore Chunks { get; init; }
    public required Chronicle Chronicle { get; init; }
    /// <summary>The player character (saved in the "hero" section when present).</summary>
    public HeroRecord? Hero { get; init; }
    /// <summary>The unlocked Codex entries (saved in the "codex" section when present).</summary>
    public CodexProgress? Codex { get; init; }
    /// <summary>Coin, goods, stores and household needs (saved in the "economy" section when present). Created by <see cref="EconomyState.Create"/> at world creation.</summary>
    public EconomyState? Economy { get; init; }
    /// <summary>Field, parcel and herd state of farming (saved in the "farming" section when present); needs <see cref="Economy"/> for the stores it owns.</summary>
    public Ravensguard.Sim.Farming.FarmingState? Farming { get; set; }
    /// <summary>The abstract tick's progress and vital counters (saved in the "abstract" section when present); attached by <c>SimWorld.From</c>.</summary>
    public Ravensguard.Sim.Abstract.AbstractState? Abstract { get; set; }
    /// <summary>Live play state restored from a save made with <see cref="WorldSave.CaptureLive"/> (hero position, torch, explored tiles...), null for a save without it.
    /// <c>new LiveWorld(streamer, state.Live, ...)</c> resumes there; <see cref="Clock"/> already holds the saved live clock.</summary>
    public LiveResume? Live { get; init; }
    public ulong Seed => ContinentSettings.Seed;
}

// ---------------- section DTOs (explicit [Key] order; all lists in index/id order, so bytes are deterministic) ----------------

[MessagePackObject]
public sealed class WorldMeta
{
    [Key(0)] public ulong Seed { get; set; }
    [Key(1)] public string ContinentSettingsJson { get; set; } = "";
    [Key(2)] public string HistorySettingsJson { get; set; } = "";
    [Key(3)] public string RegionSettingsJson { get; set; } = "";
    [Key(4)] public int EmbarkCellX { get; set; }
    [Key(5)] public int EmbarkCellY { get; set; }
    /// <summary>Hashes of the regenerated layers; verified on load.</summary>
    [Key(6)] public string ContinentHash { get; set; } = "";
    [Key(7)] public string HistoryHash { get; set; } = "";
    [Key(8)] public string RegionHash { get; set; } = "";
    /// <summary>Integrity check of the stored population after decoding.</summary>
    [Key(9)] public string PopulationHash { get; set; } = "";
    [Key(10)] public long ClockSeconds { get; set; }
    [Key(11)] public long ClockMsRemainder { get; set; }
    [Key(12)] public int ClockSpeed { get; set; }
    [Key(13)] public ulong[] IdState { get; set; } = [];
    /// <summary>Hash of the placed landmarks (a pure function of region, history and content); empty in saves made before it was stored.</summary>
    [Key(14)] public string LandmarkHash { get; set; } = "";
}

/// <summary>The "live" section: where the hero stands and which map tiles are explored. The clock is in the meta section and the Codex in its own.</summary>
[MessagePackObject]
public sealed class LiveSave
{
    [Key(0)] public long WorldXRaw { get; set; }
    [Key(1)] public long WorldYRaw { get; set; }
    [Key(2)] public int Z { get; set; }
    [Key(3)] public byte Facing { get; set; }
    [Key(4)] public bool Torch { get; set; }
    /// <summary>Packed explored-tile bits (see <see cref="ExploredTiles.ToBytes"/>).</summary>
    [Key(5)] public byte[] Explored { get; set; } = [];
}

[MessagePackObject]
public sealed class PeopleData
{
    [Key(0)] public int Count { get; set; }
    [Key(1)] public ulong PersonBaseCounter { get; set; }
    [Key(2)] public string[] Given { get; set; } = [];
    [Key(3)] public string[] Family { get; set; } = [];
    [Key(4)] public string[] Byname { get; set; } = [];
    [Key(5)] public Sex[] Sex { get; set; } = [];
    [Key(6)] public int[] BirthDay { get; set; } = [];
    [Key(7)] public byte[] Culture { get; set; } = [];
    [Key(8)] public int[] Household { get; set; } = [];
    [Key(9)] public int[] Father { get; set; } = [];
    [Key(10)] public int[] Mother { get; set; } = [];
    [Key(11)] public int[] Spouse { get; set; } = [];
    [Key(12)] public Occupation[] Occupation { get; set; } = [];
    [Key(13)] public int[] Employer { get; set; } = [];
    [Key(14)] public int[] WorkSettlement { get; set; } = [];
    [Key(15)] public short[] WorkLot { get; set; } = [];
    [Key(16)] public SocialClass[] Class { get; set; } = [];
    [Key(17)] public byte[] Attr { get; set; } = [];
    [Key(18)] public byte[] Skills { get; set; } = [];
    [Key(19)] public ushort[] Traits { get; set; } = [];
    [Key(20)] public int[] HomeSettlement { get; set; } = [];
    [Key(21)] public short[] HomeLot { get; set; } = [];
    [Key(22)] public short[] Bed { get; set; } = [];
    [Key(23)] public HomelessReason[] Homeless { get; set; } = [];
    [Key(24)] public ulong[] Notable { get; set; } = [];
    [Key(25)] public ulong[] Dynasty { get; set; } = [];

    // households
    [Key(26)] public int HhCount { get; set; }
    [Key(27)] public ulong HhBaseCounter { get; set; }
    [Key(28)] public int[] HhSettlement { get; set; } = [];
    [Key(29)] public int[] HhHead { get; set; } = [];
    [Key(30)] public int[] HhMemberStart { get; set; } = [];
    [Key(31)] public short[] HhLot { get; set; } = [];
    [Key(32)] public short[] HhMemberCount { get; set; } = [];
    [Key(33)] public short[] HhBeds { get; set; } = [];
    [Key(34)] public short[] HhParcelCount { get; set; } = [];
    [Key(35)] public DwellingType[] HhDwelling { get; set; } = [];
    [Key(36)] public DeedKind[] HhDeed { get; set; } = [];
    [Key(37)] public SocialClass[] HhClass { get; set; } = [];
    [Key(38)] public int[] HhPurse { get; set; } = [];
    [Key(39)] public string[] HhName { get; set; } = [];
    [Key(40)] public int[] HhMemberIdx { get; set; } = [];
}

/// <summary>The mutable bindings of the region's lots, fiefs and parcels to people (not regenerable from terrain).</summary>
[MessagePackObject]
public sealed class RegionLotsData
{
    [Key(0)] public int NowDay { get; set; }
    [Key(1)] public int[][] LotHousehold { get; set; } = [];
    [Key(2)] public int[][] LotBeds { get; set; } = [];
    [Key(3)] public int[][] LotKeeper { get; set; } = [];
    [Key(4)] public int[] FiefLord { get; set; } = [];
    [Key(5)] public int[] FiefLordHousehold { get; set; } = [];
    [Key(6)] public int[] ParcelHolder { get; set; } = [];
    [Key(7)] public int[] PriestHousehold { get; set; } = [];
    [Key(8)] public int[] SettlementPeople { get; set; } = [];
}

[MessagePackObject]
public sealed class ChronicleEntryData
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public long When { get; set; }
    [Key(2)] public string TypeId { get; set; } = "";
    [Key(3)] public ulong[] Subjects { get; set; } = [];
    [Key(4)] public List<CauseData> Causes { get; set; } = [];
    /// <summary>Sorted by key (ordinal): alternating key, value.</summary>
    [Key(5)] public string[] Data { get; set; } = [];
}

[MessagePackObject]
public sealed class CauseData
{
    [Key(0)] public int Kind { get; set; }
    [Key(1)] public ulong EventId { get; set; }
    [Key(2)] public bool HasEvent { get; set; }
    [Key(3)] public string Note { get; set; } = "";
}

[MessagePackObject]
public sealed class ChronicleData
{
    [Key(0)] public List<ChronicleEntryData> Entries { get; set; } = [];
}

/// <summary>
/// Turns a populated world into SaveFile sections and back (plan §2.2, §2.11).
/// Stored: meta (seed, settings, clock, ids, layer hashes), people (persons, households, kin links), region.lots, chunks, chronicle.
/// Regenerated from seed + content on load (and verified by hash): continent, history, region terrain/settlements/parcels.
/// </summary>
public static class WorldSave
{
    public const string MetaSection = "meta", PeopleSection = "people", LotsSection = "region.lots", ChunksSection = ChunkStore.SectionName, ChronicleSection = "chronicle", HeroSection = "hero", CodexSection = "codex", LiveSection = "live", EconomySection = "economy", AbstractSection = "abstract", FarmingSection = "farming";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // ---------- capture ----------

    /// <summary>Saves the world as it is in <paramref name="w"/> (no live play state). Encodes on the calling thread.</summary>
    public static SaveFile Capture(WorldState w, string contentHash, string gameVersion, IReadOnlyList<ModEntry>? mods = null, bool ironman = false)
        => Snapshot(w, null, contentHash, gameVersion, mods, ironman).Encode();

    /// <summary>
    /// Saves the world together with the live play state (hero position, facing, torch, the live clock, Codex progress, explored tiles) of
    /// <paramref name="live"/>, which replaces the world's own clock and Codex. Encodes on the calling thread; for a short hitch take a
    /// <see cref="SnapshotLive"/> there and call <see cref="LiveCapture.Encode"/> on a worker (or use <see cref="CaptureLiveAsync"/>).
    /// <paramref name="contentHash"/> must be the loaded content's hash (the same one <see cref="Restore"/> is given).
    /// </summary>
    public static SaveFile CaptureLive(WorldState world, LiveWorld live, string contentHash = "", string gameVersion = "0.1", IReadOnlyList<ModEntry>? mods = null, bool ironman = false)
        => SnapshotLive(world, live, contentHash, gameVersion, mods, ironman).Encode();

    /// <summary>The caller-thread half of a save: copies everything that play can change (people arrays, chunk store entries, live state) in a few milliseconds.</summary>
    public static LiveCapture SnapshotLive(WorldState world, LiveWorld live, string contentHash = "", string gameVersion = "0.1", IReadOnlyList<ModEntry>? mods = null, bool ironman = false)
        => Snapshot(world, live.CaptureResume(), contentHash, gameVersion, mods, ironman);

    /// <summary><see cref="SnapshotLive"/> now, encoding on the thread pool: only the snapshot costs the caller.</summary>
    public static Task<SaveFile> CaptureLiveAsync(WorldState world, LiveWorld live, string contentHash = "", string gameVersion = "0.1", IReadOnlyList<ModEntry>? mods = null, bool ironman = false)
    {
        var snap = SnapshotLive(world, live, contentHash, gameVersion, mods, ironman);
        return Task.Run(snap.Encode);
    }

    internal static LiveCapture Snapshot(WorldState w, LiveResume? live, string contentHash, string gameVersion, IReadOnlyList<ModEntry>? mods, bool ironman)
    {
        (GameTime Time, long MsRemainder, SimSpeed Speed) clock = live != null ? (live.Time, live.MsRemainder, live.Speed) : (w.Clock.Now, w.Clock.GameMsRemainder, w.Clock.Speed);
        var meta = new WorldMeta
        {
            Seed = w.Seed,
            ContinentSettingsJson = JsonSerializer.Serialize(w.ContinentSettings, Json),
            HistorySettingsJson = JsonSerializer.Serialize(w.HistorySettings, Json),
            RegionSettingsJson = JsonSerializer.Serialize(w.RegionSettings, Json),
            EmbarkCellX = w.EmbarkCellX, EmbarkCellY = w.EmbarkCellY,
            ClockSeconds = clock.Time.Seconds, ClockMsRemainder = clock.MsRemainder, ClockSpeed = (int)clock.Speed,
            IdState = w.Ids.GetState(),
        };
        var header = new SaveHeader
        {
            GameVersion = gameVersion, ContentHash = contentHash, Mods = mods ?? [], WorldSeed = w.Seed, GameSeconds = clock.Time.Seconds, Ironman = ironman,
        };
        var codex = live?.Codex ?? w.Codex;
        return new LiveCapture
        {
            Header = header, Meta = meta, Continent = w.Continent, History = w.History, Region = w.Population.Region,
            People = EncodePeople(w.Population), Lots = EncodeLots(w.Population), Chunks = w.Chunks.Snapshot(), Chronicle = EncodeChronicle(w.Chronicle),
            Hero = w.Hero?.ToSave(), Economy = w.Economy == null ? null : EconomySave.Capture(w.Economy), Abstract = w.Abstract == null ? null : AbstractSave.Capture(w.Abstract, w.Population), Farming = w.Farming == null ? null : FarmingSave.Capture(w.Farming), Codex = codex == null ? null : new CodexSave { Unlocked = codex.Unlocked.ToArray() },
            Live = live == null ? null : new LiveSave
            {
                WorldXRaw = live.WorldXRaw, WorldYRaw = live.WorldYRaw, Z = live.Z, Facing = (byte)live.Facing, Torch = live.Torch, Explored = live.Explored.ToBytes(),
            },
        };
    }

    // ---------- restore ----------

    /// <summary>Rebuilds the world from seed + content, then applies the stored sections. Throws <see cref="SaveException"/> with a readable message on any mismatch.</summary>
    public static WorldState Restore(SaveFile file, ContentDatabase content, string currentContentHash, IReadOnlyList<ModEntry>? installedMods = null,
        Continent? knownContinent = null, WorldHistory? knownHistory = null)
    {
        var report = new SaveReader().CheckCompatibility(file.Header, currentContentHash, installedMods ?? file.Header.Mods);
        if (!report.ContentHashMatches)
            throw new SaveException("This save cannot be loaded because the game content changed since it was made "
                + $"(saved content {Short(file.Header.ContentHash)}, installed {Short(currentContentHash)}). The world is regenerated from the content, so it would differ. " + report.ToText().Trim());
        if (!report.CanLoad) throw new SaveException(report.ToText().Trim());

        var meta = Decode<WorldMeta>(file, MetaSection, compressed: false);
        var cs = JsonSerializer.Deserialize<ContinentSettings>(meta.ContinentSettingsJson, Json) ?? throw new SaveException("Corrupt continent settings in save.");
        var hs = JsonSerializer.Deserialize<HistorySettings>(meta.HistorySettingsJson, Json) ?? throw new SaveException("Corrupt history settings in save.");
        var rs = JsonSerializer.Deserialize<RegionSettings>(meta.RegionSettingsJson, Json) ?? throw new SaveException("Corrupt region settings in save.");

        // A world library supplies the continent and history it already generated (and hashed) so they are not regenerated twice; hashes are still verified.
        var continent = knownContinent ?? ContinentGenerator.Generate(cs);
        Verify("continent", meta.ContinentHash, LayerHashes.Of(continent));
        var history = knownHistory ?? HistoryGenerator.Generate(continent, hs);
        Verify("history", meta.HistoryHash, LayerHashes.Of(history));
        var region = RegionGenerator.Generate(continent, history, meta.EmbarkCellX, meta.EmbarkCellY, rs);
        Verify("region", meta.RegionHash, LayerHashes.Of(region));
        // the landmark phase is a pure function of region, history and content, so only its hash is stored (absent in older saves)
        var marks = LandmarkPlacer.Place(region, history, content);
        string markHash = LayerHashes.OfLandmarks(marks);
        if (meta.LandmarkHash.Length > 0) Verify("landmark", meta.LandmarkHash, markHash);

        var pop = DecodePeople(Decode<PeopleData>(file, PeopleSection, true), Decode<RegionLotsData>(file, LotsSection, true), region);
        Verify("population", meta.PopulationHash, pop.ComputeHash());

        var chunks = file.Sections.TryGetValue(ChunksSection, out var cb) ? ChunkStore.FromSection(cb) : new ChunkStore();
        var chronicle = DecodeChronicle(Decode<ChronicleData>(file, ChronicleSection, true));
        HeroRecord? hero = file.Sections.ContainsKey(HeroSection) ? HeroRecord.FromSave(Decode<HeroSave>(file, HeroSection, compressed: false)) : null;
        CodexProgress? codex = null;
        if (file.Sections.ContainsKey(CodexSection))
        {
            codex = new CodexProgress();
            foreach (var id in Decode<CodexSave>(file, CodexSection, compressed: false).Unlocked) codex.Unlocked.Add(id);
        }
        LiveResume? resume = null;
        if (file.Sections.ContainsKey(LiveSection))
        {
            var ls = Decode<LiveSave>(file, LiveSection, compressed: true);
            resume = new LiveResume(ls.WorldXRaw, ls.WorldYRaw, ls.Z, (Facing)ls.Facing, ls.Torch, new GameTime(meta.ClockSeconds), meta.ClockMsRemainder, (SimSpeed)meta.ClockSpeed,
                ExploredTiles.FromBytes(ls.Explored), codex);
        }
        AbstractState? abstractState = file.Sections.ContainsKey(AbstractSection) ? Decode<AbstractSave>(file, AbstractSection, compressed: true).Restore(pop) : null;
        EconomyState? economy = file.Sections.ContainsKey(EconomySection) ? Decode<EconomySave>(file, EconomySection, compressed: true).Restore(pop, content) : null;
        return new WorldState
        {
            Hero = hero, Codex = codex, Live = resume, Economy = economy, Abstract = abstractState,
            Farming = file.Sections.ContainsKey(FarmingSection) ? Decode<FarmingSave>(file, FarmingSection, compressed: true).Restore(region) : null,
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = meta.EmbarkCellX, EmbarkCellY = meta.EmbarkCellY,
            Continent = continent, History = history, Population = pop,
            Clock = new SimClock(new GameTime(meta.ClockSeconds), meta.ClockMsRemainder) { Speed = (SimSpeed)meta.ClockSpeed },
            Ids = IdAllocator.FromState(meta.IdState), Chunks = chunks, Chronicle = chronicle,
        };
    }

    private static string Short(string h) => h.Length > 12 ? h[..12] : h;

    private static void Verify(string layer, string stored, string actual)
    {
        if (!string.Equals(stored, actual, StringComparison.Ordinal))
            throw new SaveException($"This save cannot be loaded: the regenerated {layer} differs from the one the save was made with "
                + "(the world generator or game content changed since). Load it with the game version that made it.");
    }

    private static T Decode<T>(SaveFile f, string name, bool compressed)
    {
        if (!f.Sections.TryGetValue(name, out var b)) throw new SaveException($"Save is missing the '{name}' section.");
        try { return compressed ? SaveCodec.DeserializeCompressed<T>(b) : SaveCodec.Deserialize<T>(b); }
        catch (Exception e) when (e is not SaveException) { throw new SaveException($"Corrupt '{name}' section in save.", e); }
    }

    // ---------- people ----------

    /// <summary>Hash of a population as encoded (decodes the copies into a temporary population, so it can run on a worker thread).</summary>
    internal static string PopulationHashOf(PeopleData people, RegionLotsData lots, RegionData region) => DecodePeople(people, lots, region).ComputeHash();

    private static T[] Cut<T>(T[] a, int n) => a.AsSpan(0, n).ToArray();

    private static PeopleData EncodePeople(Population pop)
    {
        var p = pop.People; var h = pop.Households; int n = p.Count, m = h.Count;
        return new PeopleData
        {
            Count = n, PersonBaseCounter = n > 0 ? p.IdOf(0).Counter : 1,
            Given = Cut(p.Given, n), Family = Cut(p.Family, n), Byname = Cut(p.Byname, n), Sex = Cut(p.Sex, n), BirthDay = Cut(p.BirthDay, n), Culture = Cut(p.Culture, n),
            Household = Cut(p.Household, n), Father = Cut(p.Father, n), Mother = Cut(p.Mother, n), Spouse = Cut(p.Spouse, n), Occupation = Cut(p.Occupation, n),
            Employer = Cut(p.Employer, n), WorkSettlement = Cut(p.WorkSettlement, n), WorkLot = Cut(p.WorkLot, n), Class = Cut(p.Class, n),
            Attr = Cut(p.Attr, n * PersonTable.AttrCount), Skills = Cut(p.Skills, n * PersonTable.SkillSlots * 2), Traits = Cut(p.Traits, n),
            HomeSettlement = Cut(p.HomeSettlement, n), HomeLot = Cut(p.HomeLot, n), Bed = Cut(p.Bed, n), Homeless = Cut(p.Homeless, n),
            Notable = Cut(p.Notable, n).Select(e => e.Value).ToArray(), Dynasty = Cut(p.Dynasty, n).Select(e => e.Value).ToArray(),
            HhCount = m, HhBaseCounter = m > 0 ? h.IdOf(0).Counter : 1,
            HhSettlement = Cut(h.Settlement, m), HhHead = Cut(h.Head, m), HhMemberStart = Cut(h.MemberStart, m), HhLot = Cut(h.Lot, m), HhMemberCount = Cut(h.MemberCount, m),
            HhBeds = Cut(h.Beds, m), HhParcelCount = Cut(h.ParcelCount, m), HhDwelling = Cut(h.Dwelling, m), HhDeed = Cut(h.Deed, m), HhClass = Cut(h.Class, m),
            HhPurse = Cut(h.Purse, m), HhName = Cut(h.Name, m), HhMemberIdx = h.MemberIdx.ToArray(),
        };
    }

    private static RegionLotsData EncodeLots(Population p) => new()
    {
        NowDay = p.NowDay, LotHousehold = CopyJagged(p.LotHousehold), LotBeds = CopyJagged(p.LotBeds), LotKeeper = CopyJagged(p.LotKeeper), FiefLord = (int[])p.FiefLord.Clone(),
        FiefLordHousehold = (int[])p.FiefLordHousehold.Clone(), ParcelHolder = (int[])p.ParcelHolder.Clone(), PriestHousehold = (int[])p.PriestHousehold.Clone(),
        SettlementPeople = (int[])p.SettlementPeople.Clone(),
    };

    private static int[][] CopyJagged(int[][] a) { var r = new int[a.Length][]; for (int i = 0; i < a.Length; i++) r[i] = (int[])a[i].Clone(); return r; }

    private static void Fill<T>(T[] dst, T[] src, string what)
    {
        if (src.Length > dst.Length) throw new SaveException($"Corrupt people section ({what}).");
        Array.Copy(src, dst, src.Length);
    }

    private static Population DecodePeople(PeopleData d, RegionLotsData l, RegionData region)
    {
        var p = new PersonTable(d.Count, d.PersonBaseCounter);
        for (int i = 0; i < d.Count; i++) p.Add();
        Fill(p.Given, d.Given, "given"); Fill(p.Family, d.Family, "family"); Fill(p.Byname, d.Byname, "byname"); Fill(p.Sex, d.Sex, "sex");
        Fill(p.BirthDay, d.BirthDay, "birth"); Fill(p.Culture, d.Culture, "culture"); Fill(p.Household, d.Household, "household"); Fill(p.Father, d.Father, "father");
        Fill(p.Mother, d.Mother, "mother"); Fill(p.Spouse, d.Spouse, "spouse"); Fill(p.Occupation, d.Occupation, "occupation"); Fill(p.Employer, d.Employer, "employer");
        Fill(p.WorkSettlement, d.WorkSettlement, "worksettlement"); Fill(p.WorkLot, d.WorkLot, "worklot"); Fill(p.Class, d.Class, "class"); Fill(p.Attr, d.Attr, "attr");
        Fill(p.Skills, d.Skills, "skills"); Fill(p.Traits, d.Traits, "traits"); Fill(p.HomeSettlement, d.HomeSettlement, "homesettlement"); Fill(p.HomeLot, d.HomeLot, "homelot");
        Fill(p.Bed, d.Bed, "bed"); Fill(p.Homeless, d.Homeless, "homeless");
        Fill(p.Notable, d.Notable.Select(v => new EntityId(v)).ToArray(), "notable"); Fill(p.Dynasty, d.Dynasty.Select(v => new EntityId(v)).ToArray(), "dynasty");

        var h = new HouseholdTable(d.HhCount, d.HhBaseCounter);
        for (int i = 0; i < d.HhCount; i++) h.Add(0, 0, default, 0);
        h.MemberIdx.Clear(); h.MemberIdx.AddRange(d.HhMemberIdx);
        Fill(h.Settlement, d.HhSettlement, "hhsettlement"); Fill(h.Head, d.HhHead, "hhhead"); Fill(h.MemberStart, d.HhMemberStart, "hhstart"); Fill(h.Lot, d.HhLot, "hhlot");
        Fill(h.MemberCount, d.HhMemberCount, "hhcount"); Fill(h.Beds, d.HhBeds, "hhbeds"); Fill(h.ParcelCount, d.HhParcelCount, "hhparcels"); Fill(h.Dwelling, d.HhDwelling, "hhdwelling");
        Fill(h.Deed, d.HhDeed, "hhdeed"); Fill(h.Class, d.HhClass, "hhclass"); Fill(h.Purse, d.HhPurse, "hhpurse"); Fill(h.Name, d.HhName, "hhname");

        return new Population
        {
            Region = region, People = p, Households = h, NowDay = l.NowDay, LotHousehold = l.LotHousehold, LotBeds = l.LotBeds, LotKeeper = l.LotKeeper,
            FiefLord = l.FiefLord, FiefLordHousehold = l.FiefLordHousehold, ParcelHolder = l.ParcelHolder, PriestHousehold = l.PriestHousehold, SettlementPeople = l.SettlementPeople,
        };
    }

    // ---------- chronicle ----------

    private static ChronicleData EncodeChronicle(Chronicle c)
    {
        var d = new ChronicleData();
        foreach (var e in c.All) // append order is the canonical order
        {
            var x = new ChronicleEntryData { Id = e.Id.Value, When = e.When.Seconds, TypeId = e.TypeId, Subjects = e.Subjects.Select(s => s.Value).ToArray() };
            foreach (var cs in e.Causes) x.Causes.Add(new CauseData { Kind = (int)cs.Kind, HasEvent = cs.EventId.HasValue, EventId = cs.EventId?.Value ?? 0, Note = cs.Note });
            var data = new List<string>();
            foreach (var kv in e.Data.OrderBy(k => k.Key, StringComparer.Ordinal)) { data.Add(kv.Key); data.Add(kv.Value); }
            x.Data = data.ToArray();
            d.Entries.Add(x);
        }
        return d;
    }

    private static Chronicle DecodeChronicle(ChronicleData d)
    {
        var c = new Chronicle();
        foreach (var x in d.Entries)
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < x.Data.Length; i += 2) data[x.Data[i]] = x.Data[i + 1];
            c.Add(new ChronicleEntry(new EntityId(x.Id), new GameTime(x.When), x.TypeId, x.Subjects.Select(s => new EntityId(s)).ToList(),
                x.Causes.Select(k => new Cause((CauseKind)k.Kind, k.HasEvent ? new EntityId(k.EventId) : null, k.Note)).ToList(), data));
        }
        return c;
    }
}

/// <summary>
/// A world save in two halves: <see cref="WorldSave.SnapshotLive"/> copies what play can change on the calling thread, and <see cref="Encode"/> (any thread)
/// hashes, serialises and compresses the copies. The copies share only immutable data with the running game (generated layers, chunk snapshot payloads), so
/// encoding can run while play goes on.
/// </summary>
public sealed class LiveCapture
{
    internal SaveHeader Header { get; init; } = null!;
    internal WorldMeta Meta { get; init; } = null!;
    internal Continent Continent { get; init; } = null!;
    internal WorldHistory History { get; init; } = null!;
    internal RegionData Region { get; init; } = null!;
    internal PeopleData People { get; init; } = null!;
    internal RegionLotsData Lots { get; init; } = null!;
    internal ChunkStoreSnapshot Chunks { get; init; } = null!;
    internal ChronicleData Chronicle { get; init; } = null!;
    internal HeroSave? Hero { get; init; }
    internal CodexSave? Codex { get; init; }
    internal LiveSave? Live { get; init; }
    internal EconomySave? Economy { get; init; }
    internal AbstractSave? Abstract { get; init; }
    internal FarmingSave? Farming { get; init; }

    /// <summary>Chunk entries that had to be encoded by the last <see cref="Encode"/> (the rest were reused from earlier saves).</summary>
    public int ChunkEntriesEncoded => Chunks.ReEncoded;

    public SaveFile Encode()
    {
        var meta = Meta;
        // the generated layers never change after generation, so their hashes are computed once per object (at load verification, or the first save) and reused
        meta.ContinentHash = LayerHashes.Of(Continent); meta.HistoryHash = LayerHashes.Of(History); meta.RegionHash = LayerHashes.Of(Region);
        meta.LandmarkHash = Region.Landmarks == null ? "" : LayerHashes.OfLandmarks(Region.Landmarks); // empty: the landmark phase had not run, nothing to verify
        meta.PopulationHash = WorldSave.PopulationHashOf(People, Lots, Region);
        var sections = new List<KeyValuePair<string, byte[]>>
        {
            new(WorldSave.MetaSection, SaveCodec.Serialize(meta)),
            new(WorldSave.PeopleSection, SaveCodec.SerializeCompressed(People)),
            new(WorldSave.LotsSection, SaveCodec.SerializeCompressed(Lots)),
            new(WorldSave.ChunksSection, Chunks.Encode()),
            new(WorldSave.ChronicleSection, SaveCodec.SerializeCompressed(Chronicle)),
        };
        // additive sections: absent for worlds that have no hero, Codex or live state yet, so older saves and tests keep their bytes
        if (Hero != null) sections.Add(new(WorldSave.HeroSection, SaveCodec.Serialize(Hero)));
        if (Codex != null) sections.Add(new(WorldSave.CodexSection, SaveCodec.Serialize(Codex)));
        if (Live != null) sections.Add(new(WorldSave.LiveSection, SaveCodec.SerializeCompressed(Live)));
        if (Economy != null) sections.Add(new(WorldSave.EconomySection, SaveCodec.SerializeCompressed(Economy)));
        if (Abstract != null) sections.Add(new(WorldSave.AbstractSection, SaveCodec.SerializeCompressed(Abstract)));
        if (Farming != null) sections.Add(new(WorldSave.FarmingSection, SaveCodec.SerializeCompressed(Farming)));
        return new SaveFile(Header, sections);
    }
}

/// <summary>
/// Hashes of the generated layers, computed once per layer object. Generation is finished before a layer is used, so a layer's hash cannot change; caching it
/// keeps a save from re-hashing a 500 x 500 region (and the continent and history) every time.
/// </summary>
public static class LayerHashes
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, string> Cache = new();

    public static string Of(Continent c) => Cache.GetValue(c, o => ((Continent)o).ComputeHash());
    public static string Of(WorldHistory h) => Cache.GetValue(h, o => ((WorldHistory)o).ComputeHash());
    public static string Of(RegionData r) => Cache.GetValue(r, o => ((RegionData)o).ComputeHash());
    /// <summary>Hash of a placed-landmark list (cached per list object; null or empty hash alike).</summary>
    public static string OfLandmarks(IReadOnlyList<PlacedLandmark>? marks) => marks == null ? LandmarkPlacer.ComputeHash(null) : Cache.GetValue(marks, o => LandmarkPlacer.ComputeHash((IReadOnlyList<PlacedLandmark>)o));
}
