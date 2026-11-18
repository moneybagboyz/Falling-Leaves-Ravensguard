using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Travel;

public enum CarcassState : byte { Fresh, Old, Gone }

/// <summary>What a butchering will give and cost, before it is done (the Q menu shows it).</summary>
public sealed record ButcherPlan(CarcassState State, string Why, int YieldPm, long Seconds, (string Tissue, string Good, long Grams)[] Yields, int QualityTierDelta, int SpoilMult, int SkillUse);

/// <summary>What butchering did: goods made, the unrecovered rest (gut, blood, trimmings) and the check that the two add up to the carcass.</summary>
public sealed record ButcherResult(ButcherPlan Plan, (string Tissue, string Good, long Grams)[] Made, long WasteG, long CarcassG)
{
    public long MadeG => Made.Sum(m => m.Grams);
}

public sealed partial class TravelState
{
    public GameSpeciesDef? Species(string id) => Content.All<GameSpeciesDef>().FirstOrDefault(d => d.Id == id);

    /// <summary>Yield in permille: <c>min(1000, 700 + 6 x Butchery)</c>.</summary>
    public int ButcherYieldPm(int skill) => Math.Min(1000, Cfg.ButcherYieldBasePm + Cfg.ButcherYieldPerSkillPm * skill);

    /// <summary>The hours a carcass is butcherable: fresh for 24 (12 on a hot day), old (a tier worse, spoiling twice as fast) until 72, then it is a skeleton.</summary>
    public CarcassState StateOf(Carcass c, long sec, int tempC)
    {
        long hours = (sec - c.DeathSec) / TimeConst.SecondsPerHour;
        int fresh = tempC > Cfg.ButcherHotC ? Cfg.ButcherFreshHours / 2 : Cfg.ButcherFreshHours;
        return hours < fresh ? CarcassState.Fresh : hours < Cfg.ButcherMaxHours ? CarcassState.Old : CarcassState.Gone;
    }

    /// <summary>Game seconds to butcher: <c>(2.5 + 0.015 x kg) hours x (1 - (skill - 50) / 200)</c>, a hung carcass -25%, two people halve it.</summary>
    public long ButcherSeconds(long massG, int skill, bool hung, int people)
    {
        long kg = massG / 1000;
        long s = (Cfg.ButcherBaseSeconds + (long)Cfg.ButcherSecondsPerKg * kg) * (200 - (skill - 50)) / 200;
        if (hung) s = s * (100 - Cfg.ButcherHungPct) / 100;
        if (people >= 2) s /= 2;
        return Math.Max(60, s);
    }

    /// <summary>Nominal grams of a tissue: <c>mass x share x recover x yield / 1e9</c>, floor.</summary>
    public static long TissueGrams(long massG, int sharePm, int recoverPm, int yieldPm) => massG * sharePm * recoverPm / 1000 * yieldPm / 1_000_000;

    public ButcherPlan PlanButchery(Carcass c, int skill, long sec, int tempC = 10, int people = 1, bool tabooCulture = false)
    {
        var sp = Species(c.Species);
        if (sp == null || sp.Butcher.Count == 0) return new(CarcassState.Gone, "Nothing to butcher", 0, 0, [], 0, 1, 0);
        if (sp.Taboo && tabooCulture) return new(CarcassState.Gone, "Taboo", 0, 0, [], 0, 1, 0);
        var st = StateOf(c, sec, tempC);
        if (st == CarcassState.Gone) return new(st, "Too old", 0, 0, [], 0, 1, 0);
        int y = ButcherYieldPm(skill);
        var yields = sp.Butcher.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value.Good, TissueGrams(c.MassG, kv.Value.SharePm, kv.Value.RecoverPm, y))).ToArray();
        long secs = ButcherSeconds(c.MassG, skill, c.Hung, people);
        return new(st, "", y, secs, yields, st == CarcassState.Old ? -1 : 0, st == CarcassState.Old ? 2 : 1, 70 * (int)((secs + 1799) / 1800));
    }

    /// <summary>
    /// Butchers the carcass into <paramref name="into"/> (goods with mass, created as <see cref="CreateReason.Hunted"/>). Each tissue varies plus or minus 4% (stream 0x332 keyed on the carcass), then the total is
    /// capped so it never exceeds the carcass; what is not made is waste, so made plus waste is exactly the carcass. The carcass is removed.
    /// </summary>
    public ButcherResult? Butcher(Carcass c, Store into, int skill, long sec, int tempC = 10, int people = 1, bool vary = true)
    {
        if (Goods == null) return null;
        var plan = PlanButchery(c, skill, sec, tempC, people);
        if (plan.State == CarcassState.Gone) return null;
        var made = new List<(string, string, long)>(); long total = 0; uint ti = 0;
        foreach (var (tissue, good, nominal) in plan.Yields)
        {
            long g = nominal;
            if (vary) { int v = DetRng.Range(Seed, 0, c.Id.Value, RngStreams.ButcheryVariance, ti, -Cfg.ButcherVariancePm, Cfg.ButcherVariancePm + 1); g = g * (1000 + v) / 1000; }
            ti++;
            g = Math.Min(g, c.MassG - total); if (g <= 0) continue;
            ushort gid = Goods.IdOf(good);
            long done = Math.Min(g, Math.Max(0, into.Room(gid)));
            if (done > 0 && into.Create(gid, done, CreateReason.Hunted)) { made.Add((tissue, good, done)); total += done; }
        }
        Carcasses.Remove(c);
        return new ButcherResult(plan, [.. made], c.MassG - total, c.MassG);
    }
}
