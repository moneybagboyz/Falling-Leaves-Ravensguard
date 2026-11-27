namespace Ravensguard.Content;

/// <summary>
/// The count battle's tables (abstract-war 3, 4.4; <c>abstract/count_battle.jsonc</c>, row type <c>count_battle</c>), fitted from the v2 resolver by
/// <c>tools/Ravensguard.Calibrate -- --abstract count fit</c> over the 40 scenarios of AW-10 and gated within +-10% of v2. Classes are levy, foot, missile, horse, elite (in that
/// order); every rate is an integer (permille unless named). <see cref="Beta"/> is indexed <c>attacker * 5 + victim</c>.
/// </summary>
[ContentType("count_battle")]
public sealed record CountBattleDef : ContentDef
{
    public const int Classes = 5;
    /// <summary>Casualties (milli-men) a class's engaged man puts on a victim class's whole side in a minute, before the victim's exposure share is applied.</summary>
    public int[] Beta { get; init; } = new int[Classes * Classes];
    /// <summary>The share of a class's men engaged at once (frontage and ranks; missiles shoot over the ranks), permille.</summary>
    public int[] Share { get; init; } = [250, 250, 250, 250, 250];
    /// <summary>How much of the hits a class draws, per man (the front ranks take more than the archers behind them), permille.</summary>
    public int[] Expo { get; init; } = [1000, 1000, 1000, 1000, 1000];
    /// <summary>The share of the side's start strength lost (casualties) at which a class begins to break, permille.</summary>
    public int[] RoutThr { get; init; } = [60, 60, 60, 60, 60];
    /// <summary>Of a class's casualties, the shares that die and that are crippled (the rest recover), permille.</summary>
    public int[] DeadPm { get; init; } = [400, 400, 400, 400, 400];
    public int[] CrippledPm { get; init; } = [200, 200, 200, 200, 200];
    /// <summary>The scalars, by the <see cref="CountK"/> index.</summary>
    public int[] K { get; init; } = CountK.Defaults();

    /// <summary>Every fitted number as one list (for the fitter) and back.</summary>
    public int[] Flat() => [.. Beta, .. Share, .. Expo, .. RoutThr, .. DeadPm, .. CrippledPm, .. K];

    public CountBattleDef WithFlat(int[] f)
    {
        int o = 0;
        int[] Take(int n) { var r = f[o..(o + n)]; o += n; return r; }
        return this with { Beta = Take(Beta.Length), Share = Take(Share.Length), Expo = Take(Expo.Length), RoutThr = Take(RoutThr.Length), DeadPm = Take(DeadPm.Length), CrippledPm = Take(CrippledPm.Length), K = Take(K.Length) };
    }

    public static string[] FlatNames()
    {
        string[] cls = ["levy", "foot", "missile", "horse", "elite"];
        var l = new List<string>();
        for (int a = 0; a < Classes; a++) for (int v = 0; v < Classes; v++) l.Add($"beta.{cls[a]}>{cls[v]}");
        foreach (var n in new[] { "share", "expo", "rout_thr", "dead", "crippled" }) for (int c = 0; c < Classes; c++) l.Add($"{n}.{cls[c]}");
        l.AddRange(CountK.Names);
        return [.. l];
    }

    /// <summary>A fingerprint of every number (the tests' stale-table check, the way AB-1 pins the wound tables' content hash).</summary>
    public string Fingerprint()
    {
        ulong h = 1469598103934665603UL;
        foreach (int v in Flat()) { h ^= (uint)v; h *= 1099511628211UL; h ^= h >> 29; }
        return h.ToString("X16");
    }
}

/// <summary>Indices of <see cref="CountBattleDef.K"/>.</summary>
public static class CountK
{
    /// <summary>The step, sim-seconds (v2's fights run 5 to 30 s at scale, so the spec's 60 s step would be one step; 3 s is five to ten).</summary>
    public const int StepS = 0;
    public const int MaxSteps = 1;
    /// <summary>A class breaks by this share of its standing men a step for each permille its side's loss is over its threshold, permille, and the most that break in a step.</summary>
    public const int RoutSlope = 2, RoutMaxPm = 3;
    /// <summary>The standing share of the start under which a side's line is gone and the rest flee, permille.</summary>
    public const int BreakStandingPm = 4;
    /// <summary>The charge: the multiplier on horse's first steps (permille) and how many steps.</summary>
    public const int ChargePm = 5, ChargeSteps = 6;
    /// <summary>Seconds missiles shoot at full rate, and their engaged share after (permille).</summary>
    public const int AmmoS = 7, AmmoLowPm = 8;
    /// <summary>Routers: seconds in reach, share of them struck down a minute by the winner's men, and the extra a winner's horse adds (per permille of its horse share).</summary>
    public const int FleeS = 9, PursuitPm = 10, PursuitHorsePm = 11;
    /// <summary>Of the routers who get away, the share taken as prisoners, permille.</summary>
    public const int CapturePm = 12;
    /// <summary>The luck of a side in a fight: its blows are scaled by 1 +- this, permille.</summary>
    public const int LuckPm = 13;
    /// <summary>How much a side's own pressure (the enemy's blows against its own) hastens its rout: permille of break per permille of the casualty ratio.</summary>
    public const int RatioPm = 14;
    /// <summary>The breaking threshold's scale with the side's morale (permille at morale 1000), and how much the enemy's horse in its flank adds (permille of threshold).</summary>
    public const int MoraleScalePm = 15, FlankedPm = 16;
    /// <summary>The morale cascade: each side has a panic level (permille) that a step's losses raise (gain: permille of panic per 10 permille of the start lost in a step, x10), that spreads among the men (a logistic growth: permille a step) and that fades (permille a step); it adds to the casualty share the classes break on.</summary>
    public const int PanicGain = 17, PanicSpread = 18, PanicDecay = 19;
    /// <summary>The per-pair cascade (WT-2): when a class routs, the shock falls on the classes engaged with the same enemy block (overlap of who hits them), not the whole side. Gain: permille added to a class's break share per permille of the side's start that routed in a step, times the pair's overlap; decay: permille of the shock lost a step.</summary>
    public const int CascadeGain = 20, CascadeDecay = 21;
    public const int Count = 22;
    public static readonly string[] Names = ["step_s", "max_steps", "rout_slope", "rout_max_pm", "break_standing_pm", "charge_pm", "charge_steps", "ammo_s", "ammo_low_pm", "flee_s", "pursuit_pm", "pursuit_horse_pm", "capture_pm", "luck_pm", "ratio_pm", "morale_scale_pm", "flanked_pm", "panic_gain", "panic_spread", "panic_decay", "cascade_gain", "cascade_decay"];

    public static int[] Defaults() => [3, 100, 600, 500, 300, 1500, 2, 120, 100, 16, 300, 500, 100, 100, 0, 1000, 0, 3000, 300, 20, 0, 300];
}
