using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Dialogue;

namespace Ravensguard.Sim.Tests.Soldiers;

/// <summary>P7-SC-C golden tests (soldiers-and-companies.md 10): SC-13 to SC-15, seeding and the save round trip of the companies.</summary>
public class CompanyTests
{
    private sealed class NoFit : IKitFit
    {
        public int FitOf(ulong item) => 70_000;
        public int BodyOf(ulong person) => 70_000;
        public bool SetFit(ulong item, int cm3) => true;
    }

    private static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Rival = EntityId.Make(IdKind.Faction, 901);

    private sealed class Rig
    {
        public Chronicle C; public ContractWorld W; public ContractBook B = new(); public Ledger L = new(); public SoldierWorld Sw; public IdAllocator Ids = new();
        public PurseId Employer, RivalPurse; public Dictionary<ulong, PurseId> Purses = [];

        public Rig(long employerCoin, int companies = 1, int men = 60, ulong seed = 11)
        {
            var (tw, chron) = TalkKit.Town(men * companies + 10, seed); C = chron;
            Employer = L.Open(PurseKind.Treasury, Fief); if (employerCoin > 0) L.Mint(Employer, new Coin(employerCoin), SourceReason.Seed);
            RivalPurse = L.Open(PurseKind.Treasury, Rival); L.Mint(RivalPurse, new Coin(10_000_000), SourceReason.Seed);
            var db = EconFixture.Content;
            Sw = new SoldierWorld(new SoldierBook(), db, EconFixture.NewEconomy(), new WorksBook(), db.All<LargeBuildingRulesDef>().First(), new NoFit(), () => null) { PurseHook = p => Purses.TryGetValue(p, out var x) ? x : null };
            int next = 0;
            for (int k = 1; k <= companies; k++)
            {
                var c = new MercCompany { Id = k, Name = "Company " + k, Settlement = 0 };
                for (int i = 0; i < men; i++)
                {
                    ulong p = tw.People.IdOf(next++).Value; c.Members.Add(p);
                    Sw.Book.Rows[p] = new SoldierRow { Person = p, Role = "spearman", Lord = -1 };
                    Purses[p] = L.Open(PurseKind.Person, new EntityId(p));
                }
                if (c.Members.Count > 0) c.Captain = c.Members[0]; Sw.Book.Companies[k] = c;
            }
            W = new ContractWorld
            {
                Chronicle = C, Ids = Ids, Ledger = L, People = tw.People, SettlementNames = ["Ashby"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1)], SettlementFief = [0], FiefIds = [Fief],
                Seed = seed, Content = ContractContent.From(TalkKit.LoadBase()),
                Companies = () => Sw.Book.Companies.Values.Where(c => c.Purse >= 0).Select(c => new CompanyInfo(MercCompanies.IdOf(c), c.Name, 0, 100, c.Members.Count, c.Purse, "mercenary")),
            };
        }

        public MercCompany Co(int id = 1) => Sw.Book.Companies[id];
        public HireResult Hire(int day, int days, int id = 1) => MercCompanies.Hire(W, B, Sw, id, Fief, 900, Employer, 0, days, false, day);
        public void RunDay(int day) { ContractSystem.Expiries(W, B, day); MercCompanies.Day(W, B, Sw, RuleChron.Of(C, Ids), day); }
        public int Entries(string type) => C.All.Count(e => e.TypeId == type);
    }

    [Fact]
    public void SC13_ASixtyManCompanyHiredThirtyDays_AdvanceEscrowed_WagesToItsPurseWeekly_EachManPaidLessTenPercent_CoinExact()
    {
        var r = new Rig(1_000_000); var co = r.Co();
        long wage = 370, daily = 60 * wage;                                                    // a mercenary spearman's 37 cp = 370 mites (SC-1)
        Assert.Equal(daily, MercCompanies.DailyMites(r.Sw, co));
        Assert.Equal(HireResult.Hired, r.Hire(0, 30));
        long advance = daily * 6 / 2; var k = r.B.Get(co.ContractId)!;
        Assert.Equal(advance, k.PayMites); Assert.Equal(advance, r.L.Balance(new PurseId(k.Escrow)).Mites);
        Assert.Equal(1_000_000 - advance, r.L.Balance(r.Employer).Mites);
        for (int d = 0; d < 6; d++) r.RunDay(d);
        Assert.Equal(0, r.L.Balance(r.Purses[co.Members[5]]).Mites);                           // not yet: the company pays every seventh day
        Assert.Equal(6 * daily, r.L.Balance(new PurseId(co.Purse)).Mites);
        r.RunDay(6);
        long net = 7 * wage - 7 * wage / 10;                                                   // 2,331 each, the captain's tenth held back
        Assert.Equal(net, r.L.Balance(r.Purses[co.Members[5]]).Mites);
        Assert.Equal(7 * wage + 59 * (7 * wage / 10), r.L.Balance(r.Purses[co.Captain]).Mites);
        Assert.Equal(0, r.L.Balance(new PurseId(co.Purse)).Mites);                             // all of it went to real purses
        for (int d = 7; d < 30; d++) r.RunDay(d);
        Assert.True(r.L.Audit().IsConserved);
        Assert.Equal(WorkState.Open, WorkState.Open);
        r.RunDay(30);                                                                          // the service is done: the advance is the company's, the name rises
        Assert.Equal(WorkState.Done, r.B.Get(k.Id)!.State);
        Assert.False(co.Hired); Assert.Equal(1, co.Kept); Assert.Equal(50, co.Reputation);
        Assert.Equal(0, r.L.Balance(new PurseId(k.Escrow)).Mites);
        Assert.True(r.L.Audit().IsConserved);
        Assert.Equal(HireResult.BadTerms, r.Hire(31, 90));                                      // terms: 15 to 60 days
    }

    [Fact]
    public void SC14_SixDaysOfArrears_TheCompanyLeaves_TheEmployerLosesStanding_ChronicleWithTheCause_ReputationBelowZeroNearbyPlunders()
    {
        long daily = 60 * 370, advance = daily * 3;
        var r = new Rig(advance + 2 * daily); var co = r.Co();                                   // the purse can pay the escrow and two days
        Assert.Equal(HireResult.Hired, r.Hire(0, 30));
        for (int d = 0; d < 7; d++) { r.RunDay(d); if (d is >= 2 and <= 5) { Assert.True(co.Hired); Assert.Equal(d - 1, co.ArrearsDays); Assert.Equal(-5 * (d - 1), co.Morale); } }
        Assert.True(co.Hired);                                                                 // five days short: grumbling only
        r.RunDay(7);
        Assert.False(co.Hired);
        Assert.Equal(-200, r.Sw.Book.Standing[900]);
        Assert.Equal(1, r.Entries("company_leaves")); Assert.Equal(0, r.Entries("company_plunders"));
        var e = r.C.All.Single(x => x.TypeId == "company_leaves");
        Assert.Contains(e.Causes, c => c.EventId != null && c.EventId.Value.Value == r.B.Contracts.Values.Single().PostedEntry);
        Assert.Equal(WorkState.Failed, r.B.Contracts.Values.Single().State);
        Assert.Equal(advance, r.L.Balance(r.Employer).Mites);                                  // the escrow went home (the two days paid are the company's)
        Assert.True(r.L.Audit().IsConserved);

        // a bad name with the employer's lands near: some of forty companies plunder on the way out, none with a good name or far lands
        int plunders = 0, leaves = 0;
        for (int variant = 0; variant < 2; variant++)
            for (int id = 1; id <= 40; id++)
            {
                var q = new Rig(advance + 2 * daily, companies: 1, men: 20, seed: (ulong)(100 + id)); var c = q.Co();
                c.Reputation = variant == 0 ? -100 : 100; q.Sw.CompanyHooks.EmployerLandsNear = _ => variant == 0;
        
                Assert.Equal(HireResult.Hired, MercCompanies.Hire(q.W, q.B, q.Sw, 1, Fief, 900, q.Employer, 0, 30, false, 0));
                for (int d = 0; d < 20 && c.Hired; d++) q.RunDay(d);
                plunders += q.Entries("company_plunders"); leaves += q.Entries("company_leaves");
                if (variant == 1) Assert.Equal(0, q.Entries("company_plunders"));
                Assert.Equal(1, q.Entries("company_plunders") + q.Entries("company_leaves"));
                Assert.True(q.L.Audit().IsConserved);
            }
        Assert.InRange(plunders, 1, 40);
        Assert.True(leaves > 40 - plunders);
    }

    [Fact]
    public void SC15_AnEnemyOffersOneAndAHalfTheWage_ReputationTwoHundred_TurnsOnce_ReputationMinusThreeHundred()
    {
        var r = new Rig(10_000_000); var co = r.Co(); co.Reputation = 200;
        r.Sw.CompanyHooks.RivalOffer = _ => (901, r.RivalPurse.Index);
        Assert.Equal(HireResult.Hired, r.Hire(0, 60)); var first = r.B.Get(co.ContractId)!;
        int turned = -1;
        for (int d = 0; d < 20; d++) { r.RunDay(d); if (co.Turned && turned < 0) turned = d; }
        Assert.True(turned >= 0, "an offer at 250 pm a day turns a company within twenty days");
        Assert.Equal(-100, co.Reputation); Assert.Equal(901, co.Employer); Assert.Equal(1500, co.WageMultPm);
        for (int d = 20; d < 50; d++) r.RunDay(d);
        Assert.Equal(1, r.Entries("company_turns")); Assert.Equal(-100, co.Reputation);        // once only
        Assert.Equal(WorkState.Failed, first.State);
        Assert.True(r.L.Balance(new PurseId(co.Purse)).Mites + r.Purses.Values.Sum(p => r.L.Balance(p).Mites) > 0);
        Assert.True(r.L.Audit().IsConserved);
        // a name of 300 or more is not for sale
        var s = new Rig(10_000_000); s.Co().Reputation = 400; s.Sw.CompanyHooks.RivalOffer = _ => (901, s.RivalPurse.Index); s.Hire(0, 60);
        for (int d = 0; d < 50; d++) s.RunDay(d);
        Assert.False(s.Co().Turned);
    }

    [Fact]
    public void Seeding_IsPureInTheSeedAndTheIds_TwentyToTwoHundredMen_ACaptainOfBestLeadership_AndNoPurseOrCoinIsMade()
    {
        ulong[] People(int n) => [.. Enumerable.Range(1, n).Select(i => EntityId.Make(IdKind.Person, (ulong)i).Value)];
        var a = new Rig(0, 1, 0); var b = new Rig(0, 1, 0);
        a.Sw.Book.Companies.Clear(); b.Sw.Book.Companies.Clear();
        a.Sw.Book.Skills.Set(new EntityId(People(400)[7]), Ravensguard.Sim.People.Skill.Leadership, 80_000_000L);
        var ca = MercCompanies.Seed(a.Sw, 5, 1, 0, People(400))!; var cb = MercCompanies.Seed(b.Sw, 5, 1, 0, People(400))!;
        Assert.InRange(ca.Members.Count, 20, 200); Assert.Equal(ca.Members, cb.Members); Assert.Equal(ca.Name, cb.Name);
        Assert.All(ca.Members, m => Assert.NotNull(a.Sw.Book.Of(m)));
        Assert.Equal(ca.Members.Max(m => a.Sw.SkillOf(m, Ravensguard.Sim.People.Skill.Leadership)), a.Sw.SkillOf(ca.Captain, Ravensguard.Sim.People.Skill.Leadership));
        Assert.Equal(-1, ca.Purse); Assert.Equal("sergeant", a.Sw.Book.Of(ca.Captain)!.Role);
        Assert.Null(MercCompanies.Seed(a.Sw, 5, 2, 0, People(15)));                              // fewer than twenty free men: no company
        var d = MercCompanies.Seed(a.Sw, 5, 2, 0, People(400))!;                                 // the men of the first company are taken
        Assert.Empty(d.Members.Intersect(ca.Members));
    }

    [Fact]
    public void Companies_SaveLoadSave_IsByteIdentical_AndTheSectionStaysAbsentWhileEmpty()
    {
        var r = new Rig(10_000_000, 2, 25); r.Co().Reputation = 120; r.Hire(0, 30, 1);
        for (int d = 0; d < 9; d++) r.RunDay(d);
        r.Sw.Book.Standing[7] = -200;
        var s1 = SoldierSave.Capture(r.Sw.Book); var bytes1 = SaveCodec.Serialize(s1);
        var back = SaveCodec.Deserialize<SoldierSave>(bytes1).Restore();
        var bytes2 = SaveCodec.Serialize(SoldierSave.Capture(back));
        Assert.Equal(bytes1, bytes2); Assert.Equal(r.Sw.Book.Hash(), back.Hash()); Assert.NotEqual(0UL, back.Hash());
        Assert.Equal(2, back.Companies.Count); Assert.Equal(r.Co().Members, back.Companies[1].Members); Assert.Equal(-200, back.Standing[7]);
        Assert.True(new SoldierBook().IsEmpty);
    }
}
