using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Morale;

/// <summary>Where a fighter stands in its nerve (morale-capture-succession 3.1).</summary>
public enum MoraleMood : byte { Steady, Wavering, Routing, Fled, Surrendered, Rallied }

/// <summary>What a side is, for who accepts a surrender, who routs and what a defeat costs (4.2, 4.4). Set per team by the fight's setup.</summary>
public enum SideKind : byte { Folk, Army, Bandits, Beasts, Draugr, Guards, Locals }

[Flags]
public enum MoraleFlags : ushort
{
    None = 0, Inited = 1, Captain = 2, Banner = 4, ShieldRolled = 8, WeaponRolled = 16, Superstitious = 32, Veteran = 64,
}

/// <summary>
/// One fighter's morale (3.1; live only, saved with the fight record mid-fight): courage and the decaying penalty in points x100, the routing-neighbour share of it, the flank timer,
/// the mood and the ticks its rules count from. Every field is an integer.
/// </summary>
public struct MoraleState
{
    public int CourageX100, PenaltyX100, RoutAccX100, ValueX100;
    public int FlankedUntil, RoutTick, EscapeSince, RallySince, ImmuneUntil, LastLossTick, StepBackUntil, RefusedUntil, PendingFalls;
    public MoraleMood Mood;
    public SideKind Side;
    public MoraleFlags Flags;

    public bool Has(MoraleFlags f) => (Flags & f) != 0;

    public static MoraleState Fresh(int courageX100) => new()
    {
        CourageX100 = courageX100, ValueX100 = courageX100, FlankedUntil = -1, RoutTick = -1, EscapeSince = -1, RallySince = -1, ImmuneUntil = -1, LastLossTick = -100_000,
        StepBackUntil = -1, RefusedUntil = -1, Flags = MoraleFlags.Inited,
    };

    /// <summary>Every integer in one fixed order (the fight record's morale row).</summary>
    public readonly long[] Pack() =>
    [
        CourageX100, PenaltyX100, RoutAccX100, ValueX100, FlankedUntil, RoutTick, EscapeSince, RallySince, ImmuneUntil, LastLossTick, StepBackUntil, RefusedUntil, PendingFalls,
        (long)Mood, (long)Side, (long)Flags,
    ];

    public static MoraleState Unpack(long[] s)
    {
        if (s.Length < 16) throw new Save.SaveException("Corrupt morale row in the fight record.");
        int i = 0;
        return new MoraleState
        {
            CourageX100 = (int)s[i++], PenaltyX100 = (int)s[i++], RoutAccX100 = (int)s[i++], ValueX100 = (int)s[i++], FlankedUntil = (int)s[i++], RoutTick = (int)s[i++], EscapeSince = (int)s[i++],
            RallySince = (int)s[i++], ImmuneUntil = (int)s[i++], LastLossTick = (int)s[i++], StepBackUntil = (int)s[i++], RefusedUntil = (int)s[i++], PendingFalls = (int)s[i++],
            Mood = (MoraleMood)s[i++], Side = (SideKind)s[i++], Flags = (MoraleFlags)s[i++],
        };
    }

    public readonly ulong Hash(ulong h)
    {
        foreach (long v in Pack()) h = FightMath.Mix(h, v);
        return h;
    }
}

/// <summary>The parts of one morale reading (x100): what MC-2 checks term by term.</summary>
public readonly record struct MoraleTerms(int CourageX100, int PenaltyX100, int RoutingX100, int FlankedX100, int PainPct, int BloodLossPm, int CaptainX100, int BannerX100,
    int ShieldWallX100, int WinningX100, int OutnumberedX100, int DarknessX100 = 0, int AloneX100 = 0);

/// <summary>
/// The morale rules (3.1, 4.1) read once from <c>combat/morale.jsonc</c> per content database, with the pure parts of the model: the reading from its terms and the thresholds.
/// The fight's own step is <see cref="FightWorld"/>'s morale partial (Morale/FightWorld.Morale.cs).
/// </summary>
public sealed class MoraleRules
{
    public MoraleRulesDef D { get; }
    /// <summary>Morale updates every this many fight ticks (2 Hz), staggered by fighter id.</summary>
    public const int EveryTicks = 10;
    public const int TicksPerS = 20;

    public MoraleRules(MoraleRulesDef d) { D = d; }

    private static readonly ConditionalWeakTable<ContentDatabase, MoraleRules> Cache = new();

    public static MoraleRules For(ContentDatabase db) => Cache.GetValue(db, d => new MoraleRules(d.All<MoraleRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MoraleRulesDef { Id = "default" }));

    /// <summary>The spec's defaults (tests and fights without content).</summary>
    public static MoraleRules Default { get; } = new(new MoraleRulesDef { Id = "default" });

    /// <summary>Penalty decay per update (0.4 points a second x 0.5 s = 0.2 points).</summary>
    public int DecayPerUpdate => D.DecayX100PerS * EveryTicks / TicksPerS;
    public int RoutingPerNeighbourPerUpdate => D.RoutingNeighbourX100PerS * EveryTicks / TicksPerS;

    /// <summary>
    /// <c>morale = courage - penalty - pain_k x pain - blood_loss_k x blood_loss% + captain + banner + shield_wall + winning - outnumbered</c> (3.1), with the routing-neighbour share
    /// and the flank term as penalties; clamped 0..100 points (x100).
    /// </summary>
    public int Value(in MoraleTerms t)
    {
        long v = t.CourageX100 - t.PenaltyX100 - t.RoutingX100 - t.FlankedX100 - PainTerm(t.PainPct) - BloodTerm(t.BloodLossPm)
            + t.CaptainX100 + t.BannerX100 + t.ShieldWallX100 + t.WinningX100 - t.OutnumberedX100 - t.DarknessX100 - t.AloneX100;
        return (int)Math.Clamp(v, 0, 10_000);
    }

    /// <summary>Pain is the share (0..100) of the knock-out threshold; x0.35 points each.</summary>
    public int PainTerm(int painPct) => Math.Clamp(painPct, 0, 100) * D.PainKPm / 10;
    /// <summary>Blood lost in permille; x1.0 point per percent.</summary>
    public int BloodTerm(int lossPm) => Math.Clamp(lossPm, 0, 1000) * D.BloodLossKPm / 100;

    /// <summary>The mood a reading gives a fighter on its feet (4.1): rout below 20, wavering below 35 after a recent loss, else steady. Surrender needs the cornered test too.</summary>
    public MoraleMood MoodOf(int valueX100, bool recentLoss) =>
        valueX100 < D.RoutBelowX100 ? MoraleMood.Routing : valueX100 < D.WaverBelowX100 && recentLoss ? MoraleMood.Wavering : MoraleMood.Steady;

    /// <summary>Surrender only below 10 and only when cornered; with a free flee path a fighter that low routs instead (MC-4).</summary>
    public bool Surrenders(int valueX100, bool cornered) => valueX100 < D.SurrenderBelowX100 && cornered;

    /// <summary>Acceptance of a surrender by the winner's kind, permille (4.2).</summary>
    public int AcceptPm(SideKind winner) => winner switch
    {
        SideKind.Army => D.AcceptArmyPm, SideKind.Bandits => D.AcceptBanditPm, SideKind.Beasts or SideKind.Draugr => 0, _ => D.AcceptFolkPm,
    };

    /// <summary>Animals and monsters (4.1): wolves and dogs (the quadruped plan) flee at 40% blood loss, the cave beast at 30%, a draugr never; -1 = morale as a person.</summary>
    public int BeastFleeBloodPm(string plan) => plan switch
    {
        "quadruped" or "wolf" or "dog" => D.WolfFleeBloodPm, "cave_beast" or "bear" => D.CaveBeastFleeBloodPm, "draugr" => int.MaxValue, "human" => -1, _ => int.MaxValue,
    };

    /// <summary>Base courage x100 from a kit's courage (0..100), the traits and a veteran's 5 points.</summary>
    public int CourageX100(int kitCourage, IEnumerable<string>? traits = null, bool veteran = false)
    {
        int c = kitCourage > 0 ? kitCourage * 100 : D.BaseX100;
        if (traits != null) foreach (var t in traits) if (D.TraitX100.TryGetValue(t, out var m)) c += m;
        if (veteran) c += D.VeteranX100;
        return Math.Clamp(c, 0, 10_000);
    }
}
