using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>The six stance presets of watch-mode 3.1 (rows of <c>combat/stances.jsonc</c>), in the order Shift+F6 cycles them. Append only: saved.</summary>
public enum StancePreset : byte { Aggressive, Balanced, Defensive, Protect, Subdue, FleeHurt }

/// <summary>Bits of <see cref="WatchStance.Flags"/>.</summary>
[Flags]
public enum StanceFlags : byte { None = 0, NoLethal = 1, CalledShots = 2, UseRanged = 4, TreatAllies = 8, Custom = 16 }

/// <summary>
/// A person's combat stance for watch mode (watch-mode 3.1; saved per hero and party member, 16 bytes): the preset, the guarded person, the toggles and the flee thresholds. The
/// preset's content row is the base and the fields here override it, so "balanced" with nothing changed is exactly the NPC "balanced" (WM-1).
/// </summary>
public readonly record struct WatchStance(StancePreset Preset, StanceFlags Flags, ushort FleeBloodPm, ushort FleePainPm, byte FleeMoraleBelow, byte FleeOddsBelow10, ulong Protect)
{
    public static readonly string[] Ids = ["aggressive", "balanced", "defensive", "protect", "subdue", "flee_hurt"];
    public string Id => Ids[(int)Preset % Ids.Length];
    public bool NoLethal => (Flags & StanceFlags.NoLethal) != 0;
    public bool Custom => (Flags & StanceFlags.Custom) != 0;
    public int FleeOddsBelowPm => FleeOddsBelow10 * 10;

    /// <summary>The preset as content has it (nothing overridden).</summary>
    public static WatchStance Of(FightContent c, StancePreset p) => FromDef(c.Stance(Ids[(int)p]), p);

    public static WatchStance FromDef(CombatStanceDef d, StancePreset p) => new(p,
        (d.NoLethal ? StanceFlags.NoLethal : 0) | (d.CalledShots ? StanceFlags.CalledShots : 0) | (d.UseRanged ? StanceFlags.UseRanged : 0) | (d.TreatAllies ? StanceFlags.TreatAllies : 0),
        (ushort)Math.Clamp(d.FleeBloodLossPm, 0, 1000), (ushort)Math.Clamp(d.FleePainPm, 0, 1000), (byte)Math.Clamp(d.FleeMoraleBelow, 0, 100), (byte)Math.Clamp(d.FleeOddsBelowPm / 10, 0, 100), 0);

    public static StancePreset PresetOf(string id) { int i = Array.IndexOf(Ids, id); return i < 0 ? StancePreset.Balanced : (StancePreset)i; }

    /// <summary>The next preset (Shift+F6), with the preset's own values.</summary>
    public WatchStance Next(FightContent c) => Of(c, (StancePreset)(((int)Preset + 1) % Ids.Length)) with { Protect = Protect };

    /// <summary>The definition the brain reads: the preset's row, overridden by this stance's toggles and thresholds when <see cref="Custom"/> (the HUD set one), else the row itself.</summary>
    public CombatStanceDef ToDef(FightContent c)
    {
        var d = c.Stance(Id);
        if (!Custom) return d;
        return d with
        {
            Id = d.Id, NoLethal = NoLethal, CalledShots = (Flags & StanceFlags.CalledShots) != 0, UseRanged = (Flags & StanceFlags.UseRanged) != 0, TreatAllies = (Flags & StanceFlags.TreatAllies) != 0,
            FleeBloodLossPm = FleeBloodPm, FleePainPm = FleePainPm, FleeMoraleBelow = FleeMoraleBelow, FleeOddsBelowPm = FleeOddsBelowPm,
            FleeChecks = d.FleeChecks || FleeOddsBelow10 > 0 || FleePainPm < d.FleePainPm || FleeMoraleBelow > d.FleeMoraleBelow,
        };
    }

    /// <summary>One toggle changed (the stance panel): the stance becomes custom.</summary>
    public WatchStance WithFlag(StanceFlags f, bool on) => this with { Flags = (on ? Flags | f : Flags & ~f) | StanceFlags.Custom };

    // ---- the 16 bytes: (preset, flags, morale, odds, blood, pain) in one long, the protected person in the other

    public long PackA() => (long)Preset | (long)Flags << 8 | (long)FleeMoraleBelow << 16 | (long)FleeOddsBelow10 << 24 | (long)FleeBloodPm << 32 | (long)FleePainPm << 48;

    public static WatchStance Unpack(long a, ulong protect) => new((StancePreset)(byte)a, (StanceFlags)(byte)(a >> 8), (ushort)(a >> 32), (ushort)(a >> 48), (byte)(a >> 16), (byte)(a >> 24), protect);

    /// <summary>The stance as one fight input (A = the packed fields' low 32 bits, B = blood and pain, C = the guarded fighter or -1).</summary>
    public FightInput AsInput(int protectFighter) => new(FightInputKind.Stance, (int)(PackA() & 0xFFFF_FFFF), (int)(PackA() >> 32), protectFighter);

    public static WatchStance FromInput(FightInput i) => Unpack((long)(uint)i.A | (long)(uint)i.B << 32, 0);
}

/// <summary>Why the watched hero does what it does (the HUD's "why" line, 4.5). Reason codes with <c>tr()</c> text; never a trace of the RNG. Append only.</summary>
public enum WatchReason : byte
{
    None, Fighting, Attacking, Opening, Riposte, Guarding, GuardingTired, GuardingArchers, Closing, Shooting, Reloading, Fleeing, FleeingBlood, FleeingPain, FleeingOdds, FleeingMorale,
    Protecting, Treating, TreatingSelf, Over, Down, Yielded, Fled, Waiting, Subduing,
}

/// <summary>A watch interrupt raised by the fight for the game to route (watch-mode 3.2, 4.5): applied at the tick barrier, never inside the step.</summary>
public readonly record struct WatchSignal(int Tick, Travel.InterruptKind Kind, int Fighter, int Value);

/// <summary>
/// The watch state of a fight (watch-mode 3.1, live; saved only inside a saved fight): who the hero is, whether the AI drives it, since when, the stance, the last decision for the
/// "why" line, the take-back setting, the danger and losing memories and the signals waiting for the barrier.
/// </summary>
public sealed class WatchState
{
    /// <summary>The hero's fighter index.</summary>
    public int Hero = -1;
    public bool On;
    /// <summary>The fight tick watching last began (or ended).</summary>
    public int Since;
    public WatchStance Stance;
    /// <summary>The fighter the protect stance guards (-1 none).</summary>
    public int ProtectFighter = -1;
    public WatchReason LastDecision;
    public int LastDecisionArg;
    /// <summary>The fight tick of the last decision change (the line stays 3 s after a take-back).</summary>
    public int DecisionTick;
    /// <summary>Any combat input takes control back (WA1: on by default; off for zero-player watching).</summary>
    public bool TakeBackOnInput = true;
    /// <summary>A danger pause was declined: no new one before this tick (5 s).</summary>
    public int DangerCooldownUntil;
    public bool DangerActive, LosingNotified, AmmoNotified, OverRaised;
    /// <summary>The tick the odds first fell under 25% (sustained 3 s for WatchLosing), or -1.</summary>
    public int LosingSince = -1;
    /// <summary>The last estimate (permille), updated at 5 Hz.</summary>
    public int OddsPm = 500;
    public int Allies, Enemies;
    /// <summary>Raised and waiting for the barrier (the game takes them with <see cref="TakeSignals"/>).</summary>
    public readonly List<WatchSignal> Signals = [];
    /// <summary>The fight was ever watched (the chronicle says "watched").</summary>
    public bool EverOn;

    public IReadOnlyList<WatchSignal> TakeSignals() { if (Signals.Count == 0) return []; var r = Signals.ToArray(); Signals.Clear(); return r; }

    /// <summary>The <c>WatchHash</c> (6): state, stance, last decision. Joins the live hash trail, not the <c>FightHash</c> (WM-1 compares fight hashes of a watched and a plain fighter).</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        h = FightMath.Mix(h, Hero); h = FightMath.Mix(h, On ? 1 : 0); h = FightMath.Mix(h, Since); h = FightMath.Mix(h, Stance.PackA()); h = FightMath.Mix(h, (long)Stance.Protect);
        h = FightMath.Mix(h, ProtectFighter); h = FightMath.Mix(h, (long)LastDecision | (long)LastDecisionArg << 8); h = FightMath.Mix(h, OddsPm);
        h = FightMath.Mix(h, (DangerActive ? 1 : 0) | (LosingNotified ? 2 : 0) | (AmmoNotified ? 4 : 0) | (OverRaised ? 8 : 0) | (TakeBackOnInput ? 16 : 0) | (EverOn ? 32 : 0));
        return FightMath.Mix(h, DangerCooldownUntil ^ (long)LosingSince << 32);
    }

    public long[] Pack() =>
    [
        Hero, On ? 1 : 0, Since, Stance.PackA(), (long)Stance.Protect, ProtectFighter, (long)LastDecision, LastDecisionArg, DecisionTick, TakeBackOnInput ? 1 : 0, DangerCooldownUntil,
        (DangerActive ? 1 : 0) | (LosingNotified ? 2 : 0) | (AmmoNotified ? 4 : 0) | (OverRaised ? 8 : 0) | (EverOn ? 16 : 0), LosingSince, OddsPm, Allies, Enemies,
    ];

    public static WatchState Unpack(long[] s)
    {
        var w = new WatchState();
        if (s.Length < 16) return w;
        w.Hero = (int)s[0]; w.On = s[1] != 0; w.Since = (int)s[2]; w.Stance = WatchStance.Unpack(s[3], (ulong)s[4]); w.ProtectFighter = (int)s[5]; w.LastDecision = (WatchReason)s[6];
        w.LastDecisionArg = (int)s[7]; w.DecisionTick = (int)s[8]; w.TakeBackOnInput = s[9] != 0; w.DangerCooldownUntil = (int)s[10];
        long b = s[11]; w.DangerActive = (b & 1) != 0; w.LosingNotified = (b & 2) != 0; w.AmmoNotified = (b & 4) != 0; w.OverRaised = (b & 8) != 0; w.EverOn = (b & 16) != 0;
        w.LosingSince = (int)s[12]; w.OddsPm = (int)s[13]; w.Allies = (int)s[14]; w.Enemies = (int)s[15];
        return w;
    }
}
