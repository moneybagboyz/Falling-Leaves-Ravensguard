using Ravensguard.Content;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// Locks and keys (heists-and-jailbreaks 3, 4.3): a lock's quality sets the time a pick takes (20, 40, 60, 120, 240 sim-seconds) and an offset to the skill in the Phase 4 lockpick rule (<see cref="EscapeTable.SkillChancePm"/>,
/// difficulty 30 as for a hideout's lock, stream 0x455); a key opens it at once and works until the lord changes the locks (the lock generation).
/// </summary>
public static class Locks
{
    public const int BaseDifficulty = 30;

    public static int PickTimeS(HeistRules r, int quality) => r.Lock(quality).PickS;
    public static int BreakTimeS(HeistRules r, int quality) => r.Lock(quality).BreakS;

    /// <summary>The chance (permille) one finished attempt opens the lock: the skill plus the quality's offset against the hideout's difficulty, 5% to 95%.</summary>
    public static int PickPm(HeistRules r, int skill, int quality) => EscapeTable.SkillChancePm(skill + r.Lock(quality).Offset, BaseDifficulty);

    /// <summary>The roll of the attempt: stream 0x455, keyed on the second the attempt ended, the picker and the door, so a replay and a resume agree.</summary>
    public static bool Roll(ulong seed, long sec, EntityId who, ulong door, int attempt, int pm) =>
        DetRng.Range(seed, sec, who.Value ^ door * 0x9E3779B97F4A7C15UL, RngStreams.LockPick, 0x6B0000u + (uint)attempt, 0, 1000) < pm;

    /// <summary>The identity of a door's key at a lock generation: a key stolen before the locks were changed opens nothing after.</summary>
    public static ulong KeyId(ulong siteId, int door, int generation) => DetRng.Mix(siteId ^ ((ulong)door << 40) ^ ((ulong)generation << 52) ^ 0x4B45_5900UL);
}

/// <summary>The bribe (heists-and-jailbreaks 4.3, one rule for every official): the gaoler's own price, <see cref="Gaol.BribeFloor(LawSettingsDef, int)"/>, taken with P = (60 - honesty) percent on stream 0x613 (key official and day).</summary>
public enum BribeResult : byte { Honest, TooLittle, Failed, Taken }

public static class Bribes
{
    public static BribeResult Offer(ulong seed, LawSettingsDef s, ulong official, int honesty, long offer, long sec)
    {
        var floor = Gaol.BribeFloor(s, honesty);
        if (floor == null) return BribeResult.Honest;
        if (offer < floor) return BribeResult.TooLittle;
        int day = (int)(sec / 86_400);
        return DetRng.Range(seed, day, official, RngStreams.BribeRoll, 0, 0, 100) < Gaol.BribePct(s, honesty) ? BribeResult.Taken : BribeResult.Failed;
    }
}

/// <summary>How a guard takes a man in a lord's livery (heists-and-jailbreaks 4.3).</summary>
public enum Challenge : byte { Passes, Challenged, SeesThrough }

public static class Disguise
{
    /// <summary>
    /// A guard who sees a man in the livery: one who knows his face sees through it; one who sees him run, carry a body, or stand in the cell block challenges him; within 4 m he is challenged; between 4 and 5 m the borderline is a
    /// draw (stream 0x6B3, key guard and hour); beyond that he passes. Without the livery he is no one the guard knows to belong: challenged.
    /// </summary>
    public static Challenge Check(ulong seed, HeistSettingsDef s, ulong guard, long hour, int distCm, bool livery, bool knowsHim, bool running, bool carrying, bool inCellBlock)
    {
        if (!livery) return Challenge.Challenged;
        if (knowsHim) return Challenge.SeesThrough;
        if (running || carrying || inCellBlock) return Challenge.Challenged;
        if (distCm <= s.DisguiseM * 100) return Challenge.Challenged;
        if (distCm <= s.DisguiseBorderM * 100)
            return DetRng.Range(seed, hour, guard, RngStreams.DisguiseBorderline, 0, 0, 100) < s.DisguiseBorderPct ? Challenge.Challenged : Challenge.Passes;
        return Challenge.Passes;
    }
}
