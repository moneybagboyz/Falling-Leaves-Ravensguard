using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// Watch mode in the live fight (watch-mode.md, P4-F), a partial of the fight: the hand-off and take-back at the tick barrier (the input is applied between steps; the swing in
/// progress completes under the brain, a held guard stays), the hero's stance (the same <see cref="CombatStanceDef"/> rows the NPCs use: there is no hero code in the brain, WM-1),
/// the 5 Hz odds and danger checks that raise <see cref="WatchSignal"/>s for the game's interrupt router, and the "why" line. Draws nothing (watch mode has its own reserved stream, which is never drawn).
/// </summary>
public sealed partial class FightWorld
{
    /// <summary>The watch state of the hero's fighter, or null when nobody was ever handed to the AI or given a stance by the player.</summary>
    public WatchState? Watch { get; private set; }
    /// <summary>Rules a setup gives the fight (practice to three clean hits, a tournament's yield at pain knock-out, treating in the fight).</summary>
    public FightRules Rules { get; set; } = FightRules.Plain;
    /// <summary>The kind of the setup that built the fight (null: a harness fight); a record captured without a kind keeps this one, so a replay rebuilds the same rules.</summary>
    public FightKind? SetupKind { get; set; }
    /// <summary>This fighter is always in the 20 Hz group of the AI (the hero, watched or not: set by the setup, the same for a watched hero and a plain NPC in its place).</summary>
    public int LodFighter = -1;
    /// <summary>Test hook (WM-3): every random draw the combat brain makes, as (fighter, stream). Null in play.</summary>
    public Action<int, uint>? AiDrawLog;

    /// <summary>The watch state for <paramref name="hero"/> (made on first use with the content's "balanced").</summary>
    public WatchState EnsureWatch(int hero)
    {
        if (Watch is { } w && w.Hero == hero) return w;
        Watch = new WatchState { Hero = hero, Stance = WatchStance.Of(Content, WatchStance.PresetOf(hero >= 0 && hero < Fighters.Count ? Fighters[hero].Stance.Id : "balanced")) };
        return Watch;
    }

    /// <summary>A saved fight's watch state comes back.</summary>
    public void RestoreWatch(WatchState? w) => Watch = w;

    /// <summary>Hands the fighter to the brain (<paramref name="on"/>) or gives it back to the player's inputs, effective from the next step (watch-mode 4.1).</summary>
    public void SetWatch(int fighter, bool on, bool takeBackOnInput = true)
    {
        if (fighter < 0 || fighter >= Fighters.Count) return;
        var f = Fighters[fighter]; var w = EnsureWatch(fighter); var c = f.Cmd;
        w.TakeBackOnInput = takeBackOnInput;
        if (w.On == on) return;
        w.On = on; w.Since = Tick; w.EverOn |= on;
        if (on)
        {
            f.Player = false;
            // the brain adopts the swing in progress: it releases at the end of its wind-up (a held heavy swing goes at once); the held guard stays
            if (f.Act == FAct.Windup && !f.Req.Auto) { f.Req.Auto = true; f.ImpactAt = Math.Max(Tick + 1, f.WindStart + f.WindNeeded - 1); }
            if (f.Act is FAct.Drawing or FAct.Aiming && !f.Req.Auto) f.Req.Auto = true;
            c.Hold = false; c.ClearOneShots(); c.MoveX = c.MoveY = 0; c.Sprint = false;
            f.Brain.Target = f.Brain.Target >= 0 && f.Brain.Target < Fighters.Count ? f.Brain.Target : -1;
        }
        else
        {
            // control returns: what the brain held (the walk, the facing, the sprint) stops; a swing it began completes on its own; the player's input of this tick applies next
            f.Player = true;
            c.ClearOneShots(); c.MoveX = c.MoveY = 0; c.FaceX = c.FaceY = 0; c.Sprint = false; c.Hold = false; c.Block = false;
            w.DecisionTick = Tick;
        }
    }

    /// <summary>Sets a fighter's stance (the hero's from the HUD, an order to a follower): the brain reads it from the next decision; a no-lethal stance puts a blade without a flat away.</summary>
    public void SetStance(int fighter, WatchStance st, int protectFighter = -1)
    {
        if (fighter < 0 || fighter >= Fighters.Count) return;
        var f = Fighters[fighter];
        f.Stance = st.ToDef(Content);
        f.ProtectFighter = protectFighter >= 0 && protectFighter < Fighters.Count && protectFighter != fighter ? protectFighter : -1;
        if (Watch is { } w && w.Hero == fighter) { w.Stance = st; w.ProtectFighter = f.ProtectFighter; }
        SheatheFor(f);
    }

    /// <summary>A no-lethal stance with no blunt attack on the weapon fights with fists (watch-mode 4.3); a lethal one takes the kit's weapon back out.</summary>
    internal void SheatheFor(Fighter f)
    {
        bool blunt = false;
        foreach (var a in f.Weapon.Attacks) if (a.Type == Ravensguard.Content.AttackType.Blunt) blunt = true;
        if (f.Stance.NoLethal && !blunt && f.Launcher == null && f.Improvised == null && f.Weapon.Shape.Family is not ("natural" or "unarmed"))
        {
            Disarm(f); f.Sheathed = true;
        }
        else if (!f.Stance.NoLethal && f.Sheathed && f.Kit.Weapon is { } kw)
        {
            f.Sheathed = false;
            Wield(f, T.Weapon(kw.ShapeId, kw.MaterialId, kw.Quality));
            f.Skill = f.Kit.Skill;
        }
    }

    /// <summary>Puts <paramref name="w"/> in the fighter's hand (reach and timings from its shape).</summary>
    internal void Wield(Fighter f, Resolver2.WeaponV2 w)
    {
        f.Weapon = w;
        var sh = w.Shape;
        f.Family = FamilyOf(sh); f.ReachMm = Math.Max(500, sh.ReachMm); f.StrikeTicks = Math.Max(1, sh.StrikeTicks);
        f.BaseWindTicks = TicksOf(sh.WindUpS); f.BaseRecoverTicks = TicksOf(sh.RecoverS);
    }

    /// <summary>The zones a no-lethal blow never lands on (head, neck, upper body and what they host): passed to the resolver as parts it cannot reach, so a missed blow never
    /// slips onto them either (the redirect skips a missing part). Only no-lethal attackers ask, once per blow.</summary>
    public static ulong NoLethalMask(Resolver2.BodyPlanV2 plan)
    {
        ulong m = 0;
        foreach (var p in plan.Parts)
        {
            if (p.Index >= 64) continue;
            var host = p.IsHosted ? plan.Parts[p.Host] : p;
            if (p.BaseId is "head" or "neck" or "upper_body" || host.BaseId is "head" or "neck" or "upper_body") m |= 1UL << p.Index;
        }
        return m;
    }

    /// <summary>The fighter the protect stance guards, when it is still there to guard.</summary>
    public Fighter? ProtectOf(Fighter f)
    {
        if (!f.Stance.Protect || f.ProtectFighter < 0 || f.ProtectFighter >= Fighters.Count) return null;
        var x = Fighters[f.ProtectFighter];
        return x.Act is FAct.Dead or FAct.Fled || x.Team != f.Team ? null : x;
    }

    // ------------------------------------------------------------------ inputs (recorded)

    private void WatchInput(Fighter f, FightInput i)
    {
        switch (i.Kind)
        {
            case FightInputKind.Watch: SetWatch(f.Id, i.A != 0, i.B != 0); break;
            case FightInputKind.Stance: SetStance(f.Id, WatchStance.FromInput(i) with { Protect = i.C >= 0 && i.C < Fighters.Count ? Fighters[i.C].Person : 0 }, i.C); break;
            case FightInputKind.Formation: SetFormation(f.Team, (Formation)i.A); break;
            case FightInputKind.Order: Order(f.Team, (SquadOrderKind)i.A, i.B); break;
            case FightInputKind.DeclineDanger: { var w = EnsureWatch(f.Id); w.DangerCooldownUntil = Tick + DangerCooldownTicks; break; }
        }
    }

    // ------------------------------------------------------------------ the 5 Hz watch step (after the fight step; signals wait for the barrier)

    public const int WatchEveryTicks = 4, DangerCooldownTicks = 100, LosingSustainTicks = 60;

    private void WatchStep()
    {
        var w = Watch;
        if (w == null || w.Hero < 0 || w.Hero >= Fighters.Count) return;
        var f = Fighters[w.Hero];
        if (w.On) Why(w, f);
        if (IsOver)
        {
            if (w.On && !w.OverRaised) { w.OverRaised = true; w.Signals.Add(new WatchSignal(Tick, InterruptKind.WatchOver, f.Id, WinnerTeam == f.Team ? 1 : 0)); }
            return;
        }
        if (Tick % WatchEveryTicks != 0) return;
        var (odds, allies, enemies) = RiskEstimate.Odds(this, f);
        w.OddsPm = odds; w.Allies = allies; w.Enemies = enemies;
        if (!w.On || f.Act is FAct.Dead or FAct.Surrendered or FAct.Fled || !f.Book.Conscious) return;     // a collapsed hero still hears the warning
        // WatchDanger (4.5): blood loss 30%, pain 85% of the knock-out, morale under 30, an undressed arterial bleed of 60 ml/s; a declined pause waits 5 s
        var b = f.Book;
        int painPm = (int)Math.Min(1000, b.PainX100 * 1000L / Math.Max(1, b.KnockoutPainX100));
        int worst = WorstBleedX10(b);
        int reason = b.LossPm >= 300 ? 1 : painPm >= 850 ? 2 : f.M.ValueX100 > 0 && f.M.ValueX100 < 3000 ? 3 : worst >= 600 ? 4 : 0;
        bool danger = reason != 0;
        if (danger && !w.DangerActive && Tick >= w.DangerCooldownUntil) w.Signals.Add(new WatchSignal(Tick, InterruptKind.WatchDanger, f.Id, reason));
        w.DangerActive = danger;
        // WatchLosing: odds under 25% for 3 s, once a fight
        if (odds < 250) { if (w.LosingSince < 0) w.LosingSince = Tick; else if (!w.LosingNotified && Tick - w.LosingSince >= LosingSustainTicks) { w.LosingNotified = true; w.Signals.Add(new WatchSignal(Tick, InterruptKind.WatchLosing, f.Id, odds)); } }
        else w.LosingSince = -1;
        // WatchAmmoOut: an archer with nothing left to loose
        if (f.Launcher != null && !f.Loaded && f.Ammo <= 0 && !w.AmmoNotified) { w.AmmoNotified = true; w.Signals.Add(new WatchSignal(Tick, InterruptKind.WatchAmmoOut, f.Id, 0)); }
    }

    /// <summary>The fastest undressed external bleed (ml/s x 10).</summary>
    public static int WorstBleedX10(WoundBook b)
    {
        int best = 0;
        foreach (var r in b.Records) if (!r.Internal && r.Dressing == Dressing.None && !r.Is(Aid.Resolved) && r.State != WoundState.Scar && r.BleedX10 > best) best = r.BleedX10;
        return best;
    }

    /// <summary>The hero's current decision as a reason code (the "why" line): read from the brain's memory and the body, never from a draw.</summary>
    private void Why(WatchState w, Fighter f)
    {
        var (r, arg) = WatchWhy.Of(this, f);
        if (r != w.LastDecision || arg != w.LastDecisionArg) { w.LastDecision = r; w.LastDecisionArg = arg; w.DecisionTick = Tick; }
    }
}
