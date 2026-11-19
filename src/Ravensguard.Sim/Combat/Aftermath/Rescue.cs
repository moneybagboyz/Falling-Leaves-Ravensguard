using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Aftermath;

/// <summary>A guard post of the captors' camp (millimetres from the cell) and how far the guard sees by day; at night a third of that.</summary>
public readonly record struct GuardPost(int X, int Y, int SightMm = 20_000);

/// <summary>How a live rescue went: freed or not, how many sim-seconds it took, where the rescuer was stopped and why.</summary>
public sealed record RescueOutcome(bool Freed, int Seconds, bool Seen, string Text);

/// <summary>
/// A rescue by the companion the player controls (morale-capture-succession 4.7): the rescuer creeps toward the cell at 1 m/s from its start; each sim-second inside an awake guard's sight it
/// rolls the guard's notice (the captive's notice rule with the rescuer's Stealth, stream 0x453 on GameSec / 20 windows); seen, the rescue fails and the rescuer is driven off. At the cell it
/// cuts the bonds (5 s) or works the lock (Lockpicking against the lock, a roll every 90 s on stream 0x455), and the captive walks free with it. Deterministic for a seed and a start second.
/// </summary>
public static class RescueRun
{
    public static RescueOutcome Run(AftermathRules r, ulong seed, Captive c, EntityId rescuer, int stealth, int lockpicking, int startX, int startY, IReadOnlyList<GuardPost> guards, long startSec,
        int maxSeconds = 1800)
    {
        long x = startX, y = startY;
        int awake = EscapeTable.GuardsAwake(c.Captor, Math.Max(c.Guards, guards.Count), startSec);
        for (int s = 0; s < maxSeconds; s++)
        {
            long sec = startSec + s;
            bool dark = EscapeTable.Dark(sec);
            long d = Melee2.FightMath.Len(x, y);
            if (d <= 1000)
            {
                if (c.Restraint == Restraint.Bound || c.Restraint == Restraint.None) return new RescueOutcome(true, s + 5, false, "MC_RESCUE_CUT");
                for (int k = 0; s + 90 * (k + 1) < maxSeconds; k++)
                {
                    long at = sec + 90 * (k + 1);
                    if (EscapeTable.Roll(seed, at / 20, rescuer, RngStreams.LockPick, 0x100 + (uint)k) < EscapeTable.LockPm(r, lockpicking, c.Captor, false))
                        return new RescueOutcome(true, s + 90 * (k + 1), false, "MC_RESCUE_PICKED");
                    if (Seen(r, seed, rescuer, stealth, guards, awake, 0, 0, at, dark, 0x200 + (uint)k)) return new RescueOutcome(false, s + 90 * (k + 1), true, "MC_RESCUE_SEEN");
                }
                return new RescueOutcome(false, maxSeconds, false, "MC_RESCUE_TIME");
            }
            if (Seen(r, seed, rescuer, stealth, guards, awake, x, y, sec, dark, (uint)s)) return new RescueOutcome(false, s, true, "MC_RESCUE_SEEN");
            // creep 1 m toward the cell (integer steps along the larger axis first, then the other)
            long step = Math.Min(1000, d);
            x -= x * step / Math.Max(1, d); y -= y * step / Math.Max(1, d);
        }
        return new RescueOutcome(false, maxSeconds, false, "MC_RESCUE_TIME");
    }

    private static bool Seen(AftermathRules r, ulong seed, EntityId who, int stealth, IReadOnlyList<GuardPost> guards, int awake, long x, long y, long sec, bool dark, uint draw)
    {
        for (int i = 0; i < guards.Count && i < awake; i++)
        {
            var g = guards[i];
            long dx = x - g.X, dy = y - g.Y, sight = dark ? g.SightMm / 3 : g.SightMm;
            if (dx * dx + dy * dy > sight * sight) continue;
            // a notice roll per GameSec / 20 window and guard (the per-second check reads the window's one roll)
            if (sec % 20 != 0) continue;
            if (EscapeTable.Roll(seed, sec / 20, who, RngStreams.CaptiveEvent, 0x1000 + (uint)i * 64 + (draw & 63)) < EscapeTable.NoticePm(r, stealth, dark)) return true;
        }
        return false;
    }
}
