using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Growth;

/// <summary>
/// The changes background growth makes to a region's records, in one place so a loaded save can make exactly the same ones: a house on a new plot or a vacant lot taken
/// up again (<see cref="ApplyLot"/>), a parcel of cleared fields (<see cref="ApplyParcel"/>), and the searches that find a plot or a clearing. The region is generated from
/// the seed and never stored; the log of these calls (<see cref="GrowthState"/>) is what is saved, and <see cref="Replay"/> puts it back over the regenerated region.
/// Every search walks its candidates in a fixed order and breaks ties by index, so the same world always grows the same way.
/// </summary>
public static class RegionGrowth
{
    /// <summary>Ids of grown parcels and fields start here in the region's serial space, far above the generated ones (62,000 fields), so they never depend on an allocator's state.</summary>
    public const ulong IdBase = 1UL << 32;

    private static int RegionId(RegionData r) => RegionCoord.OfCell(r.CellX, r.CellY).Id;

    // ---------------------------------------------------------------- lots

    /// <summary>Appends a lot to the plan (or takes a vacant one up again) in the region only; the population's per-lot arrays are the caller's to keep (<see cref="AddLot"/>).</summary>
    public static void ApplyLot(RegionData r, LotRecord rec)
    {
        using var _ = r.WriteGrowth();
        r.PinGrowthBaseline();
        var plan = r.Settlements[rec.Settlement].Plan;
        if (rec.NewLot)
        {
            if (rec.Lot != plan.Lots.Count) throw new InvalidOperationException($"grown lot {rec.Lot} of settlement {rec.Settlement} is out of order (the plan has {plan.Lots.Count})");
            plan.Lots.Add(new Lot(rec.Lot, rec.Use, rec.Dwelling, rec.Blueprint, rec.Rect, rec.Facing, rec.Wealth));
            MarkBuilt(r, rec.Rect);
            plan.RadiusM = Math.Max(plan.RadiusM, (int)Region.Isqrt(Region.Dist2(rec.Rect.CenterX - r.Settlements[rec.Settlement].CenterX, rec.Rect.CenterY - r.Settlements[rec.Settlement].CenterY)));
        }
        else
        {
            var old = plan.Lots[rec.Lot];
            // P6-D4: a bare plot bought and built on (blueprint "-") takes the house's use, dwelling and blueprint; a vacant house is only taken up again
            plan.Lots[rec.Lot] = old.BlueprintId == Ravensguard.Sim.Land.LandWorld.BarePlot && rec.Dwelling != DwellingType.None
                ? old with { Use = rec.Use, Dwelling = rec.Dwelling, BlueprintId = rec.Blueprint, Wealth = rec.Wealth, Vacant = false }
                : old with { Vacant = false };
        }
        r.NoteGrown();
    }

    // ---------------------------------------------------------------- streets (P6-D4)

    /// <summary>
    /// A street laid by the lord (holdings 3.4): the plan gains the segment, its corridor is marked built (as a lot's tiles are), and <see cref="FindPlot"/> finds the plots along it whatever the plan's radius is.
    /// The plots themselves are not records: growth and the lord's listings find them with <see cref="FindPlot"/> as on every other street.
    /// </summary>
    public static void ApplyStreet(RegionData r, StreetRecord rec)
    {
        using var _ = r.WriteGrowth();
        r.PinGrowthBaseline();
        var st = r.Settlements[rec.Settlement]; var plan = st.Plan;
        plan.Streets.Add(new Street(rec.A, rec.B, rec.WidthM));
        int pad = rec.WidthM / 2 + StreetPadM;
        MarkBuilt(r, new RectM(Math.Min(rec.A.X, rec.B.X) - pad, Math.Min(rec.A.Y, rec.B.Y) - pad, Math.Abs(rec.A.X - rec.B.X) + 2 * pad, Math.Abs(rec.A.Y - rec.B.Y) + 2 * pad));
        r.NoteGrown();
    }

    /// <summary>How far past a street's edge the tiles marked built reach, metres.</summary>
    public const int StreetPadM = 30;

    /// <summary>The region change of <see cref="ApplyLot"/> plus the population's per-lot arrays (a new slot per array, empty; beds from the record). Returns the lot index.</summary>
    public static int AddLot(RegionData r, Population pop, LotRecord rec)
    {
        using var _ = r.WriteGrowth();                    // the plan and the per-lot arrays change together: a chunk generated meanwhile sees neither or both
        ApplyLot(r, rec);
        int s = rec.Settlement, l = rec.Lot;
        if (rec.NewLot)
        {
            Grow(pop.LotHousehold, s, l + 1, -1); Grow(pop.LotKeeper, s, l + 1, -1); Grow(pop.LotBeds, s, l + 1, 0);
        }
        pop.LotBeds[s][l] = rec.Dwelling == DwellingType.None ? 0 : rec.Beds;
        return l;
    }

    private static void Grow(int[][] a, int s, int n, int fill)
    {
        var old = a[s]; if (old.Length >= n) return;
        var neu = new int[n]; Array.Copy(old, neu, old.Length); for (int i = old.Length; i < n; i++) neu[i] = fill;
        a[s] = neu;
    }

    private static void MarkBuilt(RegionData r, RectM q)
    {
        int x0 = Math.Clamp(q.X / RegionData.TileM, 0, RegionData.Size - 1), x1 = Math.Clamp((q.Right - 1) / RegionData.TileM, 0, RegionData.Size - 1);
        int y0 = Math.Clamp(q.Y / RegionData.TileM, 0, RegionData.Size - 1), y1 = Math.Clamp((q.Bottom - 1) / RegionData.TileM, 0, RegionData.Size - 1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) r.Built[y * RegionData.Size + x] = 1;
    }

    /// <summary>First vacant house of a settlement (lowest lot index) that no project has claimed, or -1: the house stands empty because the village shrank (the Vacant lots of generation).</summary>
    public static int FindVacantLot(RegionData r, int s, ISet<int> claimed)
    {
        var lots = r.Settlements[s].Plan.Lots;
        for (int l = 0; l < lots.Count; l++)
            if (lots[l].Vacant && lots[l].Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage && !claimed.Contains(l)) return l;
        return -1;
    }

    /// <summary>
    /// A free plot on the plan's streets for a house <paramref name="w"/> by <paramref name="d"/> metres: beside a street (either side), clear of every lot, the square, the other
    /// streets, the plots in <paramref name="reserved"/> and the fields, on level dry ground inside the plan's bounds plus <paramref name="margin"/>. The nearest to the
    /// settlement's centre wins; ties go to the lower street and then the lower position. False when the land is full.
    /// </summary>
    public static bool FindPlot(RegionData r, int s, int w, int d, int margin, IReadOnlyList<RectM> reserved, out RectM rect, out byte facing)
    {
        var st = r.Settlements[s]; var plan = st.Plan;
        rect = default; facing = 0;
        long cx = plan.Square.W > 0 ? plan.Square.CenterX : st.CenterX, cy = plan.Square.W > 0 ? plan.Square.CenterY : st.CenterY;
        long maxR = plan.RadiusM + margin, maxR2 = maxR * maxR;
        int baseStreets = r.Baseline?.Streets is { } bs && s < bs.Length ? bs[s] : plan.Streets.Count;          // P6-D4: a street the lord laid is built along its whole length, whatever the plan's radius was
        var cands = new List<(long D2, int Street, int Pos, RectM R, byte F)>();
        for (int si = 0; si < plan.Streets.Count; si++)
        {
            var sr = plan.Streets[si];
            bool horiz = Math.Abs(sr.B.X - sr.A.X) >= Math.Abs(sr.B.Y - sr.A.Y);
            int lo = horiz ? Math.Min(sr.A.X, sr.B.X) : Math.Min(sr.A.Y, sr.B.Y), hi = horiz ? Math.Max(sr.A.X, sr.B.X) : Math.Max(sr.A.Y, sr.B.Y);
            int line = horiz ? (sr.A.Y + sr.B.Y) / 2 : (sr.A.X + sr.B.X) / 2, half = sr.WidthM / 2 + 1;
            int step = Math.Max(4, w / 2);
            for (int side = -1; side <= 1; side += 2)
                for (int u = lo - w; u <= hi; u += step)
                {
                    RectM q; byte f;
                    int v0 = side > 0 ? line + half : line - half - d;
                    if (horiz) { q = new RectM(u, v0, w, d); f = (byte)(side > 0 ? 0 : 2); }
                    else { q = new RectM(v0, u, d, w); f = (byte)(side > 0 ? 3 : 1); }
                    long dx = q.CenterX - cx, dy = q.CenterY - cy, d2 = dx * dx + dy * dy;
                    if (d2 > maxR2 && si < baseStreets) continue;
                    cands.Add((d2, si, u * 2 + (side > 0 ? 1 : 0), q, f));
                }
        }
        cands.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Street != b.Street ? a.Street.CompareTo(b.Street) : a.Pos.CompareTo(b.Pos));
        foreach (var (_, _, _, q, f) in cands)
        {
            if (!PlotFree(r, s, q, reserved)) continue;
            rect = q; facing = f; return true;
        }
        return false;
    }

    /// <summary>
    /// P6-D4: the plots <see cref="FindPlot"/> would give along one street, in its order (nearest the centre first) and packed the way listings and growth take them (each plot reserves its place, so the next
    /// must clear it). <paramref name="street"/> need not be in the plan yet: the planner counts a street's plots before it lays it. The plan's radius is not applied (the street widens it when laid).
    /// </summary>
    public static List<(RectM Rect, byte Facing)> PlotsOnStreet(RegionData r, int s, Street street, int w, int d, int max, IReadOnlyList<RectM>? held = null)
    {
        var st = r.Settlements[s]; var plan = st.Plan; var res = new List<(RectM, byte)>();
        long cx = plan.Square.W > 0 ? plan.Square.CenterX : st.CenterX, cy = plan.Square.W > 0 ? plan.Square.CenterY : st.CenterY;
        bool horiz = Math.Abs(street.B.X - street.A.X) >= Math.Abs(street.B.Y - street.A.Y);
        int lo = horiz ? Math.Min(street.A.X, street.B.X) : Math.Min(street.A.Y, street.B.Y), hi = horiz ? Math.Max(street.A.X, street.B.X) : Math.Max(street.A.Y, street.B.Y);
        int line = horiz ? (street.A.Y + street.B.Y) / 2 : (street.A.X + street.B.X) / 2, half = street.WidthM / 2 + 1, step = Math.Max(4, w / 2);
        var cands = new List<(long D2, int Pos, RectM R, byte F)>();
        for (int side = -1; side <= 1; side += 2)
            for (int u = lo - w; u <= hi; u += step)
            {
                RectM q; byte f; int v0 = side > 0 ? line + half : line - half - d;
                if (horiz) { q = new RectM(u, v0, w, d); f = (byte)(side > 0 ? 0 : 2); } else { q = new RectM(v0, u, d, w); f = (byte)(side > 0 ? 3 : 1); }
                long dx = q.CenterX - cx, dy = q.CenterY - cy;
                cands.Add((dx * dx + dy * dy, u * 2 + (side > 0 ? 1 : 0), q, f));
            }
        cands.Sort((a, b) => a.D2 != b.D2 ? a.D2.CompareTo(b.D2) : a.Pos.CompareTo(b.Pos));
        var reserved = new List<RectM>(held ?? []);
        foreach (var (_, _, q, f) in cands)
        {
            if (res.Count >= max) break;
            if (!PlotFree(r, s, q, reserved, street)) continue;
            reserved.Add(q); res.Add((q, f));
        }
        return res;
    }

    private static bool Overlaps(RectM a, RectM b, int gap) => a.X < b.Right + gap && b.X < a.Right + gap && a.Y < b.Bottom + gap && b.Y < a.Bottom + gap;

    public static bool PlotFree(RegionData r, int s, RectM q, IReadOnlyList<RectM> reserved, Street? extra = null)
    {
        if (q.X < 96 || q.Y < 96 || q.Right >= RegionData.Meters - 96 || q.Bottom >= RegionData.Meters - 96) return false;
        var plan = r.Settlements[s].Plan;
        if (plan.Square.W > 0 && Overlaps(q, plan.Square, 1)) return false;
        foreach (var l in plan.Lots) if (Overlaps(q, l.Rect, 1)) return false;
        foreach (var rv in reserved) if (Overlaps(q, rv, 1)) return false;
        foreach (var st in extra == null ? plan.Streets : plan.Streets.Append(extra))
        {
            var sr = new RectM(Math.Min(st.A.X, st.B.X) - st.WidthM / 2, Math.Min(st.A.Y, st.B.Y) - st.WidthM / 2, Math.Abs(st.A.X - st.B.X) + st.WidthM, Math.Abs(st.A.Y - st.B.Y) + st.WidthM);
            if (Overlaps(q, sr, 0)) return false;
        }
        // other settlements' plots and squares near this one (the plans of two villages never touch at generation; a plot must keep it so)
        for (int o = 0; o < r.Settlements.Count; o++)
        {
            if (o == s) continue; var op = r.Settlements[o].Plan;
            if (Math.Abs(r.Settlements[o].CenterX - q.CenterX) > 400 || Math.Abs(r.Settlements[o].CenterY - q.CenterY) > 400) continue;
            if (op.Square.W > 0 && Overlaps(q, op.Square, 2)) return false;
            foreach (var l in op.Lots) if (Overlaps(q, l.Rect, 2)) return false;
        }
        int cx = q.CenterX, cy = q.CenterY, x1 = q.Right - 1, y1 = q.Bottom - 1;
        foreach (var (px, py) in new[] { (q.X, q.Y), (x1, q.Y), (q.X, y1), (x1, y1), (cx, cy), (cx, q.Y), (cx, y1), (q.X, cy), (x1, cy) })
        {
            int t = (py / RegionData.TileM) * RegionData.Size + px / RegionData.TileM;
            if (r.Water[t] != 0 || r.SlopePermille[t] > 140 || r.ParcelMap[t] != 0) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- fields

    /// <summary>
    /// Appends a parcel of cleared fields to a settlement in the region only (<see cref="Region.Fields"/> and <see cref="Region.Parcels"/> grow, the tiles are marked in
    /// <see cref="Region.ParcelMap"/> and lose their forest). Returns the parcel index. The farming and population tables are the caller's to extend.
    /// </summary>
    public static int ApplyParcel(RegionData r, ParcelRecord rec)
    {
        using var _ = r.WriteGrowth();
        r.PinGrowthBaseline();
        int pidx = r.Parcels.Count, start = r.Fields.Length, n = rec.Tiles.Length;
        int reg = RegionId(r);
        var fields = new Field[start + n];
        Array.Copy(r.Fields, fields, start);
        long fsum = 0, sx = 0, sy = 0;
        for (int k = 0; k < n; k++)
        {
            int t = rec.Tiles[k];
            fields[start + k] = new Field { Id = EntityId.MakeIn(IdKind.Field, reg, IdBase + (ulong)(start + k)), Tile = t, Parcel = pidx, Fertility = rec.Fertility[k], Slot = rec.Slot, Crop = Crop.Fallow };
            r.ParcelMap[t] = pidx + 1; r.Forest[t] = 0;
            fsum += rec.Fertility[k]; sx += t % RegionData.Size; sy += t / RegionData.Size;
        }
        r.Fields = fields;
        r.Parcels.Add(new Parcel
        {
            Id = EntityId.MakeIn(IdKind.Parcel, reg, IdBase + (ulong)pidx), Settlement = rec.Settlement, Owner = rec.Owner, Slot = rec.Slot, FieldStart = start, FieldCount = n,
            AvgFertility = n == 0 ? 0 : (int)(fsum / n), TileX = n == 0 ? 0 : (int)(sx / n), TileY = n == 0 ? 0 : (int)(sy / n),
        });
        var st = r.Settlements[rec.Settlement];
        st.Parcels.Add(pidx); st.FieldAcres += n;
        r.NoteGrown();
        return pidx;
    }

    /// <summary>
    /// Tiles to clear for a settlement: land that is woodland (tree cover 30 or more) or waste (too poor for pasture, fertile enough to plough), level, dry, unbuilt, off the
    /// roads and mines, within <paramref name="maxRadiusM"/> of the centre and touching one of the settlement's fields (or a tile already chosen), nearest first, ties by tile index.
    /// Up to <paramref name="count"/> tiles, ascending. Empty when none touches the fields.
    /// </summary>
    public static int[] FindClearing(RegionData r, int s, int count, int minFertility, int maxSlopePermille, int maxRadiusM, ISet<int> claimed)
    {
        var st = r.Settlements[s]; int N = RegionData.Size;
        long r2 = (long)maxRadiusM * maxRadiusM;
        long D2(int t) { long dx = (t % N) * (long)RegionData.TileM + RegionData.TileM / 2 - st.CenterX, dy = (t / N) * (long)RegionData.TileM + RegionData.TileM / 2 - st.CenterY; return dx * dx + dy * dy; }
        bool Eligible(int t)
        {
            if (claimed.Contains(t) || r.ParcelMap[t] != 0 || r.Built[t] != 0 || r.Road[t] != 0 || r.Water[t] != 0 || r.Mineral[t] != 0) return false;
            if (r.SlopePermille[t] > maxSlopePermille || r.Fertility[t] < minFertility) return false;
            if (D2(t) > r2) return false;
            return r.Forest[t] >= 30 || r.Fertility[t] < 40;
        }
        var seeds = new SortedSet<(long D2, int T)>();
        foreach (int p in st.Parcels)
        {
            var pc = r.Parcels[p];
            for (int k = 0; k < pc.FieldCount; k++)
            {
                int t = r.Fields[pc.FieldStart + k].Tile, x = t % N, y = t / N;
                foreach (var (dx, dy) in N4)
                {
                    int nx = x + dx, ny = y + dy; if ((uint)nx >= N || (uint)ny >= N) continue;
                    int nt = ny * N + nx; if (Eligible(nt)) seeds.Add((D2(nt), nt));
                }
            }
        }
        if (seeds.Count == 0) return [];
        var chosen = new HashSet<int>(); var frontier = seeds;
        while (chosen.Count < count && frontier.Count > 0)
        {
            var first = frontier.Min; frontier.Remove(first);
            if (!chosen.Add(first.T)) continue;
            int x = first.T % N, y = first.T / N;
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy; if ((uint)nx >= N || (uint)ny >= N) continue;
                int nt = ny * N + nx; if (!chosen.Contains(nt) && Eligible(nt)) frontier.Add((D2(nt), nt));
            }
        }
        var res = chosen.ToArray(); Array.Sort(res);
        return res;
    }
    private static readonly (int Dx, int Dy)[] N4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    // ---------------------------------------------------------------- a whole change

    /// <summary>
    /// One growth change as a unit (review finding 1): the visited chunks the <paramref name="touched"/> rectangles reach are generated before it, <paramref name="apply"/> changes the region,
    /// and their deltas are written after it, all under the region's <see cref="RegionData.GrowthLock"/>. A chunk generated on another thread meanwhile waits; the streamer re-makes one that
    /// was generated before the change and recorded after it (it checks <see cref="RegionData.GrowthVersion"/>), so a first visit always agrees with the records.
    /// </summary>
    public static T Change<T>(RegionData r, IGrowthChunkSink? sink, IReadOnlyList<RectM> touched, Func<T> apply)
    {
        using var _ = r.WriteGrowth();
        var tx = sink?.Begin(touched);
        var res = apply();
        tx?.Commit();
        return res;
    }

    // ---------------------------------------------------------------- replay

    /// <summary>
    /// Puts the grown lots and parcels of a saved <see cref="GrowthState"/> back over a freshly generated region, in the order they were made. The population's per-lot
    /// arrays, the farming tables and the resource layer come from their own saved sections (the layer's cleared rows from <see cref="GrowthState.ClearedTiles"/>).
    /// </summary>
    public static void Replay(RegionData r, GrowthState st)
    {
        // the two logs interleave only through the tiles (lots never share tiles with fields), so replaying lots first and parcels second gives the same region
        // idempotent: a region that already carries a record (a world resumed over the terrain it was captured from) is left as it is
        var seenStreets = new int[r.Settlements.Count];
        foreach (var sr in st.Streets)
        {
            int baseStreets = r.Baseline?.Streets is { } bs && sr.Settlement < bs.Length ? bs[sr.Settlement] : r.Settlements[sr.Settlement].Plan.Streets.Count;
            if (r.Settlements[sr.Settlement].Plan.Streets.Count > baseStreets + seenStreets[sr.Settlement]++) continue;
            ApplyStreet(r, sr);
        }
        foreach (var l in st.Lots) { if (l.NewLot && r.Settlements[l.Settlement].Plan.Lots.Count > l.Lot) continue; ApplyLot(r, l); }
        for (int k = 0; k < st.Parcels.Count; k++)
        {
            int baseParcels = r.Baseline?.Parcels ?? r.Parcels.Count;
            if (r.Parcels.Count > baseParcels + k) continue;
            ApplyParcel(r, st.Parcels[k]);
        }
    }
}
