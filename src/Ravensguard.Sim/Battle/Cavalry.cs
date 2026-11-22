using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

// P7-CV-A: cavalry formations and the charge (cavalry-and-mounted-war 3.1, 4.1, 4.2). A formation of Kind.Cavalry is a row of the same book as any other: its riders hold slots (a line two ranks deep
// at 1.5 m x 4 m, a wedge, a loose order, a column), its anchor walks, trots or gallops by the horses' own gaits, and a Charge order runs the sequence: walk beyond 200 m, trot, gallop from 60 m,
// couch at 20 m. The formation adds cohesion and shock (morale) and nothing else: every blow is the rider's own resolver blow with the horse's momentum (the Phase 4 mounted layer, untouched), and
// every number here is an integer. After contact a rider still above 4 m/s rides on; the formation rides 30 m on, then 80 m, wheeling back at its own turn rate, and is whole again within 15 s;
// a rider slower than that, or slow for 4 s, is in a melee and fights as a Phase 4 rider. "Charge and press" (Arg 1) stays in the melee instead.
public sealed partial class FormationBook
{
    /// <summary>A member's cavalry flags: he rode through the contact; he is in a melee and fights alone.</summary>
    public const int CfThrough = 1, CfMelee = 2;
    /// <summary>The charge after contact (<see cref="FormationRow.CavPhase"/>).</summary>
    public const int PhApproach = 0, PhRideOn = 1, PhReform = 2, PhDone = 3;
    /// <summary>The charge stages (<see cref="FormationRow.Stage"/>) and the gaits.</summary>
    public const int StWalk = 1, StTrot = 2, StGallop = 3, StCouch = 4;
    public const int GaitWalk = 0, GaitTrot = 1, GaitGallop = 2;

    private CavalryRulesDef Cv => Rules.Cav;

    private static void InitCavalry(FormationRow r, int n)
    {
        r.ContactTick = new int[n]; r.CavFlags = new int[n]; r.Stuck = new int[n]; r.CavCap = new int[n]; r.CavCapTick = new int[n];
        Array.Fill(r.ContactTick, -1); Array.Fill(r.CavCapTick, -1);
        r.StageTick = [-1, -1, -1, -1, -1];
    }

    private void ResetCharge(FormationRow r)
    {
        r.Stage = 0; Array.Fill(r.StageTick, -1);
        r.CavPhase = PhApproach; r.PhaseTick = 0;
        r.FirstContactTick = -1; r.CohAtContact = 0; r.ShockTick = -1; r.ShockHit = 0; r.ReformedTick = -1;
        Array.Fill(r.ContactTick, -1); Array.Fill(r.CavFlags, 0); Array.Fill(r.Stuck, 0);
    }

    // ------------------------------------------------------------------ slots: who stands where

    /// <summary>
    /// Slot order for a formation's members: the shared role order (4.3), except a cavalry formation, whose front rank takes the best armoured first (barding, then the rider's own armour), then Riding, then
    /// weapon skill, then id (cavalry 3.1). A total order, so it does not depend on the order given.
    /// </summary>
    private static void OrderMembers(FightWorld w, FormationKind kind, int[] ids, Formation shape)
    {
        if (kind != FormationKind.Cavalry || w.Mounts == null) { Slots.Order(w.Fighters, ids, shape); return; }
        var L = w.Mounts;
        Array.Sort(ids, (a, b) =>
        {
            var fa = w.Fighters[a]; var fb = w.Fighters[b];
            var pa = L.PairOf(a); var pb = L.PairOf(b);
            int c = (pb?.BardingKg ?? 0).CompareTo(pa?.BardingKg ?? 0);
            if (c != 0) return c;
            c = fb.ArmourG.CompareTo(fa.ArmourG);
            if (c != 0) return c;
            c = (pb?.Riding ?? 0).CompareTo(pa?.Riding ?? 0);
            if (c != 0) return c;
            c = Slots.SkillOf(fb).CompareTo(Slots.SkillOf(fa));
            return c != 0 ? c : a.CompareTo(b);
        });
    }

    /// <summary>A wedge's point is the captain's (3.1): he swaps places with whoever holds slot 0. True when he moved.</summary>
    private static bool PlaceCaptainAtPoint(FightWorld w, FormationRow r)
    {
        if (r.Shape != Formation.Wedge || r.Captain < 0) return false;
        int mi = r.IndexOfFighter(r.Captain);
        if (mi < 0) return false;
        int s = r.Slot[mi];
        if (s <= 0) return false;
        int m0 = r.Occ[0];
        r.Occ[0] = mi; r.Slot[mi] = 0;
        if (m0 >= 0) { r.Occ[s] = m0; r.Slot[m0] = s; } else r.Occ[s] = -1;
        return true;
    }

    /// <summary>The slot a hole in a wedge is filled from: the nearest man (by lateral place) in the first rank behind that has one.</summary>
    private static int WedgeBehind(FormationRow r, int s)
    {
        int n = r.LayoutN;
        for (int rk = Slots.WedgeRank(s) + 1; rk * rk < n; rk++)
        {
            int first = rk * rk, last = Math.Min(first + 2 * rk, n - 1), best = -1, bd = int.MaxValue;
            for (int t = first; t <= last; t++)
            {
                if (r.Occ[t] < 0) continue;
                int d = Math.Abs(r.Lat[t] - r.Lat[s]);
                if (d < bd) { bd = d; best = t; }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    // ------------------------------------------------------------------ the anchor's pace

    /// <summary>
    /// The anchor's pace for a cavalry formation (4.1): the 20th percentile of what the front rank's horses can do at the formation's gait (footing, wounds and load included: the same arithmetic the
    /// mounted layer moves a horse with), less a twentieth so the riders can catch up. A wedge's front is its first four riders.
    /// </summary>
    private int CavalryBrake(FightWorld w, FormationRow r)
    {
        int front = r.Shape == Formation.Wedge ? Math.Min(4, r.LayoutN) : Math.Min(r.Files, r.LayoutN);
        var buf = ScratchInts(Math.Max(1, front));
        int n = 0;
        for (int s = 0; s < front; s++)
        {
            int mi = r.Occ[s]; if (mi < 0 || (r.CavFlags[mi] & CfMelee) != 0) continue;
            var f = w.Fighters[r.Members[mi]];
            buf[n++] = w.Mounts?.Riding(f.Id) is { } m ? w.GaitMmps(m, r.Gait) : PaceCap(w, f);
        }
        if (n == 0) return 0;
        Array.Sort(buf, 0, n);
        return (int)((long)buf[Math.Min(n - 1, n * Rules.R.BrakePercentile / 100)] * Cv.AnchorPm / 1000);
    }

    private void SetGait(FightWorld w, FormationRow r, int gait)
    {
        if (r.Gait == gait) return;
        r.Gait = gait;
        r.BrakeMmps = CavalryBrake(w, r);
    }

    /// <summary>The gait and charge stage for the order in force and the distance to what it marches on (4.1): a charge walks beyond 200 m, trots to 60 m, gallops from there and couches at 20 m.</summary>
    private void CavalryGait(FightWorld w, FormationRow r, in FormationOrder o, bool moving, long dist, int tick)
    {
        int gait = GaitWalk;
        if (moving && o.Kind == OrderKind.Charge && r.CavPhase == PhApproach)
        {
            int st = dist > Cv.WalkToMm ? StWalk : dist > Cv.GallopFromMm ? StTrot : dist > Cv.CouchAtMm ? StGallop : StCouch;
            if (st > r.Stage) { r.Stage = st; r.StageTick[st] = tick; }
            gait = r.Stage switch { StWalk => GaitWalk, StTrot => GaitTrot, _ => GaitGallop };
        }
        else if (moving && o.Kind == OrderKind.Charge) gait = GaitGallop;                  // after contact the formation rides on and wheels at a gallop
        else if (moving && o.Kind is OrderKind.MoveTo or OrderKind.FlankLeft or OrderKind.FlankRight or OrderKind.Retreat or OrderKind.FollowMe) gait = GaitTrot;
        SetGait(w, r, gait);
    }

    /// <summary>The point a charge rides on to, then back to, after its contact (4.1): 30 m past where the anchor made contact, then 80 m.</summary>
    private void AfterContactPoint(FormationRow r, out int x, out int y)
    {
        int mm = r.CavPhase == PhRideOn ? Cv.RideOnMm : Cv.ReformMm;
        x = r.ContactAx + (int)((long)r.ContactFx * mm >> 10); y = r.ContactAy + (int)((long)r.ContactFy * mm >> 10);
    }

    /// <summary>The cavalry anchor's march: its pace ramps toward the braked gait, it moves only while the riders are on their slots (always once the charge is through), and a re-forming charge wheels while it goes.</summary>
    private int CavalryPace(FormationRow r, bool moving, int ux, int uy, long dist)
    {
        bool gated = r.DeployStart < 0 && (r.Cohesion >= Rules.R.CohesionMinPm || r.CavPhase >= PhRideOn);
        bool aligned = r.CavPhase == PhReform || FightMath.Dot(r.FaceX, r.FaceY, ux, uy) >= 724;
        int target = moving && gated && aligned ? Math.Min(r.BrakeMmps, 20_000) : 0;
        int pace = r.PaceNow, acc = Cv.AnchorAccelPerTick;
        if (target > pace) pace = Math.Min(target, pace + acc); else if (target < pace) pace = Math.Max(target, pace - 3 * acc);
        if (pace > 0 && moving)
        {
            int step = (int)Math.Min(pace / 20, dist);
            r.AnchorX += (int)((long)ux * step >> 10); r.AnchorY += (int)((long)uy * step >> 10);
            r.MoveDx = ux; r.MoveDy = uy;
        }
        else if (pace == 0) { r.MoveDx = r.FaceX; r.MoveDy = r.FaceY; }
        return pace;
    }

    /// <summary>A charge is over (4.1): with "rally" when the formation is whole again beyond the contact; with "press" at contact; after the longest approach in any case.</summary>
    private bool CavalryChargeOver(FormationRow r, in FormationOrder o, int tick)
    {
        if (tick - r.OrderGo >= Cv.ChargeMaxTicks) return true;
        return o.Arg != 0 ? r.InContact : r.CavPhase == PhDone;
    }

    /// <summary>The turn rate (degrees a second) of the formation at its gait (cavalry 3.1: gallop / trot / walk).</summary>
    private int TurnRate(FormationRow r, FormationShapeDef sh)
    {
        if (r.Kind != FormationKind.Cavalry) return sh.TurnDegPerS;
        int v = r.Gait == GaitGallop ? sh.TurnDegPerS : r.Gait == GaitTrot ? sh.TurnTrotDegPerS : sh.TurnWalkDegPerS;
        return v > 0 ? v : sh.TurnDegPerS;
    }

    // ------------------------------------------------------------------ the charge: contact, shock, ride on, re-form

    private int[] _mark = [];

    /// <summary>Shock struck at foot soldiers (tick, row, fighter, morale x100 taken): what the battle entry and the overlay read; a record of the fight, not saved (the morale it caused is in the fighters).</summary>
    public List<(int Tick, int Row, int Fighter, int X100)> Shocks { get; } = [];

    /// <summary>One tick of a cavalry row's charge bookkeeping, run before the anchor moves (so the anchor reads this tick's phase).</summary>
    private void CavalryStep(FightWorld w, FormationRow r, int tick)
    {
        var o = r.Order;
        if (r.ChargeGo != r.OrderGo)
        {
            r.ChargeGo = r.OrderGo;
            if (o.Kind == OrderKind.Charge) ResetCharge(r);                              // a new charge starts clean; any other order leaves the last charge's record to be read
        }
        if ((tick + r.Index) % 20 == 0 && (o.Kind != OrderKind.Charge || r.CavPhase == PhDone)) RejoinMelee(w, r);
        if (o.Kind != OrderKind.Charge || r.CavPhase == PhDone || w.Mounts == null) return;
        var cv = Cv;
        // contact: a rider within 3 m of a foe, from the gallop stage on (and for the late riders after the first contact)
        if (r.Stage >= StGallop || r.CavPhase >= PhRideOn) ScanContacts(w, r, tick);
        // a rider slow for 4 s once the charge is at the gallop is in a melee
        if (r.Stage >= StGallop)
        {
            for (int mi = 0; mi < r.Members.Length; mi++)
            {
                if (r.Slot[mi] < 0 || (r.CavFlags[mi] & CfMelee) != 0) continue;
                var pair = w.Mounts.Riding(r.Members[mi]);
                if (pair == null) continue;
                if (pair.Speed < cv.BreakthroughMmps) { if (++r.Stuck[mi] >= cv.StuckS * 20) r.CavFlags[mi] |= CfMelee; } else r.Stuck[mi] = 0;
            }
        }
        if (r.FirstContactTick < 0) return;
        if (r.ShockTick == -1) JudgeShock(w, r, tick);
        if (o.Arg != 0) return;                                                           // press: the riders fight where they are
        if (r.CavPhase == PhApproach) { r.CavPhase = PhRideOn; r.PhaseTick = tick; }
        int through = 0;
        for (int mi = 0; mi < r.Members.Length; mi++) if (r.Slot[mi] >= 0 && (r.CavFlags[mi] & CfThrough) != 0 && (r.CavFlags[mi] & CfMelee) == 0 && w.Fighters[r.Members[mi]].Active) through++;
        if (through == 0 && tick - r.FirstContactTick >= 2) { r.CavPhase = PhDone; r.PhaseTick = tick; return; }       // nobody rode through: the melee is the charge
        if (r.CavPhase == PhRideOn)
        {
            AfterContactPoint(r, out int tx, out int ty);
            if (FightMath.Len(tx - r.AnchorX, ty - r.AnchorY) <= Rules.R.ArriveMm || tick - r.PhaseTick >= cv.RideOnMaxTicks)
            {
                r.CavPhase = PhReform; r.PhaseTick = tick;
                AfterContactPoint(r, out int rx, out int ry); r.ReformX = rx; r.ReformY = ry;
                r.WantFx = 0; r.WantFy = 0; r.WheelSign = 0;
            }
        }
        else if (r.CavPhase == PhReform)
        {
            long d = FightMath.Len(r.ReformX - r.AnchorX, r.ReformY - r.AnchorY);
            bool turned = FightMath.Dot(r.FaceX, r.FaceY, -r.ContactFx, -r.ContactFy) >= 1000;
            if (d <= Rules.R.ArriveMm && turned && r.Cohesion >= Rules.R.CohesionMinPm || tick - r.PhaseTick >= cv.ReformS * 20 * 4)
            { r.CavPhase = PhDone; r.PhaseTick = tick; r.ReformedTick = tick; }
        }
    }

    /// <summary>A rider who fought on alone after a charge rejoins his slot once no foe is within 10 m of him (checked once a second while the charge is over).</summary>
    private void RejoinMelee(FightWorld w, FormationRow r)
    {
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            if ((r.CavFlags[mi] & CfMelee) == 0) continue;
            var f = w.Fighters[r.Members[mi]];
            if (!f.Active || !w.AnyEnemyWithin(f, 10_000)) r.CavFlags[mi] &= ~CfMelee;
        }
    }

    private void ScanContacts(FightWorld w, FormationRow r, int tick)
    {
        var cv = Cv;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            if (r.Slot[mi] < 0 || r.ContactTick[mi] >= 0) continue;
            var f = w.Fighters[r.Members[mi]];
            var pair = w.Mounts!.Riding(f.Id);
            if (pair == null || !f.Active) continue;
            bool hit = false;
            foreach (int id in w.NearUnsorted(3, f, cv.ContactMm))
            {
                var e = w.Fighters[id];
                if (e.Team == f.Team || !e.Active || w.IsHorse(e) || w.DistMm(f, e) > cv.ContactMm) continue;
                hit = true; break;
            }
            if (!hit) continue;
            r.ContactTick[mi] = tick;
            r.CavFlags[mi] = r.Order.Arg == 0 && pair.Speed > cv.BreakthroughMmps ? CfThrough : CfMelee;
            if (r.FirstContactTick < 0)
            {
                r.FirstContactTick = tick; r.CohAtContact = r.Cohesion;
                r.ContactAx = r.AnchorX; r.ContactAy = r.AnchorY; r.ContactFx = r.FaceX; r.ContactFy = r.FaceY;
                if (r.Order.Arg != 0) for (int k = 0; k < r.Members.Length; k++) if (r.Slot[k] >= 0) r.CavFlags[k] |= CfMelee;       // press: the whole formation fights on where it hit
            }
        }
    }

    /// <summary>
    /// Shock (4.1): when at least 10 riders of a formation that came in with cohesion 700 or more make contact within 2 s, every enemy foot soldier within 5 m of the contact points takes -15 morale once
    /// (-5 when braced in a shield wall or schiltron with cohesion 600 or more, or braced against horses). A ragged charge gives none. The blows are the riders' own; this is morale only.
    /// </summary>
    private void JudgeShock(FightWorld w, FormationRow r, int tick)
    {
        var cv = Cv;
        int n = 0;
        for (int mi = 0; mi < r.Members.Length; mi++) if (r.ContactTick[mi] >= 0 && tick - r.ContactTick[mi] <= cv.ShockWindowTicks) n++;
        if (n < cv.ShockMinRiders) return;
        if (r.CohAtContact < cv.CohesionShockPm) { r.ShockTick = -2; return; }
        r.ShockTick = tick;
        if (_mark.Length < w.Fighters.Count) _mark = new int[Math.Max(w.Fighters.Count, _mark.Length * 2)];
        int stamp = tick + 1, hit = 0;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            if (r.ContactTick[mi] < 0 || tick - r.ContactTick[mi] > cv.ShockWindowTicks) continue;
            var rider = w.Fighters[r.Members[mi]];
            foreach (int id in w.Near(3, rider, cv.ShockRadiusMm))
            {
                var e = w.Fighters[id];
                if (e.Team == rider.Team || !e.Active || e.Routing || w.IsHorse(e) || w.Mounts!.Riding(e.Id) != null || _mark[id] == stamp || w.DistMm(rider, e) > cv.ShockRadiusMm) continue;
                _mark[id] = stamp;
                if (!e.M.Has(Combat.Morale.MoraleFlags.Inited)) continue;
                bool braced = e.Act == FAct.Bracing || (e.FmRow >= 0 && e.FmRow < Rows.Count && Rows[e.FmRow].Shape is Formation.ShieldWall or Formation.Schiltron && Rows[e.FmRow].Cohesion >= cv.ShockBracedCohesionPm);
                int x100 = braced ? cv.ShockBracedX100 : cv.ShockX100;
                e.M.PenaltyX100 += x100; e.M.LastLossTick = tick;
                Shocks.Add((tick, r.Index, id, x100));
                hit++;
            }
        }
        r.ShockHit = hit;
    }

    // ------------------------------------------------------------------ the rider

    /// <summary>
    /// A cavalry formation's rider for one tick (called from the cavalry brain): he strikes the nearest foot foe in reach with the Phase 4 blow, and steers for his slot at the anchor's pace plus what closes his
    /// gap (a catch-up that eases to nothing as he arrives), at the gait that speed needs, with the speed capped so he does not run past it. False: he is not in a cavalry formation, has no slot, or is in a melee,
    /// and the Phase 4 brain rides him.
    /// </summary>
    public bool CavalryRide(FightWorld w, MountedFighter m, Fighter r)
    {
        if (r.FmRow < 0 || r.FmRow >= Rows.Count) return false;
        var row = Rows[r.FmRow];
        if (row.Kind != FormationKind.Cavalry) return false;
        int mi = r.FmMember;
        row.CavCapTick[mi] = -1;
        int s = row.Slot[mi];
        if (s < 0 || (row.CavFlags[mi] & CfMelee) != 0) { m.Walk = false; return false; }
        int tick = w.Tick + 1;
        var c = r.Cmd; var cv = Cv;
        int faceX = row.FaceX, faceY = row.FaceY;
        // the blow: the nearest foot foe in reach (every tick in a charge, twice a second otherwise)
        bool engaged = row.Stage >= StGallop || row.CavPhase > PhApproach || row.InContact;
        if (engaged || (tick + r.Id) % 10 == 0)
        {
            Fighter? t = null; long bd = long.MaxValue;
            foreach (int id in w.NearUnsorted(3, r, 12_000))
            {
                var e = w.Fighters[id];
                if (e.Team == r.Team || !e.Active || w.IsHorse(e)) continue;
                long d = w.DistMm(r, e);
                if (d <= 12_000 && (d < bd || d == bd && e.Id < t!.Id)) { bd = d; t = e; }          // the nearest, the lowest id on a tie (the scan is unsorted)
            }
            if (t != null)
            {
                MountedBrain.StrikeAt(w, m, r, t, tick, bd);
                if (bd <= 8_000) { var (ux, uy) = FightMath.Norm(t.X - r.X, t.Y - r.Y); faceX = ux; faceY = uy; }
            }
        }
        SlotWorld(row, s, tick, out int sx, out int sy);
        long dx = sx - r.X, dy = sy - r.Y, dist = FightMath.Len(dx, dy);
        long vx = (long)row.MoveDx * row.PaceNow >> 10, vy = (long)row.MoveDy * row.PaceNow >> 10;
        if (dist > 0)
        {
            long catchV = Math.Min(Math.Min(FightMath.ISqrt(2L * cv.CatchAccelMmps2 * dist), 2 * dist), cv.CatchMaxMmps);
            vx += dx * catchV / dist; vy += dy * catchV / dist;
        }
        long vlen = FightMath.Len(vx, vy);
        c.FaceX = faceX; c.FaceY = faceY;
        if (vlen < 150) { c.MoveX = c.MoveY = 0; c.Sprint = false; m.Walk = true; }
        else
        {
            c.MoveX = (int)vx; c.MoveY = (int)vy;
            c.Sprint = vlen > m.Gaits.TrotMmps;
            m.Walk = vlen <= m.Gaits.WalkMmps;
            row.CavCap[mi] = (int)vlen; row.CavCapTick[mi] = tick;
        }
        return true;
    }

    /// <summary>The speed cap a cavalry rider keeps at <paramref name="tick"/> (0: none): the mounted layer's movement takes the lesser of this and the gait's speed.</summary>
    public int CavalryCap(Fighter r, int tick)
    {
        if (r.FmRow < 0 || r.FmRow >= Rows.Count) return 0;
        var row = Rows[r.FmRow];
        return row.Kind == FormationKind.Cavalry && r.FmMember < row.CavCapTick.Length && row.CavCapTick[r.FmMember] == tick ? row.CavCap[r.FmMember] : 0;
    }

    // ------------------------------------------------------------------ cavalry captains (4.2)

    /// <summary>
    /// What a cavalry captain charges (4.2): archers and routers always; a formation whose refusal he estimates under 400 permille (the Phase 4 avoid threshold) otherwise, avoiding braced polearms and
    /// shield walls from Tactics 30 and preferring a flank or the rear from Tactics 50; when every way in is braced he rides to a flank instead. Nothing while a charge is in hand or he is in contact.
    /// </summary>
    private void PlanCavalry(FightWorld w, FormationRow r, int T)
    {
        if (!Rules.CaptainMay("cavalry_charge", T) || r.InContact || r.Active == 0 || w.Mounts == null) return;
        if (r.Order.Kind is OrderKind.Charge or OrderKind.FlankLeft or OrderKind.FlankRight && r.CavPhase != PhDone) return;
        var cap = w.Mounts.Riding(r.Captain);
        int trained = cap?.TrainedPm ?? 700, riding = cap?.Riding ?? 40;
        bool avoid = Rules.CaptainMay("cavalry_avoid_braced", T), prefer = Rules.CaptainMay("cavalry_flank_or_rear", T);
        int best = -1, bestScore = int.MinValue, nearest = -1; long nd = long.MaxValue;
        for (int i = 0; i < Rows.Count; i++)
        {
            var e = Rows[i];
            if (e.Side == r.Side || e.ActiveNow == 0) continue;
            long d = FightMath.Len(e.AnchorX - r.AnchorX, e.AnchorY - r.AnchorY);
            if (d > 400_000) continue;
            if (d < nd) { nd = d; nearest = i; }
            bool always = IsMissileRow(w, e) || e.TotalNow > 0 && e.RoutingNow * 2 >= e.TotalNow;
            int score = (always ? 90 : 70) - (int)(d / 10_000);
            if (!always && avoid && EstimatedRefusalPm(w, r, e, trained, riding) >= Cv.AvoidRefusalPm) continue;
            if (prefer && !always)
            {
                var (ux, uy) = FightMath.Norm(r.AnchorX - e.AnchorX, r.AnchorY - e.AnchorY);
                if (FightMath.Dot(e.FaceX, e.FaceY, ux, uy) < 512) score += 10;            // its flank or its rear
            }
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0)
        {
            var c = FormationOrder.Of(OrderKind.Charge); c.TargetFormation = best + 1;
            Cand(Ch.Move, bestScore, c);
        }
        else if (nearest >= 0 && avoid && r.Order.Kind is not (OrderKind.FlankLeft or OrderKind.FlankRight))
        {
            var e = Rows[nearest];
            long lat = (long)(e.AnchorX - r.AnchorX) * -r.FaceY + (long)(e.AnchorY - r.AnchorY) * r.FaceX;
            var f = FormationOrder.Of(lat >= 0 ? OrderKind.FlankLeft : OrderKind.FlankRight); f.TargetFormation = nearest + 1;
            Cand(Ch.Move, 62, f);
        }
    }

    /// <summary>
    /// The refusal a charge at a formation would meet, permille (Phase 4 <c>RefusalPm</c> with the grid's counts): the braced or brace-ready polearms within 6 m of the man nearest the charge, and 300
    /// more for a shield wall or schiltron at cohesion 600 or more (a horse does not go onto a wall).
    /// </summary>
    private int EstimatedRefusalPm(FightWorld w, FormationRow r, FormationRow e, int trainedPm, int riding)
    {
        var R = w.Mounts!.D.Refusal;
        Fighter? near = null; long nd = long.MaxValue;
        for (int i = 0; i < e.Members.Length; i++)
        {
            if (e.Slot[i] < 0) continue;
            var f = w.Fighters[e.Members[i]];
            long d = FightMath.Len(f.X - r.AnchorX, f.Y - r.AnchorY);
            if (d < nd) { nd = d; near = f; }
        }
        if (near == null) return 0;
        int n = 0;
        foreach (int id in w.Near(2, near, R.ArcMm))
        {
            var b = w.Fighters[id];
            if (b.Team != e.Side || !b.Active || !FightWorld.CanBrace(b) || w.DistMm(b, near) > R.ArcMm) continue;
            if (b.Act == FAct.Bracing || b.Act is FAct.Idle or FAct.Block) n++;
        }
        int p = ChargeMath.RefusalPm(n, trainedPm, riding, R.BasePm, R.PerBracedPolearmPm, R.TrainedK, R.TrainedRef, R.RidingK, R.MaxPm, 1000);
        if (e.Shape is Formation.ShieldWall or Formation.Schiltron && e.Cohesion >= Rules.R.CohesionMinPm) p += 300;
        return p;
    }

    // ------------------------------------------------------------------ the hash and the save

    private static ulong HashCavalry(ulong h, FormationRow r)
    {
        h = FightMath.Mix(h, (long)r.Gait | (long)r.Stage << 4 | (long)r.CavPhase << 8 | (long)r.ChargeGo << 16);
        h = FightMath.Mix(h, r.FirstContactTick); h = FightMath.Mix(h, r.ShockTick); h = FightMath.Mix(h, r.ShockHit);
        for (int i = 0; i < r.CavFlags.Length; i++) h = FightMath.Mix(h, (long)r.CavFlags[i] | (long)r.ContactTick[i] << 8);
        return h;
    }

    private static void PackCavalry(List<long> o, FormationRow r)
    {
        o.Add(r.Gait); o.Add(r.Stage); for (int i = 0; i < 5; i++) o.Add(r.StageTick[i]);
        o.Add(r.CavPhase); o.Add(r.PhaseTick); o.Add(r.ContactAx); o.Add(r.ContactAy); o.Add(r.ContactFx); o.Add(r.ContactFy);
        o.Add(r.FirstContactTick); o.Add(r.CohAtContact); o.Add(r.ShockTick); o.Add(r.ShockHit); o.Add(r.ReformedTick); o.Add(r.ReformX); o.Add(r.ReformY); o.Add(r.ChargeGo); o.Add(r.MoveDx); o.Add(r.MoveDy);
        o.Add(r.Members.Length);
        for (int i = 0; i < r.Members.Length; i++) { o.Add(r.ContactTick[i]); o.Add(r.CavFlags[i]); o.Add(r.Stuck[i]); }
    }

    private static void UnpackCavalry(Reader rd, FormationRow r)
    {
        r.Gait = rd.I(); r.Stage = rd.I(); for (int i = 0; i < 5; i++) r.StageTick[i] = rd.I();
        r.CavPhase = rd.I(); r.PhaseTick = rd.I(); r.ContactAx = rd.I(); r.ContactAy = rd.I(); r.ContactFx = rd.I(); r.ContactFy = rd.I();
        r.FirstContactTick = rd.I(); r.CohAtContact = rd.I(); r.ShockTick = rd.I(); r.ShockHit = rd.I(); r.ReformedTick = rd.I(); r.ReformX = rd.I(); r.ReformY = rd.I(); r.ChargeGo = rd.I(); r.MoveDx = rd.I(); r.MoveDy = rd.I();
        int n = rd.I();
        for (int i = 0; i < n; i++) { r.ContactTick[i] = rd.I(); r.CavFlags[i] = rd.I(); r.Stuck[i] = rd.I(); }
    }
}
