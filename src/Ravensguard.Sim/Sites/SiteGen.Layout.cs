using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Sites;

public static partial class SiteGen
{
    public sealed partial class Job
    {
        private int _downX = -1, _downY = -1;
        private bool _downShaft;
        // anchors found while stamping, by level
        private readonly List<(int Level, int Cell, char C, int Room)> _anchors = [];

        private static readonly (int Dx, int Dy)[] Four = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        private void LayOut(int l)
        {
            var m = _m!; var r = Rng(l);
            int W = m.W, H = m.H;
            bool rock = _t.Walls == "rock";
            bool mine = _t.Layout == "mine";
            var fill = rock ? SiteTile.Rock : SiteTile.Rock;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) m.Tiles[m.Index(l, x, y)] = fill;

            // the arrival: under the stairs (or shaft) of the level above; level 0's is the mouth
            var arrival = _c.Rooms["stairs_down"];
            int ox = 0, oy = 0;
            for (int i = 0; i < arrival.Cells.Length; i++) if (arrival.Cells[i] == '<') { ox = i % arrival.W; oy = i / arrival.W; }
            int ax = l == 0 ? W / 2 : _downX, ay = l == 0 ? 2 : _downY;
            int rx = Math.Clamp(ax - ox, 1, W - arrival.W - 1), ry = Math.Clamp(ay - oy, 1, H - arrival.H - 1);
            ax = rx + ox; ay = ry + oy;
            if (_t.Layout == "cave") CaveFill(l, ref r);
            var placed = new List<PlacedRoom>();
            placed.Add(Stamp(l, arrival, rx, ry, rock));
            int arrivalCell = m.Index(l, ax, ay);
            m.Tiles[arrivalCell] = l > 0 && _downShaft ? SiteTile.ShaftUp : SiteTile.StairsUp;
            m.Arrival[l] = arrivalCell;
            if (l == 0) m.Mouth = arrivalCell;
            else
            {
                int upper = m.Index(l - 1, _downX, _downY);
                m.Portals.Add(new SitePortal { Id = m.Portals.Count, Kind = _downShaft ? PortalKindSite.Ladder : PortalKindSite.Stairs, UpperCell = upper, LowerCell = arrivalCell });
            }

            // binary partition of the footprint; leaves that touch the arrival room are left out
            var leaves = new List<(int X, int Y, int W, int H)>();
            Split(1, 1, W - 2, H - 2, ref r, leaves, 0);
            leaves.RemoveAll(f => f.X < rx + arrival.W + 1 && rx - 1 < f.X + f.W && f.Y < ry + arrival.H + 1 && ry - 1 < f.Y + f.H);
            var used = new bool[leaves.Count];
            foreach (var id in _plan[l])
            {
                if (!_c.Rooms.TryGetValue(id, out var shape)) continue;
                bool lair = shape.Def.Purposes.Contains("lair");
                int best = -1; long bestKey = long.MinValue;
                for (int i = 0; i < leaves.Count; i++)
                {
                    if (used[i] || leaves[i].W < shape.W + 2 || leaves[i].H < shape.H + 2) continue;
                    long d = Math.Abs(leaves[i].X + leaves[i].W / 2 - ax) + Math.Abs(leaves[i].Y + leaves[i].H / 2 - ay);
                    long key = lair ? d * 1000 + (i * 37 % 97) : (long)(DetRng.Next(_seed, l, _h.SiteId, RngStreams.SiteLayout, 5000u + (uint)i + (uint)placed.Count * 131u) & 0xFFFFF);
                    if (key > bestKey) { bestKey = key; best = i; }
                }
                int px, py;
                if (best >= 0)
                {
                    var f = leaves[best]; used[best] = true;
                    px = f.X + 1 + r.Range(0, Math.Max(1, f.W - shape.W - 1));
                    py = f.Y + 1 + r.Range(0, Math.Max(1, f.H - shape.H - 1));
                }
                else
                {
                    // no leaf fits: rejection sampling over the footprint
                    bool ok = false; px = py = 0;
                    for (int k = 0; k < 60 && !ok; k++)
                    {
                        px = 1 + r.Range(0, Math.Max(1, W - shape.W - 2)); py = 1 + r.Range(0, Math.Max(1, H - shape.H - 2));
                        ok = true;
                        foreach (var p in placed) if (px < p.X + p.W + 2 && p.X - 2 < px + shape.W && py < p.Y + p.H + 2 && p.Y - 2 < py + shape.H) { ok = false; break; }
                    }
                    if (!ok) continue;
                }
                placed.Add(Stamp(l, shape, px, py, rock || shape.Rock));
            }

            // corridors: a spanning tree over the rooms from the arrival, nearest doors first, and a few loops
            var inTree = new bool[placed.Count]; inTree[0] = true;
            for (int added = 1; added < placed.Count; added++)
            {
                int bi = -1, bj = -1; long bd = long.MaxValue;
                for (int i = 0; i < placed.Count; i++)
                {
                    if (!inTree[i]) continue;
                    for (int j = 0; j < placed.Count; j++)
                    {
                        if (inTree[j]) continue;
                        long d = Math.Abs(placed[i].Cx - placed[j].Cx) + Math.Abs(placed[i].Cy - placed[j].Cy);
                        if (d < bd) { bd = d; bi = i; bj = j; }
                    }
                }
                if (bj < 0) break;
                inTree[bj] = true;
                Connect(l, placed[bi], placed[bj], ref r, mine);
            }
            for (int i = 1; i < placed.Count; i++)
                if (r.Range(0, 100) < 15)
                {
                    int j = -1; long bd = long.MaxValue, second = long.MaxValue; int jj = -1;
                    for (int k = 0; k < placed.Count; k++)
                    {
                        if (k == i) continue;
                        long d = Math.Abs(placed[i].Cx - placed[k].Cx) + Math.Abs(placed[i].Cy - placed[k].Cy);
                        if (d < bd) { second = bd; jj = j; bd = d; j = k; } else if (d < second) { second = d; jj = k; }
                    }
                    if (jj >= 0) Connect(l, placed[i], placed[jj], ref r, mine);
                }
            if (_t.Layout == "cave") DropPockets(l, placed);
            Reconnect(l, arrivalCell);

            // the way down: the stairs or shaft of a far room
            if (l < LevelCount - 1)
            {
                var dist = Distances(l, arrivalCell);
                int pick = -1, pd = -1;
                bool Fits(int x, int y) => x >= 3 && x <= W - 4 && y >= 2 && y <= H - 5;
                foreach (var (al, cell, ch, room) in _anchors)
                {
                    if (al != l || ch is not ('>' or 'H') || room == placed[0].Index) continue;
                    var (_, x, y) = m.At(cell);
                    if (!Fits(x, y) || dist[cell - l * m.Plane] < 0) continue;
                    if (dist[cell - l * m.Plane] > pd) { pd = dist[cell - l * m.Plane]; pick = cell; }
                }
                if (pick < 0)
                    for (int i = 0; i < m.Plane; i++)
                    {
                        int cell = l * m.Plane + i; int x = i % W, y = i / W;
                        if (m.Tiles[cell] != SiteTile.Floor || m.Room[cell] < 0 || m.Room[cell] == placed[0].Index || !Fits(x, y) || dist[i] < 0) continue;
                        if (dist[i] > pd) { pd = dist[i]; pick = cell; }
                    }
                if (pick < 0) pick = m.Index(l, Math.Clamp(ax, 3, W - 4), Math.Clamp(ay + 3, 2, H - 5));
                var (_, dx, dy) = m.At(pick);
                _downShaft = mine || m.Tiles[pick] == SiteTile.ShaftDown;
                m.Tiles[pick] = _downShaft ? SiteTile.ShaftDown : SiteTile.StairsDown;
                _downX = dx; _downY = dy;
                Reconnect(l, arrivalCell);
            }
            // leftover portal candidates become floor
            for (int i = 0; i < m.Plane; i++)
            {
                int cell = l * m.Plane + i;
                if (m.Tiles[cell] is SiteTile.StairsDown or SiteTile.ShaftDown && (l == LevelCount - 1 || i != _downY * W + _downX)) m.Tiles[cell] = SiteTile.Floor;
            }
        }

        private void Split(int x, int y, int w, int h, ref RngStream r, List<(int, int, int, int)> leaves, int depth)
        {
            const int Min = 9;
            bool canX = w >= 2 * Min, canY = h >= 2 * Min;
            if ((!canX && !canY) || depth > 6 || (w * h < 22 * 22 && r.Range(0, 100) < 25))
            { leaves.Add((x, y, w, h)); return; }
            bool alongX = canX && (!canY || w > h || (w == h && r.Range(0, 2) == 0));
            if (alongX)
            {
                int cut = Min + r.Range(0, w - 2 * Min + 1);
                Split(x, y, cut, h, ref r, leaves, depth + 1); Split(x + cut, y, w - cut, h, ref r, leaves, depth + 1);
            }
            else
            {
                int cut = Min + r.Range(0, h - 2 * Min + 1);
                Split(x, y, w, cut, ref r, leaves, depth + 1); Split(x, y + cut, w, h - cut, ref r, leaves, depth + 1);
            }
        }

        private PlacedRoom Stamp(int l, RoomShape s, int x0, int y0, bool rock)
        {
            var m = _m!;
            var pr = new PlacedRoom(m.Rooms.Count, s.Def.Id, l, x0, y0, s.W, s.H, s.Def.Purposes);
            m.Rooms.Add(pr);
            bool burial = s.Def.Purposes.Contains("burial") || s.Def.Purposes.Contains("lair") && _h.Kind == WorldGen.Region.SiteKind.Barrow;
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                {
                    char ch = s.At(x, y);
                    int cell = m.Index(l, x0 + x, y0 + y);
                    m.Room[cell] = (short)pr.Index;
                    SiteTile t = ch switch
                    {
                        '#' => rock ? SiteTile.Rock : SiteTile.Wall,
                        '+' => rock ? SiteTile.Floor : SiteTile.Door,
                        '<' => SiteTile.Floor,
                        '>' => SiteTile.StairsDown,
                        'H' => SiteTile.ShaftDown,
                        'o' => SiteTile.Pillar,
                        '$' or 'n' => SiteTile.Container,
                        'b' => burial ? SiteTile.Container : SiteTile.Floor,
                        '~' => SiteTile.Water,
                        ':' => SiteTile.Rubble,
                        'v' => SiteTile.Ore,
                        _ => SiteTile.Floor,
                    };
                    m.Tiles[cell] = t;
                    if (ch == '~') m.WaterCm[cell] = 60;
                    if (s.Def.SealedAir > 0 && ch != '#') m.Air[cell] = (ushort)Math.Min(m.Air[cell], s.Def.SealedAir);
                    if (ch is '$' or 'n' or 'b' or '^' or 'l' or '>' or 'H') _anchors.Add((l, cell, ch, pr.Index));
                    if (ch == 'l') m.Sconces.Add(cell);
                }
            return pr;
        }

        private (int X, int Y) Outside(PlacedRoom p, int dx, int dy)
        {
            int x = p.X + dx, y = p.Y + dy;
            if (dy == 0) return (x, y - 1);
            if (dy == p.H - 1) return (x, y + 1);
            if (dx == 0) return (x - 1, y);
            if (dx == p.W - 1) return (x + 1, y);
            return (x, y);
        }

        private void Connect(int l, PlacedRoom a, PlacedRoom b, ref RngStream r, bool wide)
        {
            var sa = _c.Rooms[a.Template]; var sb = _c.Rooms[b.Template];
            (int X, int Y) pa = (a.Cx, a.Cy), pb = (b.Cx, b.Cy); long bd = long.MaxValue;
            foreach (var (ax, ay) in sa.Doors.Count > 0 ? sa.Doors : [(sa.W / 2, 0)])
                foreach (var (bx, by) in sb.Doors.Count > 0 ? sb.Doors : [(sb.W / 2, 0)])
                {
                    var oa = Outside(a, ax, ay); var ob = Outside(b, bx, by);
                    long d = Math.Abs(oa.X - ob.X) + Math.Abs(oa.Y - ob.Y);
                    if (d < bd) { bd = d; pa = oa; pb = ob; }
                }
            bool hFirst = r.Range(0, 2) == 0;
            bool two = wide || r.Range(0, 100) < 30;
            var m = _m!;
            int x = pa.X, y = pa.Y;
            void Dig(int cx, int cy)
            {
                if (!m.In(l, cx, cy) || cx < 1 || cy < 1 || cx >= m.W - 1 || cy >= m.H - 1) return;
                int c = m.Index(l, cx, cy);
                var t = m.Tiles[c];
                if (t == SiteTile.Rock && m.Room[c] < 0) m.Tiles[c] = SiteTile.Floor;   // corridors go round rooms, never through their walls (the reach fix tunnels what is left)
            }
            void Walk(int tx, int ty, bool horiz)
            {
                while (horiz ? x != tx : y != ty)
                {
                    if (horiz) x += Math.Sign(tx - x); else y += Math.Sign(ty - y);
                    Dig(x, y);
                    if (two) { if (horiz) Dig(x, y + 1); else Dig(x + 1, y); }
                }
            }
            Dig(x, y);
            if (hFirst) { Walk(pb.X, pb.Y, true); Walk(pb.X, pb.Y, false); }
            else { Walk(pb.X, pb.Y, false); Walk(pb.X, pb.Y, true); }
            // the rooms' own doors open onto the corridor
            OpenDoorsToward(a, l); OpenDoorsToward(b, l);
        }

        private void OpenDoorsToward(PlacedRoom p, int l)
        {
            var m = _m!; var s = _c.Rooms[p.Template];
            foreach (var (dx, dy) in s.Doors)
            {
                var (ox, oy) = Outside(p, dx, dy);
                if (!m.In(l, ox, oy)) continue;
                int o = m.Index(l, ox, oy), d = m.Index(l, p.X + dx, p.Y + dy);
                if (SiteTiles.Walkable(m.Tiles[o]) && m.Tiles[d] == SiteTile.Wall) m.Tiles[d] = SiteTile.Door;
            }
        }

        /// <summary>Cellular-automaton rock (caves, 4.1): random fill, five smoothing passes; the chambers and passages are stamped over it.</summary>
        private void CaveFill(int l, ref RngStream r)
        {
            var m = _m!; int W = m.W, H = m.H;
            var a = new bool[W * H];
            for (int y = 2; y < H - 2; y++) for (int x = 2; x < W - 2; x++) a[y * W + x] = r.Range(0, 100) >= 55;
            var b = new bool[W * H];
            for (int pass = 0; pass < 5; pass++)
            {
                for (int y = 2; y < H - 2; y++)
                    for (int x = 2; x < W - 2; x++)
                    {
                        int rockN = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && !a[(y + dy) * W + x + dx]) rockN++;
                        b[y * W + x] = rockN < 5 && (a[y * W + x] || rockN < 4);
                    }
                (a, b) = (b, a);
            }
            for (int i = 0; i < W * H; i++) if (a[i]) m.Tiles[l * m.Plane + i] = SiteTile.Floor;
        }

        /// <summary>Open pockets of the cave too small to matter and touching no room go back to rock.</summary>
        private void DropPockets(int l, List<PlacedRoom> rooms)
        {
            var m = _m!; var comp = new int[m.Plane]; Array.Fill(comp, -1);
            int n = 0;
            for (int i = 0; i < m.Plane; i++)
            {
                if (comp[i] >= 0 || !SiteTiles.Walkable(m.Tiles[l * m.Plane + i])) continue;
                var cells = new List<int>(); var q = new Queue<int>(); q.Enqueue(i); comp[i] = n; bool room = false;
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); cells.Add(c); if (m.Room[l * m.Plane + c] >= 0) room = true;
                    int x = c % m.W, y = c / m.W;
                    foreach (var (dx, dy) in Four)
                    {
                        int nx = x + dx, ny = y + dy; if ((uint)nx >= m.W || (uint)ny >= m.H) continue;
                        int j = ny * m.W + nx; if (comp[j] >= 0 || !SiteTiles.Walkable(m.Tiles[l * m.Plane + j])) continue;
                        comp[j] = n; q.Enqueue(j);
                    }
                }
                if (!room && cells.Count < 30) foreach (int c in cells) m.Tiles[l * m.Plane + c] = SiteTile.Rock;
                n++;
            }
        }

        /// <summary>Breadth-first distances (cells) from a cell over walkable cells of its level; -1 unreached.</summary>
        internal int[] Distances(int l, int fromCell) => SiteGen.Distances(_m!, l, fromCell);

        /// <summary>
        /// Every walkable cell and every container of the level is made reachable from the arrival (test EX-3): an unreached part is tunnelled to the nearest reached cell by the
        /// cheapest way (open ground 1, rock and walls 4, pillars and ore 8, containers never).
        /// </summary>
        private void Reconnect(int l, int arrivalCell)
        {
            var m = _m!;
            for (int guard = 0; guard < 200; guard++)
            {
                var dist = Distances(l, arrivalCell);
                int target = -1;
                for (int i = 0; i < m.Plane && target < 0; i++)
                {
                    int cell = l * m.Plane + i;
                    var t = m.Tiles[cell];
                    if (SiteTiles.Walkable(t) && dist[i] < 0) target = i;
                    else if (t == SiteTile.Container && !Touches(dist, i)) target = NeighbourFloorOrDig(l, i);
                }
                if (target < 0) return;
                Tunnel(l, target, dist);
            }
        }

        private bool Touches(int[] dist, int i)
        {
            var m = _m!; int x = i % m.W, y = i / m.W;
            foreach (var (dx, dy) in Four) { int nx = x + dx, ny = y + dy; if ((uint)nx < m.W && (uint)ny < m.H && dist[ny * m.W + nx] >= 0) return true; }
            return false;
        }

        private int NeighbourFloorOrDig(int l, int i)
        {
            var m = _m!; int x = i % m.W, y = i / m.W;
            foreach (var (dx, dy) in Four)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 1 || ny < 1 || nx >= m.W - 1 || ny >= m.H - 1) continue;
                int c = l * m.Plane + ny * m.W + nx;
                if (m.Tiles[c] is SiteTile.Rock or SiteTile.Wall or SiteTile.Pillar or SiteTile.Ore or SiteTile.Rubble) { m.Tiles[c] = SiteTile.Floor; return ny * m.W + nx; }
                if (SiteTiles.Walkable(m.Tiles[c])) return ny * m.W + nx;
            }
            return -1;
        }

        private void Tunnel(int l, int from, int[] reached)
        {
            var m = _m!; int n = m.Plane;
            var cost = new int[n]; Array.Fill(cost, int.MaxValue); var prev = new int[n]; Array.Fill(prev, -1);
            var pq = new PriorityQueue<int, long>();
            cost[from] = 0; pq.Enqueue(from, 0);
            int end = -1;
            while (pq.Count > 0)
            {
                int c = pq.Dequeue();
                if (reached[c] >= 0) { end = c; break; }
                int x = c % m.W, y = c / m.W;
                foreach (var (dx, dy) in Four)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 1 || ny < 1 || nx >= m.W - 1 || ny >= m.H - 1) continue;
                    int j = ny * m.W + nx;
                    var t = m.Tiles[l * n + j];
                    int step = SiteTiles.Walkable(t) ? 1 : t is SiteTile.Rock or SiteTile.Wall or SiteTile.Rubble ? 4 : t is SiteTile.Pillar or SiteTile.Ore ? 8 : -1;
                    if (step < 0) continue;
                    int nc = cost[c] + step;
                    if (nc < cost[j]) { cost[j] = nc; prev[j] = c; pq.Enqueue(j, (long)nc * n + j); }
                }
            }
            if (end < 0) { m.Tiles[l * n + from] = SiteTile.Rock; return; }   // cannot be reached at all: it was never meant to be open
            for (int c = end; c >= 0; c = prev[c])
            {
                int cell = l * n + c;
                var t = m.Tiles[cell];
                if (!SiteTiles.Walkable(t)) m.Tiles[cell] = t == SiteTile.Wall && m.Room[cell] >= 0 && _t.Walls != "rock" ? SiteTile.Door : SiteTile.Floor;
            }
        }
    }

    /// <summary>Breadth-first distances (cells) from a cell over walkable cells of its level; -1 unreached.</summary>
    public static int[] Distances(SiteMap m, int l, int fromCell)
    {
        var d = new int[m.Plane]; Array.Fill(d, -1);
        int s = fromCell - l * m.Plane;
        if ((uint)s >= (uint)m.Plane) return d;
        var q = new Queue<int>(); d[s] = 0; q.Enqueue(s);
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int x = c % m.W, y = c / m.W;
            foreach (var (dx, dy) in Job_Four)
            {
                int nx = x + dx, ny = y + dy; if ((uint)nx >= m.W || (uint)ny >= m.H) continue;
                int j = ny * m.W + nx; if (d[j] >= 0 || !SiteTiles.Walkable(m.Tiles[l * m.Plane + j])) continue;
                d[j] = d[c] + 1; q.Enqueue(j);
            }
        }
        return d;
    }

    private static readonly (int Dx, int Dy)[] Job_Four = [(1, 0), (-1, 0), (0, 1), (0, -1)];
}
