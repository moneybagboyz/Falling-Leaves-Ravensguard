using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

// P7-CV-B: a cavalry formation gets down and up again, and horse archers keep their distance (cavalry-and-mounted-war 4.5, 4.7). The Dismount order starts every rider of the formation getting down (2 s:
// the mounted layer's pair state); when the last has done it the formation is Infantry with its own slots (a line), one man in four of the lowest-ranked (never the captain) is a horse-holder who leads
// four horses (his own and three others') 60 m to the rear and holds them, and the rest fight on foot. The Mount order brings the holders forward, every man walks to his horse and mounts (4 s, 2 s at
// Riding 60), and when they are up the formation is Cavalry again. A holder with an enemy within 10 m lets go and the horses bolt. Captains (Tactics 50) dismount men-at-arms who must hold a line
// against cavalry, fight in woods, or have 300 permille of their horses wounded, and mount again when the enemy is far; horse archers (Tactics 30) keep 60 to 120 m from foot and wheel away at 40 m.
public sealed partial class FormationBook
{
    /// <summary>The row's footing on its horses (<see cref="FormationRow.Foot"/>).</summary>
    public const int FootNone = 0, FootDismounting = 1, FootOn = 2, FootMounting = 3;
    private const long FootMagic = 0x464F4F54;       // "FOOT"

    private static MountedFighter? RiderPair(MountedLayer L, int fighter) => L.PairOf(fighter) is { } p && p.Rider == fighter ? p : null;

    // ------------------------------------------------------------------ the orders

    private void BeginDismountOrder(FightWorld w, FormationRow r, int tick)
    {
        if (w.Mounts == null || r.Kind != FormationKind.Cavalry || r.Foot != FootNone) return;
        int n = 0;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            if (r.Slot[mi] < 0) continue;
            if (RiderPair(w.Mounts, r.Members[mi]) is { Mounted: true } p && w.BeginDismount(p)) n++;
        }
        if (n == 0) return;
        r.Foot = FootDismounting; r.FootTick = tick;
        r.Order = FormationOrder.Of(OrderKind.Hold); r.Order.Signal = OrderSignal.Captain; r.QueueCount = 0; r.OrderGo = tick;      // the men stand to get down
    }

    private void BeginMountOrder(FightWorld w, FormationRow r, int tick)
    {
        if (w.Mounts == null || r.Foot != FootOn) return;
        var Cv = Rules.Cav;
        bool any = false;
        int holders = 0; for (int mi = 0; mi < r.Members.Length; mi++) if (RiderPair(w.Mounts, r.Members[mi]) is { Holding: true }) holders++;
        int j = 0;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            var p = RiderPair(w.Mounts, r.Members[mi]);
            if (p == null || p.Mounted || !w.Fighters[p.Rider].Active) continue;
            if (p.Holding)
            {
                PostBehind(r, 6000, j++, holders, out int px, out int py);                            // the holders bring the horses up to the rear of the line
                p.PostX = px; p.PostY = py; p.Remount = true; any = true;
            }
            else if (p.Phase == MountedFighter.PhFoot && w.BeginMount(p)) any = true;
        }
        if (!any) return;
        r.Foot = FootMounting; r.FootTick = tick;
    }

    /// <summary>A point <paramref name="backMm"/> behind the anchor, the <paramref name="k"/>th of <paramref name="n"/> spread 5 m apart across the line.</summary>
    private static void PostBehind(FormationRow r, int backMm, int k, int n, out int x, out int y)
    {
        int lat = (2 * k - (n - 1)) * 2500;
        x = r.AnchorX - (int)((long)r.FaceX * backMm >> 10) + (int)((long)-r.FaceY * lat >> 10);
        y = r.AnchorY - (int)((long)r.FaceY * backMm >> 10) + (int)((long)r.FaceX * lat >> 10);
    }

    // ------------------------------------------------------------------ the row's step

    private void FootStep(FightWorld w, FormationRow r, int tick)
    {
        var L = w.Mounts;
        if (L == null) { r.Foot = FootNone; return; }
        if (r.Foot == FootDismounting)
        {
            if (tick < r.FootTick + 2) return;
            for (int mi = 0; mi < r.Members.Length; mi++)
                if (RiderPair(L, r.Members[mi]) is { Phase: MountedFighter.PhDismounting }) return;       // somebody is still getting down
            FinishDismountRow(w, L, r, tick);
        }
        else if (r.Foot == FootMounting)
        {
            if ((tick + r.Index) % 10 != 0) return;
            bool busy = false;
            for (int mi = 0; mi < r.Members.Length && !busy; mi++)
            {
                var p = RiderPair(L, r.Members[mi]);
                if (p == null || p.Mounted || !w.Fighters[p.Rider].Active) continue;
                if (p.Phase is MountedFighter.PhFetch or MountedFighter.PhMounting || p.Holding) busy = true;
            }
            if (busy && tick - r.FootTick < Rules.Cav.MountMaxTicks) return;
            FinishMountRow(w, L, r, tick);
        }
    }

    /// <summary>Everyone is down: the lowest-ranked quarter hold the horses 60 m back, the rest are a line of foot.</summary>
    private void FinishDismountRow(FightWorld w, MountedLayer L, FormationRow r, int tick)
    {
        var Cv = Rules.Cav;
        var feet = new List<int>();
        for (int s = 0; s < r.LayoutN; s++)
        {
            int mi = r.Occ[s]; if (mi < 0) continue;
            if (RiderPair(L, r.Members[mi]) is { Mounted: false, Phase: MountedFighter.PhFoot } && w.Fighters[r.Members[mi]].Active) feet.Add(mi);
        }
        if (feet.Count == 0) { r.Foot = FootNone; return; }
        int perHolder = Math.Max(1, Cv.HoldersPerHorses);
        int nHold = (feet.Count + perHolder - 1) / perHolder;
        var holders = new List<int>();
        for (int k = feet.Count - 1; k >= 0 && holders.Count < nHold; k--) if (r.Members[feet[k]] != r.Captain) holders.Add(feet[k]);
        holders.Reverse();                                                                                 // front to back
        var others = new List<int>();
        foreach (int mi in feet) if (!holders.Contains(mi)) others.Add(mi);
        for (int j = 0; j < holders.Count; j++)
        {
            var hp = RiderPair(L, r.Members[holders[j]])!;
            PostBehind(r, Cv.HolderBackMm, j, holders.Count, out int px, out int py);
            w.MakeHolder(hp, px, py);
            w.LeadHorse(hp, hp, 0);
            for (int s = 0; s < perHolder - 1; s++)
            {
                int idx = j * (perHolder - 1) + s;
                if (idx < others.Count) w.LeadHorse(RiderPair(L, r.Members[others[idx]])!, hp, s + 1);
            }
            int mi = holders[j];
            int slot = r.Slot[mi]; if (slot >= 0) r.Occ[slot] = -1;
            r.Slot[mi] = -1;
        }
        r.Kind = FormationKind.Infantry;
        r.Shape = Formation.Line; r.DeployStart = -1;
        var def = Rules.Shape(Formation.Line, FormationKind.Infantry);
        r.Ranks = Math.Max(1, def.Ranks);
        Reslot(w, r);
        r.Foot = FootOn; r.FootTick = tick;
        r.BrakeMmps = ComputeBrake(w, r);
        w.Grid.Dirty = true;
    }

    /// <summary>The men are up (or the time is out): whoever sits his horse takes a place in a cavalry line again; the rest stay on foot, out of the formation.</summary>
    private void FinishMountRow(FightWorld w, MountedLayer L, FormationRow r, int tick)
    {
        var ids = new List<int>();
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            r.Slot[mi] = -1;
            var p = RiderPair(L, r.Members[mi]);
            if (p != null && p.Mounted && w.Fighters[p.Rider].Active) ids.Add(r.Members[mi]);
        }
        r.Kind = FormationKind.Cavalry;
        r.Shape = Formation.Line; r.DeployStart = -1;
        var arr = ids.ToArray();
        OrderMembers(w, r.Kind, arr, r.Shape);
        Array.Fill(r.Occ, -1); Array.Fill(r.VacSince, -1);
        for (int s = 0; s < arr.Length; s++) { int mi = r.IndexOfFighter(arr[s]); r.Slot[mi] = s; r.Occ[s] = mi; }
        r.LayoutN = arr.Length; r.VacCount = 0;
        r.Ranks = Math.Max(1, Rules.Shape(Formation.Line, FormationKind.Cavalry).Ranks);
        Relayout(r);
        ResetCharge(r);
        r.Foot = FootNone; r.FootTick = 0;
        r.BrakeMmps = ComputeBrake(w, r);
        w.Grid.Dirty = true;
    }

    // ------------------------------------------------------------------ the captain (4.5)

    /// <summary>
    /// Tactics 50 (<c>cavalry_dismount</c>): a mounted formation of men-at-arms (not horse archers) with the enemy within 80 m gets down when 300 permille of its horses are wounded, when it stands in woods,
    /// or when it holds a line with enemy cavalry near; a dismounted formation mounts again when no enemy is within 100 m and none of those still holds.
    /// </summary>
    private void PlanDismount(FightWorld w, FormationRow r, int T, in Sense s)
    {
        if (!Rules.CaptainMay("cavalry_dismount", T) || w.Mounts == null || r.Active == 0) return;
        var Cv = Rules.Cav; var L = w.Mounts;
        bool woods = L.Terrain.At(r.AnchorX, r.AnchorY) == Footing.Woods;
        int horses = 0, hurt = 0; long armour = 0;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            var p = RiderPair(L, r.Members[mi]);
            if (p == null || !w.Fighters[p.Rider].Active || !w.Fighters[p.Horse].Active) continue;
            horses++; armour += w.Fighters[p.Rider].ArmourG;
            if (w.Fighters[p.Horse].Book.Records.Count > 0) hurt++;
        }
        bool hurtHorses = horses > 0 && hurt * 1000 >= Cv.DismountWoundedPm * horses;
        if (r.Kind == FormationKind.Cavalry && r.Foot == FootNone)
        {
            if (horses == 0 || IsHorseArcherRow(w, r) || s.MinEnemyMm > Cv.DismountEnemyMm) return;
            bool holdLine = s.CavNear && r.Order.Kind == OrderKind.Hold && armour / horses >= 10_000;
            if (woods || hurtHorses || holdLine) Cand(Ch.Shape, 95, FormationOrder.Of(OrderKind.Dismount));
        }
        else if (r.Foot == FootOn && horses > 0 && !woods && !hurtHorses && !s.CavNear && s.MinEnemyMm >= Cv.RemountClearMm)
            Cand(Ch.Shape, 60, FormationOrder.Of(OrderKind.Mount));
    }

    // ------------------------------------------------------------------ horse archers (4.7)

    /// <summary>More than half of the formation's riders in the saddle carry a launcher.</summary>
    private bool IsHorseArcherRow(FightWorld w, FormationRow r)
    {
        if (r.Kind != FormationKind.Cavalry || w.Mounts == null) return false;
        int n = 0, bows = 0;
        for (int mi = 0; mi < r.Members.Length; mi++)
        {
            if (r.Slot[mi] < 0) continue;
            var f = w.Fighters[r.Members[mi]];
            if (w.Mounts.Riding(f.Id) == null) continue;
            n++; if (f.Launcher != null) bows++;
        }
        return n > 0 && bows * 2 > n;
    }

    /// <summary>
    /// Horse archers (Tactics 30, <c>horse_archer_skirmish</c>) keep 60 to 120 m from the nearest foe on foot: seen each half second, a foe inside 40 m or beyond 120 m makes the formation ride to the point 90 m
    /// from him on its own side (wheeling away at the trot, the riders shooting as they go, never at a gallop) and face him there; between them the formation stands and shoots. A formation under a
    /// march or charge order that has the foe inside its band is halted.
    /// </summary>
    private void SkirmishStep(FightWorld w, FormationRow r, int tick)
    {
        if ((tick + r.Index) % 10 != 0 || r.Foot != FootNone || w.Mounts == null) return;
        if (!r.CaptainAlways && tick - r.Micro < Rules.R.CaptainTakeoverS * 20) return;
        var cap = w.Fighters[r.Captain];
        int T = Math.Max(0, cap.Kit.Tactics);
        if (!Rules.CaptainMay("horse_archer_skirmish", T) || !IsHorseArcherRow(w, r)) return;
        var Cv = Rules.Cav;
        var e = w.NearestNonHorseEnemy(cap, 400_000);
        if (e == null) return;
        long d = FightMath.Len(e.X - r.AnchorX, e.Y - r.AnchorY);
        FormationOrder o;
        if (d < Cv.ArcherWheelMm || d > Cv.ArcherMaxMm)
        {
            var (ux, uy) = FightMath.Norm(r.AnchorX - e.X, r.AnchorY - e.Y);
            if (ux == 0 && uy == 0) (ux, uy) = (-r.FaceX, -r.FaceY);
            o = FormationOrder.Of(OrderKind.MoveTo);
            o.TargetX = e.X + (int)((long)ux * Cv.ArcherStandoffMm >> 10); o.TargetY = e.Y + (int)((long)uy * Cv.ArcherStandoffMm >> 10);
            o.FaceX = -ux; o.FaceY = -uy; o.Arg = 2;                                                                   // Arg 2: a skirmish move
            if (r.Order.Kind == OrderKind.MoveTo && r.Order.Arg == 2 && FightMath.Len(r.Order.TargetX - o.TargetX, r.Order.TargetY - o.TargetY) < 10_000) return;
        }
        else if (r.Order.Kind is OrderKind.Advance or OrderKind.Charge or OrderKind.AttackFormation or OrderKind.FlankLeft or OrderKind.FlankRight) o = FormationOrder.Of(OrderKind.Hold);
        else return;
        o.Signal = OrderSignal.Captain; o.IssuedTick = w.Tick; o.ArriveTick = w.Tick; o.By = r.Captain; o.Serial = ++_serial;
        Apply(w, r, o, tick);
    }

    // ------------------------------------------------------------------ the save

    private void PackFoot(List<long> o)
    {
        int n = 0; foreach (var r in Rows) if (r.Foot != FootNone) n++;
        o.Add(FootMagic); o.Add(n);
        foreach (var r in Rows) if (r.Foot != FootNone) { o.Add(r.Index); o.Add(r.Foot); o.Add(r.FootTick); }
    }

    private void UnpackFoot(Reader rd)
    {
        rd.L();
        int n = rd.I();
        for (int k = 0; k < n; k++) { var r = Rows[rd.I()]; r.Foot = rd.I(); r.FootTick = rd.I(); }
    }
}
