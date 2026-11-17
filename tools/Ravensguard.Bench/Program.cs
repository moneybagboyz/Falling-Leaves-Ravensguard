// Minimal Stopwatch harness (Phase 0 stub): times the melee combat resolver (a whole 10v10 skirmish) and LightGrid.Compute.
// Prints median / p95 per iteration. No allocation gates yet (those come with BenchmarkDotNet in a later phase).
//   dotnet run --project tools/Ravensguard.Bench -c Release [-- --iterations 20]
using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;
using Ravensguard.Sim.Lighting;

int iterations = 20;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--iterations" && int.TryParse(args[i + 1], out var n) && n > 0) iterations = n;

static string? FindRoot()
{
    var d = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (d != null && !File.Exists(Path.Combine(d.FullName, "Ravensguard.slnx"))) d = d.Parent;
    return d?.FullName;
}

static void Report(string name, string unit, List<double> samples)
{
    samples.Sort();
    double median = samples[samples.Count / 2], p95 = samples[Math.Min(samples.Count - 1, (int)Math.Ceiling(0.95 * samples.Count) - 1)];
    Console.WriteLine($"{name,-34} median {median,9:F3} {unit}   p95 {p95,9:F3} {unit}   (n={samples.Count})");
}

static List<double> Time(int iterations, Action warm, Func<double> run)
{
    warm();
    var s = new List<double>();
    for (int i = 0; i < iterations; i++) s.Add(run());
    return s;
}

var root = FindRoot() ?? throw new InvalidOperationException("Ravensguard.slnx not found above the working directory");
var load = new ModLoader(ContentTypeRegistry.Default).Load(new[] { Path.Combine(root, "content", "base") });
if (!load.Success) { Console.Error.WriteLine(load.Report.ToString()); return 1; }
var tables = new CombatTables(load.Database);

// 1. Combat resolver: whole 10v10 skirmish stepped to the end (or 60 s of game time), per-step cost.
int steps = 0;
double RunSkirmish(ulong seed)
{
    var w = MeleeScenarios.Skirmish(tables, seed, 10, emitText: false);
    var sw = Stopwatch.StartNew();
    int n = 0;
    while (!w.IsOver && w.TimeS < 60) { w.Step(); n++; }
    sw.Stop();
    steps = n;
    return sw.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, n); // microseconds per step
}
ulong seed = 1;
Report("melee skirmish 10v10 (per step)", "us", Time(iterations, () => RunSkirmish(0), () => RunSkirmish(seed++)));
Console.WriteLine($"  (last run: {steps} steps)");

// 2. LightGrid.Compute on a 256x256 single level with walls and 24 static lights plus a dynamic torch.
var grid = new LightGrid(256, 256, 0, 0);
for (int i = 0; i < 256; i++)
    for (int j = 0; j < 256; j++)
        if (i % 16 == 0 && j % 16 != 8 || j % 16 == 0 && i % 16 != 8) grid.SetOpaque(i, j, 0, true);
var statics = Enumerable.Range(0, 24).Select(k => new LightSource(20 + k * 9 % 220, 20 + k * 37 % 220, 0, 9, 200)).ToList();
grid.SetStaticLights(statics);
var torch = new List<LightSource> { new(128, 128, 0, 8, 220) };
var ambient = new Ambient(120, 0, default);
Report("LightGrid.Compute 256x256", "ms", Time(iterations, () => grid.Compute(ambient, torch), () =>
{
    var sw = Stopwatch.StartNew();
    grid.Compute(ambient, torch);
    return sw.Elapsed.TotalMilliseconds;
}));
return 0;
