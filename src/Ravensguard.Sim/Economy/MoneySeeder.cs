using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// Seed money (money spec 3.8): <c>seed_money_crowns_per_person x population</c> crowns, split into shares by group (gentry, burgher and peasant households,
/// fief treasuries, parish chests), each share divided by weight with the largest-remainder rule so the total is exact. Ties break by a hash of the owner id
/// (stream 0x220), never by position, so the result does not depend on iteration order. Created once as <see cref="SourceReason.Seed"/>, before day 0.
/// </summary>
public static class MoneySeeder
{
    /// <summary>Largest-remainder apportionment of <paramref name="total"/> by weight; ties by ascending key then index. The parts sum to the total exactly.</summary>
    public static long[] Apportion(long total, IReadOnlyList<long> weights, IReadOnlyList<ulong> tieKeys)
    {
        int n = weights.Count; var parts = new long[n];
        if (n == 0) return parts;
        Int128 sumW = 0; foreach (var w in weights) sumW += w;
        if (sumW <= 0) throw new ArgumentException("weights must sum to a positive number");
        var rem = new Int128[n]; long given = 0;
        for (int i = 0; i < n; i++)
        {
            Int128 prod = (Int128)total * weights[i];
            parts[i] = (long)(prod / sumW); rem[i] = prod % sumW; given += parts[i];
        }
        long left = total - given;
        var order = Enumerable.Range(0, n).OrderByDescending(i => rem[i]).ThenBy(i => tieKeys[i]).ThenBy(i => i).Take((int)left).ToList();
        foreach (int i in order) parts[i]++;
        return parts;
    }

    public static void Seed(EconomyState econ, Population pop, ulong seed)
    {
        var m = econ.Money;
        int[] pct = [m.SeedGentryPct, m.SeedBurgherPct, m.SeedPeasantPct, m.SeedTreasuryPct, m.SeedParishPct];
        if (pct.Sum() != 100 || pct.Any(p => p < 0)) throw new InvalidDataException("seed money shares must be non-negative and sum to 100");
        long total = (long)pop.People.Count * m.SeedMoneyCrownsPerPerson * Coin.MitesPerCrown;
        var hh = pop.Households;

        // households by class; weight 1 + 0.25 x workers, scaled by 4 (workers are members of working age, 12 and over)
        var groups = new List<(PurseId Purse, long Weight, ulong Tie)>[5];
        for (int g = 0; g < 5; g++) groups[g] = [];
        for (int h = 0; h < hh.Count; h++)
        {
            int workers = 0;
            foreach (int p in hh.Members(h)) if (pop.Age(p) >= 12) workers++;
            int g = hh.Class[h] switch { SocialClass.Gentry => 0, SocialClass.Burgher => 1, _ => 2 };
            groups[g].Add((econ.HouseholdPurse[h], 4 + workers, DetRng.Next(seed, 0, hh.IdOf(h).Value, 0x220, 0)));
        }
        foreach (var f in pop.Region.Fiefs)
            groups[3].Add((econ.Ledger.Open(PurseKind.Treasury, f.Id), Math.Max(1, f.Acres), DetRng.Next(seed, 0, f.Id.Value, 0x220, 0)));
        for (int s = 0; s < pop.PriestHousehold.Length; s++)
            if (pop.PriestHousehold[s] >= 0)
            {
                var sid = pop.Region.Settlements[s].Id;
                groups[4].Add((econ.Ledger.Open(PurseKind.Chest, sid), 1, DetRng.Next(seed, 0, sid.Value, 0x220, 0)));
            }

        // an empty group's share goes to the largest non-empty one (ties: lower group index)
        var share = (int[])pct.Clone();
        for (int g = 0; g < 5; g++)
        {
            if (groups[g].Count > 0 || share[g] == 0) continue;
            int best = -1;
            for (int k = 0; k < 5; k++) if (groups[k].Count > 0 && (best < 0 || share[k] > share[best])) best = k;
            if (best < 0) throw new InvalidOperationException("no purse to receive seed money");
            share[best] += share[g]; share[g] = 0;
        }
        var groupTotal = Apportion(total, share.Select(s => (long)s).ToList(), [0, 1, 2, 3, 4]);
        for (int g = 0; g < 5; g++)
        {
            if (groups[g].Count == 0 || groupTotal[g] == 0) continue;
            var parts = Apportion(groupTotal[g], groups[g].Select(x => x.Weight).ToList(), groups[g].Select(x => x.Tie).ToList());
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] > 0) econ.Ledger.Mint(groups[g][i].Purse, new Coin(parts[i]), SourceReason.Seed);
        }
    }
}
