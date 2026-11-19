using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// Builds the wound tables from the live fight (abstract-battles-v1 3.2, 4.3; <c>tools/Ravensguard.Calibrate -- --abstract tables</c>): every cell is fought out in seeded
/// <see cref="FightWorld"/>s with the real brain, defence rolls (dodge, parry, block), resolver v2 and wound books, and every attack action of the cell's attacker on the cell's
/// defender is counted by what it did. Melee cells are duels 6 m apart (the attacker padded with a class member in hand, round-robin; the defender in the class's representative kit
/// with an iron sword and, by the flag, a round shield; skills around 40 with the band's delta); shooting cells are an archer loosing at a man standing at the band's range (his
/// shield up by the flag); the lance cells are a rider on a courser charging the defender from 40 m (mounted-combat's charge duel). Each cell is its own seeded run, so the build is
/// the same whatever the thread count, and the output text is byte-identical for the same content (AB-1).
/// </summary>
public static class WoundTableBuilder
{
    public sealed class Cell
    {
        public WeaponClass W; public ArmourClass A; public int Band; public bool Shield;
        public readonly long[] Outcome = new long[AbstractClasses.Outcomes];
        public long PainL, PainS, BleedL, BleedS;
        public readonly long[] Limb = new long[6];
        public readonly long[] Fx = new long[4];
        public readonly List<List<long>>[] Templates = [.. Enumerable.Range(0, AbstractClasses.Outcomes).Select(_ => new List<List<long>>())];
        public int N;
        public int Worlds;
        /// <summary>Fight ticks from each world's first attack action to its end (or the last counted action): the attack rate's denominator.</summary>
        public long Ticks;
    }

    /// <summary>Templates kept per cell and class (the library keeps <see cref="TemplatesPerClass"/> per weapon class, armour group and outcome class from all the group's cells).</summary>
    public const int TemplatesPerCell = 6, TemplatesPerClass = 24;

    private static ulong CellSeed(WeaponClass w, ArmourClass a, int band, bool shield, int i) =>
        DetRng.Mix(0xA8_7AB1E5UL ^ ((ulong)w << 40) ^ ((ulong)a << 32) ^ ((ulong)band << 24) ^ (shield ? 1UL << 20 : 0) ^ (ulong)i);

    /// <summary>Fights one cell until <paramref name="blows"/> attack actions are counted (or the attempts run out: a throw beyond throwing range is all miss).</summary>
    public static Cell BuildCell(FightContent c, WeaponClass wc, ArmourClass ac, int band, bool shield, int blows)
    {
        var cell = new Cell { W = wc, A = ac, Band = band, Shield = shield };
        var plan = c.T.Plan("human");
        int maxWorlds = Math.Max(20, blows);
        for (int i = 0; cell.N < blows && cell.Worlds < maxWorlds; i++)
        {
            cell.Worlds++;
            ulong seed = CellSeed(wc, ac, band, shield, i);
            int jit = (int)(DetRng.NextUInt(seed, 0, 1, 7, 0) % 11) - 5;
            FightWorld w; int att, def; int maxTicks;
            if (AbstractClasses.IsRanged(wc))
            {
                int range = AbstractClasses.RangeBandM[band] * (1000 + jit * 20) / 1000;
                w = new FightWorld(c, seed, seed, 8 * 3600) { RecordEvents = false };
                FighterKit shooter = wc == WeaponClass.Thrown
                    ? new FighterKit { Name = "A", Team = 0, Weapon = new WeaponInstance("dagger", "iron"), Throwables = [new ThrowStock("javelin", "iron", 3)], Worn = FightCalibration.Padded.Worn, Skill = 45 + jit, X = 0, FaceX = FightMath.One }
                    : new FighterKit { Name = "A", Team = 0, Launcher = wc == WeaponClass.Crossbow ? "crossbow" : "longbow", Ammo = wc == WeaponClass.Crossbow ? "quarrel" : i % 2 == 0 ? "bodkin_arrow" : "broadhead_arrow", AmmoCount = 12,
                        Worn = FightCalibration.Padded.Worn, Skill = 45 + jit, ArcherySkill = 45 + jit, X = 0, FaceX = FightMath.One };
                w.Add(shooter);
                var target = w.Add(new FighterKit { Name = "B", Team = 1, Weapon = new WeaponInstance("arming_sword", "iron"), Shield = shield ? "round_shield" : null, Worn = AbstractClasses.Kit(ac), Skill = 40, X = range * 1000, FaceX = -FightMath.One }, player: true);
                if (shield) target.Cmd.Block = true;
                att = 0; def = 1; maxTicks = 90 * 20;
            }
            else if (wc == WeaponClass.Lance)
            {
                w = new FightWorld(c, seed, seed, 8 * 3600) { RecordEvents = false };
                int delta = AbstractClasses.BandDelta(band), sd = Math.Clamp(40 - delta / 2 + jit, 0, 100), sa = Math.Clamp(sd + delta, 0, 100);
                w.AddMounted(FightCalibration.KitOf("A", 0, MountedCalibration.Lance, FightCalibration.Mailed, "heater", sa, -40_000, FightMath.One), new MountKit { Riding = 40 + sa / 3 });
                var foot = w.Add(FightCalibration.KitOf("B", 1, new WeaponInstance("arming_sword", "iron"), new FightCalibration.Kit(ac.ToString(), AbstractClasses.Kit(ac)), shield ? "round_shield" : null, sd, 0, -FightMath.One));
                att = 0; def = foot.Id; maxTicks = 60 * 20;
            }
            else
            {
                var members = AbstractClasses.Members(wc);
                var weapon = members[i % members.Length];
                int delta = AbstractClasses.BandDelta(band), sd = Math.Clamp(40 - delta / 2 + jit, 0, 100), sa = Math.Clamp(sd + delta, 0, 100);
                w = new FightWorld(c, seed, seed, 8 * 3600) { RecordEvents = false };
                w.Add(FightCalibration.KitOf("A", 0, weapon, FightCalibration.Padded, null, sa, -3000, FightMath.One));
                w.Add(FightCalibration.KitOf("B", 1, new WeaponInstance("arming_sword", "iron"), new FightCalibration.Kit(ac.ToString(), AbstractClasses.Kit(ac)), shield ? "round_shield" : null, sd, 3000, -FightMath.One));
                att = 0; def = 1; maxTicks = 90 * 20;
            }
            bool shooting = AbstractClasses.IsRanged(wc);
            int shots = 0, resolved = 0, firstTick = -1, lastTick = -1;
            void Count(OutcomeClass o) { if (cell.N < blows) { cell.Outcome[(int)o]++; cell.N++; if (firstTick < 0) firstTick = w.Tick; lastTick = w.Tick; } }
            w.OnEvent = e =>
            {
                if (shooting)
                {
                    if (e.Actor == att && e.Kind is FightEventKind.Shot or FightEventKind.Throw) shots++;
                    return;
                }
                if (e.Kind == FightEventKind.Whiff && e.Actor == att) Count(OutcomeClass.Miss);
                else if (e.Kind is FightEventKind.Dodged or FightEventKind.Parried && e.Actor == def && e.Target == att) Count(OutcomeClass.Miss);
            };
            w.OnBlow = (a, t, r, s) =>
            {
                if (a.Id != att || t.Id != def || shooting != s.Missile) return;
                if (cell.N >= blows) return;
                if (shooting) resolved++;
                if (!r.Hit && !(r.Blocked && r.WoundCount > 0) || r.Blocked && r.WoundCount == 0) { Count(OutcomeClass.Miss); return; }
                var o = AbstractClasses.Classify(t.Plan, r.WoundSpan);
                Count(o);
                if (o < OutcomeClass.Light) return;
                long pain = 0, bleed = 0;
                foreach (var wd in r.WoundSpan) { bleed += wd.BleedX10; pain += (long)Wounds.WoundBook.PainOf(plan, wd, Wounds.WoundBook.Categorise(plan, wd)) * plan.Parts[wd.Part].PainPm / 1000; }
                if (o == OutcomeClass.Light) { cell.PainL += pain; cell.BleedL += bleed; }
                else if (o == OutcomeClass.Serious) { cell.PainS += pain; cell.BleedS += bleed; }
                cell.Limb[AbstractClasses.Region(plan, r.WoundSpan[0].Part)]++;
                if (o >= OutcomeClass.Grave) { int fx = AbstractClasses.Fx(plan, r.WoundSpan); if (fx >= 0) cell.Fx[fx]++; }
                var tl = cell.Templates[(int)o];
                if (tl.Count < TemplatesPerCell) tl.Add(WoundTables.Encode(r.WoundSpan, Wounds.WoundBook.SkullMomentum(r)));
            };
            while (!w.IsOver && w.Tick < maxTicks && cell.N < blows)
            {
                w.StepWithAi();
                if (shooting && !w.Fighters[def].Active) break;
                if (shooting && w.Projectiles.Count == 0 && w.Fighters[att] is { } sh && (wc == WeaponClass.Thrown ? sh.Throwables.Count == 0 : sh.Ammo <= 0 && !sh.Loaded)) break;
            }
            if (firstTick >= 0) cell.Ticks += Math.Max(1, (cell.N >= blows ? lastTick : w.Tick) - firstTick + 1);
            if (shooting)
            {
                // the rest of the shots loosed this world missed (fell short, flew wide, or were still in the air when the target went down)
                int misses = Math.Max(0, shots - resolved);
                for (int k = 0; k < misses; k++) Count(OutcomeClass.Miss);
            }
        }
        return cell;
    }

    /// <summary>Every cell of the tables (the ten weapon classes x seven armour classes x seven bands x shield), built in parallel; the result is in cell order.</summary>
    public static List<Cell> BuildAll(FightContent c, int blows, Func<WeaponClass, ArmourClass, int, bool, bool>? only = null, Action<int, int>? progress = null)
    {
        var keys = new List<(WeaponClass, ArmourClass, int, bool)>();
        for (int w = 0; w < AbstractClasses.WeaponClasses; w++)
            for (int a = 0; a < AbstractClasses.ArmourClasses; a++)
                for (int b = 0; b < AbstractClasses.Bands; b++)
                    for (int s = 0; s < 2; s++)
                        if (only == null || only((WeaponClass)w, (ArmourClass)a, b, s == 1)) keys.Add(((WeaponClass)w, (ArmourClass)a, b, s == 1));
        var cells = new Cell[keys.Count];
        int done = 0;
        Parallel.For(0, keys.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, i =>
        {
            var (w, a, b, s) = keys[i];
            cells[i] = BuildCell(c, w, a, b, s, blows);
            int d = Interlocked.Increment(ref done);
            progress?.Invoke(d, keys.Count);
        });
        return [.. cells];
    }

    /// <summary>A cell's outcome permille, summing to exactly 1000 (largest remainder, ties to the lower class).</summary>
    public static int[] Permille(Cell c)
    {
        var pm = new int[AbstractClasses.Outcomes];
        if (c.N == 0) { pm[0] = 1000; return pm; }
        var rem = new (long R, int I)[pm.Length];
        int sum = 0;
        for (int i = 0; i < pm.Length; i++) { pm[i] = (int)(c.Outcome[i] * 1000 / c.N); rem[i] = (c.Outcome[i] * 1000 % c.N, i); sum += pm[i]; }
        foreach (var (_, i) in rem.OrderByDescending(x => x.R).ThenBy(x => x.I).Take(1000 - sum)) pm[i]++;
        return pm;
    }

    private static int[] Shares(long[] x)
    {
        long t = x.Sum();
        return [.. x.Select(v => t == 0 ? 0 : (int)(v * 1000 / t))];
    }

    /// <summary>The generated content (3.2): <c>wound_tables.json</c> (the header and one row per weapon and armour class) and <c>wound_templates.json</c>, as text with a fixed layout.</summary>
    public static (string Tables, string Templates) Write(IReadOnlyList<Cell> cells, string hash, int blows)
    {
        var t = new StringBuilder();
        t.Append("// Generated by tools/Ravensguard.Calibrate -- --abstract tables (abstract-battles-v1 3.2). Do not edit: rebuild when the content hash changes (test AB-1).\n");
        t.Append("// Each cell: band, shield, n, apm_x10, outcome x 7 (miss, nothing, light, serious, grave, severed, instant: permille), mean pain light and serious, mean bleed x10 light and\n");
        t.Append("// serious, limb mix x 6 (arm, leg, torso, head, neck, hand), fx mix x 4 (fracture, nerve, organ, artery). See WoundTableDef.\n[\n");
        t.Append($"  {{ \"type\": \"wound_table_meta\", \"id\": \"default\", \"content_hash\": \"{hash}\", \"blows_per_cell\": {blows}, \"version\": {WoundTables.BuilderVersion} }}");
        var u = new StringBuilder();
        u.Append("// Generated with wound_tables.json: recorded wounds by weapon class, armour class and outcome class; each [n, n x (part, tissue, structure, organ, kind, depth_pm, grade, count, residual, bleed_x10, flags), skull_milli].\n[\n");
        bool firstU = true;
        foreach (var g in cells.GroupBy(c => (c.W, c.A)).OrderBy(g => (int)g.Key.W).ThenBy(g => (int)g.Key.A))
        {
            string wn = AbstractClasses.Name(g.Key.W), an = AbstractClasses.Name(g.Key.A);
            t.Append(",\n");
            t.Append($"  {{ \"type\": \"wound_table\", \"id\": \"{wn}.{an}\", \"weapon\": \"{wn}\", \"armour\": \"{an}\", \"cells\": [");
            bool first = true;
            foreach (var c in g.OrderBy(c => c.Band).ThenBy(c => c.Shield))
            {
                t.Append(first ? "\n" : ",\n"); first = false;
                long nl = c.Outcome[(int)OutcomeClass.Light], ns = c.Outcome[(int)OutcomeClass.Serious];
                long apm = c.Ticks == 0 ? 0 : (long)c.N * 20 * 60 * 10 / c.Ticks;
                var row = new List<long> { c.Band, c.Shield ? 1 : 0, c.N, apm };
                row.AddRange(Permille(c).Select(x => (long)x));
                row.AddRange([nl == 0 ? 0 : c.PainL / nl, ns == 0 ? 0 : c.PainS / ns, nl == 0 ? 0 : c.BleedL / nl, ns == 0 ? 0 : c.BleedS / ns]);
                row.AddRange(Shares(c.Limb).Select(x => (long)x)); row.AddRange(Shares(c.Fx).Select(x => (long)x));
                t.Append("    [" + string.Join(",", row) + "]");
            }
            t.Append(" ] }");

        }
        // the library: per weapon class, armour group and outcome class up to TemplatesPerClass, evenly taken from the group's cells in cell order
        foreach (var g in cells.GroupBy(c => (c.W, G: AbstractClasses.Group(c.A))).OrderBy(g => (int)g.Key.W).ThenBy(g => g.Key.G))
        {
            string wn = AbstractClasses.Name(g.Key.W), gn = AbstractClasses.GroupName(g.Key.G);
            var by = new SortedDictionary<int, List<List<long>>>();
            for (int o = (int)OutcomeClass.Light; o < AbstractClasses.Outcomes; o++)
            {
                var all = g.OrderBy(c => (int)c.A).ThenBy(c => c.Band).ThenBy(c => c.Shield).SelectMany(c => c.Templates[o]).ToList();
                if (all.Count == 0) continue;
                int k = Math.Min(TemplatesPerClass, all.Count);
                by[o] = [.. Enumerable.Range(0, k).Select(j => all[(int)((long)j * all.Count / k)])];
            }
            if (by.Count == 0) continue;
            u.Append(firstU ? "" : ",\n"); firstU = false;
            u.Append($"  {{ \"type\": \"wound_templates\", \"id\": \"{wn}.{gn}\", \"weapon\": \"{wn}\", \"armour\": \"{gn}\", \"by_class\": {{");
            bool fo = true;
            foreach (var (o, list) in by)
            {
                u.Append(fo ? "\n    " : ",\n    "); fo = false;
                u.Append($"\"{AbstractClasses.Name((OutcomeClass)o)}\": [{string.Join(",", list.Select(l => "[" + string.Join(",", l) + "]"))}]");
            }
            u.Append(" } }");
        }
        t.Append("\n]\n");
        u.Append("\n]\n");
        return (t.ToString(), u.ToString());
    }
}
