using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Onboarding;

/// <summary>Where a town's practice yard stands (onboarding spec 3.4): a 14 x 10 m fenced ground just outside the houses, and the guard who trains there.</summary>
public sealed record YardSite(int Settlement, int X, int Y, int TrainerPerson)
{
    public const int WidthM = 14, DepthM = 10;
    public bool Contains(long xMm, long yMm) => xMm >= (X - WidthM / 2) * 1000L && xMm <= (X + WidthM / 2) * 1000L && yMm >= (Y - DepthM / 2) * 1000L && yMm <= (Y + DepthM / 2) * 1000L;
}

/// <summary>The strike of a timing swing, as the bar shows it (the practice dummy and the sparring partner, spec 3.4).</summary>
public enum SwingResult : byte { TooEarly, Good, TooLate }

/// <summary>
/// The practice yard's rules (onboarding spec 3.4). A town (population 1,000 or more) has one yard; a training dummy gives skill for hits that land in the strike window, the gain stops at skill 30 (the dummy says "you have
/// outgrown this"), and the sparring partner fights with blunted weapons to three clean hits: free, d = 60, the same cap, a second bout only after five game minutes. This is the Phase 0 melee spike's model; the full combat
/// model of Phase 4 replaces it (risk R-7). Gains go through <see cref="Progress"/>: the same skill book, the same twelve uses an hour, the same "too easy" margin.
/// </summary>
public static class PracticeYard
{
    public static readonly SkillUse DummyHit = Progress.DummyHit;
    public static readonly SkillUse SparBout = new("spar", Skill.Melee, 60, 30);
    /// <summary>The wind-up and the strike window in seconds for a weapon class (spec 4A.3: 0.30 to 0.90 and 0.10 to 0.15).</summary>
    public static (double Windup, double Window) Timing(string weaponClass) => weaponClass switch
    {
        "dagger" or "knife" => (0.30, 0.15), "sword" => (0.45, 0.13), "axe" or "mace" => (0.60, 0.12), "spear" => (0.50, 0.13), "staff" => (0.55, 0.14), "greatsword" or "polearm" => (0.90, 0.10), _ => (0.45, 0.13),
    };

    /// <summary>Where in the swing a press landed: before the strike window opens, inside it, or after it closed.</summary>
    public static SwingResult Judge(double pressAtS, double windupS, double windowS) => pressAtS < windupS ? SwingResult.TooEarly : pressAtS <= windupS + windowS ? SwingResult.Good : SwingResult.TooLate;

    /// <summary>One hit on a dummy: a good swing is a use of the weapon skill; the others teach by their message only.</summary>
    public static (SwingResult Swing, UseOutcome Outcome, long GainMicro) HitDummy(SkillBook book, EntityId hero, SwingResult swing, long nowSec, int taskDifficulty = 30)
    {
        if (swing != SwingResult.Good) return (swing, UseOutcome.NoSkill, 0);
        var (o, g) = Progress.Use(book, hero, DummyHit, taskDifficulty, nowSec);
        return (swing, o, g);
    }

    /// <summary>The one-line reason shown under the bar.</summary>
    public static string Reason(SwingResult s, UseOutcome o) => s switch { SwingResult.TooEarly => "too early", SwingResult.TooLate => "too late", _ => o == UseOutcome.AtCap ? "outgrown" : "good hit" };

    /// <summary>A sparring bout: the hero's swings against the trainer's, first to three clean hits wins (a yield). <paramref name="heroSwings"/> are the timing results in order; the trainer lands a hit on every swing that is not good. Returns who won and the rounds fought.</summary>
    public static (bool HeroWon, int HeroHits, int TrainerHits, int Rounds) Spar(IEnumerable<SwingResult> heroSwings)
    {
        int h = 0, t = 0, n = 0;
        foreach (var s in heroSwings)
        {
            n++; if (s == SwingResult.Good) h++; else t++;
            if (h >= 3 || t >= 3) break;
        }
        return (h >= 3, h, t, n);
    }

    /// <summary>When a bout may start: the first ever, or five game minutes after the last one began (so it cannot be ground at 8x).</summary>
    public static bool CanSpar(long lastBoutSec, long nowSec, int cooldownMin) => lastBoutSec < 0 || nowSec - lastBoutSec >= cooldownMin * 60L;

    /// <summary>The bout's skill use (once per bout, d = 60, cap 30).</summary>
    public static UseOutcome FinishBout(SkillBook book, EntityId hero, long nowSec) => Progress.Use(book, hero, SparBout, 30, nowSec).Outcome;

    /// <summary>The yard of a settlement, or null when it is too small. Deterministic: the first of eight directions (from a stream 0x371 start) with open, unbuilt, dry ground 30 m beyond the houses.</summary>
    public static YardSite? SiteOf(Population pop, int settlement, ulong seed, int minPopulation = 1000)
    {
        var r = pop.Region; if (settlement < 0 || settlement >= r.Settlements.Count) return null;
        var s = r.Settlements[settlement]; if (s.TargetPop < minPopulation || s.Class != SettlementClass.Town) return null;
        int start = DetRng.Range(seed, 3, (ulong)settlement, RngStreams.OnboardingJob, 9, 0, 8), radius = s.Plan.RadiusM + 30;
        for (int k = 0; k < 8; k++)
        {
            double a = (start + k) * Math.PI / 4;
            int x = (int)(s.CenterX + Math.Cos(a) * radius), y = (int)(s.CenterY + Math.Sin(a) * radius);
            if (x < 20 || y < 20 || x > RegionSettings.RegionM - 20 || y > RegionSettings.RegionM - 20) continue;
            int tx = x / RegionSettings.TileM, ty = y / RegionSettings.TileM, i = RegionData.Index(tx, ty);
            if (r.Water[i] != 0 || r.Built[i] != 0) continue;
            return new YardSite(settlement, x, y, Trainer(pop, settlement));
        }
        return null;
    }

    /// <summary>The settlement's first adult guard by index (the yard's trainer), else -1.</summary>
    public static int Trainer(Population pop, int settlement)
    {
        var p = pop.People;
        for (int i = 0; i < p.Count; i++) if (p.IsAlive(i) && p.HomeSettlement[i] == settlement && p.Occupation[i] == Occupation.Guard && pop.Age(i) >= 18) return i;
        return -1;
    }
}
