using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Construction;

// P5-B: structural support lost, the groan and the fall (spec 4.5; decided with the user: the floor groans and dust falls for about 10 game seconds, then it falls, with real fall wounds).
public sealed partial class ConstructionWorld
{
    /// <summary>
    /// The built structure cells that stand no more once <paramref name="removed"/> are gone (and <paramref name="already"/>, cells already doomed): every cell at or above the lowest removed
    /// level within reach of a removed cell is checked again, in (z, y, x) order, and what falls is checked around in turn until nothing more does (the cascade, BC-14).
    /// </summary>
    public SortedSet<long> Doomed(IReadOnlyCollection<long> removed, IReadOnlyCollection<long> already)
    {
        var gone = new HashSet<long>(removed); gone.UnionWith(already);
        var doomed = new SortedSet<long>();
        if (removed.Count == 0) return doomed;
        int r = Math.Max(Rules.SupportReachTiles, Rules.BeamReachTiles) + Rules.BeamSpanTiles + 1;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z0 = int.MaxValue;
        void Grow(long k) { var (x, y, z) = ConstructionIds.Unpack(k); x0 = Math.Min(x0, x - r); x1 = Math.Max(x1, x + r); y0 = Math.Min(y0, y - r); y1 = Math.Max(y1, y + r); z0 = Math.Min(z0, Math.Max(1, z)); }
        foreach (var k in removed) Grow(k);
        var at = (CellAt)((x, y, z) => gone.Contains(ConstructionIds.CellKey(x, y, z)) ? null : BuiltStructure(x, y, z));
        // passes over the candidates in (z, y, x) order until nothing more falls; the box grows with what falls (the cascade, BC-14)
        while (true)
        {
            var cand = new List<BuiltCell>();
            foreach (var b in Book.Built.Values)
                if (b.Slot == GhostSlot.Structure && b.Z >= z0 && b.X >= x0 && b.X <= x1 && b.Y >= y0 && b.Y <= y1) cand.Add(b);
            int bx0 = x0, bx1 = x1, by0 = y0, by1 = y1, bz0 = z0;
            bool more = true;
            while (more)
            {
                more = false;
                foreach (var b in cand)
                {
                    long ck = ConstructionIds.CellKey(b.X, b.Y, b.Z);
                    if (gone.Contains(ck)) continue;
                    if (Support.Check(b.X, b.Y, b.Z, Costs[b.Piece], at).Ok) continue;
                    gone.Add(ck); doomed.Add(ck); more = true; Grow(ck);
                }
            }
            if (x0 == bx0 && x1 == bx1 && y0 == by0 && y1 == by1 && z0 == bz0) return doomed;
        }
    }

    /// <summary>Support was taken away at <paramref name="removed"/>: what no longer stands groans (a pending fall <c>groan_seconds</c> later), unless it already does.</summary>
    private void StartGroan(IReadOnlyCollection<long> removed, long sec, EntityId cause, string why)
    {
        var pending = new HashSet<long>(Book.Groans.SelectMany(g => g.Cells));
        var doomed = Doomed(removed, pending);
        if (doomed.Count == 0) return;
        var g = new Groan { EventId = ConstructionIds.Make(RegionId, ConstructionIds.EventSlot, Book.Take()).Value, StartSec = sec, FallSec = sec + Rules.GroanSeconds, Cells = [.. doomed], Cause = cause, Why = why };
        Book.Groans.Add(g);
        foreach (var k in doomed) { var (x, y, z) = ConstructionIds.Unpack(k); Event(ConstructionEventKind.Groan, sec, x, y, z, BuiltAt(x, y, z)?.Piece ?? "", default); }
        Chron(sec, "floor_groans", [cause], [new Cause(CauseKind.Other, null, why)], ("cells", doomed.Count.ToString()), ("fall_sec", g.FallSec.ToString()));
    }

    /// <summary>Lets every groan due by <paramref name="uptoSec"/> fall, in (fall second, event id) order. True when anything fell.</summary>
    private bool ResolveGroans(long uptoSec)
    {
        bool any = false;
        while (true)
        {
            Groan? next = null;
            foreach (var g in Book.Groans) if (g.FallSec <= uptoSec && (next == null || (g.FallSec, g.EventId).CompareTo((next.FallSec, next.EventId)) < 0)) next = g;
            if (next == null) return any;
            Book.Groans.Remove(next);
            FallNow(next);
            any = true;
        }
    }

    /// <summary>
    /// The fall (spec 4.5): the groaning cells that still stand unsupported at the fall second, and everything that loses its support with them, fall together in (z, y, x) order. Each piece
    /// becomes rubble on the cell it lands on (its salvage share as goods, the rest as waste: mass exact); people on it drop to the floor below (3,000 mm a level, real fall wounds), stores
    /// on it land there too, ghosts on it are cancelled (their materials back to the pile). Rooms below lose their roof; the street's portals follow the cells.
    /// </summary>
    private void FallNow(Groan g)
    {
        long sec = g.FallSec;
        var still = g.Cells.Where(k => { var (x, y, z) = ConstructionIds.Unpack(k); return BuiltAt(x, y, z) is { } b && !Support.Check(x, y, z, Costs[b.Piece], BuiltView([k])).Ok; }).ToList();
        if (still.Count == 0) return;
        var all = new SortedSet<long>(still);
        all.UnionWith(Doomed(still, []));
        var fallen = all.ToList();                       // (z, y, x) ascending
        var gone = new HashSet<long>(fallen);
        // where each fallen cell's contents land: the first standing floor below, or the ground
        int LandZ(int x, int y, int z)
        {
            for (int zz = z - 1; zz > 0; zz--)
                if (!gone.Contains(ConstructionIds.CellKey(x, y, zz)) && BuiltStructure(x, y, zz) is { } p && PieceCatalog.IsRoomKind(p.Kind)) return zz;
            return 0;
        }
        var first = ConstructionIds.Unpack(fallen[DetRng.Range(Seed, (long)g.EventId, (ulong)fallen.Count, RngStreams.CollapseRubble, 0, 0, fallen.Count)]);
        int lz0 = LandZ(first.X, first.Y, first.Z);
        var rubbleId = ConstructionIds.Make(RegionId, ConstructionIds.RubbleSlot, (1UL << 35) | (g.EventId & 0xFFFFFFFF));
        var wasteId = ConstructionIds.Make(RegionId, ConstructionIds.ChestSlot, (1UL << 35) | (g.EventId & 0xFFFFFFFF));
        var rubble = Econ.OpenStore(StoreKind.Open, rubbleId); var waste = Econ.OpenStore(StoreKind.Open, wasteId);
        Book.Placed[rubbleId] = new PlacedStore(rubbleId, first.X, first.Y, lz0);
        Book.Placed[wasteId] = new PlacedStore(wasteId, first.X, first.Y, lz0);
        // people on the falling cells: the crews the world knows, and whoever the live tier reports
        var people = new SortedDictionary<ulong, (EntityId P, int X, int Y, int Z)>();
        foreach (var s in Book.Sites.Values)
            foreach (var m in s.Crew)
                if (m.Ghost != default && Book.Ghosts.GetValueOrDefault(m.Ghost) is { } mg && gone.Contains(ConstructionIds.CellKey(mg.X, mg.Y, mg.Z))) people[m.Person.Value] = (m.Person, mg.X, mg.Y, mg.Z);
        if (PeopleOn != null) foreach (var (p, x, y, z) in PeopleOn(gone)) if (gone.Contains(ConstructionIds.CellKey(x, y, z))) people[p.Value] = (p, x, y, z);
        var edits = new List<CellEdit>();
        var sites = new SortedSet<EntityId>();
        foreach (var k in fallen)
        {
            var (x, y, z) = ConstructionIds.Unpack(k);
            foreach (var slot in new[] { GhostSlot.Fixture, GhostSlot.Structure })
            {
                if (GhostAt(x, y, z, slot) is { } gh) { sites.Add(gh.Site); CancelGhost(gh); }
                if (!RemoveBuilt(ConstructionIds.SlotKey(x, y, z, slot), out var b)) continue;
                Unbuild(Costs[b.Piece], rubble, waste);
                edits.Add(new CellEdit(x, y, z, slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, null));
                if (Site(b.Site) is { } bs) sites.Add(bs.Id);
                Event(ConstructionEventKind.Fell, sec, x, y, z, b.Piece, b.Site);
            }
        }
        // stores standing on fallen cells land below
        foreach (var (id, ps) in Book.Placed.ToList())
            if (gone.Contains(ConstructionIds.CellKey(ps.X, ps.Y, ps.Z))) Book.Placed[id] = ps with { Z = LandZ(ps.X, ps.Y, ps.Z) };
        foreach (var s in Book.Sites.Values) if (gone.Contains(ConstructionIds.CellKey(s.PileX, s.PileY, s.PileZ))) s.PileZ = LandZ(s.PileX, s.PileY, s.PileZ);
        Rooms.Apply(edits);
        foreach (var k in fallen) { var (x, y, z) = ConstructionIds.Unpack(k); WriteTile(x, y, z); }
        foreach (var (_, (p, x, y, z)) in people)
        {
            int lz = LandZ(x, y, z);
            int mm = (z - lz) * Rules.FallMmPerLevel;
            Fall?.Invoke(p, mm, g.EventId, sec);
            Moved?.Invoke(p, x, y, lz);
            foreach (var s in Book.Sites.Values) foreach (var m in s.Crew) if (m.Person == p) m.Ghost = default;
        }
        _builtVersion++;
        foreach (var sid in sites) if (Site(sid) is { } s) Refresh(s);
        foreach (var s in Book.Sites.Values) if (s.Open) Refresh(s);
        Chron(sec, "collapse", [g.Cause, .. people.Values.Select(v => v.P)], [new Cause(CauseKind.Other, null, g.Why)], ("cells", fallen.Count.ToString()), ("people", people.Count.ToString()), ("x", first.X.ToString()), ("y", first.Y.ToString()), ("z", first.Z.ToString()));
        SyncStructures();
    }

    /// <summary>Seconds left before a cell falls (-1: it does not groan).</summary>
    public long GroanLeft(int x, int y, int z, long nowSec)
    {
        long k = ConstructionIds.CellKey(x, y, z);
        foreach (var g in Book.Groans) if (Array.BinarySearch(g.Cells, k) >= 0) return Math.Max(0, g.FallSec - nowSec);
        return -1;
    }

    /// <summary>A built piece destroyed by something other than hands (a test, the dev console; fire and siege later): it goes at once, salvage into the site's rubble, and what it held groans.</summary>
    public ConstructionResult Destroy(int x, int y, int z, long nowSec, EntityId cause, string why = "destroyed")
    {
        var b = BuiltAt(x, y, z);
        if (b == null) return ConstructionResult.No("cn_nothing_built_here");
        var s = Site(b.Site)!;
        if (BuiltAt(x, y, z, GhostSlot.Fixture) != null) TakeDown(s, x, y, z, GhostSlot.Fixture, nowSec, cause, why);
        TakeDown(s, x, y, z, GhostSlot.Structure, nowSec, cause, why);
        _builtVersion++;
        foreach (var o in Book.Sites.Values) if (o.Open) Refresh(o);
        SyncStructures();
        Publish(nowSec);
        return ConstructionResult.Yes("cn_destroyed", Costs[b.Piece].Name);
    }

    // ------------------------------------------------------------------ the view (presentation reads only this)

    private ConstructionView? _lastView;
    /// <summary>Set by the street view (the only reader): without it no view is built (a headless world, a test, the hours nobody watches).</summary>
    public volatile bool ViewWanted;
    private long _viewBuiltVersion = -1;
    private IReadOnlyList<BuiltView> _builtList = [];

    /// <summary>Builds and swaps in the view the main thread reads (a copy: sites, ghosts with progress, built cells, groans, crews at their ghosts, recent events).</summary>
    public void Publish(long nowSec)
    {
        if (!ViewWanted) return;
        if (_viewBuiltVersion != _builtVersion)
        {
            var cells = new SortedDictionary<long, (string? S, string? F)>();
            foreach (var b in Book.Built.Values)
            {
                long k = ConstructionIds.CellKey(b.X, b.Y, b.Z);
                var cur = cells.GetValueOrDefault(k);
                cells[k] = b.Slot == GhostSlot.Fixture ? (cur.S, b.Piece) : (b.Piece, cur.F);
            }
            _builtList = cells.Select(kv => { var (x, y, z) = ConstructionIds.Unpack(kv.Key); return new BuiltView(x, y, z, kv.Value.S, kv.Value.F); }).ToList();
            _viewBuiltVersion = _builtVersion;
        }
        var ghosts = new List<GhostView>(Book.Ghosts.Count);
        var workers = new Dictionary<EntityId, int>();
        foreach (var s in Book.Sites.Values) foreach (var m in s.Crew) if (m.Ghost != default) workers[m.Ghost] = workers.GetValueOrDefault(m.Ghost) + 1;
        foreach (var g in Book.Ghosts.Values)
        {
            if (!g.Live) continue;
            var c = CostOf(g);
            ghosts.Add(new GhostView(g.Id, g.X, g.Y, g.Z, g.Piece, c.Kind, g.State, g.Block, g.Work, (int)(g.WorkNeed <= 0 ? 0 : g.WorkDone * 1000 / g.WorkNeed), workers.GetValueOrDefault(g.Id), g.Site));
        }
        var sites = new List<SiteView>();
        var crew = new List<CrewView>();
        foreach (var s in Book.Sites.Values)
        {
            long done = 0, total = 0;
            foreach (var g in GhostsOf(s.Id)) { total += g.WorkNeed; done += g.WorkDone; }
            var pile = PileOf(s);
            var need = Needed(s);
            var mats = new List<SiteMaterial>();
            var onWay = new Dictionary<ushort, long>();
            if (Jobs?.Invoke() is { } jt)
                foreach (var j in jt.Table.Jobs.Values) if (j.DstOwner == s.Pile && !j.Closed) onWay[(ushort)j.P4] = onWay.GetValueOrDefault((ushort)j.P4) + j.P5;
            foreach (var (good, grams) in need) mats.Add(new SiteMaterial(Econ.Goods.StringId(good), Econ.Goods.Def(good).Name, grams, pile.Qty(good), onWay.GetValueOrDefault(good)));
            long escrow = Econ.Ledger.TryFind(PurseKind.Chest, s.Escrow, out var ep) ? Econ.Ledger.Balance(ep).Mites : 0;
            sites.Add(new SiteView(s.Id, s.Blueprint, s.State, s.Pause, s.Crew.Count, s.Crew.Count(m => m.Ghost != default), done, total, s.PiecesBuilt, GhostsOf(s.Id).Count(), s.PileX, s.PileY, s.PileZ, mats, escrow, s.Owner,
                pile.TotalMassG()));
            foreach (var m in s.Crew)
            {
                var g = m.Ghost != default ? Book.Ghosts.GetValueOrDefault(m.Ghost) : null;
                crew.Add(new CrewView(m.Person, NameOf?.Invoke(m.Person) ?? "", g?.X ?? s.PileX, g?.Y ?? s.PileY + 1, g?.Z ?? s.PileZ, g != null, m.Class, m.WageMitesPerDay) { Site = s.Id, WorkedTodaySec = m.WorkedTodaySec });   // P5-C: Site, WorkedTodaySec
            }
        }
        var groans = Book.Groans.Select(g => new GroanView(g.EventId, g.StartSec, g.FallSec, g.Cells.Select(k => { var (x, y, z) = ConstructionIds.Unpack(k); return (x, y, z); }).ToList())).ToList();
        var placed = Book.Placed.Values.Where(p => Econ.OtherStores.TryGetValue(p.Owner, out var st) && st.RowCount > 0)
            .Select(p => (p.X, p.Y, p.Z, Econ.OtherStores[p.Owner].TotalMassG(), ConstructionIds.SlotOf(p.Owner) is ConstructionIds.RubbleSlot || (ConstructionIds.SlotOf(p.Owner) == ConstructionIds.ChestSlot && (ConstructionIds.SerialOf(p.Owner) & (1UL << 35)) != 0))).ToList();
        _lastView = new ConstructionView(nowSec, _viewBuiltVersion, sites, ghosts, _builtList, groans, crew, [.. Book.Events], placed);
        Book.View = _lastView;
    }
}

public sealed record GhostView(EntityId Id, int X, int Y, int Z, string Piece, PieceKind Kind, GhostState State, GhostBlock Block, GhostWork Work, int ProgressPm, int Workers, EntityId Site);
public sealed record SiteMaterial(string Good, string Name, long NeedG, long InPileG, long OnTheWayG);
public sealed record SiteView(EntityId Id, string Blueprint, SiteState State, PauseReason Pause, int Crew, int Working, long DoneUnits, long TotalUnits, int Built, int Ghosts, int PileX, int PileY, int PileZ,
    IReadOnlyList<SiteMaterial> Materials, long EscrowMites, EntityId Owner, long PileG);
public sealed record BuiltView(int X, int Y, int Z, string? Structure, string? Fixture);
public sealed record GroanView(ulong Event, long StartSec, long FallSec, IReadOnlyList<(int X, int Y, int Z)> Cells);
public sealed record CrewView(EntityId Person, string Name, int X, int Y, int Z, bool Working, CrewClass Class, long WageMites)
{
    /// <summary>P5-C: the site the member works for, and the seconds worked today (the site panel's crew rows).</summary>
    public EntityId Site { get; init; }
    public long WorkedTodaySec { get; init; }
}

/// <summary>What the main thread sees of construction at the last barrier: an immutable copy (build-mode-and-construction 6).</summary>
public sealed record ConstructionView(long Sec, long BuiltVersion, IReadOnlyList<SiteView> Sites, IReadOnlyList<GhostView> Ghosts, IReadOnlyList<BuiltView> Built, IReadOnlyList<GroanView> Groans,
    IReadOnlyList<CrewView> Crew, IReadOnlyList<ConstructionEvent> Events, IReadOnlyList<(int X, int Y, int Z, long MassG, bool Rubble)> Piles);
