// tools/calibrate --abstract wartune (P8-WT-2): coordinate descent over v2's integer constants against the stored live tallies of E, F, G (tests/.../TacticalReference.cs WarTallies).
//   ... --abstract wartune [--seeds 200] [--rounds 3] [--params WrapRatePm,WrapFlankPm,...] [--weight E=3,F=1,G=2]
using System.Reflection;
using Ravensguard.Sim.Combat.Melee2;
using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using W = Ravensguard.Sim.Combat.Abstract.AbstractWarScenarios;

static class WarTune
{
    static int Arg(string[] a, string n, int d) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var v) ? v : d; }
    static string? Str(string[] a, string n) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

    static double Soft(double e) => e <= 1 ? e * e * 0.5 : 0.5 + Math.Min(e - 1, 2);

    static double Err(AbstractEquivalence.Tally tac, AbstractEquivalence.Tally abs)
    {
        double l = 0;
        for (int s = 0; s < 2; s++)
        {
            long a = abs.Mean(abs.Casualties[s]), t = tac.Mean(tac.Casualties[s]);
            l += Soft(Math.Abs(a - t) / (double)Math.Max(t / 10, 500));
        }
        l += Soft(Math.Abs(abs.WinPm - tac.WinPm) / 100.0);
        l += Soft(Math.Abs(abs.MeanDurationX10 - tac.MeanDurationX10) * 4.0 / Math.Max(1, tac.MeanDurationX10));
        int[] ma = abs.Mix(), mt = tac.Mix();
        for (int i = 0; i < 4; i++) { int tol = mt[i] < 50 ? 30 : Math.Max(30, mt[i] / 10); l += 0.5 * Soft(Math.Abs(ma[i] - mt[i]) / (double)tol); }
        return l;
    }

    public static int Run(FightContent fc, WoundTables tables, string[] args, string repo)
    {
        int seeds = Arg(args, "--seeds", 200), rounds = Arg(args, "--rounds", 3);
        var src = File.ReadAllText(Path.Combine(repo, "tests", "Ravensguard.Sim.Tests", "Combat", "Abstract", "TacticalReference.cs"));
        var tac = new Dictionary<W.Scenario, AbstractEquivalence.Tally>();
        foreach (var s in Enum.GetValues<W.Scenario>())
        {
            var m = Regex.Match(src, @"Scenario\." + s + @"\] = \[([\d, ]+)\]");
            tac[s] = AbstractEquivalence.Unpack(m.Groups[1].Value.Split(',').Select(x => long.Parse(x.Trim())).ToArray());
        }
        var tac4 = new Dictionary<AbstractScenarios.Scenario, AbstractEquivalence.Tally>();
        foreach (var s in AbstractScenarios.Gate)
        {
            var m = Regex.Match(src, @"Scenario\." + s + @"\] = \[([\d, ]+)\]");
            tac4[s] = AbstractEquivalence.Unpack(m.Groups[1].Value.Split(',').Select(x => long.Parse(x.Trim())).ToArray());
        }
        int w4 = Arg(args, "--w4", 1);
        var wt = new Dictionary<string, int> { ["E"] = 1, ["F"] = 1, ["G"] = 1 };
        var ws = Str(args, "--weight"); if (ws != null) foreach (var p in ws.Split(',')) { var kv = p.Split('='); wt[kv[0]] = int.Parse(kv[1]); }
        var names = Str(args, "--params")?.Split(',');
        var props = typeof(AbstractBattleDef).GetProperties().Where(p => p.PropertyType == typeof(int) && p.SetMethod != null && (names == null || names.Contains(p.Name))).ToArray();
        var cur = AbstractBattle.V2Def(fc.Db) with { };
        double Loss(AbstractBattleDef d)
        {
            var part = new double[3]; var all = Enum.GetValues<W.Scenario>();
            Parallel.For(0, all.Length, i =>
            {
                var s = all[i]; if (!wt.TryGetValue(s.ToString(), out int w) || w == 0) return;
                part[i] = w * Err(tac[s], AbstractEquivalence.RunAbstractOf(fc, tables, x => W.InputOf(s, x), TacticalReference_Seed0, seeds, d));
            });
            var p4 = new double[AbstractScenarios.Gate.Length];
            if (w4 > 0) Parallel.For(0, p4.Length, i =>
            {
                var s = AbstractScenarios.Gate[i];
                p4[i] = w4 * Err(tac4[s], AbstractEquivalence.RunAbstract(fc, tables, s, TacticalReference_Seed0, seeds, d));
            });
            return part.Sum() + p4.Sum();
        }
        double best = Loss(cur); Console.WriteLine($"start {best:0.00}");
        for (int r = 0; r < rounds; r++)
        {
            bool any = false;
            foreach (var p in props)
            {
                int o = (int)p.GetValue(cur)!;
                foreach (int t in new[] { o * 5 / 4 + 1, o * 4 / 5, o + 50, o - 50, o * 3 / 2 + 1, o / 2, o + 10, o - 10 })
                {
                    int v = Math.Max(0, t); if (v == o) continue;
                    var trial = cur with { }; p.SetValue(trial, v);
                    double l; try { l = Loss(trial); } catch (Exception) { continue; }
                    if (l < best - 1e-9) { best = l; cur = trial; o = v; any = true; Console.WriteLine($"  {p.Name} = {v}  loss {best:0.00}"); }
                }
            }
            Console.WriteLine($"round {r + 1}: {best:0.00}");
            if (!any) break;
        }
        var def = AbstractBattle.V2Def(fc.Db);
        Console.WriteLine("changed: " + string.Join(", ", props.Where(p => (int)p.GetValue(cur)! != (int)p.GetValue(def)!).Select(p => $"\"{Snake(p.Name)}\": {p.GetValue(cur)}")));
        return 0;
    }

    const int TacticalReference_Seed0 = 20000;
    static string Snake(string n) => Regex.Replace(n, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}
