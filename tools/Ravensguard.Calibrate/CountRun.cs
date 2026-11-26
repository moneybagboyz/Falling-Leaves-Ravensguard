// tools/calibrate --abstract count (abstract-war 4.4, AW-10): fits the count battle's tables to v2.
//   ... --abstract count v2 [--seeds 4] [--seed0 100]                 v2's means per scenario (a look)
//   ... --abstract count fit [--rounds 6] [--fitseeds 10]             fits abstract/count_battle.jsonc to v2 on the fit seeds (100 on), writes it
//   ... --abstract count gate [--seeds 16] [--seed0 5000]             the gate on fresh seeds: count against v2, scenario by scenario
//   ... --abstract count reference [--seeds 16] [--seed0 5000]        the v2 reference of the gate as C# for tests/.../CountReference.cs
using System.Text.Json;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;

static class CountRun
{
    static int Arg(string[] args, string name, int def)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : def;
    }

    /// <summary>Per scenario sums over seeds: n, wins0, wins1, duration s x1000, casualties by side x1000, and by side and class the casualties, dead, crippled, fled, yielded.</summary>
    sealed class Stat
    {
        public int N, Wins0, Wins1; public long DurMilli; public long[] Cas = new long[2];
        public long[][] ClsCas = [new long[5], new long[5]], ClsDead = [new long[5], new long[5]], ClsCrip = [new long[5], new long[5]], ClsFled = [new long[5], new long[5]], ClsYield = [new long[5], new long[5]];
        public double Cas0 => Cas[0] / (double)N;
        public double Cas1 => Cas[1] / (double)N;
        public double Dur => DurMilli / 1000.0 / N;
        public double Win0 => Wins0 / (double)N;
    }

    static int SeedsFor(CountSpec s, int baseSeeds) => Math.Max(baseSeeds, 1) * (s.A.Sum() + s.B.Sum() <= 2000 ? 2 : 1);

    static Stat V2Stat(FightContent fc, WoundTables tables, AbstractBattleDef k, CountSpec spec, int seed0, int seeds)
    {
        var st = new Stat();
        for (int i = 0; i < seeds; i++)
        {
            var r = CountScenarios.RunV2(fc, tables, k, spec, (ulong)(seed0 + i));
            st.N++; if (r.Winner == 0) st.Wins0++; else if (r.Winner == 1) st.Wins1++;
            st.DurMilli += r.DurationS * 1000L;
            for (int s = 0; s < 2; s++)
            {
                st.Cas[s] += r.Total(r.Casualties, s);
                for (int c = 0; c < 5; c++) { st.ClsCas[s][c] += r.Casualties[s][c]; st.ClsDead[s][c] += r.Dead[s][c]; st.ClsCrip[s][c] += r.Crippled[s][c]; st.ClsFled[s][c] += r.Fled[s][c]; st.ClsYield[s][c] += r.Yielded[s][c]; }
            }
        }
        return st;
    }

    static Stat CountStat(CountBattleDef d, CountSpec spec, int seeds)
    {
        var st = new Stat();
        for (int i = 0; i < seeds; i++)
        {
            var r = CountBattle.Fight(d, 77, (ulong)(i + 1), [spec.A, spec.B]);
            st.N++; if (r.Winner == 0) st.Wins0++; else if (r.Winner == 1) st.Wins1++;
            st.DurMilli += r.DurationS * 1000L;
            for (int s = 0; s < 2; s++) st.Cas[s] += r.Casualties(s);
        }
        return st;
    }

    // The gate's three tolerances: casualties 10% (a floor of 0.5% of the side's start for tiny means), win rate 10 points, duration 25%.
    public static double CasErr(double cv, double cc, int start) => CountGate.CasErr(cv, cc, start);
    public static double WinErr(double wv, double wc) => CountGate.WinErr(wv, wc);
    public static double DurErr(double dv, double dc) => CountGate.DurErr(dv, dc);

    static double Loss(CountBattleDef d, IReadOnlyList<CountSpec> specs, Stat[] v2, int seeds)
    {
        double loss = 0;
        for (int i = 0; i < specs.Count; i++)
        {
            var c = CountStat(d, specs[i], seeds);
            double e0 = CasErr(v2[i].Cas0, c.Cas0, specs[i].A.Sum()), e1 = CasErr(v2[i].Cas1, c.Cas1, specs[i].B.Sum()), ew = WinErr(v2[i].Win0, c.Win0), ed = DurErr(v2[i].Dur, c.Dur);
            loss += Soft(e0) + Soft(e1) + Soft(ew) + Soft(ed);
        }
        return loss;
    }

    static double Soft(double e) => e <= 1 ? e * e : 1 + 2 * (e - 1) + (e - 1) * (e - 1);

    static BattleSummary Gate(CountBattleDef d, IReadOnlyList<CountSpec> specs, Stat[] v2, bool print)
    {
        int pass = 0; var fails = new List<string>();
        for (int i = 0; i < specs.Count; i++)
        {
            var c = CountStat(d, specs[i], 200);
            double e0 = CasErr(v2[i].Cas0, c.Cas0, specs[i].A.Sum()), e1 = CasErr(v2[i].Cas1, c.Cas1, specs[i].B.Sum()), ew = WinErr(v2[i].Win0, c.Win0), ed = DurErr(v2[i].Dur, c.Dur);
            bool ok = e0 <= 1 && e1 <= 1 && ew <= 1 && ed <= 1;
            if (ok) pass++; else fails.Add(specs[i].Name);
            if (print) Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {specs[i].Name,-66} cas {v2[i].Cas0,6:0.0}/{v2[i].Cas1,6:0.0} v {c.Cas0,6:0.0}/{c.Cas1,6:0.0}  win {v2[i].Win0:0.00} v {c.Win0:0.00}  dur {v2[i].Dur,5:0.0} v {c.Dur,5:0.0}   err {e0:0.0} {e1:0.0} {ew:0.0} {ed:0.0}");
        }
        return new BattleSummary(pass, fails);
    }

    sealed record BattleSummary(int Pass, List<string> Fails);

    static Stat[] V2All(FightContent fc, WoundTables tables, AbstractBattleDef k, IReadOnlyList<CountSpec> specs, int seed0, int seeds)
    {
        var r = new Stat[specs.Count];
        Parallel.For(0, specs.Count, i => r[i] = V2Stat(fc, tables, k, specs[i], seed0, SeedsFor(specs[i], seeds)));
        return r;
    }

    public static int Run(FightContent fc, WoundTables tables, string[] args, string repo)
    {
        var k = AbstractBattle.V2Def(fc.Db);
        var specs = CountScenarios.Gate;
        if (args.Contains("v2"))
        {
            int seeds = Arg(args, "--seeds", 4), seed0 = Arg(args, "--seed0", 100);
            var all = V2All(fc, tables, k, specs, seed0, seeds);
            for (int i = 0; i < specs.Count; i++) Console.WriteLine($"{specs[i].Name,-70} cas {all[i].Cas0:0.0} / {all[i].Cas1:0.0} win0 {all[i].Wins0}/{all[i].N} dur {all[i].Dur:0.0} s");
            return 0;
        }
        var cur = fc.Db.All<CountBattleDef>().FirstOrDefault() ?? new CountBattleDef { Id = "count" };
        if (args.Contains("fit"))
        {
            int rounds = Arg(args, "--rounds", 6), fitSeeds = Arg(args, "--fitseeds", 10), cseeds = 24;
            var v2 = V2All(fc, tables, k, specs, 100, fitSeeds);
            Console.WriteLine("v2 fit set done");
            if (!args.Contains("--resume")) cur = Initial(cur, v2);
            else { cur = Mix(cur, v2); if (cur.K.Length < CountK.Count) cur = cur with { K = [.. cur.K, .. new int[] { 200, 100, 20 }[..(CountK.Count - cur.K.Length)]] }; }
            var names = CountBattleDef.FlatNames();
            var best = cur.Flat(); double bl = Loss(cur, specs, v2, cseeds);
            Console.WriteLine($"start loss {bl:0.0}");
            int fixedFrom = best.Length - CountK.Count;
            var locked = new HashSet<int> { fixedFrom + CountK.StepS, fixedFrom + CountK.MaxSteps, fixedFrom + CountK.ChargeSteps, fixedFrom + CountK.FlankedPm };
            for (int round = 0; round < rounds; round++)
            {
                bool any = false;
                var cand = new (int idx, int val)[0];
                for (int idx = 0; idx < best.Length; idx++)
                {
                    if (locked.Contains(idx)) continue;
                    int orig = best[idx];
                    int[] tries = [orig * 5 / 4 + 1, orig * 4 / 5, orig + 20 * (round < 2 ? 2 : 1), orig - 20 * (round < 2 ? 2 : 1), orig * 3 / 2 + 1, orig / 2, orig * 2 + 5, orig / 4, orig + 200, orig - 200];
                    foreach (int t0 in tries)
                    {
                        int t = Math.Max(0, t0); if (t == best[idx]) continue;
                        var trial = (int[])best.Clone(); trial[idx] = t;
                        double l = Loss(cur.WithFlat(trial), specs, v2, cseeds);
                        if (l < bl - 1e-9) { bl = l; best = trial; any = true; }
                    }
                }
                Console.WriteLine($"round {round + 1}: loss {bl:0.00}");
                cur = cur.WithFlat(best);
                if (!any) break;
            }
            int anneal = Arg(args, "--anneal", 0);
            var rng = new Random(Arg(args, "--rseed", 1));
            for (int it = 0; it < anneal; it++)
            {
                var trial = (int[])best.Clone();
                int nch = 1 + rng.Next(3);
                for (int j = 0; j < nch; j++)
                {
                    int idx = rng.Next(best.Length); if (locked.Contains(idx)) continue;
                    double f = Math.Exp((rng.NextDouble() - 0.5) * (it < anneal / 2 ? 1.0 : 0.4));
                    trial[idx] = Math.Max(0, (int)Math.Round(trial[idx] * f) + rng.Next(-3, 4));
                }
                double l = Loss(cur.WithFlat(trial), specs, v2, cseeds);
                if (l < bl) { bl = l; best = trial; }
                if (it % 2000 == 0) { Console.WriteLine($"anneal {it}: loss {bl:0.00}"); Write(cur.WithFlat(best), repo, quiet: true); }
            }
            cur = cur.WithFlat(best);
            // the dead/crippled mix and the capture share are read straight from v2's tallies
            cur = Mix(cur, v2);
            var sum = Gate(cur, specs, v2, true);
            Console.WriteLine($"fit set: {sum.Pass}/40 within the gate");
            Write(cur, repo);
            return 0;
        }
        if (args.Contains("gate"))
        {
            int seeds = Arg(args, "--seeds", 16), seed0 = Arg(args, "--seed0", 5000);
            var v2 = V2All(fc, tables, k, specs, seed0, seeds);
            var sum = Gate(cur, specs, v2, true);
            Console.WriteLine($"{sum.Pass}/40 within the gate (fresh seeds {seed0}+)");
            return 0;
        }
        if (args.Contains("reference"))
        {
            int seeds = Arg(args, "--seeds", 16), seed0 = Arg(args, "--seed0", 5000);
            var v2 = V2All(fc, tables, k, specs, seed0, seeds);
            Console.WriteLine($"    public const string ContentHash = \"{WoundTables.ContentHash(fc.Db)}\";");
            Console.WriteLine($"    public const string TablesFingerprint = \"{cur.Fingerprint()}\";");
            Console.WriteLine($"    public const int Seed0 = {seed0};");
            Console.WriteLine("    // n, side 0 wins, side 1 wins, duration x1000 s (sum), casualties x1 of side 0 (sum), of side 1 (sum)");
            Console.WriteLine("    public static readonly long[][] Tallies =\n    [");
            foreach (var s in v2) Console.WriteLine($"        [{s.N}, {s.Wins0}, {s.Wins1}, {s.DurMilli}, {s.Cas[0]}, {s.Cas[1]}],");
            Console.WriteLine("    ];");
            return 0;
        }
        return 0;
    }

    static CountBattleDef Initial(CountBattleDef d, Stat[] v2) => d with
    {
        Beta = [.. Enumerable.Repeat(2500, 25)],
        Share = [250, 250, 150, 250, 250],
        Expo = [1000, 1000, 600, 1000, 1000],
        RoutThr = [80, 80, 80, 80, 80],
        K = [2, 150, 600, 500, 300, 1500, 2, 12, 100, 16, 300, 500, 100, 100, 500, 1000, 0, 400, 150, 20],
    };

    static CountBattleDef Mix(CountBattleDef d, Stat[] v2)
    {
        int[] dead = new int[5], crip = new int[5];
        long[] cas = new long[5], dd = new long[5], cc = new long[5];
        long fled = 0, yield = 0;
        foreach (var s in v2)
            for (int side = 0; side < 2; side++)
                for (int c = 0; c < 5; c++) { cas[c] += s.ClsCas[side][c]; dd[c] += s.ClsDead[side][c]; cc[c] += s.ClsCrip[side][c]; fled += s.ClsFled[side][c]; yield += s.ClsYield[side][c]; }
        for (int c = 0; c < 5; c++) { dead[c] = cas[c] == 0 ? d.DeadPm[c] : (int)(dd[c] * 1000 / cas[c]); crip[c] = cas[c] == 0 ? d.CrippledPm[c] : (int)(cc[c] * 1000 / cas[c]); }
        var k = (int[])d.K.Clone();
        k[CountK.CapturePm] = fled + yield == 0 ? k[CountK.CapturePm] : (int)(yield * 1000 / (fled + yield));
        return d with { DeadPm = dead, CrippledPm = crip, K = k };
    }

    static void Write(CountBattleDef d, string repo, bool quiet = false)
    {
        var opt = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        string J(int[] a) => "[" + string.Join(", ", a) + "]";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// The count battle's tables (abstract-war 3, 4.4; row type count_battle), fitted from the v2 resolver by tools/Ravensguard.Calibrate -- --abstract count fit over the 40 scenarios of");
        sb.AppendLine("// AW-10 (CountScenarios.Gate: class mixes, 200 to 4,000 a side) on seeds 100 on, and gated on fresh seeds (5000 on) within +-10% casualties, +-10 points win rate, +-25% duration.");
        sb.AppendLine("// Classes in order: levy, foot, missile, horse, elite. beta is attacker x 5 + victim, in milli-men per engaged man-minute. k: see CountK (step_s, max_steps, rout_slope, rout_max_pm,");
        sb.AppendLine("// break_standing_pm, charge_pm, charge_steps, ammo_s, ammo_low_pm, flee_s, pursuit_pm, pursuit_horse_pm, capture_pm, luck_pm, ratio_pm, morale_scale_pm, flanked_pm).");
        sb.AppendLine("[");
        sb.AppendLine($"  {{ \"type\": \"count_battle\", \"id\": \"count\",");
        sb.AppendLine($"    \"beta\": {J(d.Beta)},");
        sb.AppendLine($"    \"share\": {J(d.Share)}, \"expo\": {J(d.Expo)}, \"rout_thr\": {J(d.RoutThr)},");
        sb.AppendLine($"    \"dead_pm\": {J(d.DeadPm)}, \"crippled_pm\": {J(d.CrippledPm)},");
        sb.AppendLine($"    \"k\": {J(d.K)} }}");
        sb.AppendLine("]");
        File.WriteAllText(Path.Combine(repo, "content", "base", "abstract", "count_battle.jsonc"), sb.ToString());
        if (!quiet) Console.WriteLine("wrote count_battle.jsonc, fingerprint " + d.Fingerprint());
    }
}
