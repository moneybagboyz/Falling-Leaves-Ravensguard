using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>
/// Morale in the live fight (morale-capture-succession 4.1, 4.2; P4-D), replacing the spike's rules: each fighter's reading at 2 Hz, staggered by id (<c>(tick + id) % 10 == 0</c>),
/// from its courage, a decaying penalty (friends falling within 10 m, blows taken, a lost limb, a fallen leader or banner), the routing neighbours within 8 m, the flank timer, pain,
/// blood loss, the captain and the banner, a braced shield wall, the winning side and being outnumbered. Below 35 after a recent loss a fighter wavers (a 5% per second step back),
/// below 20 it routs (drops a shield below 15, a weapon below 10, flees; after 5 s 20 m from every enemy it has fled), below 10 and cornered it surrenders, if the winner accepts
/// (stream 0x451). A routed fighter with a captain within 20 m for 4 s at 40 or more rallies. The hero never routs by morale (C4); animals flee by blood loss only.
/// Integer only; draws on 0x450 and 0x451 keyed on the fight tick.
/// </summary>
public sealed partial class FightWorld
{
    private MoraleRules? _moraleK;
    private int _engageTick;

    /// <summary>No swing, shot or throw begun for 15 s (and the fight has run that long): both sides walked away, the fight is over (4.3).</summary>
    public bool Disengaged => Tick >= MoraleK.D.WalkAwayS * MoraleRules.TicksPerS && Tick - _engageTick >= MoraleK.D.WalkAwayS * MoraleRules.TicksPerS;

    /// <summary>
    /// The winner (4.3): the side with active fighters when only one has any; otherwise (both out, or both walked away) the side whose opponents are more often down, dead, yielded or
    /// fled; -1 for a draw.
    /// </summary>
    public int DecidedWinner
    {
        get
        {
            int w = WinnerTeam;
            if (w >= 0) return w;
            var teams = Fighters.Select(f => f.Team).Distinct().OrderBy(t => t).ToList();
            int best = -1, bestN = -1; bool tie = false;
            foreach (int t in teams)
            {
                int n = Fighters.Count(o => o.Team != t && o.Act is FAct.Down or FAct.Dead or FAct.Surrendered or FAct.Fled);
                if (n > bestN) { best = t; bestN = n; tie = false; } else if (n == bestN) tie = true;
            }
            return tie ? -1 : best;
        }
    }
    /// <summary>The morale rules of this fight's content (<c>combat/morale.jsonc</c>).</summary>
    public MoraleRules MoraleK => _moraleK ??= MoraleRules.For(Content.Db);

    /// <summary>Initialises a fighter's morale from its kit (courage x100) the first time it is needed.</summary>
    public void EnsureMorale(Fighter f)
    {
        if (f.M.Has(MoraleFlags.Inited)) return;
        var side = f.M.Side; var flags = f.M.Flags;
        f.M = MoraleState.Fresh(MoraleK.CourageX100(f.Kit.Courage));
        f.M.Side = side; f.M.Flags |= flags;
        f.M.ValueX100 = f.M.CourageX100;
    }

    /// <summary>What a team is (who accepts its enemies' surrender, how its losers fare): set by the fight's setup.</summary>
    public void SetSide(int team, SideKind kind)
    {
        foreach (var f in Fighters) if (f.Team == team) { EnsureMorale(f); f.M.Side = kind; }
    }

    public SideKind SideOf(int team)
    {
        foreach (var f in Fighters) if (f.Team == team) return f.M.Side;
        return SideKind.Folk;
    }

    public void SetCaptain(int fighter) { var f = Fighters[fighter]; EnsureMorale(f); f.M.Flags |= MoraleFlags.Captain; }
    public void SetBanner(int fighter) { var f = Fighters[fighter]; EnsureMorale(f); f.M.Flags |= MoraleFlags.Banner; }

    /// <summary>The usual leaders (4.1 g): on each side of three or more, the most skilled fighter leads (the squad's captain); the hero leads its side with Leadership 20 or more.</summary>
    public void AppointCaptains(int heroLeadership = 0)
    {
        var teams = Fighters.Select(f => f.Team).Distinct().OrderBy(t => t).ToList();
        foreach (int team in teams)
        {
            var side = Fighters.Where(f => f.Team == team).ToList();
            var hero = side.FirstOrDefault(f => f.Player);
            if (hero != null) { if (heroLeadership >= 20) SetCaptain(hero.Id); continue; }
            if (side.Count < 3) continue;
            var best = side.OrderByDescending(f => f.Skill).ThenBy(f => f.Id).First();
            SetCaptain(best.Id);
        }
    }

    // ------------------------------------------------------------------ hooks called by the fight step

    /// <summary>The morale step of a tick: every fighter whose turn it is (2 Hz, staggered by id) updates.</summary>
    private void MoraleStep()
    {
        foreach (var f in Fighters) EnsureMorale(f);
        foreach (var f in Fighters) if ((Tick + f.Id) % MoraleRules.EveryTicks == 0) UpdateMorale(f);
    }

    /// <summary>A blow taken costs 2 points; one from the flank or behind sets the 15 point flank term for 5 s (re-applied while flank blows land).</summary>
    private void MoraleOnHit(Fighter t, Facing facing)
    {
        EnsureMorale(t);
        var D = MoraleK.D;
        t.M.PenaltyX100 += D.HitTakenX100; t.M.LastLossTick = Tick;
        if (facing != Facing.Front) { t.M.FlankedUntil = Tick + D.FlankedForS * MoraleRules.TicksPerS; t.FlankedUntil = t.M.FlankedUntil; }
    }

    private void MoraleOnLimbLost(Fighter f)
    {
        EnsureMorale(f);
        f.M.PenaltyX100 += MoraleK.D.LimbLostX100; f.M.LastLossTick = Tick;
    }

    /// <summary>A fighter is out (down, dead or yielded): friends within 10 m count a fall at their next update; a fallen captain or banner costs everyone near.</summary>
    private void MoraleOnFallen(Fighter f)
    {
        var D = MoraleK.D;
        bool counts = f.Act is FAct.Down or FAct.Dead or FAct.Surrendered;
        long r = D.FriendDownRadiusM * 1000L, rl = D.LeaderDownRadiusM * 1000L, rb = D.BannerRadiusM * 1000L;
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || o.Team != f.Team || !o.Active) continue;
            EnsureMorale(o);
            long d = DistMm(o, f);
            if (counts && d <= r) o.M.PendingFalls++;
            if (f.M.Has(MoraleFlags.Captain) && d <= rl) { o.M.PenaltyX100 += D.LeaderDownX100; o.M.LastLossTick = Tick; }
            if (f.M.Has(MoraleFlags.Banner) && d <= rb) { o.M.PenaltyX100 += D.BannerLostX100; o.M.LastLossTick = Tick; }
        }
    }

    /// <summary>Blows from behind against a routing fighter hit x1.5 (4.1), permille of the momentum.</summary>
    private int MoraleRearPm(Fighter t, Facing facing) => t.Routing && facing == Facing.Rear ? 1500 : 1000;

    /// <summary>A wavering fighter's one pace back (the brain's footwork while it lasts). True while stepping back.</summary>
    public bool MoraleStepBack(Fighter f)
    {
        if (Tick >= f.M.StepBackUntil || f.Routing || f.Player) return false;
        var c = f.Cmd;
        c.MoveX = -f.FX; c.MoveY = -f.FY; c.Sprint = false;
        return true;
    }

    /// <summary>The hero (or anyone) yields (4.2): the same acceptance as any surrender; a refused yield is just a fight.</summary>
    public bool Yield(Fighter f)
    {
        if (!f.Active) return false;
        EnsureMorale(f);
        return TrySurrender(f);
    }

    // ------------------------------------------------------------------ the update

    /// <summary>Everything a reading needs about the fighter's surroundings (one pass over the fighters).</summary>
    public MoraleTerms TermsOf(Fighter f)
    {
        var D = MoraleK.D; ref var m = ref f.M;
        long rr = D.RoutingNeighbourRadiusM * 1000L, ro = D.OutnumberedRadiusM * 1000L, rc = D.CaptainRadiusM * 1000L, rb = D.BannerRadiusM * 1000L;
        long ra = D.AloneRadiusM * 1000L;
        int foes = 0, friends = 1, foesAround = 0; bool company = false;
        bool captain = m.Has(MoraleFlags.Captain), banner = m.Has(MoraleFlags.Banner);
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || !o.Active) continue;
            long d = DistMm(f, o);
            if (o.Team == f.Team)
            {
                if (o.Routing) continue;
                if (d <= ra) company = true;
                if (d <= ro) friends++;
                if (d <= rc && o.M.Has(MoraleFlags.Captain)) captain = true;
                if (d <= rb && o.M.Has(MoraleFlags.Banner)) banner = true;
            }
            else { if (d <= ro) foes++; if (d <= ra) foesAround++; }
        }
        int pain = (int)Math.Min(100, f.Book.PainX100 * 100L / Math.Max(1, f.Book.KnockoutPainX100));
        bool outnumbered = foes > 0 && (long)foes * 1000 >= (long)D.OutnumberedRatioPm * friends;
        return new MoraleTerms(m.CourageX100, m.PenaltyX100, m.RoutAccX100, Tick < m.FlankedUntil ? D.FlankedX100 : 0, pain, f.Book.LossPm,
            captain ? D.CaptainX100 : 0, banner ? D.BannerX100 : 0, Braced(f) ? D.ShieldWallX100 : 0, Winning(f.Team) ? D.WinningX100 : 0, outnumbered ? D.OutnumberedX100 : 0,
            0, !company && foes > 0 ? Math.Min(D.AloneCapX100, D.AloneX100 + D.AlonePerFoeX100 * (foesAround - 1)) : 0);
    }

    /// <summary>The side has at least 2:1 kills (enemies down or dead, not themselves) in the last 30 s.</summary>
    private bool Winning(int team)
    {
        int window = MoraleK.D.WinningWindowS * MoraleRules.TicksPerS, ours = 0, theirs = 0;
        foreach (var o in Fighters)
        {
            if (o.Act is not (FAct.Down or FAct.Dead) || o.EndedAt < Tick - window) continue;
            if (o.Team == team) theirs++; else ours++;
        }
        return ours >= 2 && (long)ours * 1000 >= (long)MoraleK.D.WinningRatioPm * theirs;
    }

    private void UpdateMorale(Fighter f)
    {
        if (!f.Active) return;
        var R = MoraleK; var D = R.D; ref var m = ref f.M; var b = f.Book;
        m.PenaltyX100 = Math.Max(0, m.PenaltyX100 - R.DecayPerUpdate);
        if (m.PendingFalls > 0) { m.PenaltyX100 += Math.Min(D.FriendDownCapX100, m.PendingFalls * D.FriendDownX100); m.PendingFalls = 0; m.LastLossTick = Tick; }
        int routing = 0; long rr = D.RoutingNeighbourRadiusM * 1000L;
        foreach (var o in Fighters)
        {
            if (o.Id == f.Id || !o.Active && !o.Routing) continue;
            if (o.Team == f.Team) { if (o.Routing && DistMm(f, o) <= rr) routing++; }
        }
        if (D.FlankCheckMm > 0 && Quadrants(f, D.FlankCheckMm) >= 2) m.FlankedUntil = Tick + D.FlankedForS * MoraleRules.TicksPerS;   // the flank check (4.1 c): beset on two sides
        if (routing > 0) { if (Tick >= m.ImmuneUntil) m.RoutAccX100 = Math.Min(D.RoutingCapX100, m.RoutAccX100 + routing * R.RoutingPerNeighbourPerUpdate); }
        else m.RoutAccX100 = Math.Max(0, m.RoutAccX100 - R.DecayPerUpdate);
        int value = R.Value(TermsOf(f));
        m.ValueX100 = value;
        f.Morale = value / 100; f.MoralePenalty = (m.PenaltyX100 + m.RoutAccX100) / 100;
        if (f.Player)                                                      // the hero never routs by morale (C4); one who ran 20 m from the start and stays 20 m clear for 5 s has fled
        {
            if (Fighters.Any(e => e.Team != f.Team && e.Active) && FightMath.Len(f.X - f.Kit.X, f.Y - f.Kit.Y) >= D.FleeEscapeM * 1000L) EscapeCheck(f);
            return;
        }

        int beast = R.BeastFleeBloodPm(f.Kit.Plan);
        if (beast != -1)
        {
            if (!f.Routing && b.LossPm >= beast) StartRout(f);
            if (f.Routing) EscapeCheck(f);
            return;
        }
        bool hurtFlee = f.Stance.FleeBloodLossPm > 0 && b.LossPm >= f.Stance.FleeBloodLossPm;
        if (f.Routing)
        {
            if (value >= D.RallyAboveX100 && !hurtFlee && CaptainWithin(f, D.RallyCaptainRadiusM * 1000L))
            {
                if (m.RallySince < 0) m.RallySince = Tick;
                else if (Tick - m.RallySince >= D.RallyHoldS * MoraleRules.TicksPerS) { Rally(f); return; }
            }
            else m.RallySince = -1;
            Drops(f, value);
            if (value < D.SurrenderBelowX100 && Tick >= m.RefusedUntil && Cornered(f) && TrySurrender(f)) return;
            EscapeCheck(f);
            return;
        }
        var mood = R.MoodOf(value, Tick - m.LastLossTick <= D.RecentLossS * MoraleRules.TicksPerS);
        if (mood == MoraleMood.Routing || hurtFlee)
        {
            StartRout(f);                                                   // it breaks; below 10 and cornered it yields at once
            if (value < D.SurrenderBelowX100 && Tick >= m.RefusedUntil && Cornered(f)) TrySurrender(f);
            return;
        }
        if (mood == MoraleMood.Wavering)
        {
            m.Mood = MoraleMood.Wavering;
            int pm = D.WaverStepPmPerS * MoraleRules.EveryTicks / MoraleRules.TicksPerS;
            if (Tick >= m.StepBackUntil && DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), RngStreams.MoraleRally, 1, 0, 1000) < pm)
                m.StepBackUntil = Tick + Math.Max(1, D.StepBackMm * MoraleRules.TicksPerS / Math.Max(1, K.WalkMmps));
        }
        else m.Mood = MoraleMood.Steady;
    }

    private bool CaptainWithin(Fighter f, long r)
    {
        foreach (var o in Fighters)
            if (o.Id != f.Id && o.Team == f.Team && o.Active && !o.Routing && o.M.Has(MoraleFlags.Captain) && DistMm(f, o) <= r) return true;
        return false;
    }

    private void StartRout(Fighter f)
    {
        ref var m = ref f.M;
        f.Routing = true; m.Mood = MoraleMood.Routing; m.RoutTick = Tick; m.RallySince = -1; m.EscapeSince = -1; m.StepBackUntil = -1;
        if (f.Act is FAct.Windup or FAct.Block or FAct.Drawing or FAct.Aiming) f.Act = FAct.Idle;
        Emit(FightEventKind.Rout, f.Id, -1);
    }

    private void Rally(Fighter f)
    {
        ref var m = ref f.M;
        f.Routing = false; m.Mood = MoraleMood.Rallied; m.RallySince = -1; m.EscapeSince = -1;
        m.RoutAccX100 = 0; m.ImmuneUntil = Tick + MoraleK.D.RallyImmuneS * MoraleRules.TicksPerS;
        Emit(FightEventKind.Rally, f.Id, -1);
    }

    /// <summary>After 5 s at 20 m from every enemy a routing fighter has fled the fight (it goes back to its camp with its wounds).</summary>
    private void EscapeCheck(Fighter f)
    {
        var D = MoraleK.D; ref var m = ref f.M;
        long nd = long.MaxValue;
        foreach (var e in Fighters) if (e.Team != f.Team && e.Active) nd = Math.Min(nd, DistMm(e, f));
        if (nd <= D.FleeEscapeM * 1000L) { m.EscapeSince = -1; return; }
        if (m.EscapeSince < 0) { m.EscapeSince = Tick; return; }
        if (Tick - m.EscapeSince < D.FleeEscapeS * MoraleRules.TicksPerS) return;
        f.Act = FAct.Fled; f.EndedAt = Tick; f.Routing = false; m.Mood = MoraleMood.Fled;
        Emit(FightEventKind.Fled, f.Id, -1);
        Announce(f);
    }

    /// <summary>A routing fighter drops its shield below 15 and its weapon below 10, each with 30% once (stream 0x450).</summary>
    private void Drops(Fighter f, int value)
    {
        var D = MoraleK.D; ref var m = ref f.M;
        if (value < D.DropShieldBelowX100 && f.Shield != null && !m.Has(MoraleFlags.ShieldRolled))
        {
            m.Flags |= MoraleFlags.ShieldRolled;
            if (DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), RngStreams.MoraleRally, 2, 0, 1000) < D.DropPm)
            {
                Ground.Add(new GroundItem(f.Shield.Def.Id, f.Shield.Def.DefaultMaterial ?? "oak", f.X, f.Y, false));
                f.Shield = null;
                Emit(FightEventKind.Disarmed, f.Id, -1, -1, 1);
            }
        }
        if (value < D.DropWeaponBelowX100 && !m.Has(MoraleFlags.WeaponRolled) && f.Weapon.Shape.Family is not ("natural" or "unarmed") && f.Launcher == null)
        {
            m.Flags |= MoraleFlags.WeaponRolled;
            if (DetRng.Range(Seed, Tick, (ulong)(f.Id + 1), RngStreams.MoraleRally, 3, 0, 1000) < D.DropPm)
            {
                if (f.Improvised == null) Ground.Add(new GroundItem(f.Weapon.Shape.Id, f.Weapon.Head.Id, f.X, f.Y, false));
                Disarm(f);
                Emit(FightEventKind.Disarmed, f.Id, -1, -1, 2);
            }
        }
    }

    /// <summary>
    /// Cornered (4.1): enemies within 3 m in at least two of the four quadrants around the fighter (toward its side's rally point, away, left, right), or the flee path blocked: an enemy
    /// within 6 m in the direction of the side's rally point (where the side stood when the fight began).
    /// </summary>
    /// <summary>How many of the four quadrants around the fighter (toward its side's rally point, away, left, right) hold an active enemy within <paramref name="r"/> mm.</summary>
    public int Quadrants(Fighter f, long r)
    {
        var (px, py) = RallyPoint(f.Team);
        var (ax, ay) = FightMath.Norm(px - f.X, py - f.Y);
        if (ax == 0 && ay == 0) ax = FightMath.One;
        int quads = 0;
        foreach (var e in Fighters)
        {
            if (e.Team == f.Team || !e.Active || DistMm(f, e) > r) continue;
            var (ux, uy) = FightMath.Norm(e.X - f.X, e.Y - f.Y);
            int dot = FightMath.Dot(ax, ay, ux, uy), cross = (int)(((long)ax * uy - (long)ay * ux) >> 10);
            quads |= 1 << (Math.Abs(dot) >= Math.Abs(cross) ? (dot >= 0 ? 0 : 1) : (cross >= 0 ? 2 : 3));
        }
        return System.Numerics.BitOperations.PopCount((uint)quads);
    }

    public bool Cornered(Fighter f)
    {
        var D = MoraleK.D;
        long rc = D.CorneredM * 1000L, rp = D.FleePathBlockM * 1000L;
        int quads = 0;
        var (px, py) = RallyPoint(f.Team);
        var (dx0, dy0) = FightMath.Norm(px - f.X, py - f.Y);
        bool path = dx0 != 0 || dy0 != 0;
        foreach (var e in Fighters)
        {
            if (e.Team == f.Team || !e.Active) continue;
            long d = DistMm(f, e);
            if (d > rp) continue;
            var (ux, uy) = FightMath.Norm(e.X - f.X, e.Y - f.Y);
            if (d <= rc)
            {
                int ax = path ? dx0 : FightMath.One, ay = path ? dy0 : 0;           // quadrants around the way home (not the facing: the same near and far, ML-16)
                int dot = FightMath.Dot(ax, ay, ux, uy), cross = (int)(((long)ax * uy - (long)ay * ux) >> 10);
                int q = Math.Abs(dot) >= Math.Abs(cross) ? (dot >= 0 ? 0 : 1) : (cross >= 0 ? 2 : 3);
                quads |= 1 << q;
            }
            if (path && FightMath.Dot(dx0, dy0, ux, uy) >= 724) return true;
        }
        return System.Numerics.BitOperations.PopCount((uint)quads) >= 2;
    }

    /// <summary>Where a side rallies and flees to: the centre of where its fighters stood at the start.</summary>
    public (int X, int Y) RallyPoint(int team)
    {
        long sx = 0, sy = 0; int n = 0;
        foreach (var o in Fighters) if (o.Team == team) { sx += o.Kit.X; sy += o.Kit.Y; n++; }
        return n == 0 ? (0, 0) : ((int)(sx / n), (int)(sy / n));
    }

    /// <summary>
    /// A surrender (4.2): the nearest enemy decides for its side (armies 90%, bandits always, beasts and draugr never; stream 0x451 keyed on the tick and the winner). Accepted:
    /// the fighter drops its weapon, is out of the fight and counts as fallen. Refused: it fights on and asks again no sooner than 10 s later.
    /// </summary>
    private bool TrySurrender(Fighter f)
    {
        Fighter? w = null; long nd = long.MaxValue;
        foreach (var e in Fighters) if (e.Team != f.Team && e.Active) { long d = DistMm(e, f); if (d < nd) { nd = d; w = e; } }
        if (w == null) return false;
        EnsureMorale(w);
        int accept = MoraleK.AcceptPm(w.M.Side);
        bool ok = accept >= 1000 || accept > 0 && DetRng.Range(Seed, Tick, (ulong)(w.Id + 1), RngStreams.Surrender, (uint)f.Id, 0, 1000) < accept;
        if (!ok)
        {
            f.M.RefusedUntil = Tick + MoraleK.D.RefusedRetryS * MoraleRules.TicksPerS;
            Emit(FightEventKind.Surrender, f.Id, w.Id, -1, 1);
            return false;
        }
        if (f.Weapon.Shape.Family is not ("natural" or "unarmed") && f.Improvised == null && f.Launcher == null) Ground.Add(new GroundItem(f.Weapon.Shape.Id, f.Weapon.Head.Id, f.X, f.Y, false));
        if (f.Weapon.Shape.Family is not ("natural" or "unarmed")) Disarm(f);
        f.Act = FAct.Surrendered; f.EndedAt = Tick; f.Routing = false; f.M.Mood = MoraleMood.Surrendered;
        Emit(FightEventKind.Surrender, f.Id, w.Id);
        Announce(f);
        return true;
    }

    // ------------------------------------------------------------------ saving and the hash

    /// <summary>The morale rows of a fight record (one per fighter, in id order).</summary>
    public long[][] CaptureMorale() => [.. Fighters.Select(f => f.M.Pack()), [_engageTick]];

    public void RestoreMorale(long[][]? rows)
    {
        if (rows == null) return;
        for (int i = 0; i < rows.Length && i < Fighters.Count; i++) Fighters[i].M = MoraleState.Unpack(rows[i]);
        if (rows.Length == Fighters.Count + 1 && rows[^1].Length == 1) _engageTick = (int)rows[^1][0];
    }

    /// <summary>The <c>MoraleHash</c> (6): every fighter's morale state in id order.</summary>
    public ulong MoraleHash()
    {
        ulong h = 14695981039346656037UL;
        foreach (var f in Fighters) h = f.M.Hash(FightMath.Mix(h, f.Id));
        return FightMath.Mix(h, _engageTick);
    }
}
