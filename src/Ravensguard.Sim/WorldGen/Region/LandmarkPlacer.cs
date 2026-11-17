using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>One prop of a placed landmark's footprint: a kind, a metre offset from the landmark centre and a variant.</summary>
public sealed record LandmarkProp(PropKind Kind, int Dx, int Dy, byte Variant);

/// <summary>
/// An authored landmark placed into a generated region (plan 1A, section 3 item 14). <see cref="Event"/> is the chronicle event it hangs on
/// (EntityId.None when the template stands on its own); <see cref="Lore"/> is the finished text that names that event, person and place.
/// </summary>
public sealed record PlacedLandmark(int Index, string TemplateId, string Name, int X, int Y, EntityId Event, string EventType, string EventTitle, int Year,
    string Place, string Lore, IReadOnlyList<LandmarkProp> Props)
{
    public int TileX => Math.Clamp(X / Region.TileM, 0, Region.Size - 1);
    public int TileY => Math.Clamp(Y / Region.TileM, 0, Region.Size - 1);
    public bool Attached => Event != EntityId.None;
    /// <summary>Half-size in metres of the footprint's bounding square.</summary>
    public int Radius => Props.Count == 0 ? 0 : Props.Max(p => Math.Max(Math.Abs(p.Dx), Math.Abs(p.Dy)));
}

/// <summary>
/// The landmark generation phase: runs after the region is built and places 4 to 10 authored landmarks (content type <c>landmark</c>) by their rules
/// (biome, near road, river, coast or crossing, distance from settlements, hill) and, where a rule asks, hangs each on a chronicle event of the
/// region's history (battle to cairn, plague to pit, saint to holy well). Deterministic: a pure function of (region seed, history, content).
/// Result is stored in <see cref="Region.Landmarks"/>; chunk generation stamps the footprints.
/// </summary>
public static class LandmarkPlacer
{
    public const int MinCount = 4, MaxCount = 10;
    const uint StLm = 41;

    public static IReadOnlyList<PlacedLandmark> Place(Region region, WorldHistory history, ContentDatabase content)
    {
        var defs = content.All<LandmarkDef>().OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        var list = new Placer(region, history, defs).Run();
        region.Landmarks = list;
        return list;
    }

    /// <summary>SHA-256 over every placed landmark (null counts as none), for determinism tests and the save check.</summary>
    public static string ComputeHash(IReadOnlyList<PlacedLandmark>? marks)
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) { L(s.Length); h.AppendData(System.Text.Encoding.UTF8.GetBytes(s)); }
        L(marks?.Count ?? 0);
        foreach (var m in marks ?? [])
        {
            L(m.Index); S(m.TemplateId); S(m.Name); L(m.X); L(m.Y); L((long)m.Event.Value); S(m.EventType); S(m.EventTitle); L(m.Year); S(m.Place); S(m.Lore);
            L(m.Props.Count);
            foreach (var p in m.Props) { L((long)p.Kind); L(p.Dx); L(p.Dy); L(p.Variant); }
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    /// <summary>
    /// Orders <paramref name="items"/> by repeated weighted draws without replacement: each slot picks the item whose cumulative weight range holds
    /// <c>draw(slot) % total</c>. Integer maths only (no logarithms), so the order is the same on every platform; heavier items tend to come first.
    /// </summary>
    public static List<T> WeightedOrder<T>(IReadOnlyList<T> items, Func<T, int> weight, Func<int, ulong> draw)
    {
        var rest = new List<T>(items);
        var order = new List<T>(rest.Count);
        for (int k = 0; rest.Count > 0; k++)
        {
            long total = 0;
            foreach (var d in rest) total += Math.Max(1, weight(d));
            long pick = (long)(draw(k) % (ulong)total), acc = 0;
            int idx = rest.Count - 1;
            for (int i = 0; i < rest.Count; i++) { acc += Math.Max(1, weight(rest[i])); if (pick < acc) { idx = i; break; } }
            order.Add(rest[idx]); rest.RemoveAt(idx);
        }
        return order;
    }

    /// <summary>Parses a content prop kind ("low_wall", "SiteRuin") into the sim's prop kind; false for an unknown name.</summary>
    public static bool TryParseKind(string name, out PropKind kind) => Enum.TryParse(name.Replace("_", ""), true, out kind) && Enum.IsDefined(kind);

    /// <summary>Lore tokens a template may use.</summary>
    public static readonly string[] Tokens = ["{place}", "{event}", "{year}", "{text}", "{person}", "{realm}"];

    sealed class Placer
    {
        readonly Region _r; readonly WorldHistory _h; readonly List<LandmarkDef> _defs;
        readonly ulong _seed;
        readonly List<PlacedLandmark> _placed = [];
        readonly HashSet<EntityId> _usedEvents = [];
        readonly int[] _sx, _sy; readonly bool[] _farm;
        readonly List<(long Ax, long Ay, long Bx, long By)> _roadSegs = [];
        readonly List<PointM> _roadPts = [], _crossings = [];
        readonly List<int> _coast = [], _hill = [];
        readonly Dictionary<string, List<ChronicleEntry>> _events = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _count = new(StringComparer.Ordinal);

        public Placer(Region r, WorldHistory h, List<LandmarkDef> defs)
        {
            _r = r; _h = h; _defs = defs;
            _seed = DetRng.Mix(r.Seed ^ 0x4C414E444D4B31UL);
            int n = r.Settlements.Count;
            _sx = new int[n]; _sy = new int[n]; _farm = new bool[n];
            for (int i = 0; i < n; i++) { _sx[i] = r.Settlements[i].CenterX; _sy[i] = r.Settlements[i].CenterY; _farm[i] = r.Settlements[i].Class == SettlementClass.Farmstead; }
            foreach (var e in r.RoadEdges)
            {
                for (int i = 0; i + 1 < e.Points.Length; i++) _roadSegs.Add((e.Points[i].X, e.Points[i].Y, e.Points[i + 1].X, e.Points[i + 1].Y));
                if (e.Class >= RoadClass.Track) foreach (var p in e.Points) _roadPts.Add(p);
                foreach (var c in e.Crossings) _crossings.Add(c.At);
            }
            ScanTerrain();
            foreach (var e in h.Chronicle.All)
            {
                if (!InRegion(e)) continue;
                if (!_events.TryGetValue(e.TypeId, out var l)) _events[e.TypeId] = l = [];
                l.Add(e);
            }
        }

        ulong Hash(long a, long b = 0, uint salt = 0) => DetRng.Next(_seed, a, (ulong)b, StLm + salt * 64, 0);
        int Range(long a, long b, uint salt, int lo, int hi) => hi <= lo ? lo : lo + (int)(Hash(a, b, salt) % (ulong)(hi - lo));

        bool InRegion(ChronicleEntry e)
        {
            if (!e.Data.TryGetValue("cell", out var cs) || !int.TryParse(cs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cell)) return false;
            int cx = cell % Continent.Size - _r.CellX, cy = cell / Continent.Size - _r.CellY;
            return (uint)cx < 16 && (uint)cy < 16;
        }

        void ScanTerrain()
        {
            const int S = Region.Size;
            for (int y = 8; y < S - 8; y += 2)
                for (int x = 8; x < S - 8; x += 2)
                {
                    int t = y * S + x;
                    if (_r.Water[t] != 0) continue;
                    // coast: open sea three tiles away in some direction
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + RegionBuilder.Dx8[d] * 3, ny = y + RegionBuilder.Dy8[d] * 3;
                        if ((_r.Water[ny * S + nx] & WaterFlags.Sea) != 0) { _coast.Add(t); break; }
                    }
                    if ((x & 1) == 0 && (y & 2) == 0 && IsHill(t)) _hill.Add(t);
                }
        }

        /// <summary>A rise: at least 3.5 m above the average of the ground 256 m around it, on gentle slope and not in a marsh.</summary>
        bool IsHill(int t)
        {
            const int S = Region.Size;
            int x = t % S, y = t / S;
            if (x < 5 || y < 5 || x >= S - 5 || y >= S - 5 || _r.SlopePermille[t] >= 260 || _r.Biomes[t] == Biome.Marsh) return false;
            long ring = 0;
            for (int d = 0; d < 8; d++) ring += _r.HeightCm[(y + RegionBuilder.Dy8[d] * 4) * S + x + RegionBuilder.Dx8[d] * 4];
            return _r.HeightCm[t] - ring / 8 >= 350;
        }

        List<LandmarkDef> WeightedOrder(List<LandmarkDef> rest, int round) => LandmarkPlacer.WeightedOrder(rest, d => d.Weight, k => Hash(round, k, 1));

        public List<PlacedLandmark> Run()
        {
            if (_defs.Count == 0) return _placed;
            int target = MinCount + (int)(Hash(0, 1) % (ulong)(MaxCount - MinCount + 1));
            for (int round = 0; round < 4 && _placed.Count < target; round++)
            {
                var order = WeightedOrder(_defs.Where(d => _count.GetValueOrDefault(d.Id) < d.MaxPerRegion).ToList(), round);
                // heavier weights come first more often: each slot is a cumulative-weight draw (hash % total) from what is left, integer maths only
                foreach (var d in order)
                {
                    if (_placed.Count >= target) break;
                    TryTemplate(d);
                }
            }
            return _placed;
        }

        static long StrKey(string s) { long h = 17; foreach (char c in s) h = h * 131 + c; return h; }

        // ---------------------------------------------------------------- one template

        readonly record struct Anchor(int X, int Y, int MinR, int MaxR, int SiteIndex);

        void TryTemplate(LandmarkDef d)
        {
            var kinds = ParseProps(d);
            if (kinds.Count == 0) return;
            int rad = kinds.Max(p => Math.Max(Math.Abs(p.Dx), Math.Abs(p.Dy)));
            if (d.AttachEvents.Count > 0 && TryAttached(d, kinds, rad)) return;
            if (d.RequireAttach) return;
            if (TryFind(d, null, rad, (int)StrKey(d.Id), out int x, out int y)) Add(d, kinds, x, y, null, null);
        }

        bool TryAttached(LandmarkDef d, List<LandmarkProp> props, int rad)
        {
            var cands = new List<ChronicleEntry>();
            foreach (var type in d.AttachEvents.Distinct()) if (_events.TryGetValue(type, out var l)) cands.AddRange(l.Where(e => !_usedEvents.Contains(e.Id)));
            cands.Sort((a, b) =>
            {
                ulong ka = Hash(StrKey(d.Id), (long)a.Id.Value, 2), kb = Hash(StrKey(d.Id), (long)b.Id.Value, 2);
                return ka != kb ? ka.CompareTo(kb) : a.Id.CompareTo(b.Id);
            });
            int tries = 0;
            foreach (var e in cands)
            {
                if (tries++ >= 6) break;
                var anchor = AnchorOf(d, e);
                if (TryFind(d, anchor, rad, (int)(e.Id.Value & 0x7FFFFFFF), out int x, out int y))
                {
                    Add(d, props, x, y, e, anchor);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Where an event happened in this region: a battlefield or ruin site it made, a settlement it names, else the middle of its continental cell.</summary>
        Anchor AnchorOf(LandmarkDef d, ChronicleEntry e)
        {
            int minR = Math.Max(d.MinSettlementM, 60), maxR = d.MaxSettlementM > 0 ? Math.Min(d.MaxSettlementM, 2200) : 1500;
            for (int i = 0; i < _r.Sites.Count; i++)
            {
                var s = _r.Sites[i];
                if (s.HistoryEvent == e.Id && s.Kind is SiteKind.Battlefield or SiteKind.Ruin) return new Anchor(s.X, s.Y, 40, 500, i);
            }
            foreach (var sub in e.Subjects)
            {
                if (sub.Kind != IdKind.Settlement) continue;
                for (int i = 0; i < _r.Settlements.Count; i++)
                    if (_r.Settlements[i].HistoryId == sub) return new Anchor(_sx[i], _sy[i], minR, maxR, -1);
                for (int i = 0; i < _r.Sites.Count; i++)
                    if (_r.Sites[i].HistorySettlement == sub) return new Anchor(_r.Sites[i].X, _r.Sites[i].Y, minR, maxR, i);
            }
            int cell = int.Parse(e.Data["cell"], CultureInfo.InvariantCulture);
            int cx = (cell % Continent.Size - _r.CellX) * RegionSettings.CellM + RegionSettings.CellM / 2, cy = (cell / Continent.Size - _r.CellY) * RegionSettings.CellM + RegionSettings.CellM / 2;
            return new Anchor(cx, cy, 0, 1100, -1);
        }

        bool TryFind(LandmarkDef d, Anchor? anchor, int rad, int salt, out int x, out int y)
        {
            const int Attempts = 90;
            long key = StrKey(d.Id) * 31 + salt;
            for (int k = 0; k < Attempts; k++)
            {
                if (anchor is { } a)
                {
                    int R = a.MinR + Math.Max(40, (a.MaxR - a.MinR)) * (k + 1) / Attempts;
                    int dx = Range(key, k * 2, 3, -R, R + 1), dy = Range(key, k * 2 + 1, 3, -R, R + 1);
                    long d2 = (long)dx * dx + (long)dy * dy;
                    if (d2 > (long)R * R || d2 < (long)a.MinR * a.MinR) continue;
                    x = a.X + dx; y = a.Y + dy;
                }
                else if (d.Coastal) { if (_coast.Count == 0) break; PointAtTile(_coast[(int)(Hash(key, k, 4) % (ulong)_coast.Count)], key, k, out x, out y); }
                else if (d.OnHill) { if (_hill.Count == 0) break; PointAtTile(_hill[(int)(Hash(key, k, 4) % (ulong)_hill.Count)], key, k, out x, out y); }
                else if (d.NearCrossing)
                {
                    if (_crossings.Count == 0) break;
                    var c = _crossings[(int)(Hash(key, k, 4) % (ulong)_crossings.Count)];
                    x = c.X + Range(key, k * 2, 5, -70, 71); y = c.Y + Range(key, k * 2 + 1, 5, -70, 71);
                }
                else if (d.NearRoad)
                {
                    if (_roadPts.Count == 0) break;
                    var c = _roadPts[(int)(Hash(key, k, 4) % (ulong)_roadPts.Count)];
                    x = c.X + Range(key, k * 2, 5, -40, 41); y = c.Y + Range(key, k * 2 + 1, 5, -40, 41);
                }
                else { x = Range(key, k * 2, 6, 700, Region.Meters - 700); y = Range(key, k * 2 + 1, 6, 700, Region.Meters - 700); }
                if (Accept(d, x, y, rad, anchor?.SiteIndex ?? -1)) return true;
            }
            x = y = 0;
            return false;
        }

        void PointAtTile(int t, long key, int k, out int x, out int y)
        {
            x = (t % Region.Size) * Region.TileM + Range(key, k * 2, 7, 8, Region.TileM - 8);
            y = (t / Region.Size) * Region.TileM + Range(key, k * 2 + 1, 7, 8, Region.TileM - 8);
        }

        long NearestSegD2(int x, int y)
        {
            long best = long.MaxValue;
            foreach (var (ax, ay, bx, by) in _roadSegs)
            {
                long dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                long tn = len2 == 0 ? 0 : Math.Clamp((x - ax) * dx + (y - ay) * dy, 0, len2);
                long cx = len2 == 0 ? ax : ax + dx * tn / len2, cy = len2 == 0 ? ay : ay + dy * tn / len2;
                long ex = x - cx, ey = y - cy;
                best = Math.Min(best, ex * ex + ey * ey);
            }
            return best;
        }

        bool Accept(LandmarkDef d, int x, int y, int rad, int ownSite)
        {
            if (x < 300 || y < 300 || x >= Region.Meters - 300 || y >= Region.Meters - 300) return false;
            int t = (y / Region.TileM) * Region.Size + x / Region.TileM;
            if (_r.Built[t] != 0 || _r.ParcelMap[t] != 0) return false;
            // the whole footprint stands on dry land
            foreach (var (cx, cy) in new[] { (x - rad, y - rad), (x + rad, y - rad), (x - rad, y + rad), (x + rad, y + rad), (x, y) })
                if (_r.Water[(cy / Region.TileM) * Region.Size + cx / Region.TileM] != 0) return false;
            if (d.Biomes.Count > 0 && !BiomeAllowed(d, _r.Biomes[t])) return false;
            if (d.Coastal && !NearFlag(t, WaterFlags.Sea, 4)) return false;
            if (d.NearRiver && !NearFlag(t, (byte)(WaterFlags.River | WaterFlags.Stream), 3)) return false;
            if (d.OnHill && !IsHill(t)) return false;

            long near = long.MaxValue;
            for (int i = 0; i < _sx.Length; i++) near = Math.Min(near, Region.Dist2(_sx[i] - x, _sy[i] - y));
            if (near < (long)d.MinSettlementM * d.MinSettlementM) return false;
            if (d.MaxSettlementM > 0 && near > (long)d.MaxSettlementM * d.MaxSettlementM) return false;

            long road = NearestSegD2(x, y);
            long clear = rad + 5;
            if (road < clear * clear) return false;
            if (d.NearRoad && road > (long)(rad + 45) * (rad + 45)) return false;
            if (d.MinRoadM > 0 && road < (long)d.MinRoadM * d.MinRoadM) return false;
            if (d.NearCrossing)
            {
                bool ok = false;
                foreach (var c in _crossings) if (Region.Dist2(c.X - x, c.Y - y) <= 80L * 80 && Region.Dist2(c.X - x, c.Y - y) >= (long)(rad + 9) * (rad + 9)) { ok = true; break; }
                if (!ok) return false;
            }
            for (int i = 0; i < _r.Sites.Count; i++)
            {
                var s = _r.Sites[i];
                long need = i == ownSite ? rad + 14 : 260;
                if (Region.Dist2(s.X - x, s.Y - y) < need * need) return false;
            }
            foreach (var p in _placed) if (Region.Dist2(p.X - x, p.Y - y) < 1100L * 1100) return false;
            return true;
        }

        bool NearFlag(int t, byte flag, int tiles)
        {
            int tx = t % Region.Size, ty = t / Region.Size;
            for (int dy = -tiles; dy <= tiles; dy++)
                for (int dx = -tiles; dx <= tiles; dx++)
                {
                    int nx = tx + dx, ny = ty + dy;
                    if (Region.InBounds(nx, ny) && (_r.Water[ny * Region.Size + nx] & flag) != 0) return true;
                }
            return false;
        }

        static bool BiomeAllowed(LandmarkDef d, Biome b)
        {
            foreach (var n in d.Biomes) if (Enum.TryParse<Biome>(n.Replace("_", ""), true, out var x) && x == b) return true;
            return false;
        }

        static List<LandmarkProp> ParseProps(LandmarkDef d)
        {
            var l = new List<LandmarkProp>();
            foreach (var p in d.Props) if (TryParseKind(p.Kind, out var k)) l.Add(new LandmarkProp(k, p.Dx, p.Dy, (byte)Math.Clamp(p.Variant, 0, 255)));
            return l;
        }

        // ---------------------------------------------------------------- the finished landmark

        void Add(LandmarkDef d, List<LandmarkProp> props, int x, int y, ChronicleEntry? e, Anchor? anchor)
        {
            string place = PlaceName(x, y, e);
            string lore, title = "", etype = "";
            int year = 0;
            var id = EntityId.None;
            if (e != null)
            {
                id = e.Id; etype = e.TypeId; title = e.Data.GetValueOrDefault("title", e.TypeId); year = e.When.Year;
                string text = e.Data.GetValueOrDefault("text", "").Trim().TrimEnd('.');
                lore = d.Lore.Replace("{event}", title).Replace("{year}", year.ToString(CultureInfo.InvariantCulture)).Replace("{text}", text)
                    .Replace("{person}", PersonOf(e)).Replace("{realm}", RealmOf(e)).Replace("{place}", place);
                _usedEvents.Add(e.Id);
            }
            else
            {
                string fallback = d.LoreUnattached.Length > 0 ? d.LoreUnattached : d.Lore;
                lore = fallback.Replace("{place}", place);
            }
            _placed.Add(new PlacedLandmark(_placed.Count, d.Id, d.Name, x, y, id, etype, title, year, place, lore, props));
            _count[d.Id] = _count.GetValueOrDefault(d.Id) + 1;
        }

        string PlaceName(int x, int y, ChronicleEntry? e)
        {
            if (e != null)
                foreach (var sub in e.Subjects)
                {
                    if (sub.Kind != IdKind.Settlement) continue;
                    var hs = _h.GetSettlement(sub);
                    if (hs != null) return hs.Name;
                }
            long best = long.MaxValue; string name = "the district";
            for (int i = 0; i < _sx.Length; i++)
            {
                if (_farm[i]) continue;
                long d2 = Region.Dist2(_sx[i] - x, _sy[i] - y);
                if (d2 < best) { best = d2; name = _r.Settlements[i].Name; }
            }
            return name;
        }

        string PersonOf(ChronicleEntry e)
        {
            foreach (var s in e.Subjects) if (_h.GetNotable(s) is { } n) return n.Full;
            return "someone whose name is lost";
        }

        string RealmOf(ChronicleEntry e)
        {
            foreach (var s in e.Subjects) foreach (var rm in _h.Realms) if (rm.Id == s) return rm.Name;
            return "the older realms";
        }
    }
}
