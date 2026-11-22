using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Battle;

// The bubble's tick (battle-bubble-and-formations 4.1): soldiers entering at the box edge (at most 50 a tick over all sides, shared between the sides that have some), the wave check (once a second per side,
// a wave at most every 5 s), the fight's own step, then the close check. The order is fixed and every decision reads only the fight's state, so a save between two ticks continues the same.
public sealed partial class BattleBubble
{
    /// <summary>One 20 Hz tick of the battle under the ledger's lock (the live tier's rule: the tick worker never reads a book half-way through a step).</summary>
    public void StepShared()
    {
        if (W.Ledger is { } l) lock (l.Sync) Step();
        else Step();
    }

    /// <summary>One 20 Hz tick of the battle; does nothing once it has closed.</summary>
    public void Step()
    {
        if (Closed) return;
        Enter();
        int tick = W.Tick;
        if (tick % Math.Max(1, Rules.WaveCheckTicks) == 0) Waves(tick);
        W.StepWithAi();
        if (W.Tick % 100 == 0) RecentreLod();                              // after the step, so the formations' contact flags are this tick's (they are derived, not saved)
        if (W.Tick % Math.Max(1, Rules.CloseCheckTicks) == 0) CheckClose();
    }

    /// <summary>
    /// Where the AI thinks at 20 Hz (the near zone of the level of detail), every 5 s: at the hero when he is on his feet, else at the middle of the formations in contact, else where it was. A function of the
    /// fight's state and saved with the bubble; the camera is never read.
    /// </summary>
    private void RecentreLod()
    {
        if (Hero >= 0 && W.Fighters[Hero].Active) { W.LodCentre = (W.Fighters[Hero].X, W.Fighters[Hero].Y); return; }
        long sx = 0, sy = 0, n = 0;
        foreach (var r in W.Formations!.Rows) if (r.InContact && r.ActiveNow > 0) { sx += (long)r.AnchorX * r.ActiveNow; sy += (long)r.AnchorY * r.ActiveNow; n += r.ActiveNow; }
        if (n > 0) W.LodCentre = ((int)(sx / n), (int)(sy / n));
    }

    // ------------------------------------------------------------------ people entering at the edge

    /// <summary>
    /// Soldiers enter: the LODBridge's 50 a frame, shared between the sides that have people waiting at the edge (an equal share, the remainder to the sides in order, what a short side leaves to the rest). A
    /// unit stands formed the tick its last soldier is in; until then its men stand on the slots they will hold (a tick or two, at the box edge, far from any foe).
    /// </summary>
    private void Enter()
    {
        int budget = Rules.PromotePerTick, entered = 0;
        var want = new List<BattleSide>();
        foreach (var s in Sides) if (s.Pending.Count > 0) want.Add(s);
        for (int pass = 0; pass < 2 && budget > 0 && want.Count > 0; pass++)
        {
            int share = budget / want.Count, rem = budget % want.Count, spent = 0;
            for (int i = 0; i < want.Count; i++)
            {
                int take = share + (i < rem ? 1 : 0);
                if (pass == 1) take = budget - spent;
                spent += EnterSide(want[i], take);
            }
            entered += spent; budget -= spent;
            want.RemoveAll(s => s.Pending.Count == 0);
        }
        if (entered > MaxEnteredInATick) MaxEnteredInATick = entered;
    }

    private int EnterSide(BattleSide s, int take)
    {
        int did = 0;
        while (take > 0 && s.Pending.Count > 0)
        {
            var u = s.Pending[0];
            var def = Ravensguard.Sim.Battle.FormationRules.For(W.Content.Db).Shape(u.Shape, u.Kind);
            int ranks = u.Ranks > 0 ? u.Ranks : Math.Max(1, def.Ranks), files = Math.Max(1, (u.Count + ranks - 1) / ranks);
            while (take > 0 && u.Entered < u.Count) { AddOne(u, s, u.Entered, files, def, s.FaceX, s.FaceY, false); take--; did++; }
            if (u.Entered == u.Count) { s.Pending.RemoveAt(0); FormUnit(u, s, -1, 0); }
        }
        return did;
    }

    // ------------------------------------------------------------------ reinforcement waves

    private int ActiveOf(int team) { int n = 0; foreach (var f in W.Fighters) if (f.Team == team && f.Active) n++; return n; }

    /// <summary>The men of a side the cap counts: everyone who has not died, fled or yielded. A man who is down may get up again, so he still holds his place.</summary>
    public int PresentOf(int team) { int n = 0; foreach (var f in W.Fighters) if (f.Team == team && f.Act is not (FAct.Dead or FAct.Fled or FAct.Surrendered)) n++; return n; }

    private void Waves(int tick)
    {
        foreach (var s in Sides)
        {
            if (s.Reserve.Count == 0 || tick - s.LastWaveTick < Rules.WaveGapTicks) continue;
            int active = ActiveOf(s.Team), entering = 0;
            foreach (var u in s.Pending) entering += u.Count - u.Entered;
            // the cap is the most the side has live at once: what is standing, what is walking in, and what the wave would add never pass it
            int room = s.LiveCap - PresentOf(s.Team) - entering;
            if (room <= 0) continue;
            bool under = (long)active * 1000 < (long)Rules.WaveUnderPm * s.LiveCap;
            if (!under)
                foreach (var r in W.Formations!.Rows)
                    if (r.Side == s.Team && r.StartCount > 0 && (long)ActiveIn(r) * 1000 < (long)Rules.WaveUnderPm * r.StartCount) { under = true; break; }
            if (!under) continue;
            int max = Math.Min(Math.Max(1, CapTotal / Math.Max(1, Rules.WaveDiv)), room);
            var units = TakeFromReserve(s, max);
            if (units.Count == 0) continue;
            s.WavesSent++; s.LastWaveTick = tick;
            // the wave forms along the box edge at the side's entry point, a little inside, side by side, each unit's anchor jittered along the entry road (stream 0x781)
            var rules = FormationRules.For(W.Content.Db);
            int fx = s.FaceX, fy = s.FaceY;
            int bx = s.EntryX + (int)((long)fx * Rules.EntryInsetMm >> 10), by = s.EntryY + (int)((long)fy * Rules.EntryInsetMm >> 10);
            var anchors = LayOutWave(rules, units, bx, by, fx, fy);
            for (int k = 0; k < units.Count; k++)
            {
                int j = DetRng.Range(W.Seed, s.WavesSent, ((ulong)Id << 12) ^ (ulong)(uint)s.Team, RngStreams.WaveEntryJitter, (uint)k, 0, Math.Max(1, Rules.EntryJitterMm));
                units[k].Wave = s.WavesSent;
                units[k].AnchorX = anchors[k].X - (int)((long)fx * j >> 10); units[k].AnchorY = anchors[k].Y - (int)((long)fy * j >> 10);
                s.Pending.Add(units[k]);
            }
        }
    }

    /// <summary>A wave's units side by side across the entry (a single line centred on the entry; a wide wave wraps to lines behind).</summary>
    private (int X, int Y)[] LayOutWave(FormationRules rules, List<BattleUnit> units, int bx, int by, int fx, int fy)
        => LayOut(rules, units, bx, by, fx, fy, 0, centred: true);       // a wave's front stands on the point itself (distance 0); a wide wave wraps to lines behind it

    // ------------------------------------------------------------------ the close

    private void CheckClose()
    {
        var rows = W.Formations!.Rows;
        bool engaged = false;
        foreach (var r in rows) if (r.InContact && r.ActiveNow > 0) { engaged = true; break; }
        if (!engaged) foreach (var p in W.Projectiles) if (!p.Done) { engaged = true; break; }
        if (engaged) { ContactSeen = true; LastEngaged = W.Tick; }
        // a side with nobody on its feet in the box, nobody waiting and nobody walking in is out of the battle
        int alive = 0, lastAlive = -1;
        for (int i = 0; i < Sides.Count; i++)
        {
            var s = Sides[i];
            bool any = s.Reserve.Count > 0 || s.Pending.Count > 0;
            if (!any) foreach (var f in W.Fighters) if (f.Team == s.Team && f.Active && InBox(f.X, f.Y)) { any = true; break; }
            if (any) { alive++; lastAlive = i; }
        }
        if (alive <= 1) { Finish(truce: false); return; }
        int quiet = W.Tick - (ContactSeen ? LastEngaged : 0);
        if (ContactSeen ? quiet >= Rules.QuietCloseS * 20 : W.Tick >= Rules.NoContactCloseS * 20) Finish(truce: false);
    }

    /// <summary>The sides agree a truce: the battle closes now with the outcome "truce".</summary>
    public void CallTruce() { if (!Closed) Finish(truce: true); }
}
