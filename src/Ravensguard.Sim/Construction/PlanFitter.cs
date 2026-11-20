using Ravensguard.Content;
using Ravensguard.Sim.Building;

namespace Ravensguard.Sim.Construction;

/// <summary>A lot to fit a plan to (region metres): its rectangle and the side the street is on (0 north, 1 east, 2 south, 3 west, as a lot's facing; -1 none: a claim or a field).</summary>
public readonly record struct FitLot(int X, int Y, int W, int H, int Street = -1)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    /// <summary>The lot's width along the street (its frontage), or its narrower side without one.</summary>
    public int Frontage => Street is 0 or 2 ? W : Street is 1 or 3 ? H : Math.Min(W, H);
    public int Depth => Street is 0 or 2 ? H : Street is 1 or 3 ? W : Math.Max(W, H);
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>A plan fitted to a lot (spec 4.2): the transform (index = mirror x 4 + quarter turns), the corner in region metres and the transformed grid; or why it does not fit.</summary>
public sealed record PlanFit(bool Ok, int Index, int Turns, bool Mirror, int X, int Y, int W, int H, BuildingGrid? Grid, bool DoorToStreet, string Key, object[] Args)
{
    public static PlanFit No(string key, params object[] args) => new(false, -1, 0, false, 0, 0, 0, 0, null, false, key, args);
}

/// <summary>
/// P5-C: fits a plan to a lot (build-mode-and-construction 4.2). The eight transforms (four quarter turns, each plain and mirrored) are tried; a transform fits when its footprint lies in
/// the lot with no setback on the street side and 1 m elsewhere (burgage plots are built to the street), no ground-floor cell is on a road or ground steeper than 100 pm, and at least one
/// exterior door opens on the ground floor's edge. It stands against the street edge, centred along the street. Among the fits: (1) a front door toward the street, (2) against the
/// street edge and (3) centred (both by construction), (4) the lowest transform index. Deterministic: the same plan and lot give the same transform on every run and machine.
/// </summary>
public static class PlanFitter
{
    public const int SideSetback = 1;

    /// <summary>The transform of an index (0..7): quarter turns clockwise and whether it is mirrored first.</summary>
    public static (int Turns, bool Mirror) Transform(int index) => (index & 3, index >= 4);
    public static int IndexOf(int turns, bool mirror) => (((turns % 4) + 4) % 4) + (mirror ? 4 : 0);

    /// <summary>The best of the eight transforms on the lot, or the reason none fits (the first that applies: too wide, too deep, the ground, no door).</summary>
    public static PlanFit Fit(BuildingGrid g, FitLot lot, PieceCatalog pieces, Func<int, int, bool>? groundOk = null)
    {
        PlanFit? best = null, firstNo = null;
        int narrowest = int.MaxValue, shallowest = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var f = FitAt(g, i, lot, pieces, groundOk);
            if (f.Ok)
            {
                if (best == null || (f.DoorToStreet && !best.DoorToStreet)) best = f;
                if (best.DoorToStreet) break;                                // the lowest index with a door to the street wins
                continue;
            }
            var t = TransformedSize(g, i);
            var (along, deep) = lot.Street is 1 or 3 ? (t.H, t.W) : (t.W, t.H);
            if (lot.Street < 0) (along, deep) = (Math.Min(t.W, t.H), Math.Max(t.W, t.H));
            narrowest = Math.Min(narrowest, along);
            if (along <= UsableAlong(lot)) shallowest = Math.Min(shallowest, deep);
            if (f.Key is not ("cn_fit_too_wide" or "cn_fit_too_deep")) firstNo ??= f;      // the ground or the door: a reason worth more than the size of another turn
        }
        if (best != null) return best;
        if (narrowest > UsableAlong(lot)) return PlanFit.No("cn_fit_too_wide", narrowest, lot.Frontage, SideSetback);
        if (shallowest != int.MaxValue && shallowest > UsableDeep(lot)) return PlanFit.No("cn_fit_too_deep", shallowest, lot.Depth, SideSetback);
        return firstNo ?? PlanFit.No("cn_fit_none");
    }

    private static (int W, int H) TransformedSize(BuildingGrid g, int index) => (index & 1) == 0 ? (g.Width, g.Height) : (g.Height, g.Width);

    private static int UsableAlong(FitLot lot) => lot.Frontage - 2 * SideSetback;
    private static int UsableDeep(FitLot lot) => lot.Depth - (lot.Street < 0 ? 2 : 1) * SideSetback;

    /// <summary>One transform on the lot with the fit check (the player's own rotation and mirror, spec 4.1: Z / X rotate, M mirrors).</summary>
    public static PlanFit FitAt(BuildingGrid g, int index, FitLot lot, PieceCatalog pieces, Func<int, int, bool>? groundOk = null)
    {
        var (turns, mirror) = Transform(index);
        var t = g.Transform(turns, mirror);
        int w = t.Width, h = t.Height;
        // the usable rectangle: no setback on the street side, one metre on the others
        int ux0 = lot.X + (lot.Street == 3 ? 0 : SideSetback), ux1 = lot.Right - (lot.Street == 1 ? 0 : SideSetback);
        int uy0 = lot.Y + (lot.Street == 0 ? 0 : SideSetback), uy1 = lot.Bottom - (lot.Street == 2 ? 0 : SideSetback);
        int uw = ux1 - ux0, uh = uy1 - uy0;
        if (w > uw) return PlanFit.No(lot.Street is 1 or 3 ? "cn_fit_too_deep" : "cn_fit_too_wide", w, lot.Street is 1 or 3 ? lot.Depth : lot.Frontage, SideSetback) with { Index = index, Turns = turns, Mirror = mirror };
        if (h > uh) return PlanFit.No(lot.Street is 1 or 3 ? "cn_fit_too_wide" : "cn_fit_too_deep", h, lot.Street is 1 or 3 ? lot.Frontage : lot.Depth, SideSetback) with { Index = index, Turns = turns, Mirror = mirror };
        // against the street edge, centred along the street (centred both ways without a street)
        int x = ux0 + (uw - w) / 2, y = uy0 + (uh - h) / 2;
        switch (lot.Street)
        {
            case 0: y = uy0; break;
            case 2: y = uy1 - h; break;
            case 1: x = ux1 - w; break;
            case 3: x = ux0; break;
        }
        var ground = t.Level(0);
        if (ground != null && groundOk != null)
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                    if (ground.Structure[ground.Idx(xx, yy)] != null && !groundOk(x + xx, y + yy))
                        return PlanFit.No("cn_fit_ground", x + xx, y + yy) with { Index = index, Turns = turns, Mirror = mirror };
        var det = new RoomDetector(pieces, []);
        bool anyDoor = false, streetDoor = false;
        if (ground != null)
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    if (pieces.KindOf(ground.Structure[ground.Idx(xx, yy)]) != PieceKind.Door || !det.IsExteriorDoor(ground, xx, yy)) continue;
                    bool edge = xx == 0 || yy == 0 || xx == w - 1 || yy == h - 1;
                    if (!edge) continue;
                    anyDoor = true;
                    if (lot.Street switch { 0 => yy == 0, 1 => xx == w - 1, 2 => yy == h - 1, 3 => xx == 0, _ => false }) streetDoor = true;
                }
        if (!anyDoor) return PlanFit.No("cn_fit_no_door") with { Index = index, Turns = turns, Mirror = mirror };
        return new PlanFit(true, index, turns, mirror, x, y, w, h, t, streetDoor || lot.Street < 0, "cn_fit_ok", []);
    }
}
