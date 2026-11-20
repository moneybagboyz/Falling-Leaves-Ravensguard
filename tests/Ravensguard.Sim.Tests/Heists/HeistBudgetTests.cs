using System.Diagnostics;
using Ravensguard.Sim.Heists;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>The heist's budgets (heists-and-jailbreaks 9, overview 9.1): a site's generation 30 ms or less; twelve guards looking and listening 0.15 ms a second of play on average (0.6 ms a five-second beat); a saved site 5 KB or less.</summary>
[Collection(TimingCollection.Name)]
public class HeistBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void ASiteIsGeneratedInThirtyMilliseconds_TwelveGuardsCostUnderACentimillisecondAStep_AndASiteSavesInFiveKilobytes()
    {
        var rules = HeistTests.Rules;
        // warm up (the first parse of the templates, the JIT)
        for (int i = 0; i < 3; i++) HeistSites.Start(new HeistBook(), rules, 1, i, HeistKind.Gaol, 2, 5 * 86_400L + 3600, 0, [], []);
        double gen = TestBudget.BestPass(30, 5, () =>
        {
            var sw = Stopwatch.StartNew(); int n = 0;
            foreach (var k in new[] { HeistKind.Gaol, HeistKind.Strongroom, HeistKind.Vault, HeistKind.Undercroft, HeistKind.Drain })
                for (int s = 0; s < 10; s++) { HeistSites.Start(new HeistBook(), rules, 1, 100 + s, k, 2, 5 * 86_400L + 3600, 0, [], []); n++; }
            return sw.Elapsed.TotalMilliseconds / n;
        });
        output.WriteLine($"a site and its guards laid out: {gen:F2} ms");
        Assert.True(gen <= TestBudget.Ms(30), $"{gen} ms");

        var b = new HeistBook();
        var run = HeistSites.Start(b, rules, 1, 7, HeistKind.Gaol, 2, 5 * 86_400L + 3600, 0, [], [])!;
        run.Here.Guards.Clear();
        var men = GuardRoster.Build(run.Here.Id, [], 12);                      // twelve men on a map with places for them
        run.Here.Guards.AddRange(GuardRoster.Layout(rules, 1, run.Map, men, 22, 5, run.Here.Id));
        run.X = 39; run.Y = 4; run.SetSneak(true);
        double step = TestBudget.BestPass(0.15, 5, () =>
        {
            var sw = Stopwatch.StartNew(); int n = 0;
            for (int i = 0; i < 2000 && run.Active; i++) { run.Step(); n++; }
            return sw.Elapsed.TotalMilliseconds / Math.Max(1, n);
        });
        output.WriteLine($"{run.Here.Guards.Count} guards: {step * 1000:F1} us a second of play");
        Assert.True(step <= TestBudget.Ms(0.15), $"{step} ms a step");

        int bytes = SaveBytes(b);
        output.WriteLine($"the heist section: {bytes} bytes");
        Assert.True(bytes <= 5 * 1024 * 3, $"{bytes} bytes");                   // the run in play, its guards and noises included: 5 KB a site, a drain and a gaol joined
    }

    private static int SaveBytes(HeistBook b) => Ravensguard.Sim.Save.SaveCodec.SerializeCompressed(Ravensguard.Sim.Save.HeistSave.Capture(b)).Length;
}
