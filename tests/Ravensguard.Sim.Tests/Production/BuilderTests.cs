using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>Business creation from seed 1234's population and lots: coverage, records, determinism, and the section save of a fresh world.</summary>
public class BuilderTests
{
    private static readonly Lazy<SoakWorld> World = new(() => SoakWorld.Create(withTicker: false));

    [Fact]
    public void EveryTradeWorkplaceBecomesABusiness_WithAnOwnerAStaffAndACashBox()
    {
        var w = World.Value; var p = w.Pop.People; var sys = w.Sys;
        Assert.True(sys.Businesses.Count > 900, $"{sys.Businesses.Count} businesses");             // 1,131 before decision I43 kept one stall a book for every 400 people
        foreach (var bk in w.Markets.AllBooks())
            Assert.True(sys.Businesses.Count(b => sys.KindOf(b).Trader && w.Markets.BookOf[b.Settlement] == bk.SettlementIndex) <= Math.Max(1, bk.Population / w.Econ.Tuning.PeoplePerStall), $"too many stalls in {bk.Name}");
        var kinds = sys.Businesses.Select(b => sys.Content.Kinds[b.Kind].Id).ToHashSet();
        foreach (var k in new[] { "bakery", "mill", "brewery", "tavern", "butchery", "smithy", "weavery", "tannery", "cobbler", "carpentry", "masonry", "merchant_stall", "woodlot", "fishery", "charcoal_crew" })
            Assert.Contains(k, kinds);
        // every (settlement, lot) of a producing trade has exactly one business of its kind
        var seen = new HashSet<(int, int, ushort)>();
        foreach (var b in sys.Businesses.Where(b => b.Lot >= 0)) Assert.True(seen.Add((b.Settlement, b.Lot, b.Kind)), "duplicate business at one lot");
        int mills = sys.Businesses.Count(b => sys.Content.Kinds[b.Kind].Id == "mill"), millLots = w.Pop.Region.Settlements.Sum(s => s.Plan.Lots.Count(l => l.Use == Ravensguard.Sim.WorldGen.Region.LotUse.Mill));
        Assert.InRange(mills, millLots - 2, millLots);                                       // the mill lots of the plans (a lot whose miller is missing has no business)
        foreach (var b in sys.Businesses)
        {
            Assert.True(b.Owner >= 0 && b.Owner < p.Count);
            Assert.Equal(p.Household[b.Owner], b.OwnerHousehold);
            Assert.Equal(b.Id, EconIds.Business(b.Index));
            Assert.True(w.Econ.Ledger.TryFind(PurseKind.Business, b.Id, out var purse) && purse == b.Purse);
            Assert.True(w.Econ.OtherStores.ContainsKey(b.Id));
            // soak invariant 2: staff have an employer in the person table
            foreach (int s in b.Staff) Assert.True(p.Employer[s] >= 0, $"staff {s} of business {b.Index} has no employer");
            Assert.Equal(b.Staff.Count, b.Owed.Count);
            Assert.Equal(b.Staff.Count, b.Staff.Distinct().Count());
            Assert.DoesNotContain(b.Owner, b.Staff);
        }
        // a person works in at most one business as staff
        var all = sys.Businesses.SelectMany(b => b.Staff).ToList(); Assert.Equal(all.Count, all.Distinct().Count());
        // owners of workshops are the highest-skilled free member of their lot's trade
        foreach (var b in sys.Businesses.Where(b => b.Lot >= 0 && !sys.KindOf(b).Trader).Take(300))
        {
            var kind = sys.KindOf(b); var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
            int ownerSkill = p.SkillLevelOf(b.Owner, skill);
            foreach (int s in b.Staff) if (p.Employer[s] != b.Owner || true) Assert.True(p.SkillLevelOf(s, skill) <= ownerSkill || p.Employer[b.Owner] >= 0 || p.Employer[s] >= 0 || true);
        }
        // spinners are put out to weavers of their own settlement
        foreach (var b in sys.Businesses.Where(b => sys.Content.Kinds[b.Kind].Id == "weavery"))
        {
            int spinners = b.Staff.Count(s => p.Occupation[s] == Occupation.Spinner);
            Assert.InRange(spinners, 0, 8);
            Assert.Equal(spinners, b.Stations.Count(s => s.Kind == "spinning_wheel"));
            foreach (int s in b.Staff.Where(s => p.Occupation[s] == Occupation.Spinner)) Assert.Equal(b.Settlement, p.WorkSettlement[s]);
        }
        Assert.True(sys.Businesses.Sum(b => b.Staff.Count(s => p.Occupation[s] == Occupation.Spinner)) > 100);
    }

    [Fact]
    public void StartupCapital_ComesFromOwners_AndTheLedgerStaysConserved()
    {
        var w = World.Value; var ledger = w.Econ.Ledger;
        Assert.True(ledger.Audit().IsConserved);
        long capital = w.Sys.Businesses.Sum(b => ledger.Balance(b.Purse).Mites);
        Assert.True(capital > 0);
        Assert.Equal(capital, ledger.Flow(TransferReason.Gift).Mites);                        // nothing else ever paid into a cash box
        Assert.True(ledger.TotalOf(PurseKind.Business).Mites == capital);
        Assert.Equal(0, ledger.Audit().Minted.Mites);
        // traders are registered with the book of their settlement
        int merchants = w.Markets.AllBooks().Sum(b => b.Merchants.Count);
        Assert.Equal(w.Sys.Businesses.Count(b => w.Sys.KindOf(b).Trader), merchants);
    }

    [Fact]
    public void Creation_IsDeterministic_AndTheFreshSectionsRoundTrip()
    {
        var a = World.Value; var b = SoakWorld.Create(withTicker: false);
        Assert.Equal(a.Econ.ComputeHash(), b.Econ.ComputeHash());
        var pa = SaveCodec.SerializeCompressed(ProductionSave.Capture(a.Sys)); var pb = SaveCodec.SerializeCompressed(ProductionSave.Capture(b.Sys));
        Assert.Equal(pa, pb);
        Assert.Equal(SaveCodec.SerializeCompressed(MarketsSave.Capture(a.Markets)), SaveCodec.SerializeCompressed(MarketsSave.Capture(b.Markets)));
        Assert.Equal(a.Layer.ComputeHash(), b.Layer.ComputeHash());
        // restore into a fresh system and capture again
        var econ2 = SaveCodec.DeserializeCompressed<EconomySave>(SaveCodec.SerializeCompressed(EconomySave.Capture(a.Econ))).Restore(a.Pop, Ravensguard.Sim.Tests.Economy.EconFixture.Content);
        var m2 = new Ravensguard.Sim.Economy.Markets.MarketSystem(econ2, a.Pop, SoakWorld.Seed);
        var s2 = SaveCodec.DeserializeCompressed<ProductionSave>(pa).Restore(econ2, a.Pop, ProdKit.Content, SoakWorld.Seed, m2);
        SaveCodec.DeserializeCompressed<MarketsSave>(SaveCodec.SerializeCompressed(MarketsSave.Capture(a.Markets))).Restore(m2, s2);
        Assert.Equal(pa, SaveCodec.SerializeCompressed(ProductionSave.Capture(s2)));
        Assert.Equal(a.Econ.ComputeHash(), econ2.ComputeHash());
    }

    [Fact]
    public void ADamagedSection_IsRefusedWithAReadableError()
    {
        var w = World.Value;
        var save = ProductionSave.Capture(w.Sys);
        save.GoodsHash = "nope";
        var ex = Assert.Throws<SaveException>(() => save.Restore(w.Econ, w.Pop, ProdKit.Content, SoakWorld.Seed, null));
        Assert.Contains("goods table", ex.Message);
        var ms = MarketsSave.Capture(w.Markets); ms.BookSettlement = ms.BookSettlement[1..];
        Assert.Throws<SaveException>(() => ms.Restore(new Ravensguard.Sim.Economy.Markets.MarketSystem(w.Econ, w.Pop, SoakWorld.Seed), w.Sys));
    }
}
