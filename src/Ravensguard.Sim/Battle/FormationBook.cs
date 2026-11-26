using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// P7-BB-B: the formations of one fight (battle-bubble-and-formations 3, 4.3 to 4.7). A fight with no formation has no book and runs exactly as before. The book forms formations from fighters,
/// lays their members out in slots, closes ranks when a man falls, brakes the anchor, turns and deploys, takes orders with their delay and runs captains; the brain reads a member's slot (steering and
/// leash), the morale step reads the formation terms. Every step is integer arithmetic, in row order then slot order, with the registered streams 0x780 to 0x783 for the few draws.
/// Where the pieces live: this file (forming, slots, closing ranks, cohesion, saving), <c>FormationBook.Orders.cs</c> (orders, delay, the anchor), <c>FormationBook.Captains.cs</c> (4.7) and
/// <c>FormationBook.Morale.cs</c> (4.6 and the brain hooks).
/// </summary>
public sealed partial class FormationBook
{
    public List<FormationRow> Rows { get; } = [];
    public FormationRules Rules { get; }
    /// <summary>The hero's fighter (-1: none) and his Leadership: his aura (4.6) and, at 20 or more, his captain flag.</summary>
    public int Hero = -1, HeroLeadership;
    /// <summary>Each side's entry point, where its routed soldiers run to (4.6).</summary>
    public SortedDictionary<int, (int X, int Y)> Entry { get; } = new();
    /// <summary>Orders lost because their runner fell (tick, row, serial, runner): the overlay says so.</summary>
    public List<(int Tick, int Row, int Serial, int Runner)> Lost { get; } = [];
    private int _serial;

    public FormationBook(FormationRules rules) { Rules = rules; }

    public FormationRow? RowOf(Fighter f) => f.FmRow >= 0 && f.FmRow < Rows.Count ? Rows[f.FmRow] : null;

    /// <summary>The row of a side by its number (null: none).</summary>
    public FormationRow? Find(int side, int id) { foreach (var r in Rows) if (r.Side == side && r.Id == id) return r; return null; }

    // ------------------------------------------------------------------ forming

    /// <summary>
    /// Forms a formation of <paramref name="fighters"/> on <paramref name="side"/> with its centre front at (<paramref name="ax"/>, <paramref name="ay"/>) facing (<paramref name="faceX"/>, <paramref name="faceY"/>):
    /// members into slots by 4.3 (role, then skill, then id), the captain by 4.7 (<paramref name="officers"/> first, then Tactics, Leadership, id), the morale captain flag on him. With
    /// <paramref name="place"/> the men are put on their slots facing the front (a battle deployed already formed).
    /// </summary>
    public FormationRow Form(FightWorld w, int side, byte id, FormationKind kind, IReadOnlyList<int> fighters, int ax, int ay, int faceX, int faceY, Formation shape = Formation.Line, int ranks = 0, int drill = 0,
        IReadOnlyCollection<int>? officers = null, IReadOnlyDictionary<int, int>? leadership = null, int banner = -1, int leader = -1, bool place = false)
    {
        var def = Rules.Shape(shape, kind);
        var (fx, fy) = FightMath.Norm(faceX, faceY);
        if (fx == 0 && fy == 0) fx = FightMath.One;
        int n = fighters.Count;
        var ids = new int[n]; for (int i = 0; i < n; i++) ids[i] = fighters[i];
        OrderMembers(w, kind, ids, shape);
        var r = new FormationRow
        {
            Index = Rows.Count, Id = id, Side = side, Kind = kind, Members = ids, Shape = shape, Ranks = ranks > 0 ? ranks : Math.Max(1, def.Ranks), LayoutN = n, AnchorX = ax, AnchorY = ay, FaceX = fx, FaceY = fy,
            Drill = Math.Clamp(drill, 0, 1000), StartCount = n, Banner = banner, Leader = leader >= 0 ? leader : Hero, TgtRow = -1,
            Slot = new int[n], Leadership = new int[n], Pace = new int[n], FlankBlow = new int[n], Occ = new int[n], VacSince = new int[n], Officer = new int[n],
            Lat = new int[n], Back = new int[n], ColLat = new int[n], ColBack = new int[n],
        };
        InitCavalry(r, n);
        for (int i = 0; i < n; i++)
        {
            r.Slot[i] = i; r.Occ[i] = i; r.VacSince[i] = -1; r.FlankBlow[i] = -100_000;
            if (leadership != null && leadership.TryGetValue(ids[i], out int ld)) r.Leadership[i] = ld;
            if (officers != null && officers.Contains(ids[i])) r.Officer[i] = 1;
            var f = w.Fighters[ids[i]]; f.FmRow = r.Index; f.FmMember = i;
        }
        Rows.Add(r);
        Relayout(r);
        r.Captain = Elect(w, r);
        if (r.Captain >= 0) w.SetCaptain(r.Captain);
        if (r.Kind == FormationKind.Cavalry && PlaceCaptainAtPoint(w, r)) Relayout(r);
        if (banner >= 0) w.SetBanner(banner);
        w.Grid.Dirty = true;
        r.BrakeMmps = ComputeBrake(w, r);
        if (place)
            for (int s = 0; s < n; s++)
            {
                var f = w.Fighters[r.FighterAt(s)];
                SlotWorld(r, s, w.Tick + 1, out int x, out int y);
                f.X = x; f.Y = y; f.FX = fx; f.FY = fy; f.BraceX = x; f.BraceY = y; f.BraceSince = w.Tick;
            }
        return r;
    }

    /// <summary>Sets the hero (his aura reaches 10 m + Leadership / 5 m; with Leadership 20 or more he counts as a captain).</summary>
    public void SetHero(FightWorld w, int fighter, int leadership)
    {
        Hero = fighter; HeroLeadership = leadership;
        if (leadership >= 20 && fighter >= 0) w.SetCaptain(fighter);
        foreach (var r in Rows) if (r.Leader < 0 && r.Side == w.Fighters[fighter].Team) r.Leader = fighter;
    }

    public void SetEntry(int side, int x, int y) => Entry[side] = (x, y);

    /// <summary>The captain of a formation (4.7): men-at-arms and knights first, then the highest Tactics, ties by Leadership, then id, among the members still slotted and on their feet.</summary>
    private int Elect(FightWorld w, FormationRow r)
    {
        int best = -1; (int off, int tac, int lead) bk = default;
        for (int i = 0; i < r.Members.Length; i++)
        {
            if (r.Slot[i] < 0) continue;
            var f = w.Fighters[r.Members[i]];
            if (!f.Active || f.Routing) continue;
            var k = (r.Officer[i], Math.Max(0, f.Kit.Tactics), r.Leadership[i]);
            if (best < 0 || k.Item1 > bk.off || k.Item1 == bk.off && (k.Item2 > bk.tac || k.Item2 == bk.tac && (k.Item3 > bk.lead || k.Item3 == bk.lead && r.Members[i] < best)))
            { best = r.Members[i]; bk = (k.Item1, k.Item2, k.Item3); }
        }
        return best;
    }

    // ------------------------------------------------------------------ the layout

    internal void Relayout(FormationRow r)
    {
        int n = r.Members.Length;
        if (r.Lat.Length < n) { r.Lat = new int[n]; r.Back = new int[n]; }
        if (r.ColLat.Length < n) { r.ColLat = new int[n]; r.ColBack = new int[n]; }
        var def = Rules.Shape(r.Shape, r.Kind);
        Slots.Layout(def, r.LayoutN, r.Ranks, r.Lat, r.Back);
        r.Files = def.Layout == "rank" ? Slots.Files(r.LayoutN, r.Ranks, def) : def.Layout == "wedge" ? Slots.WedgeWidth(r.LayoutN) : Math.Max(1, r.LayoutN);
        if (r.DeployStart >= 0) Slots.Layout(Rules.Shape(Formation.Column, r.Kind), r.LayoutN, r.Ranks, r.ColLat, r.ColBack);
        int vac = 0; for (int s = 0; s < n; s++) if (r.VacSince[s] >= 0) vac++;
        r.VacCount = vac;
    }

    private int ReleaseTick(FormationRow r, int slot)
    {
        int colRanks = Math.Max(1, (r.LayoutN + 2) / 3);
        return r.DeployStart + (int)((long)(slot / 3) * r.DeployTicks / colRanks);
    }

    /// <summary>The world position of a slot at a tick (while deploying from a column, a rank that has not been released yet is still at its column slot).</summary>
    public void SlotWorld(FormationRow r, int slot, int tick, out int x, out int y)
    {
        int lat, back;
        if (r.DeployStart >= 0 && tick < r.DeployStart + r.DeployTicks && tick < ReleaseTick(r, slot)) { lat = r.ColLat[slot]; back = r.ColBack[slot]; }
        else { lat = r.Lat[slot]; back = r.Back[slot]; }
        Slots.ToWorld(r.AnchorX, r.AnchorY, r.FaceX, r.FaceY, lat, back, out x, out y);
    }

    /// <summary>Changes the shape (4.3): the members still slotted are put back in slot order for the new shape, vacancies gone; leaving a column for a rank shape keeps the order and deploys (the ranks walk to their files, one rank after another).</summary>
    public void SetShape(FightWorld w, FormationRow r, Formation shape, int tick)
    {
        if (shape == r.Shape) return;
        var old = r.Shape; var oldDef = Rules.Shape(old, r.Kind); var def = Rules.Shape(shape, r.Kind);
        bool deploy = old == Formation.Column && r.DeployStart < 0 && def.Layout == "rank" && shape != Formation.Column && r.LayoutN > 0;
        r.Shape = shape;
        if (deploy)
        {
            Slots.Layout(oldDef, r.LayoutN, r.Ranks, r.ColLat, r.ColBack);
            int colRanks = Math.Max(1, (r.LayoutN + 2) / 3);
            long ticks = (long)colRanks * Rules.R.DeployRankMm * 20 / Math.Max(1, Rules.R.DeployPaceMmps);
            ticks = ticks * (1000 - (long)r.Drill * Rules.R.DeployDrillCutPm / 1000) / 1000;
            r.DeployStart = tick - 1; r.DeployTicks = (int)Math.Max(1, ticks);
            if (def.Ranks > 0) r.Ranks = def.Ranks;
            Relayout(r);
        }
        else
        {
            r.DeployStart = -1;
            if (def.Ranks > 0) r.Ranks = def.Ranks;
            Reslot(w, r);
        }
        r.BrakeMmps = ComputeBrake(w, r);
    }

    /// <summary>Compacts and re-sorts the slotted members into slots 0..m-1 for the current shape.</summary>
    private void Reslot(FightWorld w, FormationRow r)
    {
        int m = 0;
        for (int i = 0; i < r.Members.Length; i++) if (r.Slot[i] >= 0) m++;
        var ids = new int[m]; int k = 0;
        for (int i = 0; i < r.Members.Length; i++) if (r.Slot[i] >= 0) ids[k++] = r.Members[i];
        OrderMembers(w, r.Kind, ids, r.Shape);
        Array.Fill(r.Occ, -1); Array.Fill(r.VacSince, -1);
        for (int s = 0; s < m; s++)
        {
            int mi = r.IndexOfFighter(ids[s]);
            r.Slot[mi] = s; r.Occ[s] = mi;
        }
        r.LayoutN = m; r.VacCount = 0;
        Relayout(r);
        if (r.Kind == FormationKind.Cavalry && PlaceCaptainAtPoint(w, r)) Relayout(r);
    }

    /// <summary>The 20th-percentile pace of the rank-0 members (4.3), mm a second: the speed the anchor may not exceed (wounded and heavily laden men slow the line).</summary>
    private int ComputeBrake(FightWorld w, FormationRow r)
    {
        if (r.Kind == FormationKind.Cavalry) return CavalryBrake(w, r);
        int n = 0;
        var buf = ScratchInts(r.Members.Length);
        for (int s = 0; s < Math.Min(r.Files, r.LayoutN); s++)
        {
            int mi = r.Occ[s]; if (mi < 0) continue;
            buf[n++] = PaceCap(w, w.Fighters[r.Members[mi]]);
        }
        if (n == 0) return int.MaxValue;
        Array.Sort(buf, 0, n);
        return buf[Math.Min(n - 1, n * Rules.R.BrakePercentile / 100)];
    }

    /// <summary>The fastest a man can keep step: the fight's walk, less his wounds (the walk share and the weakened fifth).</summary>
    private static int PaceCap(FightWorld w, Fighter f)
    {
        int v = (int)((long)w.K.WalkMmps * f.WalkPm / 1000);
        return f.Book.Weakened ? v * 800 / 1000 : v;
    }

    private int[] _scratch = [];
    private int[] ScratchInts(int n) { if (_scratch.Length < n) _scratch = new int[Math.Max(n, _scratch.Length * 2)]; return _scratch; }

    // ------------------------------------------------------------------ the step

    /// <summary>The formation layer of a tick, run before the brains (4.2): rows in order.</summary>
    public void Step(FightWorld w)
    {
        int tick = w.Tick + 1;
        // P7 BB-C: every row's counts are this tick's before any row reads another's. They are derived and never saved, so a row that read the counts of the row after it (the previous tick's, or none after a load)
        // would see something else in a loaded battle; a save, a load and a continue must equal the unbroken run
        for (int i = 0; i < Rows.Count; i++) Count(w, Rows[i]);
        for (int i = 0; i < Rows.Count; i++) StepRow(w, Rows[i], tick);
    }

    /// <summary>What <see cref="Census"/> counts, without giving up any slot.</summary>
    private static void Count(FightWorld w, FormationRow r)
    {
        int active = 0, routing = 0, total = 0;
        for (int i = 0; i < r.Members.Length; i++)
        {
            var f = w.Fighters[r.Members[i]];
            if (f.Active) { total++; if (f.Routing) routing++; }
            if (r.Slot[i] >= 0 && f.Active && !f.Routing) active++;
        }
        r.ActiveNow = active; r.RoutingNow = routing; r.TotalNow = total;
    }

    private void StepRow(FightWorld w, FormationRow r, int tick)
    {
        Census(w, r);
        if (r.Active > 0 && (r.Captain < 0 || !w.Fighters[r.Captain].Active || w.Fighters[r.Captain].Routing || r.Slot[r.IndexOfFighter(r.Captain)] < 0))
        {
            r.Captain = Elect(w, r);
            if (r.Captain >= 0) w.SetCaptain(r.Captain);
        }
        Arrivals(w, r, tick);
        if (r.Active > 0 && r.Captain >= 0) CaptainStep(w, r, tick);
        if (r.Foot != FootNone) FootStep(w, r, tick);                                      // [P7 CV-B] getting down and up, the horse-holders
        UpdateContact(w, r, tick);
        if (r.Kind == FormationKind.Cavalry) CavalryStep(w, r, tick);
        if (r.Kind == FormationKind.Cavalry && r.Active > 0 && r.Captain >= 0) SkirmishStep(w, r, tick);      // [P7 CV-B] horse archers keep their distance
        Drive(w, r, tick);
        if (r.DeployStart >= 0 && tick >= r.DeployStart + r.DeployTicks) r.DeployStart = -1;
        if (r.VacCount > 0) CloseRanks(w, r, tick);
        Cohesion(w, r, tick);
        if ((tick + r.Index) % 10 == 0) { r.BrakeMmps = ComputeBrake(w, r); MoraleStep(w, r, tick); }
    }

    /// <summary>Who is still in the ranks: a member down, dead, fled, yielded or routing gives up his slot, which becomes a vacancy (4.3).</summary>
    private void Census(FightWorld w, FormationRow r)
    {
        int active = 0, routing = 0, total = 0;
        for (int i = 0; i < r.Members.Length; i++)
        {
            var f = w.Fighters[r.Members[i]];
            if (f.Active) { total++; if (f.Routing) routing++; }
            int s = r.Slot[i];
            if (s < 0) continue;
            if (f.Active && !f.Routing) { active++; continue; }
            r.Occ[s] = -1; r.Slot[i] = -1;
            if (r.VacSince[s] < 0) { r.VacSince[s] = w.Tick; r.VacCount++; }
        }
        r.ActiveNow = active; r.RoutingNow = routing; r.TotalNow = total;
    }

    /// <summary>Closing ranks (4.3): a vacancy is filled from behind after 40 ticks less Drill / 25; with nobody behind, the flank man of the rank steps in. A man who steps up leaves a vacancy of his own, which fills after the same wait and a little hesitation (0..10 ticks, stream 0x782).</summary>
    private void CloseRanks(FightWorld w, FormationRow r, int tick)
    {
        int wait = Math.Max(1, Rules.R.CloseTicks - r.Drill / Math.Max(1, Rules.R.CloseDrillDiv));
        int n = r.LayoutN;
        for (int s = 0; s < n; s++)
        {
            int since = r.VacSince[s];
            if (since < 0 || tick < since + wait) continue;
            int from = -1;
            bool wedge = r.Shape == Formation.Wedge;
            if (wedge) from = WedgeBehind(r, s);
            else for (int t = s + r.Files; t < n; t += r.Files) if (r.Occ[t] >= 0) { from = t; break; }
            if (from < 0)
            {
                Slots.RankBounds(wedge, s, r.Files, n, out int first, out int last);
                int left = -1, right = -1;
                for (int t = first; t < s; t++) if (r.Occ[t] >= 0) { left = t; break; }
                for (int t = last; t > s; t--) if (r.Occ[t] >= 0) { right = t; break; }
                from = left < 0 ? right : right < 0 ? left : (s - left <= right - s ? left : right);
            }
            if (from < 0) { r.VacSince[s] = -1; r.VacCount--; continue; }
            int mi = r.Occ[from];
            r.Occ[s] = mi; r.Slot[mi] = s; r.Occ[from] = -1;
            r.VacSince[s] = -1;
            int hes = Rules.R.HesitateMaxTicks > 0
                ? DetRng.Range(w.Seed, tick, ((ulong)w.FightId << 24) ^ (ulong)(r.Members[mi] + 1), RngStreams.RankClosingHesitation, (uint)r.SlotChanges, 0, Rules.R.HesitateMaxTicks + 1) : 0;
            r.VacSince[from] = tick + hes;      // r.VacCount: one filled, one opened
            r.SlotChanges++;
        }
    }

    /// <summary>Cohesion (4.3): the permille of the slotted members within 2 m of their slot.</summary>
    private void Cohesion(FightWorld w, FormationRow r, int tick)
    {
        int ok = 0, tot = 0; long lim = (long)Rules.R.CohesionMm * Rules.R.CohesionMm;
        for (int s = 0; s < r.LayoutN; s++)
        {
            int mi = r.Occ[s]; if (mi < 0) continue;
            if (r.Kind == FormationKind.Cavalry && (r.CavFlags[mi] & CfMelee) != 0) continue;     // a rider who fights on alone is not what the formation is measured by
            tot++;
            var f = w.Fighters[r.Members[mi]];
            SlotWorld(r, s, tick, out int x, out int y);
            long dx = f.X - x, dy = f.Y - y;
            if (dx * dx + dy * dy <= lim) ok++;
        }
        r.Cohesion = tot == 0 ? 0 : ok * 1000 / tot;
    }

    /// <summary>Contact (every 10 ticks per row): any slotted member with an enemy inside the contact range.</summary>
    private void UpdateContact(FightWorld w, FormationRow r, int tick)
    {
        if ((tick + r.Index) % 10 != 0) return;
        r.InContact = false;
        for (int i = 0; i < r.Members.Length && !r.InContact; i++)
        {
            if (r.Slot[i] < 0) continue;
            if (w.AnyEnemyWithin(w.Fighters[r.Members[i]], Rules.R.ContactMm)) r.InContact = true;
        }
    }

    // ------------------------------------------------------------------ the hash and the save

    public ulong Hash(ulong h)
    {
        h = FightMath.Mix(h, Rows.Count); h = FightMath.Mix(h, Hero);
        foreach (var r in Rows)
        {
            h = FightMath.Mix(h, r.Id | (long)r.Side << 8 | (long)r.Shape << 16 | (long)r.Fire << 24 | (long)r.LayoutN << 32);
            h = FightMath.Mix(h, r.AnchorX); h = FightMath.Mix(h, r.AnchorY); h = FightMath.Mix(h, r.FaceX); h = FightMath.Mix(h, r.FaceY);
            h = FightMath.Mix(h, r.Captain); h = FightMath.Mix(h, (long)r.Order.Kind | (long)r.Order.TargetX << 8); h = FightMath.Mix(h, r.Order.TargetY);
            h = FightMath.Mix(h, (long)r.Cohesion | (long)r.QueueCount << 16 | (long)r.PendingCount << 20 | (r.Cascaded ? 1L << 24 : 0));
            for (int i = 0; i < r.Slot.Length; i++) h = FightMath.Mix(h, r.Slot[i]);
            if (r.Kind == FormationKind.Cavalry) h = HashCavalry(h, r);
            if (r.Foot != FootNone) h = FightMath.Mix(h, (long)r.Foot | (long)r.FootTick << 8);          // [P7 CV-B] only while dismounted or mounting: a book that never does hashes as before
        }
        return h;
    }

    private sealed class Reader(long[] v) { private int _i; public long L() => v[_i++]; public int I() => (int)v[_i++]; public int[] Arr() { int n = I(); var a = new int[n]; for (int k = 0; k < n; k++) a[k] = I(); return a; } public ReadOnlySpan<long> Span(int n) { var s = new ReadOnlySpan<long>(v, _i, n); _i += n; return s; } }

    /// <summary>Every integer of the book, in one fixed order (<see cref="Restore"/> reads it back): the hero, the entry points, the lost orders, then each row.</summary>
    public long[] Pack()
    {
        bool cav = false; foreach (var r in Rows) if (r.Kind == FormationKind.Cavalry) { cav = true; break; }
        bool foot = false; foreach (var r in Rows) if (r.Foot != FootNone) { foot = true; break; }
        var o = new List<long> { foot ? 3 : cav ? 2 : 1, Hero, HeroLeadership, _serial, Entry.Count };
        foreach (var (side, e) in Entry) { o.Add(side); o.Add(e.X); o.Add(e.Y); }
        o.Add(Lost.Count);
        foreach (var l in Lost) { o.Add(l.Tick); o.Add(l.Row); o.Add(l.Serial); o.Add(l.Runner); }
        o.Add(Rows.Count);
        foreach (var r in Rows)
        {
            o.Add(r.Id); o.Add(r.Side); o.Add((long)r.Kind); o.Add((long)r.Shape); o.Add(r.Ranks); o.Add(r.LayoutN); o.Add(r.AnchorX); o.Add(r.AnchorY); o.Add(r.FaceX); o.Add(r.FaceY);
            o.Add(r.Captain); o.Add(r.Banner); o.Add(r.Leader); o.Add((long)r.Fire); o.Add(r.FireTargetRow); o.Add(r.Drill); o.Add(r.Micro); o.Add(r.CaptainAlways ? 1 : 0); o.Add(r.StartCount);
            o.Add(r.DeployStart); o.Add(r.DeployTicks); o.Add(r.TurnAcc); o.Add(r.OrderGo); o.Add(r.SlotChanges); o.Add(r.NextDecision); o.Add((r.Cascaded ? 1 : 0) | (r.InContact ? 2 : 0) | (r.WallByCaptain ? 4 : 0));
            o.Add(r.TgtRow); o.Add(r.TgtX); o.Add(r.TgtY); o.Add(r.BrakeMmps); o.Add(r.PaceNow); o.Add(r.Cohesion); o.Add(r.WantFx); o.Add(r.WantFy); o.Add(r.WheelSign);
            r.Order.Pack(o);
            o.Add(r.QueueCount); for (int i = 0; i < 4; i++) r.Queue[i].Pack(o);
            o.Add(r.PendingCount); for (int i = 0; i < 8; i++) r.Pending[i].Pack(o);
            int n = r.Members.Length; o.Add(n);
            for (int i = 0; i < n; i++) { o.Add(r.Members[i]); o.Add(r.Slot[i]); o.Add(r.Leadership[i]); o.Add(r.Pace[i]); o.Add(r.FlankBlow[i]); o.Add(r.VacSince[i]); o.Add(r.Officer[i]); }
        }
        if (cav) foreach (var r in Rows) if (r.Kind == FormationKind.Cavalry) PackCavalry(o, r);       // the cavalry tail: a book with no cavalry row is byte for byte what BB-B wrote
        if (foot) PackFoot(o);                                                                            // [P7 CV-B] format 3: the rows that are dismounted or getting up or down
        return [.. o];
    }

    public static FormationBook Restore(FightWorld w, long[] v)
    {
        var rd = new Reader(v);
        long format = rd.L();
        var book = new FormationBook(FormationRules.For(w.Content.Db)) { Hero = rd.I(), HeroLeadership = rd.I(), _serial = rd.I() };
        int ne = rd.I(); for (int i = 0; i < ne; i++) { int side = rd.I(), x = rd.I(), y = rd.I(); book.Entry[side] = (x, y); }
        int nl = rd.I(); for (int i = 0; i < nl; i++) book.Lost.Add((rd.I(), rd.I(), rd.I(), rd.I()));
        int nr = rd.I();
        for (int k = 0; k < nr; k++)
        {
            var r = new FormationRow { Index = k };
            r.Id = (byte)rd.I(); r.Side = rd.I(); r.Kind = (FormationKind)rd.I(); r.Shape = (Formation)rd.I(); r.Ranks = rd.I(); r.LayoutN = rd.I(); r.AnchorX = rd.I(); r.AnchorY = rd.I(); r.FaceX = rd.I(); r.FaceY = rd.I();
            r.Captain = rd.I(); r.Banner = rd.I(); r.Leader = rd.I(); r.Fire = (FireMode)rd.I(); r.FireTargetRow = rd.I(); r.Drill = rd.I(); r.Micro = rd.I(); r.CaptainAlways = rd.I() != 0; r.StartCount = rd.I();
            r.DeployStart = rd.I(); r.DeployTicks = rd.I(); r.TurnAcc = rd.I(); r.OrderGo = rd.I(); r.SlotChanges = rd.I(); r.NextDecision = rd.I();
            int fl = rd.I(); r.Cascaded = (fl & 1) != 0; r.InContact = (fl & 2) != 0; r.WallByCaptain = (fl & 4) != 0;
            r.TgtRow = rd.I(); r.TgtX = rd.I(); r.TgtY = rd.I(); r.BrakeMmps = rd.I(); r.PaceNow = rd.I(); r.Cohesion = rd.I(); r.WantFx = rd.I(); r.WantFy = rd.I(); r.WheelSign = rd.I();
            r.Order = FormationOrder.Unpack(rd.Span(FormationOrder.PackLen));
            r.QueueCount = rd.I(); for (int i = 0; i < 4; i++) r.Queue[i] = FormationOrder.Unpack(rd.Span(FormationOrder.PackLen));
            r.PendingCount = rd.I(); for (int i = 0; i < 8; i++) r.Pending[i] = FormationOrder.Unpack(rd.Span(FormationOrder.PackLen));
            int n = rd.I();
            r.Members = new int[n]; r.Slot = new int[n]; r.Leadership = new int[n]; r.Pace = new int[n]; r.FlankBlow = new int[n]; r.VacSince = new int[n]; r.Officer = new int[n]; r.Occ = new int[n];
            r.Lat = new int[n]; r.Back = new int[n]; r.ColLat = new int[n]; r.ColBack = new int[n];
            InitCavalry(r, n);
            Array.Fill(r.Occ, -1);
            for (int i = 0; i < n; i++)
            {
                r.Members[i] = rd.I(); r.Slot[i] = rd.I(); r.Leadership[i] = rd.I(); r.Pace[i] = rd.I(); r.FlankBlow[i] = rd.I(); r.VacSince[i] = rd.I(); r.Officer[i] = rd.I();
                if (r.Slot[i] >= 0) r.Occ[r.Slot[i]] = i;
                var f = w.Fighters[r.Members[i]]; f.FmRow = k; f.FmMember = i;
            }
            book.Rows.Add(r);
            book.Relayout(r);
        }
        if (format >= 2) foreach (var r in book.Rows) if (r.Kind == FormationKind.Cavalry) UnpackCavalry(rd, r);
        if (format >= 3) book.UnpackFoot(rd);
        w.Grid.Dirty = true;
        return book;
    }
}
