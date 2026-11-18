using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Tests.Autopilot;

/// <summary>
/// A headless world with the real economy, markets, population and odd jobs, a hero with a purse and a pack, and the autopilot driving the hero: the soak and the directive tests run on it. Jobs are posted and advanced by the autopilot
/// world itself (<see cref="AutopilotWorld.DrivesJobs"/>), the hero's commands are applied at the hour barrier exactly as the runtime does.
/// </summary>
public sealed class ApWorld : IDisposable
{
    public HeadlessWorld Headless { get; }
    public WorldTicker Ticker { get; }
    public WorldState State { get; }
    public HeroContext Ctx { get; }
    public HeroDesk Desk { get; } = new();
    public AutopilotWorld Ap { get; }
    public AutopilotBook Book { get; }
    public JobTable Table { get; } = new();
    public JobEnv Env { get; }
    public EconomyState Econ => Headless.World.Economy;
    public EntityId Hero => Ctx.Party.Hero;
    public long HourDone => Ticker.HourDone;
    public long Sec => (HourDone + 1) * 3600;
    public ContentDatabase Content { get; }

    public ApWorld(HeadlessWorld hw, WorldTicker ticker, ContentDatabase content, int heroCrowns = 30, bool pack = true, ulong chronicleSeed = 0)
    {
        Headless = hw; Ticker = ticker; Content = content;
        var origin = content.All<OriginDef>().OrderBy(o => o.Order).First();
        var hero = HeroRecord.FromOrigin("Test Hero", false, origin);
        var econ = hw.World.Economy; econ.SeedHero(heroCrowns);
        State = new WorldState
        {
            ContinentSettings = hw.ContinentSettings, HistorySettings = hw.HistorySettings, RegionSettings = hw.RegionSettings, EmbarkCellX = hw.CellX, EmbarkCellY = hw.CellY,
            Continent = hw.Continent, History = hw.History, Population = hw.Population, Clock = new SimClock(GameTime.FromGameHours(ticker.HourDone + 1)), Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
            Economy = econ, Hero = hero, Markets = hw.Economy.Markets,
        };
        State.Party = HeroParty.Create(econ, content, State.Ids, hero);
        Env = new JobEnv { Ledger = econ.Ledger, Econ = econ, Pop = hw.Population, Tuning = BehaviourContent.From(content).OddJobs };
        State.Jobs = Table;
        Ctx = new HeroContext(State, State.Party, content) { Jobs = () => (Env, Table) };
        Book = new AutopilotBook();
        Ap = new AutopilotWorld(State, Ctx, Desk, Book, AutopilotContent.From(content)) { DrivesJobs = true };
        State.Autopilot = Book;
        if (pack) Provision(2);
    }

    /// <summary>The soak world of seed 1234.</summary>
    public static ApWorld Soak(int heroCrowns = 30)
    {
        var w = SoakWorld.Create();
        var a = new ApWorld(w.Headless, w.Ticker, Ravensguard.Sim.Tests.Economy.EconFixture.Content, heroCrowns);
        a._owner = w; return a;
    }

    /// <summary>A world of another seed (the generator picks the region).</summary>
    public static ApWorld Seeded(ulong seed, int heroCrowns = 30)
    {
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var hw = HeadlessWorld.Create(seed, dir);
        var t = hw.NewTicker();
        return new ApWorld(hw, t, hw.Content, heroCrowns);
    }

    private IDisposable? _owner;

    /// <summary>Puts <paramref name="days"/> of the cheapest food into the hero's pack (a kit the hero starts with), created at the seed (the soak's mass audit allows it: it is the hero's starting kit).</summary>
    public void Provision(int days)
    {
        var goods = Econ.Goods; ushort best = ushort.MaxValue; int bf = 0;
        for (ushort g = 0; g < goods.Count; g++) { int f = goods.FoodMeasuresPpm(g); if (f > bf && !goods.IsPiece(g) && goods.StringId(g) is "bread" or "flour" or "wheat" or "oats") { best = g; bf = f; } }
        if (best == ushort.MaxValue) for (ushort g = 0; g < goods.Count; g++) { int f = goods.FoodMeasuresPpm(g); if (f > bf && !goods.IsPiece(g)) { best = g; bf = f; } }
        long grams = (1000L * 1000 * days * 1000 + bf - 1) / bf;
        Ctx.Pack.Create(best, grams, Ravensguard.Sim.Goods.CreateReason.Seed);
    }

    /// <summary>Plays <paramref name="hours"/> game hours: the autopilot acts through the hour, the world steps it, the hero's commands are applied at its barrier.</summary>
    public void RunHours(int hours, Action<long>? afterHour = null, int chunkSec = 3600)
    {
        for (int i = 0; i < hours; i++)
        {
            long h = Ticker.HourDone + 1;
            for (long t = h * 3600 + chunkSec - 1; t < h * 3600 + 3600; t += chunkSec) Ap.Advance(t);
            Ap.Advance(h * 3600 + 3599);
            Ticker.AdvanceHours(1);
            Desk.AfterHour(Ctx, h);
            afterHour?.Invoke(h);
        }
    }

    public void Dispose() { (_owner ?? Ticker).Dispose(); }
}
