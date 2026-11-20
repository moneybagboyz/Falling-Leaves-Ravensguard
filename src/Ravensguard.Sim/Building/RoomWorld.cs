using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

/// <summary>
/// P5-A3 (roomdetector-v2.md): the live structure of a region (walls, doors, floors, roofs and fixtures on every level), the structures
/// they form, the rooms they enclose and the cached <see cref="BuildingFacts"/>. <see cref="Apply"/> re-detects only what a change can reach
/// (spec 4.2); <see cref="RebuildAll"/> is the full detection the incremental result must equal. Pure: no RNG, integer maths, (z, y, x) order.
/// </summary>
public sealed partial class RoomWorld
{
    private struct Cell
    {
        public PieceInfo? Structure;
        public PieceInfo? Fixture;
        public ushort Purpose;
        public readonly bool Empty => Structure == null && Fixture == null && Purpose == 0;
    }

    private static readonly (int dx, int dy)[] N4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int dx, int dy, int dz)[] N6 = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private readonly Dictionary<string, PieceInfo> _info = new(StringComparer.Ordinal);
    private readonly RoomTypeDef[] _types;
    private readonly Dictionary<long, Cell> _cells = [];
    private readonly Dictionary<long, RoomV2> _roomAt = [];
    private readonly List<string> _purposes = [""];
    private readonly Dictionary<string, ushort> _purposeIdx = new(StringComparer.Ordinal);
    private readonly Dictionary<long, int> _structOf = [];
    private readonly Dictionary<int, HashSet<long>> _members = [];
    private readonly Dictionary<int, BuildingFacts> _facts = [];
    private readonly HashSet<RoomV2> _rooms = [];
    private List<RoomV2>? _sorted;
    private int _nextStructure = 1;

    public PieceCatalog Pieces { get; }
    /// <summary>Whether a ground cell has water on it (slipways and the like); null = none known.</summary>
    public Func<int, int, bool>? WaterAt { get; set; }
    /// <summary>Whether a footprint lies on a town lot (burgage plots); null = never.</summary>
    public Func<int, int, int, int, bool>? OnTownLot { get; set; }

    public RoomWorld(BuildingLibrary lib) : this(lib.Pieces, lib.RoomTypes, lib.MaterialMitesPerKg, lib.PieceStation) { }

    public RoomWorld(PieceCatalog pieces, IEnumerable<RoomTypeDef> roomTypes, IReadOnlyDictionary<string, long>? mitesPerKg = null, IReadOnlyDictionary<string, string>? pieceStation = null)
    {
        Pieces = pieces;
        _types = roomTypes.OrderByDescending(t => t.Priority).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
        foreach (var p in pieces.All.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            long perKg = p.Material != null && mitesPerKg != null && mitesPerKg.TryGetValue(p.Material, out var m) ? m : 0;
            int kg = p.Kg > 0 ? p.Kg : RoomValue.DefaultKg(p.Kind);
            string? station = null;
            if (pieceStation != null) pieceStation.TryGetValue(p.Id, out station);
            else if (p.Kind == PieceKind.Workstation) station = p.Station;
            _info[p.Id] = new PieceInfo { Id = p.Id, Kind = p.Kind, Tags = PieceCatalog.TagsOf(p).ToArray(), Beds = p.Beds, Station = station, ValueMites = kg * perKg };
        }
    }

    // ---- keys ----

    private const int Bias = 1 << 19;
    /// <summary>(z, y, x) ordered cell key.</summary>
    public static long Key(int x, int y, int z) => ((long)(z + 128) << 42) | ((long)(y + Bias) << 21) | (long)(x + Bias);
    public static (int X, int Y, int Z) Unpack(long k) => ((int)(k & 0x1FFFFF) - Bias, (int)((k >> 21) & 0x1FFFFF) - Bias, (int)(k >> 42) - 128);

    public int CellCount => _cells.Count;
    /// <summary>Every cell that holds a piece: key, structure piece id and fixture id (in no particular order; the caller sorts).</summary>
    public IEnumerable<(long Key, string? Structure, string? Fixture)> CellPieces()
    {
        foreach (var (k, c) in _cells) if (c.Structure != null || c.Fixture != null) yield return (k, c.Structure?.Id, c.Fixture?.Id);
    }
    public PieceInfo? PieceAt(int x, int y, int z) => _cells.TryGetValue(Key(x, y, z), out var c) ? c.Structure : null;
    public PieceInfo? FixtureAt(int x, int y, int z) => _cells.TryGetValue(Key(x, y, z), out var c) ? c.Fixture : null;
    public string? PurposeAt(int x, int y, int z) => _cells.TryGetValue(Key(x, y, z), out var c) && c.Purpose != 0 ? _purposes[c.Purpose] : null;

    private PieceInfo? Info(string? id) => id == null ? null : _info.TryGetValue(id, out var i) ? i : throw new ArgumentException($"unknown piece '{id}'");

    private ushort PurposeId(string? p)
    {
        if (string.IsNullOrEmpty(p)) return 0;
        if (_purposeIdx.TryGetValue(p, out var i)) return i;
        i = (ushort)_purposes.Count; _purposes.Add(p); _purposeIdx[p] = i;
        return i;
    }

    // ---- editing ----

    public RoomChange SetPiece(int x, int y, int z, string? pieceId) => Apply([new CellEdit(x, y, z, CellLayer.Structure, pieceId)]);
    public RoomChange SetFixture(int x, int y, int z, string? pieceId) => Apply([new CellEdit(x, y, z, CellLayer.Fixture, pieceId)]);
    public RoomChange SetPurpose(int x, int y, int z, string? purpose) => Apply([new CellEdit(x, y, z, CellLayer.Purpose, purpose)]);

    /// <summary>Applies changes and re-detects only the rooms they can reach (spec 4.2); returns what changed.</summary>
    public RoomChange Apply(IReadOnlyList<CellEdit> edits)
    {
        var change = new RoomChange();
        var seeds = new HashSet<long>();
        foreach (var e in edits)
        {
            var key = Key(e.X, e.Y, e.Z);
            _cells.TryGetValue(key, out var cell);
            bool wasMember = IsMember(cell.Structure);
            switch (e.Layer)
            {
                case CellLayer.Structure: cell.Structure = Info(e.Value); break;
                case CellLayer.Fixture: cell.Fixture = Info(e.Value); break;
                default: cell.Purpose = PurposeId(e.Value); break;
            }
            if (cell.Empty) _cells.Remove(key); else _cells[key] = cell;
            bool isMember = IsMember(cell.Structure);
            if (wasMember != isMember) UpdateMembership(key, isMember, change);
            TouchAround(key, change);
            // a change reaches rooms within two cells on its level (walls, then the exterior flag of a door next to it) and the room below (roofing)
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2 + Math.Abs(dy); dx <= 2 - Math.Abs(dy); dx++)
                    seeds.Add(Key(e.X + dx, e.Y + dy, e.Z));
            seeds.Add(Key(e.X, e.Y, e.Z - 1));
        }
        Redetect(seeds, change);
        return change;
    }

    private static bool IsMember(PieceInfo? p) => p != null && p.Kind != PieceKind.Ground;

    private void TouchAround(long key, RoomChange change)
    {
        var (x, y, z) = Unpack(key);
        if (_structOf.TryGetValue(key, out var own)) Invalidate(own, change);
        foreach (var (dx, dy, dz) in N6)
            if (_structOf.TryGetValue(Key(x + dx, y + dy, z + dz), out var s)) Invalidate(s, change);
    }

    private void Invalidate(int structure, RoomChange? change)
    {
        _facts.Remove(structure);
        change?.Touched.Add(structure);
    }

    private void Redetect(HashSet<long> seedSet, RoomChange change)
    {
        var seeds = seedSet.ToList();
        seeds.Sort();
        var dirty = new List<RoomV2>();
        foreach (var s in seeds)
            if (_roomAt.TryGetValue(s, out var r) && !dirty.Contains(r)) dirty.Add(r);
        foreach (var r in dirty) RemoveRoom(r, change);
        foreach (var s in seeds)
            if (!_roomAt.ContainsKey(s) && IsRoomCell(s)) AddRoom(Flood(s, change), change);
        _sorted = null;
    }

    private void RemoveRoom(RoomV2 r, RoomChange? change)
    {
        foreach (var k in r.CellKeys) _roomAt.Remove(k);
        _rooms.Remove(r);
        if (_structOf.TryGetValue(r.CellKeys[0], out var s)) Invalidate(s, change);
        if (change != null) change.RoomsRemoved++;
    }

    private void AddRoom(RoomV2 r, RoomChange? change)
    {
        _rooms.Add(r);
        if (_structOf.TryGetValue(r.CellKeys[0], out var s)) Invalidate(s, change);
        if (change != null) change.RoomsAdded++;
    }

    private bool IsRoomCell(long key) => _cells.TryGetValue(key, out var c) && c.Structure != null && PieceCatalog.IsRoomKind(c.Structure.Kind);

    // ---- structures (connected pieces; 6-connectivity) ----

    private void UpdateMembership(long key, bool isMember, RoomChange change)
    {
        var (x, y, z) = Unpack(key);
        if (isMember)
        {
            var ids = new SortedSet<int>();
            foreach (var (dx, dy, dz) in N6)
                if (_structOf.TryGetValue(Key(x + dx, y + dy, z + dz), out var s)) ids.Add(s);
            int id;
            if (ids.Count == 0)
            {
                id = _nextStructure++;
                _members[id] = [];
                change.Structures.Add(new StructureEvent(StructureEventKind.Created, id, 0));
            }
            else
            {
                id = ids.Min;
                foreach (var other in ids.Skip(1))
                {
                    foreach (var k in _members[other]) { _structOf[k] = id; _members[id].Add(k); }
                    _members.Remove(other);
                    Invalidate(other, change);
                    change.Structures.Add(new StructureEvent(StructureEventKind.Joined, id, other));
                    // rooms of the absorbed structure keep their cells; only facts move
                }
            }
            _structOf[key] = id; _members[id].Add(key);
            Invalidate(id, change);
            return;
        }
        if (!_structOf.Remove(key, out var old)) return;
        var set = _members[old];
        set.Remove(key);
        Invalidate(old, change);
        if (set.Count == 0)
        {
            _members.Remove(old);
            change.Structures.Add(new StructureEvent(StructureEventKind.Removed, old, 0));
            return;
        }
        var nbrs = new List<long>();
        foreach (var (dx, dy, dz) in N6)
        {
            var nk = Key(x + dx, y + dy, z + dz);
            if (_structOf.TryGetValue(nk, out var s) && s == old) nbrs.Add(nk);
        }
        if (nbrs.Count < 2) return;
        if (StillOnePiece(nbrs, old)) return;                       // P5-I: the neighbours still meet: nothing split (found without walking the whole structure)
        var comps = new List<(long Min, List<long> Cells)>();
        var seen = new HashSet<long>();
        foreach (var start in nbrs)
        {
            if (seen.Contains(start)) continue;
            var cells = new List<long> { start }; seen.Add(start);
            long min = start;
            for (int i = 0; i < cells.Count; i++)
            {
                var (cx, cy, cz) = Unpack(cells[i]);
                foreach (var (dx, dy, dz) in N6)
                {
                    var nk = Key(cx + dx, cy + dy, cz + dz);
                    if (_structOf.TryGetValue(nk, out var s) && s == old && seen.Add(nk)) { cells.Add(nk); if (nk < min) min = nk; }
                }
            }
            comps.Add((min, cells));
        }
        if (comps.Count < 2) return;
        comps.Sort((a, b) => a.Min.CompareTo(b.Min));
        // the part with the lowest cell keeps the id; the others take new ids in order
        for (int i = 1; i < comps.Count; i++)
        {
            int id = _nextStructure++;
            var mem = new HashSet<long>(comps[i].Cells);
            foreach (var k in mem) { _structOf[k] = id; set.Remove(k); }
            _members[id] = mem;
            Invalidate(id, change);
            change.Structures.Add(new StructureEvent(StructureEventKind.Split, old, id));
        }
    }

    /// <summary>
    /// P5-I: whether the neighbours of a removed cell are still one piece of structure <paramref name="id"/>. Walks outward from each neighbour in step (breadth first) and joins two walks
    /// where they meet; true as soon as all are joined (a floor with one cell gone is joined in a few steps, not after a walk of the whole floor), false when a walk runs out alone (a real
    /// split: the caller then partitions the structure as before, so ids stay what they were).
    /// </summary>
    private bool StillOnePiece(List<long> starts, int id)
    {
        int n = starts.Count;
        var owner = new Dictionary<long, int>(); var queues = new List<long>[n]; var head = new int[n]; var parent = new int[n];
        for (int i = 0; i < n; i++) { owner[starts[i]] = i; queues[i] = [starts[i]]; parent[i] = i; }
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        int groups = n;
        while (true)
        {
            bool progressed = false;
            for (int i = 0; i < n; i++)
            {
                if (head[i] >= queues[i].Count) continue;
                progressed = true;
                var (cx, cy, cz) = Unpack(queues[i][head[i]++]);
                foreach (var (dx, dy, dz) in N6)
                {
                    var nk = Key(cx + dx, cy + dy, cz + dz);
                    if (!_structOf.TryGetValue(nk, out var s) || s != id) continue;
                    if (owner.TryGetValue(nk, out var o))
                    {
                        int a = Find(i), b = Find(o);
                        if (a != b) { parent[a] = b; if (--groups == 1) return true; }
                    }
                    else { owner[nk] = i; queues[i].Add(nk); }
                }
            }
            if (!progressed) return false;
            // a walk that has run out without meeting the others is a whole piece of its own
            for (int i = 0; i < n; i++)
                if (head[i] >= queues[i].Count)
                {
                    int g = Find(i); bool alive = false;
                    for (int j = 0; j < n && !alive; j++) alive = Find(j) == g && head[j] < queues[j].Count;
                    if (!alive && groups > 1) return false;
                }
        }
    }

    public int StructureAt(int x, int y, int z) => _structOf.TryGetValue(Key(x, y, z), out var s) ? s : 0;
    public IEnumerable<int> StructureIds => _members.Keys.OrderBy(i => i);
    public IReadOnlyCollection<long> StructureCells(int id) => _members.TryGetValue(id, out var m) ? m : [];
    /// <summary>Next structure id that will be handed out (saved by the build step so ids stay stable).</summary>
    public int NextStructureId { get => _nextStructure; set => _nextStructure = Math.Max(_nextStructure, value); }

    /// <summary>The structure partition recomputed from scratch: each part as its sorted cell keys (the incremental registry must equal it).</summary>
    public List<long[]> ComputePartition()
    {
        var seen = new HashSet<long>();
        var parts = new List<long[]>();
        foreach (var start in _cells.Where(c => IsMember(c.Value.Structure)).Select(c => c.Key).OrderBy(k => k))
        {
            if (!seen.Add(start)) continue;
            var cells = new List<long> { start };
            for (int i = 0; i < cells.Count; i++)
            {
                var (cx, cy, cz) = Unpack(cells[i]);
                foreach (var (dx, dy, dz) in N6)
                {
                    var nk = Key(cx + dx, cy + dy, cz + dz);
                    if (_cells.TryGetValue(nk, out var c) && IsMember(c.Structure) && seen.Add(nk)) cells.Add(nk);
                }
            }
            cells.Sort();
            parts.Add(cells.ToArray());
        }
        return parts;
    }

    /// <summary>The registry's partition in the same canonical form.</summary>
    public List<long[]> RegistryPartition() =>
        _members.Values.Select(m => m.OrderBy(k => k).ToArray()).OrderBy(a => a[0]).ToList();

    // ---- full detection ----

    /// <summary>Forgets every room and floods the whole world again, in (z, y, x) order. Structures stay as they are.</summary>
    public void RebuildAll()
    {
        _roomAt.Clear(); _rooms.Clear(); _facts.Clear(); _sorted = null;
        foreach (var k in _cells.Where(c => c.Value.Structure != null && PieceCatalog.IsRoomKind(c.Value.Structure.Kind)).Select(c => c.Key).OrderBy(k => k))
            if (!_roomAt.ContainsKey(k)) AddRoom(Flood(k, null), null);
    }

    /// <summary>Rebuilds structures too, from scratch (ids by lowest cell): the loader after a save and the blueprint path.</summary>
    public void RebuildStructures()
    {
        _structOf.Clear(); _members.Clear(); _facts.Clear();
        foreach (var part in ComputePartition())
        {
            int id = _nextStructure++;
            _members[id] = new HashSet<long>(part);
            foreach (var k in part) _structOf[k] = id;
        }
    }

    /// <summary>
    /// P5-B: the load path of a save that keeps structure ids (build-mode-and-construction 3.3): the partition as saved (each structure id with its cells), then the counter. Cells
    /// that the parts do not name get ids after <paramref name="nextId"/> as <see cref="RebuildStructures"/> would; rooms are left for <see cref="RebuildAll"/>.
    /// </summary>
    public void RestoreStructures(IEnumerable<(int Id, long[] Cells)> parts, int nextId)
    {
        _structOf.Clear(); _members.Clear(); _facts.Clear();
        foreach (var (id, cells) in parts)
        {
            var set = new HashSet<long>();
            foreach (var k in cells) if (_cells.TryGetValue(k, out var c) && IsMember(c.Structure)) { set.Add(k); _structOf[k] = id; }
            if (set.Count > 0) _members[id] = set;
        }
        _nextStructure = Math.Max(nextId, _members.Count == 0 ? 1 : _members.Keys.Max() + 1);
        foreach (var part in ComputePartition())
        {
            var loose = part.Where(k => !_structOf.ContainsKey(k)).ToArray();
            if (loose.Length == 0) continue;
            int id = _nextStructure++;
            _members[id] = new HashSet<long>(loose);
            foreach (var k in loose) _structOf[k] = id;
        }
    }

    /// <summary>P5-B: every structure with its cells (ascending id, cells ascending): what a save keeps so ids stay stable across a load.</summary>
    public List<(int Id, long[] Cells)> StructureParts() => _members.OrderBy(m => m.Key).Select(m => (m.Key, m.Value.OrderBy(k => k).ToArray())).ToList();

    /// <summary>A copy of the cells with structures and rooms found by full detection: what <see cref="Apply"/> must always equal.</summary>
    public RoomWorld FullTwin()
    {
        var t = new RoomWorld(Pieces, _types, null, null);
        foreach (var (k, v) in _info) t._info[k] = v;
        foreach (var (k, v) in _cells) t._cells[k] = v;
        foreach (var p in _purposes.Skip(1)) t.PurposeId(p);
        t.WaterAt = WaterAt; t.OnTownLot = OnTownLot;
        t.RebuildStructures(); t.RebuildAll();
        return t;
    }

    public IReadOnlyList<RoomV2> Rooms => _sorted ??= _rooms.OrderBy(r => r.CellKeys[0]).ToList();
    public RoomV2? RoomAt(int x, int y, int z) => _roomAt.TryGetValue(Key(x, y, z), out var r) ? r : null;
    public RoomV2? RoomAt(Cell3 c) => RoomAt(c.X, c.Y, c.Z);
}
