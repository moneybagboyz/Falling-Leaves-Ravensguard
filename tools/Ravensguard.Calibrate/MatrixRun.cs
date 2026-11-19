// tools/calibrate --matrix (Phase 4 close-out item 2): every melee weapon and missile against six harnesses, on the trunk and through the armpit gap, through the shipped resolver,
// the wound book and the armour wear. A realism check judged against expectations (docs/reviews/phase-4-open.md item 2), not a balance target.
//   dotnet run --project tools/Ravensguard.Calibrate -c Release -- --matrix [--n 400] [--trials 100] [--only name] [--range 10]
using System.Text;
using Ravensguard.Sim.Combat.Melee2;

static class MatrixRun
{
    public static int Run(FightContent fc, string[] args, string? only)
    {
        int n = Arg(args, "--n", 400), trials = Arg(args, "--trials", 100), range = Arg(args, "--range", 10);
        var weapons = WeaponMatrix.Weapons(fc).Where(w => only == null || w.Name.Contains(only, StringComparison.Ordinal)).Select(w => w.How == WeaponMatrix.Delivery.Melee ? w : w with { RangeM = range }).ToList();
        var hs = WeaponMatrix.Harnesses;
        var cells = new (WeaponMatrix.Cell Trunk, WeaponMatrix.Cell Gap)[weapons.Count, hs.Count];
        Parallel.For(0, weapons.Count * hs.Count, k =>
        {
            int i = k / hs.Count, j = k % hs.Count;
            cells[i, j] = (WeaponMatrix.Run(fc, weapons[i], hs[j], false, n, trials), WeaponMatrix.Run(fc, weapons[i], hs[j], true, n, trials));
        });
        var world = new FightWorld(fc, 1);
        Console.WriteLine($"Weapon x armour matrix: {n} first blows and {trials} series per cell, skill 50 against 50, blows to the trunk (missiles at {range} m). Cell: cut% / bruise% / momentum reaching the body % / serious wounds per hit / median time to down (s, '-' = most still up after 60 s).");
        Console.WriteLine();
        foreach (bool gap in new[] { false, true })
        {
            Console.WriteLine(gap ? "### Called shot through the armpit (a blunt blow cannot slip a gap: it lands on the trunk)" : "### On the trunk");
            var sb = new StringBuilder();
            sb.Append("| weapon | cycle s |"); foreach (var h in hs) sb.Append($" {h.Name} |");
            if (!gap) sb.Append(" heater stops |");
            sb.AppendLine();
            sb.Append("|---|---|"); foreach (var _ in hs) sb.Append("---|"); if (!gap) sb.Append("---|"); sb.AppendLine();
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = weapons[i];
                sb.Append($"| {w.Name} | {WeaponMatrix.CycleTicks(world, fc, w) / 20.0:0.0} |");
                for (int j = 0; j < hs.Count; j++)
                {
                    var c = gap ? cells[i, j].Gap : cells[i, j].Trunk;
                    int md = c.MedianDownTicks;
                    sb.Append($" {c.CutPm / 10} / {c.BruisePm / 10} / {c.BodyPm / 10} / {c.SeriousX100 / 100.0:0.00} / {(md < 0 ? "-" : (md / 20.0).ToString("0.0"))} |");
                }
                if (!gap) sb.Append($" {cells[i, 0].Trunk.ShieldStoppedPm / 10}% |");
                sb.AppendLine();
            }
            Console.WriteLine(sb.ToString());
        }
        return 0;
    }

    static int Arg(string[] args, string name, int dflt)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) && v > 0 ? v : dflt;
    }
}
