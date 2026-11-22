using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P7-SG-A, walls and breaches (sieges-and-engines 3.1, 4.1): every cell of a wall, gate or tower is a piece with hit points, and a blow of any kind wears it down by the one rule (<see cref="WallDamage"/>). A generated wall (the civic
/// blueprints placed by world generation) keeps its damage in its chunk's <see cref="ChunkDelta"/>: one object per chunk level, <c>siege_damage</c>, whose property lists the damaged cells and the downed ones (with the piece that stood
/// there, for repair), and a cell at 0 hit points becomes rubble by <c>SetTile</c> (a ground piece at ground level, simply gone above it). A wall construction built keeps its hit points in <see cref="BuiltCell.Hp"/> and goes
/// through the Phase 5 collapse path at 0. Nothing here is saved separately: the chunk store and the construction book are the save, so a breach persists as the ID-keyed deltas the format already has (no section, no version bump).
/// What a breach undermines falls: the wall-walk and parapet rest on the wall under them (the plan 2.2a rule, strict here: the reach of neighbouring wall does not carry them), and what loses support through the usual rules goes with
/// them; people on the falling cells drop with real fall wounds. Integer only; every walk is in (z, y, x) order. No draw is made, so no stream is used (the sieges' 0x7C0..0x7CF are the engines' and the assault's).
/// Repair labour and materials are the economy's (a later part): <see cref="RepairNeed"/> says what a cell takes and <see cref="Repair"/> restores it when the caller has paid.
/// </summary>
public sealed partial class WallWorld
{
    /// <summary>The reserved id space of the per-chunk damage objects (<see cref="EntityId.ReservedSpace"/> is a 7-bit field; 40 is the sieges').</summary>
    public const int DamageSpace = 40;
    public const string Prop = "siege_damage";

    public ChunkStore Store { get; }
    public PiecePalette Palette { get; }
    public PieceCosts Costs { get; }
    public WallTable Walls { get; }
    public SupportRules Support { get; }
    public ulong Seed { get; }

    // ---- hooks (the wiring sets what the host has; every one may stay null) ----
    /// <summary>The construction world, when the walls include ones the player built: their hit points are the built cells' and they go through its collapse path.</summary>
    public ConstructionWorld? Construction { get; set; }
    /// <summary>The fire's (or the street's) room world, kept in step with the chunk tiles so a burnt gate is a gap to the fire too.</summary>
    public RoomWorld? Rooms { get; set; }
    /// <summary>The people standing on any of these cells (cell keys of <see cref="ConstructionIds.CellKey"/>), as (person, x, y, z).</summary>
    public Func<IReadOnlySet<long>, IEnumerable<(EntityId Person, int X, int Y, int Z)>>? PeopleOn { get; set; }
    /// <summary>A person falls this many millimetres (the event id and game second for the wound draw): <see cref="FallWounds.Apply"/> in the game.</summary>
    public Action<EntityId, int, ulong, long>? Fall { get; set; }
    public SiegeChronicle? Chronicle { get; set; }
    /// <summary>P7-SG-C: what a beam behind a gate adds to a cell's impact threshold (kg m/s); null: nothing is reinforced.</summary>
    public Func<int, int, int, int>? Reinforce { get; set; }
    /// <summary>Counts every write the walls made to the chunk store (a cell fell, was mended); a reader that caches what the cells are (the assault's walls) drops its cache when it changes.</summary>
    public int Version { get; private set; }

    public WallWorld(ChunkStore store, PiecePalette palette, PieceCosts costs, WallTable walls, ulong seed)
    {
        Store = store; Palette = palette; Costs = costs; Walls = walls; Seed = seed;
        Support = new SupportRules(costs);
    }

    public WallWorld(ChunkStore store, ContentDatabase db, ulong seed) : this(store, new PiecePalette(PieceCosts.Of(db).Library.Pieces), PieceCosts.Of(db), WallTable.Of(db), seed) { }

    /// <summary>Hooks the fire in: a wall piece burnt out becomes rubble and what it held falls, and the room world the fire sees is the one these walls keep in step.</summary>
    public void Hook(FireWorld fire)
    {
        Rooms = fire.Rooms;
        fire.PieceBurnt = (x, y, z, piece, cause, sec) => OnPieceBurnt(x, y, z, piece, cause, sec);
    }

    /// <summary>The game's fall wounds: a person who falls takes the Phase 4 fall rules through the one resolver onto the wound book.</summary>
    public void UseWounds(ContentDatabase content, Func<Ravensguard.Sim.Combat.Wounds.WoundLedger?> ledger)
        => Fall = (person, mm, ev, sec) => FallWounds.Apply(content, ledger(), person, mm, ev, sec, Seed);

    // ------------------------------------------------------------------ the chunk store's cells

    private static int Div(int a) => ChunkStreamer.FloorDiv(a, ChunkContent.Size);
    private static ChunkKey KeyOf(int x, int y, int z) => new(Div(x), Div(y), z);
    private static int Local(int x, int y) => (y - Div(y) * ChunkContent.Size) * ChunkContent.Size + x - Div(x) * ChunkContent.Size;

    /// <summary>The id of the <c>siege_damage</c> object of a chunk level: stable, a function of the key alone.</summary>
    public static ulong ObjectId(ChunkKey k)
    {
        ulong n = ((ulong)(k.Z - ChunkKey.MinZ) & 0xF) << 36 | ((ulong)(k.Y + (1 << 17)) & 0x3FFFF) << 18 | ((ulong)(k.X + (1 << 17)) & 0x3FFFF);
        return EntityId.Reserved(IdKind.Chunk, 0, (EntityId.ReservedSpace)DamageSpace, n).Value;
    }

    private readonly Dictionary<ChunkKey, (int Ops, ChunkContent? Content)> _cache = [];

    private ChunkContent? Content(ChunkKey k)
    {
        if (!k.IsValid) return null;
        int ops = Store.DeltaOpCount(k);
        if (_cache.TryGetValue(k, out var hit) && hit.Ops == ops && (hit.Content != null || !Store.Contains(k))) return hit.Content;
        if (_cache.Count > 96) _cache.Clear();
        var c = Store.Rebuild(k);
        _cache[k] = (ops, c);
        return c;
    }

    /// <summary>Whether the place was visited (its ground chunk is in the store): only visited places are written to, and never silently changed otherwise.</summary>
    public bool Visited(int x, int y) => Store.GetSnapshot(new ChunkKey(Div(x), Div(y), 0)) != null;

    public int CodeAt(int x, int y, int z) => Content(KeyOf(x, y, z)) is { } c ? c.Tiles.GetValueOrDefault(Local(x, y)) : 0;
    /// <summary>The structure piece of a cell of the chunk store (what generation and the deltas say), or null.</summary>
    public string? StructureAt(int x, int y, int z) => Palette.Id(TileCode.Structure(CodeAt(x, y, z)));
    private string? FixtureAt(int x, int y, int z) => Palette.Id(TileCode.Fixture(CodeAt(x, y, z)));

    public WallHits? HitsOf(ChunkKey k) => Content(k) is { } c && c.Objects.TryGetValue(ObjectId(k), out var o) && o.Props.TryGetValue(Prop, out var v) && v.Length > 0
        ? SaveCodec.Deserialize<WallHits>(v) : null;

    // ------------------------------------------------------------------ the hit points of a cell

    /// <summary>The wall-like piece at a cell (built or generated) with its hit points left and its full hit points; null when the cell holds no wall piece (or only rubble).</summary>
    public (string Piece, int Hp, int Max)? HpAt(int x, int y, int z)
    {
        if (Construction?.BuiltAt(x, y, z) is { } b) return Walls.Stat(b.Piece) == null ? null : (b.Piece, b.Hp, PieceCosts.BuiltHp(Costs[b.Piece], b.BuiltSkill));
        var pid = StructureAt(x, y, z);
        if (Walls.Stat(pid) == null || Costs.Get(pid!) is not { } cost) return null;
        int lost = HitsOf(KeyOf(x, y, z)) is { } h && h.Find(Local(x, y)) is { Down: false } e ? e.Lost : 0;
        return (pid!, Math.Max(0, cost.Hp - lost), cost.Hp);
    }

    // ------------------------------------------------------------------ the work of one operation (batched writes)

    /// <summary>The edits of one operation: the working copy of each chunk level's hit list and the tile writes, appended together per chunk level by <see cref="Commit"/> (one delta a chunk level per operation, all or nothing).</summary>
    private sealed class Work
    {
        public readonly SortedDictionary<ChunkKey, WallHits> Hits = [];
        public readonly SortedDictionary<ChunkKey, HashSet<ulong>> Touched = [];
        public readonly SortedDictionary<ChunkKey, List<ChunkOp>> Tiles = [];
        public readonly List<CellEdit> Rooms = [];
    }

    private WallHits HitsFor(Work w, ChunkKey k)
    {
        if (!w.Hits.TryGetValue(k, out var h)) w.Hits[k] = h = HitsOf(k)?.Clone() ?? new WallHits();
        return h;
    }

    /// <summary>Writes a cell's structure (and clears its fixture) in the working set: <paramref name="structure"/> null empties the cell.</summary>
    private void SetCell(Work w, int x, int y, int z, string? structure)
    {
        var k = KeyOf(x, y, z);
        int code = CodeAt(x, y, z);
        int next = TileCode.WithFixture(TileCode.WithStructure(code, Palette.Of(structure)), 0);
        if (!w.Tiles.TryGetValue(k, out var l)) w.Tiles[k] = l = [];
        if (next != code) l.Add(ChunkOp.SetTile(x - k.X * ChunkContent.Size, y - k.Y * ChunkContent.Size, next));
        w.Rooms.Add(new CellEdit(x, y, z, CellLayer.Structure, structure));
        if (FixtureAt(x, y, z) != null) w.Rooms.Add(new CellEdit(x, y, z, CellLayer.Fixture, null));
    }

    private void Commit(Work w)
    {
        Version++;
        CommitCore(w);
    }

    private void CommitCore(Work w)
    {
        var keys = new SortedSet<ChunkKey>(w.Hits.Keys);
        foreach (var k in w.Tiles.Keys) keys.Add(k);
        foreach (var k in keys)
        {
            var ops = new List<ChunkOp>();
            ulong oid = ObjectId(k);
            bool had = Content(k) is { } c && c.Objects.ContainsKey(oid);
            if (w.Hits.TryGetValue(k, out var h))
            {
                if (h.Cells.Count == 0) { if (had) ops.Add(ChunkOp.RemoveObject(new EntityId(oid))); }
                else
                {
                    if (!had) ops.Add(ChunkOp.AddObject(new EntityId(oid), []));
                    ops.Add(ChunkOp.SetProperty(new EntityId(oid), Prop, SaveCodec.Serialize(h)));
                }
            }
            if (w.Tiles.TryGetValue(k, out var t)) ops.AddRange(t);
            if (ops.Count > 0) Store.AppendDelta(k, ops);
        }
        if (Rooms != null && w.Rooms.Count > 0) Rooms.Apply(w.Rooms);
    }

    // ------------------------------------------------------------------ a blow (4.1)

    /// <summary>
    /// A blow of <paramref name="momentumKgMs"/> on the cell (x, y, z) at game second <paramref name="sec"/>: the damage is <c>max(0, M - threshold) / 60</c>; at 0 hit points the cell falls (rubble at ground level) and what it held up
    /// goes with it. <paramref name="cause"/> is the chronicle entry the blow belongs to (the siege) and <paramref name="noteKey"/> says how. A cell that is no wall piece, or lies in a place never visited, is left alone.
    /// </summary>
    public WallBlow Strike(int x, int y, int z, long momentumKgMs, long sec, EntityId cause = default, string noteKey = "ch_note_sg_blow")
    {
        if (!Visited(x, y)) return WallBlow.Not;
        if (HpAt(x, y, z) is not { } at) return WallBlow.Not;
        var stat = Walls.Stat(at.Piece)!;
        int dmg = WallDamage.Impact(momentumKgMs, stat.Threshold + (Reinforce?.Invoke(x, y, z) ?? 0), Walls.DivisorOf(stat));
        if (dmg == 0) return new WallBlow(WallBlowKind.NoEffect, 0, at.Hp, []);
        int left = Math.Max(0, at.Hp - dmg);
        var w = new Work();
        if (Construction?.BuiltAt(x, y, z) is { } built) return StrikeBuilt(w, built, stat, dmg, left, sec, cause, noteKey);
        var k = KeyOf(x, y, z);
        var hits = HitsFor(w, k);
        if (left > 0)
        {
            hits.Put(new WallHit { Idx = Local(x, y), Lost = at.Max - left, Piece = at.Piece, Down = false });
            Commit(w);
            return new WallBlow(WallBlowKind.Damaged, dmg, left, []);
        }
        var fell = Collapse(w, [(x, y, z, at.Piece)], sec, cause, noteKey, struckCell: (x, y, z));
        return new WallBlow(WallBlowKind.Destroyed, dmg, 0, fell);
    }

    private WallBlow StrikeBuilt(Work w, BuiltCell b, WallStatDef stat, int dmg, int left, long sec, EntityId cause, string noteKey)
    {
        b.Hp = left;
        if (left > 0) return new WallBlow(WallBlowKind.Damaged, dmg, left, []);
        var before = BreachesNear(b.X, b.Y);
        string piece = b.Piece;
        int max = PieceCosts.BuiltHp(Costs[piece], b.BuiltSkill);
        Construction!.Destroy(b.X, b.Y, b.Z, sec, cause, "breached");            // the Phase 5 collapse path: salvage to the rubble, the support rules, the groan and the fall
        Down(w, b.X, b.Y, b.Z, piece, max, stat);
        Commit(w);
        Announce(before, b.X, b.Y, sec, cause, noteKey);
        return new WallBlow(WallBlowKind.Destroyed, dmg, 0, []);
    }

    /// <summary>
    /// The fire burnt a wall piece out (the fire hook, <see cref="FireWorld.PieceBurnt"/>): the cell is written as the wall leaves it (rubble or gone) and what it held up falls, as for a blow. Does nothing for a piece that is not in the wall table.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Z)> OnPieceBurnt(int x, int y, int z, string piece, EntityId fire, long sec)
    {
        if (Walls.Stat(piece) == null || !Visited(x, y)) return [];
        var w = new Work();
        return Collapse(w, [(x, y, z, piece)], sec, fire, "ch_note_sg_burnt", struckCell: (x, y, z), byFire: true);
    }

    /// <summary>Marks a cell down in the working set and writes its rubble (ground level) or empties it (above).</summary>
    private void Down(Work w, int x, int y, int z, string piece, int lost, WallStatDef? stat)
    {
        var k = KeyOf(x, y, z);
        HitsFor(w, k).Put(new WallHit { Idx = Local(x, y), Lost = lost, Piece = piece, Down = true });
        string? rubble = z == 0 && stat is { Rubble.Length: > 0 } ? stat.Rubble : null;
        SetCell(w, x, y, z, rubble);
    }

    /// <summary>The cells destroyed together: the struck cell down, then what it undermined, the people on it falling, one commit and the chronicle.</summary>
    private IReadOnlyList<(int X, int Y, int Z)> Collapse(Work w, (int X, int Y, int Z, string Piece)[] downed, long sec, EntityId cause, string noteKey, (int X, int Y, int Z) struckCell, bool byFire = false)
    {
        var before = BreachesNear(struckCell.X, struckCell.Y);
        var gone = new HashSet<long>();
        foreach (var (x, y, z, piece) in downed)
        {
            var cost = Costs[piece];
            Down(w, x, y, z, piece, cost.Hp, Walls.Stat(piece));
            gone.Add(ConstructionIds.CellKey(x, y, z));
        }
        var fallen = Undermined(gone);
        var fell = new List<(int X, int Y, int Z)>();
        foreach (var k in fallen)
        {
            var (x, y, z) = ConstructionIds.Unpack(k);
            string piece = StructureAt(x, y, z)!;
            Down(w, x, y, z, piece, Costs[piece].Hp, null);
            fell.Add((x, y, z));
        }
        var all = new HashSet<long>(gone); all.UnionWith(fallen);
        var drop = PeopleDrop(all, fallen, sec);
        Commit(w);
        Announce(before, struckCell.X, struckCell.Y, sec, cause, noteKey);
        if (fell.Count > 0)
            Chronicle?.Invoke(new GameTime(sec), "wall_fell", "ch_sg_fell",
                [ChronParam.Count("x", struckCell.X), ChronParam.Count("y", struckCell.Y), ChronParam.Count("cells", fell.Count), ChronParam.Count("people", drop.Count)],
                [.. drop], [new Cause(byFire ? CauseKind.Fire : CauseKind.Siege, cause == default ? null : cause, "", "ch_note_sg_undermined")],
                [("x", struckCell.X.ToString()), ("y", struckCell.Y.ToString()), ("cells", fell.Count.ToString()), ("people", drop.Count.ToString())]);
        return fell;
    }

    /// <summary>The people on the falling cells drop to the floor below (or the ground) with real fall wounds (3,000 mm a level); the ones that fell, in id order.</summary>
    private List<EntityId> PeopleDrop(HashSet<long> gone, SortedSet<long> fallen, long sec)
    {
        var dropped = new SortedDictionary<ulong, (EntityId P, int X, int Y, int Z)>();
        if (PeopleOn != null)
            foreach (var (p, x, y, z) in PeopleOn(gone)) if (gone.Contains(ConstructionIds.CellKey(x, y, z)) && z > 0) dropped[p.Value] = (p, x, y, z);
        if (fallen.Count == 0 && dropped.Count == 0) return [];
        ulong ev = (ulong)(fallen.Count > 0 ? fallen.Min : gone.Min()) ^ ((ulong)sec * 0x9E3779B97F4A7C15UL);
        int perLevel = Costs.Rules.FallMmPerLevel;
        foreach (var (_, (p, x, y, z)) in dropped)
        {
            int landZ = 0;
            for (int zz = z - 1; zz > 0; zz--)
                if (!gone.Contains(ConstructionIds.CellKey(x, y, zz)) && Costs.Get(StructureAt(x, y, zz) ?? "") is { } c && PieceCatalog.IsRoomKind(c.Kind)) { landZ = zz; break; }
            Fall?.Invoke(p, (z - landZ) * perLevel, ev, sec);
        }
        return [.. dropped.Values.Select(v => v.P)];
    }

    // ------------------------------------------------------------------ what a breach undermines (4.1)

    /// <summary>
    /// The generated cells that stand no more once <paramref name="removed"/> are gone: a wall-walk or parapet piece (a wall-table piece above ground) falls when the cell under it did (it needs the wall under it, not just wall within reach);
    /// any other piece falls when it held under the Phase 5 support rules and no longer does; and what falls is checked around in turn until nothing more does. Cells construction built are never taken here (its own collapse path owns them).
    /// </summary>
    private SortedSet<long> Undermined(HashSet<long> removed)
    {
        var fallen = new SortedSet<long>();
        var gone = new HashSet<long>(removed);
        int r = Costs.Rules.SupportReachTiles + Costs.Rules.JettyReachTiles + 1;
        PieceCost? Now(int x, int y, int z) => gone.Contains(ConstructionIds.CellKey(x, y, z)) ? null : Construction?.BuiltStructure(x, y, z) ?? (StructureAt(x, y, z) is { } s ? Costs.Get(s) : null);
        PieceCost? Before(int x, int y, int z) => Construction?.BuiltStructure(x, y, z) ?? (StructureAt(x, y, z) is { } s ? Costs.Get(s) : null);
        bool more = true;
        while (more)
        {
            more = false;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z0 = int.MaxValue;
            foreach (var k in gone) { var (x, y, z) = ConstructionIds.Unpack(k); x0 = Math.Min(x0, x - r); x1 = Math.Max(x1, x + r); y0 = Math.Min(y0, y - r); y1 = Math.Max(y1, y + r); z0 = Math.Min(z0, Math.Max(1, z)); }
            for (int z = z0; z <= ChunkKey.MaxZ; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long ck = ConstructionIds.CellKey(x, y, z);
                        if (gone.Contains(ck) || !Visited(x, y)) continue;
                        if (Construction?.BuiltAt(x, y, z) != null) continue;
                        if (StructureAt(x, y, z) is not { } id || Costs.Get(id) is not { } c || PieceCosts.IsFixture(c.Kind) || c.Kind == PieceKind.Ground) continue;
                        bool falls = Walls.Stat(id) != null && gone.Contains(ConstructionIds.CellKey(x, y, z - 1))
                            || !Support.Check(x, y, z, c, Now).Ok && Support.Check(x, y, z, c, Before).Ok;
                        if (!falls) continue;
                        gone.Add(ck); fallen.Add(ck); more = true;
                    }
        }
        return fallen;
    }

    // ------------------------------------------------------------------ the chronicle

    private void Announce(List<Breach> before, int x, int y, long sec, EntityId cause, string noteKey)
    {
        if (Chronicle == null) return;
        foreach (var b in BreachesNear(x, y))
        {
            bool known = false;
            foreach (var o in before) if (o.Cells.Any(c => b.Contains(c.X, c.Y))) { known = true; break; }
            if (known || !b.Contains(x, y)) continue;
            var (cx, cy) = b.Cells[0];
            Chronicle(new GameTime(sec), "wall_breached", "ch_sg_breached", [ChronParam.Count("x", cx), ChronParam.Count("y", cy), ChronParam.Count("width", b.WidthM)], [],
                [new Cause(noteKey == "ch_note_sg_burnt" ? CauseKind.Fire : CauseKind.Siege, cause == default ? null : cause, "", noteKey)],
                [("x", cx.ToString()), ("y", cy.ToString()), ("width", b.WidthM.ToString()), ("thickness", b.ThicknessM.ToString())]);
        }
    }
}
