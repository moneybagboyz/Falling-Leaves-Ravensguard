using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

// P5-A3: flood fill, classification, purpose gaps, facts, blueprint loading and hashing (the pure part of RoomDetector v2).
public sealed partial class RoomWorld
{
    /// <summary>1,000 litres per m2 of a storage room (spec 4.4: 2,000 L x 500 pm).</summary>
    public const int LitresPerM2 = 1000;
    /// <summary>A bed needs about this many free declared floor cells.</summary>
    public const int CellsPerBed = BlueprintValidator.CellsPerBed;

    private RoomV2 Flood(long start, RoomChange? change)
    {
        int z = Unpack(start).Z;
        var room = new RoomV2 { Z = z };
        var cells = new List<long> { start };
        _roomAt[start] = room;
        bool enclosed = true;
        var tags = new Dictionary<string, int>(StringComparer.Ordinal);
        int beds = 0, free = 0;
        var purposes = new SortedSet<ushort>();
        var doors = new SortedDictionary<long, bool>();
        var walls = new HashSet<long>();
        long floorValue = 0, furnValue = 0, wallValue = 0;
        var stations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cells.Count; i++)
        {
            var key = cells[i];
            var (x, y, _) = Unpack(key);
            var c = _cells[key];
            if (c.Purpose != 0) { purposes.Add(c.Purpose); if (c.Fixture == null) free++; }
            foreach (var p in (ReadOnlySpan<PieceInfo?>)[c.Structure, c.Fixture])
            {
                if (p == null) continue;
                beds += p.Beds;
                foreach (var t in p.Tags) tags[t] = tags.GetValueOrDefault(t) + 1;
                if (p.Kind == PieceKind.Floor) floorValue += p.ValueMites;
                else if (p.Kind is PieceKind.Furniture or PieceKind.Workstation) furnValue += p.ValueMites;
                if (p.Station != null) stations[p.Station] = stations.GetValueOrDefault(p.Station) + 1;
            }
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy;
                var nk = Key(nx, ny, z);
                if (!_cells.TryGetValue(nk, out var nc) || nc.Structure == null || nc.Structure.Kind == PieceKind.Ground) { enclosed = false; continue; }
                var kind = nc.Structure.Kind;
                if (kind == PieceKind.Door && !doors.ContainsKey(nk)) doors[nk] = IsExteriorDoor(nx, ny, z);
                if (kind is PieceKind.Wall or PieceKind.Window or PieceKind.Door or PieceKind.Post && walls.Add(nk)) wallValue += nc.Structure.ValueMites;
                if (PieceCatalog.IsRoomKind(kind))
                {
                    if (_roomAt.TryGetValue(nk, out var other))
                    {
                        if (other == room) continue;
                        RemoveRoom(other, change);
                    }
                    _roomAt[nk] = room; cells.Add(nk);
                }
            }
        }
        cells.Sort();
        room.CellKeys = cells.ToArray();
        int area = cells.Count;
        room.Enclosed = enclosed;
        room.Indoor = enclosed && cells.All(k => Roofed(k));
        room.Tags = tags;
        room.Beds = beds;
        room.FreeDeclaredCells = free;
        room.Doors = doors.Select(d => { var (dx, dy, dz) = Unpack(d.Key); return new RoomDoor(new Cell3(dx, dy, dz), d.Value); })
            .OrderBy(d => d.Cell.Y).ThenBy(d => d.Cell.X).ToList();
        room.DeclaredPurposes = purposes.Select(p => _purposes[p]).OrderBy(s => s, StringComparer.Ordinal).ToList();
        room.Stations = stations;
        room.ValuePerM2 = (int)((furnValue + (floorValue + wallValue) / 2) / area);
        room.Matches = _types.Where(t => Satisfies(t, room)).ToList();
        var primary = room.Primary;
        if (primary?.StorageKind != null) { room.StorageKind = primary.StorageKind; room.StorageL = (long)area * LitresPerM2; }
        room.PurposeGaps = Gaps(room);
        return room;
    }

    /// <summary>Spec 4.1: the cell above holds a floor, a roof or any other structure piece (a wall on a loft level covers too), or the cell is underground.</summary>
    private bool Roofed(long key)
    {
        var (x, y, z) = Unpack(key);
        if (z < 0) return true;
        return _cells.TryGetValue(Key(x, y, z + 1), out var up) && up.Structure is { } p && p.Kind != PieceKind.Ground;
    }

    private bool IsExteriorDoor(int x, int y, int z)
    {
        foreach (var (dx, dy) in N4)
        {
            var kind = _cells.TryGetValue(Key(x + dx, y + dy, z), out var c) ? c.Structure?.Kind : null;
            if (kind == PieceKind.Ground || (kind == null && z == 0)) return true;
        }
        return false;
    }

    private static bool Satisfies(RoomTypeDef t, RoomV2 r)
    {
        if (t.Enclosed && !r.Enclosed) return false;
        if (r.Area < t.MinArea || (t.MaxArea > 0 && r.Area > t.MaxArea)) return false;
        if (t.Roofed == false) { if (r.Indoor) return false; }
        else if ((t.Roofed == true || t.Enclosed) && !r.Indoor) return false;
        if (t.MinValue > 0 && r.ValuePerM2 < t.MinValue) return false;
        foreach (var q in t.Requires)
            if (r.Tags.GetValueOrDefault(q.Tag) < q.Min) return false;
        return true;
    }

    /// <summary>Spec 4.6: what the best candidate type for the declared purpose still lacks.</summary>
    private string[] Gaps(RoomV2 r)
    {
        if (r.DeclaredPurposes.Count != 1 || r.PurposeMatches) return [];
        string[]? best = null;
        foreach (var t in _types.Where(t => BuildingLibrary.PurposeMatches(r.DeclaredPurposes[0], t.Purpose)))
        {
            var gaps = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var q in t.Requires) if (r.Tags.GetValueOrDefault(q.Tag) < q.Min) gaps.Add(q.Tag);
            if (t.Enclosed && !r.Enclosed) gaps.Add("walls");
            else if (t.Roofed == true || (t.Roofed == null && t.Enclosed)) { if (!r.Indoor) gaps.Add("roof"); }
            if (r.Area < t.MinArea) gaps.Add("area");
            if (t.MinValue > 0 && r.ValuePerM2 < t.MinValue) gaps.Add("value");
            if (best == null || gaps.Count < best.Length) best = gaps.ToArray();
        }
        return best ?? [];
    }

    // ---- facts (cached per structure, spec 3.3) ----

    public BuildingFacts? Facts(int structure)
    {
        if (_facts.TryGetValue(structure, out var f)) return f;
        if (!_members.ContainsKey(structure)) return null;
        return _facts[structure] = ComputeFacts(structure);
    }

    public BuildingFacts? FactsAt(int x, int y, int z) => StructureAt(x, y, z) is var s and > 0 ? Facts(s) : null;

    public IEnumerable<BuildingFacts> AllFacts() => StructureIds.Select(i => Facts(i)!);

    public int StructureOf(RoomV2 r) => _structOf.GetValueOrDefault(r.CellKeys[0]);

    private static readonly HashSet<string> BedPurposes = new(["sleeping", "solar", "loft", "guest"], StringComparer.Ordinal);

    private BuildingFacts ComputeFacts(int id)
    {
        var mem = _members[id];
        var rooms = _rooms.Where(r => _structOf.GetValueOrDefault(r.CellKeys[0]) == id).OrderBy(r => r.CellKeys[0]).ToList();
        var levels = new SortedSet<int>();
        var foot = new HashSet<(int, int)>();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var k in mem)
        {
            var (x, y, z) = Unpack(k);
            levels.Add(z);
            if (foot.Add((x, y))) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        }
        int w = maxX - minX + 1, h = maxY - minY + 1;
        int beds = 0, capacity = 0, hearth = 0;
        bool livestock = false, highSeat = false;
        var storage = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var workshops = new List<(RoomV2, IReadOnlyDictionary<string, int>)>();
        var entrances = new SortedSet<(int Z, int Y, int X)>();
        var best = ValueTier.Mean;
        foreach (var r in rooms)
        {
            var p = r.Primary;
            if (r.ValueTier > best) best = r.ValueTier;
            if (r.StorageKind != null) storage[r.StorageKind] = storage.GetValueOrDefault(r.StorageKind) + r.StorageL;
            foreach (var d in r.Doors) if (d.Exterior) entrances.Add((d.Cell.Z, d.Cell.Y, d.Cell.X));
            if (r.Indoor && r.DeclaredPurposes.Count == 1 && r.DeclaredPurposes[0] == "sleeping") capacity += r.Beds + r.FreeDeclaredCells / CellsPerBed;
            if (r.DeclaredPurposes.Contains("livestock")) livestock = true; // a byre still to be furnished counts
            if (p == null) continue;
            if (p.Purpose.StartsWith("work:", StringComparison.Ordinal)) workshops.Add((r, new SortedDictionary<string, int>(r.Stations, StringComparer.Ordinal)));
            if (p.Purpose == "livestock") livestock = true;
            if (!r.Indoor) continue;
            if (p.Id == "great_hall" || (p.Purpose == "gathering" && r.Tags.GetValueOrDefault("high_seat") > 0)) highSeat = true;
            if (p.Purpose == "hearth") hearth++;
            else if (p.Purpose == "gathering" && r.Tags.GetValueOrDefault("hearth") > 0) hearth++;
            if (!BedPurposes.Contains(p.Purpose)) continue;
            beds += r.Beds;
            if (!(r.DeclaredPurposes.Count == 1 && r.DeclaredPurposes[0] == "sleeping")) capacity += r.Beds;
        }
        // P5-B (decided in step 2, recorded in build-mode-and-construction Notes): a house with no Indoor sleeping room at all sleeps round its hearth. A one-room hovel with a
        // hearth and pallets houses its poor: the beds of its Indoor hearth rooms count (the cottage's type stays; its value tier, Mean, says how poor). A house that has a
        // bedroom still counts only the bedroom's beds, so nothing a v1 blueprint or RD-5 decided moves.
        if (beds == 0 && !rooms.Any(r => r.Indoor && r.Primary is { } pp && BedPurposes.Contains(pp.Purpose)))
            foreach (var r in rooms)
                if (r.Indoor && r.Primary is { } pp && (pp.Purpose == "hearth" || (pp.Purpose == "gathering" && r.Tags.GetValueOrDefault("hearth") > 0))) { beds += r.Beds; capacity += r.Beds; }
        var entranceCells = entrances.Select(e => new Cell3(e.X, e.Y, e.Z)).ToList();
        bool dwelling = hearth > 0 && beds >= 2 && entranceCells.Count > 0;
        int above = levels.Count(z => z >= 0);
        var type = DwellingKind.None;
        if (dwelling)
        {
            int len = Math.Max(w, h), wid = Math.Min(w, h);
            if (highSeat) type = DwellingKind.Manor;
            else if (above >= 3 && foot.Count <= 64) type = DwellingKind.Tower;
            else if (len * 2 >= wid * 5 && livestock) type = DwellingKind.Longhouse;
            else if (OnTownLot != null && wid <= 12 && OnTownLot(minX, minY, maxX, maxY)) type = DwellingKind.Burgage;
            else type = DwellingKind.Cottage;
        }
        var hash = new Fnv();
        hash.Add(dwelling ? 1 : 0); hash.Add((int)type); hash.Add(beds); hash.Add(capacity); hash.Add(hearth); hash.Add((int)best);
        hash.Add(w); hash.Add(h); hash.Add(foot.Count); hash.Add(levels.Count);
        foreach (var z in levels) hash.Add(z);
        foreach (var (k, v) in storage) { hash.Add(k); hash.Add(v); }
        foreach (var e in entrances) { hash.Add(e.X); hash.Add(e.Y); hash.Add(e.Z); }
        foreach (var r in rooms)
        {
            hash.Add(r.CellKeys[0]); hash.Add(r.Area); hash.Add(r.Enclosed ? 1 : 0); hash.Add(r.Indoor ? 1 : 0); hash.Add(r.Beds);
            hash.Add(r.Primary?.Id ?? ""); hash.Add(r.ValuePerM2); hash.Add(r.StorageL);
            foreach (var (k, v) in r.Stations) { hash.Add(k); hash.Add(v); }
            foreach (var g in r.PurposeGaps) hash.Add(g);
            foreach (var d in r.Doors) { hash.Add(d.Cell.X); hash.Add(d.Cell.Y); hash.Add(d.Cell.Z); hash.Add(d.Exterior ? 1 : 0); }
        }
        return new BuildingFacts
        {
            Structure = id, IsDwelling = dwelling, DwellingKind = type, Beds = beds, BedCapacity = capacity, HearthRooms = hearth,
            Workshops = workshops, StorageL = storage, Rooms = rooms, BestValueTier = best, Entrances = entranceCells,
            Levels = levels.ToList(), FootprintW = w, FootprintH = h, FootprintArea = foot.Count, Hash = hash.Value,
        };
    }

    /// <summary>The RoomHash for the daily trail: facts of every structure, in id order (ids included unless asked).</summary>
    public long FactsHash(bool includeIds = true)
    {
        var h = new Fnv();
        foreach (var f in includeIds ? AllFacts() : AllFacts().OrderBy(f => _members[f.Structure].Min()))
        {
            if (includeIds) h.Add(f.Structure);
            h.Add(f.Hash);
        }
        return h.Value;
    }

    // ---- blueprints and the "Roof over" tool ----

    /// <summary>Writes a blueprint into the world at an offset. With <paramref name="roofed"/> the finished roof is laid too (a blueprint is a finished building).</summary>
    public RoomChange LoadBlueprint(BuildingGrid g, int ox, int oy, bool roofed = true, string roofPiece = "roof_thatch")
    {
        var edits = new List<CellEdit>();
        foreach (var (z, lv) in g.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    if (lv.Structure[i] != null) edits.Add(new CellEdit(ox + x, oy + y, z, CellLayer.Structure, lv.Structure[i]));
                    if (lv.Fixture[i] != null) edits.Add(new CellEdit(ox + x, oy + y, z, CellLayer.Fixture, lv.Fixture[i]));
                    if (lv.Letter[i] != 0 && g.Rooms.TryGetValue(lv.Letter[i], out var purpose)) edits.Add(new CellEdit(ox + x, oy + y, z, CellLayer.Purpose, purpose));
                }
        var change = Apply(edits);
        if (roofed) Merge(change, RoofOver(roofPiece, change.Touched.ToList()));
        return change;
    }

    private static void Merge(RoomChange into, RoomChange more)
    {
        into.Structures.AddRange(more.Structures);
        into.RoomsAdded += more.RoomsAdded; into.RoomsRemoved += more.RoomsRemoved;
        foreach (var t in more.Touched) into.Touched.Add(t);
    }

    /// <summary>The "Roof over" tool (build spec 4.x): a roof piece over every cell of an enclosed room that has nothing above it (all structures when none are named).</summary>
    public RoomChange RoofOver(string roofPiece, IReadOnlyCollection<int>? structures = null)
    {
        var edits = new List<CellEdit>();
        foreach (var r in Rooms)
        {
            if (!r.Enclosed) continue;
            if (structures != null && !structures.Contains(StructureOf(r))) continue;
            foreach (var k in r.CellKeys)
            {
                var (x, y, z) = Unpack(k);
                if (z < 0 || (_cells.TryGetValue(Key(x, y, z + 1), out var up) && up.Structure != null)) continue;
                edits.Add(new CellEdit(x, y, z + 1, CellLayer.Structure, roofPiece));
            }
        }
        return Apply(edits);
    }

    /// <summary>Canonical text of every room (identity, cells, flags, tags, doors, types, value, stations, storage, gaps): what incremental and full detection must agree on.</summary>
    public string Dump()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in Rooms)
        {
            sb.Append($"room z={r.Z} cells={string.Join(',', r.CellKeys)} enc={r.Enclosed} in={r.Indoor} beds={r.Beds} val={r.ValuePerM2} storage={r.StorageKind}:{r.StorageL}");
            sb.Append(" tags=").Append(string.Join(',', r.Tags.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => $"{t.Key}={t.Value}")));
            sb.Append(" doors=").Append(string.Join(',', r.Doors.Select(d => $"{d.Cell.X}.{d.Cell.Y}.{d.Cell.Z}{(d.Exterior ? "e" : "i")}")));
            sb.Append(" match=").Append(string.Join(',', r.Matches.Select(m => m.Id)));
            sb.Append(" decl=").Append(string.Join(',', r.DeclaredPurposes));
            sb.Append(" stn=").Append(string.Join(',', r.Stations.Select(s => $"{s.Key}={s.Value}")));
            sb.Append(" gaps=").Append(string.Join(',', r.PurposeGaps)).Append(" free=").Append(r.FreeDeclaredCells).Append('\n');
        }
        return sb.ToString();
    }

    private struct Fnv
    {
        public ulong H;
        public readonly long Value => (long)(H == 0 ? 14695981039346656037UL : H);
        private void Mix(ulong v) { H = (H == 0 ? 14695981039346656037UL : H); for (int i = 0; i < 8; i++) { H ^= (v >> (8 * i)) & 0xFF; H *= 1099511628211UL; } }
        public void Add(long v) => Mix((ulong)v);
        public void Add(int v) => Mix((ulong)(long)v);
        public void Add(string s) { Mix((ulong)s.Length); foreach (var c in s) Mix(c); }
    }
}
