using System.Text;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using static Ravensguard.Sim.Combat.Abstract.AbstractScenarios;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The equivalence harness (abstract-battles-v1 4.3, 4.4; tests AB-3, AB-4, AB-11; <c>tools/Ravensguard.Calibrate -- --abstract equiv</c>): the same scenario and seed fought live
/// (<see cref="AbstractScenarios.Tactical"/>, a headless <see cref="FightWorld"/> to its end, then the aftermath's bleeding untreated) and abstractly (<see cref="AbstractBattle"/> on
/// the same kits), each summed into a <see cref="Tally"/>. Casualties are the dead and the down at the end of the fight (4.4); the wound-class mix is each casualty's worst blow by
/// the tables' classifier; the bleed-out share is the casualties who died in the two minutes after. Integer sums; means are x1000.
/// </summary>
public static class AbstractEquivalence
{
    /// <summary>One fight's result: the winner (-1 none), its length in sim-seconds x10, per side the start, casualties, fled, yielded, the worst-class counts and the dead after.</summary>
    public sealed record Result(int Winner, int DurationX10, int[] Start, int[] Casualties, int[] Fled, int[] Yielded, int[][] Worst, int[] DiedAfter, int[] FirstPass);

    public const int AftermathS = 120;

    /// <summary>The live fight of a scenario to its end (or 600 s), then two minutes of untreated bleeding.</summary>
    public static Result Tactical(FightContent c, Scenario s, ulong seed) => TacticalOf(AbstractScenarios.Tactical(c, s, seed));

    /// <summary>The live fight of any prepared world (the Phase 7 scenarios E to H use this) to its end, then the untreated aftermath.</summary>
    public static Result TacticalOf(FightWorld w)
    {
        var worst = new OutcomeClass[w.Fighters.Count];
        int contact = -1;
        w.OnBlow = (a, t, r, st) =>
        {
            if (r.WoundCount == 0) return;
            var o = AbstractClasses.Classify(t.Plan, r.WoundSpan);
            if (o > worst[t.Id]) worst[t.Id] = o;
            if (contact < 0 && t.Team == 1) contact = w.Tick;
        };
        int[] firstPass = new int[2];
        while (!w.IsOver && w.Tick < 600 * 20)
        {
            w.StepWithAi();
            if (contact >= 0 && w.Tick == contact + 10 * 20)
                foreach (var f in w.Fighters) if (!w.IsHorse(f) && f.Act is FAct.Down or FAct.Dead) firstPass[f.Team]++;
        }
        if (contact >= 0 && w.Tick < contact + 10 * 20)
            foreach (var f in w.Fighters) if (!w.IsHorse(f) && f.Act is FAct.Down or FAct.Dead) firstPass[f.Team]++;
        int dur = w.Tick * 10 / 20;
        var r = new Result(w.DecidedWinner, dur, new int[2], new int[2], new int[2], new int[2], [new int[7], new int[7]], new int[2], firstPass);
        var casualty = new bool[w.Fighters.Count];
        foreach (var f in w.Fighters)
        {
            if (w.IsHorse(f) || f.Team is < 0 or > 1) continue;
            r.Start[f.Team]++;
            if (f.Act is FAct.Down or FAct.Dead) { r.Casualties[f.Team]++; r.Worst[f.Team][(int)worst[f.Id]]++; casualty[f.Id] = true; }
            else if (f.Act == FAct.Fled || f.Routing) r.Fled[f.Team]++;
            else if (f.Act == FAct.Surrendered) r.Yielded[f.Team]++;
        }
        long end = w.GameSec + AftermathS * 20L;
        foreach (var f in w.Fighters)
        {
            if (w.IsHorse(f) || f.Team is < 0 or > 1 || f.Book.Dead) continue;
            f.Book.AdvanceTo(end);
            if (f.Book.Dead && casualty[f.Id]) r.DiedAfter[f.Team]++;
        }
        return r;
    }

    /// <summary>The input of the abstract battle of a scenario and seed: the same kits, riders, terrain and leaders; no first aid (the aftermath's bleeding compared untreated).</summary>
    public static BattleInput InputOf(Scenario s, ulong seed)
    {
        var kits = Kits(s, seed, out var mounted, out var terrain);
        return new BattleInput(kits, seed, seed, 8 * 3600, "scenario " + s) { Mounted = mounted, Terrain = terrain, Leaders = s != Scenario.ANoLeader, FirstAid = false };
    }

    public static Result Abstract(FightContent c, WoundTables t, Scenario s, ulong seed, Content.AbstractBattleDef? k = null) => AbstractOf(c, t, InputOf(s, seed), k);

    /// <summary>The abstract battle of any input (no aftermath rows: the aftermath's bleeding is compared untreated).</summary>
    public static Result AbstractOf(FightContent c, WoundTables t, BattleInput input, Content.AbstractBattleDef? k = null)
    {
        var b = new AbstractBattle(c, t, input with { Aftermath = false }, k);
        while (!b.Over) b.StepOnce();
        int[] firstPass = new int[2];
        int cs = b.ContactStep;
        foreach (var x in b.Soldiers)
            if (x.OutStep >= 0 && cs >= 0 && x.OutStep * b.K.StepS < cs * b.K.StepS + 10 + b.K.StepS && x.Status is SoldierStatus.Down or SoldierStatus.Dead) firstPass[x.Side]++;
        var r = new Result(b.Winner, b.Step * b.K.StepS * 10, new int[2], new int[2], new int[2], new int[2], [new int[7], new int[7]], new int[2], firstPass);
        var casualty = new bool[b.Soldiers.Count];
        foreach (var x in b.Soldiers)
        {
            r.Start[x.Side]++;
            if (x.Status is SoldierStatus.Down or SoldierStatus.Dead) { r.Casualties[x.Side]++; r.Worst[x.Side][(int)x.Worst]++; casualty[x.Index] = true; }
            else if (x.Status == SoldierStatus.Fled || x.Routing) r.Fled[x.Side]++;
            else if (x.Status == SoldierStatus.Surrendered) r.Yielded[x.Side]++;
        }
        long end = b.Input.GameSec + (long)b.Step * b.K.StepS * 20 + AftermathS * 20L;
        foreach (var x in b.Soldiers)
        {
            if (x.Book.Dead) continue;
            x.Book.AdvanceTo(end);
            if (x.Book.Dead && casualty[x.Index]) r.DiedAfter[x.Side]++;
        }
        return r;
    }

    /// <summary>Sums of a run of fights (means x1000 by <see cref="Mean"/>).</summary>
    public sealed class Tally
    {
        public int N, Wins0, Wins1;
        public long Duration;
        public readonly long[] Yielded = new long[2], Casualties = new long[2], Fled = new long[2], Start = new long[2], DiedAfter = new long[2], FirstPass = new long[2];
        public readonly long[][] Worst = [new long[7], new long[7]];

        public void Add(Result r)
        {
            N++; if (r.Winner == 0) Wins0++; else if (r.Winner == 1) Wins1++;
            Duration += r.DurationX10;
            for (int s = 0; s < 2; s++)
            {
                Casualties[s] += r.Casualties[s]; Yielded[s] += r.Yielded[s]; Fled[s] += r.Fled[s]; Start[s] += r.Start[s]; DiedAfter[s] += r.DiedAfter[s]; FirstPass[s] += r.FirstPass[s];
                for (int o = 0; o < 7; o++) Worst[s][o] += r.Worst[s][o];
            }
        }

        public long Mean(long sum) => N == 0 ? 0 : sum * 1000 / N;
        /// <summary>Side 0's win rate, permille.</summary>
        public int WinPm => N == 0 ? 0 : Wins0 * 1000 / N;
        public long MeanDurationX10 => N == 0 ? 0 : Duration / N;
        /// <summary>The wound-class mix of both sides' casualties: light, serious, grave, severed (with instant), permille.</summary>
        public int[] Mix()
        {
            long[] m = new long[4];
            for (int s = 0; s < 2; s++) { m[0] += Worst[s][2] + Worst[s][1] + Worst[s][0]; m[1] += Worst[s][3]; m[2] += Worst[s][4]; m[3] += Worst[s][5] + Worst[s][6]; }
            long t = m.Sum();
            return [.. m.Select(x => t == 0 ? 0 : (int)(x * 1000 / t))];
        }
        /// <summary>Of the casualties, the share who bled to death in the aftermath, permille.</summary>
        public int BleedOutPm { get { long c = Casualties[0] + Casualties[1]; return c == 0 ? 0 : (int)((DiedAfter[0] + DiedAfter[1]) * 1000 / c); } }
    }

    public static Tally RunTactical(FightContent c, Scenario s, int seed0, int n)
    {
        var res = new Result[n];
        Parallel.For(0, n, i => res[i] = Tactical(c, s, (ulong)(seed0 + i)));
        var t = new Tally();
        foreach (var r in res) t.Add(r);
        return t;
    }

    public static Tally RunTacticalOf(Func<ulong, FightWorld> make, int seed0, int n)
    {
        var res = new Result[n];
        Parallel.For(0, n, i => res[i] = TacticalOf(make((ulong)(seed0 + i))));
        var t = new Tally();
        foreach (var r in res) t.Add(r);
        return t;
    }

    public static Tally RunAbstractOf(FightContent c, WoundTables tables, Func<ulong, BattleInput> make, int seed0, int n, Content.AbstractBattleDef? k = null)
    {
        var res = new Result[n];
        Parallel.For(0, n, i => res[i] = AbstractOf(c, tables, make((ulong)(seed0 + i)), k));
        var t = new Tally();
        foreach (var r in res) t.Add(r);
        return t;
    }

    public static Tally RunAbstract(FightContent c, WoundTables tables, Scenario s, int seed0, int n, Content.AbstractBattleDef? k = null)
    {
        var res = new Result[n];
        Parallel.For(0, n, i => res[i] = Abstract(c, tables, s, (ulong)(seed0 + i), k));
        var t = new Tally();
        foreach (var r in res) t.Add(r);
        return t;
    }

    /// <summary>The gate of 4.4 for one scenario: casualties per side within +-10% (or 0.5 of a man when the mean is small), win rate within 10 points, duration within 25%, the
    /// wound-class mix within 10% relative (3 points absolute for a class under 5%, B1). Returns the failures (empty: equivalent).</summary>
    public static List<string> Compare(Tally tac, Tally abs, string name)
    {
        var bad = new List<string>();
        for (int s = 0; s < 2; s++)
        {
            long a = abs.Mean(abs.Casualties[s]), t = tac.Mean(tac.Casualties[s]);
            if (Math.Abs(a - t) * 1000 > 100 * Math.Max(t, 5000) && Math.Abs(a - t) > 500) bad.Add($"{name}: side {s} casualties {a / 1000.0:0.00} vs {t / 1000.0:0.00}");
        }
        if (Math.Abs(abs.WinPm - tac.WinPm) > 100) bad.Add($"{name}: win rate {abs.WinPm / 10}% vs {tac.WinPm / 10}%");
        if (Math.Abs(abs.MeanDurationX10 - tac.MeanDurationX10) * 4 > tac.MeanDurationX10) bad.Add($"{name}: duration {abs.MeanDurationX10 / 10.0:0.0} s vs {tac.MeanDurationX10 / 10.0:0.0} s");
        int[] ma = abs.Mix(), mt = tac.Mix();
        for (int i = 0; i < 4; i++)
        {
            int tol = mt[i] < 50 ? 30 : Math.Max(30, mt[i] / 10);
            if (Math.Abs(ma[i] - mt[i]) > tol) bad.Add($"{name}: wound mix [{i}] {ma[i]} vs {mt[i]} permille");
        }
        return bad;
    }

    /// <summary>A tally as one line of integers (the stored tactical reference): n, wins 0, wins 1, duration, then per side casualties, fled, start, died after, first pass and the 7 worst-class counts.</summary>
    public static long[] Pack(Tally t)
    {
        var l = new List<long> { t.N, t.Wins0, t.Wins1, t.Duration };
        for (int s = 0; s < 2; s++) { l.AddRange([t.Casualties[s], t.Fled[s], t.Start[s], t.DiedAfter[s], t.FirstPass[s]]); l.AddRange(t.Worst[s]); }
        return [.. l];
    }

    public static Tally Unpack(long[] v)
    {
        var t = new Tally { N = (int)v[0], Wins0 = (int)v[1], Wins1 = (int)v[2] };
        t.Duration = v[3];
        int i = 4;
        for (int s = 0; s < 2; s++)
        {
            t.Casualties[s] = v[i++]; t.Fled[s] = v[i++]; t.Start[s] = v[i++]; t.DiedAfter[s] = v[i++]; t.FirstPass[s] = v[i++];
            for (int o = 0; o < 7; o++) t.Worst[s][o] = v[i++];
        }
        return t;
    }

    public static string Code(Tally t) => "[" + string.Join(", ", Pack(t)) + "]";

    public static string Row(string name, Tally t) =>
        $"| {name} | {t.N} | {t.Mean(t.Casualties[0]) / 1000.0:0.00} | {t.Mean(t.Casualties[1]) / 1000.0:0.00} | {t.Mean(t.Fled[0]) / 1000.0:0.0} | {t.Mean(t.Fled[1]) / 1000.0:0.0} | {t.WinPm / 10.0:0.0}% | {t.MeanDurationX10 / 10.0:0.0} | {string.Join(" / ", t.Mix())} | {t.BleedOutPm / 10.0:0.0}% |";

    public const string Header = "| Scenario | n | casualties 0 | casualties 1 | fled 0 | fled 1 | side 0 wins | mean s | mix light/serious/grave/severed (pm) | bled out after |\n|---|---|---|---|---|---|---|---|---|---|";
}
