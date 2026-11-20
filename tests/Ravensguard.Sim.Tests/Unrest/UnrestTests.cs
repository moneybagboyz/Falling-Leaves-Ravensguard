using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Tests.Rule;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Unrest;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Unrest;

/// <summary>A new game of seed 1234's embark region with the unrest book attached (P6-E1), driven by hand at the barrier: contexts, days, hours and the hero's commands.</summary>
internal sealed class UnrestRig : IDisposable
{
    public RuleWorldRig R { get; } = new();
    public WorldState State => R.State;
    public UnrestBook B => State.Unrest!;
    public RuleBook Rule => R.Book;
    public SimWorld Sim => R.Sim;
    public int Day { get; set; }
    public RegionOutbox Ob { get; private set; }
    /// <summary>The settlement the scenarios use: the most households of a fief with a living lord.</summary>
    public int S { get; }
    public int Fief { get; }
    public List<int> Households { get; } = [];

    public UnrestRig()
    {
        Assert.NotNull(State.Unrest);
        Day = (int)(R.Runtime.HourDone / 24) + 1;
        Ob = new RegionOutbox(State.Ids);
        var hht = Sim.Households; var region = State.Population.Region;
        var counts = new Dictionary<int, int>();
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0) counts[hht.Settlement[h]] = counts.GetValueOrDefault(hht.Settlement[h]) + 1;
        S = counts.Where(kv => region.Settlements[kv.Key].FiefIndex is >= 0 and var f && Rule.FiefOf(f) is { Holder: >= 0, Forfeited: false }).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        Fief = region.Settlements[S].FiefIndex;
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0 && hht.Settlement[h] == S) Households.Add(h);
    }

    public UnrestCtx C(int? day = null, long? hour = null, bool withOb = true)
    {
        int d = day ?? Day;
        return UnrestWiring.Context(Sim, B, Rule, withOb ? Ob : null, d, hour ?? d * 24L);
    }

    /// <summary>Hunger and crowding (3 points) on the first <paramref name="k"/> households of the settlement, none on the rest.</summary>
    public void Aggrieve(int k)
    {
        var e = State.Economy!;
        for (int i = 0; i < Households.Count; i++) e.Grievances[Households[i]] = i < k ? Grievance.Hunger | Grievance.Crowding : Grievance.None;
    }

    public void Flush() { foreach (var e in Ob.Entries) State.Chronicle.Add(new ChronicleEntry(e.Id, e.When, e.TypeId, e.Subjects, e.Causes, e.Data, e.TextKey, e.Params)); Ob = new RegionOutbox(State.Ids); }

    public IEnumerable<ChronicleEntry> Entries(string type) => State.Chronicle.All.Where(e => e.TypeId == type);
    public ChronicleEntry? Find(ulong id) => State.Chronicle.Get(new EntityId(id));

    public void Dispose() => R.Dispose();
}

public class UnrestTests(ITestOutputHelper output)
{
    private static UnrestBook Kinds()
    {
        var db = Ravensguard.Sim.Tests.Seamless.SeamWorld.Get(1234).Content;
        return UnrestWiring.Bind(new UnrestBook(), db, 1);
    }

    // ------------------------------------------------------------------ UR-1

    [Fact]
    public void UR1_GrievancePoints_CrushingAndHungry_IsFour_AggrievedWithADoleAndHighTax_OneNot()
    {
        var b = Kinds();
        var hungryCrushing = new HouseholdInputs(true, TaxLevel.Crushing, 0, false, false, false, false, false, false);
        var k = UnrestRules.Breakdown(b, hungryCrushing, [], out int total);
        Assert.Equal(4, total); Assert.Equal([("hunger", 2), ("tax_crushing", 2)], k);
        Assert.True(UnrestRules.Aggrieved(b.Settings, total));
        var dole = new HouseholdInputs(true, TaxLevel.High, 0, false, false, false, false, false, true);
        UnrestRules.Breakdown(b, dole, [], out int t2);
        Assert.Equal(1, t2); Assert.False(UnrestRules.Aggrieved(b.Settings, t2));
        // the cap and the levy (a subsidy 1, a tallage 2)
        UnrestRules.Breakdown(b, new HouseholdInputs(true, TaxLevel.Crushing, 2, true, true, true, true, true, false), [], out int t3);
        Assert.Equal(b.Settings.PointsCap, t3);
    }

    // ------------------------------------------------------------------ UR-2

    [Fact]
    public void UR2_TheLadder_FortySixPercentForFiveDays_ARiot_ThirtyOne_EvasionOnly_AboutHalfPayNothing()
    {
        using var r = new UnrestRig();
        int n = r.Households.Count; Assert.True(n >= 30, $"{n} households");
        // the rules: 100 households, 46 aggrieved is 460 and stands at the riot rung; 31 is 310, the evasion rung
        var s = new UnrestSettingsDef { Id = "unrest" };
        Assert.Equal(460, UnrestRules.UnrestPm(s, 46, 100, false)); Assert.Equal(3, UnrestRules.RungFor(s, 460)); Assert.Equal(2, UnrestRules.RungFor(s, 310));
        Assert.False(UnrestRules.RiotDue(s, 460, 4, false)); Assert.True(UnrestRules.RiotDue(s, 460, 5, false)); Assert.False(UnrestRules.RiotDue(s, 310, 30, false));
        Assert.True(UnrestRules.RiotDue(s, 350, 0, true)); Assert.False(UnrestRules.RiotDue(s, 340, 0, true));
        // in the world: 46% for five daily passes raises a riot on the fifth
        int k = (int)Math.Ceiling(n * 0.46); r.Aggrieve(k);
        int u = UnrestRules.UnrestPm(r.B.Settings, k, n, false); Assert.True(u >= 450, $"{u}");
        for (int i = 0; i < 4; i++) { UnrestEngine.Daily(r.C(r.Day + i)); Assert.Empty(r.B.Riots); }
        UnrestEngine.Daily(r.C(r.Day + 4));
        var riot = Assert.Single(r.B.Riots); Assert.Equal(r.S, riot.Settlement); Assert.Equal(RiotState.Gathering, riot.State);
        Assert.Equal(u, r.B.UnrestOf(r.S)); Assert.Equal("hunger", riot.TopKind); Assert.Equal(RiotTarget.Barn, riot.Target);
        var rise = r.Ob.Entries.Single(e => e.TypeId == "riot_rises");
        Assert.Contains(rise.Causes, c => c.NoteKey == "ch_note_ur_hunger" && c.NoteParams!.Any(p => p.Value == k));
    }

    [Fact]
    public void UR2_ThirtyOnePercent_NoRiot_ButTheEvasionRung_AndAboutHalfOfTheAggrievedPayNoHearthTax()
    {
        using var r = new UnrestRig();
        int n = r.Households.Count; int k = (int)Math.Ceiling(n * 0.31); r.Aggrieve(k);
        for (int i = 0; i < 12; i++) UnrestEngine.Daily(r.C(r.Day + i));
        Assert.Empty(r.B.Riots); Assert.Equal(2, r.B.RungOf(r.S));
        Assert.NotNull(r.State.Economy); Assert.True(r.Sim.Get<EconomyWorld>()!.Taxes.RiotsElsewhere); Assert.NotNull(r.Sim.Get<EconomyWorld>()!.Taxes.EvasionHook);
        var c = r.C(r.Day + 12);
        int evaders = r.Households.Take(k).Count(h => UnrestEngine.Evades(c, r.S, h)), others = r.Households.Skip(k).Count(h => UnrestEngine.Evades(c, r.S, h));
        output.WriteLine($"{evaders} of {k} aggrieved evade, {others} of {n - k} others");
        Assert.Equal(0, others); Assert.InRange(evaders, k * 3 / 10, k * 7 / 10);
        Assert.Contains(r.Ob.Entries, e => e.TypeId == "unrest_rung");
        Assert.Contains(r.Ob.Entries, e => e.TypeId == "unrest_grumbling");
    }

    // ------------------------------------------------------------------ UR-3

    [Fact]
    public void UR3_ARiotForBread_TheCrowdGoesToTheBarnAt10_TheGrainMovesToNamedRioters_MassConserved()
    {
        using var r = new UnrestRig();
        int n = r.Households.Count; r.Aggrieve((int)Math.Ceiling(n * 0.6));
        for (int i = 0; i < 5; i++) UnrestEngine.Daily(r.C(r.Day + i));
        var riot = Assert.Single(r.B.Riots); int d = r.Day + 4;
        int pct = UnrestRules.CrowdPct(r.B.Settings, r.B.UnrestOf(r.S)); Assert.InRange(pct, 10, 40);
        Assert.Contains(riot.Leader, riot.Crowd); Assert.True(riot.Crowd.Count >= r.B.Settings.CrowdMinPeople);
        // the lord's barn holds grain
        var e = r.State.Economy!; int lh = r.Sim.Population.FiefLordHousehold[r.Fief]; Assert.True(lh >= 0);
        var barn = e.OtherStores.TryGetValue(Ravensguard.Sim.Farming.FarmIds.Barn(lh), out var b0) ? b0 : e.OpenStore(Ravensguard.Sim.Goods.StoreKind.Barn, Ravensguard.Sim.Farming.FarmIds.Barn(lh));
        ushort g = e.Food.Staples.First(x => e.Food.IsGrain(x)); barn.Create(g, 400_000, Ravensguard.Sim.Goods.CreateReason.Harvest);
        long Total() => e.OtherStores.Values.Sum(s => s.Qty(g)) + Enumerable.Range(0, e.HouseholdCount).Sum(h => e.HouseholdStore[h].Qty(g));
        long before = Total(), coin0 = e.Ledger.Audit().InPurses.Mites;
        UnrestEngine.Hourly(r.C(d, d * 24L + 9)); Assert.Equal(RiotState.Gathering, riot.State);           // not before the gather hour
        UnrestEngine.Hourly(r.C(d, d * 24L + 10));
        Assert.Equal(RiotState.Active, riot.State); Assert.Equal(100_000, riot.LootGrams);               // a quarter of the barn
        Assert.Equal(before, Total()); Assert.Equal(coin0, e.Ledger.Audit().InPurses.Mites);
        var crowdHouseholds = riot.Crowd.Select(p => r.Sim.People.Household[r.Sim.People.IndexOf(new EntityId(p))]).Distinct().ToList();
        Assert.All(crowdHouseholds, h => Assert.True(e.HouseholdStore[h].Qty(g) > 0));
        output.WriteLine($"crowd {riot.Crowd.Count} of the aggrieved, {r.State.Law?.Crimes.Values.Count(c => c.Kind == "theft") ?? 0} thefts");
        if (r.State.Law is { } law) foreach (var c in law.Crimes.Values.Where(c => c.Kind == "theft")) Assert.Contains(c.Doer, riot.Crowd);
        // it burns out after six hours when nothing else ends it (or the guards disperse it at the next)
        UnrestEngine.Hourly(r.C(d, d * 24L + 11));
        Assert.Equal(RiotState.Ended, riot.State); r.Flush();
        Assert.Equal(riot.RiseEntry, Assert.Single(r.Entries("riot_ends")).Causes[0].EventId!.Value.Value);
    }

    // ------------------------------------------------------------------ UR-4

    [Fact]
    public void UR4_ASentenceHarsherThanTheCulturesKey_AddsAPointToTheConvictedsKin_ThroughTheLawsHook()
    {
        using var r = new UnrestRig();
        int h = r.Households[0]; var p = r.Sim.People; int row = Enumerable.Range(0, p.Count).First(i => p.IsAlive(i) && p.Household[i] == h);
        var law = LawWorld.From(r.State, r.R.Content);
        Assert.NotNull(law.Grievance);
        law.Grievance!(p.IdOf(row), 1);
        var hid = r.Sim.Households.IdOf(h).Value;
        var live = r.B.Live(hid, r.Day).Single(); Assert.Equal("kin_punished", live.Kind); Assert.Equal(1, live.Points);
        Assert.Empty(r.B.Live(hid, r.Day + 60));                                                         // a year
        law.Grievance(p.IdOf(row), -1);
        Assert.Contains(r.B.Live(hid, r.Day), x => x.Kind == "wronged");
    }

    // ------------------------------------------------------------------ UR-5

    [Fact]
    public void UR5_LoweringTheTaxFromCrushingToHigh_TakesAPointFromEveryHouseholdWithIt_AndUnrestIsRecomputedAtTheNextPass()
    {
        using var r = new UnrestRig();
        var tax = r.Sim.Get<EconomyWorld>()!.Taxes; tax.SetLevel(r.Fief, TaxLevel.Crushing);
        var e = r.State.Economy!; foreach (int h in r.Households) e.Grievances[h] = Grievance.Hunger;       // 2 + 2 = 4 aggrieved
        var c = r.C(); var p = UnrestEngine.PlaceOf(c, r.S);
        Assert.All(r.Households, h => Assert.Equal(4, UnrestEngine.PointsOf(c, UnrestEngine.PlaceOf(c, r.S), h).Points));
        UnrestEngine.Daily(r.C(r.Day)); int u0 = r.B.UnrestOf(r.S); Assert.Equal(1000, u0);
        Assert.True(UnrestConcede.LowerTax(r.C(r.Day + 1), r.Fief)); Assert.Equal(TaxLevel.High, tax.LevelOfFief(r.Fief));
        var c2 = r.C(r.Day + 1);
        Assert.All(r.Households, h => Assert.Equal(3, UnrestEngine.PointsOf(c2, UnrestEngine.PlaceOf(c2, r.S), h).Points));      // still aggrieved (hunger 2 + tax 1)
        UnrestConcede.Dole(c2, r.Fief, Party.OfFief(r.Fief));                                              // the barn feeds the hungry: hunger points cleared while it lasts
        var c3 = r.C(r.Day + 2);
        Assert.All(r.Households, h => Assert.Equal(1, UnrestEngine.PointsOf(c3, UnrestEngine.PlaceOf(c3, r.S), h).Points));
        UnrestEngine.Daily(r.C(r.Day + 2)); Assert.Equal(0, r.B.UnrestOf(r.S)); Assert.Equal(0, r.B.RungOf(r.S));
        r.Flush(); Assert.Equal(2, r.Entries("unrest_concession").Count());
        Assert.Equal(coinOf(r), r.State.Economy!.Ledger.Audit().InPurses.Mites);
    }
    private static long coinOf(UnrestRig r) => r.State.Economy!.Ledger.Audit().InPurses.Mites;

    // ------------------------------------------------------------------ UR-6 to UR-8

    private static Riot MakeRiot(UnrestRig r, int unrest = 520)
    {
        int n = r.Households.Count; r.Aggrieve((int)Math.Ceiling(n * 0.6));
        var c = r.C(); var census = UnrestEngine.TakeCensus(r.Sim, c.S.AdultAge, r.Day);
        var a = UnrestEngine.Assess(c, r.S, census.Households[r.S]);
        var su = new SettlementUnrest { Unrest = unrest }; r.B.Settlements[r.S] = su;
        return UnrestEngine.StartRiot(c, r.S, r.Fief, su, a, census, "")!;
    }

    [Fact]
    public void UR6_ARiotPutDownWithThreeDeathsAt520_RaisesARevolt_WhoseChainReachesTheGrievances()
    {
        using var r = new UnrestRig();
        var riot = MakeRiot(r); var c = r.C(r.Day, r.Day * 24L + 11);
        riot.State = RiotState.Active; riot.ActedHour = c.Hour - 1;
        Assert.Equal(3, UnrestEngine.Kill(c, riot, 3)); Assert.Equal(3, riot.Deaths);
        UnrestEngine.End(c, riot, RiotEnd.Dispersed);
        var v = Assert.Single(r.B.Revolts); Assert.Equal(RevoltState.Pending, v.State); Assert.Equal(r.Fief, v.Fief); Assert.Equal(riot.Id, v.RiotId);
        Assert.NotEmpty(v.Rebels);
        r.Flush();
        // the chain: revolt -> the riot's end -> the riot's rise -> the hunger
        var rise = r.Find(v.RiseEntry)!; Assert.Equal("revolt_rises", rise.TypeId);
        var end = r.Find(rise.Causes.First(x => x.NoteKey == "ch_note_ur_revolt_riot").EventId!.Value.Value)!; Assert.Equal("riot_ends", end.TypeId);
        var start = r.Find(end.Causes[0].EventId!.Value.Value)!; Assert.Equal("riot_rises", start.TypeId);
        Assert.Contains(start.Causes, x => x.NoteKey == "ch_note_ur_hunger");
        // the dead carry a cause back to the riot and their kin a grievance
        Assert.True(r.Sim.Vital.DeathsByCause[(int)DeathCause.Violence] >= 3);
        Assert.Contains(r.B.Rows.Values.SelectMany(x => x), x => x.Kind == "kin_punished");
    }

    [Fact]
    public void UR7_TheRebelsWin_TheFiefDeedPassesToTheClaimant_TheChainReachesTheGrievances_TheLiegesReactionByOpinion()
    {
        using var r = new UnrestRig();
        var riot = MakeRiot(r); var c = r.C(r.Day, r.Day * 24L + 11);
        riot.State = RiotState.Active; riot.ActedHour = c.Hour - 1; UnrestEngine.End(c, riot, RiotEnd.Dispersed);
        var census = UnrestEngine.TakeCensus(r.Sim, c.S.AdultAge, r.Day);
        var v = UnrestEngine.StartRevolt(c, r.Fief, r.S, riot, UnrestEngine.Assess(c, r.S, census.Households[r.S]), census)!;
        var old = r.Rule.FiefOf(r.Fief)!.Holder;
        v.Rebels = [.. census.Adults[r.S].Select(i => r.Sim.People.IdOf(i).Value).Where(x => x != (ulong)old).Take(300).Order()]; v.Rebels0 = v.Rebels.Count; v.Defenders = 1;
        UnrestEngine.Fight(r.C(v.ResolveDay, v.ResolveDay * 24L + 12), v);
        Assert.Equal(RevoltState.Won, v.State);
        var f = r.Rule.FiefOf(r.Fief)!; Assert.Equal((long)v.Claimant, f.Holder); Assert.NotEqual(old, f.Holder);
        r.Flush();
        var won = r.Find(v.EndEntry)!; Assert.Equal("revolt_won", won.TypeId);
        Assert.Contains(won.Params!, p => p.Name == "how" && p.Key.StartsWith("ur_liege_"));
        var rise = r.Find(won.Causes[0].EventId!.Value.Value)!; Assert.Equal("revolt_rises", rise.TypeId);
        var end = r.Find(rise.Causes.First(x => x.NoteKey == "ch_note_ur_revolt_riot").EventId!.Value.Value)!;
        var start = r.Find(end.Causes[0].EventId!.Value.Value)!; Assert.Contains(start.Causes, x => x.NoteKey == "ch_note_ur_hunger");
        output.WriteLine($"claimant kind {v.ClaimantKind}, dead {v.Dead}");
    }

    [Fact]
    public void UR8_TheRebelsLose_TheLeadersAreChargedWithTreason_UnrestIsHeldDownTwoSeasons()
    {
        using var r = new UnrestRig();
        var riot = MakeRiot(r); var c = r.C(r.Day, r.Day * 24L + 11);
        riot.State = RiotState.Active; riot.ActedHour = c.Hour - 1; UnrestEngine.End(c, riot, RiotEnd.Dispersed);
        var census = UnrestEngine.TakeCensus(r.Sim, c.S.AdultAge, r.Day);
        var v = UnrestEngine.StartRevolt(c, r.Fief, r.S, riot, UnrestEngine.Assess(c, r.S, census.Households[r.S]), census)!;
        var holder = r.Rule.FiefOf(r.Fief)!.Holder; v.Defenders = 100_000;
        int day = v.ResolveDay; UnrestEngine.Fight(r.C(day, day * 24L + 12), v);
        Assert.Equal(RevoltState.Lost, v.State); Assert.Equal(holder, r.Rule.FiefOf(r.Fief)!.Holder);
        var su = r.B.Peek(r.S)!; Assert.Equal(day + 30, su.FearUntil);
        if (r.State.Law != null) Assert.Contains(r.State.Law.Crimes.Values, x => x.Kind == "treason");
        // the fear takes 200 off the unrest for the two seasons, then lets go
        int k = (int)Math.Ceiling(r.Households.Count * 0.5); r.Aggrieve(k);
        UnrestEngine.Daily(r.C(day + 1)); int held = r.B.UnrestOf(r.S);
        UnrestEngine.Daily(r.C(day + 31)); int free = r.B.UnrestOf(r.S);
        Assert.Equal(Math.Max(0, free - 200), held); Assert.True(free > held);
    }

    // ------------------------------------------------------------------ UR-9, UR-10

    [Fact]
    public void UR9_ClosingACommon_OnePointToItsUsersForTwoYears()
    {
        using var r = new UnrestRig();
        int users = UnrestConcede.Enclose(r.B, r.Sim, r.S, r.Day, 77);
        Assert.True(users > 0, "no landholders in the settlement");
        var hht = r.Sim.Households; var hid = hht.IdOf(r.Households.First(h => hht.ParcelCount[h] > 0)).Value;
        var row = r.B.Live(hid, r.Day + 119).Single(); Assert.Equal("enclosure", row.Kind); Assert.Equal(1, row.Points); Assert.Equal(77UL, row.Entry);
        Assert.Empty(r.B.Live(hid, r.Day + 120));
    }

    [Fact]
    public void UR10_HistoryParity_TheThresholdsAreOneFunction_TheLadderOfTheSpec()
    {
        var s = new UnrestSettingsDef { Id = "unrest" };
        Assert.Equal(0, UnrestRules.RungFor(s, 149)); Assert.Equal(1, UnrestRules.RungFor(s, 150)); Assert.Equal(2, UnrestRules.RungFor(s, 300)); Assert.Equal(3, UnrestRules.RungFor(s, 450)); Assert.Equal(4, UnrestRules.RungFor(s, 600));
        Assert.True(UnrestRules.RevoltDue(s, 600, 30, 0)); Assert.False(UnrestRules.RevoltDue(s, 600, 29, 0)); Assert.True(UnrestRules.RevoltDue(s, 500, 0, 3)); Assert.False(UnrestRules.RevoltDue(s, 499, 0, 3));
        Assert.Equal(10, UnrestRules.CrowdPct(s, 450)); Assert.Equal(40, UnrestRules.CrowdPct(s, 800)); Assert.Equal(25, UnrestRules.CrowdPct(s, 625));
    }

    // ------------------------------------------------------------------ slave rising (D1) and the hero

    [Fact]
    public void ASlaveRising_IsARevoltOfTheBonded_NoClaimant_NobodyTakesAFief_AndD1sEntryIsTheCause()
    {
        using var r = new UnrestRig();
        var ob = r.Ob; var c = r.C(); var pt = r.Sim.People;
        var bonded = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.Household[i] >= 0 && r.Sim.Households.Settlement[pt.Household[i]] == r.S).Take(12).Select(i => pt.IdOf(i).Value).ToList();
        var holder = r.Rule.FiefOf(r.Fief)!.Holder;
        var v = UnrestEngine.BeginSlaveRising(c, r.S, bonded[0], bonded, 99);
        Assert.True(v.Bonded); Assert.Equal(0UL, v.Claimant); Assert.Equal(99UL, v.RiseEntry);
        UnrestEngine.Fight(r.C(v.ResolveDay, v.ResolveDay * 24L + 12), v);
        Assert.NotEqual(RevoltState.Pending, v.State); Assert.Equal(holder, r.Rule.FiefOf(r.Fief)!.Holder);
        var end = ob.Entries.Single(e => e.TypeId is "slave_rising_won" or "slave_rising_lost"); Assert.Equal(99UL, end.Causes[0].EventId!.Value.Value);
        Assert.True(v.Dead >= 1);
    }

    [Fact]
    public void TheHerosCommands_JoinMediateDoleAndPardon_GoThroughTheDesk()
    {
        using var r = new UnrestRig();
        var riot = MakeRiot(r); long sec = (riot.StartDay + 0L) * 86400 + 3600;
        var res = r.R.Do(UnrestDesk.Join(riot.Id, sec), sec); Assert.Equal(HeroResultCode.Ok, res.Code); Assert.Equal(1, riot.HeroSide);
        res = r.R.Do(UnrestDesk.Dole(r.Fief, sec), sec); Assert.Equal(HeroResultCode.Refused, res.Code);   // not his fief
        res = r.R.Do(UnrestDesk.Mediate(999, sec), sec); Assert.Equal(HeroResultCode.Refused, res.Code);
        r.Flush();
        Assert.Contains(r.Entries("riot_joined"), e => e.Params!.Any(p => p.Name == "place"));
    }

    // ------------------------------------------------------------------ UR-11

    [Fact]
    public void UR11_TheBookRoundTrips_AndALoadAndResaveKeepsTheBytes_AndAFreshWorldHasNoSection()
    {
        using var r = new UnrestRig();
        var file0 = WorldSave.Capture(r.State, "h", "0.1"); Assert.DoesNotContain(UnrestSave.SectionName, file0.Sections.Keys);
        var riot = MakeRiot(r); r.B.Add(r.Sim.Households.IdOf(r.Households[0]).Value, "enclosure", r.Day, 5); r.B.DoleUntil[r.Fief] = r.Day + 9; r.B.Pardoned.Add(1234);
        r.B.Revolts.Add(new Revolt { Id = r.B.NextRevolt++, Fief = r.Fief, Settlement = r.S, StartDay = r.Day, ResolveDay = r.Day + 3, Claimant = 5, ClaimantKind = 1, Rebels = [3, 4], Rebels0 = 2, Defenders = 9 });
        var bytes = SaveCodec.SerializeCompressed(UnrestSave.Capture(r.B));
        var back = SaveCodec.DeserializeCompressed<UnrestSave>(bytes).Restore();
        Assert.Equal(r.B.Hash(), back.Hash()); Assert.Equal(bytes, SaveCodec.SerializeCompressed(UnrestSave.Capture(back)));
        var file = WorldSave.Capture(r.State, "h", "0.1"); Assert.Contains(UnrestSave.SectionName, file.Sections.Keys);
        var enc = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(enc), r.R.Content, "h", null, r.R.W.Continent, r.R.W.History); st.Content = r.R.Content;
        Assert.Equal(enc, SaveCodec.Encode(WorldSave.Capture(st, "h", "0.1"))); Assert.Equal(r.B.Hash(), st.Unrest!.Hash());
        Assert.NotNull(riot);
    }

    [Fact]
    public void UR11_AHungryFiefForAYear_TheSameTwice_AndASaveMidRiotResumesToTheSameBook()
    {
        string Run(bool resume)
        {
            using var r = new UnrestRig(); int n = r.Households.Count;
            for (int d = 0; d < 45; d++)
            {
                int k = d < 30 ? (int)Math.Ceiling(n * 0.5) : (int)Math.Ceiling(n * 0.1); r.Aggrieve(k);
                int day = r.Day + d;
                UnrestEngine.Daily(r.C(day));
                for (int h = 0; h < 24; h++) UnrestEngine.Hourly(r.C(day, day * 24L + h));
                if (resume && d == 8) { var back = SaveCodec.DeserializeCompressed<UnrestSave>(SaveCodec.SerializeCompressed(UnrestSave.Capture(r.B))).Restore(); UnrestWiring.Bind(back, r.R.Content, r.State.Seed); r.State.Unrest = back; r.Sim.Set(back); }
                r.Flush();
            }
            return r.State.Unrest!.Hash().ToString("X") + "/" + r.State.Chronicle.Hash() + "/" + r.State.Unrest.Riots.Count;
        }
        string a = Run(false), b = Run(false), c = Run(true);
        output.WriteLine(a);
        Assert.Equal(a, b); Assert.Equal(a, c);
        Assert.EndsWith("/1", a);
    }
}
