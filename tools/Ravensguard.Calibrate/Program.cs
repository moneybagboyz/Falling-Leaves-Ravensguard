// tools/calibrate (resolver-v2 8, overview 9.2): the headless duel harness. Runs seeded AI-vs-AI duels per weapon and armour pairing through the
// spike's exchange loop with resolver v2 and prints duel length, defended share, severs per duel, the wound mix and the spear-vs-sword win rate,
// plus the metal ranking.
//   dotnet run --project tools/Ravensguard.Calibrate -c Release [-- --duels 200] [--v1]
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Resolver2;

int duels = 200; bool v1 = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--duels" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0) duels = n;
    if (args[i] == "--v1") v1 = true;
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
var rows = Calibration.DefaultPairings().Select(p => Calibration.Run(tables, p, duels, v2: !v1)).ToList();
var treated = Calibration.DefaultPairings().Select(p => Calibration.Run(tables, p, duels, v2: !v1, treat: true)).ToList();
Console.WriteLine($"{rows.Sum(r => r.N)} duels in {sw.Elapsed.TotalSeconds:0.0} s with resolver {(v1 ? "v1" : "v2")}");
Console.WriteLine();
Console.WriteLine(Calibration.Table(rows, treated));
Console.WriteLine($"Knocked out and bled out: {100.0 * rows.Sum(r => r.KoBledOut) / Math.Max(1, rows.Sum(r => r.KnockedOut)):0.0}% untreated, {100.0 * treated.Sum(r => r.KoBledOut) / Math.Max(1, treated.Sum(r => r.KnockedOut)):0.0}% with a medic (target <= 35%)");
Console.WriteLine("Metal ranking (outer-piece cut rate, permille, sword slashes vs leather, bronze mail, iron mail):");
Console.WriteLine(string.Join("  <  ", Calibration.MetalRanking(tables.V2).Select(x => $"{x.Metal} {x.CutsPerMille}")));
return 0;
