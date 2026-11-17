using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

public sealed partial class ChunkGenerator
{
    /// <summary>
    /// A lot seen in street-relative coordinates: a runs along the frontage, d runs from the street edge into the plot (so the same layout code
    /// serves every facing). Everything it draws stays inside the lot rectangle and is a pure function of the lot, so seams never matter.
    /// </summary>
    private sealed class YardCanvas
    {
        public readonly Stamp St; public readonly RectM Rect; public readonly int F, FL, DL;
        public int Ba0, Ba1, Bd0, Bd1; // the building's extent in (a, d)
        private readonly HashSet<(int, int)> _used = [];
        private readonly ulong _hv;

        public YardCanvas(Stamp st, RectM rect, int facing, ulong hv)
        {
            St = st; Rect = rect; F = facing & 3; _hv = hv;
            FL = (F & 1) == 0 ? rect.W : rect.H; DL = (F & 1) == 0 ? rect.H : rect.W;
            var (a0, d0) = Loc(st.X0, st.Y0); var (a1, d1) = Loc(st.X0 + st.W - 1, st.Y0 + st.H - 1);
            Ba0 = Math.Min(a0, a1); Ba1 = Math.Max(a0, a1); Bd0 = Math.Min(d0, d1); Bd1 = Math.Max(d0, d1);
            foreach (var (x, y, _, _) in st.Ground) _used.Add((x, y)); // the door paths
        }

        public (int X, int Y) W(int a, int d) => F switch
        {
            0 => (Rect.X + a, Rect.Y + d),
            2 => (Rect.X + a, Rect.Bottom - 1 - d),
            1 => (Rect.Right - 1 - d, Rect.Y + a),
            _ => (Rect.X + d, Rect.Y + a),
        };

        public (int A, int D) Loc(int x, int y) => F switch
        {
            0 => (x - Rect.X, y - Rect.Y),
            2 => (x - Rect.X, Rect.Bottom - 1 - y),
            1 => (y - Rect.Y, Rect.Right - 1 - x),
            _ => (y - Rect.Y, x - Rect.X),
        };

        public ulong R(int salt) => DetRng.Mix(_hv ^ ((ulong)(uint)salt * 0x9E3779B97F4A7C15UL));
        public int Rn(int salt, int n) => (int)(R(salt) % (ulong)Math.Max(1, n));

        public bool Inside(int a, int d) => a >= 0 && a < FL && d >= 0 && d < DL;
        public bool OnBuilding(int a, int d, int margin) => a >= Ba0 - margin && a <= Ba1 + margin && d >= Bd0 - margin && d <= Bd1 + margin;
        public bool Clear(int a, int d, int margin = 1)
        {
            if (!Inside(a, d) || OnBuilding(a, d, margin)) return false;
            return !_used.Contains(W(a, d));
        }

        public bool Ground(int a, int d, GroundKind g, int v, int margin = 1)
        {
            if (!Clear(a, d, margin)) return false;
            var (x, y) = W(a, d);
            St.Ground.Add((x, y, g, v));
            return true;
        }

        /// <summary>A prop on a clear cell; the cell is then taken. Wide props also take the cell to the east.</summary>
        public bool Prop(int a, int d, PropKind k, int v, bool wide = false, int margin = 1)
        {
            if (!Clear(a, d, margin)) return false;
            var (x, y) = W(a, d);
            if (wide)
            {
                if (_used.Contains((x + 1, y)) || !Rect.Overlaps(new RectM(x + 1, y, 1, 1))) return false;
                if (x + 1 >= St.X0 - 1 && x + 1 <= St.X0 + St.W && y >= St.Y0 - 1 && y <= St.Y0 + St.H) return false;
                _used.Add((x + 1, y));
            }
            _used.Add((x, y));
            St.Props.Add((x, y, k, v));
            return true;
        }

        /// <summary>A boundary piece (wall, gate) that may stand on a door path: only the building itself keeps it out.</summary>
        public bool Line(int a, int d, PropKind k)
        {
            if (!Inside(a, d) || OnBuilding(a, d, 0)) return false;
            var (x, y) = W(a, d);
            _used.Add((x, y));
            St.Props.Add((x, y, k, 0));
            return true;
        }

        /// <summary>Marks cells as taken without drawing (a gate gap, a path).</summary>
        public void Reserve(int a, int d) { if (Inside(a, d)) _used.Add(W(a, d)); }
    }

    private static PropKind PickGood(ulong h, params PropKind[] kinds) => kinds[(int)(h % (ulong)kinds.Length)];

    /// <summary>Everything around a building that makes the plot look lived in: yard, garden, orchard, croft, fences, the churchyard, props.</summary>
    private void Yard(Stamp st, Lot lot, RegionSettlement rs, RectM rect, ulong hv)
    {
        var c = new YardCanvas(st, rect, lot.Facing, hv);
        bool town = rs.Class == SettlementClass.Town;
        switch (lot.Use)
        {
            case LotUse.Church or LotUse.Chapel: Churchyard(c, lot); break;
            case LotUse.Manor: ManorGrounds(c, lot, rs); break;
            case LotUse.Mill: MillYard(c, lot); break;
            case LotUse.Barn: FarmYard(c, lot, rs); break;
            case LotUse.Granary: WorkYard(c, lot, town, PropKind.Sheaves, PropKind.Sacks, PropKind.Cart); break;
            case LotUse.Smithy: WorkYard(c, lot, town, PropKind.Woodpile, PropKind.Barrel, PropKind.Trough); break;
            case LotUse.Tavern: Tavern(c, lot, town); break;
            default:
                if (town) BurgagePlot(c, lot); else Toft(c, lot, rs);
                break;
        }
    }

    // ------------------------------------------------------------------ fences and boundaries

    private static void SideFences(YardCanvas c, int fromD, PropKind kind, int gapEighths, bool bothSides)
    {
        for (int d = fromD; d < c.DL; d++)
        {
            if (c.Rn(100 + d, 8) >= gapEighths) c.Prop(0, d, kind, d & 3, false, 0);
            if (bothSides && c.Rn(300 + d, 8) >= gapEighths + 2) c.Prop(c.FL - 1, d, kind, d & 3, false, 0);
        }
    }

    private static void BackBoundary(YardCanvas c, PropKind kind, int gatePct)
    {
        int gate = c.FL / 2 + c.Rn(7, 3) - 1;
        bool hasGate = c.Rn(8, 100) < gatePct;
        for (int a = 0; a < c.FL; a++)
        {
            if (hasGate && Math.Abs(a - gate) <= 0) continue;
            if (c.Rn(500 + a, 12) == 0) continue;
            c.Prop(a, c.DL - 1, kind, a & 3, false, 0);
        }
    }

    // ------------------------------------------------------------------ garden beds, orchards, crofts, pens

    private static void Beds(YardCanvas c, int a0, int a1, int d0, int d1, bool scarecrow)
    {
        // rows of vegetables and herbs with a path between each pair
        for (int d = d0; d <= d1; d++)
        {
            int row = (d - d0) % 3;
            for (int a = a0; a <= a1; a++)
            {
                if (row == 2) c.Ground(a, d, GroundKind.Dirt, (a + d) & 3, 0);
                else if (row == 0) c.Ground(a, d, GroundKind.Furrow, (a * 7 + d) & 3, 0);
                else c.Ground(a, d, GroundKind.Sown, (int)(c.R(60 + (d - d0) / 3) % 2 == 0 ? Crop.Peas : Crop.Beans), 0);
            }
        }
        if (scarecrow) c.Prop((a0 + a1) / 2, (d0 + d1) / 2, PropKind.Scarecrow, 0, false, 0);
    }

    private static void Orchard(YardCanvas c, int a0, int a1, int d0, int d1, int spacing)
    {
        for (int d = d0; d <= d1; d += spacing)
            for (int a = a0 + (((d - d0) / spacing) & 1) * (spacing / 2); a <= a1; a += spacing)
            {
                int ja = (int)(c.R(a * 131 + d) % 3) - 1, jd = (int)(c.R(a * 71 + d * 17) % 3) - 1;
                c.Prop(a + ja, d + jd, PropKind.FruitTree, (a + d) & 3, false, 1);
            }
    }

    /// <summary>A strip of the open field growing right in the plot: rows of one crop running away from the street.</summary>
    private static void Croft(YardCanvas c, int a0, int a1, int d0, int d1)
    {
        var crop = (Crop)(1 + c.Rn(77, 4)); // wheat, rye, barley, oats
        for (int a = a0; a <= a1; a++)
            for (int d = d0; d <= d1; d++)
            {
                if ((a - a0) % 3 == 0) c.Ground(a, d, GroundKind.Furrow, (a + d) & 3, 0);
                else c.Ground(a, d, GroundKind.Crop, (int)crop, 0);
            }
    }

    private static void Pen(YardCanvas c, int a0, int d0, int w, int h)
    {
        for (int a = a0; a < a0 + w; a++) { c.Prop(a, d0, PropKind.Hurdle, 0, false, 0); c.Prop(a, d0 + h - 1, PropKind.Hurdle, 1, false, 0); }
        for (int d = d0 + 1; d < d0 + h - 1; d++) { c.Prop(a0, d, PropKind.Hurdle, 2, false, 0); c.Prop(a0 + w - 1, d, PropKind.Hurdle, 3, false, 0); }
        for (int a = a0 + 1; a < a0 + w - 1; a++) for (int d = d0 + 1; d < d0 + h - 1; d++) c.Ground(a, d, GroundKind.Mud, (a + d) & 1, 0);
        c.Prop(a0 + 1, d0 + 1, PropKind.Hay, 0, false, 0);
    }

    private static void Scatter(YardCanvas c, int a0, int a1, int d0, int d1, int salt, params PropKind[] kinds)
    {
        if (a1 < a0 || d1 < d0) return;
        foreach (var k in kinds)
            for (int tries = 0; tries < 6; tries++)
            {
                ulong h = c.R(salt * 97 + (int)k * 13 + tries);
                int a = a0 + (int)(h % (ulong)(a1 - a0 + 1)), d = d0 + (int)(h >> 20) % (d1 - d0 + 1);
                if (c.Prop(a, d, k, (int)(h >> 40) & 3, k == PropKind.Cart, 1)) break;
            }
    }

    private static void Worn(YardCanvas c, int a0, int a1, int d0, int d1, GroundKind g = GroundKind.Dirt)
    {
        for (int d = d0; d <= d1; d++)
            for (int a = a0; a <= a1; a++)
                if (c.Rn(900 + a * 31 + d, 5) != 0) c.Ground(a, d, g, (a * 3 + d) & 3, 0);
    }

    // ------------------------------------------------------------------ town: a burgage plot, house at the street, yard, beds and trees behind

    private void BurgagePlot(YardCanvas c, Lot lot)
    {
        int back = c.Bd1 + 1;
        SideFences(c, Math.Max(0, c.Bd1 - 1), PropKind.Hurdle, 1, c.Rn(1, 100) < 40);
        BackBoundary(c, PropKind.Hurdle, 50);
        Worn(c, 1, c.FL - 2, back, back + 2);
        int rem = c.DL - 1 - back;
        if (rem < 6) { Scatter(c, 1, c.FL - 2, back, back + 1, 1, PropKind.Barrel); return; }
        Scatter(c, 1, c.FL - 2, back, back + 2, 2, PickGood(c.R(3), PropKind.Woodpile, PropKind.Barrel, PropKind.Crate), PickGood(c.R(4), PropKind.Trough, PropKind.Sacks, PropKind.Barrel));
        if (lot.Vacant) return;
        int gs = back + 4, ge = Math.Min(c.DL - 6, gs + Math.Max(3, (rem - 8) * 6 / 10));
        if (ge >= gs + 2) Beds(c, 1, c.FL - 2, gs, ge, c.Rn(5, 6) == 0);
        if (c.DL - 3 > ge + 2) Orchard(c, 1, c.FL - 2, ge + 2, c.DL - 3, 4);
        if (c.Rn(9, 100) < 30 && rem > 14) Pen(c, c.FL - 5, c.DL - 5, 4, 4);
    }

    private void Tavern(YardCanvas c, Lot lot, bool town)
    {
        int back = c.Bd1 + 1;
        SideFences(c, Math.Max(0, c.Bd1 - 1), PropKind.Hurdle, 1, true);
        BackBoundary(c, PropKind.Hurdle, 80);
        Worn(c, 1, c.FL - 2, back, back + Math.Min(8, c.DL - back - 3));
        Scatter(c, 1, c.FL - 2, back, back + 8, 20, PropKind.Cart, PropKind.Barrel, PropKind.Barrel, PropKind.Trough, PropKind.Haystack, PropKind.Bench, PropKind.Crate);
        // the inn sign hangs by the door
        if (c.St.FrontDoors.Count > 0)
        {
            var (dx, dy) = c.St.FrontDoors[0];
            var (da, dd) = c.Loc(dx, dy);
            c.Prop(da + 2, Math.Max(0, dd - 1), PropKind.Sign, 1, false, 0);
            c.Prop(da - 2, Math.Max(0, dd - 1), PropKind.Bench, 0, false, 0);
        }
    }

    private void WorkYard(YardCanvas c, Lot lot, bool town, PropKind a, PropKind b, PropKind d3)
    {
        int back = c.Bd1 + 1;
        SideFences(c, Math.Max(0, c.Bd1 - 1), town ? PropKind.Hurdle : PropKind.Hurdle, 1, c.Rn(2, 100) < 50);
        BackBoundary(c, town ? PropKind.Hurdle : PropKind.Hedge, 60);
        Worn(c, 1, c.FL - 2, back, Math.Min(c.DL - 3, back + 6), GroundKind.Gravel);
        Scatter(c, 1, c.FL - 2, back, Math.Min(c.DL - 3, back + 7), 30, a, b, d3, a, b);
        Scatter(c, 1, c.FL - 2, 0, c.Bd0, 31, PropKind.Barrel);
    }

    private void MillYard(YardCanvas c, Lot lot)
    {
        Worn(c, 1, c.FL - 2, 0, c.DL - 2, GroundKind.Dirt);
        Scatter(c, 1, c.FL - 2, 0, c.DL - 2, 40, PropKind.Sacks, PropKind.Sacks, PropKind.Cart, PropKind.Barrel, PropKind.Woodpile, PropKind.Sheaves);
    }

    // ------------------------------------------------------------------ villages and farmsteads: toft and croft

    private void Toft(YardCanvas c, Lot lot, RegionSettlement rs)
    {
        bool farm = rs.Class == SettlementClass.Farmstead;
        int back = c.Bd1 + 1;
        int total = c.DL - 1 - back;
        SideFences(c, 1, PropKind.Hurdle, 1, true);
        BackBoundary(c, PropKind.Hedge, 55);
        // yard by the house: trampled earth, woodpile, a haystack, a cart or trough
        Worn(c, 1, c.FL - 2, back, back + 3);
        Scatter(c, 1, c.FL - 2, Math.Max(1, c.Bd0 - 1), back + 4, 50, PropKind.Woodpile, PropKind.Haystack, lot.Use == LotUse.Longhouse || farm ? PropKind.Cart : PropKind.Barrel, PropKind.Trough, PropKind.Barrel);
        if (lot.Vacant) return;
        int gs = back + 5, ge = gs + Math.Clamp(total / 5, 3, 7);
        if (total >= 14) Beds(c, 1, c.FL - 2, gs, ge, c.Rn(5, 5) == 0);
        int os = ge + 2;
        int crofts = total >= 26 ? Math.Min(12, total / 3) : 0;
        int oe = c.DL - 3 - (crofts > 0 ? crofts + 1 : 0);
        if (oe > os + 1) Orchard(c, 1, c.FL - 2, os, oe, farm ? 4 : 5);
        if (crofts >= 6) Croft(c, 1, c.FL - 2, c.DL - 2 - crofts, c.DL - 2);
        if (c.Rn(9, 100) < (farm ? 80 : 35) && total >= 14) Pen(c, c.FL - 6, back + 1, 5, 4);
    }

    private void FarmYard(YardCanvas c, Lot lot, RegionSettlement rs)
    {
        Worn(c, 1, c.FL - 2, 1, c.DL - 2, GroundKind.Dirt);
        SideFences(c, 1, PropKind.Hurdle, 1, true);
        BackBoundary(c, PropKind.Hurdle, 70);
        int back = c.Bd1 + 1;
        if (c.DL - back >= 6) Pen(c, 1 + c.Rn(11, Math.Max(1, c.FL - 9)), back + 1, 6, 4);
        Scatter(c, 1, c.FL - 2, 1, c.DL - 3, 60, PropKind.Haystack, PropKind.Haystack, PropKind.Cart, PropKind.Trough, PropKind.Woodpile, PropKind.Sacks);
    }

    // ------------------------------------------------------------------ the churchyard: wall, lychgate, graves, yews

    private void Churchyard(YardCanvas c, Lot lot)
    {
        int gateA = c.FL / 2;
        if (c.St.FrontDoors.Count > 0) { var (dx, dy) = c.St.FrontDoors[0]; gateA = c.Loc(dx, dy).A; }
        for (int a = 0; a < c.FL; a++)
        {
            if (a != gateA) c.Line(a, 0, PropKind.LowWall);
            c.Line(a, c.DL - 1, PropKind.LowWall);
        }
        for (int d = 1; d < c.DL - 1; d++) { c.Line(0, d, PropKind.LowWall); c.Line(c.FL - 1, d, PropKind.LowWall); }
        c.Line(gateA, 0, PropKind.Lychgate);
        // the path from the gate to the door
        for (int d = 0; d < Math.Max(1, c.Bd0); d++) c.Ground(gateA, d, GroundKind.Gravel, d & 3, 0);
        // graves in rows, the headstone at the head, a mound before it, leaning a little out of true by variant
        for (int d = 2; d < c.DL - 3; d += 3)
            for (int a = 2; a < c.FL - 2; a += 2)
            {
                if (c.OnBuilding(a, d, 2) || c.OnBuilding(a, d + 1, 2)) continue;
                if (Math.Abs(a - gateA) <= 1 && d < c.Bd0 + 1) continue;
                ulong h = c.R(a * 977 + d * 131);
                if (h % 100 >= 42) continue;
                if (!c.Clear(a, d, 0) || !c.Clear(a, d + 1, 0)) continue;
                c.Prop(a, d, PropKind.Headstone, (int)(h >> 8) % 3, false, 0);
                c.Prop(a, d + 1, PropKind.GraveMound, 0, false, 0);
            }
        // a couple of yews at the back
        c.Prop(2, c.DL - 3, PropKind.Tree, 1, false, 0);
        c.Prop(c.FL - 3, c.DL - 3, PropKind.Tree, 2, false, 0);
        if (c.Rn(3, 2) == 0) c.Prop(c.FL - 3, 2, PropKind.Tree, 0, false, 0);
    }

    // ------------------------------------------------------------------ the manor: walled court, kitchen garden, orchard

    private void ManorGrounds(YardCanvas c, Lot lot, RegionSettlement rs)
    {
        int gate = c.FL / 2;
        if (c.St.FrontDoors.Count > 0) { var (dx, dy) = c.St.FrontDoors[0]; gate = c.Loc(dx, dy).A; }
        for (int a = 0; a < c.FL; a++)
        {
            if (Math.Abs(a - gate) > 1) c.Line(a, 0, PropKind.LowWall);
            c.Line(a, c.DL - 1, PropKind.LowWall);
        }
        for (int d = 1; d < c.DL - 1; d++) { c.Line(0, d, PropKind.LowWall); c.Line(c.FL - 1, d, PropKind.LowWall); }
        c.Line(gate, 0, PropKind.Gate);
        // cobbled court in front, a wide trampled yard to the side with the working goods
        for (int d = 0; d < c.Bd0; d++) for (int a = Math.Max(1, c.Ba0 - 2); a <= Math.Min(c.FL - 2, c.Ba1 + 2); a++) c.Ground(a, d, GroundKind.Cobble, (a + d) & 3, 0);
        int back = c.Bd1 + 1;
        Worn(c, 1, c.FL - 2, back, back + 3);
        Scatter(c, 1, c.FL - 2, back, back + 5, 70, PropKind.Woodpile, PropKind.Cart, PropKind.Haystack, PropKind.Barrel, PropKind.Trough, PropKind.Sacks);
        Scatter(c, 1, c.FL - 2, 1, Math.Max(1, c.Bd0), 71, PropKind.Trough, PropKind.Barrel);
        int gs = back + 5, ge = Math.Min(c.DL - 8, gs + 6);
        if (ge >= gs + 2) Beds(c, 2, c.FL / 2, gs, ge, false);
        if (c.DL - 3 > ge + 2) Orchard(c, 2, c.FL - 3, ge + 2, c.DL - 3, 4);
        // lime trees along the walls
        for (int d = 3; d < c.DL - 3; d += 5) { c.Prop(2, d, PropKind.Tree, 1, false, 1); c.Prop(c.FL - 3, d, PropKind.Tree, 2, false, 1); }
    }
}
