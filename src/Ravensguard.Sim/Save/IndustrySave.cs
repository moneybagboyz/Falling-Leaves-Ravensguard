using MessagePack;
using Ravensguard.Sim.Production.Industry;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The optional "industry" section (P7-IN1, industries 7; no version bump, written only while the book holds a works, so a world that never used it keeps its bytes): each works with its stages, links, routing, crew targets, wage offers, balance rings and payback counters.
/// Rows are in the book's own order, so the bytes are deterministic. The stages' businesses, staffs and stores are ordinary production rows (their ids are named here); the first stage's purse is the works' purse and every other stage is pointed at it on load.
/// IN4 appends the packed-stock rows and own caravans in flight as further keys.
/// </summary>
[MessagePackObject]
public sealed class IndustrySave
{
    public const string SectionName = "industry";

    /// <summary>Per works: owner, settlement, buildings' cost, cumulative net, started day, paid-back day, closed, end balance, injected, inputs paid.</summary>
    [Key(0)] public long[][] Works { get; set; } = [];
    [Key(1)] public string[] Chains { get; set; } = [];
    /// <summary>Per stage, works by works in order: business, tier, crew target, wage offer, route, distance, stop, stop good, stop days, batch, last day, inputs seen.</summary>
    [Key(2)] public long[][] Stages { get; set; } = [];
    /// <summary>Per stage in the same order: the ring's rows then the day stamps.</summary>
    [Key(3)] public long[][] Rings { get; set; } = [];
    /// <summary>Per link: works index, from, to, distance, carts, wagons, crane, packed.</summary>
    [Key(4)] public long[][] Links { get; set; } = [];
    /// <summary>Per works: how many stages it has.</summary>
    [Key(5)] public int[] StageCounts { get; set; } = [];
    /// <summary>P7-IN2, per stage in the same order: the site rectangle (x0, y0, x1, y1), the upgrade tier and its construction site, then the drying batches (age, grams pairs) after a count, then the packed rows (good, container, grams, containers) after a count. Null while no stage uses any.</summary>
    [Key(6)] public long[][]? Extras { get; set; }
    /// <summary>P7-IN4, selling, tagged rows: <c>1</c> a stage's caravan order (business, kind, destination, vehicle, wagons, guards, standing, fleet, master), <c>2</c> a trip in flight (caravan, stage, kind, destination, master, departure day, days, wagons, guards, float, sales, quoted margin, risk, value and gross, fodder a day, sold, then the manifest's count and its good and grams pairs), <c>3</c> a works' caravan tally (works index, trips, lost, sold, net). Null while no works has an order, a trip or a tally.</summary>
    [Key(7)] public long[][]? Selling { get; set; }

    public static IndustrySave Capture(IndustryBook b)
    {
        var stages = new List<long[]>(); var rings = new List<long[]>(); var links = new List<long[]>();
        for (int i = 0; i < b.Works.Count; i++)
        {
            var w = b.Works[i];
            foreach (var s in w.Stages)
            {
                stages.Add([(long)s.Business, s.Tier, s.CrewTarget, s.WageOfferPm, (long)s.Route, s.DistanceM, (long)s.Stop, s.StopGood, s.StopDays, s.Batch, s.LastDay, s.InputsSeen]);
                var ring = new long[s.Ring.Length + s.RingStamp.Length];
                Array.Copy(s.Ring, ring, s.Ring.Length); for (int k = 0; k < s.RingStamp.Length; k++) ring[s.Ring.Length + k] = s.RingStamp[k];
                rings.Add(ring);
            }
            foreach (var l in w.Links) links.Add([i, l.From, l.To, l.DistanceM, l.Carts, l.Wagons, l.Crane ? 1 : 0, l.Packed ? 1 : 0]);
        }
        var extras = new List<long[]>(); bool anyExtra = false;
        foreach (var w in b.Works)
            foreach (var s in w.Stages)
            {
                var e = new List<long> { s.SiteX0, s.SiteY0, s.SiteX1, s.SiteY1, s.UpgradeTo, s.UpgradeSite, s.Dwell.Count };
                foreach (var (a, g) in s.Dwell) { e.Add(a); e.Add(g); }
                e.Add(s.Packed.Count);
                foreach (var p in s.Packed) { e.Add(p.Good); e.Add(p.Container); e.Add(p.Grams); e.Add(p.Containers); }
                if (s.HasSite || s.UpgradeTo != 0 || s.Dwell.Count > 0 || s.Packed.Count > 0) anyExtra = true;
                extras.Add([.. e]);
            }
        var sell = new List<long[]>();
        foreach (var w in b.Works)
            foreach (var s in w.Stages)
                if (s.Order is { } o) sell.Add([1, (long)s.Business, (long)o.Kind, o.Dest, o.Vehicle, o.Wagons, o.Guards, o.Standing ? 1 : 0, (long)o.Fleet, o.Master]);
        foreach (var t in b.Trips)
        {
            var row = new List<long> { 2, (long)t.Caravan, (long)t.Stage, (long)t.Kind, t.Dest, t.Master, t.DepartDay, t.Days, t.Wagons, t.Guards, t.Float, t.Sales, t.QuotedMargin, t.QuotedRisk, t.QuotedValue, t.QuotedGross, t.FodderPerDay, t.Sold ? 1 : 0, t.Manifest.Count };
            foreach (var (g, q) in t.Manifest) { row.Add(g); row.Add(q); }
            sell.Add([.. row]);
        }
        for (int i = 0; i < b.Works.Count; i++) { var w = b.Works[i]; if (w.HasSellingTally) sell.Add([3, i, w.CaravanTrips, w.CaravansLost, w.CaravanSoldMites, w.CaravanNetMites]); }
        return new IndustrySave
        {
            Selling = sell.Count > 0 ? [.. sell] : null,
            Extras = anyExtra ? [.. extras] : null,
            Works = [.. b.Works.Select(w => new long[] { w.Owner, w.Settlement, w.BuildingsCostMites, w.CumulativeNetMites, w.StartedDay, w.PaidBackDay, w.Closed ? 1 : 0, w.EndBalance, w.Injected, w.InputsPaid })],
            Chains = [.. b.Works.Select(w => w.Chain)], StageCounts = [.. b.Works.Select(w => w.Stages.Count)],
            Stages = [.. stages], Rings = [.. rings], Links = [.. links],
        };
    }

    public IndustryBook Restore()
    {
        if (Works.Length != Chains.Length || Works.Length != StageCounts.Length || Stages.Length != Rings.Length) throw new SaveException("Corrupt 'industry' section in save.");
        var b = new IndustryBook(); int si = 0;
        for (int i = 0; i < Works.Length; i++)
        {
            var v = Works[i]; if (v.Length < 10) throw new SaveException("Corrupt works in save.");
            var w = new Works { Owner = (int)v[0], Settlement = (int)v[1], BuildingsCostMites = v[2], CumulativeNetMites = v[3], StartedDay = (int)v[4], PaidBackDay = (int)v[5], Closed = v[6] != 0, EndBalance = v[7], Injected = v[8], InputsPaid = v[9], Chain = Chains[i] };
            for (int k = 0; k < StageCounts[i]; k++, si++)
            {
                if (si >= Stages.Length) throw new SaveException("Corrupt works stages in save.");
                var s = Stages[si]; if (s.Length < 12) throw new SaveException("Corrupt stage in save.");
                var ring = Rings[si]; if (ring.Length != WorkStage.RingDays * WorkStage.RingColumns + WorkStage.RingDays) throw new SaveException("Corrupt stage ring in save.");
                var ws = new WorkStage
                {
                    Business = (ulong)s[0], Tier = (int)s[1], CrewTarget = (int)s[2], WageOfferPm = (int)s[3], Route = (Routing)s[4], DistanceM = (int)s[5], Stop = (StageStop)s[6], StopGood = (ushort)s[7],
                    StopDays = (int)s[8], Batch = s[9], LastDay = (int)s[10], InputsSeen = s[11],
                };
                Array.Copy(ring, ws.Ring, ws.Ring.Length); for (int d = 0; d < ws.RingStamp.Length; d++) ws.RingStamp[d] = (int)ring[ws.Ring.Length + d];
                if (Extras != null)
                {
                    if (si >= Extras.Length || Extras[si].Length < 8) throw new SaveException("Corrupt stage extras in save.");
                    var e = Extras[si]; int at = 0;
                    ws.SiteX0 = (int)e[at++]; ws.SiteY0 = (int)e[at++]; ws.SiteX1 = (int)e[at++]; ws.SiteY1 = (int)e[at++]; ws.UpgradeTo = (int)e[at++]; ws.UpgradeSite = e[at++];
                    long nd = e[at++]; if (nd < 0 || at + nd * 2 >= e.Length) throw new SaveException("Corrupt stage extras in save.");
                    for (long q = 0; q < nd; q++) { ws.Dwell.Add((e[at], e[at + 1])); at += 2; }
                    long np = e[at++]; if (np < 0 || at + np * 4 > e.Length) throw new SaveException("Corrupt stage extras in save.");
                    for (long q = 0; q < np; q++) { ws.Packed.Add(new PackedRow { Good = (ushort)e[at], Container = (ushort)e[at + 1], Grams = e[at + 2], Containers = e[at + 3] }); at += 4; }
                }
                w.Stages.Add(ws);
            }
            b.Works.Add(w);
        }
        if (Selling != null)
            foreach (var r in Selling)
            {
                if (r.Length < 1) throw new SaveException("Corrupt selling row in save.");
                switch (r[0])
                {
                    case 1:
                        {
                            if (r.Length < 10) throw new SaveException("Corrupt caravan order in save.");
                            var st = b.StageOf((ulong)r[1]) ?? throw new SaveException("A caravan order names no stage.");
                            st.Works.Stages[st.Index].Order = new CaravanOrder { Kind = (DestKind)r[2], Dest = (int)r[3], Vehicle = (byte)r[4], Wagons = (int)r[5], Guards = (int)r[6], Standing = r[7] != 0, Fleet = (ulong)r[8], Master = (int)r[9] };
                            break;
                        }
                    case 2:
                        {
                            if (r.Length < 19) throw new SaveException("Corrupt own trip in save.");
                            var t = new OwnTrip { Caravan = (ulong)r[1], Stage = (ulong)r[2], Kind = (DestKind)r[3], Dest = (int)r[4], Master = (int)r[5], DepartDay = (int)r[6], Days = (int)r[7], Wagons = (int)r[8], Guards = (int)r[9], Float = r[10], Sales = r[11], QuotedMargin = r[12], QuotedRisk = r[13], QuotedValue = r[14], QuotedGross = r[15], FodderPerDay = r[16], Sold = r[17] != 0 };
                            long n = r[18]; if (n < 0 || 19 + n * 2 > r.Length) throw new SaveException("Corrupt own trip manifest in save.");
                            for (long q = 0; q < n; q++) t.Manifest.Add(((ushort)r[19 + q * 2], r[20 + q * 2]));
                            b.Trips.Add(t);
                            break;
                        }
                    case 3:
                        {
                            if (r.Length < 6 || r[1] < 0 || r[1] >= b.Works.Count) throw new SaveException("Corrupt works tally in save.");
                            var w = b.Works[(int)r[1]]; w.CaravanTrips = (int)r[2]; w.CaravansLost = (int)r[3]; w.CaravanSoldMites = r[4]; w.CaravanNetMites = r[5];
                            break;
                        }
                    default: throw new SaveException("Unknown selling row in save.");
                }
            }
        foreach (var l in Links)
        {
            if (l.Length < 8 || l[0] < 0 || l[0] >= b.Works.Count) throw new SaveException("Corrupt link in save.");
            b.Works[(int)l[0]].Links.Add(new WorkLink { From = (int)l[1], To = (int)l[2], DistanceM = (int)l[3], Carts = (int)l[4], Wagons = (int)l[5], Crane = l[6] != 0, Packed = l[7] != 0 });
        }
        return b;
    }
}
