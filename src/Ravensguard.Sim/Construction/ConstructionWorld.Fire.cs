using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Construction;

// P6-B5: fire and burning take built pieces (fire-and-burning 4.2, 4.4): a burnt piece's materials go to the Burn sink (the goods that burn; a nail stays in the rubble), a piece pulled down by a
// fire hook is salvaged as a deconstruction would; either way the support rules decide what falls (the groan, then the fall).
public sealed partial class ConstructionWorld
{
    /// <summary>
    /// Takes the built piece in <paramref name="slot"/> of a cell down for the fire: false when none is built there. <paramref name="burnt"/>: every material of a burnable good class is destroyed
    /// as <see cref="DestroyReason.Burned"/> (made first as Dematerialised, so the construction's own audit and the mass ledger both stay exact), the rest lands in the site's rubble; not
    /// burnt (a hook pulled it down): the ordinary salvage share to the site's pile and the rest to rubble. A structure piece's removal starts the groan of whatever it held up.
    /// </summary>
    public bool DestroyForFire(int x, int y, int z, GhostSlot slot, bool burnt, IReadOnlySet<string> burnableClasses, long nowSec, EntityId cause, string why = "burnt")
    {
        var b = BuiltAt(x, y, z, slot);
        if (b == null) return false;
        var s = Site(b.Site);
        if (s == null) return false;
        var key = ConstructionIds.SlotKey(x, y, z, slot);
        if (!RemoveBuilt(key, out b)) return false;
        var c = Costs[b.Piece];
        var rubble = Econ.OpenStore(StoreKind.Open, s.Rubble);
        if (!Book.Placed.ContainsKey(s.Rubble)) Book.Placed[s.Rubble] = new PlacedStore(s.Rubble, s.PileX, s.PileY, s.PileZ);
        if (burnt)
        {
            foreach (var (good, grams) in c.Materials)
            {
                var gid = Econ.Goods.IdOf(good);
                rubble.Create(gid, grams, CreateReason.Dematerialised);
                Book.DematerialisedG[gid] = Book.DematerialisedG.GetValueOrDefault(gid) + grams;
                if (burnableClasses.Contains(Econ.Goods.Def(gid).Class)) rubble.Destroy(gid, grams, DestroyReason.Burned);
            }
        }
        else Unbuild(c, PileOf(s), rubble);
        Rooms.Apply([new CellEdit(x, y, z, slot == GhostSlot.Fixture ? CellLayer.Fixture : CellLayer.Structure, null)]);
        WriteTile(x, y, z);
        Event(ConstructionEventKind.Removed, nowSec, x, y, z, b.Piece, s.Id);
        _builtVersion++;
        if (slot == GhostSlot.Structure) StartGroan([ConstructionIds.CellKey(x, y, z)], nowSec, cause, why);
        foreach (var o in Book.Sites.Values) if (o.Open) Refresh(o);
        SyncStructures();
        Publish(nowSec);
        return true;
    }

    /// <summary>The fire's hook into construction: pieces this world built go through <see cref="DestroyForFire"/>; every other cell is the fire's own.</summary>
    public bool FireDestroy(int x, int y, int z, bool fixture, bool burnt, IReadOnlySet<string> burnableClasses, long nowSec, EntityId cause)
        => DestroyForFire(x, y, z, fixture ? GhostSlot.Fixture : GhostSlot.Structure, burnt, burnableClasses, nowSec, cause, burnt ? "burnt" : "pulled down");
}
