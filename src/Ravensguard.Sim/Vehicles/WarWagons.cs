using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Naval;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Sim.Carts;

/// <summary>Where a chain between two wagons is (sieges-and-engines 4.8): waiting for both wagons, being hung, up, being cut with an axe, being loosed for a sortie, or open (a gap).</summary>
public enum ChainState : byte { Waiting, Chaining, Chained, Cutting, Unchaining, Open }

/// <summary>One wagon's place on the ring: where it goes and how far it has got (arrived, unhitched, ready to be chained).</summary>
public sealed class WagonSlot
{
    public EntityId Vehicle;
    public long X, Y;
    public byte Facing;
    public bool Arrived, Ready;
    public int UnhitchLeft;
    public int Riding;
}

/// <summary>The chain in the gap after a wagon (between slot i and slot i + 1 round the ring): its place, how far its blocking reaches, its state and the seconds left of what is being done to it.</summary>
public sealed class WagonChain
{
    public long X, Y;
    public int RadiusMm;
    public ChainState State;
    public int Left;
}

/// <summary>
/// P7-SG-E (sieges-and-engines 4.8): a Hussite wagon fort. War wagons (vehicles of the <c>war_wagon</c> template) drive from a column to their slots on a ring (the drivers' Riding is their driving), are unhitched, and are chained at the
/// hooks; the ring is then a field fortification: its boards are <see cref="IFightWalls"/> read from each wagon's part grid (an armour board or a pavise blocks bodies and missiles, a slit board blocks bodies and lets missiles out, a
/// broken board is a gap), a chain blocks bodies where it hangs and is cut in 30 seconds by a man with an axe, loosed for a sortie, or hung again. Horses refuse a fort by its standing cells counted as braced polearms. A fort steps in game seconds
/// (a live tick is one), so the live tier and the abstract hour step the same numbers; nothing here draws a random number.
/// </summary>
public sealed class WagonFort : IFightWalls
{
    public ContentDatabase Content { get; }
    public RideBook Rides { get; }
    public EngineBook Book { get; }
    public WagonRulesDef Rules { get; }
    public List<WagonSlot> Slots { get; private set; } = [];
    public List<WagonChain> Chains { get; private set; } = [];
    public long CenterX { get; private set; }
    public long CenterY { get; private set; }
    public int RadiusMm { get; private set; }
    public long ClockSec { get; private set; }
    public long FormedAtSec { get; private set; } = -1;
    public bool Formed => FormedAtSec >= 0;
    /// <summary>The fight's origin in the world, in mm: a fight's millimetres plus this are the world's (a battle bubble's box has an origin).</summary>
    public long OriginX, OriginY;
    /// <summary>Written once when the ring is chained (a root entry of the siege kind, or the caller's cause).</summary>
    public SiegeChronicle? Chronicle;
    public EntityId Cause;

    public WagonFort(ContentDatabase content, RideBook rides, ulong seed)
    {
        Content = content; Rides = rides; Book = new EngineBook(content, rides, seed);
        Rules = content.All<WagonRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new WagonRulesDef { Id = "wagon_rules" };
    }

    // ------------------------------------------------------------------ the ring

    /// <summary>
    /// Plans a ring round (<paramref name="cx"/>, <paramref name="cy"/>) for these wagons in id order (wagon i takes slot i) and sets them driving: each slot is a point on a circle whose chord is a wagon's length and a gap, the wagon along its
    /// tangent. <paramref name="riding"/> is a wagon's driver's Riding. Returns false when there are fewer wagons than the rules' least or one is not a finished war wagon.
    /// </summary>
    public bool Form(IEnumerable<VehicleRow> wagons, long cx, long cy, long nowSec, Func<EntityId, int>? riding = null)
    {
        var list = wagons.OrderBy(v => v.Id).ToList();
        int n = list.Count;
        if (n < Rules.MinWagons || list.Any(v => !v.Finished || v.Design != Rules.Template)) return false;
        var (_, len) = Book.Size(list[0]);
        long chord = len * 1000L + Rules.GapMm;
        long sinHalf = IntMath.CosSinQ30(1, 2L * n).Sin;                              // sin(pi / n), Q30
        long r = (chord << 30) / (2 * sinHalf);
        CenterX = cx; CenterY = cy; RadiusMm = (int)r; ClockSec = nowSec; FormedAtSec = -1;
        Slots = []; Chains = [];
        for (int i = 0; i < n; i++)
        {
            var (c, s) = IntMath.CosSinQ30(i, n);
            long hq = (((long)i << 24) / n + (1L << 22)) & (FrameMath.Mask);
            Slots.Add(new WagonSlot
            {
                Vehicle = list[i].Id, X = cx + ((r * c + (1L << 29)) >> 30), Y = cy + ((r * s + (1L << 29)) >> 30), Facing = (byte)(hq >> 16), Riding = riding?.Invoke(list[i].Id) ?? 0,
            });
        }
        for (int i = 0; i < n; i++)
        {
            var (fx, fy) = Ends(Slots[i], len, front: true); var (rx, ry) = Ends(Slots[(i + 1) % n], len, front: false);
            long dx = rx - fx, dy = ry - fy;
            Chains.Add(new WagonChain { X = (fx + rx) / 2, Y = (fy + ry) / 2, RadiusMm = (int)(FightMath.Len(dx, dy) / 2 + 500), State = ChainState.Waiting });
        }
        return true;
    }

    private static (long X, long Y) Ends(WagonSlot s, int lenCells, bool front)
    {
        var (cos, sin) = FrameMath.CosSin(s.Facing << 16);
        var (x, y) = FrameMath.ToWorld((int)s.X, (int)s.Y, cos, sin, 0, (front ? 1 : -1) * lenCells * 500);
        return (x, y);
    }

    /// <summary>The seconds a job on a chain takes: the base seconds of the rules' men, over the men put to it (fewer take longer, more help up to double).</summary>
    public static int Seconds(int baseS, int needMen, int men)
    {
        long m = Math.Clamp(men, 1, Math.Max(1, 2 * needMen));
        return (int)((baseS * (long)needMen + m - 1) / m);
    }

    /// <summary>Steps the fort one game second at a time: wagons drive to their slots, are unhitched, and gaps between ready wagons are chained.</summary>
    public void Step(long seconds)
    {
        for (long s = 0; s < seconds; s++) { ClockSec++; StepOne(); }
    }

    private void StepOne()
    {
        foreach (var sl in Slots)
        {
            var v = Rides.Vehicles.Find(sl.Vehicle); if (v == null) continue;
            if (!sl.Arrived)
            {
                long dx = sl.X - v.XMm, dy = sl.Y - v.YMm, dist = FightMath.Len(dx, dy);
                long pace = (long)Rules.DriveMmps * (Rules.DriveBasePm + (long)Rules.DrivePerRidingPm * Math.Clamp(sl.Riding, 0, 150)) / 1000;
                if (dist <= pace || pace <= 0 && dist == 0) { v.XMm = sl.X; v.YMm = sl.Y; v.Facing = sl.Facing; sl.Arrived = true; sl.UnhitchLeft = Rules.UnhitchS; }
                else
                {
                    v.Facing = (byte)(FightMath.DirIndex((int)Math.Clamp(dx, -1_000_000, 1_000_000), (int)Math.Clamp(dy, -1_000_000, 1_000_000)) * 4 & 255);
                    v.XMm += dx * pace / dist; v.YMm += dy * pace / dist;
                }
            }
            else if (!sl.Ready && --sl.UnhitchLeft <= 0) sl.Ready = true;
        }
        for (int i = 0; i < Chains.Count; i++)
        {
            var c = Chains[i];
            switch (c.State)
            {
                case ChainState.Waiting when Slots[i].Ready && Slots[(i + 1) % Slots.Count].Ready: c.State = ChainState.Chaining; c.Left = Seconds(Rules.ChainS, Rules.ChainMen, Rules.ChainMen); break;
                case ChainState.Chaining: if (--c.Left <= 0) c.State = ChainState.Chained; break;
                case ChainState.Cutting or ChainState.Unchaining: if (--c.Left <= 0) c.State = ChainState.Open; break;
            }
        }
        if (!Formed && Chains.Count > 0 && Chains.All(c => c.State == ChainState.Chained))
        {
            FormedAtSec = ClockSec;
            Chronicle?.Invoke(new GameTime(ClockSec), "wagon_fort_formed", "ch_sg_fort_formed", [ChronParam.Count("wagons", Slots.Count), ChronParam.Count("x", CenterX / 1000), ChronParam.Count("y", CenterY / 1000)], [],
                [new Cause(CauseKind.Siege, Cause == default ? null : Cause, "", "ch_note_sg_fort_drilled")], [("x", (CenterX / 1000).ToString()), ("y", (CenterY / 1000).ToString()), ("wagons", Slots.Count.ToString())]);
        }
    }

    // ------------------------------------------------------------------ the chains: cut, sortie, close

    /// <summary>A man with an axe cuts the chain (30 seconds a man, <c>cut_s</c>): it holds the gap shut until the last stroke. False when it is not up.</summary>
    public bool Cut(int gap, int men = 1)
    {
        var c = Chains[gap]; if (c.State != ChainState.Chained) return false;
        c.State = ChainState.Cutting; c.Left = Seconds(Rules.CutS, Rules.CutMen, men); return true;
    }

    /// <summary>The order Sortie: the fort's own men loose a gap's chain (20 seconds with two men) to let a formation out and back.</summary>
    public bool Sortie(int gap, int men = 2)
    {
        var c = Chains[gap]; if (c.State != ChainState.Chained) return false;
        c.State = ChainState.Unchaining; c.Left = Seconds(Rules.ChainS, Rules.ChainMen, men); return true;
    }

    /// <summary>Hangs the chain of a gap again after a sortie or a cut (20 seconds with two men); both hooks must still be on their wagons.</summary>
    public bool Close(int gap, int men = 2)
    {
        var c = Chains[gap]; if (c.State != ChainState.Open) return false;
        if (!HookStands(Slots[gap].Vehicle) || !HookStands(Slots[(gap + 1) % Slots.Count].Vehicle)) return false;
        c.State = ChainState.Chaining; c.Left = Seconds(Rules.ChainS, Rules.ChainMen, men); return true;
    }

    private bool HookStands(EntityId vehicle)
    {
        var v = Rides.Vehicles.Find(vehicle); if (v == null) return false;
        foreach (var p in v.Parts) if (p.State == PartState.Built && Rides.Templates.PartList[p.Part].Tags.Contains("chain_hook")) return true;
        return false;
    }

    /// <summary>Gaps (chain indices) a man can walk through: open chains.</summary>
    public IEnumerable<int> OpenGaps() { for (int i = 0; i < Chains.Count; i++) if (Chains[i].State == ChainState.Open) yield return i; }

    /// <summary>The fort is shut: every chain up.</summary>
    public bool Sealed => Chains.Count > 0 && Chains.All(c => c.State is ChainState.Chained or ChainState.Cutting or ChainState.Unchaining);

    // ------------------------------------------------------------------ boards

    /// <summary>What stands in one cell of a wagon, from its stack of built parts: 1 a board that blocks bodies and missiles, 2 a slit board (bodies only), 3 a hook or beam (bodies only), 0 nothing (a gap).</summary>
    public int CellKind(VehicleRow v, int cx, int cy)
    {
        int kind = 0;
        foreach (int i in Book.Stack(v, cx, cy))
        {
            if (v.Parts[i].State != PartState.Built) continue;
            var tags = Rides.Templates.PartList[v.Parts[i].Part].Tags;
            if (tags.Contains("armour") || tags.Contains("pavise")) return 1;
            if (tags.Contains("slit")) kind = 2;
            else if (kind == 0 && (tags.Contains("chain_hook") || tags.Contains("pole"))) kind = 3;
        }
        return kind;
    }

    /// <summary>Strikes one cell of one wagon (a blow in kg m/s, the one rule: a part takes what is over the threshold and passes on the rest); a broken board is a gap. Returns the parts that broke.</summary>
    public int Strike(int slot, int cx, int cy, long momentumKgMs) => Book.StrikeCell(Rides.Vehicles.Find(Slots[slot].Vehicle)!, cx, cy, momentumKgMs, ClockSec);

    private int Kind(long wx, long wy)
    {
        wx += OriginX; wy += OriginY;
        long dx = wx - CenterX, dy = wy - CenterY;
        if (Slots.Count == 0 || dx * dx + dy * dy > (long)(RadiusMm + 4000) * (RadiusMm + 4000)) return 0;
        int best = 0;
        foreach (var sl in Slots)
        {
            var v = Rides.Vehicles.Find(sl.Vehicle); if (v == null) continue;
            long ex = wx - v.XMm, ey = wy - v.YMm;
            if (ex * ex + ey * ey > 3_500L * 3_500) continue;
            if (Book.CellAt(v, wx, wy) is not { } cell) continue;
            int k = CellKind(v, cell.X, cell.Y);
            if (k == 1) return 1;
            if (k != 0 && (best == 0 || k < best)) best = k;
        }
        return best;
    }

    private bool ChainBlocks(long wx, long wy)
    {
        wx += OriginX; wy += OriginY;
        foreach (var c in Chains)
        {
            if (c.State is not (ChainState.Chained or ChainState.Cutting or ChainState.Unchaining)) continue;
            long dx = wx - c.X, dy = wy - c.Y;
            if (dx * dx + dy * dy <= (long)c.RadiusMm * c.RadiusMm) return true;
        }
        return false;
    }

    public bool BlocksMove(int xMm, int yMm) => Kind(xMm, yMm) != 0 || ChainBlocks(xMm, yMm);
    public bool BlocksMissile(int xMm, int yMm) => Kind(xMm, yMm) == 1;

    /// <summary>A man behind a slit board at this point is covered: the share of missiles it stops for him (permille); 0 where there is no slit.</summary>
    public int SlitCoverPm(int xMm, int yMm) => Kind(xMm, yMm) == 2 ? Rules.SlitCoverPm : 0;

    /// <summary>Whether a point lies inside the ring (within its radius less half a wagon's width).</summary>
    public bool Inside(long wx, long wy) { long dx = wx + OriginX - CenterX, dy = wy + OriginY - CenterY; long r = RadiusMm - 1000; return dx * dx + dy * dy < r * r; }

    // ------------------------------------------------------------------ horses refuse a fort

    /// <summary>The standing boards (armour, pavise, slit) whose cell centres lie within the rules' arc of a point, each counted as a braced polearm, no more than the rules' most.</summary>
    public int BracedAt(long wx, long wy)
    {
        wx += OriginX; wy += OriginY;
        int n = 0; long arc = Rules.ArcMm; long a2 = arc * arc;
        foreach (var sl in Slots)
        {
            var v = Rides.Vehicles.Find(sl.Vehicle); if (v == null) continue;
            long ex = wx - v.XMm, ey = wy - v.YMm; if (ex * ex + ey * ey > (arc + 3_500) * (arc + 3_500)) continue;
            var (w, l) = Book.Size(v); var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
            for (int cy = 0; cy < l; cy++)
                for (int cx = 0; cx < w; cx++)
                {
                    var (px, py) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, cx * 1000 + 500 - w * 500, cy * 1000 + 500 - l * 500);
                    long dx = px - wx, dy = py - wy;
                    if (dx * dx + dy * dy > a2) continue;
                    int k = CellKind(v, cx, cy); if (k is 1 or 2) n++;
                }
        }
        return Math.Min(n, Rules.MaxBraced);
    }

    /// <summary>A horse's refusal of the fort at a point (mounted-combat 4.4, permille): the standing boards in the arc as braced polearms through the one refusal formula.</summary>
    public int RefusalPm(long wx, long wy, int trainedPm, int riding, MountedRefusalDef r, int stormPm = 1000)
        => Combat.Mounted.ChargeMath.RefusalPm(BracedAt(wx, wy), trainedPm, riding, r.BasePm, r.PerBracedPolearmPm, r.TrainedK, r.TrainedRef, r.RidingK, r.MaxPm, stormPm);

    // ------------------------------------------------------------------ saved with the battle that holds it (integers only; the vehicles are the ride book's)

    /// <summary>Every integer of the fort in a fixed order: clock, formed, centre, radius, then each slot and each chain.</summary>
    public long[] Pack()
    {
        var l = new List<long> { ClockSec, FormedAtSec, CenterX, CenterY, RadiusMm, Slots.Count };
        foreach (var s in Slots) l.AddRange([(long)s.Vehicle.Value, s.X, s.Y, s.Facing, s.Arrived ? 1 : 0, s.Ready ? 1 : 0, s.UnhitchLeft, s.Riding]);
        foreach (var c in Chains) l.AddRange([c.X, c.Y, c.RadiusMm, (long)c.State, c.Left]);
        l.AddRange([OriginX, OriginY, (long)Cause.Value]);                                   // appended (P7R-22): older saves end before these
        return [.. l];
    }

    public void Unpack(long[] d)
    {
        int k = 0; ClockSec = d[k++]; FormedAtSec = d[k++]; CenterX = d[k++]; CenterY = d[k++]; RadiusMm = (int)d[k++]; int n = (int)d[k++];
        Slots = []; Chains = [];
        for (int i = 0; i < n; i++)
            Slots.Add(new WagonSlot { Vehicle = new EntityId((ulong)d[k++]), X = d[k++], Y = d[k++], Facing = (byte)d[k++], Arrived = d[k++] != 0, Ready = d[k++] != 0, UnhitchLeft = (int)d[k++], Riding = (int)d[k++] });
        for (int i = 0; i < n; i++)
            Chains.Add(new WagonChain { X = d[k++], Y = d[k++], RadiusMm = (int)d[k++], State = (ChainState)d[k++], Left = (int)d[k++] });
        if (k + 3 <= d.Length) { OriginX = d[k++]; OriginY = d[k++]; Cause = new EntityId((ulong)d[k++]); }
    }
}
