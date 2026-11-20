using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Medicine;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Disease;

/// <summary>
/// Disease and plague (P6-C4, docs/specs/phase6/disease-and-plague.md 7): DP-1 cough onset, DP-2 flux, DP-3 course and care, DP-4 a town in a plague, DP-5 a hamlet emptied, DP-6 the continent's spread,
/// DP-7 into the region, DP-8 wages, DP-9 land, DP-10 a day of deaths, DP-11 the slider, DP-12 determinism and the save. Each rig is its own new game of seed 1234's embark region.
/// </summary>
public class DiseaseTests(ITestOutputHelper output)
{
    /// <summary>The first day of the next game year (a day after the world's clock, so ages and seasons are the world's own).</summary>
    private static int Y(SimWorld w) { int d = (int)(w.State.HourDone / 24); return d - d % TimeConst.DaysPerYear + TimeConst.DaysPerYear; }
    private static int Winter(SimWorld w, int year, int k) => Y(w) + year * TimeConst.DaysPerYear + 45 + k % 15;          // the k-th day of a winter (doy 46..60)
    private static GameTime T(int day) => GameTime.FromGameDays(day);

    /// <summary>The day's outbox goes into the chronicle at once, as the barrier does after the hour.</summary>
    private static void Flush(RegionOutbox ob, SimWorld w)
    {
        foreach (var e in ob.Entries) w.Chronicle.Add(new ChronicleEntry(e.Id, e.When, e.TypeId, e.Subjects, e.Causes, e.Data, e.TextKey, e.Params));
    }

    private static void Run(SimWorld w, DiseaseBook b, int from, int days)
    {
        for (int d = from; d < from + days; d++) { var ob = new RegionOutbox(w.Ids); DiseaseEngine.Daily(w, b, d, T(d), ob); Flush(ob, w); }
    }

    private static int Biggest(SimWorld w)
    {
        var pop = new int[w.Population.Region.Settlements.Count]; var pt = w.People;
        for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive) pop[pt.HomeSettlement[i]]++;
        return Array.IndexOf(pop, pop.Max());
    }

    private static int Alive(SimWorld w, int s) { int n = 0; var pt = w.People; for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == s) n++; return n; }

    // ---------------------------------------------------------------- DP-1

    [Fact]
    public void DP1_CoughOnset_AboutTwoPercentADayWhenColdAndWet_AndAnotherFivePerSickHousemate()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People;
        var all = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.Household[i] >= 0).Select(i => pt.IdOf(i).Value).OrderBy(x => x).ToList();
        b.ExposureHours = _ => 5; b.Tracked = () => [.. all.Select(x => new TrackedPerson(x, false))];
        int trials = 0, onsets = 0;
        for (int k = 0; k < 12; k++)
        {
            b.Illnesses.Clear(); b.LastDay = -1;
            var ob = new RegionOutbox(w.Ids); DiseaseEngine.Daily(w, b, Winter(w, 0, k), T(Winter(w, 0, k)), ob);
            trials += all.Count; onsets += b.Illnesses.Count(kv => kv.Value.Kind == IllnessKind.Cough);
        }
        double pct = onsets * 100.0 / trials; output.WriteLine($"cough onset {onsets} of {trials} = {pct:0.00}%");
        Assert.InRange(pct, 1.5, 2.6);
        // outside winter nothing, and a hero who is merely in the cold winter (not wet) has 0.3 percent
        b.ExposureHours = _ => 0; b.Illnesses.Clear(); b.LastDay = -1;
        int spring = Y(w) + 5;
        DiseaseEngine.Daily(w, b, spring, T(spring), new RegionOutbox(w.Ids));
        Assert.Empty(b.Illnesses);
        // a sick housemate adds 5 percent: the people of a house with someone coughing fall ill at about 7
        var byHouse = all.Select(id => pt.IndexOf(new EntityId(id))).Where(i => w.Households.MemberCount[pt.Household[i]] >= 2).GroupBy(i => pt.Household[i]).Take(400).ToList();
        int t2 = 0, o2 = 0;
        for (int k = 0; k < 6; k++)
        {
            b.Illnesses.Clear(); b.LastDay = -1; b.ExposureHours = _ => 5;
            var rows = new List<int>();
            foreach (var g in byHouse)
            {
                var m = g.OrderBy(i => i).ToList();
                b.Illnesses[pt.IdOf(m[0]).Value] = new Illness { Person = pt.IdOf(m[0]).Value, Kind = IllnessKind.Cough, StartDay = Winter(w, 1, k) };    // the first is sick; the second is the one we watch
                rows.Add(m[1]);
            }
            b.Tracked = () => [.. rows.Select(i => new TrackedPerson(pt.IdOf(i).Value, false))];
            var ill0 = b.Illnesses.Keys.ToHashSet();
            DiseaseEngine.Daily(w, b, Winter(w, 1, k), T(Winter(w, 1, k)), new RegionOutbox(w.Ids));
            t2 += rows.Count; o2 += rows.Count(i => b.Illnesses.ContainsKey(pt.IdOf(i).Value));
        }
        double pct2 = o2 * 100.0 / t2; output.WriteLine($"with a sick housemate {o2} of {t2} = {pct2:0.0}%");
        Assert.InRange(pct2, 5.0, 9.5);
    }

    // ---------------------------------------------------------------- DP-2

    [Fact]
    public void DP2_Flux_ThreePercentADayOnUnboiledWater_AndTheDeathsGo3_1_AndHalfPercent()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People;
        var ids = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.Household[i] >= 0).Select(i => pt.IdOf(i).Value).OrderBy(x => x).ToList();
        b.UnboiledWaterDays = _ => 1; b.ExposureHours = _ => 0; b.Tracked = () => [.. ids.Select(x => new TrackedPerson(x, false))];
        int trials = 0, onsets = 0;
        for (int d = 0; d < 6; d++)
        {
            b.Illnesses.Clear(); b.LastDay = -1;
            int day = Y(w) + 20 + d;                      // summer
            DiseaseEngine.Daily(w, b, day, T(day), new RegionOutbox(w.Ids));
            trials += ids.Count; onsets += b.Illnesses.Count(kv => kv.Value.Kind == IllnessKind.Flux);
        }
        double pct = onsets * 100.0 / trials; output.WriteLine($"flux onset {onsets} of {trials} = {pct:0.00}%");
        Assert.InRange(pct, 2.4, 3.6);
        // the deaths of 10,000 cases, by care
        double Rate(IllnessCare care) { int dead = 0; for (ulong p = 1; p <= 10_000; p++) if (DiseaseEngine.DeathRoll(1234, p * 7919, 40, IllnessKind.Flux, care)) dead++; return dead / 100.0; }
        double u = Rate(IllnessCare.None), n = Rate(IllnessCare.Nursed), ph = Rate(IllnessCare.Physician);
        output.WriteLine($"flux deaths: untreated {u}%, nursed {n}%, physician {ph}%");
        Assert.InRange(u, 2.7, 3.3); Assert.InRange(n, 0.7, 1.3); Assert.InRange(ph, 0.2, 0.8);
    }

    // ---------------------------------------------------------------- DP-3

    [Fact]
    public void DP3_ANursedFeverLastsSixDaysNotSeven_AndThePhysiciansHundredMitesGoToHisBox()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People; var hht = w.Households;
        b.Tracked = () => []; b.UnboiledWaterDays = null;
        // a person alone in his house has nobody to nurse him; one with an adult beside him is nursed
        int Pick(Func<int, bool> pred) { for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive && pt.Household[i] >= 0 && pred(i)) return i; throw new InvalidOperationException(); }
        int alone = Pick(i => hht.MemberCount[pt.Household[i]] == 1 && w.AgeOn(i, Y(w)) >= 18), nursedRow = Pick(i => hht.MemberCount[pt.Household[i]] >= 2 && hht.Members(pt.Household[i]).Count(m => m != i && w.AgeOn(m, Y(w)) >= 12) >= 1);
        int start = Y(w) + 20;                              // summer: no cough rolls, and nobody is tracked
        int Lasts(int row)
        {
            ulong id = pt.IdOf(row).Value; b.Illnesses.Clear(); b.LastDay = -1;
            DiseaseEngine.Begin(w, b, id, row, IllnessKind.Ague, "ch_note_ill_marsh", start, T(start), new RegionOutbox(w.Ids));
            b.Illnesses[id].Checked = true;                                       // the death check is DP-2's
            int days = 1;
            for (int d = start + 1; d < start + 12; d++) { b.LastDay = -1; DiseaseEngine.Daily(w, b, d, T(d), new RegionOutbox(w.Ids)); if (!b.Illnesses.ContainsKey(id)) break; days++; }
            return days;
        }
        Assert.Equal(7, Lasts(alone) + 1);                                          // present on days 1..6, gone on the 7th pass: the ague's 7 days
        Assert.Equal(6, Lasts(nursedRow) + 1);
        // the physician's visit: a hundred mites from the sick one's household to the physician's box
        var book = new PhysicianBook(); w.Set(book); PhysicianSeeding.Run(w);
        var s = pt.HomeSettlement[alone];
        int town = Enumerable.Range(0, w.Population.Region.Settlements.Count).First(x => DiseaseEngine.PhysicianIn(w, x) != null);
        int sick = Pick(i => pt.HomeSettlement[i] == town && hht.MemberCount[pt.Household[i]] >= 1);
        var payer = w.Economy.HouseholdPurse[pt.Household[sick]];
        var led = w.Economy.Ledger; led.Mint(payer, new Coin(1000), SourceReason.Seed);
        var man = DiseaseEngine.PhysicianIn(w, town)!.Value;
        var ew = w.Get<EconomyWorld>();
        var payee = man.Business >= 0 && ew != null ? ew.Production.Businesses[man.Business].Purse : w.Economy.HouseholdPurse[pt.Household[pt.IndexOf(new EntityId(man.Person))]];
        long before = led.Balance(payer).Mites, boxBefore = led.Balance(payee).Mites;
        Assert.True(DiseaseEngine.PayPhysician(w, b, pt.IdOf(sick).Value, sick, town, plague: false, ordered: true));
        Assert.Equal(before - 100, led.Balance(payer).Mites); Assert.Equal(boxBefore + 100, led.Balance(payee).Mites);
        Assert.Equal(200, DiseaseEngine.VisitFee(plague: true));                      // and he charges double in a plague
        Assert.True(led.Audit().IsConserved);
        _ = s;
    }

    // ---------------------------------------------------------------- DP-4

    [Fact]
    public void DP4_APlagueInATown_ThirtyPercentDie_SurvivorsAreImmune_EveryDeathsChainReachesPlagueBegins()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!;
        b.Tracked = () => []; b.Severity = PlagueSeverity.Normal;
        int town = Biggest(w); int pop0 = Alive(w, town); int start = Y(w) + 30;
        var ob0 = new RegionOutbox(w.Ids);
        var gp = DiseaseEngine.BeginRegion(w, b, start, T(start), ob0, town, 0, 5); Flush(ob0, w);
        var ep = gp.Epidemics[town];
        var sw = System.Diagnostics.Stopwatch.StartNew(); int days = ep.EndDay - start + 14;
        Run(w, b, start + 1, days);
        sw.Stop(); output.WriteLine($"{days} days of a region-wide plague: {sw.Elapsed.TotalMilliseconds / days:0.00} ms a day");
        Assert.True(sw.Elapsed.TotalMilliseconds / days < TestBudget.Ms(60), "the plague's day stays far inside the 150 ms hour cap");
        double dead = ep.Dead * 100.0 / ep.PopStart; output.WriteLine($"town {pop0} people, {ep.Infected} infected, {ep.Dead} dead = {dead:0.0}%, {gp.Immune.Count} immune, {gp.Epidemics.Count} places reached");
        Assert.Equal(pop0, ep.PopStart);
        Assert.InRange(dead, 24, 36);
        Assert.InRange(ep.Infected * 100.0 / ep.PopStart, 40, 62);
        // survivors are immune for life: nobody immune is a case, nobody dead is immune
        Assert.DoesNotContain(gp.Cases.Keys, id => gp.Immune.Contains(id));
        foreach (var id in gp.Immune) { int row = w.People.IndexOf(new EntityId(id)); Assert.Equal(PersonStatus.Alive, w.People.Status[row]); }
        Assert.True(gp.Immune.Count >= ep.Infected - ep.Dead);
        // every chronicled plague death's chain reaches plague_begins
        var deaths = w.Chronicle.ByType("death").Where(e => e.Causes.Any(c => c.Kind == CauseKind.Disease && c.NoteKey == "ch_note_plague_death")).ToList();
        Assert.NotEmpty(deaths);
        foreach (var d in deaths)
        {
            var cause = d.Causes.First(c => c.NoteKey == "ch_note_plague_death");
            var e = w.Chronicle.Get(cause.EventId!.Value)!; Assert.Equal("plague_settlement", e.TypeId);
            var up = w.Chronicle.Get(e.Causes[0].EventId!.Value);
            for (int hop = 0; hop < 600 && up != null && up.TypeId != "plague_begins"; hop++) up = up.Causes.Count > 0 && up.Causes[0].EventId is { } eid ? w.Chronicle.Get(eid) : null;
            Assert.Equal("plague_begins", up?.TypeId);
        }
        Assert.True(w.Economy.Ledger.Audit().IsConserved);
    }

    // ---------------------------------------------------------------- DP-5

    [Fact]
    public void DP5_AHamletOfFiftyWithFortyFivePercentDead_IsAbandonedWithAQuarter_AndTheChronicleSaysWhy()
    {
        int hits = 0; const int N = 4000;
        for (int day = 0; day < N; day++) if (VitalMath.Roll(1234, day, 987654321, RngStreams.EpidemicRoll, 4, DiseaseRules.Current.S.HamletAbandonPct * 10_000)) hits++;
        double p = hits * 100.0 / N; output.WriteLine($"abandoned {p:0.0}% of {N}");
        Assert.InRange(p, 21.5, 28.5);
        // a real farmstead whose epidemic ended with 23 of 50 dead: on the first day the roll goes its way, the survivors leave and the entry names the plague
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People; var region = w.Population.Region;
        b.Tracked = () => [];
        int farm = Enumerable.Range(0, region.Settlements.Count).First(s => region.Settlements[s].Class == SettlementClass.Farmstead && Alive(w, s) > 0);
        int day0 = Y(w) + 30;
        for (int d = day0; d < day0 + 400; d++)
        {
            var gp = new GreatPlague { Id = 1, NameIndex = 2, StartDay = d - 40, BeginEntry = 0 };
            var ep = new Epidemic { Settlement = farm, StartDay = d - 40, EndDay = d, PopStart = 50, Dead = 23, Active = true, Entry = 77 };
            gp.Epidemics[farm] = ep; b.Plague = gp;
            int before = Alive(w, farm);
            var ob = new RegionOutbox(w.Ids);
            DiseaseEngine.CloseEpidemics(w, b, gp, d, T(d), ob);
            if (Alive(w, farm) == 0 && before > 0)
            {
                var e = Assert.Single(ob.Entries, x => x.TypeId == "settlement_emptied");
                Assert.Equal(CauseKind.Disease, e.Causes[0].Kind); Assert.Equal(77UL, e.Causes[0].EventId!.Value.Value);
                Assert.True(w.Economy.Ledger.Audit().IsConserved);
                return;
            }
        }
        Assert.Fail("the hamlet was never emptied in 400 days");
    }

    // ---------------------------------------------------------------- DP-6

    [Fact]
    public void DP6_TheContinentsSpread_FollowsTheRoadGraphOnly_AndTheChanceGoesByTradeVolume()
    {
        using var r = new FaithRig();
        var st = r.State.ContinentState!; var cp = st.Plague;
        // the chance by volume: the median edge 40 percent, twice it 80, a fraction of it 20
        Assert.Equal(400_000, ContinentPlagueStep.SpreadPpm(1000, 1000)); Assert.Equal(800_000, ContinentPlagueStep.SpreadPpm(5000, 1000)); Assert.Equal(200_000, ContinentPlagueStep.SpreadPpm(10, 1000));
        int origin = Enumerable.Range(0, ContinentState.Regions).Where(i => st.Summaries[i].Pop > 0 && !st.Summaries[i].Detailed).OrderByDescending(i => st.EdgesOf[i].Length).First();
        cp.Active = true; cp.Id = 1; cp.Origin = origin; cp.StartSeason = 100; cp.Infected[origin] = (100, 140);
        var seen = new HashSet<int> { origin }; var order = new List<int> { origin };
        long baseSec = (long)60 * TimeConst.SecondsPerYear;
        for (int k = 1; k <= 8; k++)
        {
            long season = 100 + k;
            ContinentPlagueStep.Run(st, id => st.Summaries[id].Detailed, new GameTime(baseSec + k * TimeConst.SecondsPerSeason), season);
            foreach (int reg in cp.Infected.Keys.Where(x => !seen.Contains(x)))
            {
                Assert.Contains(st.EdgesOf[reg], ei => seen.Contains(st.Edges[ei].Other(reg)));          // it came along a road from a region the plague already had
                seen.Add(reg); order.Add(reg);
            }
            if (!cp.Active) break;
        }
        output.WriteLine($"regions infected over 8 seasons: {order.Count} ({string.Join(",", order.Take(12))})");
        Assert.True(order.Count > 1);
        // the dead of the regions nobody watches are in the closed form: the people are fewer than they were
        Assert.True(cp.DeadEstimate > 0);
        // a plague that starts: a rare yearly roll, once at the first season of a year, keyed on the continent and the year
        int starts = 0; for (int y = 0; y < 2000; y++) if (ContinentPlagueStep.Starts(1234, y, PlagueSeverity.Normal)) starts++;
        output.WriteLine($"normal: {starts} starts in 2000 continent-years");
        Assert.InRange(starts, 8, 36);
    }

    // ---------------------------------------------------------------- DP-7

    [Fact]
    public void DP7_IntoTheRegion_ACaravanFromAnInfectedNeighbourCarriesItWithThirtyPercent_AQuarantinedTownTakesAFifth()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var link = w.Get<Ravensguard.Sim.Economy.Markets.ContinentLink>()!; var st = link.State; var cp = b.Continent;
        b.Tracked = () => [];
        int nb = st.EdgesOf[link.Region].Select(ei => st.Edges[ei].Other(link.Region)).First();
        cp.Active = true; cp.Id = 1; cp.NameIndex = 1; cp.Origin = nb; cp.StartSeason = 1; cp.Infected[nb] = (1, 99); cp.NowDay = 0;
        // no caravan: nothing; a caravan home from the infected neighbour: P 30 percent each
        int day0 = Y(w) + 30;
        b.LastDay = -1; DiseaseEngine.Daily(w, b, day0, T(day0), new RegionOutbox(w.Ids));
        Assert.Null(b.Plague);
        int carried = 0; const int N = 600;
        for (int i = 0; i < N; i++)
        {
            var c = new Ravensguard.Sim.Caravans.Caravan { Id = EntityId.Make(IdKind.Other, 0x7000_0000UL + (ulong)i), Home = link.Region, Region = link.Region, Neighbour = nb, State = Ravensguard.Sim.Caravans.CaravanState.Inbound, Settlement = 0 };
            st.Caravans.Add(c);
            b.Plague = null; b.LastDay = -1;
            DiseaseEngine.Daily(w, b, day0 + 1, T(day0 + 1), new RegionOutbox(w.Ids));
            if (b.Plague != null) carried++;
            st.Caravans.Remove(c.Id);
        }
        double p = carried * 100.0 / N; output.WriteLine($"carried {carried} of {N} = {p:0.0}%");
        Assert.InRange(p, 25, 35);
        // a quarantined town's spread is a fifth
        var region = w.Population.Region;
        int a = 0, t = Enumerable.Range(1, region.Settlements.Count - 1).First(x => DiseaseEngine.InTownPpm(w, 0, x, quarantined: false) > 0);
        long open = DiseaseEngine.InTownPpm(w, a, t, quarantined: false), closed = DiseaseEngine.InTownPpm(w, a, t, quarantined: true);
        Assert.Equal(open * 20 / 100, closed);
    }

    // ---------------------------------------------------------------- DP-8, DP-9

    [Fact]
    public void DP8_AThirdOfTheWorkersDead_WagesRiseHalfAgain_AndFallBackTenPercentOfTheGapASeason()
    {
        Assert.Equal(1500, PlagueEconomy.WageTarget(300, 200, 0));
        Assert.Equal(1000, PlagueEconomy.WageTarget(300, 200, 100));                 // a hundred idle hands fill the posts first
        Assert.Equal(1000, PlagueEconomy.WageTarget(300, 290, 0));                   // an ordinary fall (under the dead zone) moves nothing
        Assert.Equal(2000, PlagueEconomy.WageTarget(300, 20, 0));                    // and it never passes double
        int m = 1500; var steps = new List<int>();
        for (int i = 0; i < 3; i++) { m = PlagueEconomy.EaseWage(m, 1000); steps.Add(m); }
        Assert.Equal([1450, 1405, 1365], steps);
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People; int day = Y(w) + 1;
        PlagueEconomy.Seasonal(w, b, day);                                           // the first measure takes the peak
        Assert.Equal(1000, w.Economy.WageMultPm);
        int workers = PlagueEconomy.Workers(w, day), killed = 0;
        for (int i = 0; i < pt.Count && killed < workers / 3; i++)
            if (pt.Status[i] == PersonStatus.Alive && w.AgeOn(i, day) is >= 16 and < 60 && pt.Household[i] >= 0 && i != r.State.Hero!.PersonIndex) { Demography.Kill(w, i, DeathCause.Disease, T(day), new RegionOutbox(w.Ids)); killed++; }
        int idle = PlagueEconomy.Idle(w, day + 15), now = PlagueEconomy.Workers(w, day + 15);
        int expected = PlagueEconomy.WageTarget(workers, now, idle);
        PlagueEconomy.Seasonal(w, b, day + 15);
        output.WriteLine($"workers {workers} -> {now}, idle {idle}, wage term {w.Economy.WageMultPm} (target {expected})");
        Assert.Equal(expected, w.Economy.WageMultPm);
        Assert.InRange(w.Economy.WageMultPm, 1001, 2000);
        int cur = w.Economy.WageMultPm;
        PlagueEconomy.Seasonal(w, b, day + 30);
        Assert.True(w.Economy.WageMultPm <= cur);
    }

    [Fact]
    public void DP9_AThirdOfTheHoldingsEmpty_LandPricesHalve_AndRecoverTenPercentOfTheGapASeason()
    {
        Assert.Equal(500, PlagueEconomy.LandTarget(300, 200));
        Assert.Equal(1000, PlagueEconomy.LandTarget(300, 290));
        Assert.Equal(400, PlagueEconomy.LandTarget(300, 50));                        // never below four tenths
        int m = 500; var steps = new List<int>();
        for (int i = 0; i < 3; i++) { m = PlagueEconomy.EaseLand(m, 1000); steps.Add(m); }
        Assert.Equal([550, 595, 635], steps);
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; int day = Y(w) + 1;
        PlagueEconomy.Seasonal(w, b, day);
        var hht = w.Households; var people = w.People; int occupied = PlagueEconomy.HouseholdsNow(w), emptied = 0;
        for (int hh = 0; hh < hht.Count && emptied < occupied / 3; hh++)
        {
            if (hht.MemberCount[hh] == 0 || hh == people.Household[r.State.Hero!.PersonIndex]) continue;
            foreach (int p in hht.Members(hh).ToList()) Demography.Kill(w, p, DeathCause.Disease, T(day), new RegionOutbox(w.Ids));
            emptied++;
        }
        PlagueEconomy.Seasonal(w, b, day + 15);
        output.WriteLine($"households {occupied} -> {PlagueEconomy.HouseholdsNow(w)}, land term {w.Economy.LandMultPm}");
        Assert.InRange(w.Economy.LandMultPm, 400, 700);
        // the land book prices at the term
        var land = Ravensguard.Sim.Land.LandWiring.For(r.State, r.Content);
        Assert.Equal(1000L * w.Economy.LandMultPm / 1000, land.Scarce(1000));
    }

    // ---------------------------------------------------------------- DP-10

    [Fact]
    public void DP10_APlagueDay_ManyDeathsAtOnce_SettleWithCoinAndMassExact()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; b.Tracked = () => [];
        int town = Biggest(w); int day = Y(w) + 30; var pt = w.People;
        var gp = new GreatPlague { Id = 1, NameIndex = 0, StartDay = day - 6, BeginEntry = 1 }; b.Plague = gp;
        var ep = new Epidemic { Settlement = town, StartDay = day - 6, EndDay = day + 20, Active = true, Entry = 2 }; gp.Epidemics[town] = ep;
        int cases = 0;
        for (int i = 0; i < pt.Count; i++)
            if (pt.Status[i] == PersonStatus.Alive && pt.Household[i] >= 0 && i != r.State.Hero!.PersonIndex) { gp.Cases[pt.IdOf(i).Value] = new PlagueCase { Person = pt.IdOf(i).Value, Settlement = pt.HomeSettlement[i], InfectedDay = day - 6, DeathDay = day, EndDay = day + 4 }; cases++; }
        long coin = w.Economy.Ledger.Audit().InPurses.Mites; long alive = w.Vital.Alive;
        var ob = new RegionOutbox(w.Ids);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DiseaseEngine.Daily(w, b, day, T(day), ob);
        sw.Stop();
        int died = (int)(alive - w.Vital.Alive);
        output.WriteLine($"{cases} cases, {died} deaths in one day in {sw.Elapsed.TotalMilliseconds:0.0} ms");
        Assert.InRange(died * 100.0 / cases, 52, 68);
        Assert.Equal(gp.Dead, died);
        Assert.True(w.Economy.Ledger.Audit().IsConserved); Assert.True(w.Economy.MassAuditHolds());
        Assert.Equal(coin, w.Economy.Ledger.Audit().InPurses.Mites);              // nobody's coin vanishes with him: it moves to the estate and the heirs
    }

    // ---------------------------------------------------------------- DP-11

    [Fact]
    public void DP11_TheSlider_OffNeverStarts_FrequentIsTwoAndAHalfPercent_MortalityHalvesAndGrowsTheDead()
    {
        int off = 0, normal = 0, freq = 0;
        for (ulong seed = 1; seed <= 10; seed++)
            for (int y = 0; y < 1000; y++)
            {
                if (ContinentPlagueStep.Starts(seed, y, PlagueSeverity.Off)) off++;
                if (ContinentPlagueStep.Starts(seed, y, PlagueSeverity.Normal)) normal++;
                if (ContinentPlagueStep.Starts(seed, y, PlagueSeverity.Frequent)) freq++;
            }
        output.WriteLine($"10,000 continent-years: off {off}, normal {normal}, frequent {freq}");
        Assert.Equal(0, off);
        Assert.InRange(normal * 100.0 / 10_000, 0.7, 1.3); Assert.InRange(freq * 100.0 / 10_000, 2.0, 3.0);
        var rules = DiseaseRules.Current;
        Assert.Equal(300_000, rules.DeathPpm(IllnessKind.Plague, IllnessCare.None, PlagueSeverity.Rare));
        Assert.Equal(900_000, rules.DeathPpm(IllnessKind.Plague, IllnessCare.None, PlagueSeverity.Frequent));      // 60 x 1.5 = 90, the cap
        Assert.Equal(900_000, rules.DeathPpm(IllnessKind.Plague, IllnessCare.None, PlagueSeverity.Frequent) is var f && f > 900_000 ? 0 : f);
        // a town at mortality x1.5: attack unchanged, 45 percent dead
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; b.Tracked = () => []; b.Severity = PlagueSeverity.Frequent;
        int town = Biggest(w); int start = Y(w) + 30;
        var ob0 = new RegionOutbox(w.Ids); var gp = DiseaseEngine.BeginRegion(w, b, start, T(start), ob0, town, 0, 5); Flush(ob0, w);
        var ep = gp.Epidemics[town]; Run(w, b, start + 1, ep.EndDay - start + 14);
        double dead = ep.Dead * 100.0 / ep.PopStart; output.WriteLine($"frequent: {ep.Dead} of {ep.PopStart} dead = {dead:0.0}%");
        Assert.InRange(dead, 36, 54);
    }

    // ---------------------------------------------------------------- DP-12

    [Fact]
    public void DP12_SaveMidEpidemic_ThenLoad_ThenSave_IsByteIdentical_AndTheEpidemicContinuesIdentically()
    {
        using var a = new FaithRig();
        var w = a.Sim; var b = a.State.Disease!; b.Tracked = () => [];
        int town = Biggest(w); int start = Y(w) + 30;
        var ob0 = new RegionOutbox(w.Ids); DiseaseEngine.BeginRegion(w, b, start, T(start), ob0, town, 3, 5); Flush(ob0, w);
        Run(w, b, start + 1, 10);
        Assert.NotEmpty(b.Plague!.Cases);
        var file = WorldSave.Capture(a.State, "h", "0.1");
        Assert.Contains(DiseaseSave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        var loaded = WorldSave.Restore(SaveCodec.Decode(bytes), a.Content, "h", null, a.W.Continent, a.W.History);
        loaded.Content = a.Content;
        Assert.Equal(b.Hash(), loaded.Disease!.Hash());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(loaded, "h", "0.1")));
        using var rt = new Ravensguard.Sim.Worlds.WorldRuntime(loaded, a.Content, new MemoryRegionPager());
        var w2 = loaded.HomeRecords!.Sim; var b2 = loaded.Disease!; b2.Tracked = () => [];
        Assert.Same(b2, w2.Get<DiseaseBook>());
        Run(w, b, start + 11, 15); Run(w2, b2, start + 11, 15);
        Assert.Equal(b.Hash(), b2.Hash());
        Assert.Equal(w.Vital.Alive, w2.Vital.Alive);
        Assert.Equal(b.Plague!.Dead, b2.Plague!.Dead);
        output.WriteLine($"after 25 days: {b.Plague.Dead} dead, {b.Plague.Immune.Count} immune, hash {b.Hash():X16}");
    }

    // ---------------------------------------------------------------- the desk and the system

    [Fact]
    public void TheHeroNursesAndCallsAPhysician_ThroughTheDesk_AndTheSystemRunsOnTheTick()
    {
        using var r = new FaithRig();
        var w = r.Sim; var b = r.State.Disease!; var pt = w.People;
        var party = r.State.Party!; int hrow = r.State.Hero!.PersonIndex; ulong hid = pt.IdOf(hrow).Value;
        Assert.Contains(DiseaseWiring.TrackedOf(r.State), t => t.Id == hid && t.Hero);
        var cmd = r.Do(DiseaseDesk.Nurse(0, party.Hero), 3 * TimeConst.DaysPerYear * 86400L);
        Assert.Equal(HeroResultCode.Refused, cmd.Code);                                     // he is not ill
        int day = Y(w) + 20; r.State.Disease!.LastDay = -1;
        DiseaseEngine.Begin(w, b, hid, hrow, IllnessKind.Ague, "ch_note_ill_marsh", day, T(day), new RegionOutbox(w.Ids));
        var ok = r.Do(DiseaseDesk.Nurse(0, party.Hero), day * 86400L);
        Assert.True(ok.Ok, ok.Text);
        Assert.True(b.Illnesses[hid].NursedUntil >= day);
        var view = DiseaseView.Of(r.State, day + 1);
        Assert.Contains(view, v => v.Person == hid && v.IsHero);
        // the system is discovered and runs its pass at hour 0 of the day
        Assert.Contains(AbstractSystemRegistry.CreateDefault().All, s => s.Name == "disease");
    }
}
