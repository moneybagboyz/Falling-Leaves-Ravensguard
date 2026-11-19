// tools/calibrate (resolver-v2 8, overview 9.2): the headless duel harness. Runs seeded AI-vs-AI duels per weapon and armour pairing through the
// spike's exchange loop with resolver v2 and prints duel length, defended share, severs per duel, the wound mix and the spear-vs-sword win rate,
// plus the metal ranking.
//   dotnet run --project tools/Ravensguard.Calibrate -c Release [-- --duels 200] [--only name] [--spike] [--v1]
// The default runs the P4-C fight (FightWorld); --spike runs the step-A harness on the Phase 0 spike loop, --v1 that with resolver v1.
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;

int duels = 200; bool v1 = false, spike = false; string? only = null; int trace = -1;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--duels" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0) duels = n;
    if (args[i] == "--v1") { v1 = true; spike = true; }
    if (args[i] == "--spike") spike = true;          // the step-A harness on the Phase 0 spike loop (kept for comparison)
    if (args[i] == "--only" && i + 1 < args.Length) only = args[i + 1];
    if (args[i] == "--trace" && i + 1 < args.Length) trace = int.Parse(args[i + 1]);
}
var d = new DirectoryInfo(Directory.GetCurrentDirectory());
while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
if (d == null) { Console.Error.WriteLine("run from inside the repository"); return 1; }
var load = new ModLoader(ContentTypeRegistry.Default).Load([Path.Combine(d.FullName, "content", "base")]);
if (!load.Success) { Console.Error.WriteLine(load.Report.ToString()); return 1; }
var sw = Stopwatch.StartNew();
var tables = new CombatTables(load.Database);
Console.WriteLine($"tables built in {sw.ElapsedMilliseconds} ms ({tables.V2.WeaponEntries} weapon and {tables.V2.ArmourEntries} armour entries)");
sw.Restart();
if (!spike)
{
    // P4-C: the shipped fight (FightWorld + CombatBrain + wound books + first aid)
    var fc = new Ravensguard.Sim.Combat.Melee2.FightContent(tables, load.Database);
    var pairs = Ravensguard.Sim.Combat.Melee2.FightCalibration.MainPairings().Where(p => only == null || p.Name.Contains(only, StringComparison.Ordinal)).ToList();
    if (args.Contains("--why"))
    {
        var p0 = pairs[0];
        var states = new int[32]; var kinds = new int[40];
        long[] blows = new long[2], mom = new long[2], pain = new long[2], hits = new long[2], wnd = new long[2];
        for (int i = 0; i < duels; i++)
        {
            var w = new Ravensguard.Sim.Combat.Melee2.FightWorld(fc, (ulong)(1000 + i), (ulong)(i + 1), 8 * 3600) { ImpactStates = states };
            w.OnBlow = (a, t, r, s) =>
            {
                blows[a.Team]++; mom[a.Team] += r.InitialMilli;
                if (r.WoundCount > 0) { hits[a.Team]++; wnd[a.Team] += r.WoundCount; foreach (var wd in r.WoundSpan) pain[a.Team] += Ravensguard.Sim.Combat.Wounds.WoundBook.PainOf(t.Plan, wd, Ravensguard.Sim.Combat.Wounds.WoundBook.Categorise(t.Plan, wd)); }
            };
            w.Add(Ravensguard.Sim.Combat.Melee2.FightCalibration.KitOf("A", 0, p0.WeaponA, p0.KitA, p0.ShieldA, Ravensguard.Sim.Combat.Melee.MeleeScenarios.SkillFor(i, 0), -3000, 1024));
            w.Add(Ravensguard.Sim.Combat.Melee2.FightCalibration.KitOf("B", 1, p0.WeaponB, p0.KitB, p0.ShieldB, Ravensguard.Sim.Combat.Melee.MeleeScenarios.SkillFor(i, 1), 3000, -1024));
            while (!w.IsOver && w.Tick < 4800) w.StepWithAi();
            foreach (var e in w.Events) kinds[(int)e.Kind]++;
        }
        Console.WriteLine("defender state at impact: " + string.Join(", ", Enumerable.Range(0, 15).Where(i => states[i] > 0).Select(i => $"{(Ravensguard.Sim.Combat.Melee2.FAct)i} {states[i]}")));
        for (int k = 0; k < 2; k++) Console.WriteLine($"side {k}: resolved {blows[k]}, mean M {mom[k] / Math.Max(1, blows[k]) / 1000.0:0.0}, wounding {hits[k]} ({100.0 * hits[k] / Math.Max(1, blows[k]):0}%), wounds/hit {(double)wnd[k] / Math.Max(1, hits[k]):0.0}, pain/hit {(double)pain[k] / Math.Max(1, hits[k]):0.0}");
        Console.WriteLine("idle by reaction (decided for this blow): " + string.Join(", ", Enumerable.Range(16, 5).Select(i => $"{i - 16}:{states[i]}")) + "  (other blow): " + string.Join(", ", Enumerable.Range(24, 5).Select(i => $"{i - 24}:{states[i]}")));
        Console.WriteLine("events: " + string.Join(", ", Enumerable.Range(0, 40).Where(i => kinds[i] > 0).Select(i => $"{(Ravensguard.Sim.Combat.Melee2.FightEventKind)i} {kinds[i]}")));
        return 0;
    }
    if (trace >= 0)
    {
        // one duel's events, for reading the fight
        var p0 = pairs[0];
        var w = new Ravensguard.Sim.Combat.Melee2.FightWorld(fc, (ulong)(1000 + trace), (ulong)(trace + 1), 8 * 3600);
        w.Add(Ravensguard.Sim.Combat.Melee2.FightCalibration.KitOf("A", 0, p0.WeaponA, p0.KitA, p0.ShieldA, Ravensguard.Sim.Combat.Melee.MeleeScenarios.SkillFor(trace, 0), -3000, 1024));
        w.Add(Ravensguard.Sim.Combat.Melee2.FightCalibration.KitOf("B", 1, p0.WeaponB, p0.KitB, p0.ShieldB, Ravensguard.Sim.Combat.Melee.MeleeScenarios.SkillFor(trace, 1), 3000, -1024));
        Ravensguard.Sim.Combat.Melee2.FightWorld.DebugIdle = true; w.ImpactStates = new int[32];
        Console.WriteLine($"{p0.Name}: skills {w.Fighters[0].Skill} / {w.Fighters[1].Skill}");
        while (!w.IsOver && w.Tick < 4800) w.StepWithAi();
        foreach (var e in w.Events) Console.WriteLine($"{e.Tick,5} {e.Kind,-12} {e.Actor,2} -> {e.Target,2} part {(e.Part >= 0 ? w.Fighters[Math.Max(0, e.Target)].Plan.Parts[e.Part].Id : "-"),-14} {e.A,6} {e.B,4}");
        foreach (var f in w.Fighters) Console.WriteLine($"{f.Name}: {f.Act} blood {f.Book.LossPm} pm lost, pain {f.Book.PainX100 / 100}, stamina {f.Stamina / 100}, thrown {f.Thrown} landed {f.Landed}");
        return 0;
    }
    var frows = pairs.Select(p => Ravensguard.Sim.Combat.Melee2.FightCalibration.Run(fc, p, duels)).ToList();
    Console.WriteLine($"{frows.Sum(r => r.N)} duels (x2 with first aid) in {sw.Elapsed.TotalSeconds:0.0} s on FightWorld");
    Console.WriteLine();
    Console.WriteLine(Ravensguard.Sim.Combat.Melee2.FightCalibration.Table(frows));
    return 0;
}
var rows = Calibration.DefaultPairings().Select(p => Calibration.Run(tables, p, duels, v2: !v1)).ToList();
var treated = Calibration.DefaultPairings().Select(p => Calibration.Run(tables, p, duels, v2: !v1, treat: true)).ToList();
Console.WriteLine($"{rows.Sum(r => r.N)} duels in {sw.Elapsed.TotalSeconds:0.0} s with resolver {(v1 ? "v1" : "v2")}");
Console.WriteLine();
Console.WriteLine(Calibration.Table(rows, treated));
Console.WriteLine($"Knocked out and bled out: {100.0 * rows.Sum(r => r.KoBledOut) / Math.Max(1, rows.Sum(r => r.KnockedOut)):0.0}% untreated, {100.0 * treated.Sum(r => r.KoBledOut) / Math.Max(1, treated.Sum(r => r.KnockedOut)):0.0}% with a medic (target <= 35%)");
Console.WriteLine("Metal ranking (outer-piece cut rate, permille, sword slashes vs leather, bronze mail, iron mail):");
Console.WriteLine(string.Join("  <  ", Calibration.MetalRanking(tables.V2).Select(x => $"{x.Metal} {x.CutsPerMille}")));
return 0;
