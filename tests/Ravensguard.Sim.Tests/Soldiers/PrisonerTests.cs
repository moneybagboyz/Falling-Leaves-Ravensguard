using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Phase6Overview;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Soldiers;

/// <summary>P7-SC-D golden tests (soldiers-and-companies.md 10): SC-16 to SC-18, and the SC-C leftovers (first-day seeding, kit back, the contract's shares, a turned company).</summary>
public class PrisonerTests
{
    private static readonly AftermathRules R = AftermathRules.Default;
    private static ulong P(int n) => EntityId.Make(IdKind.Person, (ulong)n).Value;

    private sealed class Pens
    {
        public CompanyTests.Rig Rig = new(0, 1, 30);
        public SoldierWorld Sw => Rig.Sw;
        public Ledger L => Sw.Econ.Ledger;
        public RuleChron Chron;
        public PurseId Captor;
        public Pens() { Chron = RuleChron.Of(Rig.C, Rig.Ids); Captor = L.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 700)); }
        public PurseId Treasury(int n, long mites)
        {
            var p = L.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, (ulong)(710 + n)));
            if (mites > 0) L.Mint(p, new Coin(mites), SourceReason.Seed);
            return p;
        }
        public int Entries(string type) => Rig.C.All.Count(e => e.TypeId == type);
    }

    /// <summary>400 men: 4 men-at-arms, 6 knights and 2 barons are ransom class, the other 388 levies are common.</summary>
    private static List<Taken> Battle(int payerPurse = -1)
    {
        var l = new List<Taken>();
        for (int i = 1; i <= 400; i++)
        {
            var rank = i <= 4 ? CaptiveRank.ManAtArms : i <= 10 ? CaptiveRank.Knight : i <= 12 ? CaptiveRank.Baron : CaptiveRank.Levy;
            l.Add(new Taken(P(i), rank, PayerPurse: rank == CaptiveRank.Levy ? -1 : payerPurse));
        }
        return l;
    }

    [Fact]
    public void SC16_FourHundredPrisoners_TwelveRansomByRankPrice_388Common_FatesByTheCommander_EveryCommonOneFedOrReleased()
    {
        var a = new Pens();
        var rep = PrisonerPens.Take(a.Sw, R, a.Chron, 7, 1, 10, 5, -1, 2, a.Captor, 40, Commander.Just, Battle());
        Assert.Equal(new TakeReport(400, 12, 388, 0, 0, rep.Entry), rep);
        Assert.Equal(12, a.Sw.Book.Prisoners.Values.Count(p => p.Class == PrisonerClass.Ransom)); Assert.Equal(388, a.Sw.Book.Prisoners.Values.Count(p => p.Class == PrisonerClass.Common));
        Assert.Equal(R.D.RankManAtArms, a.Sw.Book.Prisoners[P(1)].ValueMites); Assert.Equal(R.D.RankKnight, a.Sw.Book.Prisoners[P(5)].ValueMites); Assert.Equal(R.D.RankBaron, a.Sw.Book.Prisoners[P(11)].ValueMites);
        Assert.Equal(R.D.RankLevy, a.Sw.Book.Prisoners[P(100)].ValueMites);
        Assert.Equal(PrisonerClass.Ransom, PrisonerPens.Sort(new Taken(P(99), CaptiveRank.Levy, Means: true)));          // kin or a lord with the means

        // a just commander paroles the many: released, nobody left to feed; the twelve stay in the pen
        Assert.Equal(388, PrisonerPens.Settle(a.Sw, a.Chron, 7, 10, 5, -1, Commander.Just, saleLegal: true));
        Assert.All(a.Sw.Book.Prisoners.Values.Where(p => p.Class == PrisonerClass.Common), p => Assert.True(p.ParoleUntil > 10));
        var e = a.Rig.C.All.Single(x => x.TypeId == "prisoners_fate"); Assert.Equal("ch_so_fate_parole", e.TextKey);
        Assert.Contains(e.Causes, c => c.EventId?.Value == rep.Entry);                                                       // caused by the capture

        // a greedy one sells where legal, else works; a cruel one works or kills; those put to work are fed or, unfed two days, released
        var g = new Pens(); PrisonerPens.Take(g.Sw, R, g.Chron, 7, 1, 10, 5, -1, 2, g.Captor, 40, Commander.Greedy, Battle());
        var buyer = g.Treasury(1, 1_000_000);
        g.Sw.PenHooks.SaleOffer = r => (buyer, 1_000);
        PrisonerPens.Settle(g.Sw, g.Chron, 7, 10, 5, -1, Commander.Greedy, saleLegal: true);
        Assert.Equal(12, g.Sw.Book.Prisoners.Count); Assert.Equal(388_000, g.L.Balance(g.Captor).Mites); Assert.Equal(1_000_000 - 388_000, g.L.Balance(buyer).Mites);
        Assert.True(g.L.Audit().IsConserved);
        var w = new Pens(); PrisonerPens.Take(w.Sw, R, w.Chron, 7, 1, 10, 5, -1, 2, w.Captor, 40, Commander.Greedy, Battle());
        PrisonerPens.Settle(w.Sw, w.Chron, 7, 10, 5, -1, Commander.Greedy, saleLegal: false);                          // no market: work
        Assert.Equal(388, w.Sw.Book.Prisoners.Values.Count(p => p.Work == "camp"));
        bool fed = true; w.Sw.PenHooks.Feed = p => fed;
        PrisonerPens.Day(w.Sw, R, w.Chron, 7, 11); Assert.Equal(400, w.Sw.Book.Prisoners.Count);
        fed = false; PrisonerPens.Day(w.Sw, R, w.Chron, 7, 12); Assert.Equal(400, w.Sw.Book.Prisoners.Count);            // one day unfed
        PrisonerPens.Day(w.Sw, R, w.Chron, 7, 13); Assert.Empty(w.Sw.Book.Prisoners);                         // two: released
        var c = new Pens(); var killed = new List<ulong>(); c.Sw.PenHooks.Killed = killed.Add;
        PrisonerPens.Take(c.Sw, R, c.Chron, 7, 1, 10, 5, -1, 2, c.Captor, 40, Commander.Cruel, Battle());
        PrisonerPens.Settle(c.Sw, c.Chron, 7, 10, 5, -1, Commander.Cruel, saleLegal: true);
        Assert.InRange(killed.Count, 140, 250); Assert.Equal(400 - killed.Count, c.Sw.Book.Prisoners.Count);            // about 500 permille of 388 by the draw
        Assert.Equal(12, c.Sw.Book.Prisoners.Values.Count(p => p.Class == PrisonerClass.Ransom));
        var c2 = new Pens(); var killed2 = new List<ulong>(); c2.Sw.PenHooks.Killed = killed2.Add;
        PrisonerPens.Take(c2.Sw, R, c2.Chron, 7, 1, 10, 5, -1, 2, c2.Captor, 40, Commander.Cruel, Battle()); PrisonerPens.Settle(c2.Sw, c2.Chron, 7, 10, 5, -1, Commander.Cruel, saleLegal: true);
        Assert.Equal(killed, killed2);                                                                                  // the same draws

        // only what can be guarded is held: two guards keep twenty (the twelve of rank first), the rest go, a cruel commander killing some
        var s = new Pens(); var t = PrisonerPens.Take(s.Sw, R, s.Chron, 7, 1, 10, 5, -1, 2, s.Captor, 2, Commander.Cruel, Battle());
        Assert.Equal(20, t.Held); Assert.Equal(12, t.Ransom); Assert.Equal(380, t.LetGo + t.Killed); Assert.True(t.Killed > 100);
    }

    [Fact]
    public void SC17_TwelveRansomsOfThreeTreasuries_PaidFromRealPurses_HalvedAtFiveAndQuarteredAtTenDays_ALordWhoCannotPayCedes()
    {
        var p = new Pens(); long k = R.D.RankKnight;
        var a = p.Treasury(1, 4 * k); var b = p.Treasury(2, k / 2); var c = p.Treasury(3, k / 4);
        var list = new List<Taken>();
        for (int i = 1; i <= 4; i++) list.Add(new Taken(P(i), CaptiveRank.Knight, PayerPurse: a.Index));
        for (int i = 5; i <= 8; i++) list.Add(new Taken(P(i), CaptiveRank.Knight, PayerPurse: b.Index));
        for (int i = 9; i <= 12; i++) list.Add(new Taken(P(i), CaptiveRank.Knight, PayerPurse: c.Index, PayerIsLord: true));
        PrisonerPens.Take(p.Sw, R, p.Chron, 7, 1, 0, 5, -1, 2, p.Captor, 2, Commander.Just, list);
        p.Sw.PenHooks.Feed = _ => true; var ceded = new List<ulong>(); p.Sw.PenHooks.Cede = r => { ceded.Add(r.Person); return true; };
        long total = p.L.Audit().Sourced.Mites;
        PrisonerPens.Day(p.Sw, R, p.Chron, 7, 0);
        Assert.Equal(4 * k, p.L.Balance(p.Captor).Mites); Assert.Equal(8, p.Sw.Book.Prisoners.Count);                    // the rich treasury paid four, whole, at once
        for (int d = 1; d < 5; d++) PrisonerPens.Day(p.Sw, R, p.Chron, 7, d);
        Assert.Equal(8, p.Sw.Book.Prisoners.Count); Assert.Equal(k / 2, p.L.Balance(b).Mites);                          // days 1 to 4: the full price is beyond them
        PrisonerPens.Day(p.Sw, R, p.Chron, 7, 5);
        Assert.Equal(7, p.Sw.Book.Prisoners.Count); Assert.Equal(4 * k + k / 2, p.L.Balance(p.Captor).Mites); Assert.Equal(0, p.L.Balance(b).Mites);       // half asked from the fifth day: one paid
        for (int d = 6; d < 10; d++) PrisonerPens.Day(p.Sw, R, p.Chron, 7, d);
        Assert.Equal(7, p.Sw.Book.Prisoners.Count); Assert.Empty(ceded);
        PrisonerPens.Day(p.Sw, R, p.Chron, 7, 10);
        Assert.Equal(4 * k + k / 2 + k / 4, p.L.Balance(p.Captor).Mites);                                              // a quarter from the tenth: the third treasury's one
        Assert.Equal(3, ceded.Count); Assert.Equal(3, p.Sw.Book.Prisoners.Count);                                      // its lord is out of coin for the other three: he cedes (the hook is Phase 6's); the second realm's three stay held
        Assert.All(p.Sw.Book.Prisoners.Values, r => Assert.Equal(b.Index, r.PayerPurse));
        Assert.True(p.L.Audit().IsConserved); Assert.Equal(total, p.L.Audit().Sourced.Mites);                                 // no coin made
        Assert.Equal(6, p.Entries("ransom_paid"));                                                                  // 4 + 1 + 1
        Assert.Equal(3, p.Entries("ransom_ceded"));
        Assert.All(p.Rig.C.All.Where(e => e.TypeId is "ransom_paid" or "ransom_ceded"), e => Assert.NotEmpty(e.Causes));
    }

    [Fact]
    public void SC18_PrisonersCompaniesAndTheFlag_SaveLoadSave_ByteIdentical_AndTheHashIsEqualAcrossRuns()
    {
        SoldierBook Make()
        {
            var p = new Pens(); var t = p.Treasury(1, 100_000);
            PrisonerPens.Take(p.Sw, R, p.Chron, 7, 1, 10, 5, -1, 2, p.Captor, 5, Commander.Just, Battle(t.Index));
            PrisonerPens.Settle(p.Sw, p.Chron, 7, 10, 5, -1, Commander.Cruel, true);
            p.Sw.Book.Seeded = true; p.Sw.Book.Companies[1].RansomSharePm = 500; p.Sw.Book.Prisoners.Values.First().ExchangeWar = 3;
            return p.Sw.Book;
        }
        var b1 = Make(); var b2 = Make();
        Assert.Equal(b1.Hash(), b2.Hash()); Assert.NotEqual(0UL, b1.Hash()); Assert.Equal(b1.Prisoners.Count, b2.Prisoners.Count);
        var bytes1 = SaveCodec.Serialize(SoldierSave.Capture(b1));
        var back = SaveCodec.Deserialize<SoldierSave>(bytes1).Restore();
        Assert.Equal(bytes1, SaveCodec.Serialize(SoldierSave.Capture(back)));
        Assert.Equal(b1.Hash(), back.Hash()); Assert.True(back.Seeded); Assert.Equal(500, back.Companies[1].RansomSharePm); Assert.Equal(b1.Prisoners.Count, back.Prisoners.Count);
        Assert.Equal(3, back.Prisoners.Values.First().ExchangeWar);
    }

    [Fact]
    public void Parole_ExpiresAtTheSeasonsEnd_BrokenByADrawOfOneInTen_AndExchangeSendsTheWarsPrisonersHome()
    {
        var p = new Pens(); var broke = new List<ulong>(); p.Sw.PenHooks.ParoleBroken = broke.Add; p.Sw.PenHooks.Feed = _ => true;
        PrisonerPens.Take(p.Sw, R, p.Chron, 7, 1, 0, 5, -1, 2, p.Captor, 60, Commander.Just, Battle());
        PrisonerPens.Settle(p.Sw, p.Chron, 7, 0, 5, -1, Commander.Just, false);
        for (int d = 0; d <= PrisonerPens.ParoleDays; d++) PrisonerPens.Day(p.Sw, R, p.Chron, 7, d);
        Assert.InRange(broke.Count, 10, 70); Assert.Equal(12, p.Sw.Book.Prisoners.Count(x => x.Value.Class == PrisonerClass.Ransom));         // 388 sworn; about 100 permille broke it
        Assert.Equal(broke.Count, p.Entries("parole_broken"));
        var x = new Pens(); var dead = new List<ulong>(); x.Sw.PenHooks.Killed = dead.Add; PrisonerPens.Take(x.Sw, R, x.Chron, 7, 1, 0, 5, -1, 2, x.Captor, 60, Commander.Just, Battle());
        PrisonerPens.Settle(x.Sw, x.Chron, 7, 0, 5, -1, Commander.Cruel, false, exchangeWar: 4);
        Assert.Equal(388, PrisonerPens.Exchange(x.Sw, 4) + dead.Count);          // every common man: exchanged or (a cruel one's draw) killed
        Assert.Equal(12, x.Sw.Book.Prisoners.Count);
    }

    // ------------------------------------------------------------------ the SC-C leftovers

    [Fact]
    public void FirstDay_SeedsTheCompaniesOnce_FlagSaved_RichAndWarringLandsGetMore_AndTheNewWorldIsUntouched()
    {
        var (_, st) = P6Game.Game(1234);
        Assert.Null(st.Soldiers);                                                                                       // a new world has no soldier section: the generated layers are as they were
        long hour = st.Clock.Now.TotalDays * 24;
        ContractWiring.OnHour(st, hour);
        Assert.True(st.Soldiers!.Seeded); int n = st.Soldiers.Companies.Count; Assert.InRange(n, 2, 8);
        Assert.All(st.Soldiers.Companies.Values, c => { Assert.InRange(c.Members.Count, 20, 200); Assert.Equal(-1, c.Purse); });
        ContractWiring.OnHour(st, hour + 24); ContractWiring.OnHour(st, hour + 48);
        Assert.Equal(n, st.Soldiers.Companies.Count);                                                                   // once
        var bytes = SaveCodec.Serialize(SoldierSave.Capture(st.Soldiers));
        Assert.Equal(bytes, SaveCodec.Serialize(SoldierSave.Capture(SaveCodec.Deserialize<SoldierSave>(bytes).Restore())));
        // more where wars are
        var (_, st2) = P6Game.Game(1234);
        int calm = CompanySeedStep.CountFor(st2, 99);
        var reg = st2.Population.Region;
        for (int i = 0; i < 3; i++) reg.ActiveWars.Add(new ActiveWarMarker(i, "w" + i, 0, 0, 0, 1000));
        Assert.Equal(calm + 2 - (reg.ActiveWars.Count - 3), CompanySeedStep.CountFor(st2, 99));
        // and where the households are rich
        var e = st2.Economy!; int before = CompanySeedStep.CountFor(st2, 99);
        for (int h = 0; h < e.HouseholdCount; h++) if (!e.HouseholdPurse[h].IsNone) e.Ledger.Mint(e.HouseholdPurse[h], new Coin(CompanySeedStep.RichHouseholdMites * 2), SourceReason.Seed);
        Assert.True(CompanySeedStep.CountFor(st2, 99) >= before);
    }

    [Fact]
    public void ServiceEnds_TheIssuedKitGoesBackToTheArmoury_WithItsWear_NothingMadeOrLost()
    {
        var r = new CompanyTests.Rig(10_000_000, 1, 20); var sw = r.Sw; const long key = 77;
        var cap = new ArmouryCapacity(60, 60, 60, 60, 60, 600, 240, 600, 360, 2400);
        var stock = new List<EntityId>();
        for (int i = 0; i < 20; i++)
        {
            var id = sw.Econ.Items.Add(r.Ids, "spear", ItemQuality.Common, EntityId.Make(IdKind.Household, 1), [new ItemComponent("iron", 900, 1000)], conditionPermille: 1000);
            Assert.True(sw.Armouries.Add(key, cap, id)); stock.Add(id);
        }
        int count = sw.Econ.Items.Count;
        Assert.Equal(HireResult.Hired, MercCompanies.Hire(r.W, r.B, sw, 1, CompanyTests.Fief, 900, r.Employer, 0, 15, true, 0, key));
        int issued = sw.Book.Rows.Values.Sum(x => x.Issued.Count); Assert.True(issued > 0);
        var worn = sw.Book.Rows.Values.First(x => x.Issued.Count > 0).Issued[0].Item; sw.Econ.Items.Get(new EntityId(worn)).ConditionPermille = 640;
        for (int d = 0; d < 20 && r.Co().Hired; d++) r.RunDay(d);
        Assert.False(r.Co().Hired);
        Assert.Equal(0, sw.Book.Rows.Values.Sum(x => x.Issued.Count)); Assert.All(sw.Book.Rows.Values, x => Assert.DoesNotContain(x.Worn.Values, v => stock.Any(s => s.Value == v)));
        Assert.Equal(20, sw.Armouries.Stock(key).Count); Assert.Equal(count, sw.Econ.Items.Count);
        Assert.Equal(640, sw.Econ.Items.Get(new EntityId(worn)).ConditionPermille);                                      // back with the wear it has
    }

    [Fact]
    public void TheContractsShares_RansomAndPlunder_FollowTheTerms_AndOnlyWhatExistsMoves()
    {
        var r = new CompanyTests.Rig(10_000_000, 1, 25);
        Assert.Equal(HireResult.BadTerms, MercCompanies.Hire(r.W, r.B, r.Sw, 1, CompanyTests.Fief, 900, r.Employer, 0, 30, false, 0, plunderSharePm: 1200));
        Assert.Equal(HireResult.Hired, MercCompanies.Hire(r.W, r.B, r.Sw, 1, CompanyTests.Fief, 900, r.Employer, 0, 30, false, 0, plunderSharePm: 500, ransomSharePm: 250));
        var co = r.Co(); Assert.Equal((500, 250), (co.PlunderSharePm, co.RansomSharePm));
        var payer = r.L.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 950)); r.L.Mint(payer, new Coin(30_000), SourceReason.Seed);
        long emp0 = r.L.Balance(r.Employer).Mites, co0 = r.L.Balance(new PurseId(co.Purse)).Mites;
        Assert.Equal(0, MercCompanies.ShareRansom(r.L, co, payer, 40_000));                                              // the payer holds 30,000: nothing moves
        Assert.Equal(30_000, r.L.Balance(payer).Mites);
        Assert.Equal(20_000, MercCompanies.ShareRansom(r.L, co, payer, 20_000));
        Assert.Equal(emp0 + 15_000, r.L.Balance(r.Employer).Mites); Assert.Equal(co0 + 5_000, r.L.Balance(new PurseId(co.Purse)).Mites);   // a quarter to the company, the rest to the employer
        // plunder: half the coin the loot purse really holds, and half the pieces that really exist and are the holder's
        var loot = r.L.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 951)); r.L.Mint(loot, new Coin(300), SourceReason.Seed);
        var holder = EntityId.Make(IdKind.Faction, 951); var items = r.Sw.Econ.Items; var ids = new List<EntityId>();
        for (int i = 0; i < 5; i++) ids.Add(items.Add(r.Ids, "spear", ItemQuality.Common, holder, [new ItemComponent("iron", 900, 1000)], conditionPermille: 1000));
        ids.Add(EntityId.Make(IdKind.Item, 999_999));                                                                   // an item that does not exist
        long coBefore = r.L.Balance(new PurseId(co.Purse)).Mites;
        var (mites, pieces) = MercCompanies.SharePlunder(r.L, items, co, loot, 1_000, holder, ids);
        Assert.Equal(300, mites); Assert.Equal(300, r.L.Balance(new PurseId(co.Purse)).Mites - coBefore); Assert.Equal(0, r.L.Balance(loot).Mites);   // 500 would be half of 1,000; only 300 existed
        Assert.Equal(3, pieces); Assert.Equal(3, ids.Count(i => items.IndexOf(i) >= 0 && items.At(items.IndexOf(i)).Owner == MercCompanies.IdOf(co)));
        Assert.True(r.L.Audit().IsConserved);
        // between contracts the company keeps the whole ransom
        var idle = new CompanyTests.Rig(1_000, 1, 25); idle.Co().Purse = idle.L.Open(PurseKind.Chest, MercCompanies.IdOf(idle.Co())).Index;
        var p2 = idle.L.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 952)); idle.L.Mint(p2, new Coin(9_000), SourceReason.Seed);
        Assert.Equal(9_000, MercCompanies.ShareRansom(idle.L, idle.Co(), p2, 9_000)); Assert.Equal(9_000, idle.L.Balance(new PurseId(idle.Co().Purse)).Mites);
    }

    [Fact]
    public void ATurnedCompany_TakesNoGuardContractWhileEmployed()
    {
        var r = new CompanyTests.Rig(10_000_000); var co = r.Co(); co.Reputation = 200;
        r.Sw.CompanyHooks.RivalOffer = _ => (901, r.RivalPurse.Index);
        r.Hire(0, 60); Assert.Null(MercCompanies.InfoOf(co));                                                            // hired: not on the board
        for (int d = 0; d < 20 && !co.Turned; d++) r.RunDay(d);
        Assert.True(co.Turned); Assert.True(co.Hired); Assert.Null(MercCompanies.InfoOf(co));                            // turned and still employed: nor now
    }
}
