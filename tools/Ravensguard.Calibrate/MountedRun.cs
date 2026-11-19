// tools/calibrate --mounted (mounted-combat 8): the mounted pairings (a rider charging one foot fighter or a rider from 40 m) and the MT-12 skirmish (12 mounted against 20 foot,
// against the same twelve men on foot), through the shipped FightWorld, the cavalry brain and the combat brain.
//   dotnet run --project tools/Ravensguard.Calibrate -c Release -- --mounted [--duels 200] [--only name] [--trace i]
using System.Diagnostics;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;

static class MountedRun
{
    public static int Run(FightContent fc, string[] args, int duels, string? only, int trace)
    {
        var pairs = MountedCalibration.MainPairings().Where(p => only == null || p.Name.Contains(only, StringComparison.Ordinal)).ToList();
        if (trace >= 0)
        {
            var p = pairs[0];
            var w = MountedCalibration.ChargeWorld(fc, p, trace);
            Console.WriteLine($"{p.Name}: skills {w.Fighters[0].Skill} / {w.Fighters.First(f => f.Name == "Foot").Skill}");
            int last = -1;
            while (!w.IsOver && w.Tick < 120 * 20)
            {
                w.StepWithAi();
                if (w.Tick % 10 == 0)
                {
                    var m = w.Mounts!.Pairs[0]; var r = w.Fighters[m.Rider];
                    if (w.Tick - last >= 20) { Console.WriteLine($"{w.Tick,5} rider {r.X / 1000.0,7:0.0},{r.Y / 1000.0,6:0.0} v {m.Speed / 1000.0,5:0.0} phase {m.AiPhase} act {r.Act} panic {m.PanicPm} stam {m.StaminaU / 1000}"); last = w.Tick; }
                }
            }
            foreach (var e in w.Events)
                if (e.Kind is not (FightEventKind.Windup))
                    Console.WriteLine($"{e.Tick,5} {e.Kind,-12} {e.Actor,2} -> {e.Target,2} part {(e.Part >= 0 && e.Target >= 0 ? w.Fighters[e.Target].Plan.Parts[Math.Min(e.Part, w.Fighters[e.Target].Plan.Parts.Length - 1)].Id : "-"),-14} {e.A,6} {e.B,6}");
            foreach (var f in w.Fighters) Console.WriteLine($"{f.Name}: {f.Act} blood {f.Book.LossPm} pm lost, pain {f.Book.PainX100 / 100}, wounds {f.Book.Records.Count}");
            return 0;
        }
        if (args.Contains("--why"))
        {
            // what the riders' blows do in the skirmish's first 20 s of contact
            var kinds = new int[64]; int blows = 0, wounding = 0, downs = 0;
            for (int s = 0; s < 100; s++)
            {
                var w = MountedCalibration.Skirmish(fc, (ulong)(900 + s));
                MountedCalibration.FirstContact(w, 20, onBlow: (a, t, r, st) => { if (a.Team == 0 && !w.IsHorse(a)) { blows++; if (r.WoundCount > 0 && !r.Blocked) wounding++; } });
                foreach (var e in w.Events)
                {
                    bool riderActor = e.Actor >= 0 && w.Fighters[e.Actor].Team == 0;
                    bool riderTarget = e.Target >= 0 && w.Fighters[e.Target].Team == 0;
                    if (e.Kind is FightEventKind.Dodged or FightEventKind.Parried or FightEventKind.Blocked ? riderTarget : riderActor) kinds[(int)e.Kind]++;
                    if (e.Kind == FightEventKind.Down && w.Fighters[e.Actor].Team == 1) downs++;
                }
            }
            Console.WriteLine($"rider blows {blows}, wounding {wounding}, foot downs {downs}");
            Console.WriteLine(string.Join(", ", Enumerable.Range(0, 64).Where(i => kinds[i] > 0).Select(i => $"{(FightEventKind)i} {kinds[i]}")));
            return 0;
        }
        int k12 = Array.IndexOf(args, "--mt12");
        if (k12 >= 0 && k12 + 1 < args.Length && int.TryParse(args[k12 + 1], out var n12))
        {
            // the MT-12 measure at several windows: casualties of the charged side, mounted against the same men on foot
            foreach (int mix in new[] { 0, 1, 2 })
            foreach (int win in new[] { 10, 15, 20 })
            {
                var res = new (int M, int F)[n12];
                Parallel.For(0, n12, s => res[s] = (MountedCalibration.FirstContact(MountedCalibration.Skirmish(fc, (ulong)(900 + s), footMix: mix), win).Casualties,
                    MountedCalibration.FirstContact(MountedCalibration.Skirmish(fc, (ulong)(900 + s), mounted: false, footMix: mix), win).Casualties));
                double mm = res.Average(x => x.M), ff = res.Average(x => x.F);
                Console.WriteLine($"foot mix {mix}, window {win,2} s, {n12} seeds: mounted {mm:0.00}, foot {ff:0.00}, bonus {100.0 * (mm / Math.Max(0.01, ff) - 1):0}%");
            }
            return 0;
        }
        var sw = Stopwatch.StartNew();
        var rows = pairs.Select(p => MountedCalibration.Run(fc, p, duels)).ToList();
        Console.WriteLine($"{rows.Sum(r => r.N)} mounted duels in {sw.Elapsed.TotalSeconds:0.0} s");
        Console.WriteLine();
        Console.WriteLine(MountedCalibration.Table(rows));
        // MT-12: the charge's first-contact casualties against the same men on foot
        var (mounted, foot, bonus) = MountedCalibration.ChargeBonus(fc, 200);
        Console.WriteLine($"12 v 20 skirmish, 200 seeds, foot casualties in the first {MountedCalibration.FirstPassS} s of contact (every rider's first pass): mounted {mounted:0.00}, the same men on foot {foot:0.00} (bonus {bonus:0}%, abstract charge_pm +60%)");
        return 0;
    }
}
