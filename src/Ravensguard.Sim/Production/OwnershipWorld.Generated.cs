using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Crafting;

namespace Ravensguard.Sim.Production;

// P6-B6 (holdings-and-works-v2 3.8, HW-11): founding a business in a generated building. A house the world generated has no structure record until somebody changes it, so its rooms are recognised
// from its blueprint: the same RoomDetector v2 over the plan's own cells (its fixtures and roof), and the stations those rooms hold are what the building offers.
public sealed partial class OwnershipWorld
{
    /// <summary>The workshop stations of a generated lot's building, read from its blueprint's fixtures by RoomDetector v2 as <see cref="StationsOf"/> reads a structure's, ascending.</summary>
    public SortedDictionary<string, int> StationsOfLot(int settlement, int lot)
    {
        var outp = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var reg = Pop.Region;
        if ((uint)settlement >= (uint)reg.Settlements.Count || (uint)lot >= (uint)reg.Settlements[settlement].Plan.Lots.Count) return outp;
        var plans = PlanLibrary.For(Content);
        if (plans.Grid(reg.Settlements[settlement].Plan.Lots[lot].BlueprintId) is not { } grid) return outp;
        var costs = PieceCosts.Of(Content);
        var rw = plans.Library.NewWorld();
        var edits = new List<CellEdit>();
        foreach (var c in PlanLibrary.Cells(grid, costs)) edits.Add(new CellEdit(c.X, c.Y, c.Z, PieceCosts.IsFixture(costs[c.Piece].Kind) ? CellLayer.Fixture : CellLayer.Structure, c.Piece));
        rw.Apply(edits);
        foreach (var room in rw.Rooms)
        {
            if (room.Primary == null) continue;
            foreach (var (st, n) in room.Stations)
            {
                string kind = Ravensguard.Sim.Crafting.RecipeUnifier.KindOf(Content, st);
                if (Array.IndexOf(RecipeBook.StationKinds, kind) < 0) continue;
                outp[kind] = outp.GetValueOrDefault(kind) + n;
            }
        }
        return outp;
    }

    /// <summary>
    /// The containers that count as a business's store (HW-10): the stores placed inside its premises (the structures it works in) or within 6 m of a cell of them, where the crafter stands at his station.
    /// </summary>
    public IEnumerable<Ravensguard.Sim.Goods.Store> PremisesStores(Business b, ConstructionWorld cw)
    {
        if (H(b) is not { } h || h.Premises.Count == 0) yield break;
        var cells = new List<(int X, int Y, int Z)>();
        foreach (int st in h.Premises) foreach (long k in cw.Rooms.StructureCells(st)) cells.Add(RoomWorld.Unpack(k));
        foreach (var pl in cw.Book.Placed.Values)
        {
            if (!Econ.OtherStores.TryGetValue(pl.Owner, out var store)) continue;
            bool near = h.Premises.Contains(cw.Rooms.StructureAt(pl.X, pl.Y, pl.Z));
            if (!near) foreach (var (x, y, z) in cells) if (z == pl.Z && CraftReach.Mm(x, y, pl.X, pl.Y) <= 6000) { near = true; break; }
            if (near) yield return store;
        }
    }

    /// <summary>The business kinds a generated building offers (the Q menu on a door whose building has not been changed).</summary>
    public List<BusinessKind> KindsForLot(int settlement, int lot) => KindsFor(StationsOfLot(settlement, lot));

    /// <summary>Opens a business in a generated building the hero holds (his deed, a lease): as <see cref="Found"/>, the stations from the blueprint.</summary>
    public OwnResult FoundInLot(string kindId, int settlement, int lot, long capital, int day)
    {
        var stations = StationsOfLot(settlement, lot);
        if (stations.Count == 0) return OwnResult.No("bz_no_stations", kindId);
        var reg = Pop.Region;
        var deed = Land.Registry().LotDeed(settlement, lot);
        int row = HeroRow();
        if (deed == null || (deed.Holder != Hero && !(row >= 0 && Pop.People.Household[row] is int hh && hh >= 0 && deed.Holder == Pop.Households.IdOf(hh)))) return OwnResult.No("bz_not_your_building");
        return Found(kindId, settlement, lot, 0, capital, day, null, stations);
    }
}
