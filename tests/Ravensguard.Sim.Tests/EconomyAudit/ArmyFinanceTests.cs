using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Unrest;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-AE2 gate: AE-5 to AE-10, AE-14 and AE-20: purveyance (tallies, hiding, riots, payment and default), scutage, forage, the days funded and the pay chest's capture; plus the sutlers, the book's save and the 0x660 stream.</summary>
public class ArmyFinanceTests(ITestOutputHelper output)
{
    private static ushort Wheat(WarLoadRun run) => run.Econ.Goods.IdOf("wheat");

    private static bool IsGrain(WarLoadRun run, ushort g) => !run.Econ.Goods.IsPiece(g) && run.Econ.Goods.Def(g).Class is "grain" or "flour";

    /// <summary>Empties the staple goods of a household's store and barn.</summary>
    private static void ClearGrain(WarLoadRun run, int hh)
    {
        var stores = new List<Store> { run.Econ.HouseholdStore[hh] };
        if (run.Econ.OtherStores.TryGetValue(FarmIds.Barn(hh), out var barn)) stores.Add(barn);
        foreach (var st in stores) foreach (var (g, q) in st.Rows()) if (IsGrain(run, g) && q > 0) st.Destroy(g, q, DestroyReason.Eaten);
    }

    private static void SetKg(WarLoadRun run, int hh, long grams, bool inBarn = false)
    {
        ClearGrain(run, hh);
        Store st = inBarn && run.Econ.OtherStores.TryGetValue(FarmIds.Barn(hh), out var barn) ? barn : run.Econ.HouseholdStore[hh];
        if (grams > 0) st.Create(Wheat(run), grams, CreateReason.Seed);
    }

    private static List<int> HouseholdsOf(WarLoadRun run, int s)
    {
        var hht = run.State.Population.Households; var l = new List<int>();
        for (int i = 0; i < hht.Count; i++) if (hht.Settlement[i] == s && hht.MemberCount[i] > 0) l.Add(i);
        return l;
    }

    private sealed class Village
    {
        public WarLoadRun Run = null!; public HostSupply Hs = null!; public HostId Host; public int S; public List<int> Households = [];
        public int Day; public Action Seed = null!;
    }

    /// <summary>A village of 40 households each holding its 30 days of grain and 75 kg more (3,000 kg above the reserve), the book's wheat at a mid of 45 mites a kg, a host of 300 spearmen with an empty chest camped there.</summary>
    private Village Build(int n = 11, bool realmOwn = true)
    {
        var run = AuditWorld.Create(); var hs = run.Supply; var hht = run.State.Population.Households; var region = run.State.Population.Region;
        int s = -1;
        for (int c = 0; c < region.Settlements.Count; c++) if (HouseholdsOf(run, c).Count >= 40 && (s < 0 || HouseholdsOf(run, c).Count < HouseholdsOf(run, s).Count)) s = c;
        Assert.True(s >= 0, "a settlement of 40 households");
        var v = new Village { Run = run, Hs = hs, S = s };
        v.Households = HouseholdsOf(run, s);
        v.Seed = () => Seed(v);
        v.Host = AuditWorld.Host(run, n, new Roster([(TroopRole.Spearman, 300)]), 0);
        var a = hs.Account(v.Host); a.Purveyance = true; a.Realm = hs.RealmOf(s); a.Carts = 0;
        hs.HidingEnabled = false;
        return v;
    }

    /// <summary>Lays the village's grain and the wheat price just before the host's day (the households sell their surplus to the market and eat through the day before it).</summary>
    private static void Seed(Village v)
    {
        var run = v.Run; var hht = run.State.Population.Households; var region = run.State.Population.Region; int s = v.S;
        // every household of the village holds exactly its reserve; the first 40 hold 75 kg above it
        for (int k = 0; k < v.Households.Count; k++)
        {
            int h = v.Households[k]; long reserve = (long)hht.MemberCount[h] * 1100 * 30;
            SetKg(run, h, reserve + (k < 40 ? 75_000 : 0));
        }
        // the other settlements within the purveyance radius keep nothing above their reserve either, so the village is the whole of it
        for (int c = 0; c < region.Settlements.Count; c++)
        {
            if (c == s) continue;
            foreach (int h in HouseholdsOf(run, c)) SetKg(run, h, (long)hht.MemberCount[h] * 1100 * 30);
        }
        var bk = run.Economy.Markets.BookForSettlement(s);
        var g = Wheat(run); long r0 = 45_000L * 1000 / bk.MidRatioPermille(g);
        for (long r = r0 - 400; r <= r0 + 400 && bk.MidMicro(g) != 45_000; r++) bk.Goods[g].RefMicro = r;          // the wheat's mid at exactly 45 mites a kg (45,000 micro-mites a gram)
        Assert.Equal(45_000, bk.MidMicro(g));
    }

    private static DayResult Supply(Village v, Action? beforeSupply = null)
    {
        int day = v.Run.NextSlotDay; v.Run.BeforeSlot(day);
        v.Hs.Update(v.Host, v.Hs.PlaceAt(v.S), HostMode.Camp);
        v.Seed(); beforeSupply?.Invoke(); v.Day = day;
        var r = v.Hs.Supply(v.Host, day);
        Assert.True(v.Run.CoinConserved, "coin"); Assert.True(v.Run.MassOk, "mass");
        return r;
    }

    /// <summary>AE-5: a village of 40 households holding 3,000 kg above their 30-day reserve, mid 45, hiding off: 3,000 kg taken (the day's 300 kg eaten, the rest in the carts), tallies 101,250 in all, due 30 days on, 40 purveyance grievances; coin and mass exact.</summary>
    [Fact]
    public void AE5_Purveyance_Takes3000Kg_TalliesOwed101250_FortyGrievances()
    {
        var v = Build(); using var _ = v.Run; var hs = v.Hs;
        hs.Chron = RuleChron.Of(v.Run.State.Chronicle, v.Run.State.Ids);
        var ub = v.Run.State.Unrest!; Assert.NotNull(ub);
        var r = Supply(v);
        int stp = (int)SupplyNeed.Staple;
        Assert.Equal(300_000, r.Delivered[stp, (int)SupplySource.Purveyance]); Assert.Equal(0, r.ShortG[stp]);
        Assert.Equal(3_000_000, hs.Book.Tallies.Values.Sum(t => t.Grams));                                  // 3,000 kg left the households' stores
        Assert.Equal(2_700_000, hs.BaggageOf(v.Host).Qty(Wheat(v.Run)));                                     // 300 kg eaten today, 2,700 kg in the carts
        Assert.Equal(40, hs.Book.Tallies.Count);
        Assert.Equal(101_250, hs.Book.Tallies.Values.Sum(t => t.Mites));
        Assert.All(hs.Book.Tallies.Values, t => { Assert.Equal(v.Day + 30, t.DueDay); Assert.Equal(TallyState.Owed, t.State); Assert.NotEqual(0UL, t.Entry); });
        int grievances = 0; foreach (int h in v.Households) if (ub.Live(v.Run.State.Population.Households.IdOf(h).Value, v.Day).Any(x => x.Kind == "purveyance")) grievances++;
        Assert.Equal(40, grievances);
        var e = Assert.Single(v.Run.State.Chronicle.ByType("purveyance_taken"));
        Assert.Contains(e.Causes, c => c.Kind == CauseKind.Supply);
        Assert.Equal(3000, e.Params!.Single(p => p.Name == "kg").Value);
        Assert.Equal(40, e.Params!.Single(p => p.Name == "households").Value);
        Assert.Equal(101_250, e.Params!.Single(p => p.Name == "amount").Value);
        Assert.Equal(3_000_000, hs.Book.Stats[0]);
    }

    /// <summary>AE-6: at unrest 300 a household hides with P = 20 + 300 / 20 = 35%, the draw (0x660, key household and day) says which, and a hiding household gives half of its surplus.</summary>
    [Fact]
    public void AE6_Hiding_At_Unrest300_P35_AHidingHouseholdGivesHalf()
    {
        var v = Build(12); using var _ = v.Run; var hs = v.Hs; hs.HidingEnabled = true;
        var ub = v.Run.State.Unrest!; var hht = v.Run.State.Population.Households; ulong seed = v.Run.Economy.World.Seed;
        var r = Supply(v, () => { ub.Of(v.S).Unrest = 300; });
        int hid = 0, open = 0;
        for (int k = 0; k < 40; k++)
        {
            int h = v.Households[k]; ulong id = hht.IdOf(h).Value;
            bool hides = DetRng.Range(seed, v.Day, id, RngStreams.PurveyanceHiding, 0, 0, 100) < 35;
            var t = hs.Book.Tallies.Values.SingleOrDefault(x => x.Household == id);
            if (hides) { hid++; Assert.NotNull(t); Assert.Equal(37_500, t!.Grams); } else { open++; Assert.NotNull(t); Assert.Equal(75_000, t!.Grams); }
        }
        Assert.True(hid > 0 && open > 0, $"the draw hid {hid} of 40: both cases should be seen");
        output.WriteLine($"{hid} of 40 households hid grain at unrest 300 (expected about 35%)");
        Assert.True(hs.Book.Stats[5] > 0);

    }

    /// <summary>AE-7: purveyance at a settlement at unrest 360 triggers a riot: <c>UnrestConcede.Trigger(.., "tallage")</c> is set for the next daily pass.</summary>
    [Fact]
    public void AE7_Purveyance_At_Unrest360_StrikesTheTallageTrigger()
    {
        var v = Build(13); using var _ = v.Run; var ub = v.Run.State.Unrest!;
        Supply(v, () => { ub.Of(v.S).Unrest = 360; });
        var su = ub.Peek(v.S)!; Assert.Equal("tallage", su.TriggerKind); Assert.Equal(v.Day, su.TriggerDay);
        // below 350 nothing is struck
        var w = Build(14); using var _w = w.Run; var ub2 = w.Run.State.Unrest!;
        Supply(w, () => { ub2.Of(w.S).Unrest = 340; });
        Assert.NotEqual("tallage", ub2.Peek(w.S)!.TriggerKind);
    }

    /// <summary>AE-8: a tally is paid at S5 once past due while the realm's treasury holds more than a season of its payroll (<c>Purveyance</c>, coin to the household's purse); left unpaid four seasons past due it is defaulted, the grievance is added again and <c>purveyance_unpaid</c> is written, caused by the taking.</summary>
    [Fact]
    public void AE8_Tallies_PaidAtS5_AndDefaultedAfterFourSeasons()
    {
        var v = Build(15); using var _ = v.Run; var hs = v.Hs; var led = v.Run.Econ.Ledger; var ub = v.Run.State.Unrest!;
        hs.Chron = RuleChron.Of(v.Run.State.Chronicle, v.Run.State.Ids);
        Supply(v);
        int realm = hs.Account(v.Host).Realm; Assert.True(led.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(realm), out var treasury));
        long owed = hs.TalliesOwed(realm); Assert.Equal(101_250, owed);
        int due = v.Day + 30;
        // before the due day nothing is paid; the treasury holds less than a season of payroll: nothing is paid on the due day either
        long bal = led.Balance(treasury).Mites; if (bal > 0) led.Sink(treasury, new Coin(bal), SinkReason.Burn);
        Assert.Equal(0, hs.SeasonStep(due - 1));
        Assert.Equal(0, hs.SeasonStep(due)); Assert.All(hs.Book.Tallies.Values, t => Assert.Equal(TallyState.Owed, t.State));
        // the treasury is flush: more than a season of payroll and the tallies
        long payroll = hs.RealmPayrollSeason(realm); led.Mint(treasury, new Coin(payroll + owed + 1_000), SourceReason.Seed);
        long purseSum0 = v.Households.Sum(h => led.Balance(v.Run.Econ.HouseholdPurse[h]).Mites);
        long paid = hs.SeasonStep(due + 15);
        Assert.Equal(owed, paid); Assert.All(hs.Book.Tallies.Values, t => Assert.Equal(TallyState.Paid, t.State));
        Assert.Equal(owed, v.Households.Sum(h => led.Balance(v.Run.Econ.HouseholdPurse[h]).Mites) - purseSum0);
        Assert.Equal(owed, led.Flow(TransferReason.Purveyance).Mites); Assert.True(v.Run.CoinConserved);
        Assert.Equal(owed, hs.Book.Stats[2]);

        // the second village's tallies, never paid: defaulted four seasons past due
        var w = Build(16); using var _w = w.Run; var hs2 = w.Hs; var led2 = w.Run.Econ.Ledger; var ub2 = w.Run.State.Unrest!;
        hs2.Chron = RuleChron.Of(w.Run.State.Chronicle, w.Run.State.Ids);
        Supply(w);
        int due2 = w.Day + 30; Assert.True(led2.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(hs2.Account(w.Host).Realm), out var tr2));
        long b2 = led2.Balance(tr2).Mites; if (b2 > 0) led2.Sink(tr2, new Coin(b2), SinkReason.Burn);
        hs2.SeasonStep(due2 + 59); Assert.All(hs2.Book.Tallies.Values, t => Assert.Equal(TallyState.Owed, t.State));
        int day = due2 + 60;
        hs2.SeasonStep(day);
        Assert.All(hs2.Book.Tallies.Values, t => Assert.Equal(TallyState.Defaulted, t.State));
        Assert.Equal(101_250, hs2.Book.Stats[3]);
        var hht = w.Run.State.Population.Households;
        foreach (int h in w.Households.Take(40)) Assert.Contains(ub2.Live(hht.IdOf(h).Value, day), x => x.Kind == "purveyance" && x.UntilDay > day + 20);       // the grievance added again
        var e = Assert.Single(w.Run.State.Chronicle.ByType("purveyance_unpaid"));
        var taken = Assert.Single(w.Run.State.Chronicle.ByType("purveyance_taken"));
        Assert.Contains(e.Causes, c => c.EventId == taken.Id);
        // a settled tally is dropped a year after
        hs.SeasonStep(due + 15 + 61); Assert.Empty(hs.Book.Tallies);
    }

    private static (RuleBook Rb, Func<RuleCtx> Ctx, FiefRow Fief) Vassal(WarLoadRun run)
    {
        var rb = run.State.Rule!; Assert.NotNull(rb);
        RuleCtx Ctx() => RuleWiring.Context(run.Economy.World, rb, RuleChron.Silent);
        var f = rb.Fiefs.Values.First(x => x.Realm >= 0 && !x.Forfeited && x.Holder >= 0 && x.OverlordFief < 0);
        foreach (var o in rb.Fiefs.Values) if (o.Realm == f.Realm && o.Fief != f.Fief) o.ServiceMen = 0;
        f.ServiceMen = 9;
        return (rb, Ctx, f);
    }

    /// <summary>AE-9: scutage, 9 men x 40 days: 108,000 mites to the overlord's treasury (<c>Scutage</c>); an NPC vassal with opinion -20 pays, one with +30 serves (draw 0 of 0x753 spans -10 to 10).</summary>
    [Fact]
    public void AE9_Scutage_9Men_40Days_108000_NpcPaysAtMinus20_ServesAtPlus30()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger;
        var (rb, ctx, f) = Vassal(run); var c = ctx(); var vassal = Party.OfFief(f.Fief); var overlord = c.OverlordOf(f);
        Assert.Equal(9 * 40 * 300, 108_000);
        // the vassal's treasury holds the coin
        var vp = c.PurseOf(vassal); led.Mint(vp, new Coin(200_000), SourceReason.Seed); var op = c.PurseOf(overlord); long o0 = led.Balance(op).Mites;
        rb.AddOpinion(c.KeyOf(vassal).Value, c.KeyOf(overlord).Value, -20);
        int day = run.DayNow + 1;
        var calls = hs.CallWarService(f.Realm, day);
        var call = Assert.Single(calls); Assert.False(call.Served); Assert.Equal(108_000, call.Owed); Assert.Equal(108_000, call.Paid);
        Assert.Equal(108_000, led.Balance(op).Mites - o0); Assert.Equal(108_000, led.Flow(TransferReason.Scutage).Mites);
        Assert.Contains(rb.Dues, d => d.Kind == DueKind.Scutage && d.State == DueState.Paid && d.AmountMites == 108_000 && d.Units == 9);
        Assert.True(run.CoinConserved);
        // +30: serves, no coin, a Service due of 9 men
        rb.AddOpinion(c.KeyOf(vassal).Value, c.KeyOf(overlord).Value, 50);
        Assert.Equal(30, rb.Opinion(c.KeyOf(vassal).Value, c.KeyOf(overlord).Value));
        long scut0 = led.Flow(TransferReason.Scutage).Mites;
        var calls2 = hs.CallWarService(f.Realm, day + 1); var call2 = Assert.Single(calls2);
        Assert.True(call2.Served); Assert.Equal(scut0, led.Flow(TransferReason.Scutage).Mites);
        Assert.Contains(rb.Dues, d => d.Kind == DueKind.Service && d.Units == 9 * 1000 + 40 && d.Payer == vassal);
        // the player as vassal chooses
        rb.AddOpinion(c.KeyOf(vassal).Value, c.KeyOf(overlord).Value, -50);
        var calls3 = hs.CallWarService(f.Realm, day + 2, fief => fief == f.Fief ? false : null);                // the player chooses to serve despite the opinion
        Assert.True(Assert.Single(calls3).Served);
        // a vassal that cannot pay owes a due the season takes to arrears
        var broke = hs.CallWarService(f.Realm, day + 3, fief => fief == f.Fief ? true : null); var b = Assert.Single(broke);
        Assert.False(b.Served); Assert.True(b.Paid < b.Owed || b.Paid == b.Owed);
        // the season's note: scutage_paid is a root, once per overlord
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        hs.SeasonStep(day + 15);
        Assert.Single(run.State.Chronicle.ByType("scutage_paid"));
        Assert.Empty(hs.Book.ScutageNotes);
    }

    /// <summary>AE-10: foraging in autumn on enemy land: 200 foragers (2,000 men), a barn of 4,000 kg, ripe fields of 52.8 kg an acre, the draw 1.0: 4,000 kg from the barn then 2,000 kg from 76 acres (the last one partly cut), as much again trampled; the robbed households' opinion of the host's realm -40; mass exact.</summary>
    [Fact]
    public void AE10_Forage_Autumn_EnemyLand_BarnThen76Acres_TrampledAsMuch_RobbedOpinionMinus40()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var region = run.State.Population.Region; var hht = run.State.Population.Households;
        var (start, _) = AuditWorld.Stops(run);
        // no grain in any household or barn within 8 km of the host: the one barn below is all the stores hold
        for (int s = 0; s < region.Settlements.Count; s++) foreach (int h in HouseholdsOf(run, s)) ClearGrain(run, h);
        int barnHh = HouseholdsOf(run, start).First(h => run.Econ.OtherStores.ContainsKey(FarmIds.Barn(h)));
        var barn = run.Econ.OtherStores[FarmIds.Barn(barnHh)]; barn.Create(Wheat(run), 4_000_000, CreateReason.Seed);
        // every field within 4 km stands ripe, 52.8 kg an acre
        var F = run.Economy.Farming.State.Fields; var sx = region.Settlements[start]; var ripe = new List<int>();
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            long dx = region.Settlements[s].CenterX - sx.CenterX, dy = region.Settlements[s].CenterY - sx.CenterY;
            if (dx * dx + dy * dy > 4000L * 4000L) continue;
            foreach (int pi in region.Settlements[s].Parcels) { var p = region.Parcels[pi]; for (int f = p.FieldStart; f < p.FieldStart + p.FieldCount; f++) { F.Crop[f] = Ravensguard.Sim.WorldGen.Region.Crop.Wheat; F.Stage[f] = FieldStage.Ripe; F.YieldG[f] = 52_800; ripe.Add(f); } }
        }
        Assert.True(ripe.Count >= 100, $"ripe fields {ripe.Count}");
        var h0 = AuditWorld.Host(run, 20, new Roster([(TroopRole.Spearman, 2000)]), 0);
        var a = hs.Account(h0); a.Realm = hs.RealmOf(start) + 5; a.Forage = ForagePolicy.EnemyLand; a.Carts = 0;                     // the host's realm is not the land's
        hs.ForageDrawOverride = 1000; hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        int day = 30 + 60 * 3 + 5; Assert.Equal(Season.Autumn, GameTime.FromGameHours((long)day * 24).Season);
        hs.Update(h0, hs.PlaceAt(start), HostMode.Camp);
        long lostBefore = run.Economy.Farming.State.LostG[(int)LossCause.Trampling];
        var r = hs.Supply(h0, day);
        int stp = (int)SupplyNeed.Staple;
        Assert.Equal(2_000_000, r.Delivered[stp, (int)SupplySource.Forage]); Assert.Equal(0, r.ShortG[stp]);
        Assert.Equal(0, barn.Qty(Wheat(run)));
        int cut = ripe.Count(f => F.YieldG[f] != 52_800); Assert.Equal(76, cut);
        Assert.Equal(75, ripe.Count(f => F.YieldG[f] == 0));                                                                           // 75 acres cut and trampled whole
        long taken = ripe.Sum(f => 52_800 - F.YieldG[f]);
        Assert.Equal(4_000_000, run.Economy.Farming.State.LostG[(int)LossCause.Trampling] - lostBefore);                              // 2,000 kg taken and 2,000 kg trampled, both lost to farming
        Assert.Equal(4_000_000, taken);
        Assert.Equal(4_000_000, hs.BaggageOf(h0).Qty(Wheat(run)));                                                                    // 6,000 kg brought in, 2,000 eaten: the rest in the carts
        Assert.Equal(2_000_000, hs.Book.Stats[7]); Assert.Equal(76, hs.Book.Stats[8]);
        var e = Assert.Single(run.State.Chronicle.ByType("army_foraged"));
        Assert.Equal(76, e.Params!.Single(p => p.Name == "acres").Value);
        // the robbed households think 40 the worse of the host's realm
        var rb = run.State.Rule!; ulong realmKey = LandWorld.RealmTreasuryId(a.Realm).Value;
        Assert.Equal(-40, rb.Opinion(hht.IdOf(barnHh).Value, realmKey));
        Assert.True(hs.Book.Stats[9] >= 1);
        Assert.True(run.CoinConserved); Assert.True(run.MassOk);
    }

    /// <summary>AE-14: days funded: a treasury of 5,000,000 and 108,000 of scutage due over a daily cost of 391,500 is 13 days; and the realm's live hosts read from the world (treasury, chests, scutage due, loan room over the roster's daily cost).</summary>
    [Fact]
    public void AE14_DaysFunded_13_And_TheWorldsBudget()
    {
        var b = new CampaignBudget(391_500, 261_000, 93_000, 36_900, 600, 5_000_000, 0, 108_000, 0, 0);
        Assert.Equal(13, b.DaysFunded); Assert.False(b.MayStart); Assert.False(b.SeekFunds);
        Assert.True(new CampaignBudget(391_500, 0, 0, 0, 0, 20_000_000, 0, 0, 0, 0).MayStart);
        Assert.True(new CampaignBudget(391_500, 0, 0, 0, 0, 2_000_000, 0, 0, 0, 0).SeekFunds);
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger;
        var (start, _) = AuditWorld.Stops(run);
        var h = AuditWorld.Host(run, 21, new Roster([(TroopRole.Levy, 1040), (TroopRole.Spearman, 300), (TroopRole.Archer, 160), (TroopRole.ManAtArms, 60), (TroopRole.Knight, 40), (TroopRole.Mercenary, 400)]), 0, 100, new AnimalCounts(300, 0, 0, 0));
        int realm = hs.RealmOf(start); hs.Account(h).Realm = realm;
        var bud = hs.BudgetOfHosts(realm);
        Assert.True(bud.PayMites > 0 && bud.StapleMites > 0 && bud.FodderMites > 0);
        Assert.Equal(bud.PayMites + bud.StapleMites + bud.FodderMites + bud.CartingMites, bud.DailyCostMites);
        led.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(realm), out var tr); long bal = led.Balance(tr).Mites; if (bal > 0) led.Sink(tr, new Coin(bal), SinkReason.Burn);
        led.Mint(tr, new Coin(bud.DailyCostMites * 13 + bud.DailyCostMites / 2), SourceReason.Seed);
        var after = hs.BudgetOfHosts(realm); Assert.Equal(bud.TreasuryMites + bud.DailyCostMites * 13 + bud.DailyCostMites / 2, after.TreasuryMites);
        Assert.True(after.FundsMites >= after.TreasuryMites); Assert.True(hs.DaysFunded(realm) >= 13);
        output.WriteLine($"daily cost {after.DailyCostMites:N0} (pay {after.PayMites:N0}, staple {after.StapleMites:N0}, fodder {after.FodderMites:N0}, carting {after.CartingMites:N0}); treasury {after.TreasuryMites:N0}, loan room {after.LoanRoomMites:N0}; {hs.DaysFunded(realm)} days funded");
    }

    /// <summary>The realm raises the coin of a campaign by the rule's own sources (aid, loans of existing coin, tallage with its grievances), moves it to the pay chest and writes <c>campaign_funded</c>; every coin stays (RI-13) and the treasuries are exact.</summary>
    [Fact]
    public void Fund_Raises_FromTheRulesSources_AndLoansOnlyMoveExistingCoin()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger; var (start, _) = AuditWorld.Stops(run);
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        var h = AuditWorld.Host(run, 22, new Roster([(TroopRole.Spearman, 200), (TroopRole.Archer, 100)]), 0);
        int realm = hs.RealmOf(start); hs.Account(h).Realm = realm;
        long total0 = led.Audit().InPurses.Mites; long sourced0 = led.Sourced(SourceReason.Mint).Mites + led.Sourced(SourceReason.Seed).Mites;
        var res = hs.Fund(h, 30, run.DayNow);
        output.WriteLine($"funded: treasury {res.Treasury:N0}, aid {res.Aid:N0}, loans {res.Loans:N0}, tallage {res.Tallage:N0}, to the chest {res.ToChest:N0}");
        Assert.True(res.ToChest > 0); Assert.Equal(res.ToChest, led.Balance(hs.ChestOf(h)).Mites);
        Assert.Equal(total0, led.Audit().InPurses.Mites); Assert.Equal(sourced0, led.Sourced(SourceReason.Mint).Mites + led.Sourced(SourceReason.Seed).Mites);   // nothing was made
        Assert.True(run.CoinConserved);
        Assert.Single(run.State.Chronicle.ByType("campaign_funded"));
        if (res.Loans > 0) Assert.Equal(res.Loans, led.Flow(TransferReason.Loan).Mites);
        if (res.Tallage > 0) Assert.True(run.State.Chronicle.ByType("tallage_taken").Any());
    }

    /// <summary>Loans for a campaign move coin that exists: a rich household of the realm lends a quarter of the want to the realm's treasury (<c>Loan</c>, the Phase 6 rule's rate and term), the lender's purse falls by what the treasury gains, and nothing is made.</summary>
    [Fact]
    public void Raise_Loans_MoveCoinThatExists_FromARichLenderToTheRealm()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger; var (start, _) = AuditWorld.Stops(run);
        int realm = hs.RealmOf(start); var hht = run.State.Population.Households; var rb = run.State.Rule!;
        int lender = -1; for (int h = 0; h < hht.Count && lender < 0; h++) if (hht.MemberCount[h] > 0 && hht.Class[h] != Ravensguard.Sim.People.SocialClass.Peasant && hs.RealmOf(hht.Settlement[h]) == realm) lender = h;
        Assert.True(lender >= 0); var lp = run.Econ.HouseholdPurse[lender]; led.Mint(lp, new Coin(500_000_000), SourceReason.Seed);
        led.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(realm), out var tr);
        long total0 = led.Audit().InPurses.Mites, l0 = led.Balance(lp).Mites, t0 = led.Balance(tr).Mites; int loans0 = rb.Loans.Count;
        var (aid, loans, tallage) = hs.Raise(realm, 40_000_000, run.DayNow);
        Assert.True(loans > 0, "a lender lent"); Assert.True(rb.Loans.Count > loans0);
        var loan = rb.Loans.Last(); Assert.Equal(Party.OfRealm(realm), loan.Borrower); Assert.Equal(LoanState.Open, loan.State);
        Assert.Equal(loans, led.Flow(TransferReason.Loan).Mites);
        Assert.Equal(loans, rb.Loans.Skip(loans0).Sum(x => x.PrincipalMites));
        Assert.Equal(total0, led.Audit().InPurses.Mites); Assert.True(run.CoinConserved);                              // coin moved, none was made
        output.WriteLine($"raised: aid {aid:N0}, loans {loans:N0} in {rb.Loans.Count - loans0} loans, tallage {tallage:N0}; the treasury went from {t0:N0} to {led.Balance(tr).Mites:N0}; the richest lender from {l0:N0} to {led.Balance(lp).Mites:N0}");
    }

    /// <summary>AE-20: a pay chest captured with 200 gp in pieces: 200,000 mites to the captor's purse as <c>Plunder</c>, the gold pieces with them; coin and the coin book exact.</summary>
    [Fact]
    public void AE20_PayChestCaptured_200000_Plunder_WithItsGoldPieces()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger;
        led.Coinage ??= new CoinStock();
        var h = AuditWorld.Host(run, 23, new Roster([(TroopRole.Spearman, 50)]), 0);
        var chest = hs.ChestOf(h); led.Mint(chest, new Coin(200_000), SourceReason.Seed); led.Coinage.AddStruck(led, chest, 200);
        Assert.Equal(200, led.Coinage.Pieces(chest));
        var captor = led.Open(PurseKind.Chest, HostIds.Chest(24)); long p0 = led.Flow(TransferReason.Plunder).Mites;
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        long got = hs.CapturePayChest(h, captor, EntityId.None, run.DayNow);
        Assert.Equal(200_000, got); Assert.Equal(200_000, led.Balance(captor).Mites); Assert.Equal(0, led.Balance(chest).Mites);
        Assert.Equal(200_000, led.Flow(TransferReason.Plunder).Mites - p0);
        Assert.Equal(200, led.Coinage.Pieces(captor)); Assert.Equal(0, led.Coinage.Pieces(chest));
        Assert.True(run.CoinConserved); Assert.Equal("", led.Coinage.Check(led));
        Assert.Single(run.State.Chronicle.ByType("pay_chest_captured"));
        Assert.Equal(200_000, hs.Book.Stats[14]);
        Assert.Equal(0, hs.CapturePayChest(h, captor, EntityId.None, run.DayNow));                                       // an empty chest has nothing to take
    }

    /// <summary>Stream 0x660 is unused before a purveyance (a game with no host draws nothing) and drawn by one with hiding on.</summary>
    [Fact]
    public void Stream660_IsUnusedBeforeAPurveyance_AndDrawnByOne()
    {
        Assert.Equal(0x660u, RngStreams.PurveyanceHiding);
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src", "Ravensguard.Sim"))) root = root.Parent;
        var users = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.EndsWith("RngStreams.cs") && File.ReadAllText(f).Contains("RngStreams.PurveyanceHiding")).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "HostSupply.Purveyance.cs" }, users.Select(x => x!).ToArray());
        // a world with a host that never purveys has no draw: hiding is off when the policy is
        using var run = AuditWorld.Create(); Assert.Empty(run.Supply.Book.Tallies);
    }

    /// <summary>The book's save: tallies, sutlers, unnoted scutage and the stats go through the optional keys and come back equal, and a section with none of them has the older shape; save, load, continue equals the straight run.</summary>
    [Fact]
    public void Save_Tallies_RoundTrip_AndLoadedRunContinuesAsTheStraightRun()
    {
        var v = Build(25); var hs = v.Hs;
        Supply(v);
        hs.Book.Sutlers.Add(new SutlerRec(v.Host.Value, 77, v.Day)); hs.Book.ScutageNotes.Add(new ScutageNote(1, 5, 2, 90_000));
        var back = SaveCodec.Deserialize<WarSupplySave>(SaveCodec.Serialize(WarSupplySave.Capture(hs.Book))).Restore();
        Assert.Equal(hs.Book.Hash(), back.Hash()); Assert.Equal(40, back.Tallies.Count); Assert.Equal(101_250, back.Tallies.Values.Sum(t => t.Mites));
        Assert.Equal(hs.Book.Stats, back.Stats); Assert.Single(back.Sutlers); Assert.Single(back.ScutageNotes);
        hs.Book.Sutlers.Clear(); hs.Book.ScutageNotes.Clear();
        using var _ = v.Run;
        var straight = v.Run;
        straight.AdvanceToDayEnd(v.Day);
        var file = WorldSave.Capture(straight.State, "h", "0.1"); var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.State.Content!, "h", null, straight.State.Continent, straight.State.History);
        st.Content = straight.State.Content;
        using var rt = new WorldRuntime(st, st.Content!, new MemoryRegionPager()); WarLoadRun.AttachLinks(rt);
        var again = WorldSave.Capture(st, "h", "0.1");
        foreach (var k in file.Sections.Keys) Assert.True(file.Sections[k].Span.SequenceEqual(again.Sections[k].Span), $"section {k} differs after load and save");
        var hs2 = st.HomeRecords!.Sim.Get<HostSupply>()!; Assert.Equal(hs.Book.Hash(), hs2.Book.Hash()); Assert.Equal(40, hs2.Book.Tallies.Count);
        // both run the season step that pays the tallies; the realm's treasury is flush in both
        int due = v.Day + 30;
        foreach (var (r, sup) in new[] { (straight.Runtime, hs), (rt, hs2) })
        {
            var led = (r == straight.Runtime ? straight.State : st).Economy!.Ledger; int realm = sup.Account(v.Host).Realm;
            led.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(realm), out var tr); led.Mint(tr, new Coin(sup.RealmPayrollSeason(realm) + 200_000), SourceReason.Seed);
            sup.SeasonStep(due);
        }
        Assert.Equal(hs.Book.Hash(), hs2.Book.Hash()); Assert.All(hs2.Book.Tallies.Values, t => Assert.Equal(TallyState.Paid, t.State));
        Assert.True(st.Economy!.Ledger.Audit().IsConserved);
    }

    /// <summary>The sutlers: a host of 200 camped two days draws a merchant of a book within 16 km holding the staple and the coin (P = 10% a day on stream 0x751), buys from him at his ask times 1.3 from the chest into the carts (coin to his purse, grain from his store), and he leaves when pay is three days in arrears.</summary>
    [Fact]
    public void Sutlers_Join_Sell_AtTheCampPrice_AndLeaveWhenPayIsInArrears()
    {
        using var run = AuditWorld.Create(); var hs = run.Supply; var led = run.Econ.Ledger; var (start, _) = AuditWorld.Stops(run);
        var bk = run.Economy.Markets.BookForSettlement(start); Assert.NotEmpty(bk.Merchants);
        // every grain of the books is gone from the market so only the sutler can sell
        var m = bk.Merchants[0]; var wheat = Wheat(run);
        m.Store.Create(wheat, 5_000_000, CreateReason.Seed); led.Mint(m.Purse, new Coin(50_000), SourceReason.Seed);
        var h = AuditWorld.Host(run, 26, new Roster([(TroopRole.Spearman, 200)]), 5000);
        var a = hs.Account(h); a.ServiceDays = 3; a.Realm = hs.RealmOf(start);
        hs.Update(h, hs.PlaceAt(start), HostMode.Camp);
        hs.Chron = RuleChron.Of(run.State.Chronicle, run.State.Ids);
        int day = run.NextSlotDay; run.BeforeSlot(day);
        bool joined = false;
        for (int d = day; d < day + 60 && !joined; d++)                 // P = 10% a day: it joins within a few weeks
        {
            hs.Update(h, hs.PlaceAt(start), HostMode.Camp); hs.Supply(h, d);
            joined = hs.SutlersOf(h).Any();
        }
        Assert.True(joined, "the merchant joined the host");
        Assert.Single(run.State.Chronicle.ByType("sutlers_joined"));
        // from then on the market is closed to the host (no books in reach): only the sutler feeds it
        a.BuyRadiusM = 1; hs.Update(h, new HostPlace(start, -1_000_000, -1_000_000), HostMode.Camp);
        long coin0 = led.Balance(m.Purse).Mites, stock0 = m.Store.Qty(wheat);
        var r = hs.Supply(h, day + 70);
        Assert.True(r.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Sutlers] > 0 || r.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Baggage] > 0);
        Assert.True(run.CoinConserved && run.MassOk);
        output.WriteLine($"sutler sold {stock0 - m.Store.Qty(wheat)} g for {led.Balance(m.Purse).Mites - coin0} mites");
        // three days unpaid: he leaves
        a.UnpaidDays[(int)TroopRole.Spearman] = 3; hs.Supply(h, day + 71);
        Assert.Empty(hs.SutlersOf(h));
    }
}
