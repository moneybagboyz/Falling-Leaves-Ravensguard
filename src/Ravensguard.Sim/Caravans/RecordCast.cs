using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Caravans;

/// <summary>
/// Who plays an encounter's groups, from the world's records (Phase 3 cleanup B, item 1): the crew of a caravan that is on the road near the point plays <c>caravan_crew</c>, and the vagrants of the nearest
/// settlements play the destitute (<c>refugee</c>, <c>child</c>, <c>drunk</c>). Everything else is a generated traveller (<see cref="EncounterPlacement.Cast"/>). Pure reads of the published caravan
/// snapshot and the population; the same arguments always pick the same people, nearest first, ties by id.
/// </summary>
public sealed class RecordCast(Func<IReadOnlyList<Caravan>> caravans, Func<Population> population, long originXMm, long originYMm, int reachM = 1500, int vagrantReachM = 8000) : IEncounterCast
{
    public static readonly string[] VagrantArchetypes = ["refugee", "child", "drunk"];

    /// <summary>The caravan whose crew includes <paramref name="person"/> (or whose master is), if it is on the road.</summary>
    public Caravan? CaravanOf(EntityId person)
    {
        var pop = population(); int row = pop.People.IndexOf(person); if (row < 0) return null;
        foreach (var c in caravans()) if (c.Master == row || Array.IndexOf(c.Crew, row) >= 0) return c;
        return null;
    }

    /// <summary>The nearest caravan within <see cref="reachM"/> of the point, or null.</summary>
    public Caravan? NearestCaravan(long sec, long xMm, long yMm)
    {
        Caravan? best = null; long bd = long.MaxValue;
        foreach (var c in caravans())
        {
            if (!c.Active || c.Crew.Length == 0) continue;
            var (cx, cy, _, _) = CaravanMotion.PositionOf(c, sec);
            long d = Dist(cx - originXMm, cy - originYMm, xMm, yMm);
            if (d <= reachM * 1000L && (d < bd || d == bd && best != null && c.Id.CompareTo(best.Id) < 0)) { best = c; bd = d; }
        }
        return best;
    }

    public IReadOnlyList<EntityId> Existing(string archetype, int count, long sec, long xMm, long yMm)
    {
        var ids = new List<EntityId>();
        var pop = population(); var pt = pop.People;
        if (archetype == "caravan_crew")
        {
            var c = NearestCaravan(sec, xMm, yMm); if (c == null) return ids;
            if (c.Master >= 0 && c.Master < pt.Count && pt.IsAlive(c.Master)) ids.Add(pt.IdOf(c.Master));
            foreach (int row in c.Crew) { if (ids.Count >= count) break; if (row >= 0 && row < pt.Count && pt.IsAlive(row)) { var id = pt.IdOf(row); if (!ids.Contains(id)) ids.Add(id); } }
            return ids;
        }
        if (Array.IndexOf(VagrantArchetypes, archetype) < 0) return ids;
        var region = pop.Region; var near = new List<(long D, int Row)>();
        var settlements = region.Settlements;
        for (int i = 0; i < pt.Count; i++)
        {
            if (!pt.IsAlive(i) || pt.Homeless[i] != HomelessReason.Vagrant) continue;
            if (archetype == "child" != (pt.AgeYears(i, (int)(sec / TimeConst.SecondsPerDay)) < 14)) continue;
            int s = pt.HomeSettlement[i]; if (s < 0 || s >= settlements.Count) continue;
            long d = Dist(settlements[s].CenterX * 1000L, settlements[s].CenterY * 1000L, xMm, yMm);
            if (d <= vagrantReachM * 1000L) near.Add((d, i));
        }
        near.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.Row.CompareTo(b.Row));
        foreach (var (_, row) in near) { if (ids.Count >= count) break; ids.Add(pt.IdOf(row)); }
        return ids;
    }

    private static long Dist(long ax, long ay, long bx, long by) => RoutePath.Dist(ax - bx, ay - by);
}
