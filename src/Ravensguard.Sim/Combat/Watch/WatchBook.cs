using System.Globalization;
using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Combat.Watch;

/// <summary>
/// The watch settings of the run (watch-mode 3.1, 7; saved in the optional "watch" section once anything is set): each person's stance (the hero's and the party's, 16 bytes each),
/// "keep watching" across fights (WA5), take control on any combat input (WA1) and the speed cap while watching. The hero's commands change it at the barrier
/// (<see cref="WatchDesk"/>). The no-lethal toggle is one flag with autopilot's <see cref="LimitSet.NoLethalForce"/> (WM-10): for a person with an autopilot row the limit is
/// the flag, and setting either sets it.
/// </summary>
public sealed class WatchBook
{
    public SortedDictionary<ulong, WatchStance> Stances { get; } = [];
    public bool KeepWatching { get; set; }
    public bool TakeBackOnInput { get; set; } = true;
    /// <summary>The fastest speed while watching (default 4x, plan 7).</summary>
    public int SpeedCap { get; set; } = 4;

    public bool IsEmpty => Stances.Count == 0 && !KeepWatching && TakeBackOnInput && SpeedCap == 4;

    /// <summary>The person's stance: the saved one (else "balanced"), with the no-lethal flag read from the person's autopilot limits when there is a row.</summary>
    public WatchStance StanceOf(FightContent c, EntityId person, AutopilotBook? ap = null)
    {
        var s = Stances.TryGetValue(person.Value, out var v) ? v : WatchStance.Of(c, StancePreset.Balanced);
        if (ap != null && ap.States.TryGetValue(person, out var row) && row.Limits.NoLethalForce != s.NoLethal) s = s.WithFlag(StanceFlags.NoLethal, row.Limits.NoLethalForce);
        return s;
    }

    /// <summary>Sets a person's stance; its no-lethal flag goes into the person's autopilot limits too (one flag).</summary>
    public void Set(EntityId person, WatchStance s, AutopilotBook? ap = null)
    {
        Stances[person.Value] = s;
        if (ap != null && ap.States.TryGetValue(person, out var row)) row.Limits.NoLethalForce = s.NoLethal;
    }

    /// <summary>The no-lethal toggle from either panel (the stance panel, the autopilot limits): both show it at once.</summary>
    public void SetNoLethal(FightContent c, EntityId person, bool on, AutopilotBook? ap = null) => Set(person, StanceOf(c, person, ap).WithFlag(StanceFlags.NoLethal, on), ap);

    public bool NoLethal(FightContent c, EntityId person, AutopilotBook? ap = null) => StanceOf(c, person, ap).NoLethal;

    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        foreach (var (p, s) in Stances) { h = FightMath.Mix(h, (long)p); h = FightMath.Mix(h, s.PackA()); h = FightMath.Mix(h, (long)s.Protect); }
        return FightMath.Mix(h, (KeepWatching ? 1 : 0) | (TakeBackOnInput ? 2 : 0) | SpeedCap << 4);
    }
}

/// <summary>
/// The hero's watch commands at the barrier (<see cref="HeroCommandKind.Watch"/>; the tag says what): "wm:stance|person|packed|protect", "wm:keep|0/1", "wm:takeback|0/1",
/// "wm:cap|speed", "wm:nolethal|person|0/1". Recorded with the hero's commands, so a replay sets the same.
/// </summary>
public static class WatchDesk
{
    public const string Prefix = "wm:";

    public static string StanceTag(EntityId person, WatchStance s) => string.Create(CultureInfo.InvariantCulture, $"wm:stance|{person.Value}|{s.PackA()}|{s.Protect}");
    public static string FlagTag(string what, bool on) => $"wm:{what}|{(on ? 1 : 0)}";
    public static string CapTag(int speed) => string.Create(CultureInfo.InvariantCulture, $"wm:cap|{speed}");
    public static string NoLethalTag(EntityId person, bool on) => string.Create(CultureInfo.InvariantCulture, $"wm:nolethal|{person.Value}|{(on ? 1 : 0)}");

    public static WatchBook BookOf(Save.WorldState s) => s.Watch ??= new WatchBook();

    /// <summary>Applies one watch command to the book (and, for no-lethal, to the person's autopilot limits).</summary>
    public static bool Apply(WatchBook book, string tag, FightContent? c, AutopilotBook? ap)
    {
        if (!tag.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var p = tag[Prefix.Length..].Split('|');
        long L(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        ulong U(int i) => i < p.Length && ulong.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        switch (p[0])
        {
            case "stance" when p.Length >= 4: book.Set(new EntityId(U(1)), WatchStance.Unpack(L(2), U(3)), ap); return true;
            case "keep": book.KeepWatching = L(1) != 0; return true;
            case "takeback": book.TakeBackOnInput = L(1) != 0; return true;
            case "cap": book.SpeedCap = (int)Math.Clamp(L(1), 1, 8); return true;
            case "nolethal" when c != null: book.SetNoLethal(c, new EntityId(U(1)), L(2) != 0, ap); return true;
        }
        return false;
    }

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        var fc = c.Tag.StartsWith("wm:nolethal", StringComparison.Ordinal) ? new FightContent(new CombatTables(ctx.Content), ctx.Content) : null;
        bool ok = Apply(BookOf(ctx.State), c.Tag, fc, ctx.State.Autopilot);
        return new HeroResult(c.Seq, c.Kind, ok ? HeroResultCode.Ok : HeroResultCode.Invalid, HeroText.Of(ok ? "wm_set" : "wm_bad"), 0, 0, c.IssuedSec);
    }
}

/// <summary>
/// Routes the watch signals of a fight at the barrier (watch-mode 3.2): through the autopilot router when the hero is on autopilot (its dedupe, log and chronicle), else straight onto
/// the interrupt bus with the watch defaults (WatchDanger and WatchOver pause, WatchLosing and WatchAmmoOut notify). The bus does what the policy says; the game reads it.
/// </summary>
public static class WatchInterrupts
{
    public static IReadOnlyList<(WatchSignal Signal, InterruptPolicy Policy)> Route(IReadOnlyList<WatchSignal> signals, InterruptBus bus, long sec, AutopilotState? ap = null, InterruptRouter? router = null, string text = "")
    {
        var r = new List<(WatchSignal, InterruptPolicy)>();
        foreach (var s in signals)
        {
            if (ap != null && router != null) { var res = router.Raise(ap, bus, s.Kind, sec, 0, 0, text); if (!res.Duplicate) r.Add((s, res.Policy)); continue; }
            r.Add((s, bus.Raise(s.Kind, sec, 0, 0, text).Policy));
        }
        return r;
    }
}
