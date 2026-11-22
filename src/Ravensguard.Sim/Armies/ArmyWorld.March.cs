using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>A host's road as one polyline over the region's road graph (4.4): the stops' places along it, the path itself in continent metres. Built from the region on demand, never saved.</summary>
public sealed class HostRoute
{
    public CaravanPath Path = null!;
    public long[] StopMm = [];
    public int[] Stops = [];
    public long Length => Path.LengthMm;
}

public sealed partial class ArmyWorld
{
    /// <summary>The wet ground at a game hour and a place (continent millimetres): the rain of the day, the snow lying, frost and thaw (the weather wiring sets it; null: dry).</summary>
    public Func<long, long, long, WetState>? Wetness { get; set; }

    // the host supply is AM-B's `Supply` (ArmyWorld.Muster.cs), which also binds the levy's evasion

    /// <summary>Wall time of the marching hosts' hour (not saved): read by the budget tests.</summary>
    public TimingCell MarchCost { get; } = new();

    private readonly Dictionary<int, HostRoute?> _routes = [];
    private readonly Dictionary<(int Host, int Serial, int Men), CampPlan> _plans = [];

    // ------------------------------------------------------------------ the road

    private CaravanPath? Leg(int a, int b)
    {
        if (a >= 0 && b >= 0) return PathOf(a, b);
        if (a >= 0) return CaravanRoads.Build(Region, a, -1 - b, false);
        if (b >= 0) { var p = CaravanRoads.Build(Region, b, -1 - a, false); return p == null ? null : Reverse(p); }
        return null;
    }

    private static CaravanPath Reverse(CaravanPath p)
    {
        var pts = new List<(int X, int Y, RoadClass Class)>();
        int n = p.X.Length;
        for (int j = 0; j < n; j++) pts.Add((p.X[n - 1 - j], p.Y[n - 1 - j], j == 0 ? RoadClass.Road : (RoadClass)p.Class[n - 1 - j]));
        return CaravanPath.FromPoints(pts);
    }

    /// <summary>The route over a list of stops (a settlement is its index, a gate of the region <c>-1 - gate</c>): the legs joined into one path, or null when two stops are not joined by road.</summary>
    public HostRoute? BuildRoute(IReadOnlyList<int> stops)
    {
        if (stops.Count < 2) return null;
        var pts = new List<(int X, int Y, RoadClass Class)>(); var at = new long[stops.Count];
        for (int i = 1; i < stops.Count; i++)
        {
            var leg = Leg(stops[i - 1], stops[i]); if (leg == null || leg.X.Length < 2) return null;
            for (int k = 0; k < leg.X.Length; k++)
            {
                if (pts.Count > 0 && k == 0 && pts[^1].X == leg.X[0] && pts[^1].Y == leg.Y[0]) continue;
                pts.Add((leg.X[k], leg.Y[k], k == 0 ? RoadClass.Road : (RoadClass)leg.Class[k - 1]));
            }
            at[i] = 0;
            var tmp = CaravanPath.FromPoints(pts); at[i] = tmp.LengthMm;
        }
        return new HostRoute { Path = CaravanPath.FromPoints(pts), StopMm = at, Stops = [.. stops] };
    }

    public HostRoute? RouteOf(HostMarch m)
    {
        if (_routes.TryGetValue(m.Host, out var r) && r != null && r.Stops.AsSpan().SequenceEqual(m.Stops.ToArray())) return r;
        r = BuildRoute(m.Stops);
        if (_routes.Count > 256) _routes.Clear();
        return _routes[m.Host] = r;
    }

    /// <summary>Where the head of a host's column is at a game second, continent millimetres, with its heading: a pure function of the march record.</summary>
    public (long XMm, long YMm, int HeadX, int HeadY)? HeadAt(HostMarch m, long sec)
    {
        var r = RouteOf(m); if (r == null) return null;
        return r.Path.At(Math.Clamp(HostMotion.DistAt(m, sec), 0, r.Length));
    }

    /// <summary>The settlement whose centre is nearest a continent point (ties the lower index), and the distance to it in metres.</summary>
    public (int Settlement, int DistM) NearestTo(long xMm, long yMm)
    {
        long x = (xMm - OriginXMm) / 1000, y = (yMm - OriginYMm) / 1000; int best = -1; long bd = long.MaxValue;
        for (int s = 0; s < Region.Settlements.Count; s++)
        {
            long dx = Region.Settlements[s].CenterX - x, dy = Region.Settlements[s].CenterY - y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = s; }
        }
        return (best, (int)CaravanPath.Isqrt(Math.Max(0, bd)));
    }

    // ------------------------------------------------------------------ starting and stopping a march

    /// <summary>
    /// A host sets out along a list of stops (the first where it is now, the last where it is going; a gate is <c>-1 - gate</c>). The route is the region's road graph, the same one the caravans and warbands walk. A host
    /// that starts at a gate comes into the region (its men from outside are made persons); one that ends at a gate leaves it on arrival. Returns the record, or null when the host is unknown or the roads do not join the stops.
    /// </summary>
    public HostMarch? BeginMarch(ArmyEnv e, HostId host, IReadOnlyList<int> stops, string cause = "ch_note_am_march")
    {
        var hs = Supply; if (hs == null || !hs.Exists(host)) return null;
        var route = BuildRoute(stops); if (route == null) return null;
        var m = Book.Marches.TryGetValue(host.Value, out var old) ? old : new HostMarch { Host = host.Value };     // a host already in the region (its men made persons) keeps them
        m.Stops.Clear(); m.Stops.AddRange(stops);
        m.StartDay = (int)e.Day; m.Cause = cause; m.State = MarchState.Marching; m.Camp = null; m.Hold = false; m.DistMm = 0; m.RateHourMm = 0; m.HourSec = long.MinValue; m.PrevDistMm = 0; m.PrevRateHourMm = 0; m.PrevHourSec = long.MinValue;
        m.Hours = 0; m.MarchHours = 0; m.CampsPitched = 0; m.ChronEntry = 0;
        Book.Marches[m.Host] = m; _routes[m.Host] = route;
        if (stops[0] < 0) EnterRegion(e, host, stops[0]);
        PlaceHost(m, hs, route, 0);
        return m;
    }

    /// <summary>The host stops marching and its record is dropped (it stands where its account says).</summary>
    public void EndMarch(HostId host) { Book.Marches.Remove(host.Value); _routes.Remove(host.Value); }

    /// <summary>A host halts where it stands and stays camped through the days (a stay: the ring of the camp goes up after two nights); <paramref name="on"/> false lets it march on at dawn.</summary>
    public void Halt(HostId host, bool on) { if (Book.Marches.TryGetValue(host.Value, out var m)) m.Hold = on; }

    // ------------------------------------------------------------------ the hour

    private static bool DayHour(int hod) => hod >= MarchConfig.DayStartHour && hod < MarchConfig.DayEndHour;

    /// <summary>
    /// One hour of the hosts on the road (H1, after the warbands): each march in the book's order walks its hour (05:00 to 21:00 at the column's pace, slowed by the weather and by the baggage's pace on bad ground), pitches
    /// its camp at 21:00 and strikes it at 05:00, counts its nights, and tells the supply book where the host stands. The march is a record of distances: the live column reads the same record.
    /// </summary>
    public void MarchHour(ArmyEnv e)
    {
        var hs = Supply; if (hs == null || Book.Marches.Count == 0) return;
        foreach (var m in Book.Marches.Values.ToList())
        {
            if (!hs.Exists(new HostId(m.Host))) { Book.Marches.Remove(m.Host); _routes.Remove(m.Host); continue; }
            StepMarch(e, hs, m);
        }
    }

    private void StepMarch(ArmyEnv e, HostSupply hs, HostMarch m)
    {
        var route = RouteOf(m); if (route == null) { m.State = MarchState.Arrived; return; }
        var a = hs.Account(new HostId(m.Host));
        long hour = e.Hour, sec = hour * 3600; int hod = e.HourOfDay;
        // the distance at the start of this hour: the last record walked on, for as long as it walked
        long dist = m.DistMm;
        if (m.HourSec != long.MinValue)
        {
            long walked = Math.Clamp(sec - m.HourSec, 0, 3600);
            dist = m.DistMm + m.RateHourMm * walked / 3600;
            m.PrevDistMm = m.DistMm; m.PrevRateHourMm = m.RateHourMm; m.PrevHourSec = m.HourSec;
        }
        dist = Math.Min(dist, route.Length);
        m.DistMm = dist; m.HourSec = sec; m.RateHourMm = 0;
        m.Hours++;
        if (e.HourOfDay == MarchConfig.DayStartHour) SyncDead(hs, m);
        bool atEnd = dist >= route.Length;
        if (m.State is MarchState.Arrived or MarchState.Left) { NightsAndWatch(e, m, a, hod); PlaceHost(m, hs, route, sec); return; }
        if (m.State == MarchState.Marching && atEnd) { Arrive(e, hs, m, a, route); return; }
        switch (m.State)
        {
            case MarchState.Camped:
                if (hod == MarchConfig.DayStartHour && !m.Hold) { m.State = MarchState.Marching; m.Camp = null; goto case MarchState.Marching; }
                NightsAndWatch(e, m, a, hod);
                if (m.Hold && m.Camp != null && m.Camp.Nights >= MarchConfig.PalisadeAfterNights) m.Camp.Palisade = true;
                break;
            case MarchState.Marching:
                if (m.Hold || !DayHour(hod))
                {
                    m.State = MarchState.Camped; PitchCamp(e, m, a, route, dist, sec);
                    NightsAndWatch(e, m, a, hod);
                }
                else
                {
                    m.MarchHours++;
                    m.RateHourMm = RateOf(m, a, route, dist, hour);
                }
                break;
        }
        PlaceHost(m, hs, route, sec);
    }

    /// <summary>What the column walks in the hour that begins at <paramref name="hour"/>: 65 mm a game second on the road class, at the weather's pace for men on foot and, with wagons, no faster than the baggage can go on that ground.</summary>
    private long RateOf(HostMarch m, HostAccount a, HostRoute route, long dist, long hour)
    {
        int cls = route.Path.SpeedPermille(dist);
        var wet = WetnessAt(hour, route, dist);
        var surf = SurfaceModel.Classify(Ground.Road, wet);
        int col = SurfaceModel.PersonSpeedPm(surf), bag = a.Carts > 0 ? MarchConfig.BaggagePm(surf) : 1000;
        return MarchConfig.ColumnMmPerGs * 3600L * cls / 1000 * Math.Min(col, bag) / 1000;
    }

    private WetState WetnessAt(long hour, HostRoute route, long dist)
    {
        if (Wetness == null) return WetState.Dry;
        var p = route.Path.At(Math.Clamp(dist, 0, route.Length));
        return Wetness(hour, p.XMm, p.YMm);
    }

    private void NightsAndWatch(ArmyEnv e, HostMarch m, HostAccount a, int hod)
    {
        var c = m.Camp; if (c == null) return;
        if (hod == MarchConfig.DayEndHour) c.Nights++;
        if (!DayHour(hod)) { c.Watch = CampPlan.WatchOf(hod); c.Sentries = CampPlan.SentryCount(c.Men, Math.Max(0, c.Nights - 1), c.Watch); }
        else c.Sentries = 0;
        if (c.Nights >= MarchConfig.PalisadeAfterNights && (m.Hold || m.State == MarchState.Arrived) && !c.Palisade)
        {
            c.Palisade = true;
            var plan = PlanOf(m, c, a);
            c.Pieces = plan.Pieces.Length + plan.Ring.Length;
        }
    }

    private void PitchCamp(ArmyEnv e, HostMarch m, HostAccount a, HostRoute route, long dist, long sec)
    {
        var p = route.Path.At(Math.Clamp(dist, 0, route.Length));
        var (near, nearM) = NearestTo(p.XMm, p.YMm);
        var c = new CampRecord { XMm = p.XMm, YMm = p.YMm, Near = near, HeadX = p.HeadX, HeadY = p.HeadY, SinceSec = sec, Serial = ++m.CampsPitched, Men = a.Total, Nights = 0 };
        c.WaterCarried = nearM > MarchConfig.WaterReachM;
        bool belowCamp = false;
        foreach (var (id, o) in Book.Marches) if (id != m.Host && o.Camp is { } oc && Dist2(oc.XMm, oc.YMm, c.XMm, c.YMm) <= Sq(MarchConfig.BelowTownM * 1000L)) belowCamp = true;
        c.BelowTown = nearM <= MarchConfig.BelowTownM || belowCamp;
        m.Camp = c;
        var plan = PlanOf(m, c, a);
        c.Latrine = plan.Latrines > 0; c.Pieces = plan.Pieces.Length;
        int hod = (int)(sec / 3600 % 24);
        c.Nights = !DayHour(hod) && hod != MarchConfig.DayEndHour ? 1 : 0;                  // the night's count rises at 21:00 (a camp pitched later in the night is already in its first)
    }

    private void Arrive(ArmyEnv e, HostSupply hs, HostMarch m, HostAccount a, HostRoute route)
    {
        int last = m.Stops[^1];
        var p = route.Path.At(route.Length);
        var (near, _) = NearestTo(p.XMm, p.YMm);
        int place = last >= 0 ? last : near;
        EntityId cause = a.Campaign != 0 ? new EntityId(a.Campaign) : EntityId.None;
        if (last < 0)
        {
            m.State = MarchState.Left;
            LeaveRegion(e, new HostId(m.Host));
            hs.ToAbstract(new HostId(m.Host));
            m.ChronEntry = Chron(e, "host_crossed", "ch_am_crossed", [ChronParam.Place("place", PlaceId(near)), ChronParam.Count("men", a.Total), ChronParam.Count("days", (int)e.Day - m.StartDay + 1)],
                [PlaceId(near)], CampaignCause(cause, "ch_note_am_crossed_out")).Value;
        }
        else
        {
            m.State = MarchState.Arrived;
            PitchCamp(e, m, a, route, route.Length, e.Hour * 3600);
            NightsAndWatch(e, m, a, e.HourOfDay);
            hs.Camp(new HostId(m.Host), last);
            m.ChronEntry = Chron(e, "host_arrived", "ch_am_arrived", [ChronParam.Place("place", PlaceId(place)), ChronParam.Count("men", a.Total), ChronParam.Count("days", (int)e.Day - m.StartDay + 1), ChronParam.Count("hours", m.MarchHours)],
                [PlaceId(place)], CampaignCause(cause, "ch_note_am_march_own")).Value;
        }
        PlaceHost(m, hs, route, e.Hour * 3600);
    }

    private static Cause CampaignCause(EntityId evt, string note) => evt == EntityId.None ? new Cause(CauseKind.Campaign, null, "", note) : new Cause(CauseKind.Campaign, evt);

    /// <summary>Tells the supply book where the host is: the nearest settlement and its place in region metres, marching or camped (a camp binds the disease rule's army-camp hook through the supply service).</summary>
    private void PlaceHost(HostMarch m, HostSupply hs, HostRoute route, long sec)
    {
        var h = new HostId(m.Host);
        var p = route.Path.At(Math.Clamp(HostMotion.DistAt(m, sec == 0 ? m.HourSec : sec), 0, route.Length));
        var (near, _) = NearestTo(p.XMm, p.YMm);
        int xm = (int)((p.XMm - OriginXMm) / 1000), ym = (int)((p.YMm - OriginYMm) / 1000);
        bool camped = m.State is MarchState.Camped or MarchState.Arrived;
        if (camped && hs.Account(h).Mode != HostMode.Camp && near >= 0) hs.Camp(h, near);
        hs.Update(h, new HostPlace(near, xm, ym, hs.Account(h).Pasture), camped ? HostMode.Camp : HostMode.March);
    }

    private static long Sq(long v) => v * v;
    private static long Dist2(long ax, long ay, long bx, long by) => Sq(ax - bx) + Sq(ay - by);

    // ------------------------------------------------------------------ the camp

    /// <summary>The plan of the host's camp as it stands (a pure function of the host, the camp's serial and the men; cached).</summary>
    public CampPlan PlanOf(HostMarch m, CampRecord c, HostAccount a)
    {
        var key = (m.Host, c.Serial, c.Men);
        if (_plans.TryGetValue(key, out var p)) return p;
        if (_plans.Count > 64) _plans.Clear();
        return _plans[key] = CampPlan.For(W.Seed, m.Host, c.Serial, c.Men, a.Animals.Total + a.Carts);
    }

    /// <summary>The sentries a camp has posted now: the ranks (in the camp's order of men) on the watch of the night it is.</summary>
    public int[] SentriesOf(HostMarch m) => m.Camp == null ? [] : CampPlan.SentryRanks(m.Camp.Men, Math.Max(0, m.Camp.Nights - 1), m.Camp.Watch);

    /// <summary>
    /// An enemy comes at a camp in the night (4.5, from an ambush, a raid or a battle's opening): the sentries who see him within the night's sight shout, the sleepers within 30 m of every shout stir 5 to 20 game seconds on and
    /// shout in turn. Writes <c>camp_attacked</c> with the cause given and returns the result (null when the host has no camp). <paramref name="attacker"/> is where the enemy is in the camp's frame (millimetres from its centre).
    /// </summary>
    public AlarmResult? NightAttack(ArmyEnv e, HostId host, (int X, int Y) attacker, long sightSec, Cause cause, IReadOnlyDictionary<int, long>? hits = null)
    {
        if (!Book.Marches.TryGetValue(host.Value, out var m) || m.Camp is not { } c || Supply is not { } hs) return null;
        var a = hs.Account(host); var plan = PlanOf(m, c, a);
        var res = CampAlarm.Run(plan, c.Men, SentriesOf(m), W.Seed, (ulong)host.Value, attacker, sightSec, hits);
        Chron(e, "camp_attacked", "ch_am_camp_attacked", [ChronParam.Place("place", PlaceId(c.Near)), ChronParam.Count("men", c.Men), ChronParam.Count("woken", res.Woken), ChronParam.Count("seconds", (int)Math.Max(0, res.LastWakeSec - sightSec))],
            [PlaceId(c.Near)], cause);
        return res;
    }

    /// <summary>Whether the person (a row of the people table) belongs to a host that is on the road: the people bridge leaves him to the host's column instead of his schedule.</summary>
    public bool IsInColumn(int row) => Book.Marches.Count > 0 && Supply is { } hs && hs.Book.OnCampaign.TryGetValue(row, out int host) && Book.Marches.TryGetValue(host, out var m) && m.State != MarchState.Left;

    // ------------------------------------------------------------------ the disease rule's camp

    private HostMarch? MarchOfPerson(ulong person)
    {
        if (Book.Marches.Count == 0) return null;
        int row = RowOf(person);
        if (row >= 0 && Supply is { } hs && hs.Book.OnCampaign.TryGetValue(row, out int host) && Book.Marches.TryGetValue(host, out var m)) return m;
        foreach (var o in Book.Marches.Values) if (o.Outsiders.ContainsKey(person)) return o;
        return null;
    }

    /// <summary>The disease rule's <c>ArmyCampSize</c> for a person in a camp: the host's men, doubled once a camp of two nights or more has no latrine (4.5).</summary>
    public int CampSizeOf(ulong person)
    {
        var m = MarchOfPerson(person); if (m?.Camp is not { } c || m.State == MarchState.Left) return 0;
        return c.Men * (c.Nights >= 2 && !c.Latrine ? 2 : 1);
    }

    /// <summary>The disease rule's <c>UnboiledWaterDays</c>: the nights a camp has drawn from the river below a town or another camp.</summary>
    public int UnboiledDaysOf(ulong person)
    {
        var m = MarchOfPerson(person); if (m?.Camp is not { } c || m.State == MarchState.Left) return 0;
        return c.BelowTown && !c.WaterCarried ? Math.Max(1, c.Nights) : c.BelowTown ? Math.Max(1, c.Nights) : 0;
    }

    // ------------------------------------------------------------------ the dead

    /// <summary>The persons of a host in the order of the column: its levies (the persons mustered from the region) and the persons made for its men from outside, by person id.</summary>
    public List<ulong> ColumnPersons(HostId host)
    {
        var l = new List<ulong>(); var hs = Supply; if (hs == null) return l;
        var pt = People;
        foreach (var kv in hs.Book.OnCampaign) if (kv.Value == host.Value && pt.Status[kv.Key] == Ravensguard.Sim.People.PersonStatus.Alive) l.Add(pt.IdOf(kv.Key).Value);
        if (Book.Marches.TryGetValue(host.Value, out var m) && m.InRegion)
            foreach (var id in m.Outsiders.Keys) { int r = RowOf(id); if (r >= 0 && pt.Status[r] == Ravensguard.Sim.People.PersonStatus.Alive) l.Add(id); }
        l.Sort();
        return l;
    }

    /// <summary>Takes the dead off a host's rolls: a levy who died leaves the campaign and the roster's levies, an outsider who died leaves the list, his role's count and the outsiders' count. What the live fight did to the men reaches the host here.</summary>
    public int SyncDead(HostSupply hs, HostMarch m)
    {
        var pt = People; int n = 0; var a = hs.Account(new HostId(m.Host));
        foreach (int row in hs.Book.OnCampaign.Where(kv => kv.Value == m.Host && pt.Status[kv.Key] == Ravensguard.Sim.People.PersonStatus.Dead).Select(kv => kv.Key).ToList())
        { hs.Book.OnCampaign.Remove(row); a.Men[(int)TroopRole.Levy] = Math.Max(0, a.Men[(int)TroopRole.Levy] - 1); n++; }
        foreach (var (id, role) in m.Outsiders.ToList())
        {
            int row = RowOf(id);
            if (row >= 0 && pt.Status[row] == Ravensguard.Sim.People.PersonStatus.Dead) { m.Outsiders.Remove(id); a.Men[role] = Math.Max(0, a.Men[role] - 1); a.Outside = Math.Max(0, a.Outside - 1); n++; }
        }
        return n;
    }

    public int SyncDead(HostId host) => Supply is { } hs && Book.Marches.TryGetValue(host.Value, out var m) ? SyncDead(hs, m) : 0;
}
