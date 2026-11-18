using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Tests.Economy;

public class SeedMoneyTests
{
    [Fact] // G-M8 (shares)
    public void G_M8_SharesOfAQuarterMillionCrownsAreExact()
    {
        long total = 25_000L * 50 * 100;                                   // 1,250,000 crowns in mites
        var parts = MoneySeeder.Apportion(total, [20, 35, 30, 12, 3], [0, 1, 2, 3, 4]);
        Assert.Equal([250_000L * 100, 437_500L * 100, 375_000L * 100, 150_000L * 100, 37_500L * 100], parts);
        Assert.Equal(total, parts.Sum());
    }

    [Fact]
    public void Apportion_IsExact_ForAwkwardTotals_AndTiesFollowTheKeyNotThePosition()
    {
        var w = Enumerable.Repeat(1L, 7).ToList();
        var keys = new ulong[] { 70, 60, 50, 40, 30, 20, 10 };
        var parts = MoneySeeder.Apportion(10, w, keys);
        Assert.Equal(10, parts.Sum());
        // 10 / 7: everyone gets 1, the three smallest tie keys (indices 6, 5, 4) get the extra mite
        Assert.Equal([1, 1, 1, 1, 2, 2, 2], parts);
        // same items in the opposite order give the same mite to the same key
        var rev = MoneySeeder.Apportion(10, w, keys.Reverse().ToList());
        Assert.Equal(parts.Reverse(), rev);
        var weighted = MoneySeeder.Apportion(1_000_003, [5, 9, 1, 1, 13], [4, 3, 2, 1, 0]);
        Assert.Equal(1_000_003, weighted.Sum());
        Assert.Throws<ArgumentException>(() => MoneySeeder.Apportion(5, [0, 0], [0, 1]));
    }

    [Fact] // G-M8 (permutation)
    public void Apportion_IsPermutationInvariant_PerOwner()
    {
        const ulong seed = 7; int n = 997;
        var ids = Enumerable.Range(1, n).Select(i => EntityId.Make(IdKind.Household, (ulong)i)).ToList();
        long W(EntityId id) => 4 + (long)(id.Counter % 6);
        ulong Tie(EntityId id) => DetRng.Next(seed, 0, id.Value, 0x220, 0);
        var baseline = MoneySeeder.Apportion(123_456_789, ids.Select(W).ToList(), ids.Select(Tie).ToList());
        var byId = ids.Zip(baseline).ToDictionary(x => x.First, x => x.Second);
        for (int round = 0; round < 5; round++)
        {
            var shuffled = ids.OrderBy(id => DetRng.Next(99, round, id.Value, 1, 0)).ToList();
            var got = MoneySeeder.Apportion(123_456_789, shuffled.Select(W).ToList(), shuffled.Select(Tie).ToList());
            for (int i = 0; i < n; i++) Assert.Equal(byId[shuffled[i]], got[i]);
        }
    }

    [Fact]
    public void WorldSeed_CreatesExactlyFiftyCrownsPerPerson_AndTheLedgerBalances()
    {
        var pop = EconFixture.Pop; var e = EconFixture.NewEconomy();
        long expected = (long)pop.People.Count * 50 * 100;
        var r = e.Ledger.Audit();
        Assert.True(r.IsConserved, r.ToString());
        Assert.Equal(expected, r.Seeded.Mites); Assert.Equal(expected, r.InPurses.Mites); Assert.Equal(Coin.Zero, r.Minted); Assert.Equal(Coin.Zero, r.Sunk);

        // group totals follow the money settings: 20 / 35 / 30 / 12 / 3 percent (a group with no members passes its share on, so compare only when all exist)
        long Sum(SocialClass c) { long s = 0; for (int h = 0; h < pop.Households.Count; h++) if (pop.Households.Class[h] == c) s += e.HouseholdCoin(h).Mites; return s; }
        long treasuries = e.Ledger.TotalOf(PurseKind.Treasury).Mites, chests = e.Ledger.TotalOf(PurseKind.Chest).Mites;
        Assert.True(pop.Region.Fiefs.Count > 0 && pop.PriestHousehold.Any(p => p >= 0) && Enum.GetValues<SocialClass>().All(c => Enumerable.Range(0, pop.Households.Count).Any(h => pop.Households.Class[h] == c)));
        Assert.Equal(expected * 20 / 100, Sum(SocialClass.Gentry)); Assert.Equal(expected * 35 / 100, Sum(SocialClass.Burgher)); Assert.Equal(expected * 30 / 100, Sum(SocialClass.Peasant));
        Assert.Equal(expected * 12 / 100, treasuries); Assert.Equal(expected * 3 / 100, chests);
        // richer classes hold more per household on average, and nobody is left empty-handed
        double Avg(SocialClass c) { var l = Enumerable.Range(0, pop.Households.Count).Where(h => pop.Households.Class[h] == c).Select(h => e.HouseholdCoin(h).Mites).ToList(); return l.Average(); }
        Assert.True(Avg(SocialClass.Gentry) > Avg(SocialClass.Burgher) && Avg(SocialClass.Burgher) > Avg(SocialClass.Peasant));
        for (int h = 0; h < pop.Households.Count; h++) Assert.True(e.HouseholdCoin(h).Mites > 0, $"household {h}");
    }

    [Fact]
    public void WorldSeed_IsDeterministic()
    {
        Assert.Equal(EconFixture.NewEconomy().ComputeHash(), EconFixture.NewEconomy().ComputeHash());
        var other = EconomyState.Create(EconFixture.Pop, EconFixture.Content, 99);
        Assert.NotEqual(EconFixture.NewEconomy().ComputeHash(), other.ComputeHash()); // tie breaks follow the seed
        Assert.Equal(EconFixture.NewEconomy().Ledger.Audit().InPurses, other.Ledger.Audit().InPurses);
    }

    [Fact]
    public void Hero_CoinEntersTheLedgerAsSeedMoney()
    {
        var e = EconFixture.NewEconomy(goods: false);
        long before = e.Ledger.Audit().Seeded.Mites;
        var p = e.SeedHero(50);
        Assert.Equal(Coin.FromCrowns(50), e.Ledger.Balance(p));
        Assert.Equal(before + 5000, e.Ledger.Audit().Seeded.Mites);
        Assert.True(e.Ledger.Audit().IsConserved);
        Assert.Equal(p, e.SeedHero(0)); // idempotent
    }

    [Fact]
    public void HouseholdsHaveAPurseAndAStore()
    {
        var pop = EconFixture.Pop; var e = EconFixture.NewEconomy();
        Assert.Equal(pop.Households.Count, e.HouseholdCount);
        for (int h = 0; h < pop.Households.Count; h++)
        {
            Assert.Equal(PurseKind.Household, e.Ledger.KindOf(e.HouseholdPurse[h])); Assert.Equal(pop.Households.IdOf(h), e.Ledger.OwnerOf(e.HouseholdPurse[h]));
            Assert.Equal(pop.Households.IdOf(h), e.HouseholdStore[h].Owner); Assert.Equal(Ravensguard.Sim.Goods.StoreKind.House, e.HouseholdStore[h].Kind);
        }
    }
}
