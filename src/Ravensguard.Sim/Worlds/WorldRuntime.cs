using System.Collections.Concurrent;
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Worlds;

/// <summary>One resident region as the barrier left it (what the world map and the HUD read; never the records).</summary>
public readonly record struct RegionRow(int Id, RegionLevel Level, long HourDone, long Alive, int Households);

/// <summary>
/// The abstract world as of the last finished hour barrier: immutable, swapped in whole by the tick worker, read by any thread (seamless-regions spec N21).
/// <see cref="HoursStepped"/> counts hour barriers since the runtime started; <see cref="HourMs"/> is the worker's wall time for the last one (all regions, with
/// any joins, page-outs and catch-up), <see cref="MaxHourMs"/> the slowest so far.
/// </summary>
public sealed record RuntimeSnapshot(long Hour, int Resident, int Detailed, int Daily, int Paged, long HomeAlive, int HomeHouseholds, long HoursStepped, double HourMs, double MaxHourMs,
    SchedulerReport Last, IReadOnlyList<RegionRow> Regions);

/// <summary>
/// Runs the seamless world's abstract tick next to the live game (REDESIGN_PLAN 2.4, seamless-regions N21). The main thread calls <see cref="Pump"/> every frame:
/// it tells the runtime the game clock and where the hero is, and when a whole game hour has passed since the last barrier it hands the hour to the tick worker.
/// The worker runs <see cref="RegionScheduler.Update"/> for exactly that hour (joins finished regions, pages regions in and out, steps every resident region
/// ascending id) and swaps in a new <see cref="RuntimeSnapshot"/> at the end: that swap is the barrier. Records are written by the worker alone; the main thread
/// never touches them, and asks for anything it needs through <see cref="AtBarrier{T}"/>, which runs a job on the worker between two hours. One hour is in flight
/// at a time, so a slow machine falls behind in game hours, never in frames.
/// <para>Deterministic where it matters: every hour is the scheduler's own, regions step in ascending id, and a game that is saved at a barrier and reloaded continues
/// to the same records. What depends on timing is only when a region joins and which ring region the hero had at the moment an hour was started.</para>
/// </summary>
public sealed class WorldRuntime : IDisposable
{
    public WorldState State { get; private set; }
    public RegionServices Services { get; private set; }
    public RegionManager Manager { get; private set; }
    public RegionRecordSet Records { get; private set; }
    public RegionScheduler Scheduler { get; private set; }
    public HeroRegionTracker Hero { get; private set; }

    private readonly BlockingCollection<Action> _jobs = new();
    private Thread? _worker;
    private volatile bool _hourBusy;
    private long _hourDone;                    // last hour whose barrier is applied (worker writes at the barrier, Pump reads)
    private volatile RuntimeSnapshot _snapshot;
    private Exception? _fault;
    private volatile Ravensguard.Sim.Caravans.Caravan[] _caravans = [];
    private volatile Ravensguard.Sim.Armies.ArmySnapshot _armies = Ravensguard.Sim.Armies.ArmySnapshot.Empty;
    private long _hoursStepped;
    private double _maxHourMs;
    private int _disposed;

    /// <summary>Called on the tick worker right after each hour is stepped, before the barrier's snapshot is swapped in (the hero's command desk hangs here).</summary>
    public event Action<long>? AfterHour;

    // ---- P7-0B (review finding G1): the hourly links of Phase 6 (talk, law, family, contracts) belong to the runtime, not to the Godot street view
    private readonly Dictionary<Type, object> _links = [];
    private ContentDatabase? _linkContent;
    /// <summary>The Phase 6 links (talk, law, family, contracts): attached by <see cref="Start"/> when the world has a hero, or by a harness that steps with <see cref="AdvanceTo"/> through <see cref="Phase6Links.AttachAll"/> (null until then).</summary>
    public Phase6LinkSet? Phase6 { get; internal set; }

    /// <summary>Makes a link once per runtime: a second <c>Attach</c> of the same kind (a test that attaches by hand, then <see cref="Start"/>) gets the first one back, so an hour never runs twice.</summary>
    internal T LinkOnce<T>(Func<T> make) where T : class
    {
        if (_links.TryGetValue(typeof(T), out var have)) return (T)have;
        var made = make(); _links[typeof(T)] = made; return made;
    }
    /// <summary>The worker's last failure (null while healthy). The runtime stops starting hours after it; the game reports it once.</summary>
    public Exception? Fault => Volatile.Read(ref _fault);
    public RuntimeSnapshot Snapshot => _snapshot;
    /// <summary>The caravans on the road as the last barrier left them (copies, safe to read from any thread): what the street view draws and the world map marks.</summary>
    public IReadOnlyList<Ravensguard.Sim.Caravans.Caravan> Caravans => _caravans;
    /// <summary>P7-AM-A: the standing warbands and the bandit bands as the last barrier left them (an immutable copy, safe to read from any thread): what the encounter cast and the travel feed read.</summary>
    public Ravensguard.Sim.Armies.ArmySnapshot Armies => _armies;
    /// <summary>Last hour whose barrier is applied.</summary>
    public long HourDone => Interlocked.Read(ref _hourDone);
    /// <summary>True while the worker is stepping an hour or running a job.</summary>
    public bool Busy => _hourBusy || _jobs.Count > 0 || _jobRunning;
    private volatile bool _jobRunning;

    /// <summary>Wall time of the last <see cref="Pump"/> call on the calling thread (the main thread's whole cost of the tick per frame) and the worst so far, milliseconds.</summary>
    public double PumpMs { get; private set; }
    public double MaxPumpMs { get; private set; }
    public long Pumps { get; private set; }
    public double PumpMsTotal { get; private set; }

    /// <summary>
    /// Wires the world state to a running set of regions: the manager (with the home region adopted), the record set (home region's records, the sections of a loaded
    /// save waiting in <paramref name="pager"/>) and the scheduler. <see cref="WorldState.HomeRecords"/> is used when present (a game just built), else the home
    /// records are made from the state's own economy sections (a loaded game).
    /// </summary>
    public WorldRuntime(WorldState state, ContentDatabase content, IRegionPager? pager = null, bool trimTerrain = true, int dailyRing = 1, int maxParallel = 0, RegionManager? manager = null)
    {
        try { Init(state, content, pager, trimTerrain, dailyRing, maxParallel, manager); }
        catch { (pager as IDisposable)?.Dispose(); throw; } // a failed construction must not leave the pager's temp folder behind
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(State), nameof(Services), nameof(Manager), nameof(Records), nameof(Scheduler), nameof(Hero), nameof(_snapshot))]
    private void Init(WorldState state, ContentDatabase content, IRegionPager? pager, bool trimTerrain, int dailyRing, int maxParallel, RegionManager? manager)
    {
        State = state; _linkContent = content;
        var home = state.HomeRegion;
        Services = new RegionServices(state.Seed, content, state.Continent, home) { History = state.History };
        var region = state.Population.Region;
        // the abstract continent: the one the game was made or saved with, else a fresh one over the history (a save without the continent section: one from before the
        // continent tick, or one written before the resume-drift fix, when the section was never written). It is ticked once below, the way a new game makes it.
        bool freshContinent = state.ContinentState == null;
        if (state.ContinentState == null)
        {
            if (state.RegionSections.Count > 0)
                System.Diagnostics.Trace.TraceWarning("This save has other regions but no continent section: the continent is rebuilt, and caravans and travellers in flight are lost.");
            var fresh = Ravensguard.Sim.Continental.ContinentState.Create(state.Seed, state.History, home.Id, (int)(state.Abstract?.Vital.Alive ?? state.Population.People.Count),
                state.Economy?.Ledger.Audit().InPurses.Mites ?? 0);
            state.ContinentState = fresh;
        }
        Ravensguard.Sim.Continental.ContinentWiring.Attach(state.ContinentState, state.Continent, state.History, Services.Weather);
        Ravensguard.Sim.Continental.ContinentTick.BindPrices(state.Economy!.Goods);
        Services.AbstractContinent = state.ContinentState;
        if (manager == null)
        {
            var rs = state.RegionSettings; rs.Seed = region.Seed;
            manager = new RegionManager(state.Continent, state.History, rs, content) { TrimTerrain = trimTerrain };
        }
        Manager = manager;
        if (!Manager.IsReady(home)) Manager.Adopt(home, region, state.Population);
        Records = new RegionRecordSet(Services, Manager, state.Chunks, pager);
        var homeWorld = state.HomeRecords ?? RegionWorld.ForHome(Services, state);
        // a rebuilt continent gets the tick a new game's gets when it is made (RegionServices.EnsureContinent): prices and summaries, and LastSeason set to this season,
        // so the scheduler does not run this season's tick a second time after the load
        if (freshContinent)
            Ravensguard.Sim.Continental.ContinentTick.Run(state.ContinentState!, [new Ravensguard.Sim.Continental.ResidentRegion(home.Id, homeWorld.Economy)], GameTime.FromGameHours(homeWorld.HourDone + 1));
        if (!homeWorld.Economy.Trade.IsBound) homeWorld.Economy.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(state.ContinentState, home.Id));
        else homeWorld.Economy.BindContinent(new Ravensguard.Sim.Economy.Markets.ContinentLink(state.ContinentState, home.Id));
        // what the abstract world builds in the home region reaches the chunks the player has already seen as ID-keyed deltas (plan 2.2); a chunk not yet seen is generated from the changed plan
        homeWorld.Economy.Growth.ChunkSink ??= new Ravensguard.Sim.Growth.ChunkDeltaWriter(Ravensguard.Sim.WorldGen.Chunks.ChunkGenerator.For(region, state.Population, content), state.Chunks);
        Records.AdoptHome(homeWorld);
        state.HomeRecords = homeWorld;
        homeWorld.Sim.Set(state.Wounds ??= new Ravensguard.Sim.Combat.Wounds.WoundLedger(state.Seed, state.Clock.Now.Seconds));   // the wounds both tiers share (Phase 4 P4-B)
        homeWorld.Sim.Set(Ravensguard.Sim.Combat.Aftermath.AftermathDesk.BookOf(state, content));                               // P4-D hook: the captives' hourly step (CaptivitySystem)
        Ravensguard.Sim.Sites.SiteWiring.Attach(homeWorld, state, content, Services.Weather);                                                     // P4-H hook: the sites' book, content and weather (SiteSystem)
        Ravensguard.Sim.Land.LandWiring.Attach(homeWorld, state, content);                                                                          // P5-A2 hook: the land book, the land world (LandSystem), growth's and farming's land hooks
        Ravensguard.Sim.Construction.ConstructionWiring.Attach(homeWorld, state, content, Services.Weather);                                         // P5-B hook: building sites, ghosts and crews (ConstructionSystem)
        Ravensguard.Sim.Burning.FireWiring.Attach(homeWorld, state, content, Services.Weather);                                                    // P6-B5 hook: fire and burning (FireSystem), after construction and wounds
        Ravensguard.Sim.Building.PowerWiring.Attach(homeWorld, state, content, Services.Weather);                                                  // P7-LB1 hook: water, wind and animal power over the built pieces (PowerSystem), after construction
        Ravensguard.Sim.WarSupply.ArmsPlayLink.Attach(homeWorld, state);                                                                            // P8-WS-2: the arms day and the stewards' season order run on the tick worker
        Ravensguard.Sim.WarSupply.WarSupplyWiring.Attach(homeWorld, state);                                                                         // P7-EA1 hook: the host supply interface, only when the save has a warsupply section (HostSupplySystem)
        Ravensguard.Sim.Crafting.CraftServiceWiring.Attach(homeWorld, state, content, Services.Weather);                                            // P5-E hook: commissions, fires, apprenticeships, guild dues (CraftSystem)
        Ravensguard.Sim.Carts.VehicleWiring.Attach(homeWorld, state, content);                                                                      // P5-G hook: vehicle sites, commissions and voyages (VehicleSystem)
        Ravensguard.Sim.Production.OwnershipWiring.Attach(homeWorld, state, content);                                                              // P5-F hook: the hero's businesses, stewards, stalls, prospecting (OwnershipSystem)
        Ravensguard.Sim.Production.Industry.IndustryWiring.Attach(homeWorld, state, content);                                                     // P7-IN1 hook: works, stages and the industry slider (IndustrySystem), before the scale world that inspects their size
        Ravensguard.Sim.Production.ScaleWiring.Attach(homeWorld, state, content);                                                                  // P7-ES1 hook: foremen, manufactory licences, the guilds' inspection (ScaleSystem), after the ownership world
        Ravensguard.Sim.Production.WorksWiring.Attach(homeWorld, state, content);                                                                  // P7-LB2 hook: armoury care, warehouse rents, depot rotation, the hall's span and the machines' labour (WorksSystem), after the power rules, the businesses and the scale world
        Ravensguard.Sim.Economy.CoinageWiring.Attach(homeWorld, state, content);                                                                    // P7-CN1 hook: the mints, the gold book, the mint master as staff (MintSystem), after the rule book it reads at the tick
        Ravensguard.Sim.Succession.EstateWiring.Attach(homeWorld, state, content);                                                                 // P5-H hook: estates of the dead, creditors' claims, the labour market's wills (EstateSystem)
        Ravensguard.Sim.Holdings.HoldingsWiring.Attach(homeWorld, state, content);                                                                 // P6-B6 hook: the hero's clearings, coppice and breeding (HoldingsSystem), after land and construction
        Ravensguard.Sim.Medicine.MedicineWiring.Attach(homeWorld, state, content);                                                                  // P6-B1 hook: the physicians and the hero's treatment queue (AbstractPhysic, the treatment screen)
        Ravensguard.Sim.Disease.DiseaseWiring.Attach(homeWorld, state, content);                                                                    // P6-C4 hook: illnesses, the great plague, the scarcity terms (DiseaseSystem)
        Ravensguard.Sim.Faith.FaithWiring.Attach(homeWorld, state, content);                                                                        // P6-B4 hook: the church (FaithSystem), the holy days' feasts
        Ravensguard.Sim.Rule.RuleWiring.Attach(homeWorld, state, content);                                                                         // P6-C3 hook: fief deeds, officials, dues, councils, loans, customs (RuleSystem), after land, faith and law
        Ravensguard.Sim.Politics.PoliticsWiring.Attach(homeWorld, state, content);                                                                  // P6-D2 hook: diplomacy and the AI lords (PoliticsSystem), after the rule book it reads and the war cause it feeds
        Ravensguard.Sim.Armies.ArmyWiring.Attach(homeWorld, state, content);                                                                        // P7-AM-A hook: the lords' standing warbands, their errands and the bandit bands (ArmySystem), after the rule and politics books it reads
        Ravensguard.Sim.Siege.SiegeWiring.Attach(homeWorld, state, content);                                                                        // P7-SG-D hook: sieges (investment, blockade, terms, storm, sack), over the armies
        Ravensguard.Sim.Armies.SeaRaidWiring.Attach(homeWorld, state, content);                                                                   // P7-SH-C hook: sea raids (planned each spring and summer, landed at dawn, the bell and the withdrawal)
        Ravensguard.Sim.Unrest.UnrestWiring.Attach(homeWorld, state, content);                                                                     // P6-E1 hook: grievances, the ladder, riots and revolts (UnrestSystem), after the rule and politics books it reads
        homeWorld.Sim.Set(new Ravensguard.Sim.Combat.Abstract.AbstractBattleService(content));                                    // P4-G hook: fights nobody watches (abstract battles)
        state.Regions = Records;
        if (state.RegionSections.Count > 0) { Records.AttachSections(state.RegionSections); state.RegionSections = new SortedDictionary<int, byte[]>(); } // the pager holds them now
        Scheduler = new RegionScheduler(Records) { DailyRing = dailyRing, MaxParallel = maxParallel > 0 ? maxParallel : Math.Max(1, Math.Min(2, Environment.ProcessorCount - 2)) };
        Hero = new HeroRegionTracker(home);
        _hourDone = homeWorld.HourDone;
        _snapshot = MakeSnapshot(new SchedulerReport(0, 0, 0, 0, 0, 0), 0, _hourDone);
        PublishCaravans();
    }

    private void PublishCaravans()
    {
        if (State.HomeRecords?.Sim.Get<Ravensguard.Sim.Armies.ArmyWorld>() is { } aw) _armies = Ravensguard.Sim.Armies.ArmySnapshot.Of(aw);
        var st = State.ContinentState; if (st == null) return;
        _caravans = st.Caravans.All.Where(c => c.Active).Select(c => c.Clone()).ToArray();
    }

    /// <summary>Puts the hero's tracker where a loaded or teleported hero stands (frame metres of the continent), before the first pump.</summary>
    public void PlaceHero(long continentXM, long continentYM)
    {
        var at = RegionCoord.OfMetres(continentXM, continentYM);
        if (at.IsValid) Hero = new HeroRegionTracker(at);
    }

    // ------------------------------------------------------------------ main thread

    /// <summary>Starts the worker thread (idempotent). Without it <see cref="Pump"/> only tracks the hero.</summary>
    public void Start()
    {
        if (_worker != null) return;
        if (Phase6 == null && _links.Count == 0 && _linkContent != null && State.Hero != null) Phase6Links.AttachAll(this, _linkContent);   // G1 (P7-0B): the game's hourly links are the runtime's: talk, law, family, contracts in the one order, behind the hero, before the first hour (not when a harness attached links by hand: it chose its own set)
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "region-tick", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>
    /// The per-frame call (main thread): the hero's position in continent metres decides the ring region; if the game clock is past the last barrier and the worker is
    /// idle, the next hour starts. Costs microseconds: no records are read, nothing is waited for.
    /// </summary>
    public void Pump(GameTime now, long heroXM, long heroYM)
    {
        long t0 = Stopwatch.GetTimestamp();
        Hero.Update(heroXM, heroYM);
        Manager.NowHour = now.TotalHours; // the game hour a region asked for now is anchored to (RegionManager.RequestedHour)
        if (_worker != null && !_hourBusy && Volatile.Read(ref _fault) == null && now.TotalHours > HourDone)
        {
            _hourBusy = true;
            var ring = Hero.RingRegion;
            long h = HourDone + 1;
            _jobs.Add(() => { Siege.SiegeWiring.NoteHero(State, heroXM, heroYM); Ravensguard.Sim.Armies.SeaRaidWiring.NoteHero(State, heroXM, heroYM); RunHour(h, ring); });          // the hero's place is told at the barrier, so a siege's answer to "in the bubble?" is the same on every run
        }
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        PumpMs = ms; MaxPumpMs = Math.Max(MaxPumpMs, ms); Pumps++; PumpMsTotal += ms;
    }

    /// <summary>
    /// Runs <paramref name="job"/> on the tick worker between two hours (nothing is stepping, so every record may be read) and returns its result. This is how a save
    /// copies the world: the main thread keeps drawing while the worker copies.
    /// </summary>
    public Task<T> AtBarrier<T>(Func<T> job) => AtBarrier(job, allowFaulted: false);

    /// <summary>
    /// The same. After the tick has faulted the records may be half-stepped, so a job is refused (the task fails with an <see cref="InvalidOperationException"/> that carries the fault)
    /// unless <paramref name="allowFaulted"/>: saves must not overwrite good slots with that state.
    /// </summary>
    public Task<T> AtBarrier<T>(Func<T> job, bool allowFaulted)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!allowFaulted && Fault is { } f0) { tcs.SetException(FaultedError(f0)); return tcs.Task; }
        if (_worker == null) { try { tcs.SetResult(job()); } catch (Exception e) { tcs.SetException(e); } return tcs.Task; }
        try
        {
            _jobs.Add(() =>
            {
                _jobRunning = true;
                try
                {
                    if (!allowFaulted && Fault is { } f) throw FaultedError(f); // it faulted while the job waited
                    tcs.SetResult(job());
                }
                catch (Exception e) { tcs.SetException(e); }
                finally { _jobRunning = false; }
            });
        }
        catch (InvalidOperationException) { tcs.SetException(new ObjectDisposedException(nameof(WorldRuntime))); } // adding is completed: the runtime is disposed
        return tcs.Task;
    }

    private static InvalidOperationException FaultedError(Exception fault) => new("The world tick failed, so the world cannot be saved now (the records may be half-stepped): " + fault.GetBaseException().Message, fault);

    /// <summary>Blocks until the worker has nothing queued or running (tests, leaving the game). Hours still owed by the clock are not started.</summary>
    public void Quiesce()
    {
        if (_worker == null) return;
        AtBarrier(() => 0, allowFaulted: true).GetAwaiter().GetResult();
    }

    /// <summary>Tests and tools: steps every owed hour up to <paramref name="now"/> on the calling thread (no worker may be running), then returns.</summary>
    public void AdvanceTo(GameTime now)
    {
        if (_worker != null) throw new InvalidOperationException("The worker is running; drive the runtime with Pump.");
        while (HourDone < now.TotalHours) RunHour(HourDone + 1, Hero.RingRegion);
    }

    // ------------------------------------------------------------------ worker

    private void WorkerLoop()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try { job(); }
            catch (Exception e) { Interlocked.CompareExchange(ref _fault, e, null); _hourBusy = false; }
        }
        Cleanup();                                          // disposed: nothing is queued any more, nothing steps
    }

    private void RunHour(long h, RegionCoord ring)
    {
        var sw = Stopwatch.StartNew();
        long s0 = Stopwatch.GetTimestamp();
        var report = Scheduler.Update(GameTime.FromGameHours(h), ring);
        Ravensguard.Sim.Diagnostics.PfClock.Add("(scheduler)", Stopwatch.GetTimestamp() - s0);
        if (h % (24L * TimeConst.DaysPerYear) == 0) Ravensguard.Sim.People.PersonCompaction.Run(State, GameTime.FromGameHours(h));      // P6-E4 (chronicle-full 4.5): old commoners' entries become yearly summaries; P8-PF-2: then the dead rows nothing names are dropped
        if (Ravensguard.Sim.Diagnostics.PfClock.Table != null && AfterHour is { } ah)
        {
            foreach (Action<long> hook in ah.GetInvocationList()) { long t0 = Stopwatch.GetTimestamp(); hook(h); Ravensguard.Sim.Diagnostics.PfClock.Add(hook.Target?.GetType().Name ?? hook.Method.Name, Stopwatch.GetTimestamp() - t0); }
        }
        else AfterHour?.Invoke(h);                           // the hero's desk: the hour's own passes, then the commands asked in it (trade spec 4.7)
        Manager.ReleaseDistantGenerators(State.HomeRegion);
        Manager.EvictDistantTerrainOnly(State.HomeRegion, ring);
        Manager.PrewarmGenerator();
        double ms = sw.Elapsed.TotalMilliseconds;
        _hoursStepped++; _maxHourMs = Math.Max(_maxHourMs, ms);
        PublishCaravans();
        _snapshot = MakeSnapshot(report, ms, h);             // the barrier: one reference swap
        Interlocked.Exchange(ref _hourDone, h);
        _hourBusy = false;
    }

    private RuntimeSnapshot MakeSnapshot(SchedulerReport report, double hourMs, long hour)
    {
        var rows = new List<RegionRow>();
        int detailed = 0, daily = 0;
        foreach (var rw in Records.Resident)
        {
            var v = rw.Sim.Vital;
            rows.Add(new RegionRow(rw.Coord.Id, rw.Level, rw.HourDone, v.Alive, rw.Population.Households.Count));
            if (rw.Level is RegionLevel.Live or RegionLevel.Detailed) detailed++; else daily++;
        }
        var home = Records.HomeWorld;
        long alive = home?.Sim.Vital.Alive ?? 0;
        int hh = 0;
        if (home != null) { var t = home.Population.Households; for (int i = 0; i < t.Count; i++) if (t.MemberCount[i] > 0) hh++; }
        int paged = Records.Pager.Ids.Count;
        return new RuntimeSnapshot(hour, rows.Count, detailed, daily, paged, alive, hh, _hoursStepped, hourMs, _maxHourMs, report, rows);
    }

    /// <summary>
    /// Stops the runtime without blocking the caller for long: the worker finishes the hour in flight and any save job already queued, then disposes the records and the pager itself.
    /// Waits at most a quarter of a second (the idle case is instant).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _jobs.CompleteAdding();
        if (_worker == null) { Cleanup(); return; }
        _worker.Join(250);                                  // still busy: the worker cleans up when it is done (WorkerLoop)
    }

    private void Cleanup()
    {
        foreach (var rw in Records.Resident) rw.Dispose();
        (Records.Pager as IDisposable)?.Dispose();
    }
}
