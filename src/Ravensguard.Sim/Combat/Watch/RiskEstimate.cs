using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>
/// The odds of a fight (watch-mode 3.3; shared by the HUD, the encounter panel, autopilot's <c>MaxRiskPm</c> and the abstract stub): a power rating per fighter from a few integer
/// weights, summed per side within 25 m of the person asking (weighted by distance), and the Lanchester form <c>OddsPm = 1000 Pa^k / (Pa^k + Pe^k)</c> with k = 4.
/// <para><c>Power = blood x pain x skill x weapon x shield x armour x size</c> (permille factors): skill <c>300 + 14 x skill</c>, weapon by kind (a real weapon 1000, a bow 950, an
/// improvised object 650, fists 450, jaws and claws by the beast), a shield +15%, armour <c>1000 + 1.6 x ProtectionPm</c> (none 0, plate 1000: the area-weighted cover of the
/// worn layers by hardness), size by the body plan. The side powers go through the square law sharpened to the fourth power (the fit of WM-11, which holds
/// the estimate to sampled outcomes within 15 points).</para>
/// Integer only; no draw.
/// </summary>
public static class RiskEstimate
{
    public const int RadiusMm = 25_000, FullWeightMm = 5_000;

    /// <summary>The area-weighted protection of what a fighter wears (none 0, a full plate harness about 1000).</summary>
    public static int ProtectionPm(BodyPlanV2 plan, IReadOnlyList<WornV2> worn)
    {
        long sum = 0, area = 0;
        foreach (var p in plan.Parts)
        {
            if (p.IsHosted || p.Shell) continue;
            long prot = 0;
            foreach (var w in worn)
            {
                var a = w.Stats;
                int cov = a.CoverageByPlan[plan.Index][p.BaseIndex];
                if (cov <= 0) continue;
                int hard = a.Layer == ArmourLayer.Armour ? a.Material.Category == "metal" ? a.Elastic ? 650 : 1000 : 450 : a.Layer == ArmourLayer.Over ? 280 : 60;
                prot += (long)cov * hard / 1000;
            }
            sum += Math.Min(1000, prot) * p.RelPpm; area += p.RelPpm;
        }
        return area == 0 ? 0 : (int)(sum / area);
    }

    /// <summary>One fighter's fighting power now (0 when out of the fight).</summary>
    public static long Power(Fighter f)
    {
        if (!f.Active || f.Routing) return 0;
        var b = f.Book;
        long p = 1000;
        p = p * Math.Max(0, 1000 - b.LossPm * 2) / 1000;                                                       // weak long before the collapse
        int painPm = (int)Math.Min(1000, b.PainX100 * 1000L / Math.Max(1, b.KnockoutPainX100));
        p = p * (1000 - painPm / 2) / 1000;
        int skill = f.Ranged ? f.ArcherySkill : f.Skill;
        p = p * (300 + 14 * Math.Clamp(skill, 0, 100)) / 1000;
        string fam = f.Weapon.Shape.Family;
        int weapon = f.Ranged ? (f.Loaded || f.Ammo > 0 ? 950 : 450) : f.Improvised != null ? 650 : fam == "unarmed" ? 450 : fam == "natural" ? 900 : fam == "practice" ? 700 : 1000;
        if (!f.WeaponArm && fam != "natural") weapon = weapon * 300 / 1000;
        p = p * weapon / 1000;
        if (f.Shield != null && f.ShieldArm) p = p * 1150 / 1000;
        p = p * (1000 + 16 * ProtectionPm(f.Plan, f.Worn) / 10) / 1000;
        int size = f.Kit.SizeCm3 <= 0 ? 70000 : f.Kit.SizeCm3;
        p = p * Math.Clamp(400 + 600L * size / 70000, 400, 2400) / 1000;
        p = p * (500 + f.WalkPm / 2) / 1000;                                                                     // a lame fighter still swings
        return Math.Max(1, p);
    }

    /// <summary>The weight of a fighter at <paramref name="distMm"/> from the one asking: full within 5 m, falling to nothing at 25 m.</summary>
    public static int WeightPm(long distMm) => distMm <= FullWeightMm ? 1000 : distMm >= RadiusMm ? 0 : (int)(1000 - (distMm - FullWeightMm) * 1000 / (RadiusMm - FullWeightMm));

    /// <summary>The square law on two side powers, permille for the first.</summary>
    public static int Square(long pa, long pe)
    {
        if (pa <= 0 && pe <= 0) return 500;
        if (pe <= 0) return 1000;
        if (pa <= 0) return 0;
        // the square law, sharpened to the fourth power by WM-11's fit (live fights are more decided than the square law alone says); scaled down first so it stays in 64 bits
        while (pa > 30_000 || pe > 30_000) { pa >>= 1; pe >>= 1; }
        long a2 = pa * pa, e2 = pe * pe, a4 = a2 * a2, e4 = e2 * e2;
        while (a4 > long.MaxValue / 2000 || e4 > long.MaxValue / 2000) { a4 >>= 2; e4 >>= 2; }
        return (int)(1000 * a4 / Math.Max(1, a4 + e4));
    }

    /// <summary>The odds of <paramref name="f"/>'s side in the live fight, from where it stands, and how many allies (with it) and enemies count.</summary>
    public static (int OddsPm, int Allies, int Enemies) Odds(FightWorld w, Fighter f)
    {
        long pa = 0, pe = 0; int na = 0, ne = 0;
        foreach (var o in w.Fighters)
        {
            long pw = Power(o);
            if (pw <= 0) continue;
            int wt = o.Id == f.Id ? 1000 : WeightPm(w.DistMm(f, o));
            if (wt <= 0) continue;
            if (o.Team == f.Team) { pa += pw * wt / 1000; na++; } else { pe += pw * wt / 1000; ne++; }
        }
        return (Square(pa, pe), na, ne);
    }

    /// <summary>The odds of team <paramref name="team"/> over everyone else in a fight about to start (all of it counts: the encounter panel).</summary>
    public static int OddsOf(FightWorld w, int team)
    {
        long pa = 0, pe = 0;
        foreach (var o in w.Fighters) { long pw = Power(o); if (o.Team == team) pa += pw; else pe += pw; }
        return Square(pa, pe);
    }

    /// <summary>The odds of the player's side of a setup before it is fought: the setup is built (not stepped) and rated, so the panel shows what the fight would be.</summary>
    public static int OddsOf(FightSetup s, FightContent c, int team = 0) => OddsOf(s.Build(c, null), team);

    /// <summary>The word the encounter panel shows: "probably win" (65% or more), "even" (35-65%), "probably lose" (under 35%), "hopeless" (under 10%).</summary>
    public static string Word(int oddsPm) => oddsPm >= 650 ? "wm_odds_win" : oddsPm >= 350 ? "wm_odds_even" : oddsPm >= 100 ? "wm_odds_lose" : "wm_odds_hopeless";

    /// <summary>Autopilot's rule (3.3): with <c>Attacked</c> on Ai the hero fights when the odds are at least <c>1000 - MaxRiskPm</c>, else flees.</summary>
    public static bool AutopilotFights(int oddsPm, int maxRiskPm) => oddsPm >= 1000 - maxRiskPm;
}
