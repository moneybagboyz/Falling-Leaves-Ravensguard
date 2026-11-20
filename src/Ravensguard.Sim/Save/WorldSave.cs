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
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Economy.Markets;
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
    public HeroRecord? Hero { get; set; }
    /// <summary>The unlocked Codex entries (saved in the "codex" section when present).</summary>
    public CodexProgress? Codex { get; init; }
    /// <summary>Coin, goods, stores and household needs (saved in the "economy" section when present). Created by <see cref="EconomyState.Create"/> at world creation.</summary>
    public EconomyState? Economy { get; init; }
    /// <summary>Field, parcel and herd state of farming (saved in the "farming" section when present); needs <see cref="Economy"/> for the stores it owns.</summary>
    public Ravensguard.Sim.Farming.FarmingState? Farming { get; set; }
    /// <summary>Businesses, mines and resource stocks (saved in the "production" section when present); needs <see cref="Economy"/>.</summary>
    public ProductionSystem? Production { get; set; }
    /// <summary>Cottages, fields and businesses the abstract world has built since generation, with the builds in progress (saved in the "growth" section when it has grown; replayed over the regenerated region on load).</summary>
    public Ravensguard.Sim.Growth.GrowthState? Growth { get; set; }
    /// <summary>Market books, lots and merchants (saved in the "markets" section when present); needs <see cref="Production"/> for business parties.</summary>
    public MarketSystem? Markets { get; set; }
    /// <summary>The odd jobs posted so far, with the reservations they hold (saved in the "jobs" section once any exists); the coin they escrow is in the <see cref="Economy"/> ledger.</summary>
    public Ravensguard.Sim.Jobs.JobTable? Jobs { get; set; }
    /// <summary>Unfinished crafts, skills by use and what people know how to make (saved in the "crafting" section once anyone has crafted); the components of a job are in its store in the <see cref="Economy"/>.</summary>
    public Ravensguard.Sim.Crafting.CraftState? Crafting { get; set; }
    /// <summary>Animals that are individuals, vehicles with their cargo, horse herds, loans and the hero's ride (saved in the "mounts" section once any exists).</summary>
    public Ravensguard.Sim.Animals.RideBook? Mounts { get; set; }
    /// <summary>Journey, camps, snares, carcasses and the interrupt log of travel (saved in the "travel" section once anything has been recorded).</summary>
    public Ravensguard.Sim.Travel.TravelState? Travel { get; set; }
    /// <summary>Directives, limits, interrupt policy and the log of the hero's and party members' autopilot (saved in the "autopilot" section once anyone turned it on).</summary>
    public Ravensguard.Sim.Autopilot.AutopilotBook? Autopilot { get; set; }
    /// <summary>Rumours and who holds them, relations between people, the hero's heard rumours and dialogue flags (saved in the "talk" section once anything has been recorded).</summary>
    public Ravensguard.Sim.Rumours.TalkState? Talk { get; set; }
    /// <summary>The first hour: mentor, jobs, step states and the loan (saved in the "onboarding" section once a new game has set it up).</summary>
    public Ravensguard.Sim.Onboarding.OnboardingBook? Onboarding { get; set; }
    /// <summary>Everyone's wounds, blood and pain (saved in the "wounds" section while anybody is hurt); shared by the live and the abstract tier.</summary>
    public Ravensguard.Sim.Combat.Wounds.WoundLedger? Wounds { get; set; }
    /// <summary>A fight in progress (P4-C, overview 8.3): saved in the "fight" section only mid-fight, so a load resumes it at the same FightTick. <see cref="FightSource"/>, when set by the game, is read at a save instead.</summary>
    public FightRecord? Fight { get; set; }
    public Func<FightRecord?>? FightSource { get; set; }
    /// <summary>P4-D (feedback-flavour-log): the log of the fight in progress or the last one, in the optional "fightlog" section (no format bump); <see cref="FightLogSource"/>, when set by the game, is read at a save.</summary>
    public FightLogSave? FightLog { get; set; }
    public Func<FightLogSave?>? FightLogSource { get; set; }
    /// <summary>P4-D: everyone held captive (saved in the "captivity" section once anyone has been taken).</summary>
    public Ravensguard.Sim.Combat.Aftermath.CaptiveBook? Captivity { get; set; }
    /// <summary>P4-D: estates and inheritance records of the run, and whether it has ended (saved in the "succession" section once anyone has died as the hero).</summary>
    public Ravensguard.Sim.Succession.SuccessionBook? Succession { get; set; }
    /// <summary>P4-F: the watch settings (stances by person, keep watching, take-back, the speed cap), in the optional "watch" section once anything is set (no format bump).</summary>
    public Ravensguard.Sim.Combat.Watch.WatchBook? Watch { get; set; }
    /// <summary>P4-H: the sites history and play have touched (snapshots, deltas, the crawl in progress), in the optional "sites" section (no format bump).</summary>
    public Ravensguard.Sim.Sites.SiteBook? Sites { get; set; }
    /// <summary>P4-H: while the hero is underground the game captures the book with the live crawl (under the book's lock).</summary>
    public Func<SiteSave?>? SiteSource { get; set; }
    /// <summary>P5-A2: deeds play changed, claims, founding charters (land spec 3.2), in the optional "land" section once anything is recorded (no format bump).</summary>
    public Ravensguard.Sim.Land.LandBook? Land { get; set; }
    /// <summary>P5-B: building sites, ghosts, built cells and their structure ids, groans (build-mode-and-construction 3.2, 3.3), in the optional "construction" section once anything is recorded (no format bump).</summary>
    public Ravensguard.Sim.Construction.ConstructionBook? Construction { get; set; }
    /// <summary>P5-C: the player's own building plans this save knows (their text), in the optional "designs" section once any is saved or placed (no format bump).</summary>
    public Ravensguard.Sim.Construction.DesignBook? Designs { get; set; }
    /// <summary>P5-F: the hero's businesses, shares, stewards and stalls, his prospecting knowledge and survey reports, mining licences (businesses spec 3, prospecting spec 3), in the optional "ownership" section once anything is recorded (no format bump).</summary>
    public Ravensguard.Sim.Production.OwnershipBook? Ownership { get; set; }
    /// <summary>P5-H: every will made (inheritance 3), in the optional "wills" section once any exists (no format bump).</summary>
    public Ravensguard.Sim.Succession.WillBook? Wills { get; set; }
    /// <summary>P6-A3: counted deeds, standing rows, renown and marks (reputation spec 3), in the optional "standing" section once any exists (no format bump).</summary>
    public Ravensguard.Sim.Society.StandingBook? Standing { get; set; }
    /// <summary>The hero's side of the records (trade-inventory-character spec): worn slots and where each stack sits, the price notebook, the party, the people known (saved in the "party" section when present). The things themselves are item rows, the pack store and the purse in the <see cref="Economy"/>.</summary>
    public Ravensguard.Sim.Trade.HeroParty? Party { get; set; }
    /// <summary>
    /// The hero's commands waiting for the barrier of their hour (review finding 6): read from a save into <see cref="HeroPending"/> for the desk to take up; while a desk runs, <see cref="HeroPendingSource"/>
    /// (the desk's own queue, read under its lock) is what a save writes. Saved in the "hero.pending" section when any wait.
    /// </summary>
    public List<Ravensguard.Sim.Trade.HeroCommand> HeroPending { get; set; } = [];
    public Func<IReadOnlyList<Ravensguard.Sim.Trade.HeroCommand>>? HeroPendingSource { get; set; }
    /// <summary>The abstract continent: 256 summaries, roads, caravans (saved in the "continent" section; null for a world that has none yet, which gets a fresh one when it runs).</summary>
    public Ravensguard.Sim.Continental.ContinentState? ContinentState { get; set; }
    /// <summary>The home region's records as the running game holds them (set by <see cref="WorldRuntime"/>, or by the code that built a new game's economy so the runtime adopts them as they are).</summary>
    public RegionWorld? HomeRecords { get; set; }
    /// <summary>Content, for regenerating the baseline people a save is diffed against (spec N24); null: people are saved whole.</summary>
    public ContentDatabase? Content { get; set; }
    /// <summary>The records of every other generated region (resident and paged), when the game runs the seamless continent. Saved as one section per region.</summary>
    public RegionRecordSet? Regions { get; set; }
    /// <summary>Sections of other regions read from a save, as stored (hand them to <see cref="RegionRecordSet.AttachSections"/>); re-saved unchanged when no <see cref="Regions"/> is attached.</summary>
    public IReadOnlyDictionary<int, byte[]> RegionSections { get; set; } = new SortedDictionary<int, byte[]>();
    /// <summary>The grid square of the embark (home) region.</summary>
    public RegionCoord HomeRegion => RegionCoord.Snap(EmbarkCellX, EmbarkCellY);
    /// <summary>The abstract tick's progress and vital counters (saved in the "abstract" section when present); attached by <c>SimWorld.From</c>.</summary>
    public Ravensguard.Sim.Abstract.AbstractState? Abstract { get; set; }
    /// <summary>Live play state restored from a save made with <see cref="WorldSave.CaptureLive"/> (hero position, torch, explored tiles...), null for a save without it.
    /// <c>new LiveWorld(streamer, state.Live, ...)</c> resumes there; <see cref="Clock"/> already holds the saved live clock.</summary>
    public LiveResume? Live { get; init; }
    public ulong Seed => ContinentSettings.Seed;
    /// <summary>
    /// The content id tables this world saves (the "tables" section, <see cref="IdTablesSave"/>) when <see cref="Content"/> is not set: a restored world gets the tables of the content it was
    /// loaded with, for the tables its save named. Null: a world built without content saves its economy's goods table only.
    /// </summary>
    public IdTables? IdTables { get; set; }
    /// <summary>What the load did to fit the save to the content (<see cref="ContentRemap"/>): goods, recipes and other ids mapped by name, and anything written off. Empty when nothing changed.</summary>
    public IReadOnlyList<string> LoadNotes { get; set; } = [];
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
    /// <summary>Hash of the regenerated home region the saved people are diffed against (<see cref="BaselineGuard"/>); empty when the people are stored whole, and in format v1.</summary>
    [Key(15)] public string BaselineHash { get; set; } = "";
    /// <summary>Region generator version of the save (<see cref="SaveFormat.RegionGeneratorVersion"/>); 0 in format v1.</summary>
    [Key(16)] public int GeneratorVersion { get; set; }
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
    /// <summary>Region id of the hero plus one (0 = not stored, an older save): the region the hero stands in, which changes as they cross a border.</summary>
    [Key(6)] public int RegionPlus1 { get; set; }
    /// <summary>The walk in progress (<see cref="HeroMotion"/>): route as region-cell (x, y, level) triples, next index, stuck counter, portal lock (empty or a triple), gait. Empty in older saves.</summary>
    [Key(7)] public int[] PathXyz { get; set; } = [];
    [Key(8)] public int PathIdx { get; set; }
    [Key(9)] public int PathStuck { get; set; }
    [Key(10)] public int[] PortalLock { get; set; } = [];
    [Key(11)] public byte Mode { get; set; }
    /// <summary>The hero's core temperature, exposure counters and the wetness of the clothes worn; null in older saves and in worlds without the clothing model.</summary>
    [Key(12)] public HeroBodySave? Body { get; set; }
    /// <summary>The speed the hero asked for, plus one (0: not stored, an older save: 8x). Review finding 6.</summary>
    [Key(13)] public byte WantedPlus1 { get; set; }
}

/// <summary>Save form of <see cref="Ravensguard.Sim.Clothing.HeroBody"/>.</summary>
[MessagePackObject]
public sealed class HeroBodySave
{
    [Key(0)] public long CoreU { get; set; }
    [Key(1)] public int ExposureMin { get; set; }
    [Key(2)] public int SevereMin { get; set; }
    [Key(3)] public int HeatMin { get; set; }
    [Key(4)] public bool Collapsed { get; set; }
    [Key(5)] public byte Died { get; set; }
    [Key(6)] public long LastMinute { get; set; }
    [Key(7)] public long WearDay { get; set; }
    [Key(8)] public string[] WornShapes { get; set; } = [];
    [Key(9)] public int[] WornWetU { get; set; } = [];

    public static HeroBodySave? Of(Ravensguard.Sim.Clothing.HeroBody? b) => b == null ? null : new()
    {
        CoreU = b.CoreU, ExposureMin = b.ExposureMin, SevereMin = b.SevereMin, HeatMin = b.HeatMin, Collapsed = b.Collapsed, Died = (byte)b.Died, LastMinute = b.LastMinute, WearDay = b.WearDay,
        WornShapes = [.. b.WornShapes], WornWetU = [.. b.WornWetU],
    };

    public Ravensguard.Sim.Clothing.HeroBody ToBody()
    {
        if (WornShapes.Length != WornWetU.Length) throw new SaveException("Corrupt 'live' section in save: the hero's worn clothes and their wetness do not match.");
        return new(CoreU, ExposureMin, SevereMin, HeatMin, Collapsed, (Ravensguard.Sim.Clothing.ExposureDeath)Died, LastMinute, WearDay, [.. WornShapes], [.. WornWetU]);
    }
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
    /// <summary>Rows whose person id was made in another region (arrivals), with those ids; empty for a region nobody moved into.</summary>
    [Key(41)] public int[] ForeignRow { get; set; } = [];
    [Key(42)] public ulong[] ForeignId { get; set; } = [];
    /// <summary>Set instead of the columns above (all empty) when the section holds only what differs from the regenerated baseline people; see <see cref="PeopleDiffer"/>.</summary>
    [Key(43)] public PeopleDiff? Diff { get; set; }
    /// <summary>Kin links to people of other regions (a mover whose parent or spouse stayed behind): row, link (<see cref="KinLink"/>) and the other person's id.</summary>
    [Key(44)] public int[] KinRow { get; set; } = [];
    [Key(45)] public byte[] KinLink { get; set; } = [];
    [Key(46)] public ulong[] KinId { get; set; } = [];
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
public static partial class WorldSave
{
    public const string MetaSection = "meta", PeopleSection = "people", LotsSection = "region.lots", ChunksSection = ChunkStore.SectionName, ChronicleSection = "chronicle", HeroSection = "hero", CodexSection = "codex", LiveSection = "live", EconomySection = "economy", AbstractSection = "abstract", FarmingSection = "farming", ProductionSection = ProductionSave.SectionName, ContinentSection = "continent", GrowthSection = "growth", JobsSection = "jobs", CraftingSection = "crafting", PartySection = "party", MarketsSection = MarketsSave.SectionName, RegionSectionName = "region.";
    public static string RegionSection(int id) => RegionSectionName + id.ToString("D3");

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

    /// <summary>The same with a live state taken earlier (on the main thread), so the copy of the world itself can run on the tick worker at a barrier (<see cref="WorldRuntime.AtBarrier{T}"/>).</summary>
    public static LiveCapture SnapshotLive(WorldState world, LiveResume resume, string contentHash = "", string gameVersion = "0.1", IReadOnlyList<ModEntry>? mods = null, bool ironman = false)
        => Snapshot(world, resume, contentHash, gameVersion, mods, ironman);

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
        // one consistent view of the other regions (resident, paged, which chunk entries are whose); see RegionRecordSet.CaptureView
        var view = w.Regions?.CaptureView();
        var liveTier = CaptureLiveTier(w);
        var rides = CaptureRides(w);
        return new LiveCapture
        {
            Header = header, Meta = meta, Continent = w.Continent, History = w.History, Region = w.Population.Region,
            People = EncodePeople(w.Population), Lots = EncodeLots(w.Population), Chunks = view != null ? w.Chunks.Snapshot(view.InMainChunkSection) : w.Chunks.Snapshot(), Chronicle = EncodeChronicle(w.Chronicle),
            Baseline = w.Content == null ? null : () => Populator.Populate(w.Population.Region, w.History, w.Content),
            Growth = (w.Growth ?? w.HomeRecords?.Economy.Growth.State) is { IsEmpty: false } gs ? GrowthSave.Capture(gs) : null,
            Jobs = w.Jobs is { IsEmpty: false } jt ? JobsSave.Capture(jt) : null,   // counters included (review finding 5)
            Crafting = w.Crafting is { IsEmpty: false } cr ? CraftingSave.Capture(cr) : null,
            Mounts = rides.Mounts,
            Travel = w.Travel is { IsEmpty: false } tv ? TravelSave.Capture(tv) : null,
            Autopilot = w.Autopilot is { IsEmpty: false } ap ? AutopilotSave.Capture(ap) : null,
            Talk = w.Talk is { IsEmpty: false } tk ? TalkSave.Capture(tk) : null,
            Onboarding = w.Onboarding is { IsEmpty: false } ob ? OnboardingSave.Capture(ob) : null,
            Wounds = liveTier.Wounds, Fight = liveTier.Fight, FightLog = liveTier.FightLog,
            Captivity = w.Captivity is { Captives.Count: > 0 } cb ? CaptivitySave.Capture(cb) : null,                          // P4-D
            Succession = w.Succession is { } sb && (sb.Estates.Count > 0 || sb.RunEnded || sb.Kin.Count > 0) ? SuccessionSave.Capture(sb) : null,     // P4-D
            Watch = w.Watch is { IsEmpty: false } wb ? WatchSave.Capture(wb) : null,                                                // P4-F
            Sites = liveTier.Sites,                                                                                                    // P4-H
            Land = w.Land is { IsEmpty: false } lb ? LandSave.Capture(lb) : null,                                                    // P5-A2
            Construction = w.Construction is { IsEmpty: false } cnb ? ConstructionSave.Capture(cnb) : null,                          // P5-B
            Designs = w.Designs is { IsEmpty: false } dsb ? DesignsSave.Capture(dsb) : null,                                         // P5-C
            Wills = w.Wills is { IsEmpty: false } wlb ? WillsSave.Capture(wlb) : null,                                                // P5-H
            Standing = w.Standing is { IsEmpty: false } stb ? StandingSave.Capture(stb) : null,                                      // P6-A3
            Ownership = w.Ownership is { IsEmpty: false } owb ? OwnershipSave.Capture(owb, w.Economy?.Goods.Hash() ?? "") : null,         // P5-F
            Party = w.Party == null ? null : Ravensguard.Sim.Trade.HeroPartySave.Capture(w.Party),
            HeroPending = (w.HeroPendingSource?.Invoke() ?? w.HeroPending) is { Count: > 0 } hp ? Ravensguard.Sim.Trade.HeroPendingSave.Capture(hp) : null,
            Production = w.Production == null ? null : ProductionSave.Capture(w.Production), Markets = w.Markets == null ? null : MarketsSave.Capture(w.Markets),
            ContinentSt = w.ContinentState == null ? null : ContinentSave.Capture(w.ContinentState),                                // the abstract continent and its caravans (resume-drift fix 1: it was never written)
            Regions = view?.CaptureResident(), PagedRegions = view != null ? view.Paged : new SortedDictionary<int, byte[]>(w.RegionSections.ToDictionary(kv => kv.Key, kv => kv.Value)),
            Tables = (w.Content != null ? IdTables.Of(w.Content).Tables : w.IdTables ?? (w.Economy != null ? IdTables.OfGoods(w.Economy.Goods) : new IdTables())).ToSave(),
            Hero = w.Hero?.ToSave(), Economy = rides.Economy, Abstract = w.Abstract == null ? null : AbstractSave.Capture(w.Abstract, w.Population), Farming = w.Farming == null ? null : FarmingSave.Capture(w.Farming), Codex = codex == null ? null : new CodexSave { Unlocked = codex.Unlocked.ToArray() },
            Live = live == null ? null : new LiveSave
            {
                WorldXRaw = live.WorldXRaw, WorldYRaw = live.WorldYRaw, Z = live.Z, Facing = (byte)live.Facing, Torch = live.Torch, Explored = live.Explored.ToBytes(),
                RegionPlus1 = HeroRegionOf(w, live) is { IsValid: true } hr ? hr.Id + 1 : 0,
                PathXyz = live.Motion?.PathXyz ?? [], PathIdx = live.Motion?.PathIdx ?? 0, PathStuck = live.Motion?.Stuck ?? 0, PortalLock = live.Motion?.PortalLock ?? [], Mode = (byte)(live.Motion?.Mode ?? MoveMode.Jog),
                Body = HeroBodySave.Of(live.Body), WantedPlus1 = (byte)((int)live.Wanted + 1),
            },
        };
    }

    /// <summary>
    /// The ride book and the economy's stores, read together under the ride book's lock (P5-I review, finding 2). A save copies the world on the tick worker while the main thread goes on
    /// stepping, and the live tier moves the hero's horse and vehicle, unloads a bed into the heap (both economy stores when the book is hosted), takes back a loaned animal and runs the
    /// animals' hours under that lock (<see cref="Ravensguard.Sim.Animals.RideBook.Gate"/>); unlocked, a save could copy a torn vehicle row or fail with "collection was modified".
    /// The ride book's lock is a leaf on the main thread (nothing else is taken while it is held), so taking it here after the live-tier locks cannot deadlock.
    /// </summary>
    public static (MountsSave? Mounts, EconomySave? Economy) CaptureRides(WorldState w) => CaptureRides(w.Mounts, w.Economy);

    /// <summary><see cref="CaptureRides(WorldState)"/> on the two books themselves.</summary>
    public static (MountsSave? Mounts, EconomySave? Economy) CaptureRides(Ravensguard.Sim.Animals.RideBook? rides, EconomyState? economy)
    {
        lock (rides?.Gate ?? RideFree)
            return (rides is { IsEmpty: false } rb ? MountsSave.Capture(rb) : null, economy == null ? null : EconomySave.Capture(economy));
    }
    private static readonly object RideFree = new();

    /// <summary>
    /// The parts of a save the live tier changes every 20 Hz step while a fight or a crawl runs: the wound ledger, the fight, its log and the sites with the crawl. A save copies the world on the
    /// tick worker while the main thread goes on stepping, so they are read together under the sites book's lock and then the ledger's (the order the crawl's step takes them), which the
    /// live step holds (<see cref="Ravensguard.Sim.Combat.Melee2.FightWorld.StepShared"/>): the four sections show one and the same tick (review finding 1).
    /// </summary>
    public static (WoundSave? Wounds, FightRecord? Fight, FightLogSave? FightLog, SiteSave? Sites) CaptureLiveTier(WorldState w)
    {
        object? siteLock = w.Sites?.Sync, woundLock = w.Wounds?.Sync;
        bool siteTaken = false, woundTaken = false;
        try
        {
            if (siteLock != null) Monitor.Enter(siteLock, ref siteTaken);
            if (woundLock != null) Monitor.Enter(woundLock, ref woundTaken);
            var wounds = w.Wounds is { IsEmpty: false } wl ? WoundSave.Capture(wl) : null;
            var fight = w.FightSource != null ? w.FightSource() : w.Fight;
            var log = w.FightLogSource != null ? w.FightLogSource() : w.FightLog;
            var sites = w.SiteSource != null ? w.SiteSource() : w.Sites is { IsEmpty: false } sbk ? SiteSave.Capture(sbk) : null;
            return (wounds, fight, log, sites);
        }
        finally
        {
            if (woundTaken) Monitor.Exit(woundLock!);
            if (siteTaken) Monitor.Exit(siteLock!);
        }
    }

    // ---------- restore ----------

    /// <summary>Rebuilds the world from seed + content, then applies the stored sections. Throws <see cref="SaveException"/> with a readable message on any mismatch.</summary>
    public static WorldState Restore(SaveFile file, ContentDatabase content, string currentContentHash, IReadOnlyList<ModEntry>? installedMods = null,
        Continent? knownContinent = null, WorldHistory? knownHistory = null)
    {
        // a save made with other content loads (plan 2.11): the layers it regenerates are verified below by their hashes, and the content ids by ContentRemap; Inspect reports both first
        var report = new SaveReader().CheckCompatibility(file.Header, currentContentHash, installedMods ?? file.Header.Mods);
        if (!report.CanLoad) throw new SaveException(report.ToText().Trim());
        // every saved content index (goods, recipes, proficiencies, business kinds...) onto this content by name, before any section is read (P5 goods-table fix)
        var remap = ContentRemap.Apply(file, content);
        file = remap.File;

        var meta = Decode<WorldMeta>(file, MetaSection, compressed: false);
        var cs = JsonSerializer.Deserialize<ContinentSettings>(meta.ContinentSettingsJson, Json) ?? throw new SaveException("Corrupt continent settings in save.");
        var hs = JsonSerializer.Deserialize<HistorySettings>(meta.HistorySettingsJson, Json) ?? throw new SaveException("Corrupt history settings in save.");
        var rs = JsonSerializer.Deserialize<RegionSettings>(meta.RegionSettingsJson, Json) ?? throw new SaveException("Corrupt region settings in save.");

        // A world library supplies the continent and history it already generated (and hashed) so they are not regenerated twice; hashes are still verified.
        var continent = knownContinent ?? ContinentGenerator.Generate(cs);
        Verify("continent", meta.ContinentHash, LayerHashes.Of(continent));
        var history = knownHistory ?? HistoryGenerator.Generate(continent, hs);
        Verify("history", meta.HistoryHash, LayerHashes.Of(history));
        if (meta.EmbarkCellX % RegionCoord.CellsPerSide != 0 || meta.EmbarkCellY % RegionCoord.CellsPerSide != 0)
            throw new SaveException($"This save embarked on a free 16 x 16 cell window at ({meta.EmbarkCellX}, {meta.EmbarkCellY}); embark regions now snap to the 16 x 16 region grid so neighbouring regions tile exactly, "
                + "and world generation changed with it. The world cannot be regenerated from this save.");
        var region = RegionGenerator.Generate(continent, history, meta.EmbarkCellX, meta.EmbarkCellY, rs);
        Verify("region", meta.RegionHash, LayerHashes.Of(region));
        // the landmark phase is a pure function of region, history and content, so only its hash is stored (absent in older saves)
        var marks = LandmarkPlacer.Place(region, history, content);
        string markHash = LayerHashes.OfLandmarks(marks);
        if (meta.LandmarkHash.Length > 0) Verify("landmark", meta.LandmarkHash, markHash);

        // what the abstract world built since generation goes back over the freshly generated region before anything reads its plans, parcels and fields
        Ravensguard.Sim.Growth.GrowthState? growth = null;
        if (file.Sections.ContainsKey(GrowthSection)) { growth = Decode<GrowthSave>(file, GrowthSection, compressed: true).Restore(); Ravensguard.Sim.Growth.RegionGrowth.Replay(region, growth); }
        var peopleData = Decode<PeopleData>(file, PeopleSection, true);
        if (peopleData.Diff != null)
        {
            // saved as a difference from the people the generator makes again for this region (note N24)
            if (content == null) throw new SaveException("The people of this save are stored as a difference from the generated ones; the game content is needed to generate them again.");
            var basePop = Populator.Populate(region, history, content);
            BaselineGuard.Verify("The home region", meta.BaselineHash, meta.GeneratorVersion, basePop);
            peopleData = PeopleDiffer.Expand(peopleData, EncodePeople(basePop));
        }
        var pop = DecodePeople(peopleData, Decode<RegionLotsData>(file, LotsSection, true), region);
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
            if (ls.PathXyz.Length % 3 != 0 || ls.PortalLock.Length is not (0 or 3) || !Enum.IsDefined((MoveMode)ls.Mode))
                throw new SaveException("Corrupt 'live' section in save: the hero's walk in progress is malformed.");
            var motion = new HeroMotion(ls.PathXyz, ls.PathIdx, ls.PathStuck, ls.PortalLock, (MoveMode)ls.Mode);
            resume = new LiveResume(ls.WorldXRaw, ls.WorldYRaw, ls.Z, (Facing)ls.Facing, ls.Torch, new GameTime(meta.ClockSeconds), meta.ClockMsRemainder, (SimSpeed)meta.ClockSpeed,
                ExploredTiles.FromBytes(ls.Explored), codex, Motion: motion.IsIdle ? HeroMotion.Idle : motion, Body: ls.Body?.ToBody(), Wanted: ls.WantedPlus1 == 0 ? SimSpeed.X8 : (SimSpeed)(ls.WantedPlus1 - 1));
            var home = RegionCoord.Snap(meta.EmbarkCellX, meta.EmbarkCellY);
            var heroRegion = HeroRegionTracker.OfFrameCell(home, resume.Cell.X, resume.Cell.Y);
            if (ls.RegionPlus1 > 0 && (!heroRegion.IsValid || heroRegion.Id != ls.RegionPlus1 - 1))
                throw new SaveException($"Corrupt 'live' section in save: the hero is stored in region {ls.RegionPlus1 - 1} but stands in {heroRegion}.");
            resume = resume with { RegionId = heroRegion.IsValid ? heroRegion.Id : -1 };
        }
        AbstractState? abstractState = file.Sections.ContainsKey(AbstractSection) ? Decode<AbstractSave>(file, AbstractSection, compressed: true).Restore(pop) : null;
        EconomyState? economy = file.Sections.ContainsKey(EconomySection) ? Decode<EconomySave>(file, EconomySection, compressed: true).Restore(pop, content) : null;
        ProductionSystem? production = null; MarketSystem? markets = null;
        if (economy != null && file.Sections.ContainsKey(ProductionSection))
        {
            production = Decode<ProductionSave>(file, ProductionSection, compressed: true).Restore(economy, pop, ProductionContent.Load(content, economy.Goods), cs.Seed, markets = new MarketSystem(economy, pop, cs.Seed));
            if (file.Sections.ContainsKey(MarketsSection)) Decode<MarketsSave>(file, MarketsSection, compressed: true).Restore(markets, production);
        }
        var regionSections = new SortedDictionary<int, byte[]>();
        foreach (var (name, bytes) in file.Sections)
            if (name.StartsWith(RegionSectionName, StringComparison.Ordinal) && int.TryParse(name.AsSpan(RegionSectionName.Length), out int rid)) regionSections[rid] = bytes.ToArray();
        var restored = new WorldState
        {
            Production = production, Markets = markets, RegionSections = regionSections, Growth = growth,
            Jobs = file.Sections.ContainsKey(JobsSection) ? Decode<JobsSave>(file, JobsSection, compressed: true).Restore() : null,
            Crafting = file.Sections.ContainsKey(CraftingSection) ? Decode<CraftingSave>(file, CraftingSection, compressed: true).Restore() : null,
            Mounts = file.Sections.ContainsKey(MountsSave.SectionName) ? RestoreMounts(Decode<MountsSave>(file, MountsSave.SectionName, compressed: true), content, economy, cs.Seed, file) : null,
            Autopilot = file.Sections.ContainsKey(AutopilotSave.SectionName) ? Decode<AutopilotSave>(file, AutopilotSave.SectionName, compressed: true).Restore() : null,
            Travel = file.Sections.ContainsKey(TravelSave.SectionName) ? RestoreTravel(Decode<TravelSave>(file, TravelSave.SectionName, compressed: true), content, economy, cs.Seed) : null,
            Talk = file.Sections.ContainsKey(TalkSave.SectionName) ? Decode<TalkSave>(file, TalkSave.SectionName, compressed: true).Restore() : null,
            Onboarding = file.Sections.ContainsKey(OnboardingSave.SectionName) ? Decode<OnboardingSave>(file, OnboardingSave.SectionName, compressed: true).Restore() : null,
            Wounds = file.Sections.ContainsKey(WoundSave.SectionName) ? Decode<WoundSave>(file, WoundSave.SectionName, compressed: true).Restore() : null,
            Fight = file.Sections.ContainsKey(FightRecord.SectionName) ? Decode<FightRecord>(file, FightRecord.SectionName, compressed: true) : null,
            FightLog = file.Sections.ContainsKey(FightLogSave.SectionName) ? Decode<FightLogSave>(file, FightLogSave.SectionName, compressed: true) : null,
            Captivity = file.Sections.ContainsKey(CaptivitySave.SectionName) ? Decode<CaptivitySave>(file, CaptivitySave.SectionName, compressed: true).Restore() : null,          // P4-D
            Succession = file.Sections.ContainsKey(SuccessionSave.SectionName) ? Decode<SuccessionSave>(file, SuccessionSave.SectionName, compressed: true).Restore() : null,    // P4-D
            Watch = file.Sections.ContainsKey(WatchSave.SectionName) ? Decode<WatchSave>(file, WatchSave.SectionName, compressed: true).Restore() : null,                      // P4-F
            Sites = file.Sections.ContainsKey(SiteSave.SectionName) ? Decode<SiteSave>(file, SiteSave.SectionName, compressed: true).Restore() : null,                          // P4-H
            Land = file.Sections.ContainsKey(LandSave.SectionName) ? Decode<LandSave>(file, LandSave.SectionName, compressed: true).Restore() : null,                          // P5-A2
            Construction = file.Sections.ContainsKey(ConstructionSave.SectionName) ? Decode<ConstructionSave>(file, ConstructionSave.SectionName, compressed: true).Restore() : null, // P5-B
            Designs = file.Sections.ContainsKey(DesignsSave.SectionName) ? Decode<DesignsSave>(file, DesignsSave.SectionName, compressed: true).Restore() : null,                // P5-C
            Wills = file.Sections.ContainsKey(WillsSave.SectionName) ? Decode<WillsSave>(file, WillsSave.SectionName, compressed: true).Restore() : null,                       // P5-H
            Standing = file.Sections.ContainsKey(StandingSave.SectionName) ? Decode<StandingSave>(file, StandingSave.SectionName, compressed: true).Restore() : null,          // P6-A3
            Ownership = file.Sections.ContainsKey(OwnershipSave.SectionName) ? Decode<OwnershipSave>(file, OwnershipSave.SectionName, compressed: true).Restore() : null,          // P5-F
            Party = file.Sections.ContainsKey(PartySection) ? Decode<Ravensguard.Sim.Trade.HeroPartySave>(file, PartySection, compressed: true).Restore() : null,
            HeroPending = file.Sections.ContainsKey(Ravensguard.Sim.Trade.HeroPendingSave.SectionName) ? Decode<Ravensguard.Sim.Trade.HeroPendingSave>(file, Ravensguard.Sim.Trade.HeroPendingSave.SectionName, compressed: true).Restore() : [],
            Hero = hero, Codex = codex, Live = resume, Economy = economy, Abstract = abstractState,
            Farming = file.Sections.ContainsKey(FarmingSection) ? Decode<FarmingSave>(file, FarmingSection, compressed: true).Restore(region) : null,
            ContinentState = file.Sections.ContainsKey(ContinentSection) ? Decode<ContinentSave>(file, ContinentSection, compressed: true).Restore(cs.Seed, history) : null,
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = meta.EmbarkCellX, EmbarkCellY = meta.EmbarkCellY,
            Continent = continent, History = history, Population = pop,
            Clock = new SimClock(new GameTime(meta.ClockSeconds), meta.ClockMsRemainder) { Speed = (SimSpeed)meta.ClockSpeed },
            Ids = IdAllocator.FromState(meta.IdState), Chunks = chunks, Chronicle = chronicle,
            IdTables = remap.Tables, LoadNotes = remap.Notes,
        };
        // P5-A4: a migrated v7 save's hero gets the person row now (the v8 step marks it pending); a v8 save holds it in the people diff already, and its status in the
        // abstract section's vital columns; a bare world without that section (tools, test fixtures) has only the hero section's index to say which row is the hero's
        if (hero is { PersonIndex: >= 0 } hp && hp.PersonIndex < pop.People.Count && abstractState == null && pop.People.Status[hp.PersonIndex] == PersonStatus.Alive)
            pop.People.Status[hp.PersonIndex] = PersonStatus.Hero;
        if (hero is { PersonIndex: Ravensguard.Sim.Succession.HeroPerson.Pending } && content != null)
            hero.PersonIndex = Ravensguard.Sim.Succession.HeroPerson.InsertForLoad(pop, economy, content, hero, Ravensguard.Sim.Economy.EconomyState.HeroOwner, cs.Seed,
                (int)(meta.ClockSeconds / 86400), restored.Succession?.Kin ?? [], restored.Ids);
        return restored;
    }

    /// <summary>The region the hero stands in (the streaming frame's origin is the home region's north-west corner; cells are metres).</summary>
    internal static RegionCoord HeroRegionOf(WorldState w, LiveResume live)
    {
        var c = live.Cell;
        return HeroRegionTracker.OfFrameCell(w.HomeRegion, c.X, c.Y);
    }

    private static void Verify(string layer, string stored, string actual)
    {
        if (!string.Equals(stored, actual, StringComparison.Ordinal))
            throw new SaveException($"This save cannot be loaded: the regenerated {layer} differs from the one the save was made with "
                + "(the world generator or game content changed since). Load it with the game version that made it.");
    }

    /// <summary>The ride book of a save: on the economy's stores when the world has one (their goods are in its section), else on a private goods catalog and ledger (a bare test world).</summary>
    private static Ravensguard.Sim.Animals.RideBook RestoreMounts(MountsSave m, ContentDatabase? content, EconomyState? economy, ulong seed, SaveFile file)
    {
        if (content == null) throw new SaveException("The mounts of this save need the game content to be read.");
        // P5-G: vehicles of the player's own designs need the designs section first (its texts are read again for the world below)
        var designs = file.Sections.ContainsKey(DesignsSave.SectionName) ? Decode<DesignsSave>(file, DesignsSave.SectionName, compressed: true).Restore() : null;
        if (economy != null) return m.Restore(content, economy, seed, designs);
        var goods = new Ravensguard.Sim.Goods.GoodCatalog(content);
        return m.Restore(content, goods, new Ravensguard.Sim.Goods.MassLedger(goods.Count), seed, designs);
    }

    /// <summary>The travel state of a save: on the economy's stores when the world has one, else on a private goods catalog and ledger.</summary>
    private static Ravensguard.Sim.Travel.TravelState RestoreTravel(TravelSave m, ContentDatabase? content, EconomyState? economy, ulong seed)
    {
        if (content == null) throw new SaveException("The travel records of this save need the game content to be read.");
        Ravensguard.Sim.Travel.TravelState t;
        if (economy != null) { t = new Ravensguard.Sim.Travel.TravelState(content, seed, economy.Goods, economy.Mass) { Hosted = true, OpenStore = id => economy.OpenStore(Ravensguard.Sim.Goods.StoreKind.Open, id) }; }
        else { var goods = new Ravensguard.Sim.Goods.GoodCatalog(content); t = new Ravensguard.Sim.Travel.TravelState(content, seed, goods, new Ravensguard.Sim.Goods.MassLedger(goods.Count)); }
        m.Restore(t);
        return t;
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

    public static PeopleData EncodePeople(Population pop)
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
            ForeignRow = p.ForeignIds.Keys.ToArray(), ForeignId = p.ForeignIds.Values.Select(v => v.Value).ToArray(),
            KinRow = p.ForeignKin.Select(k => k.Row).ToArray(), KinLink = p.ForeignKin.Select(k => (byte)k.Link).ToArray(), KinId = p.ForeignKin.Select(k => k.Id.Value).ToArray(),
        };
    }

    internal static RegionLotsData EncodeLots(Population p) => new()
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

    internal static Population DecodePeople(PeopleData d, RegionLotsData l, RegionData region)
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

        for (int i = 0; i < d.ForeignRow.Length; i++) p.SetForeignId(d.ForeignRow[i], new EntityId(d.ForeignId[i]));
        for (int i = 0; i < d.KinRow.Length; i++) p.SetForeignKin(d.KinRow[i], (People.KinLink)d.KinLink[i], new EntityId(d.KinId[i]));
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

    internal static ChronicleData EncodeChronicle(Chronicle c)
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

    internal static Chronicle DecodeChronicle(ChronicleData d)
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
    /// <summary>Makes the baseline people the home region's people are diffed against; null when the content is not at hand (people are then saved whole).</summary>
    internal Func<Population>? Baseline { get; init; }
    internal RegionLotsData Lots { get; init; } = null!;
    internal ChunkStoreSnapshot Chunks { get; init; } = null!;
    internal ChronicleData Chronicle { get; init; } = null!;
    internal HeroSave? Hero { get; init; }
    internal CodexSave? Codex { get; init; }
    internal LiveSave? Live { get; init; }
    internal EconomySave? Economy { get; init; }
    internal AbstractSave? Abstract { get; init; }
    internal FarmingSave? Farming { get; init; }
    internal ProductionSave? Production { get; init; }
    internal GrowthSave? Growth { get; init; }
    internal JobsSave? Jobs { get; init; }
    internal CraftingSave? Crafting { get; init; }
    internal MountsSave? Mounts { get; init; }
    internal TravelSave? Travel { get; init; }
    internal AutopilotSave? Autopilot { get; init; }
    internal TalkSave? Talk { get; init; }
    internal OnboardingSave? Onboarding { get; init; }
    internal WoundSave? Wounds { get; init; }
    internal FightRecord? Fight { get; init; }
    internal FightLogSave? FightLog { get; init; }
    internal CaptivitySave? Captivity { get; init; }
    internal SuccessionSave? Succession { get; init; }
    internal WatchSave? Watch { get; init; }
    internal SiteSave? Sites { get; init; }
    internal LandSave? Land { get; init; }                                                                                   // P5-A2
    internal ConstructionSave? Construction { get; init; }                                                                   // P5-B
    internal WillsSave? Wills { get; init; }                                                                                 // P5-H
    internal StandingSave? Standing { get; init; }                                                                           // P6-A3
    internal DesignsSave? Designs { get; init; }                                                                             // P5-C
    internal OwnershipSave? Ownership { get; init; }                                                                         // P5-F
    internal Ravensguard.Sim.Trade.HeroPartySave? Party { get; init; }
    internal Ravensguard.Sim.Trade.HeroPendingSave? HeroPending { get; init; }
    internal MarketsSave? Markets { get; init; }
    internal ContinentSave? ContinentSt { get; init; }
    internal IdTablesSave? Tables { get; init; }
    /// <summary>Captured sections of the resident regions other than home, and the stored sections of the paged ones.</summary>
    internal SortedDictionary<int, RegionSectionData>? Regions { get; init; }
    internal SortedDictionary<int, byte[]> PagedRegions { get; init; } = [];

    /// <summary>Chunk entries that had to be encoded by the last <see cref="Encode"/> (the rest were reused from earlier saves).</summary>
    public int ChunkEntriesEncoded => Chunks.ReEncoded;

    public SaveFile Encode()
    {
        var meta = Meta;
        // the generated layers never change after generation, so their hashes are computed once per object (at load verification, or the first save) and reused
        meta.ContinentHash = LayerHashes.Of(Continent); meta.HistoryHash = LayerHashes.Of(History); meta.RegionHash = LayerHashes.Of(Region);
        meta.LandmarkHash = Region.Landmarks == null ? "" : LayerHashes.OfLandmarks(Region.Landmarks); // empty: the landmark phase had not run, nothing to verify
        meta.PopulationHash = WorldSave.PopulationHashOf(People, Lots, Region);
        // the people and every region section go in as differences from the regenerated baseline (note N24); the baselines are made here, in parallel, off the main thread
        PeopleData people = People;
        if (Regions != null) Parallel.ForEach(Regions.Values, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(3, Environment.ProcessorCount - 1)) }, d => d.Compact());
        if (Baseline != null)
        {
            var bp = Baseline();
            people = PeopleDiffer.Compact(People, WorldSave.EncodePeople(bp));
            meta.BaselineHash = BaselineGuard.HashOf(bp);
        }
        meta.GeneratorVersion = SaveFormat.RegionGeneratorVersion;
        var sections = new List<KeyValuePair<string, byte[]>>
        {
            new(WorldSave.MetaSection, SaveCodec.Serialize(meta)),
            new(WorldSave.PeopleSection, SaveCodec.SerializeCompressed(people)),
            new(WorldSave.LotsSection, SaveCodec.SerializeCompressed(Lots)),
            new(WorldSave.ChunksSection, Chunks.Encode()),
            new(WorldSave.ChronicleSection, SaveCodec.SerializeCompressed(Chronicle)),
        };
        // additive sections: absent for worlds that have no hero, Codex or live state yet, so older saves and tests keep their bytes
        if (Tables != null) sections.Add(new(IdTablesSave.SectionName, SaveCodec.Serialize(Tables)));          // P5 goods-table fix: always written (no format bump)
        if (Hero != null) sections.Add(new(WorldSave.HeroSection, SaveCodec.Serialize(Hero)));
        if (Codex != null) sections.Add(new(WorldSave.CodexSection, SaveCodec.Serialize(Codex)));
        if (Live != null) sections.Add(new(WorldSave.LiveSection, SaveCodec.SerializeCompressed(Live)));
        if (Economy != null) sections.Add(new(WorldSave.EconomySection, SaveCodec.SerializeCompressed(Economy)));
        if (Abstract != null) sections.Add(new(WorldSave.AbstractSection, SaveCodec.SerializeCompressed(Abstract)));
        if (Farming != null) sections.Add(new(WorldSave.FarmingSection, SaveCodec.SerializeCompressed(Farming)));
        if (Growth != null) sections.Add(new(WorldSave.GrowthSection, SaveCodec.SerializeCompressed(Growth)));
        if (Jobs != null) sections.Add(new(WorldSave.JobsSection, SaveCodec.SerializeCompressed(Jobs)));
        if (Crafting != null) sections.Add(new(WorldSave.CraftingSection, SaveCodec.SerializeCompressed(Crafting)));
        if (Mounts != null) sections.Add(new(MountsSave.SectionName, SaveCodec.SerializeCompressed(Mounts)));
        if (Travel != null) sections.Add(new(TravelSave.SectionName, SaveCodec.SerializeCompressed(Travel)));
        if (Autopilot != null) sections.Add(new(AutopilotSave.SectionName, SaveCodec.SerializeCompressed(Autopilot)));
        if (Talk != null) sections.Add(new(TalkSave.SectionName, SaveCodec.SerializeCompressed(Talk)));
        if (Onboarding != null) sections.Add(new(OnboardingSave.SectionName, SaveCodec.SerializeCompressed(Onboarding)));
        if (Wounds != null) sections.Add(new(WoundSave.SectionName, SaveCodec.SerializeCompressed(Wounds)));
        if (Fight != null) sections.Add(new(FightRecord.SectionName, SaveCodec.SerializeCompressed(Fight)));
        if (FightLog != null) sections.Add(new(FightLogSave.SectionName, SaveCodec.SerializeCompressed(FightLog)));
        if (Captivity != null) sections.Add(new(CaptivitySave.SectionName, SaveCodec.SerializeCompressed(Captivity)));       // P4-D (v7)
        if (Succession != null) sections.Add(new(SuccessionSave.SectionName, SaveCodec.SerializeCompressed(Succession)));    // P4-D (v7)
        if (Watch != null) sections.Add(new(WatchSave.SectionName, SaveCodec.SerializeCompressed(Watch)));                   // P4-F (optional, no bump)
        if (Sites != null) sections.Add(new(SiteSave.SectionName, SaveCodec.SerializeCompressed(Sites)));                    // P4-H (optional, no bump)
        if (Land != null) sections.Add(new(LandSave.SectionName, SaveCodec.SerializeCompressed(Land)));                      // P5-A2 (optional, no bump)
        if (Construction != null) sections.Add(new(ConstructionSave.SectionName, SaveCodec.SerializeCompressed(Construction))); // P5-B (optional, no bump)
        if (Designs != null) sections.Add(new(DesignsSave.SectionName, SaveCodec.SerializeCompressed(Designs)));             // P5-C (optional, no bump)
        if (Wills != null) sections.Add(new(WillsSave.SectionName, SaveCodec.SerializeCompressed(Wills)));                   // P5-H (optional, no bump)
        if (Standing != null) sections.Add(new(StandingSave.SectionName, SaveCodec.SerializeCompressed(Standing)));         // P6-A3 (optional, no bump)
        if (Ownership != null) sections.Add(new(OwnershipSave.SectionName, SaveCodec.SerializeCompressed(Ownership)));       // P5-F (optional, no bump)
        if (Party != null) sections.Add(new(WorldSave.PartySection, SaveCodec.SerializeCompressed(Party)));
        if (HeroPending != null) sections.Add(new(Ravensguard.Sim.Trade.HeroPendingSave.SectionName, SaveCodec.SerializeCompressed(HeroPending)));
        if (Production != null) sections.Add(new(WorldSave.ProductionSection, SaveCodec.SerializeCompressed(Production)));
        if (Markets != null) sections.Add(new(WorldSave.MarketsSection, SaveCodec.SerializeCompressed(Markets)));
        if (ContinentSt != null) sections.Add(new(WorldSave.ContinentSection, SaveCodec.SerializeCompressed(ContinentSt)));
        if (Regions != null) foreach (var (id, d) in Regions) sections.Add(new(WorldSave.RegionSection(id), SaveCodec.SerializeCompressed(d)));
        foreach (var (id, b) in PagedRegions) sections.Add(new(WorldSave.RegionSection(id), b));
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
