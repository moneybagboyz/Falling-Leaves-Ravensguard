using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// Slots (battle-bubble-and-formations 4.3): who stands where in a formation, and where each place is. Pure integer functions, no draw. A slot's place is given in the formation's own frame as
/// <c>lat</c> (mm to the left of the facing) and <c>back</c> (mm behind the front rank), and turned to the world by the anchor and the facing (a Q10 unit vector):
/// <c>x = ax + ((-fy x lat - fx x back) &gt;&gt; 10)</c>, <c>y = ay + ((fx x lat - fy x back) &gt;&gt; 10)</c> (an arithmetic shift, so the same on every platform).
/// </summary>
public static class Slots
{
    /// <summary>The role class the slot order sorts by (lowest first): for a line, wall, loose or column shield bearers (0), polearms (1), other foot (2), missile troops (3); for a schiltron polearms (0, outside), others (1), missiles (3).</summary>
    public static int Priority(Fighter f, Formation shape)
    {
        if (f.Ranged) return 3;
        if (shape == Formation.Schiltron) return f.Family == "polearms" ? 0 : 1;
        if (f.Shield != null) return 0;
        return f.Family == "polearms" ? 1 : 2;
    }

    /// <summary>The skill the order sorts by: the launcher's for a missile fighter, else the weapon's.</summary>
    public static int SkillOf(Fighter f) => f.Ranged ? f.ArcherySkill : f.Skill;

    /// <summary>Sorts fighter ids into slot order: role class, then skill descending, then id. A total order, so the result does not depend on the order given.</summary>
    public static void Order(List<Fighter> fighters, int[] ids, Formation shape)
    {
        Array.Sort(ids, (a, b) =>
        {
            var fa = fighters[a]; var fb = fighters[b];
            int c = Priority(fa, shape).CompareTo(Priority(fb, shape));
            if (c != 0) return c;
            c = SkillOf(fb).CompareTo(SkillOf(fa));
            return c != 0 ? c : a.CompareTo(b);
        });
    }

    /// <summary>The files of a rank layout of <paramref name="n"/> men: the shape's fixed number abreast, else n over the ranks, rounded up.</summary>
    public static int Files(int n, int ranks, FormationShapeDef def)
    {
        if (n <= 0) return 1;
        if (def.Abreast > 0) return Math.Min(def.Abreast, n);
        return Math.Max(1, (n + Math.Max(1, ranks) - 1) / Math.Max(1, ranks));
    }

    /// <summary>The places of <paramref name="n"/> slots in the shape's frame (lat to the left, back from the front rank). Rank shapes centre every rank on the anchor; rings are about the anchor.</summary>
    public static void Layout(FormationShapeDef def, int n, int ranks, int[] lat, int[] back)
    {
        if (def.Layout == "schiltron") { Schiltron(def, n, lat, back); return; }
        if (def.Layout == "fall_back") { Rings(def, n, lat, back); return; }
        int files = Files(n, ranks, def);
        for (int s = 0; s < n; s++)
        {
            int rank = s / files, fi = s % files;
            int cnt = Math.Min(files, n - rank * files);
            lat[s] = ((cnt - 1) - 2 * fi) * def.LateralMm / 2;
            back[s] = rank * def.RankMm;
        }
    }

    /// <summary>The slot's world position.</summary>
    public static void ToWorld(int ax, int ay, int fx, int fy, int lat, int back, out int x, out int y)
    {
        x = ax + (int)(((long)-fy * lat - (long)fx * back) >> 10);
        y = ay + (int)(((long)fx * lat - (long)fy * back) >> 10);
    }

    // ---- rings ----

    /// <summary>cos and sin (Q10) of an angle in 1/256 steps of 5.625 degrees (a full turn is 64 x 256), by linear interpolation of the 64-direction table.</summary>
    private static void Trig(int u, out int cos, out int sin)
    {
        int i = (u >> 8) & 63, j = (i + 1) & 63, fr = u & 255;
        cos = (FightMath.Cos64[i] * (256 - fr) + FightMath.Cos64[j] * fr) >> 8;
        sin = (FightMath.Sin64[i] * (256 - fr) + FightMath.Sin64[j] * fr) >> 8;
    }

    private static void RingPlace(int radius, int k, int count, int startU, int[] lat, int[] back, int slot)
    {
        Trig(startU + (int)((long)k * 64 * 256 / Math.Max(1, count)), out int c, out int s);
        lat[slot] = (int)((long)radius * c >> 10);
        back[slot] = (int)(-((long)radius * s) >> 10);     // sine up is the front
    }

    private static int RingCapacity(int radius, int spacing) => Math.Max(1, (int)((long)radius * 6283 / (1000L * Math.Max(1, spacing))));

    private static void Schiltron(FormationShapeDef def, int n, int[] lat, int[] back)
    {
        int outer = (2 * n + 2) / 3, inner = n - outer;
        int r = Math.Max(def.LateralMm, (int)((long)outer * def.LateralMm * 1000 / 6283));
        for (int k = 0; k < outer; k++) RingPlace(r, k, outer, 16 * 256, lat, back, k);
        int ri = Math.Max(0, r - def.RankMm);
        for (int k = 0; k < inner; k++) RingPlace(ri, k, inner, 16 * 256 + 128, lat, back, outer + k);
    }

    private static void Rings(FormationShapeDef def, int n, int[] lat, int[] back)
    {
        int slot = 0, ring = 0;
        while (slot < n)
        {
            int radius = def.RingMm + ring * def.RankMm;
            int cnt = Math.Min(n - slot, RingCapacity(radius, def.LateralMm));
            for (int k = 0; k < cnt; k++) RingPlace(radius, k, cnt, ring * 32, lat, back, slot + k);
            slot += cnt; ring++;
        }
    }
}
