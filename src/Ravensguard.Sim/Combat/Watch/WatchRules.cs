using Ravensguard.Sim.Combat.Aftermath;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>
/// The rules around watching (watch-mode 4.1, 4.6): allowed in every run, Ironman included, with the same stakes (a death in watch mode is a death: succession applies, WM-8);
/// no damage reduction, no hidden dodge, no instant skip. Pure.
/// </summary>
public static class WatchRules
{
    /// <summary>Watch mode is offered in every fight of every run (WA2: small fights too; plan 3i: allowed in Ironman).</summary>
    public static bool Allowed(bool ironman) => true;

    /// <summary>A defeat while watched is the defeat table's like any other; the hero killed goes to Ironman succession (SU-1) exactly as in manual play.</summary>
    public static bool GoesToSuccession(DefeatOutcome o, bool ironman) => o.Killed || o.Kind == DefeatKind.Killed;
}

/// <summary>
/// Speed while a fight is on (watch-mode 4.1, overview 6.1): a frame steps the bubble <c>speed</c> times at 20 Hz, never a longer step, so the same fight at 1x, 2x, 4x and 8x is the
/// same fight (WM-9); a frame that took longer than the 25 ms budget drops one level (the HUD toasts it); watching caps the speed (default 4x).
/// </summary>
public static class WatchSpeed
{
    /// <summary>How many fight steps the next frame takes at <paramref name="speed"/> (1, 2, 4, 8), one level lower when the last frame's steps took over the budget.</summary>
    public static int StepsPerFrame(int speed, double frameMs, double budgetMs = 25)
    {
        int s = speed is 1 or 2 or 4 or 8 ? speed : 1;
        return frameMs > budgetMs && s > 1 ? s / 2 : s;
    }

    /// <summary>The speed allowed while watching: at most the cap (a setting, 4x by default).</summary>
    public static int Capped(int speed, int cap) => Math.Max(1, Math.Min(speed, cap));
}
