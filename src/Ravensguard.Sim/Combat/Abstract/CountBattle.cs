using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>What a count battle came to (abstract-war 4.4): per side and class the start, the casualties (dead, crippled, recovering), those who fled, the prisoners and those still standing.</summary>
public sealed class CountResult
{
    public int Winner = -1, DurationS, Steps;
    public readonly int[][] Start = [new int[5], new int[5]], Dead = [new int[5], new int[5]], Crippled = [new int[5], new int[5]], Recovering = [new int[5], new int[5]];
    public readonly int[][] Fled = [new int[5], new int[5]], Captured = [new int[5], new int[5]], Standing = [new int[5], new int[5]];
    public int Casualties(int side) { int n = 0; for (int c = 0; c < 5; c++) n += Dead[side][c] + Crippled[side][c] + Recovering[side][c]; return n; }
    public int Total(int[][] a, int side) { int n = 0; for (int c = 0; c < 5; c++) n += a[side][c]; return n; }
    public int DeadOf(int side) => Total(Dead, side);
    public int CrippledOf(int side) => Total(Crippled, side);
    /// <summary>Every man accounted for: start = casualties + fled + captured + standing, per side.</summary>
    public bool Conserves() { for (int s = 0; s < 2; s++) if (Total(Start, s) != Casualties(s) + Total(Fled, s) + Total(Captured, s) + Total(Standing, s)) return false; return true; }
}

/// <summary>
/// The count battle (abstract-war 4.4): two sides of five class counts fight a square-law exchange in short steps, by the fitted <see cref="CountBattleDef"/>. Each step a side's engaged
/// men of each class put casualties on the other side's classes (shared by exposure), the side's casualty share against each class's threshold breaks men into routers, routers are
/// struck by the winner's pursuit and leave after a few seconds, and the fight is over when a side has none left in the line. Casualties are split into dead, crippled and recovering by
/// the table's mix. Integer math throughout; the rounding draws are stream 0x7E2 keyed on the battle, step and class. About 0.05 ms a battle.
/// </summary>
public static class CountBattle
{
    private const int C = 5;
    /// <summary>A scalar of the table, 0 when an older row has no such entry.</summary>
    private static int Kv(CountBattleDef d, int i) => i < d.K.Length ? d.K[i] : 0;

    /// <param name="men">Each side's standing men by class (levy, foot, missile, horse, elite).</param>
    /// <param name="qualityPm">Each side's quality by class, permille (1000 = the calibration kit; from the realm's armoury class and drill).</param>
    /// <param name="moralePm">Each side's morale, permille (1000 normal).</param>
    public static CountResult Fight(CountBattleDef d, ulong seed, ulong battleId, int[][] men, int[][]? qualityPm = null, int[]? moralePm = null)
    {
        var r = new CountResult();
        int[][] n = [(int[])men[0].Clone(), (int[])men[1].Clone()];
        int[][] rt = [new int[C], new int[C]], cas = [new int[C], new int[C]];
        int[][] fled = [new int[C], new int[C]], capt = [new int[C], new int[C]];
        int[] start = [n[0].Sum(), n[1].Sum()];
        for (int s = 0; s < 2; s++) Array.Copy(n[s], r.Start[s], C);
        int[] q = new int[2 * C], lk = [1000, 1000], mor = [moralePm?[0] ?? 1000, moralePm?[1] ?? 1000];
        for (int s = 0; s < 2; s++) for (int c = 0; c < C; c++) q[s * C + c] = qualityPm?[s][c] ?? 1000;
        ulong key = battleId * 64;
        int luck = d.K[CountK.LuckPm];
        for (int s = 0; s < 2; s++) lk[s] = 1000 + (luck == 0 ? 0 : DetRng.Range(seed, 0, key + (ulong)s, RngStreams.CountBattleRounding, 99, -luck, luck + 1));
        int stepS = Math.Max(1, d.K[CountK.StepS]), steps = 0;
        long[] lossMilli = new long[C];
        int[] standing = new int[2];
        var wExpo = new long[2];
        long[] panic = new long[2];
        int[] casBefore = new int[2];
        long[][] shock = [new long[C], new long[C]];
        int[][] newRt = [new int[C], new int[C]];
        for (int step = 0; step < d.K[CountK.MaxSteps]; step++)
        {
            standing[0] = n[0].Sum(); standing[1] = n[1].Sum();
            int routers0 = rt[0].Sum(), routers1 = rt[1].Sum();
            if (standing[0] == 0 && standing[1] == 0 && routers0 + routers1 == 0) break;
            if ((standing[0] == 0 || standing[1] == 0) && routers0 + routers1 == 0) break;
            steps = step + 1;
            int t = step * stepS;
            casBefore[0] = cas[0].Sum(); casBefore[1] = cas[1].Sum();
            // the exchange: each side's casualties from the other's engaged men
            for (int s = 0; s < 2; s++)
            {
                wExpo[s] = 0;
                for (int c = 0; c < C; c++) wExpo[s] += (long)n[s][c] * d.Expo[c];
            }
            if (standing[0] > 0 && standing[1] > 0)
                for (int s = 0; s < 2; s++)
                {
                    int e = 1 - s;                                                    // the attacker
                    if (wExpo[s] == 0) continue;
                    for (int c = 0; c < C; c++) lossMilli[c] = 0;
                    for (int k = 0; k < C; k++)
                    {
                        long share = d.Share[k];
                        if (k == 2 && t >= d.K[CountK.AmmoS]) share = d.K[CountK.AmmoLowPm];
                        long eng = n[e][k] * share / 1000;
                        if (eng == 0) continue;
                        long mult = (long)q[e * C + k] * lk[e] / 1000;                // permille
                        if (k == 3 && step < d.K[CountK.ChargeSteps]) mult = mult * d.K[CountK.ChargePm] / 1000;
                        long power = eng * mult / 1000 * stepS / 60;                 // engaged man-minutes x1000/1000
                        for (int c = 0; c < C; c++)
                        {
                            if (n[s][c] == 0) continue;
                            long beta = d.Beta[k * C + c];
                            if (beta == 0) continue;
                            long qd = Math.Max(200, q[s * C + c]);
                            lossMilli[c] += power * beta * n[s][c] * d.Expo[c] / wExpo[s] * 1000 / qd;
                        }
                    }
                    for (int c = 0; c < C; c++)
                    {
                        long l = lossMilli[c];
                        int whole = (int)(l / 1000), rem = (int)(l % 1000);
                        if (rem > 0 && DetRng.Range(seed, step, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, 0, 0, 1000) < rem) whole++;
                        whole = Math.Min(whole, n[s][c]);
                        n[s][c] -= whole; cas[s][c] += whole;
                    }
                }
            // the break: a class breaks as its side's casualty share passes its threshold
            for (int s = 0; s < 2; s++)
            {
                if (start[s] == 0) continue;
                int totalCas = cas[s].Sum();
                // the cascade: this step's losses raise the panic, which spreads among the neighbours and fades
                long stepLoss10k = (long)(totalCas - casBefore[s]) * 10000 / start[s];
                panic[s] += stepLoss10k * Kv(d, CountK.PanicGain) / 100;
                panic[s] += panic[s] * (1000 - Math.Min(1000, panic[s])) / 1000 * Kv(d, CountK.PanicSpread) / 1000;
                panic[s] = Math.Clamp(panic[s] - panic[s] * Kv(d, CountK.PanicDecay) / 1000, 0, 1000);
                int lpm = (int)((long)totalCas * 1000 / start[s]);
                lpm += (int)panic[s];
                for (int c = 0; c < C; c++) newRt[s][c] = 0;
                int ratioAdd = 0;
                if (d.K[CountK.RatioPm] != 0 && standing[s] > 0 && standing[1 - s] > 0) ratioAdd = (int)((long)d.K[CountK.RatioPm] * (cas[s].Sum() - cas[1 - s].Sum()) / Math.Max(1, start[s]) * 1000 / 1000);
                bool flanked = d.K[CountK.FlankedPm] != 0 && n[1 - s].Sum() * 2 > n[s].Sum() * 3;
                for (int c = 0; c < C; c++)
                {
                    if (n[s][c] == 0) continue;
                    long thr = (long)d.RoutThr[c] * mor[s] / 1000 * d.K[CountK.MoraleScalePm] / 1000;
                    if (flanked) thr = thr * (1000 - d.K[CountK.FlankedPm]) / 1000;
                    long over = lpm + ratioAdd + shock[s][c] - thr;
                    if (over <= 0) continue;
                    long hz = Math.Min(d.K[CountK.RoutMaxPm], over * d.K[CountK.RoutSlope] / 1000);
                    long m = n[s][c] * hz;
                    int whole = (int)(m / 1000), rem = (int)(m % 1000);
                    if (rem > 0 && DetRng.Range(seed, step, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, 1, 0, 1000) < rem) whole++;
                    whole = Math.Min(whole, n[s][c]);
                    n[s][c] -= whole; rt[s][c] += whole; newRt[s][c] = whole;
                }
                CascadeUpdate(d, n, shock[s], newRt[s], 1 - s, start[s]);
                if ((long)n[s].Sum() * 1000 <= (long)start[s] * d.K[CountK.BreakStandingPm] && n[s].Sum() > 0 && n[1 - s].Sum() > 0 && d.K[CountK.BreakStandingPm] > 0)
                    for (int c = 0; c < C; c++) { rt[s][c] += n[s][c]; n[s][c] = 0; }
            }
            // pursuit and flight: the winner's men strike routers, the rest get away (a few are taken)
            for (int s = 0; s < 2; s++)
            {
                int e = 1 - s; int en = n[e].Sum();
                int flee = Math.Max(1, (d.K[CountK.FleeS] + stepS - 1) / stepS);
                long horse = en == 0 ? 0 : (long)n[e][3] * 1000 / en;
                long pk = en == 0 ? 0 : (long)d.K[CountK.PursuitPm] * (1000 + d.K[CountK.PursuitHorsePm] * horse / 1000) / 1000 * stepS / 60;
                for (int c = 0; c < C; c++)
                {
                    if (rt[s][c] == 0) continue;
                    long m = rt[s][c] * Math.Min(1000, pk);
                    int whole = (int)(m / 1000), rem = (int)(m % 1000);
                    if (rem > 0 && DetRng.Range(seed, step, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, 2, 0, 1000) < rem) whole++;
                    whole = Math.Min(whole, rt[s][c]);
                    rt[s][c] -= whole; cas[s][c] += whole;
                    if (rt[s][c] == 0) continue;
                    int leave = (int)((long)rt[s][c] * 1 / flee), lrem = (int)((long)rt[s][c] % flee);
                    if (lrem > 0 && DetRng.Range(seed, step, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, 3, 0, flee) < lrem) leave++;
                    if (n[s].Sum() == 0 && n[e].Sum() == 0) leave = rt[s][c];
                    leave = Math.Min(leave, rt[s][c]);
                    int taken = en == 0 ? 0 : leave * d.K[CountK.CapturePm] / 1000;
                    int trem = en == 0 ? 0 : leave * d.K[CountK.CapturePm] % 1000;
                    if (trem > 0 && DetRng.Range(seed, step, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, 4, 0, 1000) < trem) taken++;
                    taken = Math.Min(taken, leave);
                    rt[s][c] -= leave; fled[s][c] += leave - taken; capt[s][c] += taken;
                }
            }
        }
        // whoever is still in the line when the other has none has won; routers still in reach at the end flee
        int a0 = n[0].Sum(), a1 = n[1].Sum();
        r.Winner = a0 > 0 && a1 == 0 ? 0 : a1 > 0 && a0 == 0 ? 1 : a0 == 0 && a1 == 0 ? -1 : (start[0] - a0 - fled[0].Sum()) < (start[1] - a1 - fled[1].Sum()) ? 0 : (start[0] - a0 - fled[0].Sum()) > (start[1] - a1 - fled[1].Sum()) ? 1 : -1;
        r.Steps = steps; r.DurationS = steps * stepS;
        for (int s = 0; s < 2; s++)
            for (int c = 0; c < C; c++)
            {
                fled[s][c] += rt[s][c];
                int total = cas[s][c];
                int dead = Split(seed, key, s, c, 10, total, d.DeadPm[c], total);
                int crip = Split(seed, key, s, c, 11, total - dead, d.CrippledPm[c], total - dead);
                r.Dead[s][c] = dead; r.Crippled[s][c] = crip; r.Recovering[s][c] = total - dead - crip;
                r.Fled[s][c] = fled[s][c]; r.Captured[s][c] = capt[s][c]; r.Standing[s][c] = n[s][c];
            }
        return r;
    }

    /// <summary>The per-pair cascade: this step's routers raise the shock on the classes engaged with the same enemy block, by how much the same enemy classes hit each (the overlap of their hit shares).</summary>
    private static void CascadeUpdate(CountBattleDef d, int[][] n, long[] shock, int[] routed, int e, int start)
    {
        int gain = Kv(d, CountK.CascadeGain), decay = Kv(d, CountK.CascadeDecay);
        if (gain == 0) return;
        for (int c = 0; c < C; c++) shock[c] -= shock[c] * decay / 1000;
        int total = 0; for (int c = 0; c < C; c++) total += routed[c];
        if (total == 0 || start == 0) return;
        long[][] p = new long[C][];                                   // p[c][k]: the share of c's hits that come from enemy class k, permille
        for (int c = 0; c < C; c++)
        {
            p[c] = new long[C]; long sum = 0;
            for (int k = 0; k < C; k++) { p[c][k] = (long)n[e][k] * d.Share[k] / 1000 * d.Beta[k * C + c]; sum += p[c][k]; }
            if (sum > 0) for (int k = 0; k < C; k++) p[c][k] = p[c][k] * 1000 / sum;
        }
        for (int a = 0; a < C; a++)
        {
            if (routed[a] == 0) continue;
            long fr = (long)routed[a] * 1000 / start;                 // permille of the side's start
            for (int c = 0; c < C; c++)
            {
                long ov = 0; for (int k = 0; k < C; k++) ov += p[a][k] * p[c][k] / 1000;
                shock[c] += fr * gain / 1000 * ov / 1000;
            }
        }
    }

    private static int Split(ulong seed, ulong key, int s, int c, uint draw, int of, int pm, int cap)
    {
        pm = Math.Clamp(pm, 0, 1000);
        long m = (long)of * pm;
        int whole = (int)(m / 1000), rem = (int)(m % 1000);
        if (rem > 0 && DetRng.Range(seed, 9999, key + (ulong)(s * C + c), RngStreams.CountBattleRounding, draw, 0, 1000) < rem) whole++;
        return Math.Min(whole, cap);
    }
}
