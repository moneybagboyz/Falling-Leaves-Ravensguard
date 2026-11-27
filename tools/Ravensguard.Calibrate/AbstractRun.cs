// tools/calibrate --abstract (abstract-battles-v1 4.3): builds the wound tables from the live fight, runs the tactical scenarios and compares the abstract battle with them.
//   dotnet run --project tools/Ravensguard.Calibrate -c Release -- --abstract tables [--blows 4000]     writes content/base/abstract/wound_tables.json and wound_library.json
//   dotnet run --project tools/Ravensguard.Calibrate -c Release -- --abstract equiv [--seeds 200] [--seed0 10000] [--only A]   tactical against abstract, the 4.4 gate
//   dotnet run --project tools/Ravensguard.Calibrate -c Release -- --abstract bench | cellbench | mt12
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;

static class AbstractRun
{
    static int Arg(string[] args, string name, int def)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : def;
    }

    static string? Str(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static int Run(FightContent fc, string[] args, string repo)
    {
        if (args.Contains("bench"))
        {
            foreach (var s in AbstractScenarios.Gate)
            {
                var sw = Stopwatch.StartNew();
                int n = 8; double secs = 0; int[] act = new int[2];
                for (int i = 0; i < n; i++)
                {
                    var w = AbstractScenarios.Tactical(fc, s, (ulong)(100 + i));
                    while (!w.IsOver && w.Tick < 600 * 20) w.StepWithAi();
                    secs += w.Tick / 20.0;
                    foreach (var f in w.Fighters) if (f.Active) act[f.Team]++;
                }
                Console.WriteLine($"{s}: {sw.ElapsedMilliseconds / n} ms per fight, mean {secs / n:0.0} s, active at end {act[0] / (double)n:0.0} / {act[1] / (double)n:0.0}");
            }
            return 0;
        }
        if (args.Contains("hitdiag"))
        {
            // per attacker weapon class: wounding blows by outcome class, mean pain and bleed per wounding blow, in the live scenario and in the abstract one
            var sc = Enum.Parse<AbstractScenarios.Scenario>(Str(args, "--only") ?? "A");
            int seeds = Arg(args, "--seeds", 40);
            var plan = fc.T.Plan("human");
            var tac = new Dictionary<WeaponClass, long[]>();
            void Note(Dictionary<WeaponClass, long[]> d, WeaponClass wc, OutcomeClass o, ReadOnlySpan<Ravensguard.Sim.Combat.Resolver2.WoundV2> wounds)
            {
                if (!d.TryGetValue(wc, out var r)) d[wc] = r = new long[10];
                r[(int)o]++;
                foreach (var wd in wounds) { r[7] += (long)Ravensguard.Sim.Combat.Wounds.WoundBook.PainOf(plan, wd, Ravensguard.Sim.Combat.Wounds.WoundBook.Categorise(plan, wd)) * plan.Parts[wd.Part].PainPm / 1000; r[8] += wd.BleedX10; }
            }
            for (int i = 0; i < seeds; i++)
            {
                var w = AbstractScenarios.Tactical(fc, sc, (ulong)(1000 + i));
                w.OnBlow = (a, t, r, s) => { if (w.IsHorse(a)) return; var wc = s.Missile ? WeaponClass.Longbow : AbstractClasses.Of(a.Weapon.Shape); Note(tac, wc, r.Blocked && r.WoundCount == 0 || !r.Hit ? OutcomeClass.Miss : AbstractClasses.Classify(t.Plan, r.WoundSpan), r.WoundSpan); };
                while (!w.IsOver && w.Tick < 600 * 20) w.StepWithAi();
            }
            var tb = WoundTables.For(fc.Db)!;
            var abs = new Dictionary<WeaponClass, long[]>();
            for (int i = 0; i < seeds; i++)
            {
                var b = new AbstractBattle(fc, tb, AbstractEquivalence.InputOf(sc, (ulong)(1000 + i)) with { Aftermath = false });
                b.OnHit = (wc, o, tpl) => Note(abs, wc, o, tpl == null ? default : tpl.Wounds);
                while (!b.Over) b.StepOnce();
            }
            foreach (var (name, d) in new[] { ("tactical", tac), ("abstract", abs) })
                foreach (var (wc, r) in d.OrderBy(x => x.Key))
                {
                    long wn = r[2] + r[3] + r[4] + r[5] + r[6];
                    Console.WriteLine($"{name} {wc}: resolved {r.Take(7).Sum()}, miss {r[0]}, nothing {r[1]}, wounding {wn}: light/serious/grave/severed/instant {string.Join("/", Enumerable.Range(2, 5).Select(k => wn == 0 ? 0 : r[k] * 1000 / wn))} pm, pain/hit {(wn == 0 ? 0 : r[7] / wn)}, bleed x10/hit {(wn == 0 ? 0 : r[8] / wn)}");
                }
            return 0;
        }
        if (args.Contains("yielddiag"))
        {
            var sc = Enum.Parse<AbstractScenarios.Scenario>(Str(args, "--only") ?? "A");
            int seeds = Arg(args, "--seeds", 40);
            long n = 0, rn = 0; long[] sum = new long[12], rsum = new long[12];
            for (int i = 0; i < seeds; i++)
            {
                var w = AbstractScenarios.Tactical(fc, sc, (ulong)(1000 + i));
                var hits = new int[w.Fighters.Count];
                w.OnBlow = (a, t, r, s) => { if (r.WoundCount > 0) hits[t.Id]++; };
                w.OnEvent = e =>
                {
                    if (e.Kind is not (FightEventKind.Surrender or FightEventKind.Rout) || e.Actor < 0) return;
                    var f = w.Fighters[e.Actor]; var tm = w.TermsOf(f);
                    if (Arg(args, "--team", -1) is int tt && tt >= 0 && f.Team != tt) return;
                    var arr = e.Kind == FightEventKind.Surrender ? sum : rsum;
                    if (e.Kind == FightEventKind.Surrender) n++; else rn++;
                    arr[0] += tm.PenaltyX100; arr[1] += tm.RoutingX100; arr[2] += tm.FlankedX100; arr[3] += w.MoraleK.PainTerm(tm.PainPct); arr[4] += w.MoraleK.BloodTerm(tm.BloodLossPm);
                    arr[5] += tm.CaptainX100; arr[6] += tm.WinningX100; arr[7] += tm.OutnumberedX100; arr[8] += tm.AloneX100; arr[9] += f.M.ValueX100; arr[10] += hits[f.Id] * 100; arr[11] += f.Routing ? 100 : 0;
                };
                while (!w.IsOver && w.Tick < 600 * 20) w.StepWithAi();
            }
            string[] names = ["penalty", "routing", "flank", "pain", "blood", "captain", "winning", "outnumbered", "alone", "value", "wounding hits", "was routing"];
            Console.WriteLine($"surrenders {n / (double)seeds:0.0} per fight; routs {rn / (double)seeds:0.0}");
            var tb = WoundTables.For(fc.Db)!;
            long ro = 0, ly = 0, ry = 0, fl = 0;
            for (int i = 0; i < seeds; i++)
            {
                var b = new AbstractBattle(fc, tb, AbstractEquivalence.InputOf(sc, (ulong)(1000 + i)) with { Aftermath = false });
                while (!b.Over) b.StepOnce();
                ro += b.Routs; ly += b.LineYields; ry += b.RouterYields; fl += b.Flights;
            }
            Console.WriteLine($"abstract: routs {ro / (double)seeds:0.0}, yields from the line {ly / (double)seeds:0.0}, while routing {ry / (double)seeds:0.0}, flights {fl / (double)seeds:0.0}");
            for (int k = 0; k < 12; k++) Console.WriteLine($"  {names[k]}: at surrender {sum[k] / 100.0 / Math.Max(1, n):0.0}, at rout {rsum[k] / 100.0 / Math.Max(1, rn):0.0}");
            return 0;
        }
        if (args.Contains("classes"))
        {
            for (int a = 0; a < AbstractClasses.ArmourClasses; a++) Console.WriteLine($"{(ArmourClass)a}: {AbstractClasses.ProtectionPm(fc.T, AbstractClasses.Kit((ArmourClass)a))}");
            foreach (var s in AbstractScenarios.Gate)
            {
                var kits = AbstractScenarios.Kits(s, 1, out _, out _);
                Console.WriteLine($"{s}: " + string.Join(", ", kits.GroupBy(k => k.Team).Select(g => $"team {g.Key}: " + string.Join(" ", g.Select(k => $"{AbstractClasses.MeleeOf(fc, k)}/{AbstractClasses.ArmourOf(fc.T, k.Worn)}({AbstractClasses.ProtectionPm(fc.T, k.Worn)})").Distinct()))));
            }
            return 0;
        }
        if (args.Contains("cellbench"))
        {
            foreach (var wc in Enum.GetValues<WeaponClass>())
            {
                var sw = Stopwatch.StartNew();
                var cell = WoundTableBuilder.BuildCell(fc, wc, ArmourClass.Padded, 3, false, 1000);
                Console.WriteLine($"{wc}: {sw.ElapsedMilliseconds} ms, worlds {cell.Worlds}, n {cell.N}: {string.Join(" ", WoundTableBuilder.Permille(cell))}");
            }
            return 0;
        }
        if (args.Contains("diag"))
        {
            var sc = Enum.Parse<AbstractScenarios.Scenario>(Str(args, "--only") ?? "A");
            int seeds = Arg(args, "--seeds", 20);
            var rows = new SortedDictionary<int, long[]>();
            for (int i = 0; i < seeds; i++)
            {
                var w = AbstractScenarios.Tactical(fc, sc, (ulong)(1000 + i));
                int contact = -1;
                long[] act = new long[2], hit = new long[2];
                w.OnBlow = (a, t, r, s) => { if (contact < 0 && r.WoundCount > 0) contact = w.Tick; if (!w.IsHorse(a)) { act[a.Team]++; if (r.WoundCount > 0) hit[a.Team]++; } };
                w.OnEvent = e => { if (e.Kind == FightEventKind.Whiff && e.Actor >= 0) act[w.Fighters[e.Actor].Team]++; else if (e.Kind is FightEventKind.Dodged or FightEventKind.Parried && e.Target >= 0) act[w.Fighters[e.Target].Team]++; };
                while (!w.IsOver && w.Tick < 600 * 20)
                {
                    w.StepWithAi();
                    if (w.Tick % 20 != 0) continue;
                    int sec = w.Tick / 20;
                    if (!rows.TryGetValue(sec, out var r)) rows[sec] = r = new long[40];
                    r[0]++;
                    r[16] += act[0]; r[17] += hit[0]; r[36] += act[1]; r[37] += hit[1];
                    foreach (var f in w.Fighters)
                    {
                        if (w.IsHorse(f)) continue;
                        int o = f.Team * 20;
                        if (f.Act is FAct.Down or FAct.Dead) r[o + 1]++;
                        else if (f.Act == FAct.Surrendered) r[o + 2]++;
                        else if (f.Act == FAct.Fled) r[o + 3]++;
                        else if (f.Routing) r[o + 4]++;
                        else
                        {
                            r[o + 5]++;
                            var tm = w.TermsOf(f);
                            r[o + 6] += tm.PenaltyX100; r[o + 7] += tm.RoutingX100; r[o + 8] += tm.FlankedX100; r[o + 9] += w.MoraleK.PainTerm(tm.PainPct); r[o + 10] += w.MoraleK.BloodTerm(tm.BloodLossPm);
                            r[o + 11] += tm.CaptainX100; r[o + 12] += tm.WinningX100; r[o + 13] += tm.OutnumberedX100; r[o + 14] += tm.AloneX100; r[o + 15] += f.M.ValueX100;
                        }
                    }
                }
            }
            Console.WriteLine("sec n | per side: out yielded fled routing inline | pen rout flank pain blood capt win outn alone value (points, means over the line)");
            foreach (var (sec, r) in rows)
            {
                string S(int o) { long n = Math.Max(1, r[o + 5]); return $"{r[o + 1] / (double)r[0],4:0.0} {r[o + 2] / (double)r[0],4:0.0} {r[o + 3] / (double)r[0],4:0.0} {r[o + 4] / (double)r[0],4:0.0} {r[o + 5] / (double)r[0],4:0.0} | " + string.Join(" ", Enumerable.Range(6, 10).Select(k => $"{r[o + k] / 100.0 / n,4:0}")) + $" | act {r[o + 16] / (double)r[0],5:0.0} hit {r[o + 17] / (double)r[0],5:0.0}"; }
                Console.WriteLine($"{sec,3} {r[0],3} | {S(0)} || {S(20)}");
            }
            return 0;
        }
        if (args.Contains("adiag"))
        {
            var tb = WoundTables.For(fc.Db)!;
            var sc = Enum.Parse<AbstractScenarios.Scenario>(Str(args, "--only") ?? "A");
            int seeds = Arg(args, "--seeds", 20);
            var rows = new SortedDictionary<int, long[]>();
            for (int i = 0; i < seeds; i++)
            {
                var b = new AbstractBattle(fc, tb, AbstractEquivalence.InputOf(sc, (ulong)(1000 + i)) with { Aftermath = false });
                while (!b.Over)
                {
                    b.StepOnce();
                    int sec = b.Step * b.K.StepS;
                    if (!rows.TryGetValue(sec, out var r)) rows[sec] = r = new long[40];
                    r[0]++;
                    foreach (var x in b.Soldiers)
                    {
                        int o = x.Side * 20;
                        if (x.Status is SoldierStatus.Down or SoldierStatus.Dead) r[o + 1]++;
                        else if (x.Status == SoldierStatus.Surrendered) r[o + 2]++;
                        else if (x.Status == SoldierStatus.Fled) r[o + 3]++;
                        else if (x.Routing) r[o + 4]++;
                        else { r[o + 5]++; r[o + 6] += x.MoraleX100; r[o + 7] += x.PenaltyX100; }
                        r[o + 8] += x.HitsTaken;
                    }
                    r[9] += b.GapMm; r[10] = r[10] + 0; r[18] += b.ActionsBy[0]; r[19] += b.WoundingBy[0]; r[38] += b.ActionsBy[1]; r[39] += b.WoundingBy[1];
                }
            }
            Console.WriteLine("sec n | per side: out yielded fled routing inline | value penalty | hits taken");
            foreach (var (sec, r) in rows)
            {
                string S(int o) { long n = Math.Max(1, r[o + 5]); return $"{r[o + 1] / (double)r[0],4:0.0} {r[o + 2] / (double)r[0],4:0.0} {r[o + 3] / (double)r[0],4:0.0} {r[o + 4] / (double)r[0],4:0.0} {r[o + 5] / (double)r[0],4:0.0} | {r[o + 6] / 100.0 / n,4:0} {r[o + 7] / 100.0 / n,4:0} | {r[o + 8] / (double)r[0],5:0.0} | act {r[o + 18] / (double)r[0],5:0.0} hit {r[o + 19] / (double)r[0],5:0.0}"; }
                Console.WriteLine($"{sec,3} {r[0],3} gap {r[9] / r[0] / 1000.0,5:0.0} | {S(0)} || {S(20)}");
            }
            return 0;
        }
        if (args.Contains("tables"))
        {
            int blows = Arg(args, "--blows", 4000);
            var sw = Stopwatch.StartNew();
            int last = -1;
            var cells = WoundTableBuilder.BuildAll(fc, blows, progress: (d, n) => { int p = d * 20 / n; if (p != Volatile.Read(ref last)) { Volatile.Write(ref last, p); Console.Error.Write($"\r{d}/{n} cells"); } });
            Console.Error.WriteLine();
            string hash = WoundTables.ContentHash(fc.Db);
            var (t, u) = WoundTableBuilder.Write(cells, hash, blows);
            var dir = Path.Combine(repo, "content", "base", "abstract");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "wound_tables.json"), t);
            File.WriteAllText(Path.Combine(dir, "wound_library.json"), u);
            Console.WriteLine($"{cells.Count} cells x {blows} blows in {sw.Elapsed.TotalSeconds:0} s; hash {hash}; tables {t.Length / 1024} KB, library {u.Length / 1024} KB");
            return 0;
        }
        var tables = WoundTables.For(fc.Db);
        if (tables == null) { Console.Error.WriteLine("no wound tables in content: run --abstract tables first"); return 1; }
        if (args.Contains("count")) return CountRun.Run(fc, tables, args, repo);
        if (args.Contains("fit"))
        {
            // 4.3: fit the constants by minimising the mismatch with the tactical fights (coordinate descent over multipliers); seeds 1000 on (the fit set), equivalence on fresh ones
            int seeds = Arg(args, "--seeds", 100), seed0 = Arg(args, "--seed0", 1000), rounds = Arg(args, "--rounds", 2);
            var scen = new[] { AbstractScenarios.Scenario.A, AbstractScenarios.Scenario.B, AbstractScenarios.Scenario.C, AbstractScenarios.Scenario.D, AbstractScenarios.Scenario.DMud,
                AbstractScenarios.Scenario.AFlanked, AbstractScenarios.Scenario.ANoLeader };
            var tac = scen.ToDictionary(s => s, s => AbstractEquivalence.RunTactical(fc, s, seed0, seeds));
            var k0 = fc.Db.All<AbstractBattleDef>().FirstOrDefault() ?? new AbstractBattleDef { Id = "default" };
            string[] names = (Str(args, "--params") ?? "PressRatePct,PressHitPm,MoraleK,MeleeFlankedPm,CorneredPm,RouterCorneredPm,RouterYieldBelow,EngagementPm,OutnumberCapPm,AdvanceMmps,ChargeMmps,RiderBlowsPerMinX10,RiderSlowBlowsPerMinX10,ChargePm,VsRiderRatePm,VsRiderHitPm,VsRiderSlowRatePm,VsRiderSlowHitPm,PursuePm,BesetPm").Split(',');
            AbstractBattleDef With(AbstractBattleDef d, string n, int v)
            {
                var node = System.Text.Json.JsonSerializer.SerializeToNode(d)!.AsObject();
                node[n] = v;
                return System.Text.Json.JsonSerializer.Deserialize<AbstractBattleDef>(node)!;
            }
            int Get(AbstractBattleDef d, string n) => (int)typeof(AbstractBattleDef).GetProperty(n)!.GetValue(d)!;
            double Loss(AbstractBattleDef d, bool print = false)
            {
                double loss = 0;
                foreach (var s in scen)
                {
                    var a = AbstractEquivalence.RunAbstract(fc, tables, s, seed0, seeds, d);
                    var t = tac[s];
                    double l = 0;
                    for (int sd = 0; sd < 2; sd++) { double tc = t.Mean(t.Casualties[sd]) / 1000.0, ac = a.Mean(a.Casualties[sd]) / 1000.0; l += Math.Pow((ac - tc) / Math.Max(1.0, tc) / 0.1, 2); }
                    l += Math.Pow((a.WinPm - t.WinPm) / 100.0, 2);
                    l += Math.Pow((a.MeanDurationX10 - t.MeanDurationX10) / (double)Math.Max(1, t.MeanDurationX10) / 0.25, 2);
                    for (int sd = 0; sd < 2; sd++) { double tf = t.Mean(t.Fled[sd]) / 1000.0, af = a.Mean(a.Fled[sd]) / 1000.0; l += 0.3 * Math.Pow((af - tf) / Math.Max(2.0, tf) / 0.2, 2); }
                    int[] ma = a.Mix(), mt = t.Mix();
                    for (int i = 1; i < 4; i++) l += 0.3 * Math.Pow((ma[i] - mt[i]) / Math.Max(30.0, mt[i] / 10.0), 2);
                    if (s is AbstractScenarios.Scenario.D or AbstractScenarios.Scenario.DMud) { double tp = t.Mean(t.FirstPass[1]) / 1000.0, ap = a.Mean(a.FirstPass[1]) / 1000.0; l += Math.Pow((ap - tp) / Math.Max(0.5, tp) / 0.15, 2); }
                    if (s is AbstractScenarios.Scenario.AFlanked or AbstractScenarios.Scenario.ANoLeader) l *= 0.5;
                    loss += l;
                    if (print) Console.WriteLine($"  {s}: {l:0.0}  cas {a.Mean(a.Casualties[0]) / 1000.0:0.00}/{a.Mean(a.Casualties[1]) / 1000.0:0.00} vs {t.Mean(t.Casualties[0]) / 1000.0:0.00}/{t.Mean(t.Casualties[1]) / 1000.0:0.00}, win {a.WinPm / 10}% vs {t.WinPm / 10}%, {a.MeanDurationX10 / 10.0:0.0} s vs {t.MeanDurationX10 / 10.0:0.0} s, fled {a.Mean(a.Fled[0]) / 1000.0:0.0}/{a.Mean(a.Fled[1]) / 1000.0:0.0} vs {t.Mean(t.Fled[0]) / 1000.0:0.0}/{t.Mean(t.Fled[1]) / 1000.0:0.0}");
                }
                return loss;
            }
            var best = k0; double bl = Loss(best, true);
            Console.WriteLine($"start loss {bl:0.0}");
            int[] mults = [600, 800, 900, 1100, 1250, 1600];
            for (int r = 0; r < rounds; r++)
                foreach (var n in names)
                {
                    int v0 = Get(best, n);
                    foreach (int m in mults)
                    {
                        int v = v0 == 0 ? m / 10 : (int)((long)v0 * m / 1000);
                        if (v == v0) continue;
                        var c = With(best, n, v);
                        double l = Loss(c);
                        if (l < bl) { bl = l; best = c; Console.WriteLine($"  {n} {v0} -> {v}: loss {bl:0.0}"); v0 = v; }
                    }
                }
            Console.WriteLine($"fit loss {bl:0.0}");
            Loss(best, true);
            foreach (var n in names) Console.WriteLine($"  \"{System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(n)}\": {Get(best, n)},");
            return 0;
        }
        if (args.Contains("ndiag"))
        {
            foreach (int n in new[] { 16, 18, 20 })
            {
                int wins = 0;
                for (int seed = 0; seed < 40; seed++)
                {
                    var kits = AbstractScenarios.Kits(AbstractScenarios.Scenario.A, (ulong)(50_000 + seed), out _, out _);
                    var militia = kits.Where(k => k.Team == 0).Take(n).ToList(); var bandits = kits.Where(k => k.Team == 1).ToList();
                    var b = new AbstractBattle(fc, tables, new BattleInput([.. militia, .. bandits], (ulong)(50_000 + seed), (ulong)(n * 1000 + seed), 8 * 3600) { Aftermath = false });
                    if (seed == 0) Console.WriteLine($"n {n}: gap {b.GapMm}, positions {string.Join("", b.Soldiers.Select(s => s.Position == BattlePosition.Front ? "." : s.Position == BattlePosition.Flank ? "F" : "R"))}, rows {string.Join("", b.Soldiers.Select(s => s.Row))}");
                    b.Run(); if (b.Winner == 0) wins++;
                }
                Console.WriteLine($"n {n}: {wins}/40");
            }
            return 0;
        }
        if (args.Contains("reference"))
        {
            // the tactical reference of the fast AB-3 test (the live fights on the fresh seeds, summed), as C# for tests/.../Combat/Abstract/TacticalReference.cs
            int seeds = Arg(args, "--seeds", 200), seed0 = Arg(args, "--seed0", 20_000);
            Console.WriteLine($"    public const string ContentHash = \"{WoundTables.ContentHash(fc.Db)}\";");
            Console.WriteLine($"    public const int Seed0 = {seed0}, Seeds = {seeds};");
            foreach (var s in Enum.GetValues<AbstractScenarios.Scenario>())
            {
                var t = AbstractEquivalence.RunTactical(fc, s, seed0, seeds);
                Console.WriteLine($"    [AbstractScenarios.Scenario.{s}] = {AbstractEquivalence.Code(t)},");
            }
            return 0;
        }
        if (args.Contains("wartune")) return WarTune.Run(fc, tables, args, repo);
        if (args.Contains("war"))
        {
            // AW-C: v2 against the live fight on E, F, G (AbstractWarScenarios): --abstract war [--seeds 200] [--seed0 10000] [--only E,F] [--reference] [--v1]
            int seeds = Arg(args, "--seeds", 200), seed0 = Arg(args, "--seed0", 10_000);
            string? only = Str(args, "--only");
            var k = args.Contains("--v1") ? null : AbstractBattle.V2Def(fc.Db);
            var sc = Enum.GetValues<AbstractWarScenarios.Scenario>().Where(s => only == null || only.Split(',').Contains(s.ToString())).ToList();
            Console.WriteLine(AbstractEquivalence.Header);
            var fails = new List<string>();
            foreach (var s in sc)
            {
                var tac = AbstractEquivalence.RunTacticalOf(x => AbstractWarScenarios.Tactical(fc, s, x), seed0, seeds);
                if (args.Contains("--reference")) { Console.WriteLine($"        [AbstractWarScenarios.Scenario.{s}] = {AbstractEquivalence.Code(tac)},"); continue; }
                var abs = AbstractEquivalence.RunAbstractOf(fc, tables, x => AbstractWarScenarios.InputOf(s, x), seed0, seeds, k);
                Console.WriteLine(AbstractEquivalence.Row($"{s} tactical", tac));
                Console.WriteLine(AbstractEquivalence.Row($"{s} abstract", abs));
                Console.WriteLine($"  yielded tactical {tac.Mean(tac.Yielded[0]) / 1000.0:0.0}/{tac.Mean(tac.Yielded[1]) / 1000.0:0.0} abstract {abs.Mean(abs.Yielded[0]) / 1000.0:0.0}/{abs.Mean(abs.Yielded[1]) / 1000.0:0.0}");
                fails.AddRange(AbstractEquivalence.Compare(tac, abs, s.ToString()));
            }
            Console.WriteLine(fails.Count == 0 ? "equivalent" : string.Join("\n", fails));
            return 0;
        }
        if (args.Contains("equiv"))
        {
            int seeds = Arg(args, "--seeds", 200), seed0 = Arg(args, "--seed0", 10_000);
            string? only = Str(args, "--only");
            var scen = Enum.GetValues<AbstractScenarios.Scenario>().Where(s => only == null || only.Split(',').Contains(s.ToString())).ToList();
            Console.WriteLine($"tables hash {tables.ContentHashBuilt}, content {WoundTables.ContentHash(fc.Db)}, {tables.CellsBuilt} cells, {tables.TemplateCount} templates");
            Console.WriteLine(AbstractEquivalence.Header);
            var fails = new List<string>();
            foreach (var s in scen)
            {
                var sw = Stopwatch.StartNew();
                var tac = AbstractEquivalence.RunTactical(fc, s, seed0, seeds);
                long tms = sw.ElapsedMilliseconds; sw.Restart();
                var abs = AbstractEquivalence.RunAbstract(fc, tables, s, seed0, seeds, args.Contains("--v2") ? AbstractBattle.V2Def(fc.Db) : null);
                long ams = sw.ElapsedMilliseconds;
                Console.WriteLine(AbstractEquivalence.Row($"{s} tactical", tac));
                Console.WriteLine(AbstractEquivalence.Row($"{s} abstract", abs));
                Console.Error.WriteLine($"  {s}: tactical {tms} ms, abstract {ams} ms; first pass {tac.Mean(tac.FirstPass[1]) / 1000.0:0.00} vs {abs.Mean(abs.FirstPass[1]) / 1000.0:0.00}");
                fails.AddRange(AbstractEquivalence.Compare(tac, abs, s.ToString()));
            }
            Console.WriteLine(fails.Count == 0 ? "equivalent" : string.Join("\n", fails));
            return 0;
        }
        return 0;
    }
}
