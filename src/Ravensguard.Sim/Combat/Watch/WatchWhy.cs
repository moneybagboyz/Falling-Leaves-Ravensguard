using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>
/// The "why" line of the watched hero (watch-mode 4.5): what the brain is doing and the number that explains it ("Guarding: stamina 22", "Fleeing: blood loss 26%",
/// "Treating Edda: bleeding 40 ml/s"). Read from the fighter's state and the brain's memory after the step; it never draws and never changes anything.
/// </summary>
public static class WatchWhy
{
    /// <summary>The reason code and its argument (a percentage, ml/s, metres, or a fighter index for the treated ally).</summary>
    public static (WatchReason Reason, int Arg) Of(FightWorld w, Fighter f)
    {
        var b = f.Book;
        if (w.IsOver) return (WatchReason.Over, f.Book.Records.Count);
        switch (f.Act)
        {
            case FAct.Dead: return (WatchReason.Down, 0);
            case FAct.Down: return (WatchReason.Down, b.LossPm / 10);
            case FAct.Surrendered: return (WatchReason.Yielded, 0);
            case FAct.Fled: return w.Tick < f.TreatBusyUntil ? (WatchReason.TreatingSelf, FightWorld.WorstBleedX10(b) / 10) : (WatchReason.Fled, 0);
        }
        if (w.Tick < f.TreatBusyUntil) return (WatchReason.Treating, f.Brain.Target);
        if (f.Routing)
        {
            int painPm = (int)Math.Min(1000, b.PainX100 * 1000L / Math.Max(1, b.KnockoutPainX100));
            if (f.Stance.FleeBloodLossPm > 0 && b.LossPm >= f.Stance.FleeBloodLossPm) return (WatchReason.FleeingBlood, b.LossPm / 10);
            if (f.Stance.FleeChecks && painPm >= f.Stance.FleePainPm) return (WatchReason.FleeingPain, painPm / 10);
            if (f.Stance.FleeChecks && f.Stance.FleeOddsBelowPm > 0 && w.Watch is { } ws && ws.OddsPm < f.Stance.FleeOddsBelowPm) return (WatchReason.FleeingOdds, ws.OddsPm / 10);
            return (WatchReason.FleeingMorale, f.Morale);
        }
        if (w.ProtectOf(f) is { } x && w.DistMm(f, x) > 2500) return (WatchReason.Protecting, x.Id);
        switch (f.Act)
        {
            case FAct.Windup or FAct.Strike:
                if (f.Riposte) return (WatchReason.Riposte, 0);
                if (f.Stance.NoLethal) return (WatchReason.Subduing, 0);
                return f.Req.Target >= 0 && f.Req.Target < w.Fighters.Count && w.Fighters[f.Req.Target].Act is FAct.Recover or FAct.Stagger or FAct.Down
                    ? (WatchReason.Opening, 0) : (WatchReason.Attacking, 0);
            case FAct.Drawing or FAct.Aiming: return (WatchReason.Shooting, 0);
            case FAct.Reloading: return (WatchReason.Reloading, f.Ammo);
            case FAct.Block or FAct.Recover:
                if (f.Stamina < w.K.AiGuardBelowX100) return (WatchReason.GuardingTired, f.Stamina / 100);
                if (f.Stance.ShieldToArchers && f.Shield != null && NearestArcherM(w, f) is int m and > 0) return (WatchReason.GuardingArchers, m);
                return (WatchReason.Guarding, f.Stamina / 100);
        }
        var t = f.Brain.Target >= 0 && f.Brain.Target < w.Fighters.Count ? w.Fighters[f.Brain.Target] : null;
        if (t == null) return (WatchReason.Waiting, 0);
        long d = w.DistMm(f, t);
        if (d > f.ReachMm + w.K.ReachSlackMm) return (WatchReason.Closing, (int)(d / 1000));
        return (WatchReason.Fighting, w.Watch?.OddsPm / 10 ?? 50);
    }

    /// <summary>Metres to the nearest enemy archer within 40 m, or -1.</summary>
    public static int NearestArcherM(FightWorld w, Fighter f)
    {
        long best = long.MaxValue;
        foreach (var o in w.Fighters) if (o.Team != f.Team && o.Active && o.Ranged) best = Math.Min(best, w.DistMm(f, o));
        return best <= 40_000 ? (int)(best / 1000) : -1;
    }

    /// <summary>The <c>tr()</c> key of a reason (the HUD fills the argument).</summary>
    public static string Key(WatchReason r) => "wm_why_" + Flavour.FlavourBook.Snake(r.ToString());
}
