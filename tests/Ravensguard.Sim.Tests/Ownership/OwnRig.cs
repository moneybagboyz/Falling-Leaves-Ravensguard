using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Ownership;

/// <summary>
/// A private world of seed 1234 (its own region, people and economy) with a hero row (the hero's household is his home chest), his purse seeded, the land and ownership installed as the runtime
/// installs them, the real ticker driving every system. Finders pick in a fixed order (lowest index first), so every run picks the same.
/// </summary>
internal sealed class OwnRig : IDisposable
{
    public required SoakWorld W { get; init; }
    public required LandWorld L { get; init; }
    public required OwnershipWorld O { get; init; }
    public int HeroRow { get; init; }
    public EntityId Hero { get; } = EconomyState.HeroOwner;
    public PurseId HeroPurse { get; init; }
    public Ledger Led => W.Econ.Ledger;
    public PersonTable P => W.Pop.People;
    public static ContentDatabase Content => EconFixture.Content;

    public static OwnRig Make(int crowns = 2000, bool ticker = true)
    {
        var w = SoakWorld.Create(withTicker: ticker);
        var purse = w.Econ.SeedHero(crowns);
        var origin = Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var rec = HeroRecord.FromOrigin("Test Hero", false, origin);
        int town = w.Pop.Region.Settlements.FindIndex(s => s.Class == SettlementClass.Town);
        int row = HeroPerson.Insert(w.Pop, w.Econ, Content, rec, EconomyState.HeroOwner, SoakWorld.Seed, w.Pop.NowDay, town, [], w.Sim, w.Sim.Ids, rollKin: false);
        rec.PersonIndex = row;
        var lw = new LandWorld(new LandBook(), w.Pop, w.Econ, Content, () => w.Economy.Growth.State, null, w.Markets);
        w.Sim.Set(lw);
        var ow = new OwnershipWorld(new OwnershipBook(), w.Sys, Content, SoakWorld.Seed, 0, w.Sim.Ids)
        {
            HeroRow = () => row, Land = lw, Fairs = Ravensguard.Sim.Travel.FairBook.From(Content), HeroSkill = _ => 50, HeroPerception = () => 50,
        };
        w.Sim.Set(ow);
        w.Economy.Labour.Rebuild();
        return new OwnRig { W = w, L = lw, O = ow, HeroRow = row, HeroPurse = purse };
    }

    public long Coin => Led.Balance(HeroPurse).Mites;
    public long Chest => O.ChestCoin;
    public int Day => (int)((W.Sim.State.HourDone + 1) / 24);
    public long Sec => (W.Sim.State.HourDone + 1) * 3600;
    public ushort G(string id) => W.Econ.Goods.IdOf(id);

    public int Town() => W.Pop.Region.Settlements.FindIndex(s => s.Class == SettlementClass.Town);

    /// <summary>A business of the kind the hero owns at a settlement (built as founding does, without a room): the kind's stations, capital from his purse.</summary>
    public Ravensguard.Sim.Production.Business Own(string kind, int settlement, long capital)
    {
        var k = W.Sys.Content.Kind(kind);
        var b = W.Sys.NewBusiness(k, settlement, -1, HeroRow);
        b.OwnerWorks = false;
        foreach (var (st, n) in k.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Station { Kind = st });
        if (capital > 0) Led.Transfer(HeroPurse, b.Purse, new Coin(capital), TransferReason.Gift);
        b.Capital = capital; b.CashStart = capital;
        var h = O.Hold(b); h.FoundedDay = Day;
        return b;
    }

    /// <summary>The lowest-index alive adult of a settlement with no post, no business, not gentry.</summary>
    public IEnumerable<int> Idle(int settlement)
    {
        var hht = W.Pop.Households;
        for (int i = 0; i < P.Count; i++)
        {
            if (P.Status[i] != PersonStatus.Alive || P.HomeSettlement[i] != settlement || P.Employer[i] >= 0 || W.Economy.Labour.IsStaff(i)) continue;
            if (P.Occupation[i] != Occupation.None || hht.Class[P.Household[i]] == SocialClass.Gentry || W.Sys.Businesses.Any(b => b.Owner == i)) continue;
            int age = P.AgeYears(i, Day); if (age < 20 || age > 50) continue;
            yield return i;
        }
    }

    public bool CoinExact()
    {
        var a = Led.Audit(); long sum = 0;
        for (int p = 0; p < Led.PurseCount; p++) sum += Led.Balance(new PurseId(p)).Mites;
        return a.IsConserved && sum == a.InPurses.Mites;
    }

    public void Dispose() => W.Dispose();
}
