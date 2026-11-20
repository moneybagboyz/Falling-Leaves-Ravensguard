using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;

namespace Ravensguard.Sim.Construction;

// P5-D: what background growth made visible (GrowthSites) asks of the construction world: a plan laid out on a plot without the land rule (the region's own growth), pieces counted as
// built from the abstract hours already worked, the finished structure handed to the generated lot, a site given up.
public sealed partial class ConstructionWorld
{
    /// <summary>A growth plan's ghosts, all or nothing, without the land rule (the plot is the settlement's own, given by the planner): the payer household owns and pays.</summary>
    internal PlaceResult PlaceGrowth(EntityId owner, PurseKind ownerKind, IReadOnlyList<PlaceCell> cells, string blueprint, long nowSec)
    {
        var may = May;
        May = null;
        try { return Place(owner, ownerKind, -1, cells, nowSec, default, allOrNothing: true, blueprint); }
        finally { May = may; }
    }

    /// <summary>The build order of a site's ghosts (the order a crew takes them in when everything is ready: level, kind, distance from the pile, id).</summary>
    internal List<Ghost> BuildOrder(ConstructionSite s) =>
        [.. GhostsOf(s.Id).Where(g => g.Work == GhostWork.Build).OrderBy(g => g.Z).ThenBy(g => KindRank(CostOf(g).Kind)).ThenBy(g => Math.Abs(g.X - s.PileX) + Math.Abs(g.Y - s.PileY)).ThenBy(g => g.Id)];

    /// <summary>Whether a ghost could stand now on what is built (its support, or the floor under a fixture).</summary>
    internal bool Stands(Ghost g)
    {
        if (g.Slot == GhostSlot.Fixture) return BuiltStructure(g.X, g.Y, g.Z) is { } f && PieceCatalog.IsRoomKind(f.Kind);
        return Support.Check(g.X, g.Y, g.Z, CostOf(g), BuiltView()).Ok;
    }

    /// <summary>
    /// A piece the abstract hours already built (spec 4.9 mapping): it becomes a built cell, a tile and a room cell as a finished piece does; its materials were consumed by the abstract
    /// build, so the book counts them as built in (<see cref="ConstructionBook.MaterialisedG"/>) without destroying goods again.
    /// </summary>
    internal void BuiltByGrowth(Ghost g, long sec)
    {
        var s = Site(g.Site)!;
        var c = CostOf(g);
        foreach (var (good, grams) in MaterialIds(c)) if (good != ushort.MaxValue) Book.MaterialisedG[good] = Book.MaterialisedG.GetValueOrDefault(good) + grams;
        var b = new BuiltCell { X = g.X, Y = g.Y, Z = g.Z, Slot = g.Slot, Piece = g.Piece, Hp = PieceCosts.BuiltHp(c, 50), BuiltSkill = 50, Site = s.Id, BuiltSec = sec };
        AddBuilt(b);
        g.State = GhostState.Built;
        DropGhost(g);
        Rooms.Apply([new CellEdit(g.X, g.Y, g.Z, g.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, g.Piece)]);
        WriteTile(g.X, g.Y, g.Z);
        s.PiecesBuilt++;
        _builtVersion++;
    }

    /// <summary>
    /// A piece the abstract hours had started: its materials (consumed by the abstract build) are made again into the ghost's own store and counted in and out (net nothing built yet),
    /// and the work done so far is kept.
    /// </summary>
    internal void StartedByGrowth(Ghost g, long workDone)
    {
        var c = CostOf(g);
        var own = Econ.OpenStore(StoreKind.Open, g.Id);
        foreach (var (good, grams) in MaterialIds(c))
        {
            if (good == ushort.MaxValue) continue;
            own.Create(good, grams, CreateReason.Dematerialised);
            Book.MaterialisedG[good] = Book.MaterialisedG.GetValueOrDefault(good) + grams;
            Book.DematerialisedG[good] = Book.DematerialisedG.GetValueOrDefault(good) + grams;
        }
        g.Materialised = true; g.WorkDone = Math.Clamp(workDone, 0, g.WorkNeed - 1); g.BuiltSkill = 50; g.State = GhostState.InProgress;
    }

    /// <summary>Goods the abstract build had consumed and no piece holds: made again into the pile (counted in and out, so the book still equals what stands built).</summary>
    internal void ToPileByGrowth(ConstructionSite s, ushort good, long grams)
    {
        if (grams <= 0) return;
        PileOf(s).Create(good, grams, CreateReason.Dematerialised);
        Book.MaterialisedG[good] = Book.MaterialisedG.GetValueOrDefault(good) + grams;
        Book.DematerialisedG[good] = Book.DematerialisedG.GetValueOrDefault(good) + grams;
    }

    /// <summary>
    /// A growth site is finished and its lot is added to the plan (spec 4.9: "its structure record is dropped, it equals the blueprint"): the built cells leave the book and the rooms (the
    /// chunk keeps their tiles, which the generated lot repeats), the roof cells are cleared from the chunk (a generated house draws its roof itself), what is left in the pile goes home.
    /// </summary>
    internal void HandOver(ConstructionSite s, Store home, long nowSec)
    {
        var keys = Book.Built.Values.Where(b => b.Site == s.Id).Select(b => b.SlotKey).ToList();
        var roofs = new List<(int X, int Y, int Z)>();
        foreach (var k in keys)
        {
            if (!RemoveBuilt(k, out var b)) continue;
            var c = Costs[b.Piece];
            foreach (var (good, grams) in MaterialIds(c)) if (good != ushort.MaxValue) Book.DematerialisedG[good] = Book.DematerialisedG.GetValueOrDefault(good) + grams;
            Rooms.Apply([new CellEdit(b.X, b.Y, b.Z, b.Slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, null)]);
            if (b.Slot == GhostSlot.Structure && c.Kind == PieceKind.Roof) roofs.Add((b.X, b.Y, b.Z));
        }
        foreach (var (x, y, z) in roofs) WriteTile(x, y, z);
        // the house is the lot's now: its materials are no longer construction's (counted out as the generated building's, never as goods)
        var pile = PileOf(s);
        foreach (var (good, qty) in pile.Rows().ToList()) Store.Move(pile, home, good, qty);
        if (pile.RowCount == 0) { Econ.OtherStores.Remove(s.Pile); Book.Placed.Remove(s.Pile); }
        foreach (var g in GhostsOf(s.Id).ToList()) CancelGhost(g);
        s.State = SiteState.Finished; s.Pause = PauseReason.None;
        s.Crew.Clear();
        _builtVersion++;
        SyncStructures();
        Event(ConstructionEventKind.Finished, nowSec, s.PileX, s.PileY, s.PileZ, s.Blueprint, s.Id);
        Publish(nowSec);
    }

    /// <summary>A growth site given up (its payer gone, or no progress for the give-up seasons): it stops with everything on it, half-built work stays.</summary>
    internal void GiveUp(ConstructionSite s, long nowSec)
    {
        s.State = SiteState.Abandoned; s.Pause = PauseReason.Owner;
        foreach (var c in s.Crew) c.Ghost = default;
        s.Crew.Clear();
        if (Econ.Ledger.TryFind(PurseKind.Chest, s.Escrow, out var e) && Econ.Ledger.Balance(e).Mites > 0 && PayerPurse(s) is { } p) Econ.Ledger.Transfer(e, p, Econ.Ledger.Balance(e), JobReasons.Refund);
        foreach (var c in Book.Contracts.Values)
            if (c.Site == s.Id && c.Live)
            {
                // the escrow goes back to the client; the builder keeps what he was paid
                var esc = Econ.Ledger.Open(PurseKind.Chest, c.Escrow);
                if (Econ.Ledger.Balance(esc).Mites > 0 && PurseOf(c.ClientKind, c.Client) is { } cp) Econ.Ledger.Transfer(esc, cp, Econ.Ledger.Balance(esc), JobReasons.Refund);
                c.State = ContractState.Cancelled; c.DoneSec = nowSec;
            }
        Publish(nowSec);
    }
}
