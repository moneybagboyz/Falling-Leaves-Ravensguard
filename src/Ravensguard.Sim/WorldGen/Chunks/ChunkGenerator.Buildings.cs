using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    /// <summary>One lot's building as placed in the world: its cells per level (palette numbers), plus the yard around it. Immutable once built.</summary>
    public sealed class Stamp
    {
        public int Settlement, Lot;
        public string Blueprint = "", Style = "";
        public int X0, Y0, W, H;
        public int Turns; public bool Mirror;
        /// <summary>True when the footprint fits the lot rectangle.</summary>
        public bool Fits;
        public readonly SortedDictionary<int, (int[] Structure, int[] Fixture)> Levels = [];
        public readonly List<(int X, int Y, GroundKind G, int V)> Ground = [];
        public readonly List<(int X, int Y, PropKind K, int V)> Props = [];
        /// <summary>Exterior doors on the street side, world cells.</summary>
        public readonly List<(int X, int Y)> FrontDoors = [];
        public int Cells;
    }

    /// <summary>A prop waiting for a cell: world position, kind and how many tiers of priority (lower wins a contested cell).</summary>
    private readonly record struct PendingProp(int X, int Y, PropKind Kind, byte Variant, int Priority, ushort Extra = 0);
    private readonly record struct FixedProp(int X, int Y, PropKind Kind, byte Variant);

    /// <summary>The placed building of a lot (null for a lot whose blueprint is missing). Exposed for tests and the town view.</summary>
    public Stamp? StampOf(int settlement, int lot) => GetStamp(settlement, lot);

    private static string Fallback(Lot lot) => lot.BlueprintId.Length > 0 ? lot.BlueprintId : lot.Use switch
    {
        LotUse.Church => "chapel", LotUse.Mill => "granary", LotUse.Chapel => "chapel", LotUse.Cottage => "peasant_cottage",
        LotUse.Longhouse => "longhouse", LotUse.Burgage => "burgage_townhouse", LotUse.Manor => "manor_hall", LotUse.Smithy => "smithy",
        LotUse.Tavern => "tavern", LotUse.Barn => "barn", LotUse.Granary => "granary", _ => "",
    };

    private Stamp? GetStamp(int s, int lotIdx) => _stamps.GetOrAdd(((long)s << 32) | (uint)lotIdx, _ => MakeStamp(s, lotIdx));

    private BuildingGrid? GridFor(string bp, string style, int wealth) => _grids.GetOrAdd((bp, style, wealth), k =>
    {
        BlueprintEntry? e = null;
        foreach (var x in Library.Blueprints) if (x.Def.Id == k.Item1) { e = x; break; }
        if (e == null) return null;
        var def = e.Def with { Style = k.Item2, Wealth = (Wealth)Math.Clamp(k.Item3, 0, 2) };
        var issues = new List<BlueprintIssue>();
        return Library.Parse(BlueprintEntry.Of(def), issues) ?? Library.Parse(e, issues);
    });

    private Stamp? MakeStamp(int s, int lotIdx)
    {
        var rs = Region.Settlements[s];
        var lot = rs.Plan.Lots[lotIdx];
        string bp = Fallback(lot);
        if (bp.Length == 0) return null;
        int wealth = lot.Wealth;
        // a household better or worse off than its plot says builds to its means
        if (Population.LotHousehold.Length > s && lotIdx < Population.LotHousehold[s].Length && Population.LotHousehold[s][lotIdx] is int hh and >= 0 && lot.IsDwelling && hh < Population.Households.Class.Length)
        {
            // the tick may be growing the household table on its worker while this runs (a chunk is made on a worker or the main thread, the tick on another): read the array once, bounds checked
            var cls = Population.Households.Class[hh];
            wealth = cls switch { SocialClass.Gentry => 2, SocialClass.Burgher => Math.Max(1, wealth), _ => Math.Min(wealth, 1) };
            if (lot.Dwelling == DwellingType.Cottage) wealth = Math.Min(wealth, lot.Wealth);
        }
        var placed = Placement(s, lot, bp, wealth);
        if (placed is not { } pl) return null;
        var (best, bestTurns, bestMirror, bestScore, x0, y0, style, hv) = pl;
        int w = best.Width, h = best.Height;
        var rect = lot.Rect;

        var st = new Stamp { Settlement = s, Lot = lotIdx, Blueprint = bp, Style = style, X0 = x0, Y0 = y0, W = w, H = h, Turns = bestTurns, Mirror = bestMirror, Fits = bestScore < 100 };
        var furnished = best.Clone();
        Furnish(furnished, st, lot, s, lotIdx, hv);
        foreach (var (z, lv) in furnished.Levels)
        {
            var sa = new int[w * h]; var fa = new int[w * h];
            for (int i = 0; i < sa.Length; i++)
            {
                sa[i] = Palette.Of(lv.Structure[i]); fa[i] = Palette.Of(lv.Fixture[i]);
                if (z == 0 && sa[i] != 0) st.Cells++;
            }
            st.Levels[z] = (sa, fa);
        }
        // front door paths out to the street edge of the lot
        if (FrontDoor(furnished, lot.Facing, out var doors))
        {
            var kind = rs.Class == SettlementClass.Town && lot.Use is not (LotUse.Church or LotUse.Chapel) ? GroundKind.Cobble : GroundKind.Dirt;
            foreach (var (dx, dy) in doors)
            {
                int wx = x0 + dx, wy = y0 + dy;
                st.FrontDoors.Add((wx, wy));
                var (sx, sy) = (lot.Facing & 3) switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
                for (int k = 1; k <= 12; k++)
                {
                    int px = wx + sx * k, py = wy + sy * k;
                    if (!rect.Overlaps(new RectM(px, py, 1, 1))) break;
                    st.Ground.Add((px, py, kind, k & 3));
                }
            }
        }
        Yard(st, lot, rs, rect, hv);
        return st;
    }

    /// <summary>
    /// The placement of a lot's building (shared by <see cref="MakeStamp"/> and P5-D's growth sites): the styled grid in its chosen transform (the door to the street, the least overflow,
    /// ties by the lot's hash), and its corner by the lot's facing and setback.
    /// </summary>
    private (BuildingGrid Grid, int Turns, bool Mirror, int Score, int X0, int Y0, string Style, ulong Hv)? Placement(int s, Lot lot, string bp, int wealth)
    {
        var rs = Region.Settlements[s];
        string style = StyleOf(s, wealth);
        var grid = GridFor(bp, style, wealth);
        if (grid == null) return null;
        var rect = lot.Rect;
        ulong hv = H(StLot, rect.X, rect.Y);
        BuildingGrid? best = null; int bestTurns = 0; bool bestMirror = false; int bestScore = int.MaxValue;
        var options = new List<(BuildingGrid G, int Turns, bool Mirror, int Score)>();
        for (int m = 0; m < 2; m++)
            for (int turns = 0; turns < 4; turns++)
            {
                var t = grid.Transform(turns, m == 1);
                int overflow = Math.Max(0, t.Width - rect.W) + Math.Max(0, t.Height - rect.H);
                bool door = FrontDoor(t, lot.Facing, out _);
                int score = overflow * 100 + (door ? 0 : 10) + (rs.Class == SettlementClass.Town ? 0 : 0);
                options.Add((t, turns, m == 1, score));
                if (score < bestScore) { bestScore = score; best = t; bestTurns = turns; bestMirror = m == 1; }
            }
        var tied = options.Where(o => o.Score == bestScore).ToList();
        var pick = tied[(int)(hv % (ulong)tied.Count)];
        best = pick.G; bestTurns = pick.Turns; bestMirror = pick.Mirror;
        int w = best.Width, h = best.Height;
        int setback = lot.Use is LotUse.Church or LotUse.Chapel ? 3 : lot.Use == LotUse.Manor ? 5 : lot.Use == LotUse.Burgage || rs.Class == SettlementClass.Town ? 0 : 1 + (int)(hv >> 8 & 1);
        int x0, y0;
        int jit = (int)((hv >> 16) % 3) - 1;
        switch (lot.Facing & 3)
        {
            case 0: x0 = rect.X + (rect.W - w) / 2 + jit; y0 = rect.Y + setback; break;
            case 2: x0 = rect.X + (rect.W - w) / 2 + jit; y0 = rect.Bottom - h - setback; break;
            case 1: x0 = rect.Right - w - setback; y0 = rect.Y + (rect.H - h) / 2 + jit; break;
            default: x0 = rect.X + setback; y0 = rect.Y + (rect.H - h) / 2 + jit; break;
        }
        x0 = Math.Clamp(x0, rect.X, Math.Max(rect.X, rect.Right - w)); y0 = Math.Clamp(y0, rect.Y, Math.Max(rect.Y, rect.Bottom - h));
        return (best, bestTurns, bestMirror, bestScore, x0, y0, style, hv);
    }

    /// <summary>
    /// P5-D: the building a lot not yet in the plan will get when it is added (a growth project's plot): its styled grid in the generator's own transform and its corner, so a visible
    /// growth site is laid out exactly as the generator stamps the finished lot (no household yet: the lot's own wealth). Null when the blueprint is missing.
    /// </summary>
    public (BuildingGrid Grid, int X0, int Y0, string Blueprint)? PlanFor(int settlement, Lot lot)
    {
        string bp = Fallback(lot);
        if (bp.Length == 0) return null;
        return Placement(settlement, lot, bp, lot.Wealth) is { } p ? (p.Grid, p.X0, p.Y0, bp) : null;
    }

    /// <summary>True when the ground floor has an exterior door on the given side (0 north, 1 east, 2 south, 3 west); lists the door cells (local).</summary>
    private bool FrontDoor(BuildingGrid g, int facing, out List<(int X, int Y)> doors)
    {
        doors = [];
        var lv = g.Level(0);
        if (lv == null) return false;
        for (int y = 0; y < lv.Height; y++)
            for (int x = 0; x < lv.Width; x++)
            {
                bool edge = (facing & 3) switch { 0 => y == 0, 1 => x == lv.Width - 1, 2 => y == lv.Height - 1, _ => x == 0 };
                if (!edge || Library.Pieces.KindOf(lv.Structure[lv.Idx(x, y)]) != PieceKind.Door) continue;
                if (_detector.IsExteriorDoor(lv, x, y)) doors.Add((x, y));
            }
        return doors.Count > 0;
    }

    // ------------------------------------------------------------------ furnishing: what the household brings, kept clear of doors and stairs

    private void Furnish(BuildingGrid g, Stamp st, Lot lot, int s, int lotIdx, ulong hv)
    {
        var pieces = Library.Pieces;
        bool vacant = lot.Vacant;
        int bedsWanted = Population.LotBeds.Length > s && lotIdx < Population.LotBeds[s].Length ? Population.LotBeds[s][lotIdx] : 0;
        int salt = 0;
        foreach (var (z, lv) in g.Levels)
        {
            // room regions by letter
            var byLetter = new SortedDictionary<char, List<int>>();
            for (int i = 0; i < lv.Letter.Length; i++) if (lv.Letter[i] != '\0') { if (!byLetter.TryGetValue(lv.Letter[i], out var l)) byLetter[lv.Letter[i]] = l = []; l.Add(i); }
            foreach (var (letter, cells) in byLetter)
            {
                if (!g.Rooms.TryGetValue(letter, out var purpose)) continue;
                var (wall, mid) = Candidates(g, lv, cells);
                // order candidates deterministically
                wall = Shuffle(wall, DetRng.Mix(hv ^ (ulong)(++salt * 977)));
                mid = Shuffle(mid, DetRng.Mix(hv ^ (ulong)(++salt * 1303)));
                int area = cells.Count;
                void Put(string piece, List<int> from, int count)
                {
                    if (!pieces.TryGet(piece, out _)) return;
                    foreach (int i in from.ToList())
                    {
                        if (count <= 0) break;
                        if (lv.Fixture[i] != null || !Free(g, lv, i)) continue;
                        lv.Fixture[i] = piece;
                        if (!StaysConnected(g, lv)) { lv.Fixture[i] = null; continue; }
                        count--;
                        from.Remove(i);
                    }
                }
                if (vacant) { RemoveHearth(lv); continue; }
                switch (purpose.Split(':')[0])
                {
                    case "sleeping": Put("bed", wall, Math.Clamp(bedsWanted > 0 ? Math.Min(bedsWanted, area / 3) : area / 5, 1, 6)); Put("chest", wall, 1); break;
                    case "hearth": Put("table", mid, area >= 12 ? 1 : 0); Put("chest", wall, 1); Put("barrel", wall, 1); if (area >= 14) Put("shelf", wall, 1); break;
                    case "gathering": Put("table", mid, Math.Max(1, area / 14)); Put("barrel", wall, 2); Put("shelf", wall, 1); break;
                    case "storage": Put("grain_bin", wall, 1); Put("barrel", wall, 2); Put("chest", wall, 1); Put("shelf", wall, 1); break;
                    case "livestock": Put("stall", wall, Math.Clamp(area / 5, 1, 4)); break;
                    case "work":
                        if (purpose.EndsWith("smithing")) { Put("anvil", mid, 1); Put("barrel", wall, 1); }
                        else { Put("workbench", wall, 2); Put("shelf", wall, 1); }
                        break;
                    case "worship": Put("altar", wall, 1); Put("pew", mid, Math.Max(2, area / 10)); break;
                    case "watch": Put("weapon_rack", wall, 1); break;
                }
            }
        }
    }

    private void RemoveHearth(BuildingLevel lv)
    {
        for (int i = 0; i < lv.Fixture.Length; i++) if (lv.Fixture[i] is "hearth" or "hearth_central" or "hearth_chimney" or "forge") lv.Fixture[i] = null;
    }

    private static List<int> Shuffle(List<int> l, ulong seed)
    {
        var a = l.ToArray();
        for (int i = a.Length - 1; i > 0; i--)
        {
            seed = DetRng.Mix(seed);
            int j = (int)(seed % (ulong)(i + 1));
            (a[i], a[j]) = (a[j], a[i]);
        }
        return [.. a];
    }

    private static readonly (int Dx, int Dy)[] N4 = [(0, -1), (1, 0), (0, 1), (-1, 0)];
    private static readonly (int Dx, int Dy)[] N8 = [(0, -1), (1, 0), (0, 1), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Floor cells of a room next to a wall, and the ones in the middle; neither includes cells beside doors, stairs or ladders.</summary>
    private (List<int> Wall, List<int> Mid) Candidates(BuildingGrid g, BuildingLevel lv, List<int> cells)
    {
        var wall = new List<int>(); var mid = new List<int>();
        foreach (int i in cells)
        {
            int x = i % lv.Width, y = i / lv.Width;
            if (lv.Fixture[i] != null || Library.Pieces.KindOf(lv.Structure[i]) != PieceKind.Floor) continue;
            bool nearPortal = false, wallNext = false;
            foreach (var (dx, dy) in N8)
            {
                int nx = x + dx, ny = y + dy;
                if (!lv.InBounds(nx, ny)) continue;
                var k = Library.Pieces.KindOf(lv.Structure[lv.Idx(nx, ny)]);
                if (k is PieceKind.Door or PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor) nearPortal = true;
            }
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy;
                var k = lv.InBounds(nx, ny) ? Library.Pieces.KindOf(lv.Structure[lv.Idx(nx, ny)]) : null;
                if (k is PieceKind.Wall or PieceKind.Post or PieceKind.Window or null) wallNext = true;
            }
            // levels above the ground also keep clear of the stairwell cell of the level below
            if (nearPortal) continue;
            if (wallNext) wall.Add(i); else mid.Add(i);
        }
        return (wall, mid);
    }

    private bool Free(BuildingGrid g, BuildingLevel lv, int i)
    {
        // never stand a fixture where a portal on another level lands (stairs and ladders share their cell across levels)
        foreach (var (z, other) in g.Levels)
        {
            if (other == lv) continue;
            var k = Library.Pieces.KindOf(other.Structure[i]);
            if (k is PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor) return false;
        }
        return true;
    }

    /// <summary>True when all walkable cells of the level still form one piece.</summary>
    private bool StaysConnected(BuildingGrid g, BuildingLevel lv)
    {
        int n = lv.Structure.Length, total = 0, start = -1;
        var open = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var k = Library.Pieces.KindOf(lv.Structure[i]);
            if (k == null || !PieceCatalog.IsWalkable(k.Value)) continue;
            var f = Library.Pieces.KindOf(lv.Fixture[i]);
            if (f is { } ff && PieceCatalog.IsSolidFixture(ff)) continue;
            open[i] = true; total++; start = i;
        }
        if (total == 0) return true;
        var seen = new bool[n]; var q = new Queue<int>(); q.Enqueue(start); seen[start] = true; int count = 1;
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int x = c % lv.Width, y = c / lv.Width;
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy;
                if (!lv.InBounds(nx, ny)) continue;
                int ni = lv.Idx(nx, ny);
                if (open[ni] && !seen[ni]) { seen[ni] = true; count++; q.Enqueue(ni); }
            }
        }
        return count == total;
    }

    // ------------------------------------------------------------------ onto the chunk

    private void PlaceBuildings(Buf b)
    {
        if (!_lotsAt.TryGetValue(Key((b.X0 + Pad) >> 5, (b.Y0 + Pad) >> 5), out var lots)) return;
        foreach (var (s, li) in lots)
        {
            var st = GetStamp(s, li);
            if (st == null) continue;
            b.Stamps.Add(st);
            var lr = Region.Settlements[s].Plan.Lots[li].Rect;
            for (int wy = Math.Max(lr.Y, b.Y0); wy < Math.Min(lr.Bottom, b.Y0 + B); wy++)
                for (int wx = Math.Max(lr.X, b.X0); wx < Math.Min(lr.Right, b.X0 + B); wx++) b.Flags[b.I(wx, wy)] |= Buf.Yard;
            foreach (var (x, y, g, v) in st.Ground)
            {
                if (!b.In(x, y)) continue;
                int idx = b.I(x, y);
                if ((b.Flags[idx] & (Buf.Water | Buf.Deep)) != 0 && g != GroundKind.Cobble && g != GroundKind.Dirt) continue;
                b.Ground[idx] = g; b.Variant[idx] = (byte)v; b.Flags[idx] |= Buf.Yard;
            }
            foreach (var (z, (sa, fa)) in st.Levels)
            {
                for (int y = 0; y < st.H; y++)
                    for (int x = 0; x < st.W; x++)
                    {
                        int wx = st.X0 + x, wy = st.Y0 + y, i = y * st.W + x;
                        if (sa[i] == 0 && fa[i] == 0) continue;
                        if (z == 0)
                        {
                            if (!b.In(wx, wy)) continue;
                            int idx = b.I(wx, wy);
                            b.Structure[idx] = sa[i]; b.Fixture[idx] = fa[i];
                            b.Flags[idx] |= Buf.Build | Buf.Yard;
                            b.Flags[idx] &= unchecked((byte)~(Buf.Water | Buf.Deep));
                        }
                        else
                        {
                            if (!b.InChunk(wx, wy)) continue;
                            if (!b.Upper.TryGetValue(z, out var u)) b.Upper[z] = u = (new int[Size * Size], new int[Size * Size]);
                            int li2 = (wy - b.Y0 - Pad) * Size + (wx - b.X0 - Pad);
                            u.Structure[li2] = sa[i]; u.Fixture[li2] = fa[i];
                        }
                    }
            }
        }
    }
}
