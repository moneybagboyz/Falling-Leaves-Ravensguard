using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.People;

public class PeopleTests(ITestOutputHelper output)
{
    private static readonly ulong[] Seeds = [1, 2, 1234];

    private sealed record World(WorldHistory H, RegionData R, Population P, long PopMs, long Bytes);
    private static readonly Dictionary<ulong, World> Cache = [];
    private static ContentDatabase? _content;

    private static ContentDatabase Content()
    {
        lock (Cache)
        {
            if (_content != null) return _content;
            var load = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!]);
            Assert.True(load.Success, load.Report.ToString());
            return _content = load.Database;
        }
    }

    private static World Get(ulong seed)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(seed, out var w)) return w;
            var dir = ContinentSettings.FindBaseContent();
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
            var c = ContinentGenerator.Generate(cs);
            var h = HistoryGenerator.Generate(c, hs);
            var pick = EmbarkSite.AutoPick(c, h);
            var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
            var content = Content();
            Populator.Populate(r, h, content); // warm up the JIT so the timing below is the steady state
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var p = Populator.Populate(r, h, content);
            sw.Stop();
            long after = GC.GetTotalMemory(true);
            return Cache[seed] = new World(h, r, p, sw.ElapsedMilliseconds, after - before);
        }
    }

    public static IEnumerable<object[]> SeedCases() => Seeds.Select(s => new object[] { s });

    [Theory, MemberData(nameof(SeedCases))]
    public void Stats(ulong seed)
    {
        var w = Get(seed); var p = w.P; var t = p.People;
        int kids = 0, adults = 0, elders = 0;
        for (int i = 0; i < t.Count; i++) { int a = p.Age(i); if (a < 15) kids++; else if (a < 50) adults++; else elders++; }
        output.WriteLine($"seed {seed}: persons {t.Count} (target {w.R.TotalPopulation}), households {p.Households.Count} (target {w.R.TotalHouseholds}), fiefs {w.R.Fiefs.Count}, settlements {w.R.Settlements.Count}");
        output.WriteLine($"  ages 0-14 {kids * 100.0 / t.Count:0.0}% 15-49 {adults * 100.0 / t.Count:0.0}% 50+ {elders * 100.0 / t.Count:0.0}%");
        output.WriteLine($"  class: " + string.Join(' ', Enum.GetValues<SocialClass>().Select(c => $"{c}={Enumerable.Range(0, t.Count).Count(i => t.Class[i] == c)}")));
        output.WriteLine($"  gentry households {Enumerable.Range(0, p.Households.Count).Count(h => p.Households.Class[h] == SocialClass.Gentry)}, burgher people {Enumerable.Range(0, t.Count).Count(i => p.Households.Class[t.Household[i]] == SocialClass.Burgher)}");
        output.WriteLine($"  homeless {Enumerable.Range(0, t.Count).Count(i => t.Homeless[i] != HomelessReason.None)}");
        output.WriteLine($"  populate {w.PopMs} ms, bytes/person {p.ApproxBytes() / t.Count}, gc/person {w.Bytes / t.Count}");
        output.WriteLine($"  occupations: " + string.Join(' ', Enum.GetValues<Occupation>().Select(o => $"{o}={Enumerable.Range(0, t.Count).Count(i => t.Occupation[i] == o)}")));
        foreach (var cls in new[] { SettlementClass.Town, SettlementClass.Village, SettlementClass.Farmstead })
        {
            int pop = 0, hh = 0;
            for (int h = 0; h < p.Households.Count; h++) if (w.R.Settlements[p.Households.Settlement[h]].Class == cls) { hh++; pop += p.Households.MemberCount[h]; }
            output.WriteLine($"  {cls}: households {hh}, people {pop}, mean {pop / (double)Math.Max(1, hh):0.00}");
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Same_inputs_same_hash(ulong seed)
    {
        var w = Get(seed);
        var again = Populator.Populate(w.R, w.H, Content());
        Assert.Equal(w.P.ComputeHash(), again.ComputeHash());
    }

    [Fact]
    public void Different_seeds_differ() => Assert.NotEqual(Get(1).P.ComputeHash(), Get(2).P.ComputeHash());

    [Theory, MemberData(nameof(SeedCases))]
    public void Population_matches_region_target_within_five_percent(ulong seed)
    {
        var w = Get(seed);
        long target = w.R.TotalPopulation;
        Assert.InRange(w.P.People.Count, target * 95 / 100, target * 105 / 100);
        Assert.Equal(w.R.TotalHouseholds, w.P.Households.Count - VagrantHouseholds(w.P));
    }

    private static int VagrantHouseholds(Population p) => Enumerable.Range(0, p.Households.Count).Count(h => p.Households.Dwelling[h] == DwellingType.None);

    [Theory, MemberData(nameof(SeedCases))]
    public void Household_sizes_follow_settlement_type(ulong seed)
    {
        var w = Get(seed); var p = w.P;
        var rs = RegionSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        void Check(SettlementClass cls, double expect)
        {
            int pop = 0, hh = 0;
            for (int h = 0; h < p.Households.Count; h++)
                if (w.R.Settlements[p.Households.Settlement[h]].Class == cls) { hh++; pop += p.Households.MemberCount[h]; }
            if (hh == 0) return;
            Assert.InRange(pop / (double)hh, expect - 0.5, expect + 0.5);
        }
        Check(SettlementClass.Town, rs.TownHouseholdSizeX10 / 10.0);
        Check(SettlementClass.Village, rs.VillageHouseholdSizeX10 / 10.0);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Age_pyramid_matches_life_table(ulong seed)
    {
        var w = Get(seed); var p = w.P; int n = p.People.Count;
        int kids = 0, elders = 0;
        for (int i = 0; i < n; i++) { int a = p.Age(i); if (a < 15) kids++; else if (a >= 50) elders++; }
        double k = kids * 100.0 / n, e = elders * 100.0 / n, a2 = 100 - k - e;
        Assert.InRange(k, 33, 39); Assert.InRange(a2, 45, 51); Assert.InRange(e, 13, 19);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Everyone_has_a_household_and_a_bed_within_capacity(ulong seed)
    {
        var w = Get(seed); var p = w.P; var t = p.People;
        int homeless = 0;
        var occupants = new Dictionary<(int S, int L), HashSet<int>>();
        for (int i = 0; i < t.Count; i++)
        {
            int h = t.Household[i];
            Assert.InRange(h, 0, p.Households.Count - 1);
            Assert.Contains(i, p.Households.Members(h));
            if (t.Bed[i] < 0)
            {
                homeless++;
                Assert.NotEqual(HomelessReason.None, t.Homeless[i]);
                continue;
            }
            Assert.Equal(HomelessReason.None, t.Homeless[i]);
            int s = t.HomeSettlement[i], l = t.HomeLot[i];
            Assert.True(l >= 0, "a bed needs a dwelling lot");
            Assert.True(w.R.Settlements[s].Plan.Lots[l].IsDwelling);
            Assert.InRange((int)t.Bed[i], 0, p.LotBeds[s][l] - 1);
            Assert.Equal(s, p.Households.Settlement[h]); Assert.Equal(l, p.Households.Lot[h]);
            Assert.True(occupants.TryGetValue((s, l), out var set) ? set.Add(t.Bed[i]) : occupants.TryAdd((s, l), [t.Bed[i]]), "two people share one bed");
        }
        Assert.True(homeless * 100 < t.Count * 2, $"{homeless} homeless of {t.Count}");
        foreach (var ((s, l), set) in occupants) Assert.True(set.Count <= p.LotBeds[s][l]);
        for (int h = 0; h < p.Households.Count; h++)
            Assert.True(p.Households.MemberCount[h] <= Math.Max(p.Households.Beds[h], p.Households.Dwelling[h] == DwellingType.None ? int.MaxValue : 0) || p.Households.Dwelling[h] == DwellingType.None);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Kin_graph_is_consistent(ulong seed)
    {
        var w = Get(seed); var p = w.P; var t = p.People;
        for (int i = 0; i < t.Count; i++)
        {
            int sp = t.Spouse[i];
            if (sp >= 0) { Assert.Equal(i, t.Spouse[sp]); Assert.NotEqual(t.Sex[i], t.Sex[sp]); }
            if (t.Father[i] >= 0)
            {
                Assert.Equal(Sex.Male, t.Sex[t.Father[i]]);
                Assert.True(t.BirthDay[i] - t.BirthDay[t.Father[i]] >= 14 * TimeConst.DaysPerYear, "father too young");
            }
            if (t.Mother[i] >= 0)
            {
                Assert.Equal(Sex.Female, t.Sex[t.Mother[i]]);
                Assert.True(t.BirthDay[i] - t.BirthDay[t.Mother[i]] >= 14 * TimeConst.DaysPerYear, "mother too young");
            }
        }
        Assert.False(p.Kin.HasParentCycle());
        // Siblings are symmetric.
        for (int i = 0; i < t.Count; i += 7)
            foreach (int s in p.Kin.Siblings(i)) Assert.Contains(i, p.Kin.Siblings(s));
    }

    [Fact]
    public void Heir_order_is_eldest_son_then_children_then_spouse_then_lord()
    {
        var w = Get(1); var p = w.P; var t = p.People;
        int checkedOne = 0;
        for (int f = 0; f < w.R.Fiefs.Count && checkedOne < 5; f++)
        {
            int lord = p.FiefLord[f];
            if (lord < 0) continue;
            var order = p.Kin.HeirOrder(lord);
            var kids = p.Kin.Children(lord).ToArray();
            var sons = kids.Where(c => t.Sex[c] == Sex.Male).ToList();
            var daughters = kids.Where(c => t.Sex[c] == Sex.Female).ToList();
            Assert.Equal(sons.Concat(daughters).Concat(t.Spouse[lord] >= 0 ? [t.Spouse[lord]] : []), order);
            for (int k = 1; k < sons.Count; k++) Assert.True(t.BirthDay[sons[k - 1]] <= t.BirthDay[sons[k]]);
            checkedOne++;
        }
        Assert.True(checkedOne > 0);
        // A tenant's heir chain ends at the lord.
        int farmer = Enumerable.Range(0, t.Count).First(i => t.Occupation[i] == Occupation.Farmer && t.Employer[i] >= 0 && t.Father[i] < 0);
        Assert.Equal(t.Employer[farmer], p.Kin.HeirOrder(farmer)[^1]);
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Lords_descend_from_history_dynasties(ulong seed)
    {
        var w = Get(seed); var p = w.P; var t = p.People;
        int fromDynasty = 0, fromNotable = 0;
        for (int f = 0; f < w.R.Fiefs.Count; f++)
        {
            int lord = p.FiefLord[f];
            Assert.True(lord >= 0, $"fief {f} has no lord");
            Assert.Equal(Occupation.Lord, t.Occupation[lord]);
            Assert.Equal(SocialClass.Gentry, t.Class[lord]);
            var d = t.Dynasty[lord];
            if (d != EntityId.None) { fromDynasty++; Assert.NotNull(w.H.GetDynasty(d)); Assert.Equal(w.H.GetDynasty(d)!.Name, t.Family[lord]); }
            if (t.Notable[lord] != EntityId.None) { fromNotable++; Assert.NotNull(w.H.GetNotable(t.Notable[lord])); }
        }
        output.WriteLine($"seed {seed}: {fromDynasty} of {w.R.Fiefs.Count} lords from dynasties, {fromNotable} are history notables themselves");
        Assert.True(fromDynasty >= 3, $"only {fromDynasty} lords descend from a dynasty");
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Names_are_nearly_unique(ulong seed)
    {
        var w = Get(seed); var t = w.P.People;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < t.Count; i++) { string n = t.FullName(i); counts[n] = counts.GetValueOrDefault(n) + 1; }
        int dup = counts.Values.Where(c => c > 1).Sum();
        Assert.True(dup * 100 < t.Count, $"{dup} people share a full name");
        for (int i = 0; i < t.Count; i++) { Assert.NotEqual("", t.Given[i]); Assert.NotEqual("", t.Family[i]); }
    }

    [Fact]
    public void Name_pools_have_enough_given_names_in_three_cultures()
    {
        var pools = new NamePools(Content().All<NamePoolDef>(), 1);
        Assert.True(pools.Pools.Count >= 3);
        foreach (var pool in pools.Pools)
        {
            Assert.True(pool.Male.Distinct().Count() >= 150, $"{pool.Id} male");
            Assert.True(pool.Female.Distinct().Count() >= 150, $"{pool.Id} female");
            Assert.Empty(pool.Male.Intersect(pool.Female));
        }
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Workers_have_workplaces_and_workplaces_have_keepers(ulong seed)
    {
        var w = Get(seed); var p = w.P; var t = p.People;
        for (int i = 0; i < t.Count; i++)
        {
            if (t.Occupation[i] == Occupation.None) { Assert.Equal(-1, t.WorkSettlement[i]); continue; }
            Assert.True(t.WorkSettlement[i] >= 0);
            if (t.Occupation[i] is Occupation.Servant or Occupation.Guard or Occupation.Steward) Assert.True(t.Employer[i] >= 0, "servants, guards and stewards have employers");
            if (t.Employer[i] >= 0) Assert.NotEqual(i, t.Employer[i]);
        }
        int smithies = 0, taverns = 0, mills = 0, churches = 0;
        for (int si = 0; si < w.R.Settlements.Count; si++)
            foreach (var lot in w.R.Settlements[si].Plan.Lots)
            {
                int keeper = p.LotKeeper[si][lot.Index];
                switch (lot.Use)
                {
                    case LotUse.Smithy: if (keeper >= 0) { smithies++; Assert.Equal(Occupation.Smith, t.Occupation[keeper]); Assert.Equal(lot.Index, t.WorkLot[keeper]); } break;
                    case LotUse.Tavern: if (keeper >= 0) { taverns++; Assert.Equal(Occupation.Innkeeper, t.Occupation[keeper]); } break;
                    case LotUse.Mill: if (keeper >= 0) { mills++; Assert.Equal(Occupation.Miller, t.Occupation[keeper]); } break;
                    case LotUse.Church or LotUse.Chapel: if (keeper >= 0) { churches++; Assert.Equal(Occupation.Priest, t.Occupation[keeper]); } break;
                }
            }
        Assert.True(smithies > 0 && taverns > 0 && churches > 0);
        output.WriteLine($"seed {seed}: workplaces with keepers: smithies {smithies}, taverns {taverns}, mills {mills}, churches {churches}");
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Parcels_are_held_by_households(ulong seed)
    {
        var w = Get(seed); var p = w.P;
        int none = 0;
        for (int i = 0; i < w.R.Parcels.Count; i++)
        {
            int h = p.ParcelHolder[i];
            if (h < 0) { none++; continue; }
            var o = w.R.Parcels[i].Owner;
            if (o.Kind == OwnerKind.Demesne) Assert.Equal(SocialClass.Gentry, p.Households.Class[h]);
            if (o.Kind == OwnerKind.Tenant) Assert.Equal(DeedKind.Tenant, p.Households.Deed[h]);
            if (o.Kind == OwnerKind.Freeholder) Assert.Equal(DeedKind.Owned, p.Households.Deed[h]);
            Assert.True(p.Households.ParcelCount[h] > 0);
        }
        Assert.True(none * 50 < w.R.Parcels.Count, $"{none} of {w.R.Parcels.Count} parcels without a holder");
    }

    [Theory, MemberData(nameof(SeedCases))]
    public void Memory_and_time_stay_in_budget(ulong seed)
    {
        var w = Get(seed);
        long perPerson = w.P.ApproxBytes() / w.P.People.Count;
        output.WriteLine($"seed {seed}: {perPerson} bytes/person (array estimate), {w.Bytes / w.P.People.Count} bytes/person (GC delta incl. households, kin), populate {w.PopMs} ms");
        Assert.True(perPerson <= 2048);
        Assert.True(w.Bytes / w.P.People.Count <= 2048);
        Assert.True(w.PopMs < 5000);
    }

    [Fact]
    public void Dwelling_beds_come_from_blueprints_within_ranges()
    {
        var w = Get(1); var p = w.P;
        var byType = new Dictionary<DwellingType, HashSet<int>>();
        for (int si = 0; si < w.R.Settlements.Count; si++)
            foreach (var l in w.R.Settlements[si].Plan.Lots.Where(l => l.IsDwelling))
            {
                if (!byType.TryGetValue(l.Dwelling, out var set)) byType[l.Dwelling] = set = [];
                set.Add(p.LotBeds[si][l.Index]);
            }
        foreach (var (type, set) in byType) output.WriteLine($"{type}: beds {string.Join(',', set.Order())}");
        Assert.All(byType[DwellingType.Cottage], b => Assert.InRange(b, 4, 7));
        Assert.All(byType[DwellingType.Longhouse], b => Assert.InRange(b, 6, 10));
        if (byType.TryGetValue(DwellingType.Burgage, out var bg)) Assert.All(bg, b => Assert.InRange(b, 6, 12));
    }
}
