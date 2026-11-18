using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Live;

/// <summary>
/// The constants that stay constant (the day's timetable itself is content: <c>behaviour/schedules.jsonc</c>, see <see cref="Ravensguard.Sim.Behaviour.ScheduleBook"/>): a commute at 5 real m/s
/// (0.25 m per game second, decision A2). Seconds of the day, millimetres, millimetres per game second.
/// </summary>
public static class ScheduleTuning
{
    public const int Hour = 3600, Day = 86400;
    public const long CommuteMmPerGs = 250;
    /// <summary>Beyond this the worker lives at the site (a field camp, abstract spec 3.4).</summary>
    public const long MaxCommuteMm = 1_350_000;
    /// <summary>The least a walk between two places takes, game seconds (a person does not blink across a room).</summary>
    public const int MinWalkSec = 30;
}

/// <summary>Where a block is: the home lot, the bed in it (the home spot when the household has no placed bed), the workplace, the square, the church, a market stall's customer side.</summary>
public enum PlaceRef : byte { Home = 0, Work = 1, Square = 2, Bed = 3, Church = 4, Market = 5 }

/// <summary>
/// Everything the schedule needs about one person, copied out of the records (the person's row, their home and work lots, the settlement square): the cache that a
/// <see cref="LiveBinding"/> keeps next to the record. Positions are millimetres of the person's region frame. Built at a barrier by <see cref="From"/>.
/// </summary>
public readonly struct PersonAnchors
{
    public EntityId Id { get; init; }
    public int Region { get; init; }
    public int Index { get; init; }
    public ulong Seed { get; init; }
    public Occupation Occupation { get; init; }
    public Sex Sex { get; init; }
    public SocialClass Class { get; init; }
    public string Name { get; init; }
    public bool Child { get; init; }
    public bool HasWork { get; init; }
    public bool WorkInField { get; init; }
    public bool HasSquare { get; init; }
    public long HomeX { get; init; }
    public long HomeY { get; init; }
    public int HomeJit { get; init; }
    public int HomeJitY { get; init; }
    public byte HomeFacing { get; init; }
    public int HomeSettlement { get; init; }
    public int HomeLot { get; init; }
    public long WorkX { get; init; }
    public long WorkY { get; init; }
    public int WorkJit { get; init; }
    public int WorkJitY { get; init; }
    public byte WorkFacing { get; init; }
    public int WorkSettlement { get; init; }
    public int WorkLot { get; init; }
    public long SquareX { get; init; }
    public long SquareY { get; init; }
    public int SquareJit { get; init; }
    public int SquareJitY { get; init; }
    /// <summary>Latitude in tenths of a degree (the sun's day); the plan this person follows and the tuning it reads.</summary>
    public int LatTenths { get; init; } = 480;
    public Ravensguard.Sim.Behaviour.ScheduleBook? Book { get; init; }
    /// <summary>The bed (millimetres, level) and whether the household has a placed one.</summary>
    public bool HasBed { get; init; }
    public long BedX { get; init; }
    public long BedY { get; init; }
    public int BedZ { get; init; }
    public bool HasChurch { get; init; }
    public long ChurchX { get; init; }
    public long ChurchY { get; init; }
    public int ChurchJit { get; init; }
    public int ChurchJitY { get; init; }
    /// <summary>The stalls of the home settlement's square (null for a green), and the queue gap.</summary>
    public Ravensguard.Sim.WorldGen.Chunks.ChunkGenerator.StallSpot[]? Stalls { get; init; }
    public int QueueGapMm { get; init; } = 900;
    /// <summary>Parcel tiles (region tile x, y) of the work settlement, for a field worker (the day's tile is picked from them).</summary>
    public (int X, int Y)[]? FieldTiles { get; init; }

    /// <summary>The anchors of person <paramref name="index"/> (alive) at game second <paramref name="nowSec"/>.</summary>
    public static PersonAnchors From(Population pop, int index, ulong seed, int regionId, long nowSec, Dictionary<int, (int, int)[]?>? tileCache = null, Ravensguard.Sim.Behaviour.LiveFacts? facts = null, Ravensguard.Sim.Behaviour.ScheduleBook? book = null)
    {
        var p = pop.People; var r = pop.Region;
        int hs = p.HomeSettlement[index];
        long hx = 0, hy = 0; int hj = 1000, hjy = 1000; byte hf = 2; bool hasHome = false;
        if (hs >= 0 && hs < r.Settlements.Count)
        {
            var plan = r.Settlements[hs].Plan; int hl = p.HomeLot[index];
            if (hl >= 0 && hl < plan.Lots.Count) { LotSpot(plan.Lots[hl], out hx, out hy, out hj, out hjy, out hf); hasHome = true; }
            else { hx = (long)r.Settlements[hs].CenterX * 1000; hy = (long)r.Settlements[hs].CenterY * 1000; hasHome = true; }
        }
        if (!hasHome) { hx = (long)r.Settlements.Count > 0 ? (long)r.Settlements[0].CenterX * 1000 : 0; hy = r.Settlements.Count > 0 ? (long)r.Settlements[0].CenterY * 1000 : 0; }
        int ws = p.WorkSettlement[index] >= 0 ? p.WorkSettlement[index] : hs, wl = p.WorkLot[index];
        var occ = p.Occupation[index];
        int age = p.AgeYears(index, (int)(nowSec / ScheduleTuning.Day));
        bool child = occ == Occupation.None || age < OccupationInfo.MinWorkAge(occ);
        bool hasWork = !child && ws >= 0 && ws < r.Settlements.Count;
        long wx = hx, wy = hy; int wj = hj, wjy = hjy; byte wf = hf; bool field = false; (int, int)[]? tiles = null;
        long sx = hx, sy = hy; int sj = 3000, sjy = 3000; bool hasSq = false; RectM sq0 = default;
        if (hs >= 0 && hs < r.Settlements.Count) { var sq = r.Settlements[hs].Plan.Square; sq0 = sq; if (sq.W > 0) { sx = (long)sq.CenterX * 1000; sy = (long)sq.CenterY * 1000; sj = Math.Max(1500, (sq.W - 2) * 1000 / 2); sjy = Math.Max(1500, (sq.H - 2) * 1000 / 2); hasSq = true; } }
        if (hasWork)
        {
            var wset = r.Settlements[ws]; var wplan = wset.Plan;
            if (wl >= 0 && wl < wplan.Lots.Count) LotSpot(wplan.Lots[wl], out wx, out wy, out wj, out wjy, out wf);
            else if (IsFieldWork(occ) && wset.Parcels.Count > 0)
            {
                field = true; wj = wjy = 20_000;
                if (tileCache == null || !tileCache.TryGetValue(ws, out tiles))
                {
                    var list = new (int, int)[wset.Parcels.Count];
                    for (int k = 0; k < list.Length; k++) { var pc = r.Parcels[wset.Parcels[k]]; list[k] = (pc.TileX, pc.TileY); }
                    tiles = list; if (tileCache != null) tileCache[ws] = list;
                }
            }
            else if (!(occ is Occupation.Merchant or Occupation.Reeve or Occupation.Mayor or Occupation.GuildMaster or Occupation.Steward or Occupation.Guard)) { /* a spinner, a servant of a household, a town's day labourer: works about the house */ }
            else if (wplan.Square.W > 0) { wx = (long)wplan.Square.CenterX * 1000; wy = (long)wplan.Square.CenterY * 1000; wj = Math.Max(3000, (wplan.Square.W - 2) * 1000 / 2); wjy = Math.Max(3000, (wplan.Square.H - 2) * 1000 / 2); wf = 2; }
        }
        // the bed: a real cell of the dwelling (its upper floor too) when the generator's building is at hand
        bool hasBed = false; long bedx = 0, bedy = 0; int bedz = 0;
        if (facts != null && hs >= 0 && hs < r.Settlements.Count && p.HomeLot[index] >= 0 && facts.TryBed(hs, p.HomeLot[index], p.Bed[index], p.IdOf(index).Value, seed, out var bc)) { hasBed = true; bedx = bc.X * 1000L + 500; bedy = bc.Y * 1000L + 500; bedz = bc.Z; }
        bool hasChurch = false; long cx = 0, cy = 0; int cj = 1000, cjy = 1000;
        if (hs >= 0 && hs < r.Settlements.Count && r.Settlements[hs].Plan.ChurchLot is >= 0 and var chl && chl < r.Settlements[hs].Plan.Lots.Count)
        { LotSpot(r.Settlements[hs].Plan.Lots[chl], out cx, out cy, out cj, out cjy, out _); hasChurch = true; }
        else if (sq0.W > 0) { cx = sx; cy = sy; cj = sj; cjy = sjy; hasChurch = true; }
        int lat = Ravensguard.Sim.Behaviour.SunCache.LatitudeOfRow(r.CellY);
        var book0 = book ?? Ravensguard.Sim.Behaviour.ScheduleBook.Default;
        return new PersonAnchors
        {
            Id = p.IdOf(index), Region = regionId, Index = index, Seed = seed, Occupation = occ, Sex = p.Sex[index], Class = p.Class[index], Name = p.FullName(index), Child = child,
            HasWork = hasWork, WorkInField = field, HasSquare = hasSq, HomeX = hx, HomeY = hy, HomeJit = hj, HomeJitY = hjy, HomeFacing = hf, HomeSettlement = hs, HomeLot = p.HomeLot[index],
            WorkX = wx, WorkY = wy, WorkJit = wj, WorkJitY = wjy, WorkFacing = wf, WorkSettlement = ws, WorkLot = wl, SquareX = sx, SquareY = sy, SquareJit = sj, SquareJitY = sjy,
            FieldTiles = tiles, LatTenths = lat, Book = book0, HasBed = hasBed, BedX = bedx, BedY = bedy, BedZ = bedz, HasChurch = hasChurch, ChurchX = cx, ChurchY = cy, ChurchJit = cj, ChurchJitY = cjy,
            Stalls = hs >= 0 && facts != null ? facts.StallsOf(hs) is { Length: > 0 } sp ? sp : null : null, QueueGapMm = book0.Content.Crowd.QueueGapMm,
        };
    }

    private static void LotSpot(Lot l, out long x, out long y, out int jit, out int jitY, out byte facing)
    {
        // people are spread over the lot's floor (its rect less a metre of wall), not stacked on its centre
        x = (long)l.Rect.CenterX * 1000; y = (long)l.Rect.CenterY * 1000; jit = Math.Max(500, (l.Rect.W - 2) * 1000 / 2); jitY = Math.Max(500, (l.Rect.H - 2) * 1000 / 2); facing = (byte)(l.Facing & 3);
    }

    private static bool IsFieldWork(Occupation o) => o is Occupation.Farmer or Occupation.Ploughman or Occupation.Shepherd or Occupation.Labourer or Occupation.Woodcutter
        or Occupation.Fisher or Occupation.Miner or Occupation.CharcoalBurner;

    /// <summary>The spot of <paramref name="place"/> on game day <paramref name="day"/>: the place's anchor plus a jitter drawn from stream 0x2A0 keyed on the day (so one day always gives the same spot).</summary>
    public (long X, long Y) Spot(PlaceRef place, long day)
    {
        long bx, by; int jit, jitY; uint draw = (uint)place * 4;
        switch (place)
        {
            case PlaceRef.Home: bx = HomeX; by = HomeY; jit = HomeJit; jitY = HomeJitY; break;
            case PlaceRef.Square: bx = SquareX; by = SquareY; jit = SquareJit; jitY = SquareJitY; break;
            default:
                bx = WorkX; by = WorkY; jit = WorkJit; jitY = WorkJitY;
                if (WorkInField && FieldTiles is { Length: > 0 } t)
                {
                    var (tx, ty) = t[DetRng.Range(Seed, day, Id.Value, RngStreams.ScheduleJitter, 15, 0, t.Length)];
                    bx = ((long)tx * RegionSettings.TileM + RegionSettings.TileM / 2) * 1000; by = ((long)ty * RegionSettings.TileM + RegionSettings.TileM / 2) * 1000;
                }
                break;
        }
        long jx = DetRng.Range(Seed, day, Id.Value, RngStreams.ScheduleJitter, draw, -jit, jit + 1), jy = DetRng.Range(Seed, day, Id.Value, RngStreams.ScheduleJitter, draw + 1, -jitY, jitY + 1);
        return (bx + jx, by + jy);
    }

    public (int X, int Y) FacingOf(PlaceRef place)
    {
        int f = place == PlaceRef.Work ? WorkFacing : place == PlaceRef.Square ? 2 : HomeFacing;
        return (f & 3) switch { 0 => (0, -1024), 1 => (1024, 0), 2 => (0, 1024), _ => (-1024, 0) };
    }
}

internal ref struct SegBuilder(Span<DaySeg> buf)
{
    private readonly Span<DaySeg> _buf = buf;
    public int Count { get; private set; }
    public void Add(int s, int e, JobKind k, PlaceRef f, PlaceRef t)
    {
        s = Math.Clamp(s, 0, ScheduleTuning.Day); e = Math.Clamp(e, 0, ScheduleTuning.Day);
        if (e > s && Count < _buf.Length) _buf[Count++] = new DaySeg(s, e, k, f, t);
    }
}

/// <summary>One block of a person's day: seconds of the day, what they do, where they come from and go to (the same place unless it is a commute).</summary>
public readonly record struct DaySeg(int Start, int End, JobKind Kind, PlaceRef From, PlaceRef To);

/// <summary>A person's state at one game second: the abstract job, where they stand (region millimetres, heading x1024) and the block's walk endpoints.</summary>
public readonly record struct ResolvedJob(AbstractJob Job, long XMm, long YMm, int HeadX, int HeadY, int Segment, long Day, DaySeg Seg, long FromX, long FromY, long ToX, long ToY);

/// <summary>Abstract spec 3.4: <c>PositionOf(person, t)</c> as a pure function of the person's anchors and the game second.</summary>
public static class Schedule
{
    public const int MaxSegs = 16;

    private static long Secs(long distMm) => (distMm + ScheduleTuning.CommuteMmPerGs - 1) / ScheduleTuning.CommuteMmPerGs;

    /// <summary>The day's blocks of the person (seconds of the day, contiguous from 0 to 86,400). Returns how many.</summary>
    public static int Segments(in PersonAnchors a, long day, Span<DaySeg> buf)
    {
        int D = ScheduleTuning.Day;
        var b = new SegBuilder(buf);
        var home = a.Spot(PlaceRef.Home, day);
        if (!a.HasWork)
        {
            int wake = a.Child ? ScheduleTuning.ChildWake : ScheduleTuning.WorkStart - 3600;
            b.Add(0, wake, JobKind.Sleep, PlaceRef.Home, PlaceRef.Home);
            b.Add(wake, ScheduleTuning.ChildIdleFrom, JobKind.Home, PlaceRef.Home, PlaceRef.Home);
            b.Add(ScheduleTuning.ChildIdleFrom, ScheduleTuning.ChildIdleTo, JobKind.Idle, PlaceRef.Home, PlaceRef.Home);
            b.Add(ScheduleTuning.ChildIdleTo, ScheduleTuning.SleepStart, JobKind.Home, PlaceRef.Home, PlaceRef.Home);
            b.Add(ScheduleTuning.SleepStart, D, JobKind.Sleep, PlaceRef.Home, PlaceRef.Home);
            return b.Count;
        }
        int ws = a.WorkStartSod, we = a.WorkEndSod, wakeAt = ws - ScheduleTuning.WakeBeforeWork;
        var work = a.Spot(PlaceRef.Work, day);
        long dist = PathPlan.Dist(work.X - home.X, work.Y - home.Y);
        if (dist > ScheduleTuning.MaxCommuteMm)   // a field camp: the worker stays at the site, sleeping there
        {
            int bed = Math.Min(D, Math.Max(ScheduleTuning.SleepStart, we + ScheduleTuning.RestBeforeSleep));
            b.Add(0, wakeAt, JobKind.Sleep, PlaceRef.Work, PlaceRef.Work);
            b.Add(wakeAt, bed, JobKind.Work, PlaceRef.Work, PlaceRef.Work);
            b.Add(bed, D, JobKind.Sleep, PlaceRef.Work, PlaceRef.Work);
            return b.Count;
        }
        int t = (int)Secs(dist);
        int sleep = Math.Min(D, Math.Max(ScheduleTuning.SleepStart, we + t + ScheduleTuning.RestBeforeSleep));
        b.Add(0, wakeAt, JobKind.Sleep, PlaceRef.Home, PlaceRef.Home);
        b.Add(wakeAt, ws - t, JobKind.Home, PlaceRef.Home, PlaceRef.Home);
        if (t > 0) b.Add(ws - t, ws, JobKind.Commute, PlaceRef.Home, PlaceRef.Work);
        b.Add(ws, we, JobKind.Work, PlaceRef.Work, PlaceRef.Work);
        int ret = we;
        if (t > 0) { b.Add(we, we + t, JobKind.Commute, PlaceRef.Work, PlaceRef.Home); ret = we + t; }
        bool shops = a.HasSquare && DetRng.Range(a.Seed, day, a.Id.Value, RngStreams.ScheduleJitter, 14, 0, 3) == 0;
        if (shops)
        {
            var sq = a.Spot(PlaceRef.Square, day);
            int ts = (int)Secs(PathPlan.Dist(sq.X - home.X, sq.Y - home.Y));
            if (ret <= ScheduleTuning.ShopStart - ts && ScheduleTuning.ShopEnd + ts + ScheduleTuning.RestBeforeSleep <= sleep)
            {
                b.Add(ret, ScheduleTuning.ShopStart - ts, JobKind.Home, PlaceRef.Home, PlaceRef.Home);
                if (ts > 0) b.Add(ScheduleTuning.ShopStart - ts, ScheduleTuning.ShopStart, JobKind.Commute, PlaceRef.Home, PlaceRef.Square);
                b.Add(ScheduleTuning.ShopStart, ScheduleTuning.ShopEnd, JobKind.Shop, PlaceRef.Square, PlaceRef.Square);
                if (ts > 0) b.Add(ScheduleTuning.ShopEnd, ScheduleTuning.ShopEnd + ts, JobKind.Commute, PlaceRef.Square, PlaceRef.Home);
                ret = ScheduleTuning.ShopEnd + ts;
            }
        }
        b.Add(ret, sleep, JobKind.Home, PlaceRef.Home, PlaceRef.Home);
        b.Add(sleep, D, JobKind.Sleep, PlaceRef.Home, PlaceRef.Home);
        return b.Count;
    }

    /// <summary>The person's job and position at game second <paramref name="sec"/>. A pure function.</summary>
    public static ResolvedJob Resolve(in PersonAnchors a, long sec)
    {
        long day = sec / ScheduleTuning.Day; int sod = (int)(sec % ScheduleTuning.Day);
        Span<DaySeg> segs = stackalloc DaySeg[MaxSegs];
        int n = Segments(a, day, segs), k = 0;
        while (k < n - 1 && sod >= segs[k].End) k++;
        var s = segs[k];
        var from = a.Spot(s.From, day); var to = a.Spot(s.To, day);
        long segStart = day * ScheduleTuning.Day + s.Start, segEnd = day * ScheduleTuning.Day + s.End;
        long x, y; int hx, hy; int ppm = (int)Math.Clamp((sec - segStart) * 1_000_000 / Math.Max(1, segEnd - segStart), 0, 1_000_000);
        if (s.Kind == JobKind.Commute)
        {
            x = from.X + (to.X - from.X) * ppm / 1_000_000; y = from.Y + (to.Y - from.Y) * ppm / 1_000_000;
            long dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Max(1, PathPlan.Dist(dx, dy)); hx = (int)(dx * 1024 / len); hy = (int)(dy * 1024 / len);
        }
        else { x = to.X; y = to.Y; (hx, hy) = a.FacingOf(s.To); }
        var place = s.To == PlaceRef.Home ? new JobPlace(a.HomeSettlement, a.HomeLot, (int)(to.X / 1000), (int)(to.Y / 1000))
            : s.To == PlaceRef.Work ? new JobPlace(a.WorkSettlement, a.WorkLot, (int)(to.X / 1000), (int)(to.Y / 1000)) : new JobPlace(a.HomeSettlement, -1, (int)(to.X / 1000), (int)(to.Y / 1000));
        return new ResolvedJob(new AbstractJob(s.Kind, place, segStart, segEnd, ppm), x, y, hx, hy, k, day, s, from.X, from.Y, to.X, to.Y);
    }
}
