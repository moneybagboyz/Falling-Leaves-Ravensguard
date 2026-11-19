using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Land;

/// <summary>
/// A private world of seed 1234 (its own region, people and economy: the land tests change them) with the land installed as the runtime installs it, the hero's purse seeded with
/// <c>crowns</c>, and a chronicle sink that records what land writes. Finders pick the places the tests need in a fixed order (lowest index first), so every run picks the same.
/// </summary>
internal sealed class LandRig : IDisposable
{
    public required SoakWorld W { get; init; }
    public required LandWorld L { get; init; }
    public EntityId Hero { get; } = EconomyState.HeroOwner;
    public PurseId HeroPurse { get; init; }
    public List<ChronicleEntry> Chron { get; } = [];
    public RegionData Region => W.Pop.Region;
    public Ledger Led => W.Econ.Ledger;

    public static LandRig Make(int crowns = 2000, bool ticker = false)
    {
        var w = SoakWorld.Create(withTicker: ticker);
        var purse = w.Econ.SeedHero(crowns);
        var lw = new LandWorld(new LandBook(), w.Pop, w.Econ, EconFixture.Content, () => w.Economy.Growth.State, null, w.Markets);
        w.Sim.Set(lw);
        w.Economy.Growth.LandHolds = lw.HoldsLot;
        w.Economy.Farming.LeasedOut = lw.IsLeased;
        return new LandRig { W = w, L = lw, HeroPurse = purse };
    }

    public EntityId C(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
    {
        var id = W.Sim.Ids.Allocate(IdKind.Event);
        Chron.Add(new ChronicleEntry(id, when, type, subjects, causes, data.ToDictionary(x => x.Key, x => x.Value)));
        return id;
    }

    public int Day => (int)((W.Sim.State.HourDone + 1) / 24);
    public long Coin => Led.Balance(HeroPurse).Mites;
    public long Bal(EntityId id, PurseKind k) => k == PurseKind.Household ? Led.Balance(W.Econ.HouseholdPurse[W.Pop.Households.IndexOf(id)]).Mites : Led.TryFind(k, id, out var p) ? Led.Balance(p).Mites : 0;

    /// <summary>Moves all of the hero's coin to a test chest (coin stays in the audit).</summary>
    public void Drain() { long c = Coin; if (c > 0) Led.Transfer(HeroPurse, Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 999_999)), new Coin(c), TransferReason.Gift); }

    public int Town() => Region.Settlements.FindIndex(s => s.Class == SettlementClass.Town && L.Listings(Region.Settlements.IndexOf(s)).Any(x => x.Lot < 0));
    public int Village() => Region.Settlements.FindIndex(s => s.Class == SettlementClass.Village && L.Listings(Region.Settlements.IndexOf(s)).Any(x => x.Lot < 0));

    /// <summary>The centre of the first tile (every 7th, ascending) whose land is <paramref name="fief"/>'s kind (-1 wild, else a lord's waste) and where a claim could grow to <paramref name="room"/> tiles.</summary>
    public (int X, int Y) ClaimPoint(bool wild, int room = 24)
    {
        int N = RegionData.Size;
        for (int t = 1000; t < RegionData.Tiles; t += 7)
        {
            if (!LandRules.OpenGround(Region, t)) continue;
            int f = LandRules.LandFief(Region, L.Rules, t);
            if (wild ? f != -1 : f < 0) continue;
            int x = (t % N) * RegionData.TileM + RegionData.TileM / 2, y = (t / N) * RegionData.TileM + RegionData.TileM / 2;
            if (L.Grow(t, [], room, x, y, u => L.ClaimTileOk(u, f, null)).Length >= room) return (x, y);
        }
        throw new InvalidOperationException("no claimable ground");
    }

    /// <summary>True when the tiles are one 4-connected piece.</summary>
    public static bool Connected(int[] tiles)
    {
        if (tiles.Length == 0) return true;
        var set = tiles.ToHashSet(); var seen = new HashSet<int> { tiles[0] }; var q = new Queue<int>(); q.Enqueue(tiles[0]); int N = RegionData.Size;
        while (q.Count > 0)
        {
            int t = q.Dequeue();
            foreach (int n in new[] { t - N, t + N, t % N > 0 ? t - 1 : -1, t % N < N - 1 ? t + 1 : -1 }) if (n >= 0 && set.Contains(n) && seen.Add(n)) q.Enqueue(n);
        }
        return seen.Count == tiles.Length;
    }

    public void Dispose() => W.Dispose();
}
