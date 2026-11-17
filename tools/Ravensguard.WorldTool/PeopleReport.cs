using System.Diagnostics;
using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.WorldTool;

/// <summary>`region --people`: populates the region (stage 3 part B) and prints statistics, sample households and a family tree.</summary>
public static class PeopleReport
{
    public static void Run(RegionData region, WorldHistory history, string? baseDir)
    {
        var load = new ModLoader(ContentTypeRegistry.Default).Load([baseDir!]);
        if (!load.Success) { Console.Error.WriteLine(load.Report.ToString()); return; }
        Populator.Populate(region, history, load.Database); // warm-up so the timing is the steady state
        var sw = Stopwatch.StartNew();
        var pop = Populator.Populate(region, history, load.Database);
        sw.Stop();
        var t = pop.People; var hh = pop.Households;
        int n = t.Count;
        int kids = 0, adults = 0, elders = 0, homeless = 0;
        for (int i = 0; i < n; i++) { int a = pop.Age(i); if (a < 15) kids++; else if (a < 50) adults++; else elders++; if (t.Bed[i] < 0) homeless++; }
        Console.WriteLine($"people: {n:N0} persons (region target {region.TotalPopulation:N0}), {hh.Count:N0} households, populate {sw.ElapsedMilliseconds} ms, hash {pop.ComputeHash()[..16]}");
        Console.WriteLine($"  ~{pop.ApproxBytes() / n} bytes/person (struct-of-arrays + names), ages 0-14 {kids * 100.0 / n:0.0}% 15-49 {adults * 100.0 / n:0.0}% 50+ {elders * 100.0 / n:0.0}%, homeless {homeless}");
        foreach (var cls in new[] { SettlementClass.Town, SettlementClass.Village, SettlementClass.Farmstead })
        {
            int p = 0, h = 0;
            for (int i = 0; i < hh.Count; i++) if (region.Settlements[hh.Settlement[i]].Class == cls) { h++; p += hh.MemberCount[i]; }
            Console.WriteLine($"  {cls}: {h} households, {p} people, {p / (double)Math.Max(1, h):0.00} per household");
        }
        Console.WriteLine("  class (people): " + string.Join(' ', Enum.GetValues<SocialClass>().Select(c => $"{c}={Enumerable.Range(0, n).Count(i => hh.Class[t.Household[i]] == c)}")) +
                          $"; gentry households {Enumerable.Range(0, hh.Count).Count(h => hh.Class[h] == SocialClass.Gentry)}");
        Console.WriteLine("  occupations: " + string.Join(' ', Enum.GetValues<Occupation>().Select(o => (o, c: Enumerable.Range(0, n).Count(i => t.Occupation[i] == o))).Where(x => x.c > 0 && x.o != Occupation.None).OrderByDescending(x => x.c).Select(x => $"{x.o}={x.c}")));
        int dyn = 0, notable = 0;
        for (int f = 0; f < region.Fiefs.Count; f++) { int l = pop.FiefLord[f]; if (l < 0) continue; if (t.Dynasty[l].Value != 0) dyn++; if (t.Notable[l].Value != 0) notable++; }
        Console.WriteLine($"  lords: {region.Fiefs.Count} fiefs, {dyn} of dynasty houses, {notable} historic notables");

        // Sample households: a lord's hall, a town house, a village house, a farmstead.
        var samples = new List<int>();
        int lordHh = pop.FiefLordHousehold.FirstOrDefault(h => h >= 0, -1);
        if (lordHh >= 0) samples.Add(lordHh);
        foreach (var cls in new[] { SettlementClass.Town, SettlementClass.Village, SettlementClass.Farmstead })
        {
            int h = Enumerable.Range(0, hh.Count).Where(i => hh.Class[i] != SocialClass.Gentry && hh.Dwelling[i] != DwellingType.None && region.Settlements[hh.Settlement[i]].Class == cls && hh.MemberCount[i] >= 4)
                .Skip(3).FirstOrDefault(-1);
            if (h >= 0) samples.Add(h);
        }
        foreach (int h in samples) PrintHousehold(pop, h);

        // A family tree around the first lord with children.
        for (int f = 0; f < region.Fiefs.Count; f++)
        {
            int lord = pop.FiefLord[f];
            if (lord < 0 || pop.Kin.Children(lord).Length == 0) continue;
            Console.WriteLine($"  family tree of {Name(pop, lord)} of {region.Fiefs[f].Name}:");
            Tree(pop, lord, 2, new HashSet<int>());
            Console.WriteLine("    heirs: " + string.Join(" > ", pop.Kin.HeirOrder(lord).Select(i => Name(pop, i))));
            break;
        }
        // A peasant family with grandparents.
        for (int i = 0; i < n; i++)
        {
            int m = t.Mother[i];
            if (m < 0 || t.Mother[m] < 0 || hh.Class[t.Household[i]] != SocialClass.Peasant) continue;
            int g = t.Mother[m];
            Console.WriteLine($"  three generations of {Name(pop, g)}:");
            Tree(pop, g, 2, new HashSet<int>());
            break;
        }
    }

    private static string Name(Population p, int i) => $"{p.People.FullName(i)} ({(p.People.Sex[i] == Sex.Male ? 'm' : 'f')}{p.Age(i)})";

    private static void Tree(Population p, int i, int indent, HashSet<int> seen)
    {
        if (!seen.Add(i)) return;
        var t = p.People;
        var sb = new StringBuilder(new string(' ', indent * 2)).Append(Name(p, i));
        if (t.Spouse[i] >= 0) { sb.Append(" m. ").Append(Name(p, t.Spouse[i])); seen.Add(t.Spouse[i]); }
        if (t.Occupation[i] != Occupation.None) sb.Append(" - ").Append(t.Occupation[i]);
        Console.WriteLine(sb);
        foreach (int c in p.Kin.Children(i)) Tree(p, c, indent + 1, seen);
    }

    private static void PrintHousehold(Population p, int h)
    {
        var hh = p.Households; var t = p.People; var reg = p.Region;
        var s = reg.Settlements[hh.Settlement[h]];
        Console.WriteLine($"  household {hh.IdOf(h)} '{hh.Name[h]}' in {s.Name} ({s.Class}), lot {hh.Lot[h]} {hh.Dwelling[h]} ({hh.Beds[h]} beds), {hh.Class[h]}, deed {hh.Deed[h]}, {hh.ParcelCount[h]} parcels, purse {hh.Purse[h]}");
        foreach (int m in hh.Members(h))
        {
            string rel = t.Spouse[m] >= 0 ? $", spouse of {t.Given[t.Spouse[m]]}" : "";
            string par = t.Father[m] >= 0 || t.Mother[m] >= 0 ? $", child of {(t.Father[m] >= 0 ? t.Given[t.Father[m]] : "?")} & {(t.Mother[m] >= 0 ? t.Given[t.Mother[m]] : "?")}" : "";
            string job = t.Occupation[m] == Occupation.None ? "" : $", {t.Occupation[m]}{(t.Employer[m] >= 0 ? " for " + t.Given[t.Employer[m]] : "")}";
            string sk = string.Join('/', Enumerable.Range(0, PersonTable.SkillSlots).Select(k => t.SkillAt(m, k)).Where(x => x.Skill != Skill.None).Take(2).Select(x => $"{x.Skill} {x.Level}"));
            Console.WriteLine($"    bed {t.Bed[m]}: {Name(p, m)}{rel}{par}{job}; S{t.Attribute(m, 0)} T{t.Attribute(m, 1)} D{t.Attribute(m, 2)} P{t.Attribute(m, 3)} W{t.Attribute(m, 4)} {sk}");
        }
    }
}
