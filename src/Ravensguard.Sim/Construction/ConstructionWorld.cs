using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Construction;

/// <summary>Writes a chronicle line (the abstract tick's outbox, or the world's chronicle at the desk).</summary>
public delegate EntityId ConstructionChronicle(GameTime when, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data);

/// <summary>Where built pieces become tile codes in the chunks (the chunk store's ID-keyed deltas in the running game; nothing in tests).</summary>
public interface ITileSink { void Set(int x, int y, int z, string? structure, string? fixture); }

/// <summary>One cell of a placement: a piece at (x, y, z) in region metres and level, turned <c>Rot</c> quarter turns.</summary>
public readonly record struct PlaceCell(int X, int Y, int Z, string Piece, byte Rot = 0);

/// <summary>A cell refused at placement with its reason (an en.csv key and its arguments).</summary>
public sealed record PlaceRefusal(int X, int Y, int Z, string Piece, string Key, object[] Args);

/// <summary>What a placement did: the site, the ghosts made, the cells refused and a summary key.</summary>
public sealed record PlaceResult(bool Ok, EntityId Site, IReadOnlyList<EntityId> Ghosts, IReadOnlyList<PlaceRefusal> Refused, string Key, object[] Args)
{
    public static PlaceResult No(string key, params object[] args) => new(false, default, [], [], key, args);
}

/// <summary>The outcome of a construction command at the desk (the hero's side).</summary>
public sealed record ConstructionResult(bool Ok, string Key, object[] Args, long Mites = 0, EntityId Subject = default)
{
    public static ConstructionResult No(string key, params object[] args) => new(false, key, args);
    public static ConstructionResult Yes(string key, params object[] args) => new(true, key, args);
}

/// <summary>
/// Hands-on construction for the home region (build-mode-and-construction.md, P5-B): ghosts placed where the land rule allows (<see cref="Place"/>), materials that must lie in the site's
/// pile (moved into a piece when work on it starts, mass exact), people who do the work (the hero by hand through his desk, followers, hired hands paid through the ledger), structural
/// support with groans and collapses, and the live <see cref="RoomWorld"/> updated piece by piece. Every change happens on the tick worker at a barrier; the work engine credits whole
/// quanta of game time (<see cref="AdvanceTo"/>), so the result is a pure function of game time whatever the call cadence. The main thread reads only <see cref="ConstructionBook.View"/>.
/// </summary>
public sealed partial class ConstructionWorld
{
    public ConstructionBook Book { get; }
    public EconomyState Econ { get; }
    public PieceCosts Costs { get; }
    public SupportRules Support { get; }
    public ConstructionRulesDef Rules => Costs.Rules;
    public RoomWorld Rooms { get; private set; }
    public int RegionId { get; }
    public Population? Pop { get; init; }
    /// <summary>The world seed (counter streams 0x510, 0x416).</summary>
    public ulong Seed { get; init; }

    // ---- hooks (null: the default) ----
    /// <summary>The land rule (LandRegistry.May); null: building is allowed everywhere (a test world without land).</summary>
    public Func<EntityId, int, LandAction, int, int, MayResult>? May { get; set; }
    /// <summary>The ground under a cell: a road on it, and its slope in permille. Null: no road, flat.</summary>
    public Func<int, int, (bool Road, int SlopePm)>? Ground { get; set; }
    /// <summary>Whether a piece already stands at a cell that the book does not know (a generated building in the chunks). Null: nothing does.</summary>
    public Func<int, int, int, bool>? Standing { get; set; }
    /// <summary>Rain (tenths of a mm an hour) and temperature (C) of a game hour at the site. Null: dry, 12 C.</summary>
    public Func<long, (int RainTenths, int TempC)>? Weather { get; set; }
    /// <summary>A person's level in a skill; null: the person table, 0 for others.</summary>
    public Func<EntityId, Skill, int>? SkillOf { get; set; }
    /// <summary>A skill used for an hour (the crafting skill book's rule): person, skill, d x 100, difficulty, game second.</summary>
    public Action<EntityId, Skill, int, int, long>? Train { get; set; }
    public ITileSink? Tiles { get; set; }
    /// <summary>The job table and its environment, for haul jobs; null: no hauling.</summary>
    public Func<(JobEnv Env, JobTable Table)?>? Jobs { get; set; }
    /// <summary>Where a person's pay goes (the hero's purse, else the household's).</summary>
    public Func<EntityId, PurseId?>? PayeeOf { get; set; }
    /// <summary>Who stands on these cells (cell keys) besides the crews the world knows: the hero in the live tier. Person, cell.</summary>
    public Func<IReadOnlyCollection<long>, IEnumerable<(EntityId Person, int X, int Y, int Z)>>? PeopleOn { get; set; }
    /// <summary>A person falls (fall wounds through the resolver): person, height in mm, event id, game second.</summary>
    public Action<EntityId, int, ulong, long>? Fall { get; set; }
    /// <summary>A person landed on another cell (the live tier moves the hero).</summary>
    public Action<EntityId, int, int, int>? Moved { get; set; }
    /// <summary>A dwelling stands on (x, y) (a structure RoomDetector v2 calls one): the land improves the claim there; returns the claim serial, or -1 when none.</summary>
    public Func<int, int, int, int>? DwellingBuilt { get; set; }
    /// <summary>The chronicle of the current batch (set by the caller: the system's outbox or the desk's chronicle).</summary>
    public ConstructionChronicle? Chronicle { get; set; }
    /// <summary>Names of people for the view (crew rows).</summary>
    public Func<EntityId, string>? NameOf { get; set; }

    private readonly Dictionary<long, EntityId> _ghostAt = [];
    private readonly Dictionary<EntityId, GhostSet> _siteGhosts = [];      // P5-D: the ghosts themselves, by id (no book lookup per ghost)
    /// <summary>P5-D: the sites in id order as a flat list (the engine walks it every quantum; ids only grow, so a new site is appended).</summary>
    private readonly List<ConstructionSite> _siteList = [];
    private long _builtVersion;

    public ConstructionWorld(ConstructionBook book, EconomyState econ, PieceCosts costs, int regionId = 0)
    {
        Book = book; Econ = econ; Costs = costs; RegionId = regionId;
        Support = new SupportRules(costs) { AnyBeams = () => _beams > 0 || _planBeams };
        Rooms = costs.Library.NewWorld();
        Reindex();
    }

    /// <summary>Rebuilds the indexes and the live RoomWorld from the book (the load path): the built cells, the saved structure ids, then a full room detection.</summary>
    public void Reindex()
    {
        _ghostAt.Clear(); _siteGhosts.Clear(); _siteList.Clear(); _siteList.AddRange(Book.Sites.Values);
        foreach (var s in Book.Sites.Keys) _siteGhosts[s] = new GhostSet();
        foreach (var g in Book.Ghosts.Values)
        {
            if (!g.Live) continue;
            _ghostAt[g.SlotKey] = g.Id;
            if (!_siteGhosts.TryGetValue(g.Site, out var set)) _siteGhosts[g.Site] = set = new GhostSet();
            set.Add(g);
        }
        Rooms = Costs.Library.NewWorld();
        _built.Clear(); _beams = 0;
        foreach (var b in Book.Built.Values) { _built[b.SlotKey] = b; if (b.Slot == GhostSlot.Structure && Costs.Get(b.Piece)?.Kind == PieceKind.Beam) _beams++; }
        var edits = new List<CellEdit>();
        foreach (var b in Book.Built.Values) edits.Add(new CellEdit(b.X, b.Y, b.Z, b.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, b.Piece));
        if (edits.Count > 0)
        {
            Rooms.Apply(edits);
            Rooms.RestoreStructures(Book.Structures, Book.NextStructureId);
            Rooms.RebuildAll();
        }
        Rooms.NextStructureId = Book.NextStructureId;
        _builtVersion++;
    }

    // ------------------------------------------------------------------ cells

    public BuiltCell? BuiltAt(int x, int y, int z, GhostSlot slot = GhostSlot.Structure) => _built.GetValueOrDefault(ConstructionIds.SlotKey(x, y, z, slot));

    // the built cells hashed for the support checks (the book keeps them sorted for the save); beams counted so a world without any skips the beam scan
    private readonly Dictionary<long, BuiltCell> _built = [];
    private bool _planBeams;
    private int _beams;
    public bool AnyBeams => _beams > 0;
    private void AddBuilt(BuiltCell b)
    {
        Book.Built[b.SlotKey] = b; _built[b.SlotKey] = b;
        if (b.Slot == GhostSlot.Structure && Costs.Get(b.Piece)?.Kind == PieceKind.Beam) _beams++;
    }
    private bool RemoveBuilt(long key, out BuiltCell b)
    {
        if (!Book.Built.Remove(key, out b!)) return false;
        _built.Remove(key);
        if (b.Slot == GhostSlot.Structure && Costs.Get(b.Piece)?.Kind == PieceKind.Beam) _beams--;
        return true;
    }
    public Ghost? GhostAt(int x, int y, int z, GhostSlot slot = GhostSlot.Structure) => _ghostAt.TryGetValue(ConstructionIds.SlotKey(x, y, z, slot), out var id) ? Book.Ghosts.GetValueOrDefault(id) : null;
    public IEnumerable<Ghost> GhostsOf(EntityId site) => _siteGhosts.TryGetValue(site, out var set) ? set.AsEnumerable() : [];
    private static readonly GhostSet NoGhosts = new();
    /// <summary>P5-D: a site's ghosts for the engine's hot loops (walked by index, no allocation).</summary>
    internal GhostSet SetOf(EntityId site) => _siteGhosts.TryGetValue(site, out var set) ? set : NoGhosts;
    public ConstructionSite? Site(EntityId id) => Book.Sites.GetValueOrDefault(id);
    public Store PileOf(ConstructionSite s) => Econ.OpenStore(StoreKind.Open, s.Pile);

    /// <summary>The built structure piece at a cell (what a crew can rest on now).</summary>
    public PieceCost? BuiltStructure(int x, int y, int z) => _built.TryGetValue(ConstructionIds.SlotKey(x, y, z, GhostSlot.Structure), out var b) ? b.CostCache ??= Costs.Get(b.Piece) : null;   // P5-D: the cost cached on the cell

    private CellAt BuiltView(HashSet<long>? without = null) => (x, y, z) =>
    {
        if (without != null && without.Contains(ConstructionIds.CellKey(x, y, z))) return null;
        return BuiltStructure(x, y, z);
    };

    /// <summary>The cells as they will be when every build ghost is finished (and the batch being placed): what placement checks.</summary>
    private CellAt PlannedView(Dictionary<long, PieceCost>? batch) => (x, y, z) =>
    {
        long k = ConstructionIds.CellKey(x, y, z);
        if (batch != null && batch.TryGetValue(k, out var p)) return p;
        if (BuiltStructure(x, y, z) is { } b) return b;
        if (GhostAt(x, y, z) is { Work: GhostWork.Build } g) return Costs.Get(g.Piece);
        return null;
    };

    private static int Day(long sec) => (int)(sec / 86400);

    private void Chron(long sec, string type, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
        => Chronicle?.Invoke(new GameTime(sec), type, subjects, causes, data);

    private void Event(ConstructionEventKind k, long sec, int x, int y, int z, string piece, EntityId site)
    {
        Book.Events.Add(new ConstructionEvent(++Book.EventSerial, k, sec, x, y, z, piece, site));
        if (Book.Events.Count > 64) Book.Events.RemoveRange(0, Book.Events.Count - 64);
    }

    // ------------------------------------------------------------------ placing (spec 4.1)

    /// <summary>
    /// Places ghosts (spec 4.1). Each cell is checked against the land rule, the ground (no piece over a road, no ground floor on a slope over 100 pm), what is already there (a built piece or
    /// a ghost: deconstruct first), the level (0 to +3), a stair's upper cell and support when finished (rest and load rules, with the planned ghosts and this batch counted as built).
    /// Refused cells are listed with their reason; with <paramref name="allOrNothing"/> (a plan) one refusal places nothing. The ghosts join <paramref name="siteId"/>, or the owner's
    /// open site within 24 m, or a new site whose pile is at <paramref name="pile"/> (default: the free ground cell beside the footprint nearest its first cell).
    /// </summary>
    public PlaceResult Place(EntityId owner, PurseKind ownerKind, int household, IReadOnlyList<PlaceCell> cells, long nowSec, EntityId siteId = default, bool allOrNothing = false,
        string blueprint = "", (int X, int Y)? pile = null, EntityId payer = default, PurseKind payerKind = PurseKind.Person)
    {
        if (cells.Count == 0) return PlaceResult.No("cn_nothing_placed");
        _planBeams = true;                                   // the planned cells may hold beams: support checks scan for them
        try { return PlaceCore(owner, ownerKind, household, cells, nowSec, siteId, allOrNothing, blueprint, pile, payer, payerKind); }
        finally { _planBeams = false; }
    }

    private PlaceResult PlaceCore(EntityId owner, PurseKind ownerKind, int household, IReadOnlyList<PlaceCell> cells, long nowSec, EntityId siteId, bool allOrNothing,
        string blueprint, (int X, int Y)? pile, EntityId payer, PurseKind payerKind)
    {
        var refused = new List<PlaceRefusal>();
        var batch = new Dictionary<long, PieceCost>();
        var batchFix = new HashSet<long>();
        var ok = new List<(PlaceCell C, PieceCost Cost)>();
        EntityId deed = default;
        foreach (var c in cells)
        {
            var cost = Costs.Get(c.Piece);
            void No(string key, params object[] args) => refused.Add(new PlaceRefusal(c.X, c.Y, c.Z, c.Piece, key, args));
            if (cost == null || !cost.HasBuild) { No("cn_unknown_piece", c.Piece); continue; }
            if (c.Z > Rules.MaxLevel) { No("cn_too_high", Rules.MaxLevel); continue; }
            if (c.Z < 0) { No("cn_below_ground"); continue; }
            if (May != null)
            {
                var m = May(owner, household, LandAction.Build, c.X, c.Y);
                if (!m.Allowed) { No(m.Why.Length > 0 ? m.Why : "cn_not_yours", m.HolderName); continue; }
                if (deed == default) deed = m.Deed;
            }
            if (c.Z == 0 && Ground?.Invoke(c.X, c.Y) is { } gr)
            {
                if (gr.Road && cost.Kind != PieceKind.Ground) { No("cn_on_road"); continue; }
                if (gr.SlopePm > Rules.MaxSlopePm && cost.Kind is PieceKind.Floor or PieceKind.Ground) { No("cn_too_steep", gr.SlopePm, Rules.MaxSlopePm); continue; }
            }
            var slot = cost.Slot;
            long sk = ConstructionIds.SlotKey(c.X, c.Y, c.Z, slot), ck = ConstructionIds.CellKey(c.X, c.Y, c.Z);
            if (_built.ContainsKey(sk) || _ghostAt.ContainsKey(sk) || (slot == GhostSlot.Structure ? batch.ContainsKey(ck) : batchFix.Contains(ck))) { No("cn_occupied"); continue; }
            if (slot == GhostSlot.Structure && BuiltAt(c.X, c.Y, c.Z) == null && Standing?.Invoke(c.X, c.Y, c.Z) == true) { No("cn_occupied"); continue; }
            if (slot == GhostSlot.Fixture)
            {
                var floor = batch.GetValueOrDefault(ck) ?? BuiltStructure(c.X, c.Y, c.Z) ?? (GhostAt(c.X, c.Y, c.Z) is { Work: GhostWork.Build } fg ? Costs.Get(fg.Piece) : null);
                if (floor == null || !PieceCatalog.IsRoomKind(floor.Kind)) { No("cn_needs_floor"); continue; }
                batchFix.Add(ck);
                ok.Add((c, cost));
                continue;
            }
            batch[ck] = cost;
            ok.Add((c, cost));
        }
        // the checks that read the whole batch: a stair's upper cell, and support when everything planned is finished
        var at = PlannedView(batch);
        foreach (var (c, cost) in ok.ToList())
        {
            if (cost.Slot != GhostSlot.Structure) continue;
            string? key = null; object[] args = [];
            if (cost.Kind == PieceKind.StairsUp)
            {
                var up = at(c.X, c.Y, c.Z + 1);
                if (c.Z + 1 > Rules.MaxLevel || (up != null && up.Kind != PieceKind.StairsDown)) key = "cn_stairs_blocked";
            }
            if (key == null)
            {
                var v = Support.Check(c.X, c.Y, c.Z, cost, (x, y, z) => x == c.X && y == c.Y && z == c.Z ? null : at(x, y, z));
                if (v.Fault == SupportFault.Load) { key = "cn_load"; args = [Costs.Get(v.Weakest ?? "")?.Name ?? v.Weakest ?? "", v.WeakestCarries]; }
                else if (v.Fault != SupportFault.None) key = v.Fault == SupportFault.Jetty ? "cn_no_jetty" : "cn_no_support";
            }
            if (key == null) continue;
            refused.Add(new PlaceRefusal(c.X, c.Y, c.Z, c.Piece, key, args));
            ok.Remove((c, cost));
            batch.Remove(ConstructionIds.CellKey(c.X, c.Y, c.Z));
        }
        if (ok.Count == 0 || (allOrNothing && refused.Count > 0))
            return new PlaceResult(false, default, [], refused, refused.Count > 0 ? refused[0].Key : "cn_nothing_placed", refused.Count > 0 ? refused[0].Args : []);
        // the site
        ConstructionSite? site = siteId != default ? Site(siteId) : null;
        if (site != null && (!site.Open || (site.Owner != owner && May != null && !May(owner, household, LandAction.Build, site.PileX, site.PileY).Allowed))) site = null;
        site ??= blueprint.Length == 0 ? NearSite(owner, ok[0].C.X, ok[0].C.Y) : null;
        if (site == null)
        {
            var (px, py) = pile ?? DefaultPile(ok.Select(o => (o.C.X, o.C.Y)).ToList());
            site = NewSite(owner, ownerKind, payer == default ? owner : payer, payer == default ? ownerKind : payerKind, deed, px, py, blueprint, nowSec);
        }
        var made = new List<EntityId>();
        foreach (var (c, cost) in ok)
        {
            var g = new Ghost
            {
                Id = ConstructionIds.Make(RegionId, ConstructionIds.GhostSlotId, Book.Take()), Site = site.Id, X = c.X, Y = c.Y, Z = c.Z, Piece = c.Piece, Slot = cost.Slot, Rot = c.Rot,
                Work = GhostWork.Build, State = GhostState.Planned, WorkNeed = Math.Max(1, cost.WorkUnits), PlacedSec = nowSec,
            };
            AddGhost(g);
            made.Add(g.Id);
        }
        site.GhostsPlaced += made.Count;
        if (site.State == SiteState.Finished) site.State = SiteState.Active;
        Refresh(site);
        Publish(nowSec);
        return new PlaceResult(true, site.Id, made, refused, refused.Count == 0 ? "cn_placed" : "cn_placed_some", [made.Count, refused.Count]);
    }

    /// <summary>The cells of a plan placed at (ox, oy) on level 0 (spec 4.2's fitting is build mode's; here the caller gives the corner and the transform), committed in one command.</summary>
    public PlaceResult PlacePlan(EntityId owner, PurseKind ownerKind, int household, string blueprint, int ox, int oy, int quarterTurns, bool mirror, long nowSec, (int X, int Y)? pile = null)
    {
        BuildingGrid g;
        try { g = Costs.Library.Grid(blueprint).Transform(quarterTurns, mirror); }
        catch (Exception) { return PlaceResult.No("cn_unknown_plan", blueprint); }
        var cells = Costs.PlanCells(g).Select(c => new PlaceCell(ox + c.X, oy + c.Y, c.Z, c.Piece)).ToList();
        return Place(owner, ownerKind, household, cells, nowSec, default, allOrNothing: true, blueprint, pile);
    }

    private ConstructionSite? NearSite(EntityId owner, int x, int y)
    {
        foreach (var s in Book.Sites.Values)
            if (s.Open && s.Owner == owner && Math.Max(Math.Abs(s.PileX - x), Math.Abs(s.PileY - y)) <= 24) return s;
        return null;
    }

    /// <summary>The pile's default cell: the free ground cell on the ring around the footprint nearest the first cell, (y, x) order on ties.</summary>
    private (int X, int Y) DefaultPile(List<(int X, int Y)> cells)
    {
        int x0 = cells.Min(c => c.X) - 1, x1 = cells.Max(c => c.X) + 1, y0 = cells.Min(c => c.Y) - 1, y1 = cells.Max(c => c.Y) + 1;
        var (fx, fy) = cells[0];
        var best = (X: x0, Y: y1); long bestD = long.MaxValue;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (x != x0 && x != x1 && y != y0 && y != y1) continue;
                if (BuiltAt(x, y, 0) != null || GhostAt(x, y, 0) != null) continue;
                if (Ground?.Invoke(x, y) is { Road: true }) continue;
                long d = (long)(x - fx) * (x - fx) + (long)(y - fy) * (y - fy);
                if (d < bestD) { bestD = d; best = (x, y); }
            }
        return best;
    }

    private ConstructionSite NewSite(EntityId owner, PurseKind ownerKind, EntityId payer, PurseKind payerKind, EntityId deed, int px, int py, string blueprint, long nowSec)
    {
        var s = new ConstructionSite
        {
            Id = ConstructionIds.Make(RegionId, ConstructionIds.SiteSlot, Book.Take()), Region = RegionId, Owner = owner, OwnerKind = ownerKind, Payer = payer, PayerKind = payerKind,
            Deed = deed, PileX = px, PileY = py, PileZ = 0, Blueprint = blueprint, CreatedSec = nowSec,
        };
        Book.Sites[s.Id] = s; _siteList.Add(s);
        _siteGhosts[s.Id] = new GhostSet();
        PileOf(s);
        Book.Placed[s.Pile] = new PlacedStore(s.Pile, px, py, 0);
        if (Book.AdvancedSec < 0 || !Busy()) Book.AdvancedSec = nowSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        Chron(nowSec, "site_started", [owner, s.Id], [new Cause(CauseKind.Other, null, blueprint.Length > 0 ? "a plan placed" : "ghosts placed")], ("blueprint", blueprint), ("x", px.ToString()), ("y", py.ToString()));
        return s;
    }

    private void AddGhost(Ghost g)
    {
        Book.Ghosts[g.Id] = g;
        _ghostAt[g.SlotKey] = g.Id;
        if (!_siteGhosts.TryGetValue(g.Site, out var set)) _siteGhosts[g.Site] = set = new GhostSet();
        set.Add(g);
    }

    private void DropGhost(Ghost g)
    {
        if (_ghostAt.TryGetValue(g.SlotKey, out var id) && id == g.Id) _ghostAt.Remove(g.SlotKey);
        if (_siteGhosts.TryGetValue(g.Site, out var set)) set.Remove(g.Id);
        Book.Ghosts.Remove(g.Id);
        if (Econ.OtherStores.TryGetValue(g.Id, out var st) && st.RowCount == 0) Econ.OtherStores.Remove(g.Id);
        foreach (var s in Book.Sites.Values) foreach (var m in s.Crew) if (m.Ghost == g.Id) m.Ghost = default;
    }

    /// <summary>Removes a ghost (right-click, spec 4.1): one in progress gives its materials back to the pile and loses its work.</summary>
    public ConstructionResult Cancel(EntityId by, int household, int x, int y, int z, GhostSlot slot, long nowSec)
    {
        var g = GhostAt(x, y, z, slot);
        if (g == null) return ConstructionResult.No("cn_no_ghost");
        var s = Site(g.Site)!;
        if (s.Owner != by && May != null && !May(by, household, LandAction.Build, x, y).Allowed) return ConstructionResult.No("cn_not_your_site");
        CancelGhost(g);
        Refresh(s);
        Publish(nowSec);
        return ConstructionResult.Yes("cn_ghost_removed", Costs.Get(g.Piece)?.Name ?? g.Piece);
    }

    private void CancelGhost(Ghost g)
    {
        var s = Site(g.Site);
        if (s != null && g.Materialised && g.Work == GhostWork.Build && Econ.OtherStores.TryGetValue(g.Id, out var st))
            foreach (var (good, qty) in st.Rows()) Store.Move(st, PileOf(s), good, qty);
        g.State = GhostState.Cancelled;
        DropGhost(g);
    }

    /// <summary>
    /// Marks a built piece to be taken down (spec 4.1 Deconstruct): a ghost that does half the piece's work and, when finished, gives its salvage share back to the pile and the rest as
    /// rubble. Warns when the piece holds something up ("The floor above will fall").
    /// </summary>
    public ConstructionResult MarkDeconstruct(EntityId by, PurseKind byKind, int household, int x, int y, int z, GhostSlot slot, long nowSec)
    {
        var b = BuiltAt(x, y, z, slot);
        if (b == null) return ConstructionResult.No("cn_nothing_built_here");
        if (_ghostAt.ContainsKey(b.SlotKey)) return ConstructionResult.No("cn_occupied");
        if (slot == GhostSlot.Structure && BuiltAt(x, y, z, GhostSlot.Fixture) != null) return ConstructionResult.No("cn_fixture_first");
        if (May != null && !May(by, household, LandAction.Demolish, x, y).Allowed) return ConstructionResult.No("cn_not_yours", May(by, household, LandAction.Demolish, x, y).HolderName);
        var site = Site(b.Site);
        if (site is { Open: false } && site.Owner == by && site.State == SiteState.Finished) site.State = SiteState.Active;     // the site that built it takes it down again
        if (site is not { Open: true }) site = NearSite(by, x, y) ?? NewSite(by, byKind, by, byKind, May?.Invoke(by, household, LandAction.Demolish, x, y).Deed ?? default, x, y + 1, "", nowSec);
        if (Book.AdvancedSec < 0 || !Busy()) Book.AdvancedSec = nowSec / Rules.QuantumSeconds * Rules.QuantumSeconds;
        var cost = Costs[b.Piece];
        var g = new Ghost
        {
            Id = ConstructionIds.Make(RegionId, ConstructionIds.GhostSlotId, Book.Take()), Site = site.Id, X = x, Y = y, Z = z, Piece = b.Piece, Slot = slot, Work = GhostWork.Deconstruct,
            State = GhostState.Ready, WorkNeed = Math.Max(1, cost.WorkUnits / 2), Materialised = true, PlacedSec = nowSec,
        };
        AddGhost(g);
        site.GhostsPlaced++;
        if (site.State == SiteState.Finished) site.State = SiteState.Active;
        bool falls = slot == GhostSlot.Structure && Doomed([ConstructionIds.CellKey(x, y, z)], []).Count > 0;
        Refresh(site);
        Publish(nowSec);
        return new ConstructionResult(true, falls ? "cn_deconstruct_will_fall" : "cn_deconstruct_marked", [cost.Name], 0, g.Id);
    }

    // ------------------------------------------------------------------ ghost states (spec 3.2)

    /// <summary>A ghost's piece cost (cached on the ghost: derived, the same after a load).</summary>
    public PieceCost CostOf(Ghost g) => g.CostCache ??= Costs[g.Piece];

    private readonly Dictionary<PieceCost, (ushort Good, long Grams)[]> _goodIds = [];
    /// <summary>A piece's materials as good ids of this economy.</summary>
    public (ushort Good, long Grams)[] MaterialIds(PieceCost c)
    {
        if (_goodIds.TryGetValue(c, out var ids)) return ids;
        return _goodIds[c] = [.. c.Materials.Select(m => (Econ.Goods.TryIdOf(m.Good, out var g) ? g : ushort.MaxValue, m.Grams))];
    }

    /// <summary>Whether the pile holds every material of a piece.</summary>
    public bool PileHas(ConstructionSite s, PieceCost c)
    {
        var ids = MaterialIds(c);
        if (ids.Length == 0) return true;
        var pile = PileOf(s);
        foreach (var (g, grams) in ids)
            if (g == ushort.MaxValue || pile.Qty(g) < grams) return false;
        return true;
    }

    // P5-D: one pick or refresh asks the pile about each kind of piece once (the answer cannot change inside it)
    private long _memoStamp;
    private Store? _memoPile;
    private bool PileHasMemo(ConstructionSite s, PieceCost c)
    {
        if (c.PileMemoStamp == _memoStamp) return c.PileMemo;
        var ids = MaterialIds(c);
        bool ok = true;
        if (ids.Length > 0)
        {
            var pile = _memoPile ??= PileOf(s);
            foreach (var (g, grams) in ids) if (g == ushort.MaxValue || pile.Qty(g) < grams) { ok = false; break; }
        }
        c.PileMemoStamp = _memoStamp; c.PileMemo = ok;
        return ok;
    }
    private void NewMemo() { _memoStamp++; _memoPile = null; }

    /// <summary>Recomputes the states of a site's ghosts: what is ready (support built, materials in the pile), what waits and why, and the site's own pause.</summary>
    public void Refresh(ConstructionSite s) => Refresh(s, null);

    /// <summary>
    /// <see cref="Refresh(ConstructionSite)"/> after pieces were only added at <paramref name="built"/> (cell keys): a finished piece can only hold more up, so support is checked again
    /// only for the ghosts that waited on it near those cells; materials for all.
    /// </summary>
    public void Refresh(ConstructionSite s, IReadOnlyList<long>? built)
    {
        Touch(s);
        NewMemo();
        // a new piece helps only what is on its level or the one above, within the longest reach (a beam's, and the jetty beside a beam's end)
        int near = Math.Max(Rules.SupportReachTiles, Rules.BeamReachTiles) + Rules.JettyReachTiles;
        // P5-D: with no beam anywhere, a finished piece holds up only the level above it within the support reach (an upright: wall, post, door or window), or the fixture on its own
        // floor; everything else it cannot help. With a beam about, the wider rule above.
        bool wide = AnyBeams || _planBeams;
        int reach = Rules.SupportReachTiles;
        bool Near(Ghost g)
        {
            for (int bi = 0; bi < built!.Count; bi++)
            {
                var (x, y, z) = ConstructionIds.Unpack(built[bi]);
                int dx = Math.Abs(g.X - x), dy = Math.Abs(g.Y - y);
                if (wide) { if (g.Z - z is >= 0 and <= 1 && dx <= near && dy <= near) return true; continue; }
                if (g.Z == z && dx == 0 && dy == 0) return true;
                if (g.Z == z + 1 && dx <= reach && dy <= reach && BuiltStructure(x, y, z) is { } p && (PieceCosts.IsUpright(p.Kind) || p.Kind == PieceKind.Beam)) return true;
            }
            return false;
        }
        var at = BuiltView();
        bool anyLive = false, anyReady = false, anyMaterials = false;
        DiagX[3] += SetOf(s.Id).Count;
        foreach (var g in SetOf(s.Id))
        {
            anyLive = true;
            if (g.Work == GhostWork.Deconstruct) { g.Block = GhostBlock.None; g.State = g.WorkDone > 0 ? GhostState.InProgress : GhostState.Ready; anyReady = true; continue; }
            var cost = CostOf(g);
            GhostBlock block = GhostBlock.None;
            bool supportKnown = built != null && g.State != GhostState.Planned && (g.Block != GhostBlock.Support || !Near(g));
            if (supportKnown) block = g.Block == GhostBlock.Support ? GhostBlock.Support : GhostBlock.None;
            else if (g.Slot == GhostSlot.Fixture) { if (BuiltStructure(g.X, g.Y, g.Z) is not { } f || !PieceCatalog.IsRoomKind(f.Kind)) block = GhostBlock.Support; }
            else { DiagX[2]++; if (!Support.Check(g.X, g.Y, g.Z, cost, at).Ok) block = GhostBlock.Support; }
            if (block == GhostBlock.None && !g.Materialised && !PileHasMemo(s, cost)) block = GhostBlock.Materials;
            g.Block = block;
            g.State = block != GhostBlock.None ? GhostState.Blocked : g.WorkDone > 0 || g.Materialised ? GhostState.InProgress : GhostState.Ready;
            if (block == GhostBlock.None) anyReady = true;
            if (block == GhostBlock.Materials) anyMaterials = true;
        }
        if (!anyLive)
        {
            if (s.State == SiteState.Active || s.State == SiteState.Paused) s.State = SiteState.Finished;
            if (s.Pause != PauseReason.Owner) s.Pause = PauseReason.None;
            return;
        }
        if (s.Pause is PauseReason.Owner or PauseReason.NoWages) return;
        s.Pause = !anyReady && anyMaterials ? PauseReason.NoMaterials : PauseReason.None;
    }
}
