using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The mounted part of a fight (mounted-combat.md): the rider and horse pairs, what the layer keeps for every fighter (braces, trample cooldowns), the ground and the weather,
/// and the work queued during the impact phase (unhorsings, shattered lances) that is done after it. A fight without horses has none (<c>FightWorld.Mounts</c> is null) and runs
/// exactly as before.
/// </summary>
public sealed class MountedLayer
{
    public MountedTuning Tuning { get; }
    public MountedTuningDef D => Tuning.D;
    public List<MountedFighter> Pairs { get; } = [];
    public FightTerrain Terrain { get; set; } = new();
    public MountedExtra[] Extra = [];
    /// <summary>[P7 CV-B] The cavalry rules' constants.</summary>
    public CavalryRulesDef Cv => Tuning.Cv;
    /// <summary>[P7 CV-B] The horses lying down this tick (fighter ids, pair order): each is a 2 m x 1 m obstacle to the mounts that come after.</summary>
    internal readonly List<int> Fallen = [];
    /// <summary>Fighter id to pair index (rider or horse), -1 none.</summary>
    private int[] _pairOf = [];
    internal readonly List<(int Rider, int Attacker)> PendingUnseat = [];
    internal readonly List<int> PendingShatter = [];
    /// <summary>The horse's stance: it never routs or yields on its own (its fear is panic, 4.2).</summary>
    public CombatStanceDef HorseStance { get; } = new() { Id = "mounted.horse", FleeBloodLossPm = 0, FleeMoraleBelow = 0, UseRanged = false, CalledShots = false, TreatAllies = false };

    /// <summary>Stopwatch ticks the mounted hooks have spent (a measurement for the budget tests; never hashed, saved or read by the rules).</summary>
    public long PerfTicks;
    /// <summary>The brace scan's scratch (the nearest closing rider's distance and id per foot fighter), rebuilt every think.</summary>
    internal long[] BraceD = [];
    internal int[] BraceFace = [];

    public MountedLayer(MountedTuning tuning) { Tuning = tuning; }

    public void Grow(int fighters)
    {
        if (Extra.Length < fighters) Array.Resize(ref Extra, fighters);
        if (_pairOf.Length < fighters)
        {
            int old = _pairOf.Length;
            Array.Resize(ref _pairOf, fighters);
            for (int i = old; i < fighters; i++) _pairOf[i] = -1;
        }
    }

    internal void Index(MountedFighter m, int fighters)
    {
        Grow(fighters);
        _pairOf[m.Rider] = m.Index; _pairOf[m.Horse] = m.Index;
    }

    /// <summary>The pair a fighter is the rider or the horse of (null for a foot fighter).</summary>
    public MountedFighter? PairOf(int fighter) => fighter >= 0 && fighter < _pairOf.Length && _pairOf[fighter] >= 0 ? Pairs[_pairOf[fighter]] : null;
    public bool IsHorse(int fighter) => PairOf(fighter) is { } m && m.Horse == fighter;
    /// <summary>The pair whose rider this fighter is, while he sits the horse.</summary>
    public MountedFighter? Riding(int fighter) => PairOf(fighter) is { Mounted: true } m && m.Rider == fighter ? m : null;
}
