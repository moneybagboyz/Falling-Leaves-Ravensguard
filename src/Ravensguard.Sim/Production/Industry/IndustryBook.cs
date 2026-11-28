namespace Ravensguard.Sim.Production.Industry;

/// <summary>Why a stage did not make what it could (industries 4.2). Shown on the tab in plain words with the good or the cause.</summary>
public enum StageStop : byte { None, NoInput, NoFuel, Full, UnderCrewed, NoPower, Broken, Flooded, Winter, Unpaid, NoLicence, Depleted }

/// <summary>
/// Where a stage's output goes (industries 5.10): on to the next stage, to the gate (the nearest book the carting makes worth it), kept in the stage's store, held for a supply contract (the goods of a few deliveries stay, the rest goes to the gate), or
/// carried by the works' own caravan to the destination of the stage's standing order (P7-IN4).
/// </summary>
public enum Routing : byte { Next, Gate, Store, Contract, Caravan }

/// <summary>One stage of a works (industries 3.2): its business, its recognised tier, the crew target and wage offer the owner set, where its output goes, its stop and its balance ring.</summary>
public sealed class WorkStage
{
    public const int RingColumns = 5, RingDays = 60;
    /// <summary>The ring's columns: income, wages, inputs, upkeep, royalties and rents (mites, the last <see cref="RingDays"/> days).</summary>
    public const int Income = 0, Wages = 1, Inputs = 2, Upkeep = 3, Royalties = 4;

    public ulong Business;
    /// <summary>The recognised tier (IN2 derives it from what is installed; until then the owner's plan sets it). A stage works at the highest tier its crew also reaches (4.1).</summary>
    public int Tier = 1;
    public int CrewTarget;
    /// <summary>The wage the labour market is offered, in permille of the class wage.</summary>
    public int WageOfferPm = 1000;
    public Routing Route = Routing.Next;
    /// <summary>P7-IN4: the stage's caravan order (destination, wagons, guards, standing or one trip), null until the owner sets one.</summary>
    public CaravanOrder? Order;
    /// <summary>A haul stage's distance in metres (the rates are over the kind's reference distance).</summary>
    public int DistanceM;
    public StageStop Stop;
    /// <summary>The good a stop names (a shortage), or 65535.</summary>
    public ushort StopGood = ushort.MaxValue;
    public int StopDays;
    /// <summary>Batches the stage has run (the key of the ore yield draw, stream 0x741).</summary>
    public long Batch;
    /// <summary>The business's <c>InputsSeason</c> as of the last works day (the inputs the market charged since are the day's inputs row).</summary>
    public long InputsSeen;
    /// <summary>The last day the stage ran, -1 before its first.</summary>
    public int LastDay = -1;
    /// <summary>The stage's premises: the rectangle of the home region (metres, inclusive) whose built pieces its tier is recognised from (5.2); empty until the owner sets one or an upgrade's plan is placed (then the tier is whatever <see cref="Tier"/> says, as in IN1).</summary>
    public int SiteX0, SiteY0, SiteX1 = -1, SiteY1 = -1;
    public bool HasSite => SiteX1 >= SiteX0 && SiteY1 >= SiteY0;
    /// <summary>The tier an upgrade in hand is building toward (0: none) and the construction site's serial that builds it.</summary>
    public int UpgradeTo;
    public long UpgradeSite;
    /// <summary>A drying stage's batches: how old each is in thousandths of a drying day and its grams (oldest first; 5.1, 5.6).</summary>
    public List<(long AgeMilli, long G)> Dwell { get; } = [];
    /// <summary>What the stage's store holds packed: per good, the container and the grams and containers bound together (5.9).</summary>
    public List<PackedRow> Packed { get; } = [];
    public long[] Ring = new long[RingDays * RingColumns];
    public int[] RingStamp = NewStamps();

    private static int[] NewStamps() { var a = new int[RingDays]; Array.Fill(a, -1); return a; }

    /// <summary>Adds mites to a column of the day's row (clearing the slot when it still holds an older day).</summary>
    public void Add(int day, int column, long mites)
    {
        int slot = day % RingDays; if (slot < 0) slot += RingDays;
        if (RingStamp[slot] != day) { Array.Clear(Ring, slot * RingColumns, RingColumns); RingStamp[slot] = day; }
        Ring[slot * RingColumns + column] += mites;
    }

    /// <summary>The column's sum over the last <paramref name="days"/> days ending at <paramref name="today"/>.</summary>
    public long Sum(int column, int today, int days = RingDays)
    {
        long s = 0;
        for (int d = Math.Max(0, today - days + 1); d <= today; d++) { int slot = d % RingDays; if (RingStamp[slot] == d) s += Ring[slot * RingColumns + column]; }
        return s;
    }

    public long Net(int today, int days = RingDays) => Sum(Income, today, days) - Sum(Wages, today, days) - Sum(Inputs, today, days) - Sum(Upkeep, today, days) - Sum(Royalties, today, days);
}

/// <summary>Where a works' own caravan goes: a town of the home region (another settlement's book) or a border gate that leads to a neighbouring region.</summary>
public enum DestKind : byte { Town = 0, Neighbour = 1 }

/// <summary>
/// A stage's caravan order (industries 5.10): the destination, the wagons (a kind and how many), the guards hired for each trip, and whether the order stands (a caravan goes whenever the stage holds a load and the fleet is home) or sends one trip. <see cref="Fleet"/> is the caravan row
/// (its id) that holds the wagons and drivers between trips, 0 until the first trip buys them. <see cref="Master"/> is a person row the owner named (-1: the best free trader is hired for each trip).
/// </summary>
public sealed class CaravanOrder
{
    public DestKind Kind;
    /// <summary>The settlement index (a town) or the region id (a neighbour).</summary>
    public int Dest;
    public byte Vehicle = 2;                       // Ravensguard.Sim.Caravans.VehicleKind: 1 cart, 2 ox wagon
    public int Wagons = 2, Guards = 2;
    public bool Standing;
    public ulong Fleet;
    public int Master = -1;
}

/// <summary>
/// A trip of a works' own caravan in flight (industries 7: "own caravans in flight: route, manifest, hired staff, expected margin at departure"). The caravan itself is an ordinary Phase 2 caravan row; this is what the works knows of it: where it
/// went, who leads it, what it carried, what the quote promised at departure and what the sales have brought so far.
/// </summary>
public sealed class OwnTrip
{
    public ulong Caravan, Stage;
    public DestKind Kind;
    public int Dest, Master, DepartDay, Days, Wagons, Guards;
    /// <summary>Mites the works put into the caravan's purse at departure (the float), the sales so far, the quote's margin, risk and value at departure.</summary>
    public long Float, Sales, QuotedMargin, QuotedRisk, QuotedValue, QuotedGross, FodderPerDay;
    public bool Sold;
    /// <summary>The manifest: goods and grams the caravan left with.</summary>
    public List<(ushort Good, long Grams)> Manifest { get; } = [];
}

/// <summary>Goods packed in containers in a stage's store (industries 5.9, <c>PackedStock</c>): the grams of <see cref="Good"/> sit in <see cref="Containers"/> of the <see cref="Container"/> good.</summary>
public sealed class PackedRow
{
    public ushort Good, Container;
    public long Grams, Containers;
}

/// <summary>The link from one stage to the next (industries 4.3): the distance between their stores and what carries the goods beyond the crews' own reach.</summary>
public sealed class WorkLink
{
    public int From, To;
    public int DistanceM;
    public int Carts, Wagons;
    public bool Crane, Packed;
}

/// <summary>A works (industries 3.2): the stages of one chain on one purse, the links between them, the owner and the payback counters.</summary>
public sealed class Works
{
    /// <summary>The first stage's business id: a works' id.</summary>
    public ulong Id => Stages.Count > 0 ? Stages[0].Business : 0;
    public int Owner;
    public string Chain = "";
    public int Settlement;
    public List<WorkStage> Stages { get; } = [];
    public List<WorkLink> Links { get; } = [];
    /// <summary>What the buildings cost (each plan's spend is recorded as it completes; IN2) and what the works has netted since it was founded.</summary>
    public long BuildingsCostMites, CumulativeNetMites;
    /// <summary>P8-BL-1 (EA_F18): the most cash the works has needed so far: the deepest its purse has sunk below the capital put into it, mites. Never negative; saved as optional keys 10 and 11 of the works' row.</summary>
    public long CapitalNeedMites;
    /// <summary>The coin put into the works' purse by its owner less what he took out (the founding capital, cash gifts and dividends), mites.</summary>
    public long CapitalInMites;
    /// <summary>What the works' stages owed their suppliers on terms at the end of the last works day, mites (the income the market paid is read from the purse's change, which a purchase on credit does not touch). Saved as key 12.</summary>
    public long OwedLastMites;
    public int StartedDay, PaidBackDay = -1;
    public bool Closed;
    /// <summary>The purse's balance at the end of the last works day, and the coin the owner put in or took out and the inputs paid since (for the income, which the market pays between works days).</summary>
    public long EndBalance, Injected, InputsPaid;
    /// <summary>P7-IN4: trips the works' own caravans finished, trips lost, and the coin they sold for and netted (sales less what the trip cost), for the tab and the export payback (EA3).</summary>
    public int CaravanTrips, CaravansLost;
    public long CaravanSoldMites, CaravanNetMites;
    public bool HasSellingTally => CaravanTrips != 0 || CaravansLost != 0 || CaravanSoldMites != 0 || CaravanNetMites != 0;
}

/// <summary>
/// The saved state of the industries (P7-IN1; the optional "industry" section, absent while empty): every works with its stages, links, routing, crew targets, wage offers, balance rings and payback counters. The stages' businesses, staffs and stores are ordinary
/// production rows. <see cref="Hash"/> is <c>IndustryHash</c> (overview 7): fixed order, integers only, zero while empty.
/// </summary>
public sealed class IndustryBook
{
    public List<Works> Works { get; } = [];
    /// <summary>P7-IN4: the own caravans in flight, ascending caravan id.</summary>
    public List<OwnTrip> Trips { get; } = [];
    public bool IsEmpty => Works.Count == 0;

    public OwnTrip? TripOf(ulong caravan) { foreach (var t in Trips) if (t.Caravan == caravan) return t; return null; }
    public void AddTrip(OwnTrip t) { int at = Trips.FindIndex(x => x.Caravan > t.Caravan); if (at < 0) Trips.Add(t); else Trips.Insert(at, t); }

    public Works? Of(ulong business)
    {
        foreach (var w in Works) foreach (var s in w.Stages) if (s.Business == business) return w;
        return null;
    }

    public (Works Works, int Index)? StageOf(ulong business)
    {
        foreach (var w in Works) for (int i = 0; i < w.Stages.Count; i++) if (w.Stages[i].Business == business) return (w, i);
        return null;
    }

    public ulong Hash()
    {
        if (IsEmpty) return 0;
        ulong h = 14695981039346656037UL;
        void M(long v) { unchecked { h = (h ^ (ulong)v) * 1099511628211UL; } }
        foreach (var w in Works)
        {
            M(w.Owner); foreach (char c in w.Chain) M(c); M(w.Settlement); M(w.BuildingsCostMites); M(w.CumulativeNetMites); M(w.StartedDay); M(w.PaidBackDay); M(w.Closed ? 1 : 0); M(w.EndBalance); M(w.Injected); M(w.InputsPaid);
            foreach (var s in w.Stages)
            {
                M((long)s.Business); M(s.Tier); M(s.CrewTarget); M(s.WageOfferPm); M((int)s.Route); M(s.DistanceM); M((int)s.Stop); M(s.StopGood); M(s.StopDays); M(s.Batch); M(s.LastDay); M(s.InputsSeen);
                foreach (long r in s.Ring) M(r); foreach (int t in s.RingStamp) M(t);
                M(s.SiteX0); M(s.SiteY0); M(s.SiteX1); M(s.SiteY1); M(s.UpgradeTo); M(s.UpgradeSite);
                foreach (var (a, g) in s.Dwell) { M(a); M(g); }
                foreach (var p in s.Packed) { M(p.Good); M(p.Container); M(p.Grams); M(p.Containers); }
                if (s.Order is { } o) { M(-3); M((int)o.Kind); M(o.Dest); M(o.Vehicle); M(o.Wagons); M(o.Guards); M(o.Standing ? 1 : 0); M((long)o.Fleet); M(o.Master); }
            }
            if (w.HasSellingTally) { M(-4); M(w.CaravanTrips); M(w.CaravansLost); M(w.CaravanSoldMites); M(w.CaravanNetMites); }
            M(-1);
            foreach (var l in w.Links) { M(l.From); M(l.To); M(l.DistanceM); M(l.Carts); M(l.Wagons); M(l.Crane ? 1 : 0); M(l.Packed ? 1 : 0); }
            M(-2);
        }
        foreach (var t in Trips)
        {
            M(-5); M((long)t.Caravan); M((long)t.Stage); M((int)t.Kind); M(t.Dest); M(t.Master); M(t.DepartDay); M(t.Days); M(t.Wagons); M(t.Guards); M(t.Float); M(t.Sales); M(t.QuotedMargin); M(t.QuotedRisk); M(t.QuotedValue); M(t.QuotedGross); M(t.FodderPerDay); M(t.Sold ? 1 : 0);
            foreach (var (g, q) in t.Manifest) { M(g); M(q); }
        }
        return h;
    }
}
