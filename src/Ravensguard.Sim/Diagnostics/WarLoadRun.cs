using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// A whole new game of a seed's world on the real <see cref="WorldRuntime"/> with the game's own links attached (economy-audit finding G1: law with its heist, bond and faith-politics links, family and contracts are attached by
/// the Godot street view, not by the runtime, so a headless soak must do it itself) and the host supply interface installed, driven a day at a time around the host slot: <see cref="BeforeSlot"/> leaves the runtime at the barrier
/// of hour 17 (where <see cref="WarLoad.Before"/> places the host), <see cref="AfterSlot"/> at the barrier of hour 18 (the slot has run; <see cref="WarLoad.After"/> reads the day).
/// </summary>
public sealed class WarLoadRun : IDisposable
{
    public WorldState State { get; }
    public WorldRuntime Runtime { get; }
    public HostSupply Supply { get; }
    public ulong Seed => State.Seed;
    public EconomyState Econ => State.Economy!;
    public Ravensguard.Sim.Abstract.EconomyWorld Economy => State.HomeRecords!.Economy;
    public bool LinksAttached { get; }

    private WarLoadRun(WorldState st, WorldRuntime rt, HostSupply hs, bool links) { State = st; Runtime = rt; Supply = hs; LinksAttached = links; }

    /// <summary>Generates the seed's continent, history, embark region and people (as the world tool does) and starts a new game on them.</summary>
    public static WarLoadRun Create(ulong seed, string contentDir, ContentDatabase? db = null, bool attachLinks = true)
    {
        if (db == null)
        {
            var load = new ModLoader(ContentTypeRegistry.Default).Load([contentDir]);
            if (!load.Success) throw new InvalidOperationException(load.Report.ToString());
            db = load.Database;
        }
        var cs = ContinentSettings.LoadDefaults(contentDir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(contentDir); hs.Seed = seed;
        var rs = RegionSettings.LoadDefaults(contentDir); rs.Seed = seed;
        var c = ContinentGenerator.Generate(cs); var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var region = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        LandmarkPlacer.Place(region, h, db);
        var pop = Populator.Populate(region, h, db);
        return Create(db, c, h, cs, hs, rs, pick.CellX, pick.CellY, pop, attachLinks);
    }

    /// <summary>A new game over an already generated region (tests share one and pass a clone of its people).</summary>
    public static WarLoadRun Create(ContentDatabase db, Continent continent, WorldHistory history, ContinentSettings cs, HistorySettings hs, RegionSettings rs, int cellX, int cellY, Population pop, bool attachLinks = true)
    {
        var st = NewGameWorld.Create(db, continent, history, cs, hs, rs, cellX, cellY, pop, null, null);
        st.Content = db;
        var rt = new WorldRuntime(st, db, new MemoryRegionPager());
        if (attachLinks) AttachLinks(rt);
        var supply = WarSupplyWiring.Ensure(st.HomeRecords!, st);
        return new WarLoadRun(st, rt, supply, attachLinks);
    }

    /// <summary>The game's own links, as the street view attaches them and in its order: the law (with the heist, bond and faith-politics links), the family, the contracts (economy-audit finding G1: a headless run has none unless it attaches them).</summary>
    public static void AttachLinks(WorldRuntime rt)
    {
        Ravensguard.Sim.Law.LawLink.Attach(rt);
        Ravensguard.Sim.Family.FamilyLink.Attach(rt);
        Ravensguard.Sim.Contracts.ContractLink.Attach(rt);
    }

    public long HourDone => Runtime.HourDone;
    public int DayNow => (int)(Runtime.HourDone / 24);
    /// <summary>The first day whose host slot has not run yet.</summary>
    public int NextSlotDay => HourDone >= (long)DayNow * 24 + 18 ? DayNow + 1 : DayNow;

    /// <summary>Steps to the barrier before the host slot of <paramref name="day"/> (hour 17 done).</summary>
    public void BeforeSlot(int day) => Runtime.AdvanceTo(GameTime.FromGameHours((long)day * 24 + 17));
    /// <summary>Steps through the host slot of <paramref name="day"/> (hour 18 done).</summary>
    public void AfterSlot(int day) => Runtime.AdvanceTo(GameTime.FromGameHours((long)day * 24 + 18));
    public void AdvanceToDayEnd(int day) => Runtime.AdvanceTo(GameTime.FromGameHours((long)(day + 1) * 24));

    public bool CoinConserved => Econ.Ledger.Audit().IsConserved;
    public bool MassOk => Econ.MassAuditHolds();

    /// <summary>The world's saved bytes: every section, in name order, as <c>WorldSave.Capture</c> writes them, for the byte-for-byte checks.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Sections() => WorldSave.Capture(State, "h", "0.1").Sections;

    /// <summary>A hash over every section of the save (as the Phase 6 trail does).</summary>
    public string SaveHash()
    {
        var file = WorldSave.Capture(State, "h", "0.1");
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var name in file.Sections.Keys.Order(StringComparer.Ordinal))
        {
            if (name == WorldSave.MetaSection) continue;
            sha.AppendData(Encoding.UTF8.GetBytes(name)); sha.AppendData([0]);
            var bytes = file.Sections[name]; sha.AppendData(BitConverter.GetBytes(bytes.Length)); sha.AppendData(bytes.Span);
        }
        return Convert.ToHexString(sha.GetHashAndReset())[..16];
    }

    public void Dispose() => Runtime.Dispose();
}

/// <summary>One campaign of the scenario (economy-audit 5.2): the muster day (before the jitter), the day it is over, and where the siege is (from a day of the campaign for a number of days, 0 none).</summary>
public sealed record WarCampaign(int Id, int Year, int FromDay, int ToYear, int ToDay, bool Purveyance, int SiegeFromDay, int SiegeDays);

/// <summary>The scenario of the war-load soak (economy-audit 5.2): Y1-Y5 peace; Y6 and Y7 a campaign from day 16 to 30 (2,000 men, 300 horses, 60 carts); Y8 days 16 to 45 with the levies out through the harvest and, from day 31, a 60-day siege of the largest town (to Y9 day 30); Y9 a 15-day campaign; Y10 a 15-day campaign; Y11 on peace. Years are 1-based.</summary>
public static class WarLoadScenario
{
    public static readonly IReadOnlyList<WarCampaign> Campaigns =
    [
        new(1, 6, 16, 6, 30, false, 0, 0),
        new(2, 7, 16, 7, 30, true, 0, 0),
        new(3, 8, 16, 9, 30, true, 31, 60),
        new(4, 9, 31, 9, 45, false, 0, 0),
        new(5, 10, 16, 10, 30, false, 0, 0),
    ];
}

/// <summary>One yearly row of the war table (the soak's war columns of economy-audit 5.4 that the first cut can measure).</summary>
public sealed class WarYearRow
{
    public int Year;
    public long Alive, HungerDeaths, HarvestT, RegionCoin;
    public long ManDaysFed, ManDaysHungry, ManDaysUnpaid, BoughtKg, PaidMites, Deserted, Mustered, ArrearsMites, FundedMites;
    public int SiegeDaysOfFood = -1;
    /// <summary>What the realm raised for the campaign (aid from the fiefs, tallage on the households).</summary>
    public long AidMites, TallageMites;
    /// <summary>Idle adults (16-59, no occupation, no land) at the year's end; wages paid a day over the year; coin in purses at the year's end (the money supply).</summary>
    public long Idle, WagesPerDay, MoneySupply;
    /// <summary>The staple price (mites a kg, the mean of the grains and bread over the books) over the year: the towns within 8 km of the route and the whole region, mean and peak of the daily value, in mites a kg.</summary>
    public long RouteMeanMilli, RoutePeakMilli, RegionMeanMilli, RegionPeakMilli;
    /// <summary>Merchants holding at least the solvency floor (10 crowns), per cent, at the year's end.</summary>
    public int SolventPct;
    /// <summary>Farm labour hours wanted and left undone over the year (the planner's running totals): a levy out through the harvest shows as hours undone.</summary>
    public long FarmDemandH, FarmUnmetH;
    public bool CoinOk, MassOk;
    public string Hash = "";
    // ---- P7-EA3b: the second run's columns (money by source, industries, price level, timing)
    /// <summary>Coin that crossed the region border in the year (immigrants' purses in, emigrants' purses out), struck at the mints, melted, and left in hoards at the year's end; the harness's one-off seed (the L3 works' capital) already made, subtracted from <see cref="MoneySupply"/>.</summary>
    public long ImmigMites, EmigMites, MintMites, MeltMites, HoardMites, HarnessSeedMites;
    /// <summary>Works (open at the year's end, founded in the year, closed in the year) of the industries; NPC works are those the founders made.</summary>
    public int WorksOpen, WorksFounded, WorksClosed;
    /// <summary>The median over non-piece goods of the books' mean reference price as permille of the content's base price, and the same for the wage a day (the wage classes are fixed content): the price level against the wages.</summary>
    public int RefPricePermille;
    /// <summary>Charcoal and bar iron over the region's books at the year's end: stock (kg), days of cover (stock over the books' demand), the books' mean mid price as permille of base; and the kg made in the year (created from labour: the mass book's creations).</summary>
    public long CharcoalKg, IronBarKg, CharcoalMadeKg, IronBarMadeKg; public int CharcoalCoverPm, IronBarCoverPm, CharcoalPricePm, IronBarPricePm;
    /// <summary>Wall-clock milliseconds a game day took, the mean over the year, and the slowest single hour step the soak made (the hour of the host slot).</summary>
    public long DayMsMean, HourMsMax;
    /// <summary>What crossed the border in the year, by cause (the trade desk's and the caravans' exports paid by Immigrate and imports by Emigrate; the desk's share of each): the rest of <see cref="ImmigMites"/> is the immigrants' stakes and of <see cref="EmigMites"/> the emigrants' purses, tolls and soldiers away.</summary>
    public long ExpMites, ImpMites, DeskExpMites, DeskImpMites;
    /// <summary>The L3 works' line at the year's end (empty when there is none).</summary>
    public string L3 = "";
    /// <summary>Per chain: open works and the NPC works' mean net a day over the last 60 days.</summary>
    public string Chains = "";
    /// <summary>The books' mean reference price over the content's base price (percent) for the chain goods and the staples, and the 25th, 50th, 75th and 90th percentile over all non-piece goods (permille).</summary>
    public string Prices = "";
}

/// <summary>The war-load soak on a <see cref="WarLoadRun"/>: runs the scenario and the control, one row a year. The gates that need the audit's own measures (WL-3 to WL-12, WL-14, WL-15) are EA2's; the first cut gates WL-1 and WL-2 (coin and mass exact every day) and prints the rest.</summary>
public sealed class WarLoadSoak(WarLoadRun run, int years, bool control = false)
{
    /// <summary>The realm raises the coin of each campaign (<see cref="WarFunding"/>); false leaves the chest to whatever the treasury holds (the first cut's behaviour).</summary>
    public bool RealmRaises { get; set; } = true;
    /// <summary>P7-EA3b: the scripted L3 iron works, founded on day 1 of <see cref="L3Year"/> at a town of the march route (null: none).</summary>
    public L3IronWorks? L3 { get; set; }
    public int L3Year { get; set; } = 2;
    /// <summary>Called at the start of each game day (year, day of the year 1-60) before anything of that day runs: the checks of WL-13 hang here.</summary>
    public Action<int, int, WarLoadRun>? AtDayStart { get; set; }
    private long _routeSum, _routePeak, _regionSum, _regionPeak, _samples, _wage0, _demand0, _unmet0;
    private long _exp0, _imp0, _dexp0, _dimp0;
    private long _immig0, _emig0, _mint0, _melt0, _charMade0, _barMade0, _dayMsSum, _hourMsMax; private int _founded0, _closed0;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private int[] _routeBooks = [];
    private ushort[] _stapleIds = [];

    public List<WarYearRow> Rows { get; } = [];
    public bool Ok { get; private set; } = true;
    public string FirstFailure { get; private set; } = "";

    public void Run(Action<WarYearRow>? each = null)
    {
        var sup = run.Supply; var region = run.State.Population.Region;
        var tally = new WarTally(); WarLoad? load = null; WarCampaign? camp = null; int siegeTown = -1;
        int startYearDay = ((int)(run.HourDone / 24) / TimeConst.DaysPerYear) * TimeConst.DaysPerYear;       // day 0 of year 1
        int gate = 0, home = 0;
        var largest = Enumerable.Range(0, region.Settlements.Count).OrderByDescending(s => region.Settlements[s].TargetPop).ThenBy(s => s).First();
        home = largest; siegeTown = largest; gate = WarLoad.FarthestSettlement(region, home);
        long hunger0 = run.State.Abstract!.Vital.DeathsByCause[(int)DeathCause.Hunger];
        PrepareIndex(region, home, gate);
        var led0 = run.Econ.Ledger; _immig0 = led0.Sourced(SourceReason.Immigrate).Mites; _emig0 = led0.Sunk(SinkReason.Emigrate).Mites; _mint0 = led0.Sourced(SourceReason.Mint).Mites; _melt0 = led0.Sunk(SinkReason.Melt).Mites;
        _charMade0 = Made("charcoal"); _barMade0 = Made("iron_bar"); _founded0 = _closed0 = 0;
        _wage0 = run.Econ.Ledger.Flow(TransferReason.Wage).Mites; _demand0 = run.Economy.Farming.TotalDemandH; _unmet0 = run.Economy.Farming.TotalUnmetH;
        for (int y = 1; y <= years; y++)
        {
            tally.Reset(); _routeSum = _routePeak = _regionSum = _regionPeak = _samples = 0; _dayMsSum = _hourMsMax = 0;
            for (int d = 1; d <= TimeConst.DaysPerYear; d++)
            {
                int day = startYearDay + (y - 1) * TimeConst.DaysPerYear + d - 1;
                AtDayStart?.Invoke(y, d, run);
                _clock.Restart();
                if (L3 != null)
                {
                    if (L3.Works == null && y == L3Year && d == 1) { L3.Place(day, WarLoad.RouteFor(region, home, gate)); }
                    else L3.Daily(day);
                }
                if (!control && load == null)
                {
                    foreach (var c in WarLoadScenario.Campaigns)
                        if (c.Year == y && WarLoad.MusterDay(run.Seed, c.FromDay, c.Id, y * 4) == d) { camp = c; load = new WarLoad(sup, WarLoadSpec.Army2000, run.Seed, c.Id, tally) { RealmRaises = RealmRaises }; break; }
                    if (load != null) { run.BeforeSlot(day); load.Begin(home, gate); }
                }
                run.BeforeSlot(day);
                if (load != null && camp != null)
                {
                    int daysIn = (y - camp.Year) * TimeConst.DaysPerYear + d - camp.FromDay;
                    bool sieging = camp.SiegeDays > 0 && daysIn >= camp.SiegeFromDay - camp.FromDay && daysIn < camp.SiegeFromDay - camp.FromDay + camp.SiegeDays;
                    if (sieging)
                    {
                        if (!sup.IsBlockaded(siegeTown)) sup.SiegeBlockade(siegeTown, true, load.Host);
                        sup.Update(load.Host, sup.PlaceAt(siegeTown), HostMode.Siege);
                    }
                    else
                    {
                        if (camp.SiegeDays > 0 && sup.IsBlockaded(siegeTown)) sup.SiegeBlockade(siegeTown, false, load.Host);
                        load.Before(marching: !load.Home);
                    }
                }
                long hourT = _clock.ElapsedMilliseconds; run.AfterSlot(day); _hourMsMax = Math.Max(_hourMsMax, _clock.ElapsedMilliseconds - hourT);
                if (load != null)
                {
                    load.After(day);
                    if (!run.CoinConserved || !run.MassOk) Fail($"day {day}: coin {run.CoinConserved}, mass {run.MassOk}");
                    if (camp != null && y == camp.ToYear && d == camp.ToDay) { if (sup.IsBlockaded(siegeTown)) sup.SiegeBlockade(siegeTown, false, load.Host); load.End(); load = null; camp = null; }
                }
                Sample();
                run.AdvanceToDayEnd(day);
                _dayMsSum += _clock.ElapsedMilliseconds;
            }
            var row = Row(y, tally, hunger0, sup, siegeTown);
            hunger0 = run.State.Abstract!.Vital.DeathsByCause[(int)DeathCause.Hunger];
            Rows.Add(row); each?.Invoke(row);
        }
    }

    private void Fail(string why) { if (Ok) FirstFailure = why; Ok = false; }

    private WarYearRow Row(int y, WarTally t, long hunger0, HostSupply sup, int town)
    {
        var s = run.State; var led = run.Econ.Ledger; var farming = run.Economy.Farming.State;
        var row = new WarYearRow
        {
            Year = y, Alive = s.Abstract!.Vital.Alive, HungerDeaths = s.Abstract.Vital.DeathsByCause[(int)DeathCause.Hunger] - hunger0,
            HarvestT = farming.Summaries.Count > 0 ? farming.Summaries[^1].HarvestG / 1_000_000 : 0, RegionCoin = led.Audit().InPurses.Mites,
            ManDaysFed = t.ManDaysFed, ManDaysHungry = t.ManDaysHungry, ManDaysUnpaid = t.ManDaysUnpaid, BoughtKg = t.BoughtStapleG / 1000, PaidMites = t.PaidMites, Deserted = t.Deserted, Mustered = t.Mustered,
            ArrearsMites = t.ArrearsMites + sup.Book.Hosts.Values.Sum(a => a.Arrears.Sum()), FundedMites = t.FundedMites,
            SiegeDaysOfFood = sup.IsBlockaded(town) ? sup.DaysOfFood(town) : -1,
            CoinOk = run.CoinConserved, MassOk = run.MassOk,
        };
        if (!row.CoinOk || !row.MassOk) Fail($"year {y} end: coin {row.CoinOk}, mass {row.MassOk}");
        row.Hash = sup.Book.Hash();
        row.AidMites = t.AidMites; row.TallageMites = t.TallageMites;
        row.MoneySupply = row.RegionCoin;                                  // MeasureEconomy subtracts the harness's one-off seed
        long wage = led.Flow(TransferReason.Wage).Mites; row.WagesPerDay = (wage - _wage0) / TimeConst.DaysPerYear; _wage0 = wage;
        row.RouteMeanMilli = _samples == 0 ? 0 : _routeSum / _samples; row.RoutePeakMilli = _routePeak; row.RegionMeanMilli = _samples == 0 ? 0 : _regionSum / _samples; row.RegionPeakMilli = _regionPeak;
        var fm = run.Economy.Farming; row.FarmDemandH = fm.TotalDemandH - _demand0; row.FarmUnmetH = fm.TotalUnmetH - _unmet0; _demand0 = fm.TotalDemandH; _unmet0 = fm.TotalUnmetH;
        MeasureWorld(row);
        MeasureEconomy(row);
        return row;
    }

    /// <summary>The towns within 8 km of the host's route (the route's own stops included) and the staple goods the price index is taken over.</summary>
    private void PrepareIndex(Ravensguard.Sim.WorldGen.Region.Region region, int home, int gate)
    {
        var route = WarLoad.RouteFor(region, home, gate).Distinct().ToArray(); var near = new List<int>();
        var markets = run.Economy.Markets;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            if (markets.Books[markets.BookOf[s]] == null) continue;
            foreach (int r in route)
            {
                long dx = region.Settlements[s].CenterX - region.Settlements[r].CenterX, dy = region.Settlements[s].CenterY - region.Settlements[r].CenterY;
                if (dx * dx + dy * dy <= 8000L * 8000L) { near.Add(markets.BookOf[s]); break; }
            }
        }
        _routeBooks = [.. near.Distinct().Order()];
        var cat = run.Econ.Goods; var ids = new List<ushort>();
        foreach (var g in new[] { "wheat", "rye", "barley", "bread" }) if (cat.TryIdOf(g, out var id)) ids.Add(id);
        _stapleIds = [.. ids];
    }

    private long MeanStaple(IEnumerable<Ravensguard.Sim.Economy.Markets.MarketBook> books)
    {
        long sum = 0, n = 0;
        foreach (var b in books) { foreach (var g in _stapleIds) { sum += b.MidMicro(g); n++; } }
        return n == 0 ? 0 : sum / n / 1000;       // micro-mites a gram / 1000 = mites a kg
    }

    private void Sample()
    {
        var markets = run.Economy.Markets;
        long route = MeanStaple(_routeBooks.Select(i => markets.Books[i]!)), region = MeanStaple(markets.AllBooks());
        _routeSum += route; _regionSum += region; _routePeak = Math.Max(_routePeak, route); _regionPeak = Math.Max(_regionPeak, region); _samples++;
    }

    private long Made(string good) => run.Econ.Goods.TryIdOf(good, out var id) ? run.Econ.Mass.TotalCreated(id) / 1000 : 0;

    /// <summary>P7-EA3b columns: coin by source, the works, the price level, charcoal and iron, the clock.</summary>
    private void MeasureEconomy(WarYearRow row)
    {
        var led = run.Econ.Ledger; var cat = run.Econ.Goods; var markets = run.Economy.Markets;
        long im = led.Sourced(SourceReason.Immigrate).Mites, em = led.Sunk(SinkReason.Emigrate).Mites, mi = led.Sourced(SourceReason.Mint).Mites, me = led.Sunk(SinkReason.Melt).Mites;
        row.ImmigMites = im - _immig0; row.EmigMites = em - _emig0; row.MintMites = mi - _mint0; row.MeltMites = me - _melt0; _immig0 = im; _emig0 = em; _mint0 = mi; _melt0 = me;
        row.HoardMites = led.Audit().InHoards.Mites;
        var tr = run.Economy.Trade;
        row.ExpMites = tr.ExportedMites - _exp0; row.ImpMites = tr.ImportedMites - _imp0; row.DeskExpMites = tr.DeskExportedMites - _dexp0; row.DeskImpMites = tr.DeskImportedMites - _dimp0;
        _exp0 = tr.ExportedMites; _imp0 = tr.ImportedMites; _dexp0 = tr.DeskExportedMites; _dimp0 = tr.DeskImportedMites;
        row.HarnessSeedMites = L3?.SeedMites ?? 0;
        row.MoneySupply = row.RegionCoin - row.HarnessSeedMites;
        var book = run.State.Industry; int day = (int)(run.HourDone / 24);
        if (book != null)
        {
            int open = 0, founded = 0, closed = 0; int yearStart = day - TimeConst.DaysPerYear;
            foreach (var w in book.Works) { if (!w.Closed) open++; if (w.StartedDay > yearStart && w.StartedDay <= day) founded++; }
            // works closed this year: the total closed now less the total at the last row (the book keeps no close day)
            int closedNow = book.Works.Count(w => w.Closed); closed = closedNow - _closed0; _closed0 = closedNow;
            row.WorksOpen = open; row.WorksFounded = founded; row.WorksClosed = closed;
            var chains = new SortedDictionary<string, (int Open, int Npc, long Net, int Aged)>(StringComparer.Ordinal); var iw = run.State.HomeRecords!.Sim.Get<IndustryWorld>()!;
            foreach (var w in book.Works)
            {
                if (w.Closed) continue; var c = chains.GetValueOrDefault(w.Chain);
                c.Open++;
                if (iw.IsNpc(w) && w.StartedDay < 900_000 && day - w.StartedDay >= 60) { c.Npc++; c.Net += w.Stages.Sum(s => s.Net(day, WorkStage.RingDays)) / WorkStage.RingDays; }
                chains[w.Chain] = c;
            }
            row.Chains = string.Join("; ", chains.Select(kv => $"{kv.Key} {kv.Value.Open} open" + (kv.Value.Npc > 0 ? $", {kv.Value.Npc} NPC aged 60+ d net {kv.Value.Net / kv.Value.Npc:N0}/d each" : "")));
        }
        // the price level: the median of goods' mean reference price over their base price
        var ratios = new List<int>(); var books = markets.AllBooks().ToList();
        for (int g = 0; g < cat.Count; g++)
        {
            ushort id = (ushort)g; if (cat.IsPiece(id)) continue; double bp = cat.Def(id).BasePriceMitesPerKg; if (bp < 0.05) continue;
            double sum = 0; int n = 0; foreach (var b in books) { sum += b.Goods[id].RefMicro; n++; }
            if (n == 0) continue; ratios.Add((int)(sum / n * 1000.0 / Ravensguard.Sim.Economy.Markets.PriceCurve.Micro / bp * 1000));
        }
        ratios.Sort();
        if (ratios.Count > 0) row.Prices = $"all goods p25 {ratios[ratios.Count / 4]} p50 {ratios[ratios.Count / 2]} p75 {ratios[ratios.Count * 3 / 4]} p90 {ratios[ratios.Count * 9 / 10]} permille of base; " + string.Join(", ", new[] { "iron_ore", "iron_bar", "charcoal", "clay", "bricks", "logs", "planks", "firewood", "wheat", "bread" }.Where(g => cat.TryIdOf(g, out _)).Select(g =>
        {
            var id = cat.IdOf(g); double sm = 0, mid = 0; foreach (var b in books) { sm += b.Goods[id].RefMicro; mid += b.MidMicro(id); }
            double bp = Math.Max(0.001, cat.Def(id).BasePriceMitesPerKg) * Ravensguard.Sim.Economy.Markets.PriceCurve.Micro / 1000.0;
            return $"{g} ref {sm / books.Count / bp:P0} mid {mid / books.Count / bp:P0}";
        }));
        row.RefPricePermille = ratios.Count == 0 ? 0 : ratios[ratios.Count / 2];
        if (cat.TryIdOf("charcoal", out var chId))        // P7-EA3e: the burners the growth opened, and where charcoal is dear (over 1.2 of its reference)
        {
            var prodSys = run.Economy.Production; int crews = 0, staffed = 0, dear = 0; var kinds = prodSys.Content.Kinds;
            foreach (var b in prodSys.Businesses) if (b.State == Ravensguard.Sim.Production.BusinessState.Open && kinds[b.Kind].Id == "charcoal_crew") { crews++; staffed += b.Staff.Count; }
            foreach (var b in books) if (b.MidRatioPermille(chId) > 1200) dear++;
            row.Prices += $"; charcoal burners open {crews} with {staffed} hands, books with charcoal over 1.2 of its reference {dear}/{books.Count}";
        }
        void Fuel(string good, out long kg, out int cover, out int price)
        {
            kg = 0; cover = 0; price = 0; if (!cat.TryIdOf(good, out var id)) return;
            long stock = 0, demand = 0; double ps = 0; foreach (var b in books) { stock += b.Goods[id].StockUnits; demand += b.Goods[id].Demand; ps += b.MidMitesPerKg(id); }
            kg = stock / 1000; cover = (int)(demand <= 0 ? 0 : stock * 1000 / demand); price = (int)(ps / Math.Max(1, books.Count) * 1000 / Math.Max(0.001, cat.Def(id).BasePriceMitesPerKg));
        }
        Fuel("charcoal", out row.CharcoalKg, out row.CharcoalCoverPm, out row.CharcoalPricePm); Fuel("iron_bar", out row.IronBarKg, out row.IronBarCoverPm, out row.IronBarPricePm);
        long cm = Made("charcoal"), bm = Made("iron_bar"); row.CharcoalMadeKg = cm - _charMade0; row.IronBarMadeKg = bm - _barMade0; _charMade0 = cm; _barMade0 = bm;
        row.DayMsMean = _dayMsSum / TimeConst.DaysPerYear; row.HourMsMax = _hourMsMax;
        if (L3?.Works != null) { row.L3 = L3.Status(day); L3.NextYear(); }
    }

    public static string HeaderBorder() => "year   export-mites   (desk)   import-mites   (desk)   immigrant-stakes   emigrants+tolls";
    public static string FormatBorder(WarYearRow r) => $"{r.Year,4} {r.ExpMites,14:N0} {r.DeskExpMites,9:N0} {r.ImpMites,14:N0} {r.DeskImpMites,9:N0} {r.ImmigMites - r.ExpMites,18:N0} {r.EmigMites - r.ImpMites,17:N0}";

    public static string HeaderEx() => "year   immig-mites    emig-mites     mint-mites     melt  hoards       works open/new/closed  refprice  charcoal-kg cover-d price  bar-kg cover-d price   charcoal-made  bar-made  day-ms  hour-ms-max";

    public static string FormatEx(WarYearRow r) =>
        $"{r.Year,4} {r.ImmigMites,13:N0} {r.EmigMites,13:N0} {r.MintMites,14:N0} {r.MeltMites,6:N0} {r.HoardMites,10:N0}   {r.WorksOpen,5}/{r.WorksFounded}/{r.WorksClosed}   {r.RefPricePermille,8} {r.CharcoalKg,12:N0} {r.CharcoalCoverPm / 1000.0,7:0.0} {r.CharcoalPricePm,5} {r.IronBarKg,8:N0} {r.IronBarCoverPm / 1000.0,6:0.0} {r.IronBarPricePm,5} {r.CharcoalMadeKg,15:N0} {r.IronBarMadeKg,9:N0} {r.DayMsMean,7} {r.HourMsMax,12}";

    private void MeasureWorld(WarYearRow row)
    {
        var sim = run.Economy.World; var p = sim.People; long day = run.HourDone / 24; long idle = 0;
        for (int i = 0; i < p.Count; i++)
            if (p.Status[i] == Ravensguard.Sim.People.PersonStatus.Alive) { int age = sim.AgeOn(i, day); if (age is >= 16 and < 60 && p.Occupation[i] == Ravensguard.Sim.People.Occupation.None && !(sim.Households.ParcelCount[Math.Max(0, p.Household[i])] > 0)) idle++; }
        row.Idle = idle;
        int merchants = 0, solvent = 0;
        foreach (var b in run.Economy.Markets.AllBooks()) foreach (var m in b.Merchants) { merchants++; if (run.Econ.Ledger.Balance(m.Purse).Mites >= Ravensguard.Sim.Production.ProductionSystem.MerchantSolventMites) solvent++; }
        row.SolventPct = merchants == 0 ? 100 : solvent * 100 / merchants;
    }

    public static string Header() => "year   alive hunger-dead harvest-t   fed-md  hungry-md  unpaid-md  bought-kg   funded-mites  paid-mites  deserted  levies  arrears  siege-food  raised-aid raised-tall  idle  wages/d  money-supply  route-mean route-peak region-mean region-peak solvent% farm-demand-h farm-unmet-h  coin  mass  supplyhash";

    public static string Format(WarYearRow r) =>
        $"{r.Year,4} {r.Alive,7} {r.HungerDeaths,11} {r.HarvestT,9} {r.ManDaysFed,8} {r.ManDaysHungry,10} {r.ManDaysUnpaid,10} {r.BoughtKg,10} {r.FundedMites,13} {r.PaidMites,11} {r.Deserted,9} {r.Mustered,7} {r.ArrearsMites,8} {(r.SiegeDaysOfFood < 0 ? "-" : r.SiegeDaysOfFood.ToString()),11} {r.AidMites,10} {r.TallageMites,11} {r.Idle,5} {r.WagesPerDay,8} {r.MoneySupply,13} {r.RouteMeanMilli,10} {r.RoutePeakMilli,10} {r.RegionMeanMilli,11} {r.RegionPeakMilli,11} {r.SolventPct,8} {r.FarmDemandH,13} {r.FarmUnmetH,12}  {(r.CoinOk ? "ok" : "FAIL"),-4}  {(r.MassOk ? "ok" : "FAIL"),-4}  {r.Hash}";
}
