using Ravensguard.Sim.Core;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Holdings;

/// <summary>One clearing as the Q menu and the status line show it: where the acre is (region metres of its centre), how far along and with how many hands.</summary>
public sealed record ClearingRow(int Serial, int Claim, int Tile, int XM, int YM, ClearingState State, int Trees, int Felled, int Grubbed, int Percent, int Hands, long LogsG, long FirewoodG, long HeroMilli);

/// <summary>
/// What the street view reads of holdings, built at a barrier (immutable to the main thread): the hero's clearings, the claim he stands in (its serial, -1 none), the acre he could mark out there
/// (its tile, -1 none) and whether it is a sowing day.
/// </summary>
public sealed record HoldingsView(int Day, IReadOnlyList<ClearingRow> Clearings, int ClaimHere, int ClearableTile, bool SowingDay, bool FarmedClaim);

public static partial class HoldingsDesk
{
    /// <summary>The holdings as the hero at (<paramref name="xM"/>, <paramref name="yM"/>) region metres sees them (reads only).</summary>
    public static HoldingsView View(HoldingsWorld hw, LandWorld lw, EntityId hero, int xM, int yM, int day)
    {
        int N = RegionData.Size, h = RegionData.TileM;
        var rows = hw.Book.Clearings.Where(c => c.Owner == hero && c.State != ClearingState.Abandoned)
            .Select(c => new ClearingRow(c.Serial, c.Claim, c.Tile, c.Tile % N * h + h / 2, c.Tile / N * h + h / 2, c.State, c.Trees, c.Felled, c.Grubbed, HoldingsWorld.Percent(c), c.Hands.Count, c.LogsG, c.FirewoodG, c.HeroMilli)).ToList();
        int t = LandRules.TileAt(xM, yM); int claimHere = -1, clearable = -1;
        foreach (var cl in lw.Book.Claims)
        {
            if (cl.Claimant != hero || cl.State is not (ClaimState.Open or ClaimState.Deeded)) continue;
            // standing in the claim, or within a short walk of it
            bool near = false;
            foreach (int ct in cl.Tiles) { long dx = (long)(ct % N - xM / h), dy = (long)(ct / N - yM / h); if (dx * dx + dy * dy <= 16) { near = true; break; } }
            if (!near) continue;
            claimHere = cl.Serial;
            long bd = long.MaxValue;
            foreach (int ct in hw.ClearableTiles(cl))
            {
                long dx = ct % N * h + h / 2 - xM, dy = ct / N * h + h / 2 - yM, d2 = dx * dx + dy * dy;
                if (d2 < bd || (d2 == bd && ct < clearable)) { bd = d2; clearable = ct; }
            }
            break;
        }
        _ = t;
        bool sowing = hw.Ew.Farming.SowingSeasonAt(day % TimeConst.DaysPerYear + 1) != null;
        return new HoldingsView(day, rows, claimHere, clearable, sowing, rows.Any(r => r.State is ClearingState.Cleared or ClearingState.Sown));
    }
}
