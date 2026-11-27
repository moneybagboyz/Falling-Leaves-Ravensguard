using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>
/// P7-AW-B, scale and the library (abstract-war-and-calibration 4.3, 10): AW-7 (the budgets at 100, 200 and 2,000 a side, measured the way every budget test does:
/// <see cref="TestBudget.BestPass"/> in the Timing collection) and AW-8 (the hash-consed template library: size and the same draws). The speed-ups (a Fenwick picker for the target
/// draw, one RNG prefix per man's morale draws, a grid for the sight count, lazy books, the unhurt body read once) change no draw: the pinned hashes of the Phase 4 and Phase 7
/// battles in <see cref="AbstractBattleTests"/> and <see cref="AbstractWarTests"/> are the proof, and the cases below pin the 2,000 v 2,000 battle itself.
/// </summary>
[Collection(TimingCollection.Name)]
public class AbstractScaleTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private WoundTables T => WoundTables.For(fx.Db) ?? throw new InvalidOperationException("no wound tables in content/base/abstract");

    [Fact, Trait("Category", "Soak")]
    public void AW7_TheBudgets_100_200_And_2000_APlayedSide()
    {
        var t = T;
        var def = AbstractBattle.V2Def(fx.Db);                                          // play fights v2 (AbstractBattleService)
        var over = new List<string>();
        foreach (var (n, budgetMs) in new[] { (100, 3), (200, 8), (2000, 45) })
        {
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < 2 || warm.ElapsedMilliseconds < (n >= 2000 ? 4000 : 1500); i++) new AbstractBattle(fx.C, t, AbstractBattleTests.Big(n, (ulong)(100 + i % 8)), def).Run();
            int k = 0;
            double ms = TestBudget.BestPass(budgetMs, 6, () =>
            {
                var input = AbstractBattleTests.Big(n, (ulong)(2 + k++ % 3));
                var sw = Stopwatch.StartNew(); new AbstractBattle(fx.C, t, input, def).Run(); return sw.Elapsed.TotalMilliseconds;
            });
            output.WriteLine($"{n} v {n}: {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
            if (ms > TestBudget.Ms(budgetMs)) over.Add($"{n} v {n} took {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
        }
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    [Fact]
    public void AW7_2000v2000_IsTheSameBattleEveryTime_AsBeforeTheSpeedUps()
    {
        var def = AbstractBattle.V2Def(fx.Db);
        var a = new AbstractBattle(fx.C, T, AbstractBattleTests.Big(2000, 100), def); var ra = a.Run();
        var b = new AbstractBattle(fx.C, T, AbstractBattleTests.Big(2000, 100), def); var rb = b.Run();
        Assert.Equal(ra.Hash, rb.Hash);
        Assert.Equal(a.Commands.Count, b.Commands.Count);
        Assert.NotEqual(ra.Hash, new AbstractBattle(fx.C, T, AbstractBattleTests.Big(2000, 101), def).Run().Hash);
        Assert.True(ra.Sides[0].Start == 2000 && ra.Sides[1].Start == 2000);
        Assert.True(ra.Winner >= 0 && ra.DurationS > 0 && ra.DurationS < def.MaxSteps * def.StepS);
        // measured on the resolver as it was before the P7-AW-B speed-ups: every draw and every order unchanged since (re-pin only when the battle changes on purpose)
        // re-pinned in P8-WT-2: the v2 row's constants were refitted for E and G (the v1 hash below did not move)
        Assert.Equal(0xCFA9A6003646EFCAUL, ra.Hash);
        Assert.Equal(0x09FC8C781816BF17UL, new AbstractBattle(fx.C, T, AbstractBattleTests.Big(2000, 100)).Run().Hash);   // re-pinned P8-WT-3b (v1 cavalry rows refitted after the kick gate); was 0x275D1E758DFDA8F1       // v1, the Phase 4 row
        output.WriteLine($"2000 v 2000 seed 100: hash {ra.Hash:X16}, winner {ra.Winner}, {ra.DurationS} s, casualties {ra.Sides[0].Casualties}/{ra.Sides[1].Casualties}");
    }

    [Fact]
    public void AW7_TheSightCount_EqualsTheBruteForce()
    {
        var rng = new Random(77);
        foreach (var (n, spread, radius) in new[] { (60, 4000, 10_000L), (400, 30_000, 10_000L), (300, 200_000, 10_000L), (50, 5_000_000, 10_000L), (200, 9_000, 0L), (200, 9_000, 3L), (1, 100, 10_000L), (0, 100, 10_000L) })
        {
            var xs = new long[n]; var ys = new long[n];
            for (int i = 0; i < n; i++) { xs[i] = rng.Next(-spread, spread); ys[i] = rng.Next(-spread, spread); }
            if (n > 20) { xs[5] = xs[6]; ys[5] = ys[6]; }                              // two men on one spot
            long brute = 0, r2 = radius * radius;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) if (i != j && (xs[i] - xs[j]) * (xs[i] - xs[j]) + (ys[i] - ys[j]) * (ys[i] - ys[j]) <= r2) brute++;
            Assert.Equal(brute, AbstractBattle.NearPairs(xs, ys, radius));
        }
    }

    // ------------------------------------------------------------------ AW-8: the template library

    /// <summary>A hash of every template the library hands out: for each weapon, armour and outcome class, what <see cref="WoundTables.Templates"/> returns (fallbacks to the nearest group included), wound by wound.</summary>
    private static ulong LibraryFingerprint(WoundTables t)
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        foreach (var w in Enum.GetValues<WeaponClass>())
            foreach (var a in Enum.GetValues<ArmourClass>())
                foreach (var o in Enum.GetValues<OutcomeClass>())
                {
                    var l = t.Templates(w, a, o);
                    Mix(-1); Mix(l.Length);
                    foreach (var tpl in l)
                    {
                        Mix(tpl.SkullMilli); Mix(tpl.Wounds.Length);
                        foreach (var x in tpl.Wounds) { Mix(x.Part); Mix(x.Tissue); Mix(x.Structure); Mix(x.Organ); Mix((long)x.Kind); Mix(x.DepthPm); Mix(x.Grade); Mix(x.Count); Mix(x.Residual); Mix(x.BleedX10); Mix((long)x.Flags); }
                    }
                }
        return h;
    }

    // measured on the library as it shipped before hash-consing (wound_templates.json, 5,504 templates, 910 KB): the same draws
    private const ulong PinnedLibrary = 0x4C301EA952131FAA;
    private const int PinnedTemplates = 5504;

    [Fact]
    public void AW8_TheLibrary_IsHashConsed_Small_AndHandsOutTheSameTemplates()
    {
        var t = T;
        Assert.Equal(PinnedTemplates, t.TemplateCount);
        Assert.Equal(PinnedLibrary, LibraryFingerprint(t));
        Assert.Empty(fx.Db.All<WoundTemplatesDef>());                                   // the old per-class rows are gone: the library is the one hash-consed row
        var lib = Assert.Single(fx.Db.All<WoundLibraryDef>());
        Assert.True(lib.Wounds.Count < 2500, $"{lib.Wounds.Count} distinct wound records");          // 29,645 stored before
        Assert.True(lib.Templates.Count < t.TemplateCount, "templates are stored once");
        var seen = new HashSet<WoundTemplate>(ReferenceEqualityComparer.Instance);                  // identical templates are one object, wherever they are drawn from
        int refs = 0;
        foreach (var w in Enum.GetValues<WeaponClass>()) foreach (var a in Enum.GetValues<ArmourClass>()) foreach (var o in Enum.GetValues<OutcomeClass>()) foreach (var x in t.Templates(w, a, o)) { refs++; seen.Add(x); }
        Assert.True(seen.Count < refs, $"{seen.Count} objects for {refs} draws");
        long bytes = new FileInfo(FindContent("abstract", "wound_library.json")).Length;
        output.WriteLine($"wound_library.json {bytes / 1024} KB; {lib.Wounds.Count} wounds, {lib.Templates.Count} templates, {t.TemplateCount} draws' worth");
        Assert.True(bytes <= 300 * 1024, $"{bytes} bytes");                              // from 0.9 MB
    }

    [Fact]
    public void AW8_TheBuilder_WritesTheLibraryTheLoaderReads()
    {
        // two sets sharing a template and a wound record: stored once each, in the order they are first used, as the same draws
        long[] w1 = [1, 29, -1, -1, 0, 1000, 0, 0, 0, 5, 0], w2 = [4, 11, -1, -1, 1, 600, 2, 0, 0, 30, 0];
        List<long> T1() => [.. new long[] { 2 }.Concat(w1).Concat(w2).Append(88_000)];
        List<long> T2() => [.. new long[] { 1 }.Concat(w2).Append(5_000)];
        string Make() => WoundTableBuilder.Library([("blade.none", [("light", [T1(), T2()]), ("serious", [T1()])]), ("axe.none", [("light", [T2(), T2(), T1()])])]);
        Assert.Equal(Make(), Make());
        var body = string.Join("\n", Make().Split('\n').Where(l => !l.StartsWith("//")));
        Assert.Contains("\"blade.none\": { \"light\": [0,1], \"serious\": [0] }", body);
        Assert.Contains("\"axe.none\": { \"light\": [1,1,0] }", body);
        Assert.Contains("[88000,0,1]", body);
        Assert.Contains("[5000,1]", body);
    }

    private static string FindContent(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine([d.FullName, "content", "base", .. parts]);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }
}
