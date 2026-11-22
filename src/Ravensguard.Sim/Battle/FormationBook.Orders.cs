using Ravensguard.Content;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

// Orders and the anchor (battle-bubble-and-formations 4.3, 4.5): issue with the delay of the voice, the horn or a runner; arrive; take effect (a shape or a fire mode at once, a movement order
// replacing or joining the queue); and the anchor's turn, pivot and march, braked by its slowest ranks and stopped while the men are not on their slots.
public sealed partial class FormationBook
{
    /// <summary>
    /// Gives an order to a formation (4.5). It reaches the formation's captain after <c>max(10, 80 - Leadership x 4 / 5)</c> ticks, plus nothing within 60 m of the captain (the voice), plus 20 ticks beyond that
    /// for an order a horn can carry when the side has a banner bearer on his feet, else the time a runner takes at 5 m/s (a real fighter of the issuer's formation, or the nearest friend; if he falls first the
    /// order is lost and <see cref="Lost"/> says so). Returns the order as sent (its <see cref="FormationOrder.ArriveTick"/> is the tick it arrives), or null when the formation has eight orders on their way.
    /// <paramref name="queue"/> starts it after the current order instead of replacing it. Time counts in fight ticks, so a paused fight gives no free time (BB-11): an order placed while the game is paused
    /// arrives the same number of ticks after the play resumes.
    /// </summary>
    public FormationOrder? Issue(FightWorld w, FormationRow r, FormationOrder o, int issuer = -1, int leadership = 0, bool queue = false)
    {
        if (r.PendingCount >= r.Pending.Length) return null;
        int delay = DelayOf(w, r, ref o, issuer, leadership, out int runner);
        o.Runner = runner; o.By = issuer; o.Queued = queue; o.Serial = ++_serial;
        o.IssuedTick = w.Tick; o.ArriveTick = w.Tick + delay;
        if (o.TargetFighter == 0 && o.Kind != OrderKind.FollowMe) o.TargetFighter = -1;
        int k = r.PendingCount++;
        while (k > 0 && (r.Pending[k - 1].ArriveTick > o.ArriveTick)) { r.Pending[k] = r.Pending[k - 1]; k--; }
        r.Pending[k] = o;
        r.Micro = w.Tick;
        return o;
    }

    /// <summary>
    /// The delay of an order in ticks and how it travels (4.5), and sets <paramref name="o"/>'s signal: what <see cref="Issue"/> uses, and the order wheel's tooltip ("arrives in 2.0 s, by horn") before the order is given.
    /// Pure: it picks a runner (<paramref name="runner"/>) but changes nothing in the fight.
    /// </summary>
    public int DelayOf(FightWorld w, FormationRow r, ref FormationOrder o, int issuer, int leadership, out int runner)
    {
        var R = Rules.R;
        int delay = Math.Max(R.DelayMinTicks, R.DelayBaseTicks - leadership * R.DelayLeadershipNum / Math.Max(1, R.DelayLeadershipDen));
        var cap = r.Captain >= 0 ? w.Fighters[r.Captain] : null;
        int cx = cap?.X ?? r.AnchorX, cy = cap?.Y ?? r.AnchorY;
        long dist = 0; runner = -1;
        Fighter? from = issuer >= 0 && issuer < w.Fighters.Count ? w.Fighters[issuer] : null;
        if (from != null) dist = FightMath.Len(cx - from.X, cy - from.Y);
        o.Signal = OrderSignal.Voice;
        if (dist > R.VoiceRangeMm)
        {
            if (Rules.Order(o.Kind).Horn && SideHasHorn(w, r.Side)) { o.Signal = OrderSignal.Horn; delay += R.HornExtraTicks; }
            else
            {
                o.Signal = OrderSignal.Runner; delay += (int)(dist * 20 / Math.Max(1, R.RunnerMmps));
                runner = PickRunner(w, from!);
            }
        }
        return delay;
    }

    /// <summary>The ticks from now until the order reaches the formation's captain (the overlay's countdown).</summary>
    public static int ArrivesIn(FightWorld w, in FormationOrder o) => Math.Max(0, o.ArriveTick - w.Tick);

    private bool SideHasHorn(FightWorld w, int side)
    {
        foreach (var r in Rows)
            if (r.Side == side && r.Banner >= 0 && r.Banner < w.Fighters.Count && w.Fighters[r.Banner].Active && !w.Fighters[r.Banner].Routing) return true;
        return false;
    }

    private int PickRunner(FightWorld w, Fighter from)
    {
        if (from.FmRow >= 0 && from.FmRow < Rows.Count)
        {
            var row = Rows[from.FmRow];
            for (int i = 0; i < row.Members.Length; i++)
                if (row.Slot[i] >= 0 && row.Members[i] != from.Id && w.Fighters[row.Members[i]].Active) return row.Members[i];
        }
        int best = -1; long bd = long.MaxValue;
        foreach (var f in w.Fighters)
        {
            if (f.Id == from.Id || f.Team != from.Team || !f.Active || f.Routing) continue;
            long d = w.DistMm(from, f);
            if (d < bd) { bd = d; best = f.Id; }
        }
        return best >= 0 ? best : from.Id;
    }

    /// <summary>Orders reaching their captain this tick; an order whose runner has fallen is lost.</summary>
    private void Arrivals(FightWorld w, FormationRow r, int tick)
    {
        int k = 0;
        while (k < r.PendingCount)
        {
            var p = r.Pending[k];
            bool lost = false;
            if (p.Signal == OrderSignal.Runner && p.Runner >= 0)
            {
                var rf = w.Fighters[p.Runner];
                lost = !rf.Active || rf.Routing;
            }
            if (!lost && tick < p.ArriveTick) { k++; continue; }
            for (int j = k + 1; j < r.PendingCount; j++) r.Pending[j - 1] = r.Pending[j];
            r.PendingCount--;
            if (lost) Lost.Add((w.Tick, r.Index, p.Serial, p.Runner));
            else Apply(w, r, p, tick);
        }
    }

    /// <summary>An order takes effect now (also how a captain's own decision is carried out: no delay).</summary>
    public void Apply(FightWorld w, FormationRow r, FormationOrder o, int tick)
    {
        if (o.Frontage > 0) Reface(r, o.Frontage);
        switch (o.Kind)
        {
            case OrderKind.ShieldWall: SetShape(w, r, Formation.ShieldWall, tick); return;
            case OrderKind.Loose: SetShape(w, r, Formation.Loose, tick); return;
            case OrderKind.Line: SetShape(w, r, Formation.Line, tick); return;
            case OrderKind.Wedge: SetShape(w, r, Formation.Wedge, tick); return;
            case OrderKind.Column: SetShape(w, r, Formation.Column, tick); return;
            case OrderKind.SetAgainstCavalry:
                {
                    int pole = 0, n = 0;
                    for (int i = 0; i < r.Members.Length; i++) { if (r.Slot[i] < 0) continue; n++; if (w.Fighters[r.Members[i]].Family == "polearms") pole++; }
                    SetShape(w, r, n > 0 && pole * 2 >= n ? Formation.Schiltron : Formation.ShieldWall, tick);
                    return;
                }
            case OrderKind.FireAtWill: r.Fire = FireMode.AtWill; r.FireTargetRow = -1; return;
            case OrderKind.HoldFire: r.Fire = FireMode.Hold; r.FireTargetRow = -1; return;
            case OrderKind.Face:
                {
                    var (wx, wy) = o.FaceX != 0 || o.FaceY != 0 ? FightMath.Norm(o.FaceX, o.FaceY) : FightMath.Norm(o.TargetX - r.AnchorX, o.TargetY - r.AnchorY);
                    r.WantFx = wx; r.WantFy = wy; r.WheelSign = 0; return;
                }
            case OrderKind.Wheel:
                {
                    var (wx, wy) = FightMath.Norm(o.FaceX, o.FaceY);
                    r.WantFx = wx; r.WantFy = wy; r.WheelSign = o.Arg > 0 ? 1 : -1; return;
                }
            case OrderKind.Dismount: case OrderKind.Mount: return;      // the cavalry spec acts on these
            case OrderKind.None: return;
        }
        if (o.Queued && r.Order.Kind is not (OrderKind.None or OrderKind.Hold))
        {
            if (r.QueueCount < Math.Min(Rules.R.MaxQueue, r.Queue.Length)) r.Queue[r.QueueCount++] = o;
            return;
        }
        Install(r, o, tick);
        r.BrakeMmps = ComputeBrake(w, r);
    }

    private void Install(FormationRow r, FormationOrder o, int tick)
    {
        r.Order = o; r.QueueCount = 0; r.OrderGo = tick; r.TgtRow = -1;
        if (o.Kind == OrderKind.Retreat && o.Arg != 1) r.Order.TargetX = int.MinValue;      // resolved against the facing or the entry point when the march starts
    }

    /// <summary>The order in force is done: the next queued order starts, else the formation holds.</summary>
    private void Complete(FormationRow r, int tick)
    {
        if (r.Order.FaceX != 0 || r.Order.FaceY != 0) { r.WantFx = r.Order.FaceX; r.WantFy = r.Order.FaceY; r.WheelSign = 0; }
        if (r.QueueCount > 0)
        {
            r.Order = r.Queue[0];
            for (int i = 1; i < r.QueueCount; i++) r.Queue[i - 1] = r.Queue[i];
            r.QueueCount--;
        }
        else { r.Order = FormationOrder.Of(OrderKind.Hold); r.Order.Signal = OrderSignal.Captain; }      // a finished march holds, and the captain may march on
        r.OrderGo = tick; r.TgtRow = -1;
    }

    /// <summary>A drawn frontage (RMB-drag in the overlay): the ranks that make about that width in files.</summary>
    private void Reface(FormationRow r, int frontageMm)
    {
        var def = Rules.Shape(r.Shape, r.Kind);
        if (def.Layout != "rank" || def.Abreast > 0) return;
        int files = Math.Clamp(frontageMm / Math.Max(1, def.LateralMm), 1, Math.Max(1, r.LayoutN));
        r.Ranks = Math.Clamp((r.LayoutN + files - 1) / files, 1, 8);
        Relayout(r);
    }

    // ------------------------------------------------------------------ the anchor

    private int NearestEnemyRow(FormationRow r)
    {
        int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < Rows.Count; i++)
        {
            var e = Rows[i];
            if (e.Side == r.Side || e.ActiveNow == 0) continue;
            long dx = e.AnchorX - r.AnchorX, dy = e.AnchorY - r.AnchorY, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private void Drive(FightWorld w, FormationRow r, int tick)
    {
        var sh = Rules.Shape(r.Shape, r.Kind);
        if (r.Shape == Formation.FallBack && r.Leader >= 0 && r.Leader < w.Fighters.Count && w.Fighters[r.Leader].Active)
        {
            var l = w.Fighters[r.Leader];
            r.AnchorX = l.X; r.AnchorY = l.Y; r.FaceX = l.FX; r.FaceY = l.FY; r.PaceNow = 0;
            return;
        }
        var o = r.Order;
        bool moving = false, jog = false; int tx = 0, ty = 0, arrive = Rules.R.ArriveMm;
        switch (o.Kind)
        {
            case OrderKind.MoveTo: tx = o.TargetX; ty = o.TargetY; moving = true; break;
            case OrderKind.Retreat:
                if (o.TargetX == int.MinValue)
                {
                    if (Entry.TryGetValue(r.Side, out var e)) { r.Order.TargetX = e.X; r.Order.TargetY = e.Y; }
                    else { r.Order.TargetX = r.AnchorX - (int)((long)r.FaceX * Rules.R.RetreatMm >> 10); r.Order.TargetY = r.AnchorY - (int)((long)r.FaceY * Rules.R.RetreatMm >> 10); }
                    o = r.Order;
                }
                tx = o.TargetX; ty = o.TargetY; moving = true; jog = true; break;
            case OrderKind.Advance: case OrderKind.Charge: case OrderKind.AttackFormation:
                if (r.Kind == FormationKind.Cavalry && o.Kind == OrderKind.Charge && r.CavPhase >= PhRideOn) { AfterContactPoint(r, out tx, out ty); moving = true; }
                else moving = EnemyPoint(w, r, o, out tx, out ty);
                jog = o.Kind == OrderKind.Charge; break;
            case OrderKind.FlankLeft: case OrderKind.FlankRight:
                moving = FlankPoint(w, r, o, out tx, out ty); arrive = 25_000;
                if (!moving) { r.Order.Kind = OrderKind.Advance; o = r.Order; moving = EnemyPoint(w, r, o, out tx, out ty); }
                break;
            case OrderKind.FollowMe:
                {
                    int lf = o.TargetFighter >= 0 ? o.TargetFighter : r.Leader;
                    if (lf >= 0 && lf < w.Fighters.Count && w.Fighters[lf].Active)
                    {
                        var l = w.Fighters[lf]; tx = l.X; ty = l.Y; arrive = 4000;
                        long d = FightMath.Len(tx - r.AnchorX, ty - r.AnchorY);
                        moving = d > arrive; jog = d > 20_000;
                        if (!moving) { r.PaceNow = 0; return; }
                    }
                    break;
                }
        }
        int wantX = 0, wantY = 0; long dist = 0; int ux = 0, uy = 0;
        if (moving)
        {
            dist = FightMath.Len(tx - r.AnchorX, ty - r.AnchorY);
            if (dist <= arrive && o.Kind is OrderKind.MoveTo or OrderKind.Retreat or OrderKind.FlankLeft or OrderKind.FlankRight)
            {
                if (o.Kind is OrderKind.FlankLeft or OrderKind.FlankRight) { r.Order.Kind = OrderKind.Advance; }
                else Complete(r, tick);
                r.PaceNow = 0; return;
            }
            (ux, uy) = FightMath.Norm(tx - r.AnchorX, ty - r.AnchorY);
            wantX = ux; wantY = uy;
        }
        bool cav = r.Kind == FormationKind.Cavalry;
        if (cav) CavalryGait(w, r, o, moving, dist, tick);
        bool over = o.Kind is OrderKind.Advance or OrderKind.AttackFormation && r.InContact;
        if (o.Kind == OrderKind.Charge) over = cav ? CavalryChargeOver(r, o, tick) : r.InContact || tick - r.OrderGo >= Rules.R.ChargeMaxTicks;
        if (over) { Complete(r, tick); r.PaceNow = 0; return; }
        if (!moving && (r.WantFx != 0 || r.WantFy != 0)) { wantX = r.WantFx; wantY = r.WantFy; }
        if (cav && o.Kind == OrderKind.Charge && r.CavPhase == PhReform) { wantX = -r.ContactFx; wantY = -r.ContactFy; }      // a re-forming charge wheels back toward the enemy as it rides
        if (wantX != 0 || wantY != 0) Turn(r, wantX, wantY, sh);
        else r.TurnAcc = 0;
        int speed = 0;
        if (cav) speed = CavalryPace(r, moving, ux, uy, dist);
        else if (moving && r.DeployStart < 0 && r.Cohesion >= Rules.R.CohesionMinPm && FightMath.Dot(r.FaceX, r.FaceY, ux, uy) >= 724)
        {
            int cap = jog ? sh.JogMmps : sh.WalkMmps;
            speed = Math.Min(cap, r.BrakeMmps);
            int step = (int)Math.Min(speed / 20, dist);
            r.AnchorX += (int)((long)ux * step >> 10); r.AnchorY += (int)((long)uy * step >> 10);
        }
        r.PaceNow = speed;
    }

    /// <summary>The point an advance, charge or attack order marches on: the target formation's anchor, else the nearest enemy formation's, else the nearest enemy fighter. False: no enemy.</summary>
    private bool EnemyPoint(FightWorld w, FormationRow r, in FormationOrder o, out int x, out int y)
    {
        int row = o.TargetFormation > 0 ? o.TargetFormation - 1 : -1;
        if (row < 0 || row >= Rows.Count || Rows[row].Side == r.Side || Rows[row].ActiveNow == 0) row = NearestEnemyRow(r);
        if (row >= 0) { x = Rows[row].AnchorX; y = Rows[row].AnchorY; r.TgtRow = row; return true; }
        r.TgtRow = -1;
        for (int i = 0; i < r.Members.Length; i++)
        {
            if (r.Slot[i] < 0) continue;
            var e = w.NearestEnemyOf(w.Fighters[r.Members[i]]);
            if (e == null) break;
            x = e.X; y = e.Y; return true;
        }
        x = y = 0; return false;
    }

    /// <summary>The point on the enemy formation's flank (left or right as the formation sees it) 15 m beyond its end and a little behind its front.</summary>
    private bool FlankPoint(FightWorld w, FormationRow r, in FormationOrder o, out int x, out int y)
    {
        x = y = 0;
        int row = o.TargetFormation > 0 ? o.TargetFormation - 1 : -1;
        if (row < 0 || row >= Rows.Count || Rows[row].Side == r.Side || Rows[row].ActiveNow == 0) row = NearestEnemyRow(r);
        if (row < 0) return false;
        var e = Rows[row]; r.TgtRow = row;
        var def = Rules.Shape(e.Shape, e.Kind);
        int hw = (e.Files - 1) * def.LateralMm / 2 + Rules.R.FlankOffsetMm;
        Slots.ToWorld(e.AnchorX, e.AnchorY, e.FaceX, e.FaceY, hw, 5000, out int lx, out int ly);
        Slots.ToWorld(e.AnchorX, e.AnchorY, e.FaceX, e.FaceY, -hw, 5000, out int rx, out int ry);
        // my left is the side of the candidate lying further along my left normal (-fy, fx)
        long dl = (long)(lx - r.AnchorX) * -r.FaceY + (long)(ly - r.AnchorY) * r.FaceX;
        long dr = (long)(rx - r.AnchorX) * -r.FaceY + (long)(ry - r.AnchorY) * r.FaceX;
        bool left = o.Kind == OrderKind.FlankLeft;
        bool useFirst = left ? dl >= dr : dl < dr;
        if (useFirst) { x = lx; y = ly; } else { x = rx; y = ry; }
        return true;
    }

    /// <summary>Turns the facing toward (wx, wy) at the shape's rate (30 degrees a second for a line: one 5.625 degree step every 3.75 ticks); a wheel keeps its pivot flank where it was.</summary>
    private void Turn(FormationRow r, int wx, int wy, FormationShapeDef sh)
    {
        if (FightMath.Dot(r.FaceX, r.FaceY, wx, wy) >= 1020)
        {
            if (r.WantFx != 0 || r.WantFy != 0) { if (FightMath.Dot(r.FaceX, r.FaceY, r.WantFx, r.WantFy) >= 1020) { r.WantFx = r.WantFy = 0; r.WheelSign = 0; } }
            r.TurnAcc = 0; return;
        }
        if (TurnRate(r, sh) <= 0) return;
        int rate = TurnRate(r, sh);
        r.TurnAcc += rate * 50;
        int steps = r.TurnAcc / 5625;
        if (steps <= 0) return;
        r.TurnAcc -= steps * 5625;
        int hw = (r.Files - 1) * sh.LateralMm / 2, lat = r.WheelSign > 0 ? -hw : r.WheelSign < 0 ? hw : 0;
        Slots.ToWorld(r.AnchorX, r.AnchorY, r.FaceX, r.FaceY, lat, 0, out int px, out int py);
        (r.FaceX, r.FaceY) = FightMath.TurnToward(r.FaceX, r.FaceY, wx, wy, steps);
        if (r.WheelSign != 0)
        {
            Slots.ToWorld(0, 0, r.FaceX, r.FaceY, lat, 0, out int ox, out int oy);
            r.AnchorX = px - ox; r.AnchorY = py - oy;
        }
        if ((r.WantFx != 0 || r.WantFy != 0) && FightMath.Dot(r.FaceX, r.FaceY, r.WantFx, r.WantFy) >= 1020) { r.WantFx = r.WantFy = 0; r.WheelSign = 0; r.TurnAcc = 0; }
    }
}
