using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production;

/// <summary>One station's crew as the Crew view shows it (economies-of-scale 9): the people by role, the grade hours of the batch in worker-minutes, the elapsed minutes at this crew, and the crowding (0 under the limit, 1 to twice it, 2 beyond).</summary>
public sealed record CrewStationView(int Index, string Kind, string Recipe, int Masters, int Journeymen, int Hands, long MinutesM, long MinutesJ, long MinutesA, long ElapsedMinutes, int Crowd, int CrewMax);

/// <summary>A staff member the owner may appoint foreman.</summary>
public sealed record ForemanView(int Person, string Name, int Leadership, bool Appointed);

/// <summary>
/// The business panel's Crew section (P7-ES1, economies-of-scale 9): one row per station at work, the span of control (the overseers' summed span, the crew, the excess and the speed it costs), the foremen and who could
/// be, and the guild's verdict on the shop (the caps' excess and the licence). A pure read of the hour's crews; never saved.
/// </summary>
public sealed record CrewView(IReadOnlyList<CrewStationView> Stations, int Workers, int SpanSum, int Excess, int SpeedPm, IReadOnlyList<ForemanView> Foremen, string Guild, int OverCaps, bool Licensed, int Fines);

public static class CrewViews
{
    public static CrewView Of(ProductionSystem prod, ScaleBook? book, ContentDatabase db, Business b, int day)
    {
        var pt = prod.Pop.People; var sc = prod.Content.Scale; var rows = new List<CrewStationView>();
        var seen = new HashSet<int>(); int span = 0;
        int hall = prod.SpanBonus?.Invoke(b) ?? 0;
        void Overseer(int p) { if (seen.Add(p)) span += Crews.Span(sc, pt.SkillLevelOf(p, Skill.Leadership)) + hall; }
        if (b.Owner >= 0) Overseer(b.Owner);
        var crew = new HashSet<int>();
        for (int i = 0; i < b.Stations.Count; i++)
        {
            var st = b.Stations[i]; if (!st.Busy) continue;
            var r = prod.Content.Recipes.Recipes[st.Recipe]; int m = 0, j = 0, h = 0;
            foreach (int p in st.Crew)
            {
                crew.Add(p);
                var role = prod.RoleAt(b, p, r.Skill == Skill.None ? 40 : pt.SkillLevelOf(p, r.Skill), day);
                if (role == CrewRole.Master) { m++; Overseer(p); } else if (role == CrewRole.Journeyman) j++; else h++;
            }
            long total = (long)r.WorkerHours * 60 * st.ScalePermille / 1000, g = r.Grades.M, gj = r.Grades.J;
            long hM = total * g / 1000, hJ = total * gj / 1000, hA = total - hM - hJ;
            int k = prod.Content.CrewMax(st.Kind);
            long t = st.Crew.Count == 0 ? 0 : Crews.ElapsedMinutes(hM, hJ, hA, m, j, h, sc.UnsupervisedPm);
            rows.Add(new CrewStationView(i, st.Kind, r.Id, m, j, h, hM, hJ, hA, t, st.Crew.Count <= k ? 0 : st.Crew.Count <= 2 * k ? 1 : 2, k));
        }
        var foremen = new List<ForemanView>();
        for (int s = 0; s < b.Staff.Count; s++)
        {
            int p = b.Staff[s]; bool on = book?.IsForeman(b.Id.Value, p) ?? false; int ld = pt.SkillLevelOf(p, Skill.Leadership);
            if (on) Overseer(p);
            if (on || ld >= sc.ForemanMinLeadership) foremen.Add(new ForemanView(p, pt.FullName(p), ld, on));
        }
        int workers = crew.Count, excess = Math.Max(0, workers - span);
        string guild = ""; int over = 0;
        foreach (var shop in Limits.Shops(prod, db, day)) if (shop.Business == b) { guild = shop.Guild.Id; over = shop.Over.Total; break; }
        return new CrewView(rows, workers, span, excess, workers > sc.SpanBase ? Crews.SpeedPm(sc, workers, span) : 1000, foremen, guild, over, book?.LicenceOf(b.Id.Value) != null, book?.FinesOf(b.Id.Value) ?? 0);
    }
}
