using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// A rider's horse for a fight (the input of <c>FightWorld.AddMounted</c>): the breed, the Phase 3 animal row it is (0 for a harness horse), its training and temper (-1: the breed's),
/// the rider's Riding skill and weight, the barding worn. Built from the hero's ride or by hand in tests, the harness and the demo.
/// </summary>
public sealed record MountKit
{
    public string Breed { get; init; } = "warhorse_courser";
    public ulong Animal { get; init; }
    public string Name { get; init; } = "";
    public int TrainedPm { get; init; } = -1;
    public int Temper { get; init; } = int.MinValue;
    public int Riding { get; init; } = 40;
    /// <summary>The rider's body (the fall and the charge's mass; spec 80 kg).</summary>
    public int RiderKg { get; init; } = 80;
    /// <summary>Gear carried (armour, weapon, shield), kg; -1 to take it from the rider's kit (worn pieces, weapon and shield).</summary>
    public int GearKg { get; init; } = -1;
    public IReadOnlyList<WornItem> Barding { get; init; } = [];
    /// <summary>The horse's stamina at the start (stamina points; -1 full).</summary>
    public int StaminaSp { get; init; } = -1;
}

/// <summary>A footing-free speed table of one breed, mm/s, with its stamina costs (sp per metre) and regeneration (sp per game second).</summary>
public readonly record struct BreedGaits(int WalkMmps, int TrotMmps, int GallopMmps, int TrotCostSp, int GallopCostSp, int RegenWalkSp, int RegenRestSp, int StaminaSp, int MassKg, int CarryKg);

/// <summary>
/// One rider and horse (mounted-combat 3.1 <c>MountedFighter</c>): two fighters in the fight, one position. What the pair is (breed numbers, masses, training, the rider's Riding)
/// and everything the mounted layer keeps between ticks (speed, heading, the last four positions for the closing speed, the horse's stamina, panic, grip, a pinned leg, a balk, a bolt,
/// a side-step and the cavalry AI's plan). Every field is an integer and saved with the fight (<c>FightRecord</c>'s mounted tail).
/// </summary>
public sealed class MountedFighter
{
    public const int AiCharge = 0, AiPass = 1, AiReform = 2, AiFlank = 3, AiRetreat = 4, AiHold = 5;

    public int Index;
    public int Rider, Horse;
    public ulong Animal;
    public BreedGaits Gaits;
    public int RiderKg, GearKg, BardingKg, TrainedPm, Temper, Riding;
    public int TotalKg => Gaits.MassKg + RiderKg + GearKg + BardingKg;

    // ---- state ----
    public bool Mounted = true;
    public int Speed, HX = 1024, HY;
    public int VX, VY;
    public readonly int[] HistX = new int[4], HistY = new int[4];
    public long StaminaU;
    public bool Winded, Walk;
    public int PanicPm, PanicCarry;
    public int GripPm = 1000;
    public int PinnedUntil = -1;
    public int RefusedUntil = -1, RefusalFor = -1, RefuseBrake;
    public int BoltUntil = -1, BoltX, BoltY;
    public int SideReady, SideTicks, SideX, SideY;
    public int HorseWoundsSeen;
    public bool Lame;
    public int LeftAt = -1, RunX, RunY;
    public bool Couched;
    // ---- the cavalry AI (4.6) ----
    public int AiPhase, AiX, AiY, AiTarget = -1, AiSince, AiStruck = -1;

    /// <summary>Every integer of the pair in a fixed order (the save tail; <see cref="Unpack"/> reads it back).</summary>
    public long[] Pack() =>
    [
        Rider, Horse, (long)Animal, Gaits.WalkMmps, Gaits.TrotMmps, Gaits.GallopMmps, Gaits.TrotCostSp, Gaits.GallopCostSp, Gaits.RegenWalkSp, Gaits.RegenRestSp, Gaits.StaminaSp, Gaits.MassKg, Gaits.CarryKg,
        RiderKg, GearKg, BardingKg, TrainedPm, Temper, Riding,
        Mounted ? 1 : 0, Speed, HX, HY, VX, VY, HistX[0], HistX[1], HistX[2], HistX[3], HistY[0], HistY[1], HistY[2], HistY[3], StaminaU, Winded ? 1 : 0, Walk ? 1 : 0,
        PanicPm, PanicCarry, GripPm, PinnedUntil, RefusedUntil, RefusalFor, RefuseBrake, BoltUntil, BoltX, BoltY, SideReady, SideTicks, SideX, SideY, HorseWoundsSeen, Lame ? 1 : 0,
        LeftAt, RunX, RunY, Couched ? 1 : 0, AiPhase, AiX, AiY, AiTarget, AiSince, AiStruck,
    ];

    public const int PackLength = 62;

    public static MountedFighter Unpack(ReadOnlySpan<long> s, int index)
    {
        int i = 0;
        var m = new MountedFighter { Index = index, Rider = (int)s[i++], Horse = (int)s[i++], Animal = (ulong)s[i++] };
        m.Gaits = new BreedGaits((int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++], (int)s[i++]);
        m.RiderKg = (int)s[i++]; m.GearKg = (int)s[i++]; m.BardingKg = (int)s[i++]; m.TrainedPm = (int)s[i++]; m.Temper = (int)s[i++]; m.Riding = (int)s[i++];
        m.Mounted = s[i++] != 0; m.Speed = (int)s[i++]; m.HX = (int)s[i++]; m.HY = (int)s[i++]; m.VX = (int)s[i++]; m.VY = (int)s[i++];
        for (int k = 0; k < 4; k++) m.HistX[k] = (int)s[i++];
        for (int k = 0; k < 4; k++) m.HistY[k] = (int)s[i++];
        m.StaminaU = s[i++]; m.Winded = s[i++] != 0; m.Walk = s[i++] != 0;
        m.PanicPm = (int)s[i++]; m.PanicCarry = (int)s[i++]; m.GripPm = (int)s[i++]; m.PinnedUntil = (int)s[i++]; m.RefusedUntil = (int)s[i++]; m.RefusalFor = (int)s[i++]; m.RefuseBrake = (int)s[i++];
        m.BoltUntil = (int)s[i++]; m.BoltX = (int)s[i++]; m.BoltY = (int)s[i++]; m.SideReady = (int)s[i++]; m.SideTicks = (int)s[i++]; m.SideX = (int)s[i++]; m.SideY = (int)s[i++];
        m.HorseWoundsSeen = (int)s[i++]; m.Lame = s[i++] != 0; m.LeftAt = (int)s[i++]; m.RunX = (int)s[i++]; m.RunY = (int)s[i++]; m.Couched = s[i++] != 0;
        m.AiPhase = (int)s[i++]; m.AiX = (int)s[i++]; m.AiY = (int)s[i++]; m.AiTarget = (int)s[i++]; m.AiSince = (int)s[i++]; m.AiStruck = (int)s[i++];
        return m;
    }

    public ulong Hash(ulong h)
    {
        foreach (var v in Pack()) h = Melee2.FightMath.Mix(h, v);
        return h;
    }
}

/// <summary>What the mounted layer keeps for any fighter of a mounted fight: a brace wanted (the AI's or the player's RMB), when it braced, the wounds it had then, a trample cooldown.</summary>
public struct MountedExtra
{
    public bool BraceWanted;
    public int BracedAt, HitsAtBrace, TrampledUntil;
    /// <summary>The tick of its last braced strike: for a second after it the bearer still stands planted (a horse it struck does not trample it).</summary>
    public int BraceStruckAt;
}

/// <summary>Mounted combat's numbers as the step reads them (<see cref="MountedTuningDef"/> plus the footing table and the synthetic trample weapon), built once per content database.</summary>
public sealed class MountedTuning
{
    public MountedTuningDef D { get; }
    public FootingRule[] Footing { get; }
    public IReadOnlyDictionary<string, SpeciesDef> Breeds { get; }
    public WeaponV2 Trample { get; }

    public MountedTuning(ContentDatabase db, CombatTablesV2 t)
    {
        D = db.All<MountedTuningDef>().OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? new MountedTuningDef { Id = "default" };
        Footing = TerrainFooting.From(D);
        Breeds = db.All<SpeciesDef>().ToDictionary(d => d.Id, StringComparer.Ordinal);
        Trample = t.Synthetic(new WeaponShapeDef
        {
            Id = "mounted.trample", Name = "Trample", Family = "natural", ReachMm = 700, StrikeTicks = 1, HeadVolumeCm3 = 800,
            Attacks = [new AttackDef { Id = "trample", Name = "Trample", Verb = "tramples", Type = AttackType.Blunt, ContactArea = 80, PenetrationMm = 0, Velocity = 1.0 }],
        }, "bone");
    }

    public int WeaponPm(string key, int fallback) => D.ChargeWeaponPm.TryGetValue(key, out var v) ? v : fallback;

    public BreedGaits Gaits(string breed)
    {
        if (!Breeds.TryGetValue(breed, out var b)) b = Breeds.TryGetValue("warhorse_courser", out var c) ? c : Breeds.Values.First();
        int G(string k) => b.GaitCmps.TryGetValue(k, out var v) ? v * 10 : 0;
        int C(string k) => b.StaminaCostSpPerM.TryGetValue(k, out var v) ? v : 0;
        int R(string k) => b.RegenSpPerGs.TryGetValue(k, out var v) ? v : 0;
        return new BreedGaits(G("walk"), G("trot"), G("gallop"), C("trot"), C("gallop"), R("walk"), R("rest"), b.StaminaSp, b.MassKg, b.CarryKg);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, MountedTuning> Cache = new();
    public static MountedTuning For(ContentDatabase db, CombatTablesV2 t) { lock (Cache) return Cache.GetValue(db, d => new MountedTuning(d, t)); }
}
