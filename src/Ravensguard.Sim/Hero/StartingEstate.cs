using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Succession;

/// <summary>What an origin's inheritance came to at the start: the business, the lease it stands in, and what that lease owes.</summary>
public sealed record StartingEstateResult(EntityId Business, EntityId Lease, long ArrearsMites, string Note);

/// <summary>
/// P5-H: the estate an origin begins with (<see cref="OriginInheritsDef"/>; the tanner's widow: her late husband's tannery). Applied once at a new game, through the same rules play uses: the first
/// vacant house the lord of the start place lets (lowest lot) is leased in the hero's name (<see cref="LandWorld.LeaseHouse"/>) with <c>arrears_seasons</c> of its rent owed (the husband's debt:
/// it climbs the arrears ladder at the next season's dues unless she pays it, <c>ld:pay</c>); the business is the hero's (her person row its owner, hands-on, inherited the day the game starts), its
/// stations the kind's, its cash box and stock seed goods and seed coin (the only faucets a new game has: money spec 3.8). Nothing is drawn.
/// </summary>
public static class StartingEstate
{
    public static StartingEstateResult? Apply(WorldState st, ContentDatabase db, OriginDef origin, int settlement, long nowSec)
    {
        if (origin.Inherits is not { } inh || st.Economy is not { } e || st.Production is not { } prod || st.Hero is not { } hero) return null;
        var pt = st.Population.People;
        int row = HeroPerson.RowOf(pt, hero, EconomyState.HeroOwner);
        if (row < 0) return new StartingEstateResult(default, default, 0, "no hero row");
        var kind = prod.Content.Kinds.FirstOrDefault(k => k.Id == inh.Business);
        if (kind == null) return new StartingEstateResult(default, default, 0, "no business kind " + inh.Business);
        var region = st.Population.Region;
        if ((uint)settlement >= (uint)region.Settlements.Count) return null;
        int day = (int)(nowSec / 86400);
        var lw = LandWiring.For(st, db);
        var heroId = EconomyState.HeroOwner;
        EntityId Chron(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
        {
            var id = st.Ids.Allocate(IdKind.Event);
            var d = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var (k, v) in data) d[k] = v;
            st.Chronicle.Add(new ChronicleEntry(id, when, type, subjects, causes, d));
            return id;
        }
        // the premises: the first house the lord lets in the place (lowest lot), leased in her name; its rent behind by the seasons the husband left unpaid
        int lot = -1; EntityId lease = default; long arrears = 0;
        var lots = region.Settlements[settlement].Plan.Lots;
        for (int l = 0; l <= lots.Count && lot < 0; l++)
        {
            // a vacant house of the lord's, lowest lot first; with none, a plot of his let on the same terms (a new lot of the plan)
            var r = l < lots.Count ? lw.LeaseHouse(heroId, PurseKind.Person, settlement, l, day, Chron) : lw.LeasePlot(heroId, PurseKind.Person, settlement, day, Chron);
            if (!r.Ok) continue;
            lot = l < lots.Count ? l : lots.Count - 1; lease = r.Deed;
            var deed = lw.Book.Deeds[lease];
            deed.AcquiredDay = day; deed.Cause = DeedCause.Inherited;
            arrears = deed.DuesMitesPerSeason * Math.Max(0, inh.ArrearsSeasons);
            deed.OwedMites = arrears; deed.ArrearsSeasons = (byte)Math.Clamp(inh.ArrearsSeasons, 0, 255);
            lw.Book.Changes.Add(new DeedChange(day, deed.Id, deed.Holder, deed.Tenure, deed.DuesMitesPerSeason, deed.LeaseEndDay, DeedCause.Inherited));
        }
        // the business: hers, in that house, its stations, a little coin and stock
        var b = prod.NewBusiness(kind, settlement, lot, row);
        b.OwnerWorks = false;
        foreach (var (stn, n) in kind.Stations) for (int i = 0; i < Math.Max(1, n); i++) b.Stations.Add(new Station { Kind = stn });
        if (inh.CashMites > 0) { e.Ledger.Mint(b.Purse, new Coin(inh.CashMites), SourceReason.Seed); b.Capital = inh.CashMites; b.CashStart = inh.CashMites; }
        foreach (var (good, qty) in inh.Stock.OrderBy(x => x.Key, StringComparer.Ordinal))
            if (qty > 0 && e.Goods.TryIdOf(good, out ushort g)) b.Store.Create(g, qty, CreateReason.Seed);
        var ow = OwnershipWiring.For(st, db);
        var h = ow.Hold(b);
        h.FoundedDay = day - Math.Max(0, inh.Years) * TimeConst.DaysPerYear; h.HandsOn = true; h.InheritedDay = day;
        Chron(new GameTime(nowSec), "estate_settled", [heroId, b.Id], [new Cause(CauseKind.Death, null, "the husband's death")],
            ("name", hero.Name), ("business", kind.Id), ("arrears", arrears.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new StartingEstateResult(b.Id, lease, arrears, lot < 0 ? "no house to let: the business has no premises" : "");
    }
}
