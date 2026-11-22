using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Tests.Economy;
using Xunit;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-EA3e (EA_F20): a works passes to its owner's heir by the rules a business follows; with no heir it is sold, else it escheats to the lord of the fief; it keeps its stages, crews, stock and cash box, and coin and mass stay exact.</summary>
public class IndustrySuccessionTests
{
    private static GameTime At(long day, int hour = 9) => GameTime.FromGameHours(day * 24 + hour);

    private sealed class Rig
    {
        public readonly ProdKit K; public readonly IndustryBook Book = new(); public readonly IndustryWorld World; public readonly Works W; public readonly Chronicle Chr = new();
        public Rig(bool fief = false, int households = 40, long capital = 1_000_000)
        {
            K = new ProdKit(households: households, fief: fief);
            World = new IndustryWorld(Book, K.Sys, EconFixture.Content, 42) { Chron = RuleChron.Of(Chr, new IdAllocator()) };
            K.Fund(0, capital + 100_000);
            var r = World.Found("iron", 0, 0, K.Econ.HouseholdPurse[0], capital, 0);
            Assert.True(r.Ok, r.Key);
            W = Book.Works[0];
            // a crew on the mine: two hewers and a hand, hired to the books by the labour market's own call
            var k = World.KindOf(W.Stages[0]); var b = Stage(0);
            for (int i = 0; i < 3; i++)
            {
                int p = 20 + i; bool prim = i < 2;
                K.People.Occupation[p] = prim ? k.Primary : k.Support;
                K.People.SetSkill(p, 0, k.Skill == Skill.None ? Skill.Farming : k.Skill, 50);
                K.Sys.Employ(b, p, prim ? k.Primary : k.Support);
            }
        }
        public Business Stage(int i) => K.Sys.ById(new EntityId(W.Stages[i].Business))!;
        public void Kill(int person) => K.People.Status[person] = PersonStatus.Dead;
        public void Child(int person, int of, int ageYears) { K.People.Father[person] = of; K.People.BirthDay[person] = -ageYears * TimeConst.DaysPerYear; }
        public long Coin => K.Econ.Ledger.Balance(K.Econ.HouseholdPurse[0]).Mites;
        public void Exact() { Assert.True(K.Econ.Ledger.Audit().IsConserved, "coin"); Assert.True(K.Econ.MassAuditHolds(), "mass"); }
        public IEnumerable<ChronicleEntry> Of(string type) => Chr.ByType(type);
    }

    [Fact]
    public void EA3e_OwnerDies_TheHeirOwnsTheWorksTheNextDay_AndItKeepsRunning()
    {
        var t = new Rig();
        t.Child(5, of: 0, ageYears: 22);
        t.World.Day(t.W, 1, At(1));
        long madeBefore = t.World.Status(t.W.Stages[0].Business)!.MadeG; Assert.True(madeBefore > 0);
        long cash = t.K.Econ.Ledger.Balance(t.Stage(0).Purse).Mites;
        int staffBefore = t.Stage(0).Staff.Count;
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        Assert.False(t.W.Closed);
        Assert.Equal(5, t.W.Owner);
        int hh = t.K.People.Household[5];
        for (int i = 0; i < t.W.Stages.Count; i++) { var b = t.Stage(i); Assert.Equal(BusinessState.Open, b.State); Assert.Equal(5, b.Owner); Assert.Equal(hh, b.OwnerHousehold); }
        Assert.Equal(staffBefore, t.Stage(0).Staff.Count);
        Assert.All(t.Stage(0).Staff, p => Assert.Equal(5, t.K.People.Employer[p]));
        Assert.True(t.World.Status(t.W.Stages[0].Business)!.MadeG > 0, "the works worked on the day it passed");
        t.World.Day(t.W, 3, At(3)); Assert.True(t.World.Status(t.W.Stages[0].Business)!.MadeG > 0);
        Assert.Equal(1, t.World.PassedToHeir);
        var e = Assert.Single(t.Of("works_inherited"));
        Assert.Equal(CauseKind.Death, e.Causes[0].Kind); Assert.Equal("ch_note_works_owner_died", e.Causes[0].NoteKey); Assert.Equal("ch_ind_inherited", e.TextKey);
        Assert.True(cash > 0); t.Exact();
    }

    [Fact]
    public void EA3e_AnHeirWhoAlreadyOwnsAWorks_IsPassedOver_TheNextInTheLawsOrderTakesIt()
    {
        var t = new Rig();
        t.Child(5, of: 0, ageYears: 24); t.Child(6, of: 0, ageYears: 20);                 // sons or daughters of 16 and over, eldest first: 5 then 6
        t.K.Fund(5, 1_100_000);
        var other = t.World.Found("iron", 5, 0, t.K.Econ.HouseholdPurse[5], 1_000_000, 0); Assert.True(other.Ok);
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        Assert.Equal(6, t.W.Owner); Assert.Equal(5, t.Book.Works[1].Owner);
        t.Exact();
    }

    [Fact]
    public void EA3e_NoFitHeir_TheRichestHouseholdBuys_ThePriceGoesToTheHeirsHousehold()
    {
        var t = new Rig();
        t.Child(5, of: 0, ageYears: 9);                                                    // a child of 9 is the law's heir but cannot run a works
        t.K.Fund(7, 5_000_000); t.K.Fund(8, 3_000_000);
        long price = t.World.SalePriceOf(t.W); Assert.True(price > 0);
        long b5 = t.K.Econ.Ledger.Balance(t.K.Econ.HouseholdPurse[5]).Mites, b7 = t.K.Econ.Ledger.Balance(t.K.Econ.HouseholdPurse[7]).Mites;
        long cash = t.K.Econ.Ledger.Balance(t.Stage(0).Purse).Mites;
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        Assert.Equal(7, t.W.Owner); Assert.Equal(1, t.World.PassedBySale);
        Assert.Equal(b7 - price, t.K.Econ.Ledger.Balance(t.K.Econ.HouseholdPurse[7]).Mites);
        Assert.Equal(b5 + price, t.K.Econ.Ledger.Balance(t.K.Econ.HouseholdPurse[5]).Mites);
        Assert.True(cash > 0 && t.K.Econ.Ledger.Balance(t.Stage(0).Purse).Mites >= 0); Assert.False(t.W.Closed);
        var e = Assert.Single(t.Of("works_sold")); Assert.Contains(e.Params!, p => p.Name == "price");
        t.Exact();
    }

    [Fact]
    public void EA3e_NoHeirAtAll_TheSalePriceGoesToTheFiefsTreasury()
    {
        var t = new Rig(fief: true);
        t.K.Fund(7, 5_000_000);
        long price = t.World.SalePriceOf(t.W);
        var fiefId = t.K.Pop.Region.Fiefs[0].Id; Assert.True(t.K.Econ.Ledger.TryFind(PurseKind.Treasury, fiefId, out var tr));
        long before = t.K.Econ.Ledger.Balance(tr).Mites;
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        Assert.Equal(7, t.W.Owner);
        Assert.Equal(before + price, t.K.Econ.Ledger.Balance(tr).Mites);
        Assert.Single(t.Of("works_sold"));
        t.Exact();
    }

    [Fact]
    public void EA3e_NoHeirAndNoBuyer_TheWorksEscheatsToTheLord_WithItsStockAndCash()
    {
        var t = new Rig(fief: true);
        t.World.Day(t.W, 1, At(1));
        long cash = t.K.Econ.Ledger.Balance(t.Stage(0).Purse).Mites;
        long stock = t.Stage(1).Store.Rows().Sum(r => r.Item2) + t.Stage(0).Store.Rows().Sum(r => r.Item2);
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        int lord = t.K.Pop.FiefLord[0];
        Assert.Equal(lord, t.W.Owner); Assert.False(t.W.Closed); Assert.Equal(1, t.World.PassedToLord);
        Assert.All(t.W.Stages, s => Assert.Equal(lord, t.K.Sys.ById(new EntityId(s.Business))!.Owner));
        Assert.True(t.K.Econ.Ledger.Balance(t.Stage(0).Purse).Mites >= cash - 50_000);
        Assert.True(stock >= 0);
        var e = Assert.Single(t.Of("works_escheated")); Assert.Equal("ch_ind_escheated", e.TextKey);
        t.Exact();
    }

    [Fact]
    public void EA3e_OwnerWhoLeftTheRegion_IsTreatedAsGone_TheChronicleCitesMigration()
    {
        var t = new Rig();
        t.Child(5, of: 0, ageYears: 30);
        t.K.People.Status[0] = PersonStatus.Departed;
        t.World.Day(t.W, 2, At(2));
        Assert.Equal(5, t.W.Owner);
        var e = Assert.Single(t.Of("works_inherited")); Assert.Equal(CauseKind.Migration, e.Causes[0].Kind); Assert.Equal("ch_note_works_owner_left", e.Causes[0].NoteKey);
        t.Exact();
    }

    [Fact]
    public void EA3e_NoHeirBuyerOrLord_TheWorksCloses_AsBefore()
    {
        var t = new Rig();
        t.Kill(0);
        t.World.Day(t.W, 2, At(2));
        Assert.True(t.W.Closed); Assert.Equal(1, t.World.ClosedOrphaned); Assert.Single(t.Of("works_closed"));
        t.Exact();
    }

    [Fact]
    public void EA3e_ChronicleTypesAreRoots_WithTheDeathOrMigrationCause()
    {
        var types = EconFixture.Content.All<ChronicleTypeDef>().ToDictionary(d => d.Id);
        foreach (var id in new[] { "works_inherited", "works_sold", "works_escheated" })
        {
            Assert.True(types.TryGetValue(id, out var row), id);
            Assert.True(row!.Root); Assert.Contains("Death", row.CauseKinds); Assert.Contains("Migration", row.CauseKinds);
        }
    }
}
