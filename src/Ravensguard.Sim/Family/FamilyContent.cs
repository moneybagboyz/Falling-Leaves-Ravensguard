using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Family;

/// <summary>
/// The numbers of family life as the sim reads them (family-and-lifespan 3, 4): built from the content database, or the spec's defaults when a world runs without content. The ageing table and the
/// old-age hazard are functions here, so a test and the game read the same rows (FL-9: "the sequence for 40-75 equals the content table").
/// </summary>
public sealed class FamilyContent
{
    public static readonly FamilyContent Default = new(new FamilySettingsDef { Id = "family" });

    public FamilySettingsDef S { get; }

    public FamilyContent(FamilySettingsDef s)
    {
        S = s.AgeLoss.Count == 0 || s.OldAge.Count == 0 ? s with { AgeLoss = s.AgeLoss.Count == 0 ? DefaultLoss() : s.AgeLoss, OldAge = s.OldAge.Count == 0 ? DefaultOldAge() : s.OldAge } : s;
    }

    public static FamilyContent From(ContentDatabase? db) =>
        db == null ? Default : new FamilyContent(db.All<FamilySettingsDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new FamilySettingsDef { Id = "family" });

    private static List<AgeLossRow> DefaultLoss() =>
    [
        new() { Attr = "strength", From = 40, To = 49, Every = 2 }, new() { Attr = "strength", From = 50, To = 59 }, new() { Attr = "strength", From = 60, Amount = 2 },
        new() { Attr = "toughness", From = 45, To = 59 }, new() { Attr = "toughness", From = 60, Amount = 2 },
        new() { Attr = "dexterity", From = 45 }, new() { Attr = "perception", From = 50 }, new() { Attr = "wits", From = 65 },
    ];

    private static List<OldAgeRow> DefaultOldAge() => [new() { From = 0, Pct = 0 }, new() { From = 50, Pct = 1 }, new() { From = 60, Pct = 3 }, new() { From = 65, Pct = 10 }, new() { From = 75, Pct = 20 }];

    public static readonly string[] AttrIds = ["strength", "toughness", "dexterity", "perception", "wits"];

    /// <summary>What attribute <paramref name="attr"/> (0..4, the person table's order) loses on the birthday at which the hero turns <paramref name="age"/> (points).</summary>
    public int LossOnBirthday(int attr, int age)
    {
        int lost = 0;
        foreach (var r in S.AgeLoss)
            if (string.Equals(r.Attr, AttrIds[attr], StringComparison.Ordinal) && age >= r.From && age <= r.To && (age - r.From) % Math.Max(1, r.Every) == 0) lost += r.Amount;
        return lost;
    }

    /// <summary>The total an attribute loses over the birthdays after <paramref name="fromAge"/> up to and including <paramref name="toAge"/>.</summary>
    public int LossBetween(int attr, int fromAge, int toAge)
    {
        int sum = 0;
        for (int a = fromAge + 1; a <= toAge; a++) sum += LossOnBirthday(attr, a);
        return sum;
    }

    /// <summary>The yearly chance (percent) that a hero of this age dies of old age and nothing else.</summary>
    public int OldAgePct(int age)
    {
        int pct = 0;
        foreach (var r in S.OldAge.OrderBy(r => r.From)) if (age >= r.From) pct = r.Pct;
        return pct;
    }

    /// <summary>The dowry share (percent) of the bride's household movables for a class.</summary>
    public int DowryPct(SocialClass c) => c switch { SocialClass.Gentry => S.DowryGentryPct, SocialClass.Burgher => S.DowryBurgherPct, _ => S.DowryPeasantPct };
}

/// <summary>The pure rules of family life: courting's acceptance score, the next holy day, the old-age roll, a child's starting attributes (family-and-lifespan 4.1, 4.4, 4.6). No state.</summary>
public static class FamilyRules
{
    /// <summary>
    /// The acceptance score of 4.1: <c>trust / 10 + 20 - 30 x |class gap| + 10 x sign(wealth ratio - 1) + standing / 20 + jitter</c> (trust and standing in permille; the jitter is drawn by the caller).
    /// <paramref name="wealthSign"/> is +1 when the suitor is the wealthier, -1 when poorer, 0 when equal.
    /// </summary>
    public static int Acceptance(FamilySettingsDef s, int trustPm, int classGap, int wealthSign, int standingPm, int jitter) =>
        trustPm / Math.Max(1, s.TrustDiv) + s.Base - s.ClassPenalty * Math.Abs(classGap) + s.WealthSwing * Math.Sign(wealthSign) + standingPm / Math.Max(1, s.StandingDiv) + jitter;

    /// <summary>The acceptance jitter (stream 0x640, key the courtship and the day): -Jitter..+Jitter.</summary>
    public static int Jitter(FamilySettingsDef s, ulong seed, int courtship, int day, uint draw) =>
        s.Jitter <= 0 ? 0 : DetRng.Range(seed, day, (ulong)courtship, RngStreams.CourtshipRoll, draw, -s.Jitter, s.Jitter + 1);

    /// <summary>The first holy day at least <see cref="FamilySettingsDef.WeddingMinDaysAhead"/> days after <paramref name="today"/>.</summary>
    public static int NextHolyDay(FamilySettingsDef s, int today)
    {
        int every = Math.Max(1, s.HolyDayEvery), d = today + Math.Max(0, s.WeddingMinDaysAhead);
        while (d % every != 0) d++;
        return d;
    }

    /// <summary>Does a hero of <paramref name="age"/> die of old age in game year <paramref name="year"/> (stream 0x644, key the hero's person id and the year)?</summary>
    public static bool DiesOfOldAge(FamilyContent c, ulong seed, ulong hero, int year, int age)
    {
        int pct = c.OldAgePct(age);
        return pct > 0 && DetRng.Range(seed, year, hero, RngStreams.HeroOldAge, 0, 0, 100) < pct;
    }

    /// <summary>A child's attributes (stream 0x641, key the child's person id): each the parents' mean plus or minus the spread.</summary>
    public static byte[] ChildAttributes(FamilySettingsDef s, ulong seed, ulong child, ReadOnlySpan<int> a, ReadOnlySpan<int> b)
    {
        var r = new byte[PersonTable.AttrCount];
        for (int k = 0; k < r.Length; k++)
        {
            int mean = (a[k] + b[k]) / 2, d = s.ChildAttrSpread <= 0 ? 0 : DetRng.Range(seed, 0, child, RngStreams.ChildAttributes, (uint)k, -s.ChildAttrSpread, s.ChildAttrSpread + 1);
            r[k] = (byte)Math.Clamp(mean + d, 1, 100);
        }
        return r;
    }

    /// <summary>The skill points (thousandths) a lesson day adds at skill <paramref name="milliSkill"/> (thousandths): <c>rate x (1 - s / 100) squared</c>, so <c>1 / (1 - s / 100)</c> grows by rate / 100000 a day.</summary>
    public static long LessonGain(long rateMilli, long milliSkill)
    {
        long left = Math.Max(0, 100_000 - milliSkill);
        return (rateMilli * left * left + 5_000_000_000L) / 10_000_000_000L;          // rounded to the nearest thousandth
    }
}
