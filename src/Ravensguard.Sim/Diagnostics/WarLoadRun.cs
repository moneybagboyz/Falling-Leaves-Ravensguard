using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
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
}

/// <summary>The war-load soak on a <see cref="WarLoadRun"/>: runs the scenario and the control, one row a year. The gates that need the audit's own measures (WL-3 to WL-12, WL-14, WL-15) are EA2's; the first cut gates WL-1 and WL-2 (coin and mass exact every day) and prints the rest.</summary>
public sealed class WarLoadSoak(WarLoadRun run, int years, bool control = false)
{
    /// <summary>The realm raises the coin of each campaign (<see cref="WarFunding"/>); false leaves the chest to whatever the treasury holds (the first cut's behaviour).</summary>
    public bool RealmRaises { get; set; } = true;
    private long _routeSum, _routePeak, _regionSum, _regionPeak, _samples, _wage0, _demand0, _unmet0;
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
        _wage0 = run.Econ.Ledger.Flow(TransferReason.Wage).Mites; _demand0 = run.Economy.Farming.TotalDemandH; _unmet0 = run.Economy.Farming.TotalUnmetH;
        for (int y = 1; y <= years; y++)
        {
            tally.Reset(); _routeSum = _routePeak = _regionSum = _regionPeak = _samples = 0;
            for (int d = 1; d <= TimeConst.DaysPerYear; d++)
            {
                int day = startYearDay + (y - 1) * TimeConst.DaysPerYear + d - 1;
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
                run.AfterSlot(day);
                if (load != null)
                {
                    load.After(day);
                    if (!run.CoinConserved || !run.MassOk) Fail($"day {day}: coin {run.CoinConserved}, mass {run.MassOk}");
                    if (camp != null && y == camp.ToYear && d == camp.ToDay) { if (sup.IsBlockaded(siegeTown)) sup.SiegeBlockade(siegeTown, false, load.Host); load.End(); load = null; camp = null; }
                }
                Sample();
                run.AdvanceToDayEnd(day);
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
        row.MoneySupply = row.RegionCoin;
        long wage = led.Flow(TransferReason.Wage).Mites; row.WagesPerDay = (wage - _wage0) / TimeConst.DaysPerYear; _wage0 = wage;
        row.RouteMeanMilli = _samples == 0 ? 0 : _routeSum / _samples; row.RoutePeakMilli = _routePeak; row.RegionMeanMilli = _samples == 0 ? 0 : _regionSum / _samples; row.RegionPeakMilli = _regionPeak;
        var fm = run.Economy.Farming; row.FarmDemandH = fm.TotalDemandH - _demand0; row.FarmUnmetH = fm.TotalUnmetH - _unmet0; _demand0 = fm.TotalDemandH; _unmet0 = fm.TotalUnmetH;
        MeasureWorld(row);
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
